using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Google.OrTools.Sat;
using DersDagitim.Models;
using System.Diagnostics;
using System.Text.Json;
using DersDagitim.Persistence;
using System.Text;

namespace DersDagitim.Services;

public class DistributionEngine
{
    private readonly DistributionRepository _repo;
    private readonly TeacherRepository _teacherRepo;
    private readonly ClassRepository _classRepo;
    private readonly SchoolRepository _schoolRepo;

    public DistributionEngine()
    {
        _repo = new DistributionRepository();
        _teacherRepo = new TeacherRepository();
        _classRepo = new ClassRepository();
        _schoolRepo = new SchoolRepository();
        //_logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ai_debug.txt");
    }

    private void Log(string message)
    {
        // try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n"); } catch { }
    }

    private bool TestOrToolsBasic(Action<string>? onStatusUpdate)
    {
        Log("AI temel test başlıyor...");
        try
        {
            CpModel testModel = new CpModel();
            var x = testModel.NewBoolVar("test_x");
            var y = testModel.NewBoolVar("test_y");
            testModel.Add(x + y <= 1);
            testModel.Maximize(x + y);
            
            CpSolver testSolver = new CpSolver();
            testSolver.StringParameters = "max_time_in_seconds:5";
            
            Log("Test Solve başlıyor...");
            var testStatus = testSolver.Solve(testModel);
            Log($"Test Solve sonucu: {testStatus}");
            
            if (testStatus == CpSolverStatus.Optimal || testStatus == CpSolverStatus.Feasible)
            {
                Log("AI temel test BAŞARILI!");
                return true;
            }
            else
            {
                Log($"AI temel test BAŞARISIZ: {testStatus}");
                onStatusUpdate?.Invoke($"AI test başarısız: {testStatus}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"AI temel test EXCEPTION: {ex.Message}\n{ex.StackTrace}");
            onStatusUpdate?.Invoke($"AI test hatası: {ex.Message}");
            return false;
        }
    }

    public void Run(DistributionParameters paramsData, Action<string>? onStatusUpdate = null)
    {
        Log("=========== DAĞITIM BAŞLIYOR (AI-ENHANCED V2) ===========");
        
        try
        {
            // 0. Pre-Flight Checks
            onStatusUpdate?.Invoke("AI Testi yapılıyor...");
            if (!TestOrToolsBasic(onStatusUpdate)) return;
            
            // 1. Fetch Data
            onStatusUpdate?.Invoke("Veriler Yükleniyor...");
            var blocks = _repo.GetAllBlocks() ?? new List<DistributionBlock>();
            var teachers = _teacherRepo.GetAll() ?? new List<Teacher>();
            var classes = _classRepo.GetAll() ?? new List<SchoolClass>();
            var schoolInfo = _schoolRepo.GetSchoolInfo() ?? new SchoolInfo();

            if (blocks.Count == 0) return;
            List<DistributionBlock> activeBlocks = blocks;

            // 2. Reset if needed
            if (paramsData.OperationMode == OperationMode.Rebuild)
            {
                bool isClearAll = paramsData.PlacementMode == PlacementMode.ClearAll;
                bool isKeepLocked = paramsData.PlacementMode == PlacementMode.KeepLocked;

                if (isClearAll || isKeepLocked)
                {
                    _repo.ResetAllDistributions(keepManual: isKeepLocked); 
                    foreach(var b in activeBlocks)
                    {
                        if (isClearAll) { b.Day = 0; b.Hour = 0; b.IsLocked = false; }
                        else if (isKeepLocked && !b.IsLocked) { b.Day = 0; b.Hour = 0; }
                    }
                }
            }

            // 3. Diagnostics
            var diagResult = DiagnoseCapacity(activeBlocks, teachers, paramsData, onStatusUpdate);
            if (diagResult.hasError)
            {
                Log("KRİTİK HATA TESPİT EDİLDİ.");
                if (paramsData.UseStrictMode)
                {
                    Log("Katı Mod Aktif: Dağıtım durduruluyor.");
                    throw new Exception("DAĞITIM ÖNCESİ KRİTİK HATALAR:\n\n" + diagResult.report + "\n\n(Devam etmek için Parametrelerden 'Katı Mod'u kapatın.)");
                }
                else
                {
                    Log("UYARI: Kapasite hatalarına rağmen (Esnek Mod) devam ediliyor...");
                    
                    // Show first few lines of error report to user
                    string[] errorLines = diagResult.report.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    string summary = "Kapasite Sorunları: ";
                    int count = 0;
                    foreach(var line in errorLines)
                    {
                        if (line.Contains("HATA") || line.Contains("YETERSİZ"))
                        {
                            summary += line.Replace("[!] HATA: ", "").Replace("YETERSİZ KAPASİTE: ", "") + " | ";
                            count++;
                            if (count >= 2) break; // Show max 2 errors to fit UI
                        }
                    }
                    if (count == 0) summary = "Kapasite yetersizlikleri tespit edildi.";
                    
                    onStatusUpdate?.Invoke($"UYARI: {summary} (En iyi çaba ile devam ediliyor...)");
                }
            }

            // 4. RETRY LOOP with Parameter Relaxation
            bool solved = false;
            int attempt = 1;
            int maxAttempts = 3;
            
            while (!solved && attempt <= maxAttempts)
            {
                onStatusUpdate?.Invoke($"AI Model Oluşturuluyor (Deneme {attempt}/{maxAttempts})...");
                
                long currentGapPenalty = paramsData.GapPenalty; 
                long splitPenaltyBase = 5000000;
                long hugeGapPenaltyBase = 5000000;

                if (attempt > 1) {
                    Log("!!! GEVŞETİLMİŞ MOD (Gap Cezası Korundu, Split ve Morning Esnetildi) !!!");
                    if (attempt >= 2) splitPenaltyBase = 500000; // 10x smaller
                    if (attempt >= 3) splitPenaltyBase = 5000;   // Almost negligible
                    
                    if (attempt >= 3) hugeGapPenaltyBase = 50000; // Allow huge gaps if desperate
                }

                CpModel model = new CpModel();
                Dictionary<(int, int, int), BoolVar> x = new(); 
                Dictionary<(int, int, int), List<BoolVar>> resourceMap = new();

                HashSet<(int, int)> schoolClosed = new();
                if (schoolInfo.DefaultTimetable != null)
                   foreach(var kvp in schoolInfo.DefaultTimetable)
                       if (kvp.Value == SlotState.Closed) schoolClosed.Add((kvp.Key.Day, kvp.Key.Hour));

                var tClosed = teachers.ToDictionary(t => t.Id, t => t.Constraints?.Where(c => c.Value == SlotState.Closed).Select(c => (c.Key.Day, c.Key.Hour)).ToHashSet() ?? new HashSet<(int, int)>());
                var cClosed = classes.ToDictionary(c => c.Id, c => c.Constraints?.Where(c => c.Value == SlotState.Closed).Select(c => (c.Key.Day, c.Key.Hour)).ToHashSet() ?? new HashSet<(int, int)>());

                // --- KEY CHANGE: Pre-emptive Day Closing (Forced Condensation) ---
                Dictionary<int, HashSet<int>> softDayBans = new(); // TeacherId -> Days to discourage

                if (paramsData.MinimizeWorkingDays)
                {
                    Log("Kompakt Mod Aktif: Düşük yüklü öğretmenler için gün kapatılıyor (ESNEK Mod)...");
                    Random rnd = new Random(42 + attempt); // Deterministic per attempt
                    
                    foreach (var t in teachers)
                    {
                        // Calculate Total Load
                        int totalLoad = activeBlocks.Where(b => b.TeacherIds.Contains(t.Id)).Sum(b => b.BlockDuration);
                        
                        Log($"[Kompakt Mod Analiz] Öğretmen: {t.Name}, Yük: {totalLoad}, ID: {t.Id}");

                        // Rule: If load <= 24 hours (fits in 4 days easily), force 1 day OFF.
                        if (totalLoad > 0 && totalLoad <= 24)
                        {
                            // Priority: Friday (5) > Thursday (4) > Monday (1) > Wednesday (3) > Tuesday (2)
                            List<int> candidateDays = new List<int> { 5, 4, 1, 3, 2 }; 
                            bool dayClosed = false;

                            foreach (var dayToClose in candidateDays)
                            {
                                if (dayToClose > paramsData.MaxDays) continue;

                                // Check if user already manually closed this day?
                                bool alreadyClosed = tClosed.ContainsKey(t.Id) && tClosed[t.Id].Count(x => x.Item1 == dayToClose) == paramsData.MaxHours;
                                if (alreadyClosed) 
                                {
                                    dayClosed = true; // Already has a day off, good.
                                    Log($"  -> Zaten {dayToClose}. günü kapalı. İşlem yapılmadı.");
                                    break; 
                                }

                                // Try to close this day
                                bool hasLockedLesson = activeBlocks.Any(b => b.TeacherIds.Contains(t.Id) && b.IsLocked && b.Day == dayToClose);
                                if (!hasLockedLesson)
                                {
                                    // SOFT BAN THIS DAY
                                    Log($"  -> {dayToClose}. gün İSTENMİYOR (Puan Cezası ile Yasaklanıyor).");
                                    if (!softDayBans.ContainsKey(t.Id)) softDayBans[t.Id] = new HashSet<int>();
                                    softDayBans[t.Id].Add(dayToClose);
                                    
                                    dayClosed = true;
                                    break; // Done, 1 day is enough
                                }
                                else
                                {
                                     Log($"  -> {dayToClose}. gün kapatılamadı (Kilitli ders var).");
                                }
                            }
                            
                            if (!dayClosed) Log("  -> Uygun boş gün bulunamadı (Tüm günler kilitli veya dolu).");
                        }
                    }
                }
                // -----------------------------------------------------------------

                // Key Gen Local Func
                (int, int, int) resourceMapKey(int type, int id, int d, int h) => (type, id, (d * 100) + h);

                // Helper to register resource usage
                void RegisterResourceUsage(DistributionBlock b, int d, int h, BoolVar v)
                {
                    // Class
                    for(int i=0; i<b.BlockDuration; i++) {
                         var k = resourceMapKey(0, b.ClassId, d, h+i);
                         if(!resourceMap.ContainsKey(k)) resourceMap[k] = new List<BoolVar>();
                         resourceMap[k].Add(v);
                    }
                    // Teachers
                    foreach(var tid in b.TeacherIds) {
                        for(int i=0; i<b.BlockDuration; i++) {
                             var k = resourceMapKey(1, tid, d, h+i);
                             if(!resourceMap.ContainsKey(k)) resourceMap[k] = new List<BoolVar>();
                             resourceMap[k].Add(v);
                        }
                    }
                    // Rooms
                    foreach(var rid in b.GetOrtakMekanIds()) {
                        for(int i=0; i<b.BlockDuration; i++) {
                             var k = resourceMapKey(2, rid, d, h+i);
                             if(!resourceMap.ContainsKey(k)) resourceMap[k] = new List<BoolVar>();
                             resourceMap[k].Add(v);
                        }
                    }
                }

                // 4A. Create Variables
                List<BoolVar> allVars = new();
                foreach (var b in activeBlocks)
                {
                    // Fixed Check
                    if ((b.IsLocked && b.Day > 0) || (paramsData.PlacementMode == PlacementMode.KeepCurrent && b.Day > 0))
                    {
                        var v = model.NewBoolVar($"fixed_{b.Id}");
                        model.Add(v == 1);
                        RegisterResourceUsage(b, b.Day, b.Hour, v);
                        x[(b.Id, b.Day, b.Hour)] = v;
                        continue;
                    }

                    List<BoolVar> possibilities = new();
                    for (int d = 1; d <= paramsData.MaxDays; d++)
                    {
                        int limit = paramsData.MaxHours - b.BlockDuration + 1;
                        for (int h = 1; h <= limit; h++)
                        {
                            if (IsBlocked(b, d, h, schoolClosed, tClosed, cClosed)) continue;
                            
                            var v = model.NewBoolVar($"x_{b.Id}_{d}_{h}");
                            x[(b.Id, d, h)] = v;
                            possibilities.Add(v);
                            allVars.Add(v);
                            RegisterResourceUsage(b, d, h, v);
                        }
                    }
                    if (possibilities.Count > 0) model.Add(LinearExpr.Sum(possibilities) == 1);
                    else Log($"[UYARI] Blok {b.Id} yerleşemez!");
                }

                // 4B. Hard Conflict Constraints
                foreach(var kvp in resourceMap)
                    if (kvp.Value.Count > 1) model.Add(LinearExpr.Sum(kvp.Value) <= 1);

                // 4C. Same Lesson Same Day Handling
                var blocksByLesson = activeBlocks.Where(b => b.ClassLessonId > 0).GroupBy(b => b.ClassLessonId);
                foreach(var grp in blocksByLesson)
                {
                    if (grp.Count() < 2) continue;
                    for (int d = 1; d <= paramsData.MaxDays; d++) {
                        List<BoolVar> dayVars = new();
                        foreach(var b in grp) {
                            for(int h=1; h<=paramsData.MaxHours; h++) 
                                if (x.TryGetValue((b.Id, d, h), out var v)) dayVars.Add(v);
                        }
                        if (dayVars.Count > 1) model.Add(LinearExpr.Sum(dayVars) <= 1);
                    }
                }

                // 5. OBJECTIVES & SOFT CONSTRAINTS
                List<LinearExpr> objTerms = new();

                // 5A. Morning Penalty (Relaxed on attempts)
                int morningP = (int)paramsData.MorningPenalty;
                if (attempt > 1) morningP = morningP / 2; // Relax this one

                if (morningP > 0) {
                    foreach(var kvp in x) {
                        objTerms.Add(kvp.Value * (int)(morningP * kvp.Key.Item3));
                    }
                }

                // 5B. Teacher Constraints
                var teacherBlocks = activeBlocks.SelectMany(b => b.TeacherIds.Select(tid => new { Tid = tid, Block = b })).GroupBy(i => i.Tid);
                foreach (var grp in teacherBlocks)
                {
                    int tid = grp.Key;
                    var tBlocks = grp.Select(g => g.Block).ToList();

                    for (int d = 1; d <= paramsData.MaxDays; d++)
                    {
                        Dictionary<int, BoolVar> hActive = new();
                        List<BoolVar> dayActiveVars = new();

                        for (int h = 1; h <= paramsData.MaxHours; h++)
                        {
                            List<BoolVar> candidates = new();
                            foreach (var b in tBlocks) {
                                int limit = paramsData.MaxHours - b.BlockDuration + 1;
                                for (int s = 1; s <= limit; s++) {
                                    if (h >= s && h < s + b.BlockDuration) {
                                        if (x.TryGetValue((b.Id, d, s), out var v)) candidates.Add(v);
                                    }
                                }
                            }
                            if (candidates.Count > 0) {
                                var occ = model.NewBoolVar($"occ_{tid}_{d}_{h}");
                                model.AddMaxEquality(occ, candidates);
                                hActive[h] = occ;
                                dayActiveVars.Add(occ);
                            }
                        }
                        if (hActive.Count == 0) continue;

                        // Variables
                        var lessonsCount = model.NewIntVar(0, paramsData.MaxHours, $"cnt_{tid}_{d}");
                        model.Add(lessonsCount == LinearExpr.Sum(dayActiveVars));
                        
                        var isWorking = model.NewBoolVar($"wrk_{tid}_{d}");
                        model.Add(lessonsCount > 0).OnlyEnforceIf(isWorking);
                        model.Add(lessonsCount == 0).OnlyEnforceIf(isWorking.Not());

                        // 1. Min Lesson Penalty (Relax slightly on attempt 3)
                        long singlePenalty = 2000000;
                        if (attempt >= 3) singlePenalty = 50000; // Last resort: Accept single lessons if needed

                        var isSingleLesson = model.NewBoolVar($"single_{tid}_{d}");
                        model.Add(lessonsCount == 1).OnlyEnforceIf(isSingleLesson);
                        model.Add(lessonsCount != 1).OnlyEnforceIf(isSingleLesson.Not());
                        objTerms.Add(isSingleLesson * singlePenalty); 

                        // 1.5. Soft Day Bans (Compact Mode High Penalty)
                        // If teacher works on a day we wanted to close, apply massive penalty (but allow it if mandatory)
                        if (softDayBans.ContainsKey(tid) && softDayBans[tid].Contains(d))
                        {
                            // 500,000 Penalty - High enough to prevent unless absolutely necessary for a class
                            objTerms.Add(isWorking * 500000);
                        } 

                        // 2. Linear Gap Calculation
                        var firstH = model.NewIntVar(0, paramsData.MaxHours + 1, $"fh_{tid}_{d}");
                        var lastH = model.NewIntVar(0, paramsData.MaxHours + 1, $"lh_{tid}_{d}");
                        
                        // Define First/Last
                        int maxVal = paramsData.MaxHours + 1;
                        List<IntVar> fCandidates = new();
                        List<IntVar> lCandidates = new();

                        for (int h = 1; h <= paramsData.MaxHours; h++) {
                             var hVal = model.NewIntVar(0, maxVal, $"hv_{tid}_{d}_{h}");
                             if (hActive.ContainsKey(h)) {
                                 model.Add(hVal == h).OnlyEnforceIf(hActive[h]);
                                 model.Add(hVal == maxVal).OnlyEnforceIf(hActive[h].Not());
                             } else {
                                 model.Add(hVal == maxVal);
                             }
                             fCandidates.Add(hVal);

                             var hValL = model.NewIntVar(0, maxVal, $"hvl_{tid}_{d}_{h}");
                             if (hActive.ContainsKey(h)) {
                                 model.Add(hValL == h).OnlyEnforceIf(hActive[h]);
                                 model.Add(hValL == 0).OnlyEnforceIf(hActive[h].Not());
                             } else {
                                 model.Add(hValL == 0);
                             }
                             lCandidates.Add(hValL);
                        }
                        model.AddMinEquality(firstH, fCandidates);
                        model.AddMaxEquality(lastH, lCandidates);

                        var gap = model.NewIntVar(0, paramsData.MaxHours, $"gap_{tid}_{d}");
                        model.Add(gap == lastH - firstH + 1 - lessonsCount).OnlyEnforceIf(isWorking);
                        model.Add(gap == 0).OnlyEnforceIf(isWorking.Not());

                        // Gap Penalty
                        if (currentGapPenalty > 0)
                            objTerms.Add(gap * currentGapPenalty); 

                        // 3. CRITICAL ANTI-SPLIT LOGIC (Anti-Karnıyarık)
                        // OPTIMIZATION: Changed threshold from 4 to 2, and use dynamic penalty
                        
                        var isLowLoad = model.NewBoolVar($"low_{tid}_{d}");
                        model.Add(lessonsCount <= 2).OnlyEnforceIf(isLowLoad);
                        model.Add(lessonsCount > 2).OnlyEnforceIf(isLowLoad.Not());
                        
                        // Strict Compactness for Low Load
                        var hasGap = model.NewBoolVar($"hasGap_{tid}_{d}");
                        model.Add(gap > 0).OnlyEnforceIf(hasGap);
                        model.Add(gap == 0).OnlyEnforceIf(hasGap.Not());
                        
                        var splitDayPenalty = model.NewBoolVar($"split_{tid}_{d}");
                        model.AddBoolAnd(new[] { isLowLoad, hasGap }).OnlyEnforceIf(splitDayPenalty);
                        // Imposing Dynamic Penalty for Split Day on Low Load
                        objTerms.Add(splitDayPenalty * splitPenaltyBase);

                        // General Max Gap >= 4 is always bad
                        var hugeGap = model.NewBoolVar($"hugeGap_{tid}_{d}");
                        model.Add(gap > 3).OnlyEnforceIf(hugeGap);
                        model.Add(gap <= 3).OnlyEnforceIf(hugeGap.Not());
                        objTerms.Add(hugeGap * hugeGapPenaltyBase);

                        // 4. Adjacency Boost
                        if (paramsData.AdjacencyReward > 0) {
                            for(int h=1; h<paramsData.MaxHours; h++) {
                                if (hActive.ContainsKey(h) && hActive.ContainsKey(h+1)) {
                                    var adj = model.NewBoolVar($"adj_{tid}_{d}_{h}");
                                    model.AddBoolAnd(new[] { hActive[h], hActive[h+1] }).OnlyEnforceIf(adj);
                                    
                                    objTerms.Add(adj * -paramsData.AdjacencyReward);
                                    
                                    // Extra Boost to encourage sticking
                                    var lowAdj = model.NewBoolVar($"ladj_{tid}_{d}_{h}");
                                    model.AddBoolAnd(new[] { adj, isLowLoad }).OnlyEnforceIf(lowAdj);
                                    objTerms.Add(lowAdj * -(paramsData.AdjacencyReward * 10)); 
                                }
                            }
                        }
                        
                        // 5. Minimize Working Days cost
                        if (paramsData.MinimizeWorkingDays)
                             objTerms.Add(isWorking * 500000);
                    }
                }

                // SOLVE
                model.Minimize(LinearExpr.Sum(objTerms));
                
                Log($"Model Built. Vars: {allVars.Count}. Attempt: {attempt}");
                CpSolver solver = new CpSolver();
                // Parallel search enabled
                solver.StringParameters = $"max_time_in_seconds:{paramsData.MaxTimeInSeconds},num_search_workers:8,log_search_progress:true,linearization_level:2,cp_model_presolve:true,random_seed:42";
                
                var status = solver.Solve(model);
                Log($"Solve Status (Attempt {attempt}): {status}");
                
                if (status == CpSolverStatus.Optimal || status == CpSolverStatus.Feasible)
                {
                    ApplySolution(solver, x, activeBlocks);
                    CalculateAndSaveTeacherHours(activeBlocks);
                    onStatusUpdate?.Invoke("Dağıtım Başarılı!");
                    solved = true;
                }
                else
                {
                    attempt++;
                    if (attempt > maxAttempts) {
                        string reason = "Bilinmeyen Neden";
                        string details = "";

                        if (status == CpSolverStatus.Infeasible)
                        {
                            reason = "KISIT ÇAKIŞMASI (INFEASIBLE).";
                            
                            // Generate Specific Details
                            var diag = DiagnoseCapacity(activeBlocks, teachers, paramsData, null);
                            if (diag.hasError) // Only if strictly obvious errors exists
                            {
                                details = "Tespit Edilen Kritik Sorunlar:\n" + diag.report; 
                            }
                            else
                            {
                                // Heuristic: Find 'Tight' Resources (Load > 90% Capacity)
                                var tightReport = new StringBuilder();
                                
                                // 1. Tight Teachers
                                tightReport.AppendLine("Muhtemel Sıkışıklık Noktaları:");
                                foreach(var t in teachers)
                                {
                                    // Calculate Open Slots manualy as we don't have access to solver internals
                                    int openSlots = 0;
                                    for(int d=1; d<=paramsData.MaxDays; d++)
                                        for(int h=1; h<=paramsData.MaxHours; h++)
                                            if(t.Constraints == null || !t.Constraints.ContainsKey(new TimeSlot(d,h)) || t.Constraints[new TimeSlot(d,h)]!=SlotState.Closed) openSlots++;
                                            
                                    int load = activeBlocks.Where(b => b.TeacherIds.Contains(t.Id)).Sum(b => b.BlockDuration);
                                    if(load > 0 && openSlots > 0 && (double)load/openSlots > 0.95)
                                    {
                                        tightReport.AppendLine($"- Öğretmen: {t.Name} (Yük: {load}, Boş: {openSlots}) -> %{(int)((double)load/openSlots*100)} Dolu");
                                    }
                                }
                                
                                details = tightReport.ToString();
                                if (details.Length < 30) details = "Belirgin bir kapasite sorunu görülmedi. Sorun çakışan ders saatleri veya oda sıkıntısı olabilir.";
                            }
                        }
                        else if (status == CpSolverStatus.Unknown)
                        {
                            reason = "ZAMAN AŞIMI (TIMEOUT).";
                            details = "Karmaşıklık çok yüksek. Lütfen 'Düşünme Süresi'ni artırın.";
                        }
                        
                        Log($"DAĞITIM BAŞARISIZ! Reason: {status}");
                        onStatusUpdate?.Invoke($"!!! BAŞARISIZ ({status}) !!!\n\n{reason}\n\n{details}");
                    } else {
                        onStatusUpdate?.Invoke($"Çözüm Bulunamadı ({status}), parametreler esnetilerek tekrar deneniyor ({attempt})...");
                    }
                }
            } // End Retry Loop

        }
        catch (Exception ex)
        {
             Log($"FATAL: {ex.Message}");
             onStatusUpdate?.Invoke($"HATA: {ex.Message}");
             throw; 
        }
    }
    
    // --- HELPERS ---

    private void RegisterUsage(DistributionBlock b, int d, int h, BoolVar v, Dictionary<(int, int, int), List<BoolVar>> map)
    {
        // 0=Class, 1=Teacher, 2=Room
        // Class
        for(int i=0; i<b.BlockDuration; i++)
        {
             var keyC = (0, b.ClassId, (d*100) + h+i);
             if(!map.ContainsKey(keyC)) map[keyC] = new List<BoolVar>();
             map[keyC].Add(v);
        }
        // Teachers
        foreach(var tid in b.TeacherIds)
        {
            for(int i=0; i<b.BlockDuration; i++)
            {
                 var keyT = (1, tid, (d*100) + h+i);
                 if(!map.ContainsKey(keyT)) map[keyT] = new List<BoolVar>();
                 map[keyT].Add(v);
            }
        }
        // Rooms
        foreach(var rid in b.GetOrtakMekanIds())
        {
            for(int i=0; i<b.BlockDuration; i++)
            {
                 var keyR = (2, rid, (d*100) + h+i);
                 if(!map.ContainsKey(keyR)) map[keyR] = new List<BoolVar>();
                 map[keyR].Add(v);
            }
        }
    }
    
    private void ApplySolution(CpSolver solver, Dictionary<(int, int, int), BoolVar> x, List<DistributionBlock> blocks)
    {
        var blockMap = blocks.ToDictionary(b => b.Id);
        foreach(var kvp in x)
        {
            if (solver.Value(kvp.Value) == 1)
            {
                int bid = kvp.Key.Item1;
                var b = blockMap[bid];
                b.Day = kvp.Key.Item2;
                b.Hour = kvp.Key.Item3;
                _repo.PlaceBlock(b, "ortools_ai");
            }
        }
    }

    private bool IsBlocked(DistributionBlock b, int d, int h, 
        HashSet<(int, int)> schoolClosed, 
        Dictionary<int, HashSet<(int, int)>> tClosed, 
        Dictionary<int, HashSet<(int, int)>> cClosed)
    {
        for (int i = 0; i < b.BlockDuration; i++) 
        {
            int slotH = h + i;
            if (schoolClosed.Contains((d, slotH))) return true;
            if (cClosed.ContainsKey(b.ClassId) && cClosed[b.ClassId].Contains((d, slotH))) return true;
            foreach(var tid in b.TeacherIds) 
                if (tClosed.ContainsKey(tid) && tClosed[tid].Contains((d, slotH))) return true;
        }
        return false;
    }

    private void CalculateAndSaveTeacherHours(List<DistributionBlock> placedBlocks)
    {
        var allTeachers = _teacherRepo.GetAll();
        foreach(var t in allTeachers) 
        {
            _teacherRepo.UpdateTotalHours(t.Id);
        }
    }

    private (bool hasError, string report) DiagnoseCapacity(List<DistributionBlock> blocks, List<Teacher> teachers, DistributionParameters paramsData, Action<string>? onStatusUpdate)
    {
        Log("--- KAPASİTE ANALİZİ ---");
        bool hasCriticalError = false;
        StringBuilder errorReport = new StringBuilder();
        errorReport.AppendLine("=== DAĞITIM ÖNCESİ KONTROL RAPORU ===");
        
        // Teacher Analysis
        Dictionary<int, int> load = new();
        foreach(var b in blocks)
            foreach(var tid in b.TeacherIds)
                load[tid] = load.GetValueOrDefault(tid, 0) + b.BlockDuration;
                
        foreach(var t in teachers)
        {
            if (!load.ContainsKey(t.Id)) continue;
             
            int openSlots = 0;
            for(int d=1; d<=paramsData.MaxDays; d++)
                for(int h=1; h<=paramsData.MaxHours; h++)
                {
                    bool isClosed = t.Constraints.ContainsKey(new TimeSlot(d, h)) && t.Constraints[new TimeSlot(d, h)] == SlotState.Closed;
                    if (!isClosed) openSlots++;
                }
                
            Log($"Öğretmen {t.Name}: Yük={load[t.Id]}, Açık Slot={openSlots}");
            if (load[t.Id] > openSlots)
            {
                string msg = $"[!] HATA: Öğretmen {t.Name} yükü ({load[t.Id]}) > kapasite ({openSlots})!";
                Log(msg);
                errorReport.AppendLine(msg);
                hasCriticalError = true;
            }
        }

        // --- NEW: Room (Ortak Mekan) Analysis ---
        Dictionary<int, int> roomLoad = new();
        foreach(var b in blocks)
        {
            var rids = b.GetOrtakMekanIds();
            foreach(var rid in rids) roomLoad[rid] = roomLoad.GetValueOrDefault(rid, 0) + b.BlockDuration;
        }
        var rmRepo = new OrtakMekanRepository();
        var rooms = rmRepo.GetAll();
        foreach(var r in rooms)
        {
             if (!roomLoad.ContainsKey(r.Id)) continue;
             int openSlots = paramsData.MaxDays * paramsData.MaxHours; // Rooms usually open. Add constraints if needed later.
             
             if (roomLoad[r.Id] > openSlots)
             {
                 string msg = $"[!] HATA: Mekan {r.Name} yükü ({roomLoad[r.Id]}) > kapasite ({openSlots})!";
                 Log(msg);
                 errorReport.AppendLine(msg);
                 hasCriticalError = true;
             }
        }

        // Class Analysis
        var classRepo = new ClassRepository();
        var classes = classRepo.GetAll();
        foreach(var c in classes)
        {
            var cBlocks = blocks.Where(b => b.ClassId == c.Id).ToList();
            int totalLoad = cBlocks.Sum(b => b.BlockDuration);
            
            int classOpenSlots = 0;
            for(int d=1; d<=paramsData.MaxDays; d++)
                for(int h=1; h<=paramsData.MaxHours; h++)
                {
                    bool isClosed = c.Constraints != null && c.Constraints.ContainsKey(new TimeSlot(d, h)) && c.Constraints[new TimeSlot(d, h)] == SlotState.Closed;
                    if (!isClosed) classOpenSlots++;
                }

            if (totalLoad > 0)
            {
                Log($"Sınıf {c.Name}: Yük={totalLoad}, Açık Slot={classOpenSlots}");
                if (totalLoad > classOpenSlots)
                {
                    string msg = $"[!] HATA: Sınıf {c.Name} yükü ({totalLoad}) > kapasite ({classOpenSlots})!";
                    Log(msg);
                    errorReport.AppendLine(msg);
                }

                var lessonGroups = cBlocks.GroupBy(b => b.ClassLessonId);
                foreach (var group in lessonGroups)
                {
                    int blockCount = group.Count();
                    if (blockCount > paramsData.MaxDays)
                    {
                        var lessonName = group.First().LessonCode;
                        string msgSameDay = $"[!] KRİTİK HATA: Sınıf {c.Name} - {lessonName} ({blockCount} blok) > Gün Sayısı ({paramsData.MaxDays})";
                        Log(msgSameDay);
                        errorReport.AppendLine(msgSameDay);
                        hasCriticalError = true;
                    }
                }
            }
        }

        Log("--- ANALİZ TAMAMLANDI ---");
        
        if (hasCriticalError)
        {
             Log("DURDURULDU: Kritik hatalar mevcut.");
        }
        
        return (hasCriticalError, errorReport.ToString());
    }
}
