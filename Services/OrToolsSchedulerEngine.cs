using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.OrTools.Sat;
using DersDagitim.Models;
using DersDagitim.Persistence;

namespace DersDagitim.Services
{
    public class OrToolsSchedulerEngine
    {
        private static OrToolsSchedulerEngine _instance;
        public static OrToolsSchedulerEngine Instance => _instance ??= new OrToolsSchedulerEngine();

        public event Action<string> OnLog;

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }

        public async Task<bool> Run(DistributionParameters paramsData, Action<string> onStatusUpdate)
        {
            try
            {
                App.LogToDisk("OrToolsSchedulerEngine.Run basladi.");
                onStatusUpdate?.Invoke("V3 (AI) Veriler Yükleniyor...");
                
                var repo = new DistributionRepository();
                var teacherRepo = new TeacherRepository();
                var classRepo = new ClassRepository();
                var schoolRepo = new SchoolRepository();
                var roomRepo = new OrtakMekanRepository();
                
                var blocks = repo.GetAllBlocks();
                var teachers = teacherRepo.GetAll().ToDictionary(t => t.Id);
                var classes = classRepo.GetAll().ToDictionary(c => c.Id);
                var rooms = roomRepo.GetAll().ToDictionary(r => r.Id);
                var schoolInfo = schoolRepo.GetSchoolInfo();

                if (blocks == null || blocks.Count == 0)
                {
                    onStatusUpdate?.Invoke("[UYARI] Dağıtılacak blok bulunamadı.");
                    return true;
                }

                // 1. Reset logic (Mirroring Python)
                if (paramsData.OperationMode == OperationMode.Rebuild)
                {
                    onStatusUpdate?.Invoke("Mevcut Dağıtım Temizleniyor...");
                    repo.ResetAllDistributions(keepManual: paramsData.PlacementMode == PlacementMode.KeepLocked);
                    blocks = repo.GetAllBlocks(); // Refresh
                }
                
                ScheduleState state = new ScheduleState(blocks, teachers, classes, rooms, schoolInfo, paramsData.MaxDays, paramsData.MaxHours);

                // --- 2. KAPASİTE ANALİZİ (Diagnostics) ---
                onStatusUpdate?.Invoke("Kapasite Analiz Ediliyor...");
                if (!RunDiagnostics(state, onStatusUpdate)) return false;

                App.LogToDisk("OrToolsSchedulerEngine: Model olusturuluyor...");
                onStatusUpdate?.Invoke("V3 Model Oluşturuluyor...");
                
                CpModel model = new CpModel();
                var allBlocks = state.GetAllBlocks(); 
                
                // --- 3. DEĞİŞKENLER (Modified for Unified Lessons) ---
                var x = new Dictionary<int, List<(int d, int h, BoolVar bv)>>();
                var blockDayVars = new Dictionary<int, Dictionary<int, BoolVar>>(); 
                var processedBlocks = new HashSet<int>();

                int maxDays = paramsData.MaxDays;
                int maxHours = paramsData.MaxHours;

                // 3a. Handle Unified Groups (Kardeş Dersler)
                var kardesGroups = allBlocks.Where(b => b.KardesId > 0).GroupBy(b => b.KardesId).ToList();
                foreach (var grp in kardesGroups)
                {
                    int gid = grp.Key;
                    var grpBlocks = grp.ToList();
                    var firstB = grpBlocks[0]; // Assume all in group have same duration
                    
                    // Check if any block is already locked (placed manually)
                    var lockedB = grpBlocks.FirstOrDefault(b => b.IsLocked && b.Day > 0);
                    
                    if (lockedB != null)
                    {
                        int fixedD = lockedB.Day;
                        int fixedH = lockedB.Hour;
                        
                        var v = model.NewBoolVar($"grp_{gid}_fixed"); // Single variable for the whole group
                        model.Add(v == 1);
                        var dv = model.NewBoolVar($"grpDay_{gid}_{fixedD}");
                        model.Add(dv == 1);
                        
                        var sharedX = new List<(int d, int h, BoolVar bv)> { (fixedD, fixedH, v) };
                        var sharedDayVars = new Dictionary<int, BoolVar> { { fixedD, dv } };
                        
                        foreach (var b in grpBlocks)
                        {
                            x[b.Id] = sharedX;
                            blockDayVars[b.Id] = sharedDayVars;
                            processedBlocks.Add(b.Id);
                        }
                    }
                    else
                    {
                        // Find common valid slots where ALL blocks in group can fit
                        var commonSlots = new List<(int d, int h, BoolVar bv)>();
                        var commonDayVars = new Dictionary<int, BoolVar>();
                        
                        for (int d = 1; d <= maxDays; d++)
                        {
                            for (int h = 1; h <= maxHours - firstB.BlockDuration + 1; h++)
                            {
                                bool allOk = true;
                                foreach (var b in grpBlocks)
                                {
                                    if (!IsStaticAvailable(state, b, d, h)) { allOk = false; break; }
                                }
                                
                                if (allOk)
                                {
                                    var v = model.NewBoolVar($"grp_{gid}_{d}_{h}");
                                    commonSlots.Add((d, h, v));
                                    
                                    if (!commonDayVars.ContainsKey(d))
                                        commonDayVars[d] = model.NewBoolVar($"grpDay_{gid}_{d}");
                                    
                                    model.AddImplication(v, commonDayVars[d]);
                                }
                            }
                        }
                        
                        if (commonSlots.Count == 0)
                        {
                            var report = new StringBuilder();
                            report.AppendLine($"[HATA] Kardeş Ders Grubu (ID:{gid}) için ortak müsait zaman dilimi bulunamadı!");
                            
                            foreach(var b in grpBlocks)
                            {
                                int slots = 0;
                                for (int d = 1; d <= maxDays; d++)
                                    for (int h = 1; h <= maxHours - b.BlockDuration + 1; h++)
                                        if (IsStaticAvailable(state, b, d, h)) slots++;
                                
                                string cName = classes.ContainsKey(b.ClassId) ? classes[b.ClassId].Name : b.ClassId.ToString();
                                report.AppendLine($"  - {cName} ({b.LessonCode}): Tek başına {slots} uygun slotu var.");
                            }
                            report.AppendLine("Bu sınıfların ve öğretmenlerin kapalı saatleri (kısıtları) üst üste geldiğinde hiç boş yer kalmıyor.");
                            
                            onStatusUpdate?.Invoke(report.ToString());
                            return false;
                        }

                        foreach (var b in grpBlocks)
                        {
                            x[b.Id] = commonSlots;
                            blockDayVars[b.Id] = commonDayVars;
                            processedBlocks.Add(b.Id);
                        }
                        model.Add(LinearExpr.Sum(commonSlots.Select(s => s.bv)) == 1);
                    }
                }

                // 3b. Standard Independent Blocks
                foreach (var b in allBlocks)
                {
                    if (processedBlocks.Contains(b.Id)) continue;
                    
                    x[b.Id] = new List<(int d, int h, BoolVar bv)>();
                    blockDayVars[b.Id] = new Dictionary<int, BoolVar>();

                    if (b.IsLocked && b.Day != 0)
                    {
                        var v = model.NewBoolVar($"x_{b.Id}_fixed");
                        model.Add(v == 1);
                        x[b.Id].Add((b.Day, b.Hour, v));
                        
                        var dv = model.NewBoolVar($"d_{b.Id}_{b.Day}");
                        model.Add(dv == 1);
                        blockDayVars[b.Id][b.Day] = dv;
                    }
                    else
                    {
                        for (int d = 1; d <= maxDays; d++)
                        {
                            if (!blockDayVars[b.Id].ContainsKey(d))
                                blockDayVars[b.Id][d] = model.NewBoolVar($"day_{b.Id}_{d}");
                            
                            for (int h = 1; h <= maxHours - b.BlockDuration + 1; h++)
                            {
                                if (IsStaticAvailable(state, b, d, h))
                                {
                                    var v = model.NewBoolVar($"x_{b.Id}_{d}_{h}");
                                    x[b.Id].Add((d, h, v));
                                    model.AddImplication(v, blockDayVars[b.Id][d]);
                                }
                            }
                        }
                        
                        var possibleVars = x[b.Id].Select(v => v.bv).ToList();
                        if (possibleVars.Count == 0)
                        {
                            string msg = $"[HATA] {b.LessonCode} ({classes[b.ClassId].Name}) için müsait yer yok!";
                            onStatusUpdate?.Invoke(msg);
                            return false; 
                        }
                        model.Add(LinearExpr.Sum(possibleVars) == 1);
                    }
                }

                // --- 4. ÇAKIŞMA KISITLARI (Hard) ---
                onStatusUpdate?.Invoke("Çakışma Kontrolleri Ekleniyor...");
                
                var classSlots = new Dictionary<(int cid, int d, int h), List<BoolVar>>();
                var teacherSlots = new Dictionary<(int tid, int d, int h), List<BoolVar>>();
                var roomSlots = new Dictionary<(int rid, int d, int h), List<BoolVar>>();

                foreach (var bid in x.Keys)
                {
                    var block = allBlocks.First(b => b.Id == bid);
                    foreach (var move in x[bid])
                    {
                        for (int i = 0; i < block.BlockDuration; i++)
                        {
                            int currH = move.h + i;
                            
                            // Class (Each class can only have one block at a time)
                            var cKey = (block.ClassId, move.d, currH);
                            if (!classSlots.ContainsKey(cKey)) classSlots[cKey] = new List<BoolVar>();
                            if (!classSlots[cKey].Contains(move.bv))
                                classSlots[cKey].Add(move.bv);
                            
                            // Teachers (Each teacher can only have one 'unit' of work at a time)
                            // If blocks share a KardesId, they use the same BoolVar, effectively counting as one.
                            foreach (var tid in block.TeacherIds)
                            {
                                var tKey = (tid, move.d, currH);
                                if (!teacherSlots.ContainsKey(tKey)) teacherSlots[tKey] = new List<BoolVar>();
                                if (!teacherSlots[tKey].Contains(move.bv))
                                    teacherSlots[tKey].Add(move.bv);
                            }
                            
                            // Rooms
                            foreach (var rid in block.GetOrtakMekanIds())
                            {
                                var rKey = (rid, move.d, currH);
                                if (!roomSlots.ContainsKey(rKey)) roomSlots[rKey] = new List<BoolVar>();
                                if (!roomSlots[rKey].Contains(move.bv))
                                    roomSlots[rKey].Add(move.bv);
                            }
                        }
                    }
                }

                foreach (var list in classSlots.Values) if (list.Count > 1) model.Add(LinearExpr.Sum(list) <= 1);
                foreach (var list in teacherSlots.Values) if (list.Count > 1) model.Add(LinearExpr.Sum(list) <= 1);
                foreach (var list in roomSlots.Values) if (list.Count > 1) model.Add(LinearExpr.Sum(list) <= 1);

                // --- 5. AYNI DERS AYNI GÜNE GELMEZ (Hard) ---
                var lessonGroups = allBlocks.GroupBy(b => new { b.ClassId, b.LessonCode });
                foreach (var grp in lessonGroups)
                {
                    var blocksInGrp = grp.ToList();
                    if (blocksInGrp.Count <= 1) continue;
                    
                    for (int d = 1; d <= maxDays; d++)
                    {
                        var varsInDay = new List<BoolVar>();
                        foreach (var b in blocksInGrp)
                        {
                            if (blockDayVars[b.Id].TryGetValue(d, out var dv)) varsInDay.Add(dv);
                        }
                        if (varsInDay.Count > 1) model.Add(LinearExpr.Sum(varsInDay) <= 1);
                    }
                }

                // --- 6. OBJECTIVE (Max 2 Hour Gap Penalty) ---
                var objTerms = new List<LinearExpr>();
                foreach (var tid in teachers.Keys)
                {
                    for (int d = 1; d <= maxDays; d++)
                    {
                        var dayOccupancy = new BoolVar[maxHours + 1];
                        for (int h = 1; h <= maxHours; h++)
                        {
                            dayOccupancy[h] = model.NewBoolVar($"busy_{tid}_{d}_{h}");
                            var slotsAtH = teacherSlots.ContainsKey((tid, d, h)) ? teacherSlots[(tid, d, h)] : new List<BoolVar>();
                            if (slotsAtH.Count > 0) model.Add(dayOccupancy[h] == LinearExpr.Sum(slotsAtH));
                            else model.Add(dayOccupancy[h] == 0);
                        }

                        // --- NEW GAP LOGIC (Min-Max Span) ---
                        
                        // 1. Calculate Daily Load (Number of Lessons)
                        var dailyLoad = model.NewIntVar(0, maxHours, $"load_{tid}_{d}");
                        model.Add(dailyLoad == LinearExpr.Sum(dayOccupancy.Skip(1))); // dayOccupancy[0] is unused

                        // 2. Define First and Last Hour variables
                        var firstHour = model.NewIntVar(0, maxHours + 1, $"fh_{tid}_{d}");
                        var lastHour = model.NewIntVar(0, maxHours + 1, $"lh_{tid}_{d}");

                        var firstHourCandidates = new List<IntVar>();
                        var lastHourCandidates = new List<IntVar>();

                        // We need to map boolean occupancy to hour indices
                        for (int h = 1; h <= maxHours; h++)
                        {
                            // For First Hour: If occupied, value is h. If not, value is maxHours+1 (ignored by Min)
                            var valF = model.NewIntVar(0, maxHours + 1, $"vF_{tid}_{d}_{h}");
                            model.Add(valF == h).OnlyEnforceIf(dayOccupancy[h]);
                            model.Add(valF == maxHours + 1).OnlyEnforceIf(dayOccupancy[h].Not());
                            firstHourCandidates.Add(valF);

                            // For Last Hour: If occupied, value is h. If not, value is 0 (ignored by Max)
                            var valL = model.NewIntVar(0, maxHours + 1, $"vL_{tid}_{d}_{h}");
                            model.Add(valL == h).OnlyEnforceIf(dayOccupancy[h]);
                            model.Add(valL == 0).OnlyEnforceIf(dayOccupancy[h].Not());
                            lastHourCandidates.Add(valL);
                        }

                        model.AddMinEquality(firstHour, firstHourCandidates);
                        model.AddMaxEquality(lastHour, lastHourCandidates);

                        // 3. Calculate Gap
                        // Gap = (Last - First + 1) - Load
                        // Only valid if Load > 0
                        var isWorking = model.NewBoolVar($"work_{tid}_{d}");
                        model.Add(dailyLoad > 0).OnlyEnforceIf(isWorking);
                        model.Add(dailyLoad == 0).OnlyEnforceIf(isWorking.Not());

                        var gapInfo = model.NewIntVar(0, maxHours, $"gap_{tid}_{d}");
                        model.Add(gapInfo == lastHour - firstHour + 1 - dailyLoad).OnlyEnforceIf(isWorking);
                        model.Add(gapInfo == 0).OnlyEnforceIf(isWorking.Not());

                        // 4. Penalties
                        // A. Linear Penalty (Every empty hour hurts)
                        objTerms.Add(gapInfo * paramsData.V3GapPenalty); 

                        // B. Quadratic/Heavy penalty for large gaps (>2 hours) to avoid "Swiss Cheese"
                        var isLargeGap = model.NewBoolVar($"bg_{tid}_{d}");
                        model.Add(gapInfo > 2).OnlyEnforceIf(isLargeGap);
                        objTerms.Add(isLargeGap * 10000); // Huge penalty for > 2 hours gap
                    }
                }
                if (objTerms.Count > 0) model.Minimize(LinearExpr.Sum(objTerms));

                // --- 7. ÇÖZÜM ---
                App.LogToDisk("OrToolsSchedulerEngine: Solve baslatiliyor...");
                onStatusUpdate?.Invoke($"V3 Planlayıcı Başlatıldı ({paramsData.MaxTimeInSeconds}sn)...");

                CpSolver solver = new CpSolver();
                solver.StringParameters = $"max_time_in_seconds:{paramsData.MaxTimeInSeconds}, num_search_workers:8";
                
                CpSolverStatus status = await System.Threading.Tasks.Task.Run(() => solver.Solve(model));
                
                if (status == CpSolverStatus.Optimal || status == CpSolverStatus.Feasible)
                {
                    onStatusUpdate?.Invoke($"Dağıtım Başarılı ({status}), Kaydediliyor...");
                    int placedCount = 0;
                    foreach (var b in allBlocks)
                    {
                        if (b.IsLocked && b.Day > 0) continue;
                        foreach (var move in x[b.Id])
                        {
                            if (solver.BooleanValue(move.bv))
                            {
                                b.Day = move.d;
                                b.Hour = move.h;
                                b.PlacementType = "v3_ortools";
                                placedCount++;
                                break;
                            }
                        }
                    }

                    repo.SaveDistributionAtomically(allBlocks, paramsData.PlacementMode == PlacementMode.KeepLocked);
                    repo.SyncSignalTables();
                    
                    teacherRepo.SyncAllTeacherHours();
                    
                    onStatusUpdate?.Invoke("V3 (AI) Dağıtım Tamamlandı.");
                    return true;
                }
                
                onStatusUpdate?.Invoke($"[HATA] Dağıtım Yapılamadı: {status}. Analiz başlatılıyor...");
                
                // --- QUICK BOTTLENECK REPORT ---
                var diagnostics = new StringBuilder();
                diagnostics.AppendLine("--- Sıkışıklık Analizi ---");
                
                foreach (var tid in teachers.Keys)
                {
                    int load = allBlocks.Where(b => b.TeacherIds.Contains(tid)).Sum(b => b.BlockDuration);
                    int openSlots = 0;
                    for (int d = 1; d <= maxDays; d++)
                        for (int h = 1; h <= maxHours; h++)
                            if (state.IsTeacherOpen(tid, d, h)) openSlots++;
                    
                    double occupancy = openSlots > 0 ? (double)load / openSlots * 100 : 100;
                    if (occupancy > 90) diagnostics.AppendLine($"[!] {teachers[tid].Name}: %{occupancy:F0} Doluluk ({load}/{openSlots} saat).");
                }
                
                foreach (var cid in classes.Keys)
                {
                    int load = allBlocks.Where(b => b.ClassId == cid).Sum(b => b.BlockDuration);
                    int openSlots = 0;
                    for (int d = 1; d <= maxDays; d++)
                        for (int h = 1; h <= maxHours; h++)
                            if (state.IsClassOpen(cid, d, h)) openSlots++;
                    
                    double occupancy = openSlots > 0 ? (double)load / openSlots * 100 : 100;
                    if (occupancy > 95) diagnostics.AppendLine($"[!] {classes[cid].Name}: %{occupancy:F0} Doluluk ({load}/{openSlots} saat).");
                }
                
                if (diagnostics.Length > 30) onStatusUpdate?.Invoke(diagnostics.ToString());

                // --- DETAILED RELAXED SOLVE ---
                await RunRelaxedDiagnostic(state, x, blockDayVars, teacherSlots, classSlots, roomSlots, onStatusUpdate);

                return false;
            }
            catch (Exception ex)
            {
                onStatusUpdate?.Invoke($"[KRİTİK HATA] {ex.Message}");
                return false;
            }
        }

