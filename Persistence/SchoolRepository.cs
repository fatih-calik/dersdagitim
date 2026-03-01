using DersDagitim.Models;
using System.Collections.Generic;

namespace DersDagitim.Persistence;

/// <summary>
/// School repository for database operations
/// </summary>
public class SchoolRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;
    
    public SchoolInfo GetSchoolInfo()
    {
        var results = _db.Query("SELECT * FROM okul LIMIT 1");
        var info = new SchoolInfo
        {
            Days = 5,
            DailyLessonCount = 8,
            LessonHours = new string[12]
        };
        
        if (results.Count > 0)
        {
            var row = results[0];
            info.Name = DatabaseManager.GetString(row, "ad");
            info.Principal = DatabaseManager.GetString(row, "mudur");
            info.Date = DatabaseManager.GetString(row, "baslangic_tarihi");
            
            var days = DatabaseManager.GetInt(row, "gun_sayisi");
            if (days > 0) info.Days = days;
            
            var lessons = DatabaseManager.GetInt(row, "ders_sayisi");
            if (lessons > 0) info.DailyLessonCount = lessons;
            
            info.Schedule = GetSchedule(row);
            info.DefaultTimetable = ConvertScheduleToTimetable(info.Schedule);
            
            info.Version = row.ContainsKey("versiyon") ? DatabaseManager.GetString(row, "versiyon") : "1.0.0.0";
            info.LastUpdateDate = row.ContainsKey("son_guncelleme") ? DatabaseManager.GetString(row, "son_guncelleme") : "";
            info.V3GapPenalty = row.ContainsKey("v3_gap_penalty") ? DatabaseManager.GetInt(row, "v3_gap_penalty") : 100;

            for (int i = 1; i <= 12; i++)
            {
                info.LessonHours[i - 1] = DatabaseManager.GetString(row, $"saat{i}");
            }
        }
        else 
        {
             // Create empty/default schedule
             info.Schedule = new Dictionary<string, string>();
             info.DefaultTimetable = new Dictionary<TimeSlot, SlotState>();
        }
        
        return info;
    }
    
    public void SaveSchoolInfo(SchoolInfo info)
    {
        var escapedName = DatabaseManager.Escape(info.Name);
        var escapedPrincipal = DatabaseManager.Escape(info.Principal);
        var escapedDate = DatabaseManager.Escape(info.Date);
        
        var check = _db.Query("SELECT count(*) as count FROM okul");
        var count = check.Count > 0 ? DatabaseManager.GetInt(check[0], "count") : 0;
        
        if (count == 0)
        {
             _db.Execute($"""
                INSERT INTO okul (id, ad, mudur, baslangic_tarihi, gun_sayisi, ders_sayisi, versiyon, son_guncelleme, v3_gap_penalty) 
                VALUES (1, '{escapedName}', '{escapedPrincipal}', '{escapedDate}', {info.Days}, {info.DailyLessonCount}, '{DatabaseManager.Escape(info.Version)}', '{DatabaseManager.Escape(info.LastUpdateDate)}', {info.V3GapPenalty})
            """);
        }
        else
        {
            _db.Execute($"""
                UPDATE okul SET
                ad = '{escapedName}',
                mudur = '{escapedPrincipal}',
                baslangic_tarihi = '{escapedDate}',
                gun_sayisi = {info.Days},
                ders_sayisi = {info.DailyLessonCount},
                versiyon = '{DatabaseManager.Escape(info.Version)}',
                son_guncelleme = '{DatabaseManager.Escape(info.LastUpdateDate)}',
                v3_gap_penalty = {info.V3GapPenalty}
                WHERE id = 1
            """);
        }
        
        SaveSchoolTimetable(info.DefaultTimetable);
        SaveLessonHours(info.LessonHours);
    }
    
    public void UpdateSchoolName(string name)
    {
        var escapedName = DatabaseManager.Escape(name);
        _db.Execute($"UPDATE okul SET ad = '{escapedName}'");
    }
    
    public string GetLicenseCode()
    {
        var results = _db.Query("SELECT ls FROM okul LIMIT 1");
        return results.Count > 0 ? DatabaseManager.GetString(results[0], "ls") : "";
    }
    
    public void SaveLicenseCode(string code)
    {
        var check = _db.Query("SELECT COUNT(*) as count FROM okul");
        var count = check.Count > 0 ? DatabaseManager.GetInt(check[0], "count") : 0;
        
        var escapedCode = DatabaseManager.Escape(code);
        
        if (count == 0)
        {
            _db.Execute($"INSERT INTO okul (id, ls) VALUES (1, '{escapedCode}')");
        }
        else
        {
            _db.Execute($"UPDATE okul SET ls = '{escapedCode}'");
        }
    }

    private Dictionary<TimeSlot, SlotState> ConvertScheduleToTimetable(Dictionary<string, string> schedule)
    {
        var table = new Dictionary<TimeSlot, SlotState>();
        foreach (var kvp in schedule)
        {
            var parts = kvp.Key.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int day) && int.TryParse(parts[2], out int hour))
            {
                 var state = kvp.Value == "KAPALI" ? SlotState.Closed : SlotState.Open;
                 table[new TimeSlot(day, hour)] = state;
            }
        }
        return table;
    }
    
    private void SaveSchoolTimetable(Dictionary<TimeSlot, SlotState> table)
    {
        if (table.Count == 0) return;
        
        var updates = new List<string>();
        foreach (var (slot, state) in table)
        {
            var key = $"d_{slot.Day}_{slot.Hour}";
            var value = state == SlotState.Closed ? "KAPALI" : "ACIK";
            updates.Add($"{key} = '{value}'");
        }
        
        if (updates.Count > 0)
        {
            _db.Execute($"UPDATE okul SET {string.Join(", ", updates)}");
        }
    }
    
    private static Dictionary<string, string> GetSchedule(Dictionary<string, object?> row)
    {
        var schedule = new Dictionary<string, string>();
        for (int day = 1; day <= 7; day++)
        {
            for (int hour = 1; hour <= 12; hour++)
            {
                var key = $"d_{day}_{hour}";
                schedule[key] = DatabaseManager.GetString(row, key);
            }
        }
        return schedule;
    }
    
    private void SaveLessonHours(string[] hours)
    {
        var updates = new List<string>();
        for (int i = 0; i < hours.Length && i < 12; i++)
        {
            var escaped = DatabaseManager.Escape(hours[i] ?? "");
            updates.Add($"saat{i + 1} = '{escaped}'");
        }
        
        if (updates.Count > 0)
        {
            _db.Execute($"UPDATE okul SET {string.Join(", ", updates)}");
        }
    }
}
