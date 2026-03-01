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
    /// <summary>
    /// V7 ADVANCED OR-TOOLS SCHEDULER (BEST-EFFORT MODE)
    /// This engine prioritizes placing as many blocks as possible.
    /// If a solution is infeasible, it drops the minimum number of blocks and reports them.
    /// </summary>
    public class OrToolsAdvancedEngine
    {
        private static OrToolsAdvancedEngine _instance;
        public static OrToolsAdvancedEngine Instance => _instance ??= new OrToolsAdvancedEngine();

        public event Action<string> OnLog;

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }

        public async Task<bool> Run(DistributionParameters paramsData, Action<string> onStatusUpdate)
        {
            try
            {
                onStatusUpdate?.Invoke("V7 (Advanced AI) Veriler Yükleniyor...");
                
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

                if (paramsData.OperationMode == OperationMode.Rebuild)
                {
                    onStatusUpdate?.Invoke("Mevcut Dağıtım Temizleniyor...");
                    repo.ResetAllDistributions(keepManual: paramsData.PlacementMode == PlacementMode.KeepLocked);
                    blocks = repo.GetAllBlocks();
                }
                
                ScheduleState state = new ScheduleState(blocks, teachers, classes, rooms, schoolInfo, paramsData.MaxDays, paramsData.MaxHours);

                onStatusUpdate?.Invoke("V7 (Advanced AI) Model Kuruluyor...");
                
                CpModel model = new CpModel();
                var allBlocks = state.GetAllBlocks(); 
                
                var x = new Dictionary<int, List<(int d, int h, BoolVar bv)>>();
                var isPlaced = new Dictionary<int, BoolVar>(); // Key variable for V7
                var blockDayVars = new Dictionary<int, Dictionary<int, BoolVar>>(); 
                var processedBlocks = new HashSet<int>();

                int maxDays = paramsData.MaxDays;
                int maxHours = paramsData.MaxHours;

                // --- 1. DEĞİŞKENLER VE OPSİYONEL YERLEŞİM ---
                
                // 1a. Handle Unified Groups (Kardeş Dersler)
                var kardesGroups = allBlocks.Where(b => b.KardesId > 0).GroupBy(b => b.KardesId).ToList();
                foreach (var grp in kardesGroups)
                {
                    int gid = grp.Key;
                    var grpBlocks = grp.ToList();
                    var firstB = grpBlocks[0];
                    
                    var groupPlacedVar = model.NewBoolVar($"grp_placed_{gid}");
                    
                    var lockedB = grpBlocks.FirstOrDefault(b => b.IsLocked && b.Day > 0);
                    if (lockedB != null)
                    {
                        int fixedD = lockedB.Day;
                        int fixedH = lockedB.Hour;
                        
                        var v = model.NewBoolVar($"grp_{gid}_fixed"); 
                        model.Add(v == 1);
                        model.Add(groupPlacedVar == 1);
                        
                        var sharedX = new List<(int d, int h, BoolVar bv)> { (fixedD, fixedH, v) };
                        var sharedDayVars = new Dictionary<int, BoolVar> { { fixedD, model.NewBoolVar($"grpDay_{gid}_{fixedD}") } };
                        model.Add(sharedDayVars[fixedD] == 1);

                        foreach (var b in grpBlocks)
                        {
                            isPlaced[b.Id] = groupPlacedVar;
                            x[b.Id] = sharedX;
                            blockDayVars[b.Id] = sharedDayVars;
                            processedBlocks.Add(b.Id);
                        }
                    }
                    else
                    {
                        var commonSlots = new List<(int d, int h, BoolVar bv)>();
                        var commonDayVars = new Dictionary<int, BoolVar>();
                        
                        for (int d = 1; d <= maxDays; d++)
                        {
                            for (int h = 1; h <= maxHours - firstB.BlockDuration + 1; h++)
                            {
                                bool allOk = true;
                                foreach (var b in grpBlocks) if (!IsStaticAvailable(state, b, d, h)) { allOk = false; break; }
                                
                                if (allOk)
                                {
                                    var v = model.NewBoolVar($"grp_{gid}_{d}_{h}");
                                    commonSlots.Add((d, h, v));
                                    if (!commonDayVars.ContainsKey(d)) commonDayVars[d] = model.NewBoolVar($"grpDay_{gid}_{d}");
                                    model.AddImplication(v, commonDayVars[d]);
                                }
                            }
                        }

                        // V7 Logic: Group can be unplaced if impossible
                        model.Add(LinearExpr.Sum(commonSlots.Select(s => s.bv)) == groupPlacedVar);

                        foreach (var b in grpBlocks)
                        {
                            isPlaced[b.Id] = groupPlacedVar;
                            x[b.Id] = commonSlots;
                            blockDayVars[b.Id] = commonDayVars;
                            processedBlocks.Add(b.Id);
                        }
                    }
                }

                // 1b. Standard Independent Blocks
                foreach (var b in allBlocks)
                {
                    if (processedBlocks.Contains(b.Id)) continue;
                    
                    isPlaced[b.Id] = model.NewBoolVar($"placed_{b.Id}");
                    x[b.Id] = new List<(int d, int h, BoolVar bv)>();
                    blockDayVars[b.Id] = new Dictionary<int, BoolVar>();

                    if (b.IsLocked && b.Day != 0)
                    {
                        var v = model.NewBoolVar($"x_{b.Id}_fixed");
                        model.Add(v == 1);
                        model.Add(isPlaced[b.Id] == 1);
                        x[b.Id].Add((b.Day, b.Hour, v));
                        
                        var dv = model.NewBoolVar($"d_{b.Id}_{b.Day}");
                        model.Add(dv == 1);
                        blockDayVars[b.Id][b.Day] = dv;
                    }
                    else
                    {
                        for (int d = 1; d <= maxDays; d++)
                        {
                            if (!blockDayVars[b.Id].ContainsKey(d)) blockDayVars[b.Id][d] = model.NewBoolVar($"day_{b.Id}_{d}");
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
                        // V7 Logic: Block is placed if one of the slots is selected
                        model.Add(LinearExpr.Sum(x[b.Id].Select(v => v.bv)) == isPlaced[b.Id]);
                    }
                }

                // --- 2. ÇAKIŞMA KISITLARI ---
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
                            var cKey = (block.ClassId, move.d, currH);
                            if (!classSlots.ContainsKey(cKey)) classSlots[cKey] = new List<BoolVar>();
                            if (!classSlots[cKey].Contains(move.bv)) classSlots[cKey].Add(move.bv);
                            
                            foreach (var tid in block.TeacherIds)
                            {
                                var tKey = (tid, move.d, currH);
                                if (!teacherSlots.ContainsKey(tKey)) teacherSlots[tKey] = new List<BoolVar>();
                                if (!teacherSlots[tKey].Contains(move.bv)) teacherSlots[tKey].Add(move.bv);
                            }
                            
                            foreach (var rid in block.GetOrtakMekanIds())
                            {
                                var rKey = (rid, move.d, currH);
                                if (!roomSlots.ContainsKey(rKey)) roomSlots[rKey] = new List<BoolVar>();
                                if (!roomSlots[rKey].Contains(move.bv)) roomSlots[rKey].Add(move.bv);
                            }
                        }
                    }
                }

                foreach (var list in classSlots.Values) if (list.Count > 1) model.Add(LinearExpr.Sum(list) <= 1);
                foreach (var list in teacherSlots.Values) if (list.Count > 1) model.Add(LinearExpr.Sum(list) <= 1);
                foreach (var list in roomSlots.Values) if (list.Count > 1) model.Add(LinearExpr.Sum(list) <= 1);

                // --- 3. AYNI DERS AYNI GÜNE GELMEZ ---
                var lessonGroups = allBlocks.GroupBy(b => new { b.ClassId, b.LessonCode });
                foreach (var grp in lessonGroups)
                {
                    var blocksInGrp = grp.ToList();
                    if (blocksInGrp.Count <= 1) continue;
                    for (int d = 1; d <= maxDays; d++)
                    {
                        var varsInDay = new List<BoolVar>();
                        foreach (var b in blocksInGrp) if (blockDayVars[b.Id].TryGetValue(d, out var dv)) varsInDay.Add(dv);
                        if (varsInDay.Count > 1) model.Add(LinearExpr.Sum(varsInDay) <= 1);
                    }
                }

                // --- 4. OBJECTIVE (Best Effort + Gap Penalty) ---
                var objTerms = new List<LinearExpr>();
                
                // Extremely high penalty for not placing a block
                foreach(var b in allBlocks)
                {
                    // Minimize (1 - isPlaced)
                    // LinearExpr.Sum([1, -isPlaced])
                    objTerms.Add((1 - isPlaced[b.Id]) * 1000000); 
                }

                // Standard Gap Penalties (Lower Priority)
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

                        var dailyLoad = model.NewIntVar(0, maxHours, $"load_{tid}_{d}");
                        model.Add(dailyLoad == LinearExpr.Sum(dayOccupancy.Skip(1)));

                        var firstHour = model.NewIntVar(0, maxHours + 1, $"fh_{tid}_{d}");
                        var lastHour = model.NewIntVar(0, maxHours + 1, $"lh_{tid}_{d}");
                        var firstHourCandidates = new List<IntVar>();
                        var lastHourCandidates = new List<IntVar>();

                        for (int h = 1; h <= maxHours; h++)
                        {
                            var valF = model.NewIntVar(0, maxHours + 1, $"vF_{tid}_{d}_{h}");
                            model.Add(valF == h).OnlyEnforceIf(dayOccupancy[h]);
                            model.Add(valF == maxHours + 1).OnlyEnforceIf(dayOccupancy[h].Not());
                            firstHourCandidates.Add(valF);

                            var valL = model.NewIntVar(0, maxHours + 1, $"vL_{tid}_{d}_{h}");
                            model.Add(valL == h).OnlyEnforceIf(dayOccupancy[h]);
                            model.Add(valL == 0).OnlyEnforceIf(dayOccupancy[h].Not());
                            lastHourCandidates.Add(valL);
                        }

                        model.AddMinEquality(firstHour, firstHourCandidates);
                        model.AddMaxEquality(lastHour, lastHourCandidates);

                        var isWorking = model.NewBoolVar($"work_{tid}_{d}");
                        model.Add(dailyLoad > 0).OnlyEnforceIf(isWorking);
                        model.Add(dailyLoad == 0).OnlyEnforceIf(isWorking.Not());

                        var gapInfo = model.NewIntVar(0, maxHours, $"gap_{tid}_{d}");
                        model.Add(gapInfo == lastHour - firstHour + 1 - dailyLoad).OnlyEnforceIf(isWorking);
                        model.Add(gapInfo == 0).OnlyEnforceIf(isWorking.Not());

                        objTerms.Add(gapInfo * paramsData.V3GapPenalty); 
                        
                        var isLargeGap = model.NewBoolVar($"bg_{tid}_{d}");
                        model.Add(gapInfo > 2).OnlyEnforceIf(isLargeGap);
                        objTerms.Add(isLargeGap * 50000); 
                    }
                }
                
                model.Minimize(LinearExpr.Sum(objTerms));

                // --- 5. ÇÖZÜM ---
                onStatusUpdate?.Invoke("V7 (Advanced AI) Çözülüyor...");
                CpSolver solver = new CpSolver();
                solver.StringParameters = $"max_time_in_seconds:{paramsData.MaxTimeInSeconds}, num_search_workers:8";
                
                CpSolverStatus status = await Task.Run(() => solver.Solve(model));
                
                if (status == CpSolverStatus.Optimal || status == CpSolverStatus.Feasible)
                {
                    int placedCount = 0;
                    int failedCount = 0;
                    var failReport = new StringBuilder();
                    failReport.AppendLine("--- Yerleşemeyen Dersler (V7 Analizi) ---");

                    foreach (var b in allBlocks)
                    {
                        bool wasPlaced = false;
                        foreach (var move in x[b.Id])
                        {
                            if (solver.BooleanValue(move.bv))
                            {
                                b.Day = move.d;
                                b.Hour = move.h;
                                b.PlacementType = "v7_advanced";
                                placedCount++;
                                wasPlaced = true;
                                break;
                            }
                        }
                        
                        if (!wasPlaced && !(b.IsLocked && b.Day > 0))
                        {
                            b.Day = 0; b.Hour = 0;
                            failedCount++;
                            string cName = classes.ContainsKey(b.ClassId) ? classes[b.ClassId].Name : "???";
                            failReport.AppendLine($"[!] {cName} - {b.LessonCode} (ID:{b.Id})");
                        }
                    }

                    repo.SaveDistributionAtomically(allBlocks, paramsData.PlacementMode == PlacementMode.KeepLocked);
                    repo.SyncSignalTables();
                    teacherRepo.SyncAllTeacherHours();
                    
                    if (failedCount > 0)
                    {
                        onStatusUpdate?.Invoke($"V7 Tamamlandı. {placedCount} ders yerleşti, {failedCount} ders yerleşemedi (Çakışma var).");
                        onStatusUpdate?.Invoke(failReport.ToString());
                    }
                    else
                    {
                        onStatusUpdate?.Invoke($"V7 Başarılı! Tüm dersler ({placedCount}) yerleştirildi.");
                    }
                    return true;
                }
                
                onStatusUpdate?.Invoke($"[HATA] V7 bile çözüm bulamadı: {status}");
                return false;
            }
            catch (Exception ex)
            {
                onStatusUpdate?.Invoke($"[KRİTİK HATA] {ex.Message}");
                return false;
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