        private bool RunDiagnostics(ScheduleState state, Action<string> log)
        {
            var blocks = state.GetAllBlocks();
            
            // Teacher Loads
            var tLoads = new Dictionary<int, int>();
            foreach (var b in blocks) foreach (var tid in b.TeacherIds) 
                tLoads[tid] = tLoads.GetValueOrDefault(tid) + b.BlockDuration;

            foreach (var kvp in tLoads)
            {
                int tid = kvp.Key; int load = kvp.Value;
                int openSlots = 0;
                for (int d = 1; d <= state.MaxDays; d++)
                    for (int h = 1; h <= state.MaxHours; h++)
                        if (state.IsTeacherOpen(tid, d, h)) openSlots++;
                
                if (load > openSlots)
                {
                    string name = state.Teachers.TryGetValue(tid, out var t) ? t.Name : $"ID:{tid}";
                    log?.Invoke($"[HATA] {name} yükü ({load}) kapasitesinden ({openSlots}) fazla!");
                    return false;
                }
            }

            // Class Loads
            var cLoads = new Dictionary<int, int>();
            foreach (var b in blocks) 
                cLoads[b.ClassId] = cLoads.GetValueOrDefault(b.ClassId) + b.BlockDuration;

            foreach (var kvp in cLoads)
            {
                int cid = kvp.Key; int load = kvp.Value;
                int openSlots = 0;
                for (int d = 1; d <= state.MaxDays; d++)
                    for (int h = 1; h <= state.MaxHours; h++)
                        if (state.IsClassOpen(cid, d, h)) openSlots++;

                if (load > openSlots)
                {
                    string name = state.Classes.TryGetValue(cid, out var c) ? c.Name : $"ID:{cid}";
                    log?.Invoke($"[HATA] {name} sınıfının toplam ders saati ({load}), okulun açık olduğu saatlerden ({openSlots}) fazla!");
                    return false;
                }
            }

            // Room Capacities
            var rLoads = new Dictionary<int, int>();
            foreach (var b in blocks)
            {
                var rids = b.GetOrtakMekanIds();
                foreach (var rid in rids)
                    rLoads[rid] = rLoads.GetValueOrDefault(rid) + b.BlockDuration;
            }

            foreach (var kvp in rLoads)
            {
                int rid = kvp.Key; int load = kvp.Value;
                int openSlots = 0;
                for (int d = 1; d <= state.MaxDays; d++)
                    for (int h = 1; h <= state.MaxHours; h++)
                        if (state.IsSchoolOpen(d, h)) openSlots++; 

                if (load > openSlots)
                {
                    string name = state.Rooms.TryGetValue(rid, out var r) ? r.Name : $"Mekan ID:{rid}";
                    log?.Invoke($"[HATA] {name} mekanının kullanım yükü ({load} saat), okulun toplam kapasitesinden ({openSlots} saat) fazla!");
                    return false;
                }
            }

            // Lesson to Day Ratio (NEW CRITICAL CHECK)
            var lessonGroups = blocks.GroupBy(b => new { b.ClassId, b.LessonCode });
            foreach (var grp in lessonGroups)
            {
                int blockCount = grp.Count();
                if (blockCount <= 1) continue;

                int availableDays = 0;
                for (int d = 1; d <= state.MaxDays; d++)
                {
                    bool dayPossible = false;
                    for (int h = 1; h <= state.MaxHours; h++)
                    {
                        // Check if at least one block of this lesson can potentially fit in this day
                        if (grp.Any(b => IsStaticAvailable(state, b, d, h)))
                        {
                            dayPossible = true;
                            break;
                        }
                    }
                    if (dayPossible) availableDays++;
                }

                if (blockCount > availableDays)
                {
                    string cName = state.Classes.TryGetValue(grp.Key.ClassId, out var c) ? c.Name : grp.Key.ClassId.ToString();
                    log?.Invoke($"[HATA] {cName} - {grp.Key.LessonCode}: Bu ders {blockCount} farklı güne bölünmüş ama yerleşebileceği sadece {availableDays} farklı gün var! (Öğretmenlerin veya sınıfların kapalı saatlerini kontrol edin)");
                    return false;
                }
            }

            return true;
        }

