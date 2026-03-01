using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.OrTools.Sat;
using DersDagitim.Models;

namespace DersDagitim.Services;

/// <summary>
/// OR-Tools CP-SAT tabanlı program düzenleme SERBEST motoru.
/// Mevcut dağıtımda bir bloğu yeni konuma taşırken tüm blokları
/// esneterek çözüm arar (Tüm okulu tekrar organize eder).
/// V2/V3 motorlarına dokunmaz.
/// </summary>
public static class OrToolsScheduleEditEngineFree
{
    /// <summary>
    /// Bir bloğu hedef slota taşıyarak tüm çakışmaları CP-SAT solver ile çözer.
    /// Tüm hesaplamalar in-memory yapılır ve tüm okul çözüme dahil edilir.
    /// </summary>
    public static async Task<ScheduleEditService.MoveResult> SolveEdit(
        DistributionBlock sourceBlock,
        int targetDay, int targetHour,
        List<DistributionBlock> allPlacedBlocks,
        Dictionary<int, Teacher> teachers,
        Dictionary<int, SchoolClass> classes,
        Dictionary<int, OrtakMekan> rooms,
        int maxDays, int maxHours)
    {
        var result = new ScheduleEditService.MoveResult();

        // ===== A. ÖN DOĞRULAMA (UI Thread) =====

        if (sourceBlock.IsLocked)
        {
            result.Message = "Bu blok kilitli, taşınamaz.";
            return result;
        }

        for (int i = 0; i < sourceBlock.BlockDuration; i++)
        {
            if (targetHour + i > maxHours)
            {
                result.Message = $"Blok süresi gün sonunu aşıyor (saat {targetHour + i} > {maxHours}).";
                return result;
            }
        }

        var schoolInfo = new DersDagitim.Persistence.SchoolRepository().GetSchoolInfo();

        if (IsAnySlotTrulyClosed(sourceBlock, targetDay, targetHour, teachers, classes, schoolInfo))
        {
            result.Message = "Hedef slot KAPALI, buraya yerleştirilemez.";
            return result;
        }

        if (sourceBlock.Day == targetDay && sourceBlock.Hour == targetHour)
        {
            result.Success = true;
            result.Message = "Blok zaten bu konumda.";
            return result;
        }

        string lockedConflictMsg = GetLockedConflictAtTarget(sourceBlock, targetDay, targetHour, allPlacedBlocks);
        if (!string.IsNullOrEmpty(lockedConflictMsg))
        {
            result.Message = lockedConflictMsg;
            App.LogToDisk($"[EditEngine] ÖN-DOĞRULAMA REDDİ: {lockedConflictMsg}");
            return result;
        }

        // ===== B-H: Model oluşturma ve çözüm (Background Thread) =====
        return await Task.Run(() => BuildAndSolve(
            sourceBlock, targetDay, targetHour,
            allPlacedBlocks, teachers, classes, rooms,
            maxDays, maxHours, schoolInfo));
    }

