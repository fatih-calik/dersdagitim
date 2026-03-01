using DersDagitim.Models;
using DersDagitim.Persistence;
using Google.OrTools.Sat;

namespace DersDagitim.Services;

/// <summary>
/// Distribution parameters for scheduling engine
/// </summary>
public class DistributionParameters
{
    public OperationMode OperationMode { get; set; } = OperationMode.Rebuild;
    public int MaxTimeInSeconds { get; set; } = 120;
    public PlacementMode PlacementMode { get; set; } = PlacementMode.ClearAll;
    public int GapPenalty { get; set; } = 200000;
    public int MorningPenalty { get; set; } = 10;
    public int AdjacencyReward { get; set; } = 10000;
    public double PerturbationRate { get; set; } = 0.3;
    
    // Day Condensation
    public bool MinimizeWorkingDays { get; set; } = false;
}

public enum PlacementMode
{
    ClearAll,
    KeepPlaced,
    KeepLocked
}

/// <summary>
/// AI based scheduling engine for lesson distribution
/// </summary>
public class SchedulingEngine
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;
    private readonly TeacherRepository _teacherRepo = new();
    private readonly ClassRepository _classRepo = new();
    private readonly LessonRepository _lessonRepo = new();
    private readonly SchoolRepository _schoolRepo = new();
    
    /// <summary>
    /// Runs the scheduling solver
    /// </summary>
    public (bool success, string message) RunSolver(DistributionParameters parameters)
    {
        try
        {
            // Console.WriteLine("🚀 Starting AI Scheduling Engine...");
            
            // Load data
            var teachers = _teacherRepo.GetAll();
            var classes = _classRepo.GetAll();
            var lessons = _lessonRepo.GetAll();
            var schoolInfo = _schoolRepo.GetSchoolInfo();
            var blocks = LoadDistributionBlocks();
            
            // Load rooms for availability constraints
            var roomRepo = new OrtakMekanRepository();
            var rooms = roomRepo.GetAll();
            
            if (blocks.Count == 0)
            {
                return (false, "Dağıtım bloğu bulunamadı. Önce sınıf-ders atamaları yapınız.");
            }
            
            // Console.WriteLine($"📊 Loaded: {teachers.Count} teachers, {classes.Count} classes, {blocks.Count} blocks");
            
            // Create model
            var model = new CpModel();
            
            // Decision variables
            // x[blockId, day, hour] = 1 if block is placed at that slot
            var x = new Dictionary<(int blockId, int day, int hour), BoolVar>();
            
            foreach (var block in blocks)
            {
                // Skip if locked and we're keeping locked
                if (block.IsLocked && parameters.PlacementMode == PlacementMode.KeepLocked)
                {
                    continue;
                }
                
                // Skip if placed and we're keeping placed
                if (block.IsPlaced && parameters.PlacementMode == PlacementMode.KeepPlaced)
                {
                    continue;
                }
                
                for (int day = 1; day <= schoolInfo.Days; day++)
                {
                    for (int hour = 1; hour <= schoolInfo.DailyLessonCount - block.BlockDuration + 1; hour++)
                    {
                        x[(block.Id, day, hour)] = model.NewBoolVar($"x_{block.Id}_{day}_{hour}");
                    }
                }
            }
            
            // Console.WriteLine($"📐 Created {x.Count} decision variables");
            
            // Constraints
            
            // 1. Each block must be placed exactly once
            foreach (var block in blocks)
            {
                if (block.IsLocked && parameters.PlacementMode == PlacementMode.KeepLocked) continue;
                if (block.IsPlaced && parameters.PlacementMode == PlacementMode.KeepPlaced) continue;
                
                var blockVars = x.Where(kv => kv.Key.blockId == block.Id).Select(kv => kv.Value).ToList();
                if (blockVars.Count > 0)
                {
                    model.Add(LinearExpr.Sum(blockVars) == 1);
                }
            }
            
            // 2. No teacher conflicts (teacher can only be in one place at a time)
            foreach (var teacher in teachers)
            {
                for (int day = 1; day <= schoolInfo.Days; day++)
                {
                    for (int hour = 1; hour <= schoolInfo.DailyLessonCount; hour++)
                    {
                        var slot = new TimeSlot(day, hour);
                        
                        // Check if teacher has constraint (closed slot)
                        if (teacher.Constraints.TryGetValue(slot, out var state) && state == SlotState.Closed)
                        {
                            // No block can be placed here for this teacher
                            var teacherBlocks = blocks.Where(b => b.TeacherIds.Contains(teacher.Id));
                            foreach (var block in teacherBlocks)
                            {
                                // Block cannot start at positions that would cover this hour
                                for (int startHour = Math.Max(1, hour - block.BlockDuration + 1); startHour <= hour; startHour++)
                                {
                                    if (x.TryGetValue((block.Id, day, startHour), out var v))
                                    {
                                        model.Add(v == 0);
                                    }
                                }
                            }
                            }
                        }

                        // --- FIX: Check if teacher is already occupied by a FIXED block here --
                        bool isOccupiedByFixed = false;
                        foreach(var fixedBlock in blocks)
                        {
                            // Is this block fixed?
                            bool isFixed = (fixedBlock.IsLocked && parameters.PlacementMode == PlacementMode.KeepLocked) ||
                                           (fixedBlock.IsPlaced && parameters.PlacementMode == PlacementMode.KeepPlaced);
                            
                            if (isFixed && fixedBlock.TeacherIds.Contains(teacher.Id))
                            {
                                // Check overlap
                                // Block occupies [Day, Hour] to [Day, Hour + Duration - 1]
                                if (fixedBlock.Day == day && 
                                    hour >= fixedBlock.Hour && 
                                    hour < fixedBlock.Hour + fixedBlock.BlockDuration)
                                {
                                    isOccupiedByFixed = true;
                                    break;
                                }
                            }
                        }

                        if (isOccupiedByFixed)
                        {
                            // Hard Constraint: CANNOT place any new block here for this teacher
                            var teacherBlocks = blocks.Where(b => b.TeacherIds.Contains(teacher.Id));
                            foreach (var block in teacherBlocks)
                            {
                                // Skip if it's the fixed block itself (it won't have a variable anyway, but just in case)
                                bool isFixed = (block.IsLocked && parameters.PlacementMode == PlacementMode.KeepLocked) ||
                                               (block.IsPlaced && parameters.PlacementMode == PlacementMode.KeepPlaced);
                                if (isFixed) continue;

                                for (int startHour = Math.Max(1, hour - block.BlockDuration + 1); startHour <= hour; startHour++)
                                {
                                    if (x.TryGetValue((block.Id, day, startHour), out var v))
                                    {
                                        model.Add(v == 0);
                                    }
                                }
                            }
                            // Since it's occupied, we don't need to check conflict sum (it's 0 forced above)
                            continue; 
                        }
                        // --------------------------------------------------------------------
                        
                        // Only one block per teacher per slot
                        var conflictVars = new List<BoolVar>();
                        foreach (var block in blocks.Where(b => b.TeacherIds.Contains(teacher.Id)))
                        {
                            // All start positions that would occupy this slot
                            for (int startHour = Math.Max(1, hour - block.BlockDuration + 1); startHour <= hour; startHour++)
                            {
                                if (x.TryGetValue((block.Id, day, startHour), out var v))
                                {
                                    conflictVars.Add(v);
                                }
                            }
                        }
                        
                        if (conflictVars.Count > 1)
                        {
                            model.Add(LinearExpr.Sum(conflictVars) <= 1);
                        }
                    }
                }
            }
            
            // 3. No class conflicts (class can only have one lesson at a time)
            foreach (var schoolClass in classes)
            {
                for (int day = 1; day <= schoolInfo.Days; day++)
                {
                    for (int hour = 1; hour <= schoolInfo.DailyLessonCount; hour++)
                    {
                        var slot = new TimeSlot(day, hour);
                        
                        // Check if class has constraint (closed slot)
                        if (schoolClass.Constraints.TryGetValue(slot, out var state) && state == SlotState.Closed)
                        {
                            var classBlocks = blocks.Where(b => b.ClassId == schoolClass.Id);
                            foreach (var block in classBlocks)
                            {
                                for (int startHour = Math.Max(1, hour - block.BlockDuration + 1); startHour <= hour; startHour++)
                                {
                                    if (x.TryGetValue((block.Id, day, startHour), out var v))
                                    {
                                        model.Add(v == 0);
                                    }
                                }
                            }
                        }

                         // --- FIX: Check if class is already occupied by a FIXED block here --
                        bool isOccupiedByFixed = false;
                        foreach(var fixedBlock in blocks)
                        {
                            bool isFixed = (fixedBlock.IsLocked && parameters.PlacementMode == PlacementMode.KeepLocked) ||
                                           (fixedBlock.IsPlaced && parameters.PlacementMode == PlacementMode.KeepPlaced);
                            
                            if (isFixed && fixedBlock.ClassId == schoolClass.Id)
                            {
                                if (fixedBlock.Day == day && 
                                    hour >= fixedBlock.Hour && 
                                    hour < fixedBlock.Hour + fixedBlock.BlockDuration)
                                {
                                    isOccupiedByFixed = true;
                                    break;
                                }
                            }
                        }

                        if (isOccupiedByFixed)
                        {
                            var classBlocks = blocks.Where(b => b.ClassId == schoolClass.Id);
                            foreach (var block in classBlocks)
                            {
                                bool isFixed = (block.IsLocked && parameters.PlacementMode == PlacementMode.KeepLocked) ||
                                               (block.IsPlaced && parameters.PlacementMode == PlacementMode.KeepPlaced);
                                if (isFixed) continue;

                                for (int startHour = Math.Max(1, hour - block.BlockDuration + 1); startHour <= hour; startHour++)
                                {
                                    if (x.TryGetValue((block.Id, day, startHour), out var v))
                                    {
                                        model.Add(v == 0);
                                    }
                                }
                            }
                            continue;
                        }
                        // --------------------------------------------------------------------
                        
                        // Only one block per class per slot
                        var conflictVars = new List<BoolVar>();
                        foreach (var block in blocks.Where(b => b.ClassId == schoolClass.Id))
                        {
                            for (int startHour = Math.Max(1, hour - block.BlockDuration + 1); startHour <= hour; startHour++)
                            {
                                if (x.TryGetValue((block.Id, day, startHour), out var v))
                                {
                                    conflictVars.Add(v);
                                }
                            }
                        }
                        
                        if (conflictVars.Count > 1)
                        {
                            model.Add(LinearExpr.Sum(conflictVars) <= 1);
                        }
                    }
                }
            }
            
            // 4. No shared room conflicts
            var roomUsage = new Dictionary<(int mid, int day, int hour), List<BoolVar>>();
            
            foreach(var block in blocks)
            {
                 var mIds = new List<int>();
                 if(block.OrtakMekan1Id > 0) mIds.Add(block.OrtakMekan1Id);
                 if(block.OrtakMekan2Id > 0) mIds.Add(block.OrtakMekan2Id);
                 if(block.OrtakMekan3Id > 0) mIds.Add(block.OrtakMekan3Id);
                 if(block.OrtakMekan4Id > 0) mIds.Add(block.OrtakMekan4Id);
                 if(block.OrtakMekan5Id > 0) mIds.Add(block.OrtakMekan5Id);
                 
                 foreach(var mid in mIds)
                 {
                     for (int day = 1; day <= schoolInfo.Days; day++)
                     {
                         for (int hour = 1; hour <= schoolInfo.DailyLessonCount; hour++)
                         {
                             // Find variables that cover this slot
                             // Block covers [hour, hour + duration - 1]
                             // So if block starts at s, it covers s..s+dur-1.
                             // We check if 'hour' falls into that range.
                             // Start range: max(1, hour - duration + 1) to hour
                             
                             for (int startHour = Math.Max(1, hour - block.BlockDuration + 1); startHour <= hour; startHour++)
                             {
                                 if (x.TryGetValue((block.Id, day, startHour), out var v))
                                 {
                                     var key = (mid, day, hour);
                                     if (!roomUsage.ContainsKey(key)) roomUsage[key] = new List<BoolVar>();
                                     roomUsage[key].Add(v);
                                 }
                             }
                         }
                     }
                 }
            }
            
                if (kvp.Value.Count > 1)
                {
                    model.Add(LinearExpr.Sum(kvp.Value) <= 1);
                }
            }

            // 5. No shared room availability conflicts (If room is closed)
            foreach (var room in rooms)
            {
                for (int day = 1; day <= schoolInfo.Days; day++)
                {
                    for (int hour = 1; hour <= schoolInfo.DailyLessonCount; hour++)
                    {
                        var slot = new TimeSlot(day, hour);
                        if (room.Constraints.TryGetValue(slot, out var state) && state == SlotState.Closed)
                        {
                            // Find all blocks that could occupy this room at this time
                            var roomBlocks = blocks.Where(b => b.GetOrtakMekanIds().Contains(room.Id));
                            foreach (var block in roomBlocks)
                            {
                                for (int startHour = Math.Max(1, hour - block.BlockDuration + 1); startHour <= hour; startHour++)
                                {
                                    if (x.TryGetValue((block.Id, day, startHour), out var v))
                                    {
                                        model.Add(v == 0);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            // Objective: Minimize gaps, prefer morning for certain lessons
            var objectives = new List<LinearExpr>();
            
            // Morning preference penalty
            foreach (var block in blocks)
            {
                if (block.MorningPriority > 0)
                {
                    for (int day = 1; day <= schoolInfo.Days; day++)
                    {
                        for (int hour = 1; hour <= schoolInfo.DailyLessonCount - block.BlockDuration + 1; hour++)
                        {
                            if (x.TryGetValue((block.Id, day, hour), out var v))
                            {
                                // Later hours get higher penalty
                                int penalty = (int)(hour * block.MorningPriority * parameters.MorningPenalty);
                                objectives.Add(v * penalty);
                            }
                        }
                    }
                }
            }
            
                }
            }
            
            // Minimize Working Days (Day Condensation)
            if (parameters.MinimizeWorkingDays)
            {
                // Group blocks by main teacher
                var teacherBlocks = blocks.Where(b => !b.IsLocked && !b.IsPlaced).GroupBy(b => b.TeacherIds.FirstOrDefault());
                
                foreach (var group in teacherBlocks)
                {
                    int tid = group.Key;
                    if (tid == 0) continue;
                    
                    for (int day = 1; day <= schoolInfo.Days; day++)
                    {
                        var isWorkingDay = model.NewBoolVar($"work_{tid}_{day}");
                        var dayVars = new List<BoolVar>();
                        
                        foreach (var block in group)
                        {
                            // Collect all placement vars for this teacher on this day
                            for (int hour = 1; hour <= schoolInfo.DailyLessonCount - block.BlockDuration + 1; hour++)
                            {
                                if (x.TryGetValue((block.Id, day, hour), out var v))
                                {
                                    dayVars.Add(v);
                                }
                            }
                        }
                        
                        if (dayVars.Any())
                        {
                            // If any lesson is placed on this day, working var MUST be true
                            model.Add(LinearExpr.Sum(dayVars) > 0).OnlyEnforceIf(isWorkingDay);
                            model.Add(LinearExpr.Sum(dayVars) == 0).OnlyEnforceIf(isWorkingDay.Not());
                            
                            // High penalty for each working day to force condensation
                            // 500,000 points per day
                            objectives.Add(isWorkingDay * 500000); 
                        }
                    }
                }
            }
            
            if (objectives.Count > 0)
            {
                model.Minimize(LinearExpr.Sum(objectives));
            }
            
            // Console.WriteLine("🔧 Constraints and objectives configured");
            
            // Solve
            var solver = new CpSolver();
            solver.StringParameters = $"max_time_in_seconds:{parameters.MaxTimeInSeconds}";
            
            // Console.WriteLine($"⏱️ Solving with max time: {parameters.MaxTimeInSeconds}s...");
            var status = solver.Solve(model);
            
            if (status == CpSolverStatus.Optimal || status == CpSolverStatus.Feasible)
            {
                // Console.WriteLine($"✅ Solution found! Status: {status}");
                
                // Extract solution
                int placedCount = 0;
                foreach (var block in blocks)
                {
                    if (block.IsLocked && parameters.PlacementMode == PlacementMode.KeepLocked) continue;
                    if (block.IsPlaced && parameters.PlacementMode == PlacementMode.KeepPlaced) continue;
                    
                    for (int day = 1; day <= schoolInfo.Days; day++)
                    {
                        for (int hour = 1; hour <= schoolInfo.DailyLessonCount - block.BlockDuration + 1; hour++)
                        {
                            if (x.TryGetValue((block.Id, day, hour), out var v) && solver.Value(v) == 1)
                            {
                                // Update block placement in database
                                _db.Execute($"""
                                    UPDATE dagitim_bloklari 
                                    SET gun = {day}, saat = {hour}, yerlesim_tipi = 'otomatik'
                                    WHERE id = {block.Id}
                                """);
                                """);
                                
                                // NEW: Use the shared repository to ensure consistent syncing (Append logic, multi-teacher, multi-room)
                                new DistributionRepository().PlaceBlock(block, "otomatik");
                                placedCount++;
                            }
                        }
                    }
                }
                
                // CRITICAL FIX: Also update flat tables for LOCKED blocks, because they might have been cleared visuals
                var distRepo = new DistributionRepository();
                foreach(var block in blocks)
                {
                     if (block.IsLocked && block.Day > 0 && block.Hour > 0)
                     {
                         distRepo.PlaceBlock(block, block.PlacementType ?? "manuel");
                     }
                }
                
                // Update teacher weekly hours
                _teacherRepo.SyncAllTeacherHours();
                
                return (true, $"Dağıtım tamamlandı. {placedCount} blok yerleştirildi.");
            }
            else
            {
                // Console.WriteLine($"❌ No solution found. Status: {status}");
                return (false, $"Çözüm bulunamadı. Durum: {status}. Kısıtları gevşetmeyi deneyin.");
            }
        }
        catch (Exception ex)
        {
            // Console.WriteLine($"❌ Error in solver: {ex.Message}");
            return (false, $"Hata: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Loads distribution blocks from database
    /// </summary>
    private List<DistributionBlock> LoadDistributionBlocks()
    {
        var results = _db.Query("SELECT * FROM dagitim_bloklari");
        return results.Select(row => new DistributionBlock
        {
            Id = DatabaseManager.GetInt(row, "id"),
            ClassLessonId = DatabaseManager.GetInt(row, "sinif_ders_id"),
            LessonCode = DatabaseManager.GetString(row, "ders_kodu"),
            ClassId = DatabaseManager.GetInt(row, "sinif_id"),
            BlockDuration = DatabaseManager.GetInt(row, "blok_suresi", 1),
            Teacher1Id = DatabaseManager.GetInt(row, "ogretmen_1_id"),
            Teacher2Id = DatabaseManager.GetInt(row, "ogretmen_2_id"),
            Teacher3Id = DatabaseManager.GetInt(row, "ogretmen_3_id"),
            Teacher4Id = DatabaseManager.GetInt(row, "ogretmen_4_id"),
            Teacher5Id = DatabaseManager.GetInt(row, "ogretmen_5_id"),
            OrtakMekan1Id = DatabaseManager.GetInt(row, "ortak_mekan_1_id"),
            OrtakMekan2Id = DatabaseManager.GetInt(row, "ortak_mekan_2_id"),
            OrtakMekan3Id = DatabaseManager.GetInt(row, "ortak_mekan_3_id"),
            OrtakMekan4Id = DatabaseManager.GetInt(row, "ortak_mekan_4_id"),
            OrtakMekan5Id = DatabaseManager.GetInt(row, "ortak_mekan_5_id"),
            Day = DatabaseManager.GetInt(row, "gun"),
            Hour = DatabaseManager.GetInt(row, "saat"),
            PlacementType = DatabaseManager.GetString(row, "yerlesim_tipi", "otomatik"),
            IsLocked = DatabaseManager.GetInt(row, "kilitli") == 1,
            IsManual = DatabaseManager.GetInt(row, "manuel") == 1,
            MorningPriority = DatabaseManager.GetDouble(row, "sabah_onceligi")
        }).ToList();
    }
    

    
    /// <summary>
    /// Resets all distributions
    /// </summary>
    public void ResetAllDistributions(bool keepManual = false)
    {
        if (keepManual)
        {
            _db.Execute("UPDATE dagitim_bloklari SET gun = 0, saat = 0 WHERE manuel = 0");
        }
        else
        {
            _db.Execute("UPDATE dagitim_bloklari SET gun = 0, saat = 0");
        }
        
        // Clear flat tables
        var updates = new List<string>();
        for (int day = 1; day <= 7; day++)
        {
            for (int hour = 1; hour <= 12; hour++)
            {
                updates.Add($"d_{day}_{hour} = CASE WHEN d_{day}_{hour} = 'KAPALI' THEN 'KAPALI' ELSE '' END");
            }
        }
        
        var setClause = string.Join(", ", updates);
        _db.Execute($"UPDATE ogretmen SET {setClause}");
        _db.Execute($"UPDATE sinif SET {setClause}");
        _db.Execute($"UPDATE ortak_mekan SET {setClause}");
    }
}