        private async Task RunRelaxedDiagnostic(ScheduleState state, 
            Dictionary<int, List<(int d, int h, BoolVar bv)>> x,
            Dictionary<int, Dictionary<int, BoolVar>> blockDayVars,
            Dictionary<(int tid, int d, int h), List<BoolVar>> teacherSlots,
            Dictionary<(int cid, int d, int h), List<BoolVar>> classSlots,
            Dictionary<(int rid, int d, int h), List<BoolVar>> roomSlots,
            Action<string> log)
        {
            try
            {
                log?.Invoke("Gelişmiş Çıkmaz Analizi Yapılıyor (Relaxed Solve)...");
                CpModel model = new CpModel();
                var blocks = state.GetAllBlocks();
                var unplaced = new Dictionary<int, BoolVar>();
                
                // Re-create variables but with slack
                var diagX = new Dictionary<int, List<(int d, int h, BoolVar bv)>>();
                
                foreach(var b in blocks)
                {
                    unplaced[b.Id] = model.NewBoolVar($"unplaced_{b.Id}");
                    diagX[b.Id] = new List<(int d, int h, BoolVar bv)>();
                    
                    // Possible moves (reuse logic or assume we can find them)
                    // For diagnostic, we use the same moves as the original model
                    if (!x.ContainsKey(b.Id)) continue;

                    foreach(var move in x[b.Id])
                    {
                        var bv = model.NewBoolVar($"diag_{b.Id}_{move.d}_{move.h}");
                        diagX[b.Id].Add((move.d, move.h, bv));
                    }
                    
                    var allVars = diagX[b.Id].Select(m => (LinearExpr)m.bv).ToList();
                    allVars.Add(unplaced[b.Id]);
                    model.Add(LinearExpr.Sum(allVars) == 1);
                }

                // Add Hard Conflicts (Simplified)
                var diagTeacherSlots = new Dictionary<(int tid, int d, int h), List<BoolVar>>();
                var diagClassSlots = new Dictionary<(int cid, int d, int h), List<BoolVar>>();

                foreach(var bid in diagX.Keys)
                {
                    var b = blocks.First(bl => bl.Id == bid);
                    foreach(var move in diagX[bid])
                    {
                        for(int i=0; i<b.BlockDuration; i++)
                        {
                            int currH = move.h + i;
                            // Teacher
                            foreach(var tid in b.TeacherIds)
                            {
                                var key = (tid, move.d, currH);
                                if (!diagTeacherSlots.ContainsKey(key)) diagTeacherSlots[key] = new List<BoolVar>();
                                diagTeacherSlots[key].Add(move.bv);
                            }
                            // Class
                            var clsKey = (b.ClassId, move.d, currH);
                            if (!diagClassSlots.ContainsKey(clsKey)) diagClassSlots[clsKey] = new List<BoolVar>();
                            diagClassSlots[clsKey].Add(move.bv);
                        }
                    }
                }

                foreach(var list in diagTeacherSlots.Values) model.Add(LinearExpr.Sum(list) <= 1);
                foreach(var list in diagClassSlots.Values) model.Add(LinearExpr.Sum(list) <= 1);

                // Objective: Maximize placed blocks
                model.Minimize(LinearExpr.Sum(unplaced.Values.Select(v => (LinearExpr)v)));

                CpSolver solver = new CpSolver();
                solver.StringParameters = "max_time_in_seconds:10, num_search_workers:4";
                CpSolverStatus status = await System.Threading.Tasks.Task.Run(() => solver.Solve(model));

                if (status == CpSolverStatus.Optimal || status == CpSolverStatus.Feasible)
                {
                    var report = new StringBuilder();
                    report.AppendLine("--- ÇAKISMA TESPİT EDİLDİ ---");
                    report.AppendLine("Aşağıdaki dersler diğer kısıtlar yüzünden yerleşemiyor:");
                    int count = 0;
                    foreach(var b in blocks)
                    {
                        if (solver.BooleanValue(unplaced[b.Id]))
                        {
                            string cName = state.Classes.TryGetValue(b.ClassId, out var c) ? c.Name : "???";
                            report.AppendLine($"[!] {cName} - {b.LessonCode} (ID:{b.Id})");
                            count++;
                            if (count >= 15) { report.AppendLine("... ve daha fazlası."); break; }
                        }
                    }
                    if (count > 0) log?.Invoke(report.ToString());
                    else log?.Invoke("Ders bazlı bir çakışma bulunamadı, kısıtlar çok karmaşık.");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[Analiz Hatası] {ex.Message}");
            }
        }

        private bool IsStaticAvailable(ScheduleState state, DistributionBlock b, int d, int h)
        {
             for(int i=0; i<b.BlockDuration; i++)
             {
                 if (!state.IsSchoolOpen(d, h+i)) return false;
                 if (!state.IsClassOpen(b.ClassId, d, h+i)) return false;
                 foreach(var tid in b.TeacherIds) if (!state.IsTeacherOpen(tid, d, h+i)) return false;
                 foreach(var rid in b.GetOrtakMekanIds()) if (!state.IsRoomOpen(rid, d, h+i)) return false;
             }
             return true;
        }
    }
}