    private static ScheduleEditService.MoveResult BuildAndSolve(
        DistributionBlock sourceBlock,
        int targetDay, int targetHour,
        List<DistributionBlock> allPlacedBlocks,
        Dictionary<int, Teacher> teachers,
        Dictionary<int, SchoolClass> classes,
        Dictionary<int, OrtakMekan> rooms,
        int maxDays, int maxHours,
        SchoolInfo schoolInfo)
    {
        var result = new ScheduleEditService.MoveResult();

        try
        {
            // Orijinal pozisyonları sakla
            var originalPositions = allPlacedBlocks.ToDictionary(
                b => b.Id,
                b => (day: b.Day, hour: b.Hour));

            // ===== B. ETKİLENEN BLOKLARI BUL (Focused Approach) =====
            var movableIds = FindMovableBlocks(sourceBlock, targetDay, targetHour,
                allPlacedBlocks, maxDays, maxHours);

            App.LogToDisk($"[EditEngine] Kaynak: Blok {sourceBlock.Id} → ({targetDay},{targetHour}). " +
                          $"Toplam blok: {allPlacedBlocks.Count}, Taşınabilir: {movableIds.Count}");

            // ===== C. CP-SAT MODEL OLUŞTURMA =====
            var model = new CpModel();
            var x = new Dictionary<int, List<(int d, int h, BoolVar bv)>>();
            var blockDayVars = new Dictionary<int, Dictionary<int, BoolVar>>();
            var kardesVars = new Dictionary<int, List<(int d, int h, BoolVar bv)>>();
            var kardesDayVars = new Dictionary<int, Dictionary<int, BoolVar>>();

            // Her bloğu bağımsız yerine KardesId'si aynı olanları aynı değişken (BoolVar) ile gruplar.
            // Bu sayede Ortak Derslerde öğretmenin saat limiti aşılıp Infeasible fırlatmaz.
            foreach (var b in allPlacedBlocks)
            {
                x[b.Id] = new List<(int d, int h, BoolVar bv)>();
                blockDayVars[b.Id] = new Dictionary<int, BoolVar>();

                bool isSourceOrSourceKardes = b.Id == sourceBlock.Id || (b.KardesId > 0 && b.KardesId == sourceBlock.KardesId);

                if (isSourceOrSourceKardes)
                {
                    // Kaynak blok veya kardeşi → hedef pozisyona sabitle
                    BoolVar v, dv;
                    if (b.KardesId > 0 && kardesVars.ContainsKey(b.KardesId))
                    {
                        v = kardesVars[b.KardesId].First().bv;
                        dv = kardesDayVars[b.KardesId][targetDay];
                    }
                    else
                    {
                        v = model.NewBoolVar($"x_{b.Id}_target");
                        model.Add(v == 1);
                        dv = model.NewBoolVar($"d_{b.Id}_{targetDay}");
                        model.Add(dv == 1);
                        
                        if (b.KardesId > 0)
                        {
                            kardesVars[b.KardesId] = new List<(int, int, BoolVar)> { (targetDay, targetHour, v) };
                            kardesDayVars[b.KardesId] = new Dictionary<int, BoolVar> { { targetDay, dv } };
                        }
                    }
                    
                    x[b.Id].Add((targetDay, targetHour, v));
                    blockDayVars[b.Id][targetDay] = dv;
                }
                else if (movableIds.Contains(b.Id) && !b.IsLocked)
                {
                    // Taşınabilir blok
                    if (b.KardesId > 0 && kardesVars.ContainsKey(b.KardesId))
                    {
                        // Aynı kardeş grubunun oluşturulmuş değişkenlerini bağla
                        x[b.Id] = kardesVars[b.KardesId].ToList();
                        foreach (var kv in kardesDayVars[b.KardesId])
                            blockDayVars[b.Id][kv.Key] = kv.Value;
                    }
                    else
                    {
                        for (int d = 1; d <= maxDays; d++)
                        {
                            var dv = model.NewBoolVar($"day_{b.Id}_{d}");
                            blockDayVars[b.Id][d] = dv;

                            for (int h = 1; h <= maxHours - b.BlockDuration + 1; h++)
                            {
                                // Sınıfın veya Öğretmenin GERÇEKTEN KAPALI olduğu mesai dışı saatlere ders kesinlikle konulamasın.
                                // Eğer ders konacak başka açık bir yer kalmazsa, bu ders zorunlu olarak "isAssigned=False" listesine düşecektir.
                                if (IsAnySlotTrulyClosed(b, d, h, teachers, classes, schoolInfo))
                                    continue;

                                var v = model.NewBoolVar($"x_{b.Id}_{d}_{h}");
                                x[b.Id].Add((d, h, v));
                                model.AddImplication(v, dv);
                            }
                        }

                        var possibleVars = x[b.Id].Select(v => v.bv).ToList();
                        if (possibleVars.Count == 0)
                        {
                            string className = classes.TryGetValue(b.ClassId, out var c) ? c.Name : "";
                            var tNames = string.Join(", ", b.TeacherIds.Select(tid => teachers.TryGetValue(tid, out var t) ? t.Name : ""));
                            result.Message = $"Taşıma Hatası: '{b.LessonCode}' dersi ({className}, {tNames}) için boşta veya esnetilebilir hiçbir uygun saat bulunamadı.";
                            return result;
                        }

                        model.Add(LinearExpr.Sum(possibleVars) == 1);

                        if (b.KardesId > 0)
                        {
                            kardesVars[b.KardesId] = x[b.Id].ToList();
                            kardesDayVars[b.KardesId] = blockDayVars[b.Id].ToDictionary(k => k.Key, v => v.Value);
                        }
                    }
                }
                else
                {
                    // Sabit blok → mevcut pozisyonda sabitle
                    BoolVar v, dv;
                    if (b.KardesId > 0 && kardesVars.ContainsKey(b.KardesId))
                    {
                        v = kardesVars[b.KardesId].First().bv;
                        dv = kardesDayVars[b.KardesId][b.Day];
                    }
                    else
                    {
                        v = model.NewBoolVar($"x_{b.Id}_fixed");
                        model.Add(v == 1);
                        dv = model.NewBoolVar($"d_{b.Id}_{b.Day}");
                        model.Add(dv == 1);

                        if (b.KardesId > 0)
                        {
                            kardesVars[b.KardesId] = new List<(int, int, BoolVar)> { (b.Day, b.Hour, v) };
                            kardesDayVars[b.KardesId] = new Dictionary<int, BoolVar> { { b.Day, dv } };
                        }
                    }
                    
                    x[b.Id].Add((b.Day, b.Hour, v));
                    blockDayVars[b.Id][b.Day] = dv;
                }
            }

            // ===== D. HARD KISITLAR =====

            var classSlots = new Dictionary<(int cid, int d, int h), List<BoolVar>>();
            var teacherSlots = new Dictionary<(int tid, int d, int h), List<BoolVar>>();
            var roomSlots = new Dictionary<(int rid, int d, int h), List<BoolVar>>();

            foreach (var bid in x.Keys)
            {
                var block = allPlacedBlocks.First(b => b.Id == bid);
                foreach (var move in x[bid])
                {
                    for (int i = 0; i < block.BlockDuration; i++)
                    {
                        int currH = move.h + i;

                        var cKey = (block.ClassId, move.d, currH);
                        if (!classSlots.ContainsKey(cKey)) classSlots[cKey] = new List<BoolVar>();
                        if (!classSlots[cKey].Contains(move.bv))
                            classSlots[cKey].Add(move.bv);

                        foreach (var tid in block.TeacherIds)
                        {
                            var tKey = (tid, move.d, currH);
                            if (!teacherSlots.ContainsKey(tKey)) teacherSlots[tKey] = new List<BoolVar>();
                            if (!teacherSlots[tKey].Contains(move.bv))
                                teacherSlots[tKey].Add(move.bv);
                        }

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

            foreach (var list in classSlots.Values)
                if (list.Count > 1) model.Add(LinearExpr.Sum(list) <= 1);
            foreach (var list in teacherSlots.Values)
                if (list.Count > 1) model.Add(LinearExpr.Sum(list) <= 1);
            foreach (var list in roomSlots.Values)
                if (list.Count > 1) model.Add(LinearExpr.Sum(list) <= 1);

            // ===== E. AYNI DERS AYNI GÜN KISITI (İPTAL / SOFT YAPILDI) =====
            // V3'ün doğası gereği bazı durumlarda aynı güne aynı ders düşmüş olabilir.
            // Bu durumu hard kısıt yaparsak sistem çözümsüz (Infeasible) kalır.
            // Bu nedenle bu kuralı Soft Amaç Fonksiyonuna cezalı olarak taşıyoruz.

            // ===== F. SOFT AMAÇ FONKSİYONU =====

            var objTerms = new List<LinearExpr>();
            const int STAY_PENALTY = 1000;
            const int SAME_DAY_LESSON_PENALTY = 5000;

            // 1. Yerinde Kalma Tercihi
            foreach (var b in allPlacedBlocks)
            {
                if (b.Id == sourceBlock.Id || b.IsLocked) continue;
                if (!movableIds.Contains(b.Id)) continue; // Sabit bloklar zaten yerinde

                if (!originalPositions.TryGetValue(b.Id, out var origPos)) continue;

                var origVar = x[b.Id].FirstOrDefault(v => v.d == origPos.day && v.h == origPos.hour);
                if (origVar.bv != null)
                {
                    var moved = model.NewBoolVar($"moved_{b.Id}");
                    model.Add(moved == 1 - origVar.bv);
                    objTerms.Add(moved * STAY_PENALTY);
                }
                else
                {
                    objTerms.Add(LinearExpr.Constant(STAY_PENALTY));
                }
            }

            // 2. Aynı Gün Aynı Ders Soft Cezası (Basitleştirilmiş)
            var lessonGroupsObj = allPlacedBlocks.GroupBy(b => new { b.ClassId, b.LessonCode });
            foreach (var grp in lessonGroupsObj)
            {
                var blocksInGrp = grp.ToList();
                if (blocksInGrp.Count <= 1) continue;

                bool anyMovable = blocksInGrp.Any(b => b.Id == sourceBlock.Id || movableIds.Contains(b.Id));
                if (!anyMovable) continue;

                for (int d = 1; d <= maxDays; d++)
                {
                    var varsInDay = new List<BoolVar>();
                    
                    // O günde sabit olarak bulunan ders sayısı (bu sayı ceza tabanıdır)
                    int fixedCount = blocksInGrp.Count(b => b.Id != sourceBlock.Id && !movableIds.Contains(b.Id) && b.Day == d);
                    
                    // O güne atanabilme ihtimali olan movable değişkenleri topla
                    foreach (var b in blocksInGrp)
                    {
                        if (b.Id == sourceBlock.Id || movableIds.Contains(b.Id))
                        {
                            if (blockDayVars.TryGetValue(b.Id, out var dayVars) && dayVars.TryGetValue(d, out var dv))
                                varsInDay.Add(dv);
                        }
                    }

                    if (varsInDay.Count > 0)
                    {
                        // Sadece o gün gerçekten hareket edenleri toplayıp ceza katsayısıyla çarparak eklenecek
                        var totalInDay = LinearExpr.Sum(varsInDay) + fixedCount;
                        
                        // İdeal olan bir günde en fazla 1 dersin olması.
                        // Fazla olanları penalty olarak amaca dahil edelim.
                        var penaltyVar = model.NewIntVar(0, 100, $"penalty_{grp.Key.ClassId}_{grp.Key.LessonCode}_{d}");
                        
                        // penaltyVar >= totalInDay - 1
                        model.Add(penaltyVar >= totalInDay - 1);
                        
                        // Cezayı ekle
                        objTerms.Add(penaltyVar * SAME_DAY_LESSON_PENALTY);
                    }
                }
            }
            
            // ===== G. ÇÖZÜM =====

            if (objTerms.Count > 0)
                model.Minimize(LinearExpr.Sum(objTerms));

            // ===== G. ÇÖZÜM =====

            int totalVars = x.Values.Sum(list => list.Count);
            App.LogToDisk($"[EditEngine] Model: {totalVars} değişken, " +
                          $"{classSlots.Count + teacherSlots.Count + roomSlots.Count} çakışma slotu");

            var solver = new CpSolver();
            solver.StringParameters = "max_time_in_seconds:30, num_search_workers:8, log_search_progress:true";

            // CP-SAT C++ Console (Stdout) çıktılarını yakalamak için Stream yönlendirmesi
            var originalOut = Console.Out;
            var sw = new System.IO.StringWriter();
            Console.SetOut(sw);
            
            var status = solver.Solve(model);
            
            // Console çıktısını geri yükle ve log dosyasına yaz
            Console.SetOut(originalOut);
            string solverLog = sw.ToString();
            
            if (!string.IsNullOrWhiteSpace(solverLog))
            {
                App.LogToDisk($"\n--- CP-SAT DETAYLI MOTOR LOGU BAŞLANGIÇ ---\n{solverLog}\n--- MOTOR LOGU BİTİŞ ---\n");
            }

            App.LogToDisk($"[EditEngine] Solver sonuç: {status}, Çözüm süresi: {solver.WallTime()} sn, Amaç: {solver.ObjectiveValue}");

            if (status == CpSolverStatus.Optimal || status == CpSolverStatus.Feasible)
            {
                // ===== H. SONUÇ ÇIKARMA =====
                var changes = new List<ScheduleEditService.BlockChange>();

                foreach (var b in allPlacedBlocks)
                {
                    if (!originalPositions.TryGetValue(b.Id, out var origPos)) continue;

                    int newDay = origPos.day;
                    int newHour = origPos.hour;

                    foreach (var move in x[b.Id])
                    {
                        if (solver.BooleanValue(move.bv))
                        {
                            newDay = move.d;
                            newHour = move.h;
                            break;
                        }
                    }

                    if (newDay != origPos.day || newHour != origPos.hour)
                    {
                        string className = classes.TryGetValue(b.ClassId, out var c) ? c.Name : "";
                        string[] dayNames = { "", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz" };
                        
                        string oldPos = $"{dayNames[origPos.day]} {origPos.hour}.saat";
                        string newPos = $"{dayNames[newDay]} {newHour}.saat";

                        changes.Add(new ScheduleEditService.BlockChange
                        {
                            BlockId = b.Id,
                            OldDay = origPos.day,
                            OldHour = origPos.hour,
                            NewDay = newDay,
                            NewHour = newHour,
                            Description = $"{b.LessonCode} {className}: {oldPos} → {newPos}"
                        });
                    }
                }

                result.Success = true;
                result.Changes = changes;
                result.RemainingConflicts = 0;

                int movedCount = changes.Count(c => c.BlockId != sourceBlock.Id);
                result.Message = changes.Count == 0
                    ? "Blok zaten bu konumda."
                    : movedCount == 0
                        ? $"✓ Blok başarıyla taşındı ({status})."
                        : $"✓ Blok taşındı, {movedCount} ek blok yeniden yerleştirildi ({status}).";

                return result;
            }

            // Çözüm bulunamadı — detaylı mesaj
            result.Success = false;
            string statusMsg = status switch
            {
                CpSolverStatus.Infeasible => "ÇÖZÜMSÜZ (Infeasible)",
                CpSolverStatus.ModelInvalid => "MODEL GEÇERSİZ",
                _ => $"ZAMAN AŞIMI ({status})"
            };

            string infMsg = $"[EditEngine] Solver sonuç: {statusMsg}.\n";
            infMsg += "- Orijinal dağıtımda (V3) hedeflenen öğretmen, sınıf veya mekan zaten manuel yerleştirilmiş olabilir.\n";
            infMsg += "- Serbest havuzda yer alan dersleri dağıtmak için boş saat veya esnetilebilir bir zincir bulunamamış olabilir.\n";
            infMsg += $"- maxDays: {maxDays}, maxHours: {maxHours}. \n";
            infMsg += $"- Sisteme giren kilitli_blok_sayisi: {allPlacedBlocks.Count(b => b.IsLocked)}, serbest_blok_sayisi: {movableIds.Count}\n";
            App.LogToDisk(infMsg);

            result.Message = $"Bu taşıma yapılamadı: {statusMsg}\n" +
                             "Model kısıtları birbiriyle çakışıyor, yeterli boş esneklik bulunamadı.";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Hata: {ex.Message}\n{ex.StackTrace?.Substring(0, Math.Min(ex.StackTrace?.Length ?? 0, 300))}";
            App.LogToDisk($"[EditEngine] EXCEPTION: {ex}");
            return result;
        }
    }

    // ===== ETKİLENEN BLOKLARI BUL (FREE ENGINE) =====

    /// <summary>
    /// Kaynak bloğun hedef pozisyona taşınmasından etkilenecek blokları bulur.
    /// FREE ENGINE: Kilitli olmayan tüm blokları taşınabilir (movable) kabul eder.
    /// Sadece kaynak bloğu sabitlemez, diğer tüm bloklara esneklik hakkı tanır.
    /// </summary>
    private static HashSet<int> FindMovableBlocks(
        DistributionBlock sourceBlock,
        int targetDay, int targetHour,
        List<DistributionBlock> allBlocks,
        int maxDays, int maxHours)
    {
        var movableIds = new HashSet<int>();

        foreach (var other in allBlocks)
        {
            // Kaynak blok hedef konuma özel olarak sabitlendiği için serbest havuzdan çıkar.
            // Kilitli (IsLocked) bloklar da serbest kalmamalı.
            if (other.Id == sourceBlock.Id || other.IsLocked) continue;

            // Free Engine mantığı: Kalan tüm bloklar serbestçe (cezalı da olsa) hareket edebilir.
            movableIds.Add(other.Id);
        }

        return movableIds;
    }

    private static bool HoursOverlap(int hourA, int durA, int hourB, int durB)
    {
        return hourA < hourB + durB && hourB < hourA + durA;
    }

    // ===== YARDIMCI METODLAR =====

    /// <summary>
    /// Gerçek KAPALI kontrolü — V3'teki IsStaticAvailable'ın düzeltilmiş versiyonu.
    /// TeacherRepository ALL non-empty cells'ı Closed olarak işaretler.
    /// Gerçekten KAPALI olan: Constraints=Closed VE ScheduleInfo'da olmayan.
    /// </summary>
    private static bool IsEditSlotAvailable(
        DistributionBlock block, int day, int hour,
        Dictionary<int, Teacher> teachers,
        Dictionary<int, SchoolClass> classes,
        int maxHours)
    {
        for (int i = 0; i < block.BlockDuration; i++)
        {
            int h = hour + i;
            if (h > maxHours) return false;

            // Öğretmen gerçek KAPALI kontrolü
            foreach (var tid in block.TeacherIds)
            {
                if (teachers.TryGetValue(tid, out var teacher))
                {
                    var slot = new TimeSlot(day, h);
                    if (teacher.Constraints.TryGetValue(slot, out var state) && state == SlotState.Closed
                        && !teacher.ScheduleInfo.ContainsKey(slot))
                        return false;
                }
            }

            // Sınıf gerçek KAPALI kontrolü
            if (classes.TryGetValue(block.ClassId, out var cls))
            {
                var slot = new TimeSlot(day, h);
                if (cls.Constraints.TryGetValue(slot, out var state) && state == SlotState.Closed)
                {
                    var key = $"d_{day}_{h}";
                    if (cls.Schedule.TryGetValue(key, out var val))
                    {
                        if (val == "KAPALI" || string.IsNullOrEmpty(val))
                            return false;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            
            // Mekan gerçek KAPALI kontrolü
            // (Mekan modeli nasıl tanımlandıysa o şekilde, ama Dictionary üzerinden gidiyoruz)
        }

        return true;
    }

    /// <summary>
    /// Herhangi bir slot gerçek KAPALI mı? (ön doğrulama için)
    /// </summary>
    private static bool IsAnySlotTrulyClosed(
        DistributionBlock block, int day, int hour,
        Dictionary<int, Teacher> teachers,
        Dictionary<int, SchoolClass> classes,
        SchoolInfo schoolInfo)
    {
        for (int i = 0; i < block.BlockDuration; i++)
        {
            int h = hour + i;
            var slot = new TimeSlot(day, h);

            if (schoolInfo != null && schoolInfo.DefaultTimetable.TryGetValue(slot, out var sState) && sState == SlotState.Closed)
            {
                return true;
            }

            foreach (var tid in block.TeacherIds)
            {
                if (teachers.TryGetValue(tid, out var teacher))
                {
                    bool isClosed = teacher.Constraints.TryGetValue(slot, out var state) && state == SlotState.Closed && !teacher.ScheduleInfo.ContainsKey(slot);
                    bool isKapaliText = teacher.ScheduleInfo.TryGetValue(slot, out var tText) && tText.Equals("KAPALI", StringComparison.OrdinalIgnoreCase);

                    if (isClosed || isKapaliText)
                        return true;
                }
            }

            if (classes.TryGetValue(block.ClassId, out var cls))
            {
                var key = $"d_{day}_{h}";
                if (cls.Schedule.TryGetValue(key, out var val))
                {
                    if (val.Equals("KAPALI", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else
                {
                    if (cls.Constraints.TryGetValue(slot, out var cState) && cState == SlotState.Closed)
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Hedef hücrede halihazırda "Kilitli (IsLocked)" bir ders varsa
    /// CP-SAT bu dersi itemeyeceği için model Infeasible döner.
    /// Bunu önceden tespit edip UI'a anlamlı mesaj döndürür.
    /// </summary>
    private static string GetLockedConflictAtTarget(
        DistributionBlock sourceBlock, int targetDay, int targetHour,
        List<DistributionBlock> allBlocks)
    {
        for (int i = 0; i < sourceBlock.BlockDuration; i++)
        {
            int checkHour = targetHour + i;

            foreach (var other in allBlocks)
            {
                if (other.Id == sourceBlock.Id || !other.IsLocked) continue;

                if (other.Day == targetDay && HoursOverlap(checkHour, 1, other.Hour, other.BlockDuration))
                {
                    // Saat ve gün çakışıyor, kilitli ders sınıf veya öğretmenle çakışıyor mu?
                    if (sourceBlock.ClassId == other.ClassId)
                        return $"Hedef konumda kilitli olan '{other.LessonCode}' dersi var, taşınamaz.";

                    if (sourceBlock.TeacherIds.Intersect(other.TeacherIds).Any())
                        return $"Hedef konumdaki '{other.LessonCode}' dersinin öğretmeni kilitli.";

                    var soRooms = sourceBlock.GetOrtakMekanIds();
                    var otRooms = other.GetOrtakMekanIds();
                    if (soRooms.Count > 0 && soRooms.Intersect(otRooms).Any())
                        return $"Hedef konumdaki '{other.LessonCode}' dersinin ortak mekanı kilitli.";
                }
            }
        }
        return null;
    }
}
