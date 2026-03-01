using DersDagitim.Models;
using DersDagitim.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DersDagitim.Services
{
    public class ValidationResults
    {
        public double AssignmentCompleteness { get; set; } // Dağıtım Yüzdesi (Programın ne kadarı bitti?)
        public double TeacherAvailability { get; set; }   // Öğretmenlerin uygunluk durumu
        public double ResourceBalance { get; set; }      // Kaynak (Öğretmen/Sınıf) dengesi
        public Dictionary<int, Dictionary<int, string>> ResourceBalanceGrid { get; set; } = new Dictionary<int, Dictionary<int, string>>();
        public double ScheduleFeasibility { get; set; }   // Genel uygulanabilirlik

        public string AssignmentDetails { get; set; } = "";
        public string TeacherDetails { get; set; } = "";
        public string ResourceDetails { get; set; } = "";
        public string ScheduleDetails { get; set; } = "";

        public List<string> AssignmentIssues { get; set; } = new List<string>();
        public List<string> TeacherIssues { get; set; } = new List<string>();
        public List<string> ResourceIssues { get; set; } = new List<string>();
        public List<string> ScheduleIssues { get; set; } = new List<string>();
        public List<string> CriticalConflicts { get; set; } = new List<string>();

        public int TotalLessons { get; set; }
        public int AssignedLessons { get; set; }
        public int UnassignedLessons { get; set; }
        public int TotalTeachers { get; set; }
        public int TotalClasses { get; set; }

        public bool CanProceed => AssignmentCompleteness >= 100 &&
                                   TeacherAvailability >= 50 &&
                                   ResourceBalance >= 50 &&
                                   ScheduleFeasibility >= 50;
    }

    public class DataValidatorService
    {
        private readonly DatabaseManager _db = DatabaseManager.Shared;
        private readonly TeacherRepository _teacherRepo = new TeacherRepository();
        private readonly ClassRepository _classRepo = new ClassRepository();
        private readonly SchoolRepository _schoolRepo = new SchoolRepository();

        public ValidationResults Validate()
        {
            var info = _schoolRepo.GetSchoolInfo();
            int schoolDays = info.Days > 0 ? info.Days : 5;

            // --- VERİ TOPLAMA (SADECE OKUMA) ---
            var allClassLessons = FetchAllClassLessons();
            var allAssignments = FetchAllAssignments();
            var allBlocks = FetchAllBlocks();
            var allTeachers = _teacherRepo.GetAll().ToDictionary(t => t.Id);
            var allClasses = _classRepo.GetAll().ToDictionary(c => c.Id);

            // --- 1. DAĞITIM VE YERLEŞİM KONTROLÜ ---
            var assignmentCheck = CheckDistributionStatus(allClassLessons, allAssignments, allBlocks, allClasses);

            // --- 2. ÖĞRETMEN VE KISIT KONTROLÜ ---
            var teacherCheck = CheckTeacherConstraints(allTeachers, allBlocks, schoolDays);

            // --- 3. KAYNAK VE ÇAKIŞMA KONTROLÜ ---
            var resourceCheck = CheckResourceHeatmap(allTeachers, allClasses, allBlocks, info.DefaultTimetable, schoolDays, info.DailyLessonCount);

            // --- 4. PROGRAM UYGULANABİLİRLİĞİ ---
            var scheduleCheck = CheckFeasibility(allClassLessons, allTeachers, allClasses, info.DefaultTimetable, schoolDays);

            var results = new ValidationResults
            {
                AssignmentCompleteness = assignmentCheck.Percentage,
                TeacherAvailability = teacherCheck.Percentage,
                ResourceBalance = resourceCheck.Percentage,
                ResourceBalanceGrid = resourceCheck.Grid,
                ScheduleFeasibility = scheduleCheck.Percentage,

                AssignmentDetails = assignmentCheck.Details,
                TeacherDetails = teacherCheck.Details,
                ResourceDetails = resourceCheck.Details,
                ScheduleDetails = scheduleCheck.Details,

                AssignmentIssues = assignmentCheck.Issues,
                TeacherIssues = teacherCheck.Issues,
                ResourceIssues = resourceCheck.Issues,
                ScheduleIssues = scheduleCheck.Issues,

                TotalLessons = assignmentCheck.TotalHours,
                AssignedLessons = assignmentCheck.PlacedHours,
                UnassignedLessons = assignmentCheck.TotalHours - assignmentCheck.PlacedHours,
                TotalTeachers = allTeachers.Count,
                TotalClasses = allClasses.Count
            };

            // --- 5. TEŞHİS VE KRİTİK ANALİZ (Diagnostic) ---
            var diagnosticIssues = new List<string>();
            
            // A. Kardeş Ders Çıkmazı Analizi
            var combinedIssues = CheckCombinedLessonConflicts(allBlocks, allTeachers, allClasses, info.DefaultTimetable, schoolDays, info.DailyLessonCount);
            diagnosticIssues.AddRange(combinedIssues);

            // B. Mükerrer Kayıt Kontrolü
            var duplicateIssues = CheckDuplicateLessons(allClassLessons, allClasses);
            diagnosticIssues.AddRange(duplicateIssues);

            // C. Statik Erişilebilirlik Analizi (Her dersin en az 1 yeri var mı?)
            var staticIssues = CheckStaticConsistency(allBlocks, allTeachers, allClasses, info.DefaultTimetable, schoolDays, info.DailyLessonCount);
            diagnosticIssues.AddRange(staticIssues);

            // D. Mekan (Room) Darboğaz Analizi
            var roomIssues = CheckRoomBottlenecks(allBlocks, schoolDays, info.DailyLessonCount, info.DefaultTimetable);
            diagnosticIssues.AddRange(roomIssues);

            // E. Öğretmen Zaman Penceresi Analizi
            var teacherTimeIssues = CheckTeacherTimeWindows(allTeachers, allBlocks, schoolDays, info.DailyLessonCount, info.DefaultTimetable);
            diagnosticIssues.AddRange(teacherTimeIssues);

            if (diagnosticIssues.Count > 0)
            {
                results.CriticalConflicts.AddRange(diagnosticIssues);
                results.ScheduleFeasibility = Math.Max(10, results.ScheduleFeasibility - (diagnosticIssues.Count * 25));
                results.ScheduleDetails = $"{diagnosticIssues.Count} adet çözümü imkansız kılan kritik çakışma bulundu!";
            }

            return results;
        }

        private (double Percentage, string Details, List<string> Issues, int TotalHours, int PlacedHours) CheckDistributionStatus(
            List<ClassLesson> lessons, List<TeacherAssignment> assignments, List<DistributionBlock> blocks, Dictionary<int, SchoolClass> classes)
        {
            var issues = new List<string>();
            int totalNeeded = lessons.Sum(l => l.TotalHours);
            int actuallyPlaced = blocks.Where(b => b.Day > 0).Sum(b => b.BlockDuration);

            foreach (var lesson in lessons)
            {
                string className = classes.ContainsKey(lesson.ClassId) ? classes[lesson.ClassId].Name : $"Sınıf #{lesson.ClassId}";
                string lessonName = GetLessonName(lesson.LessonId);
                
                // Atama var mı?
                var lessonAssignments = assignments.Where(a => a.ClassLessonId == lesson.Id).ToList();
                int assignedHours = lessonAssignments.Sum(a => a.AssignedHours);

                if (assignedHours < lesson.TotalHours)
                    issues.Add($"{className} - {lessonName}: Atama eksik ({assignedHours}/{lesson.TotalHours} saat)");
                else if (assignedHours > lesson.TotalHours)
                    issues.Add($"[FAZLA ATAMA] {className} - {lessonName}: {assignedHours} saat atanmış ama ders {lesson.TotalHours} saat!");
            }

            double percentage = totalNeeded > 0 ? (double)actuallyPlaced / totalNeeded * 100 : 100;
            string details = $"Haftalık toplam {totalNeeded} saatin {actuallyPlaced} saati programa yerleştirildi.";

            return (percentage, details, issues, totalNeeded, actuallyPlaced);
        }

        private (double Percentage, string Details, List<string> Issues) CheckTeacherConstraints(
            Dictionary<int, Teacher> teachers, List<DistributionBlock> blocks, int schoolDays)
        {
            var info = _schoolRepo.GetSchoolInfo();
            int dailyLessonCount = info.DailyLessonCount > 0 ? info.DailyLessonCount : 8;

            var issues = new List<string>();
            int violations = 0;
            int totalTeachers = teachers.Count;

            foreach (var tPair in teachers)
            {
                var teacher = tPair.Value;
                var teacherBlocks = blocks.Where(b => b.TeacherIds.Contains(teacher.Id) && b.Day > 0).ToList();

                // 1. Kısıt İhlali (KAPALI saate ders yerleşmiş mi?)
                foreach (var block in teacherBlocks)
                {
                    for (int i = 0; i < block.BlockDuration; i++)
                    {
                        var slot = new TimeSlot(block.Day, block.Hour + i);
                        if (teacher.Constraints.TryGetValue(slot, out var state) && state == SlotState.Closed)
                        {
                            issues.Add($"{teacher.Name}: {slot.Day}. gün {slot.Hour}. saat KAPALI olmasına rağmen ders yerleşmiş!");
                            violations++;
                        }
                    }
                }

                // 2. Günlük Maksimum Ders Kontrolü
                for (int d = 1; d <= schoolDays; d++)
                {
                    int dailySum = teacherBlocks.Where(b => b.Day == d).Sum(b => b.BlockDuration);
                    if (dailySum > teacher.MaxHoursPerDay)
                        issues.Add($"{teacher.Name}: {d}. gün toplam {dailySum} saat dersi var (Maksimum: {teacher.MaxHoursPerDay})");
                }
            }

            // 3. Sınıf Günlük Maksimum Ders Kontrolü
            var placedBlocks = blocks.Where(b => b.Day > 0).ToList();
            var classGroups = placedBlocks.GroupBy(b => b.ClassId);
            var allClasses = _classRepo.GetAll().ToDictionary(c => c.Id);

            foreach (var cg in classGroups)
            {
                string className = allClasses.ContainsKey(cg.Key) ? allClasses[cg.Key].Name : $"Sınıf #{cg.Key}";
                for (int d = 1; d <= schoolDays; d++)
                {
                    int dailySum = cg.Where(b => b.Day == d).Sum(b => b.BlockDuration);
                    if (dailySum > dailyLessonCount)
                    {
                        issues.Add($"{className}: {d}. gün toplam {dailySum} saat ders var (Okul günlük max: {dailyLessonCount})");
                        violations++;
                    }
                }
            }

            double percentage = totalTeachers > 0 ? Math.Max(0, 100 - (violations * 10)) : 100;
            string details = violations == 0 ? "Kısıt ihlali bulunamadı." : $"{violations} adet kısıt ihlali tespit edildi!";

            return (percentage, details, issues);
        }

        private (double Percentage, string Details, List<string> Issues, Dictionary<int, Dictionary<int, string>> Grid) CheckResourceHeatmap(
            Dictionary<int, Teacher> teachers, Dictionary<int, SchoolClass> classes, List<DistributionBlock> blocks,
            Dictionary<TimeSlot, SlotState> schoolConstraints, int schoolDays, int dailyLessonCount)
        {
            var issues = new List<string>();
            var grid = new Dictionary<int, Dictionary<int, string>>();
            int conflicts = 0;
            int totalClassCount = classes.Count;

            // En büyük ders saatini belirle (Okul sabiti, atılmış bloklar veya kısıtlardaki en büyük değer)
            int maxHourInBlocks = blocks.Any() ? blocks.Max(b => b.Hour + b.BlockDuration - 1) : 0;
            int maxHourInConstraints = schoolConstraints.Where(kv => kv.Value == SlotState.Open).Any()
                ? schoolConstraints.Where(kv => kv.Value == SlotState.Open).Max(kv => kv.Key.Hour)
                : 0;

            int actualMaxHour = Math.Max(dailyLessonCount, Math.Max(maxHourInBlocks, maxHourInConstraints));
            if (actualMaxHour < 1) actualMaxHour = 8; // Default fallback
            if (actualMaxHour > 12) actualMaxHour = 12; // Database limit

            // Oda isimlerini önceden çek (eşzamanlı oda çakışma kontrolü için)
            var roomNames = _db.Query("SELECT id, ad FROM ortak_mekan").ToDictionary(
                row => DatabaseManager.GetInt(row, "id"),
                row => DatabaseManager.GetString(row, "ad")
            );

            // Eşzamanlı oda kullanım haritası: (roomId, day, hour) -> List<blockId>
            var roomOccupancy = new Dictionary<(int roomId, int day, int hour), List<int>>();
            foreach (var b in blocks.Where(b => b.Day > 0))
            {
                foreach (var rid in b.GetOrtakMekanIds())
                {
                    for (int i = 0; i < b.BlockDuration; i++)
                    {
                        int h = b.Hour + i;
                        if (h > 12) continue;
                        var key = (rid, b.Day, h);
                        if (!roomOccupancy.ContainsKey(key)) roomOccupancy[key] = new List<int>();
                        roomOccupancy[key].Add(b.Id);
                    }
                }
            }

            for (int d = 1; d <= schoolDays; d++)
            {
                var dayMap = new Dictionary<int, string>();
                for (int h = 1; h <= actualMaxHour; h++)
                {
                    var slot = new TimeSlot(d, h);

                    // Okul bu saatte kapalı mı?
                    if (schoolConstraints.TryGetValue(slot, out var schoolState) && schoolState == SlotState.Closed)
                    {
                        dayMap[h] = "-"; // Kapalı saatleri "-" ile işaretle
                        continue;
                    }

                    // 1. O saatte çalışabilir (Açık) olan öğretmen sayısı
                    int availableTeachers = teachers.Values.Count(t => !t.Constraints.ContainsKey(slot) || t.Constraints[slot] != SlotState.Closed);

                    // 2. O saatte ataması olan ders/sınıf sayısı (Blok tabanlı)
                    int activeClassesCount = blocks.Count(b => b.Day == d && h >= b.Hour && h < b.Hour + b.BlockDuration);

                    // 3. Boş olan öğretmen sayısı
                    int freeTeachers = availableTeachers - activeClassesCount;

                    // Grid Gösterimi: "Boş Öğr / Toplam Sınıf"
                    dayMap[h] = $"{freeTeachers}/{totalClassCount}";

                    if (activeClassesCount > availableTeachers)
                    {
                        conflicts++;
                        if (conflicts < 5) issues.Add($"{d}. gün {h}. saatte öğretmen yetersizliği! (Ders: {activeClassesCount}, Müsait Öğr: {availableTeachers})");
                    }
                }
                grid[d] = dayMap;
            }

            // 4. Eşzamanlı Oda Çakışma Kontrolü
            int roomConflicts = 0;
            foreach (var kvp in roomOccupancy.Where(x => x.Value.Distinct().Count() > 1))
            {
                var distinctBlocks = kvp.Value.Distinct().ToList();
                string roomName = roomNames.ContainsKey(kvp.Key.roomId) ? roomNames[kvp.Key.roomId] : $"Oda #{kvp.Key.roomId}";
                issues.Add($"[ODA ÇAKIŞMASI] {roomName}: {kvp.Key.day}. gün {kvp.Key.hour}. saatte {distinctBlocks.Count} ders aynı anda! (Bloklar: {string.Join(",", distinctBlocks)})");
                roomConflicts++;
                conflicts++;
            }

            string details = conflicts == 0
                ? $"Kaynak dengesi uygun (Sınıf: {totalClassCount}, Öğretmen: {teachers.Count})."
                : $"{conflicts} zaman diliminde kapasite aşımı riski var.";

            return (Math.Max(0, 100 - conflicts * 5), details, issues, grid);
        }

        private (double Percentage, string Details, List<string> Issues) CheckFeasibility(
            List<ClassLesson> lessons, Dictionary<int, Teacher> teachers, Dictionary<int, SchoolClass> classes, 
            Dictionary<TimeSlot, SlotState> schoolConstraints, int schoolDays)
        {
            // 1. Toplam Ders Yükü
            int totalHours = lessons.Sum(l => l.TotalHours);

            // 2. Gerçek Okul Kapasitesi (Sınıf Sayısı * Okulun Açık Olduğu Toplam Saat)
            // Eğer okul kısıtları boşsa varsayılan 40 saati (5 gün * 8 saat) baz alalım.
            int openSlotsCount = schoolConstraints.Count(kv => kv.Value == SlotState.Open);
            if (openSlotsCount == 0) openSlotsCount = schoolDays * 8; 

            int totalCapacity = classes.Count * openSlotsCount;

            if (totalHours > totalCapacity)
            {
                return (40, $"Ders yükü ({totalHours} saat), okul kapasitesinden ({totalCapacity} saat) fazla!", 
                    new List<string> { $"Okul kapasitesi yetersiz. {totalHours - totalCapacity} saatlik ders dışarıda kalıyor." });
            }

            string status = totalHours == totalCapacity ? "Tam Doluluk" : "Uygun";
            string details = $"Toplam {totalHours} ders saati, {totalCapacity} saatlik kapasiteye tam uyumlu ({status}).";
            
            return (100, details, new List<string>());
        }

        private List<string> CheckDuplicateLessons(List<ClassLesson> lessons, Dictionary<int, SchoolClass> classes)
        {
            var issues = new List<string>();
            var duplicates = lessons.GroupBy(l => new { l.ClassId, l.LessonId })
                                   .Where(g => g.Count() > 1);

            foreach (var group in duplicates)
            {
                string className = classes.ContainsKey(group.Key.ClassId) ? classes[group.Key.ClassId].Name : group.Key.ClassId.ToString();
                string lessonName = GetLessonName(group.Key.LessonId);
                issues.Add($"[MÜKERRER] {className} sınıfında '{lessonName}' dersi {group.Count()} kez tanımlanmış! (Bu blok sayısını gereksiz yere artırır)");
            }
            return issues;
        }

        private List<string> CheckCombinedLessonConflicts(
            List<DistributionBlock> blocks, Dictionary<int, Teacher> teachers, Dictionary<int, SchoolClass> classes,
            Dictionary<TimeSlot, SlotState> schoolConstraints, int schoolDays, int dailyLessonCount)
        {
            var issues = new List<string>();
            var groups = blocks.Where(b => b.KardesId > 0).GroupBy(b => b.KardesId);

            foreach (var grp in groups)
            {
                var grpBlocks = grp.ToList();
                int gid = grp.Key;
                int duration = grpBlocks[0].BlockDuration; // Assuming all blocks in a combined group have the same duration
                int commonSlots = 0;

                for (int d = 1; d <= schoolDays; d++)
                {
                    for (int h = 1; h <= dailyLessonCount - duration + 1; h++)
                    {
                        bool allOk = true;
                        foreach (var b in grpBlocks)
                        {
                            if (!IsStaticAvailable(b, d, h, teachers, classes, schoolConstraints, duration))
                            {
                                allOk = false;
                                break;
                            }
                        }
                        if (allOk) commonSlots++;
                    }
                }

                if (commonSlots == 0)
                {
                    // Assuming LessonCode is available in DistributionBlock or can be derived
                    // For now, using a placeholder if not directly available from DB fetch
                    string lessonName = grpBlocks[0].LessonCode ?? $"Ders #{grpBlocks[0].ClassLessonId}"; 
                    var classNames = string.Join(", ", grpBlocks.Select(b => classes.ContainsKey(b.ClassId) ? classes[b.ClassId].Name : b.ClassId.ToString()));
                    issues.Add($"[KRİTİK] '{lessonName}' Kardeş Grubu (#{gid}) için ortak boş vakit yok! (Sınıflar: {classNames})");
                }
            }
            return issues;
        }

        private List<string> CheckStaticConsistency(
            List<DistributionBlock> blocks, Dictionary<int, Teacher> teachers, Dictionary<int, SchoolClass> classes,
            Dictionary<TimeSlot, SlotState> schoolConstraints, int schoolDays, int dailyLessonCount)
        {
            var issues = new List<string>();

            foreach (var b in blocks)
            {
                if (b.Day > 0) continue; // Already placed

                int validSlots = 0;
                for (int d = 1; d <= schoolDays; d++)
                {
                    for (int h = 1; h <= dailyLessonCount - b.BlockDuration + 1; h++)
                    {
                        if (IsStaticAvailable(b, d, h, teachers, classes, schoolConstraints, b.BlockDuration))
                            validSlots++;
                    }
                }

                if (validSlots == 0)
                {
                    string className = classes.ContainsKey(b.ClassId) ? classes[b.ClassId].Name : b.ClassId.ToString();
                    string lessonName = b.LessonCode ?? $"Ders #{b.ClassLessonId}"; // Placeholder if LessonCode not directly available
                    issues.Add($"[KRİTİK] {className} - {lessonName} dersi için kısıtlar yüzünden yerleşebilecek HİÇBİR saat yok!");
                }
            }
            return issues;
        }

        private List<string> CheckTeacherTimeWindows(Dictionary<int, Teacher> teachers, List<DistributionBlock> blocks, int schoolDays, int dailyLessonCount, Dictionary<TimeSlot, SlotState> schoolConstraints)
        {
            var issues = new List<string>();
            var teacherHours = new Dictionary<int, int>();

            foreach (var b in blocks)
            {
                foreach (var tid in b.TeacherIds)
                    teacherHours[tid] = teacherHours.GetValueOrDefault(tid) + b.BlockDuration;
            }

            foreach (var kvp in teacherHours)
            {
                int tid = kvp.Key;
                int hoursNeeded = kvp.Value;

                if (teachers.TryGetValue(tid, out var t))
                {
                    int openSlots = 0;
                    for (int d = 1; d <= schoolDays; d++)
                    {
                        for (int h = 1; h <= dailyLessonCount; h++)
                        {
                            var slot = new TimeSlot(d, h);
                            bool schoolOpen = !schoolConstraints.TryGetValue(slot, out var sState) || sState == SlotState.Open;
                            bool teacherOpen = !t.Constraints.TryGetValue(slot, out var tState) || tState == SlotState.Open;
                            if (schoolOpen && teacherOpen) openSlots++;
                        }
                    }

                    if (hoursNeeded > openSlots)
                    {
                        issues.Add($"[KRİTİK] {t.Name} için yeterli açık saat yok! (Ders: {hoursNeeded} saat, Uygun Boşluk: {openSlots} saat)");
                    }
                    else if (hoursNeeded > openSlots * 0.9)
                    {
                        issues.Add($"[UYARI] {t.Name} öğretmeni çok sıkışık (%90+ doluluk). Kısıtlarını (kapalı saatlerini) esnetmeniz gerekebilir.");
                    }
                }
            }

            return issues;
        }

        private List<string> CheckRoomBottlenecks(List<DistributionBlock> blocks, int schoolDays, int dailyLessonCount, Dictionary<TimeSlot, SlotState> schoolConstraints)
        {
            var issues = new List<string>();
            var roomHours = new Dictionary<int, int>();

            foreach (var b in blocks)
            {
                foreach (var rid in b.GetOrtakMekanIds())
                {
                    roomHours[rid] = roomHours.GetValueOrDefault(rid) + b.BlockDuration;
                }
            }

            var rooms = _db.Query("SELECT id, ad FROM ortak_mekan").ToDictionary(
                row => DatabaseManager.GetInt(row, "id"),
                row => DatabaseManager.GetString(row, "ad")
            );

            // Calculate total open slots in school
            int openSchoolSlots = 0;
            for (int d = 1; d <= schoolDays; d++)
                for (int h = 1; h <= dailyLessonCount; h++)
                    if (!schoolConstraints.TryGetValue(new TimeSlot(d, h), out var state) || state == SlotState.Open)
                        openSchoolSlots++;

            foreach (var kvp in roomHours)
            {
                int rid = kvp.Key;
                int hours = kvp.Value;
                if (rooms.TryGetValue(rid, out var roomName))
                {
                    if (hours > openSchoolSlots)
                    {
                        issues.Add($"[KRİTİK] {roomName} mekanı kapasitesinden fazla ders içeriyor! (Ders: {hours} saat, Kapasite: {openSchoolSlots} saat)");
                    }
                    else if (hours > openSchoolSlots * 0.9)
                    {
                        issues.Add($"[UYARI] {roomName} mekanı %90 dolulukta. Bu dağıtımı zorlaştırabilir.");
                    }
                }
            }

            return issues;
        }

        private bool IsStaticAvailable(DistributionBlock b, int d, int h, 
            Dictionary<int, Teacher> teachers, Dictionary<int, SchoolClass> classes, Dictionary<TimeSlot, SlotState> schoolConstraints, int duration)
        {
            for (int i = 0; i < duration; i++)
            {
                var slot = new TimeSlot(d, h + i);

                // School
                if (schoolConstraints.TryGetValue(slot, out var sState) && sState == SlotState.Closed) return false;

                // Class
                if (classes.TryGetValue(b.ClassId, out var cls) && cls.Constraints.TryGetValue(slot, out var cState) && cState == SlotState.Closed) return false;

                // Teachers
                foreach (var tid in b.TeacherIds)
                {
                    if (teachers.TryGetValue(tid, out var t) && t.Constraints.TryGetValue(slot, out var tState) && tState == SlotState.Closed) return false;
                }
            }
            return true;
        }

        // --- YARDIMCI SORGULAR (SADECE SELECT) ---

        private List<ClassLesson> FetchAllClassLessons()
        {
            return _db.Query("SELECT * FROM sinif_ders").Select(row => new ClassLesson {
                Id = DatabaseManager.GetInt(row, "id"),
                ClassId = DatabaseManager.GetInt(row, "sinif_id"),
                LessonId = DatabaseManager.GetInt(row, "ders_id"),
                TotalHours = DatabaseManager.GetInt(row, "toplam_saat"),
                KardesId = DatabaseManager.GetInt(row, "kardes_id")
            }).ToList();
        }

        private List<TeacherAssignment> FetchAllAssignments()
        {
            return _db.Query("SELECT * FROM atama").Select(row => new TeacherAssignment {
                Id = DatabaseManager.GetInt(row, "id"),
                ClassLessonId = DatabaseManager.GetInt(row, "sinif_ders_id"),
                TeacherId = DatabaseManager.GetInt(row, "ogretmen_id"),
                AssignedHours = DatabaseManager.GetInt(row, "atanan_saat")
            }).ToList();
        }

        private List<DistributionBlock> FetchAllBlocks()
        {
            return _db.Query("SELECT * FROM dagitim_bloklari").Select(row => new DistributionBlock {
                Id = DatabaseManager.GetInt(row, "id"),
                ClassLessonId = DatabaseManager.GetInt(row, "sinif_ders_id"),
                ClassId = DatabaseManager.GetInt(row, "sinif_id"),
                BlockDuration = DatabaseManager.GetInt(row, "blok_suresi"),
                Day = DatabaseManager.GetInt(row, "gun"),
                Hour = DatabaseManager.GetInt(row, "saat"),
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
                KardesId = DatabaseManager.GetInt(row, "kardes_id"),
                LessonCode = DatabaseManager.GetString(row, "ders_kodu")
            }).ToList();
        }

        private string GetLessonName(int id)
        {
            var res = _db.Query($"SELECT ad FROM ders WHERE id = {id}");
            return res.Count > 0 ? DatabaseManager.GetString(res[0], "ad") : $"Ders #{id}";
        }
    }
}
