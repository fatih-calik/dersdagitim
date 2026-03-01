using System;
using System.Collections.Generic;
using System.Linq;
using DersDagitim.Models;

namespace DersDagitim.Persistence;

public class TeacherRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;

    public List<Teacher> GetAll()
    {
        var sql = "SELECT * FROM ogretmen ORDER BY ad_soyad";
        var results = _db.Query(sql);
        
        return results.Select(row => MapTeacher(row)).ToList();
    }
    
    public List<Teacher> Search(string query)
    {
        var escapedQuery = DatabaseManager.Escape(query);
        var sql = $"SELECT * FROM ogretmen WHERE ad_soyad LIKE '%{escapedQuery}%' OR tc_kimlik_no LIKE '%{escapedQuery}%' ORDER BY ad_soyad";
        
        var results = _db.Query(sql);
        return results.Select(row => MapTeacher(row)).ToList();
    }
    
    public Teacher? GetById(int id)
    {
        var sql = $"SELECT * FROM ogretmen WHERE id = {id}";
        var results = _db.Query(sql);
        return results.Count > 0 ? MapTeacher(results[0]) : null;
    }
    
    private Teacher MapTeacher(Dictionary<string, object?> row)
    {
        var t = new Teacher
        {
            Id = DatabaseManager.GetInt(row, "id"),
            Name = DatabaseManager.GetString(row, "ad_soyad"),
            TcNo = DatabaseManager.GetString(row, "tc_kimlik_no"),
            Position = DatabaseManager.GetString(row, "gorevi"),
            DutyDay = DatabaseManager.GetString(row, "nobet_gunu"),
            DutyLocation = DatabaseManager.GetString(row, "nobet_yeri"),
            DutyLocationName = DatabaseManager.GetString(row, "nobet_yeri"), // Map UI helper
            Club = DatabaseManager.GetString(row, "egitsel_klub"),
            Guidance = DatabaseManager.GetInt(row, "rehberlik"),
            MaxHours = DatabaseManager.GetInt(row, "max_ders"),
            MaxHoursPerDay = DatabaseManager.GetInt(row, "max_gunluk_ders", 8),
            HasExtraLessons = DatabaseManager.GetInt(row, "ekders_durumu") == 1,
            TotalAssignedHours = DatabaseManager.GetInt(row, "verilen_ders_saati"),
            Branch = DatabaseManager.GetString(row, "brans")
        };
        
        LoadScheduleAndConstraints(t, row);
        LoadEkDers(t, row);
        
        return t;
    }
    
    private void LoadEkDers(Teacher t, Dictionary<string, object?> row)
    {
        t.EkDersGunduz101 = GetEkDersArray(row, "101gunduz");
        t.EkDersGece102 = GetEkDersArray(row, "102gece");
        t.EkDersFazlaGunduz103 = GetEkDersArray(row, "103fazlagunduz");
        t.EkDersFazlaGece104 = GetEkDersArray(row, "104fazlagece");
        t.EkDersBelleticilik106 = GetEkDersArray(row, "106belleticilik");
        t.EkDersSinav107 = GetEkDersArray(row, "107sinav");
        t.EkDersEgzersiz108 = GetEkDersArray(row, "108egzersiz");
        t.EkDersHizmetIci109 = GetEkDersArray(row, "109hizmetici");
        t.EkDersEDYGG110 = GetEkDersArray(row, "110edygg");
        t.EkDersEDYGGGece111 = GetEkDersArray(row, "111edygggece");
        t.EkDersEDYGGFazlaGunduz112 = GetEkDersArray(row, "112edyggfazlagunduz");
        t.EkDersEDYGGFazlaGece113 = GetEkDersArray(row, "113edyggfazlagece");
        t.EkDersAtis114 = GetEkDersArray(row, "114atis");
        t.EkDersCezaevi115 = GetEkDersArray(row, "115cezaevi");
        t.EkDersTakviye116 = GetEkDersArray(row, "116takviye");
        t.EkDersTakviyeGece117 = GetEkDersArray(row, "117takviyegece");
        t.EkDersBelleticiFazla118 = GetEkDersArray(row, "118belleticifazla");
        t.EkDersNobet119 = GetEkDersArray(row, "119nobet");
    }

    private int[] GetEkDersArray(Dictionary<string, object?> row, string prefix)
    {
        string[] days = { "pazartesi", "sali", "carsamba", "persembe", "cuma", "cumartesi", "pazar" };
        var arr = new int[7];
        for(int i=0; i<7; i++)
        {
            arr[i] = DatabaseManager.GetInt(row, $"{prefix}_{days[i]}");
        }
        return arr;
    }

    private void LoadScheduleAndConstraints(Teacher t, Dictionary<string, object?> row)
    {
        for (int d = 1; d <= 7; d++)
        {
            for (int h = 1; h <= 12; h++)
            {
                var key = $"d_{d}_{h}";
                var val = DatabaseManager.GetString(row, key);
                var slot = new TimeSlot(d, h);
                
                if (!string.IsNullOrEmpty(val))
                {
                    if (val == "KAPALI")
                    {
                        t.Constraints[slot] = SlotState.Closed;
                    }
                    else
                    {
                        t.ScheduleInfo[slot] = val;
                        // NEW: Treat ANY text as a constraint for the distributor
                        // This prevents double booking when lessons are kept or manually added
                        t.Constraints[slot] = SlotState.Closed;
                    }
                }
            }
        }
    }
    
    public void Save(Teacher teacher)
    {
        var name = DatabaseManager.Escape(teacher.Name);
        var tc = DatabaseManager.Escape(teacher.TcNo);
        var position = DatabaseManager.Escape(teacher.Position);
        var day = DatabaseManager.Escape(teacher.DutyDay);
        var loc = DatabaseManager.Escape(teacher.DutyLocation);
        var club = DatabaseManager.Escape(teacher.Club);
        var branch = DatabaseManager.Escape(teacher.Branch);
        var extra = teacher.HasExtraLessons ? 1 : 0;
        
        if (teacher.Id == 0)
        {
            // NEW: Initialize constraints from school map if it's a new teacher
            var schoolRepo = new SchoolRepository();
            var schoolInfo = schoolRepo.GetSchoolInfo();
            if (schoolInfo?.DefaultTimetable != null)
            {
                foreach (var kvp in schoolInfo.DefaultTimetable)
                {
                    // If teacher constraints don't already have this slot, apply school default
                    if (!teacher.Constraints.ContainsKey(kvp.Key))
                    {
                        teacher.Constraints[kvp.Key] = kvp.Value;
                    }
                }
            }

            var sql = $@"
                INSERT INTO ogretmen (ad_soyad, tc_kimlik_no, gorevi, brans, nobet_gunu, nobet_yeri, egitsel_klub, rehberlik, max_ders, max_gunluk_ders, ekders_durumu, verilen_ders_saati)
                VALUES ('{name}', '{tc}', '{position}', '{branch}', '{day}', '{loc}', '{club}', {teacher.Guidance}, {teacher.MaxHours}, {teacher.MaxHoursPerDay}, {extra}, {teacher.TotalAssignedHours})";
            
            _db.Execute(sql);
            
            var idRows = _db.Query("SELECT last_insert_rowid() as id");
            if (idRows.Count > 0)
            {
                teacher.Id = DatabaseManager.GetInt(idRows[0], "id");
            }
        }
        else
        {
            var sql = $@"
                UPDATE ogretmen SET 
                ad_soyad='{name}', tc_kimlik_no='{tc}', gorevi='{position}', brans='{branch}', nobet_gunu='{day}', nobet_yeri='{loc}', egitsel_klub='{club}', 
                rehberlik={teacher.Guidance}, max_ders={teacher.MaxHours}, max_gunluk_ders={teacher.MaxHoursPerDay}, ekders_durumu={extra}, verilen_ders_saati={teacher.TotalAssignedHours}
                WHERE id={teacher.Id}";
            
            _db.Execute(sql);
        }
        
        SaveConstraints(teacher);
        SaveEkDers(teacher);
    }
    
    public void Delete(int id)
    {
        // Cascading delete
        try { _db.Execute($"DELETE FROM atama WHERE ogretmen_id = {id}"); } catch {}
        try {
            _db.Execute($@"DELETE FROM dagitim_bloklari
                WHERE ogretmen_1_id = {id} OR ogretmen_2_id = {id}
                   OR ogretmen_3_id = {id} OR ogretmen_4_id = {id}
                   OR ogretmen_5_id = {id} OR ogretmen_6_id = {id}
                   OR ogretmen_7_id = {id}");
        } catch {}
        _db.Execute($"DELETE FROM ogretmen WHERE id = {id}");
    }
    
    public void SaveConstraints(Teacher teacher)
    {
        if (teacher.Id == 0) return;
        
        var sets = new List<string>();
        
        for (int d = 1; d <= 7; d++)
        {
            for (int h = 1; h <= 12; h++)
            {
                var ts = new TimeSlot(d, h);
                var isClosed = teacher.Constraints.ContainsKey(ts) && teacher.Constraints[ts] == SlotState.Closed;
                string val = "";
                
                // CRITICAL FIX: Prioritize actual lesson content if present
                if (teacher.ScheduleInfo.ContainsKey(ts) && !string.IsNullOrEmpty(teacher.ScheduleInfo[ts]))
                {
                    val = teacher.ScheduleInfo[ts];
                }
                else if (isClosed)
                {
                    val = "KAPALI";
                }
                
                sets.Add($"d_{d}_{h} = '{DatabaseManager.Escape(val)}'");
            }
        }
        
        var sql = $"UPDATE ogretmen SET {string.Join(", ", sets)} WHERE id = {teacher.Id}";
        _db.Execute(sql);
    }

    public void SaveEkDers(Teacher teacher)
    {
        if (teacher.Id == 0) return;

        var updates = new List<string>();
        string[] days = { "pazartesi", "sali", "carsamba", "persembe", "cuma", "cumartesi", "pazar" };

        void AddUpdates(string prefix, int[] values)
        {
             for(int i=0; i<7; i++)
             {
                 // Important: Quote the column name because it starts with a number (e.g. "101gunduz_pazartesi")
                 updates.Add($"\"{prefix}_{days[i]}\" = {values[i]}");
             }
        }

        AddUpdates("101gunduz", teacher.EkDersGunduz101);
        AddUpdates("102gece", teacher.EkDersGece102);
        AddUpdates("103fazlagunduz", teacher.EkDersFazlaGunduz103);
        AddUpdates("104fazlagece", teacher.EkDersFazlaGece104);
        AddUpdates("106belleticilik", teacher.EkDersBelleticilik106);
        AddUpdates("107sinav", teacher.EkDersSinav107);
        AddUpdates("108egzersiz", teacher.EkDersEgzersiz108);
        AddUpdates("109hizmetici", teacher.EkDersHizmetIci109);
        AddUpdates("110edygg", teacher.EkDersEDYGG110);
        AddUpdates("111edygggece", teacher.EkDersEDYGGGece111);
        AddUpdates("112edyggfazlagunduz", teacher.EkDersEDYGGFazlaGunduz112);
        AddUpdates("113edyggfazlagece", teacher.EkDersEDYGGFazlaGece113);
        AddUpdates("114atis", teacher.EkDersAtis114);
        AddUpdates("115cezaevi", teacher.EkDersCezaevi115);
        AddUpdates("116takviye", teacher.EkDersTakviye116);
        AddUpdates("117takviyegece", teacher.EkDersTakviyeGece117);
        AddUpdates("118belleticifazla", teacher.EkDersBelleticiFazla118);
        AddUpdates("119nobet", teacher.EkDersNobet119);

        if (updates.Count > 0)
        {
            var sql = $"UPDATE ogretmen SET {string.Join(", ", updates)} WHERE id = {teacher.Id}";
            _db.Execute(sql);
        }
    }
    
    public void UpdateTotalHours(int teacherId)
    {
         var sql = $@"
            UPDATE ogretmen SET verilen_ders_saati = (
                SELECT IFNULL(SUM(blok_suresi), 0) FROM dagitim_bloklari
                WHERE ogretmen_1_id = {teacherId}
                   OR ogretmen_2_id = {teacherId}
                   OR ogretmen_3_id = {teacherId}
                   OR ogretmen_4_id = {teacherId}
                   OR ogretmen_5_id = {teacherId}
                   OR ogretmen_6_id = {teacherId}
                   OR ogretmen_7_id = {teacherId}
            ) WHERE id = {teacherId}";
         _db.Execute(sql);
    }

    public void SyncAllTeacherHours()
    {
        var teachers = GetAll();
        foreach (var t in teachers)
        {
            UpdateTotalHours(t.Id);
        }
    }
    
    public List<TeacherAssignmentInfo> GetAssignments(int teacherId)
    {
        var sql = $@"
            SELECT s.ad AS sinif_adi, d.kod AS ders_kodu, d.ad AS ders_adi, sd.toplam_saat, d.varsayilan_blok
            FROM atama a
            JOIN sinif_ders sd ON a.sinif_ders_id = sd.id
            JOIN sinif s ON sd.sinif_id = s.id
            JOIN ders d ON sd.ders_id = d.id
            WHERE a.ogretmen_id = {teacherId}
            ORDER BY s.ad, d.kod";
            
        var rows = _db.Query(sql);
        return rows.Select(r => 
        {
            string def = DatabaseManager.GetString(r, "varsayilan_blok");
            int total = DatabaseManager.GetInt(r, "toplam_saat");
            string pattern = CalculatePattern(def, total);
            
            return new TeacherAssignmentInfo 
            {
                ClassName = DatabaseManager.GetString(r, "sinif_adi"),
                LessonName = $"{DatabaseManager.GetString(r, "ders_adi")} {DatabaseManager.GetString(r, "ders_kodu")} {pattern}",
                LessonFullName = DatabaseManager.GetString(r, "ders_adi"),
                TotalHours = total
            };
        }).ToList();
    }
    
    public List<TeacherAssignmentDetail> GetAssignmentsWithId(int teacherId)
    {
        var sql = $@"
            SELECT a.id AS atama_id, a.sinif_ders_id, s.ad AS sinif_adi,
                   d.ad AS ders_adi, d.kod AS ders_kodu, sd.toplam_saat, d.varsayilan_blok
            FROM atama a
            JOIN sinif_ders sd ON a.sinif_ders_id = sd.id
            JOIN sinif s ON sd.sinif_id = s.id
            JOIN ders d ON sd.ders_id = d.id
            WHERE a.ogretmen_id = {teacherId}
            ORDER BY s.ad, d.kod";

        var rows = _db.Query(sql);
        return rows.Select(r =>
        {
            string def = DatabaseManager.GetString(r, "varsayilan_blok");
            int total = DatabaseManager.GetInt(r, "toplam_saat");
            string pattern = CalculatePattern(def, total);

            return new TeacherAssignmentDetail
            {
                AssignmentId = DatabaseManager.GetInt(r, "atama_id"),
                ClassLessonId = DatabaseManager.GetInt(r, "sinif_ders_id"),
                ClassName = DatabaseManager.GetString(r, "sinif_adi"),
                LessonName = DatabaseManager.GetString(r, "ders_adi"),
                LessonCode = DatabaseManager.GetString(r, "ders_kodu"),
                BlockPattern = pattern,
                TotalHours = total
            };
        }).ToList();
    }

    private string CalculatePattern(string defaultBlock, int totalHours)
    {
        if (string.IsNullOrEmpty(defaultBlock)) defaultBlock = "2";
        
        var parts = defaultBlock.Split('+').Select(s => int.TryParse(s, out int n) ? n : 1).ToList();
        if (parts.Count == 0 || parts.All(p => p == 0)) parts = new List<int> { 2 };
        
        var pattern = new List<int>();
        int currentTotal = 0;
        int partIndex = 0;
        
        while (currentTotal < totalHours)
        {
            int next = parts[partIndex % parts.Count];
            if (currentTotal + next > totalHours)
            {
                next = totalHours - currentTotal;
            }
            if (next > 0)
            {
                pattern.Add(next);
                currentTotal += next;
            }
            else
            {
                 // Avoid infinite loop if next is 0 (though we filtered p==0 above)
                 currentTotal++; 
            }
            partIndex++;
        }
        
        return string.Join("+", pattern);
    }
}

public class TeacherAssignmentInfo
{
    public string ClassName { get; set; } = "";
    public string LessonName { get; set; } = "";
    public string LessonFullName { get; set; } = "";
    public int TotalHours { get; set; }
}

public class TeacherAssignmentDetail
{
    public int AssignmentId { get; set; }
    public int ClassLessonId { get; set; }
    public string ClassName { get; set; } = "";
    public string LessonName { get; set; } = "";
    public string LessonCode { get; set; } = "";
    public string BlockPattern { get; set; } = "";
    public int TotalHours { get; set; }
}
