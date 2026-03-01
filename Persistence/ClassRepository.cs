using DersDagitim.Models;

namespace DersDagitim.Persistence;

/// <summary>
/// Class repository for database operations
/// </summary>
public class ClassRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;
    
    public List<SchoolClass> GetAll()
    {
        var results = _db.Query("SELECT * FROM sinif ORDER BY ad");
        return results.Select(row => MapToClass(row)).ToList();
    }
    
    public SchoolClass? GetById(int id)
    {
        var results = _db.Query($"SELECT * FROM sinif WHERE id = {id}");
        return results.Count > 0 ? MapToClass(results[0]) : null;
    }
    
    public void Save(SchoolClass schoolClass)
    {
        // NEW: If it's a new class (not yet in DB) or has no constraints, initialize from school default
        var check = _db.Query($"SELECT id FROM sinif WHERE id = {schoolClass.Id}");
        if (check.Count == 0 && schoolClass.Constraints.Count == 0)
        {
            var schoolRepo = new SchoolRepository();
            var schoolInfo = schoolRepo.GetSchoolInfo();
            if (schoolInfo?.DefaultTimetable != null)
            {
                foreach (var kvp in schoolInfo.DefaultTimetable)
                {
                    schoolClass.Constraints[kvp.Key] = kvp.Value;
                }
            }
        }

        var escapedName = DatabaseManager.Escape(schoolClass.Name);
        _db.Execute($"INSERT OR REPLACE INTO sinif (id, ad) VALUES ({schoolClass.Id}, '{escapedName}')");
        
        SaveConstraints(schoolClass.Id, schoolClass.Constraints);
        SaveSchedule(schoolClass.Id, schoolClass.Schedule);
    }
    
    public void Delete(int id)
    {
        // 1. Get all class lesson IDs for this class
        var classLessons = _db.Query($"SELECT id FROM sinif_ders WHERE sinif_id = {id}");
        var classLessonIds = classLessons.Select(row => DatabaseManager.GetInt(row, "id")).ToList();
        
        if (classLessonIds.Any())
        {
            var idsStr = string.Join(",", classLessonIds);
            
            // 2. Delete distribution blocks
            _db.Execute($"DELETE FROM dagitim_bloklari WHERE sinif_ders_id IN ({idsStr})");
            
            // 3. Delete teacher assignments
            _db.Execute($"DELETE FROM atama WHERE sinif_ders_id IN ({idsStr})");
            
            // 4. Delete class lessons
            _db.Execute($"DELETE FROM sinif_ders WHERE sinif_id = {id}");
        }
        
        // 5. Delete time constraints
        _db.Execute($"DELETE FROM zaman_tablosu WHERE tip = 'sinif' AND ref_id = {id}");
        
        // 6. Finally delete the class
        _db.Execute($"DELETE FROM sinif WHERE id = {id}");
    }
    
    private SchoolClass MapToClass(Dictionary<string, object?> row)
    {
        var id = DatabaseManager.GetInt(row, "id");
        var name = DatabaseManager.GetString(row, "ad");
        var constraints = GetConstraints(id);
        var schedule = GetSchedule(row);

        // MERGE: Treat ANY text in the class schedule as a constraint
        foreach (var kvp in schedule)
        {
            if (!string.IsNullOrEmpty(kvp.Value))
            {
                // Key format is "d_X_Y"
                var parts = kvp.Key.Split('_');
                if (parts.Length == 3 && int.TryParse(parts[1], out int d) && int.TryParse(parts[2], out int h))
                {
                    constraints[new TimeSlot(d, h)] = SlotState.Closed;
                }
            }
        }
        
        return new SchoolClass
        {
            Id = id,
            Name = name,
            Constraints = constraints,
            Schedule = schedule
        };
    }
    
    private Dictionary<TimeSlot, SlotState> GetConstraints(int classId)
    {
        var results = _db.Query($"SELECT * FROM zaman_tablosu WHERE tip = 'sinif' AND ref_id = {classId}");
        var constraints = new Dictionary<TimeSlot, SlotState>();
        
        foreach (var row in results)
        {
            var day = DatabaseManager.GetInt(row, "gun", 1);
            var hour = DatabaseManager.GetInt(row, "saat", 1);
            var status = DatabaseManager.GetString(row, "durum", "ACIK");
            
            var slot = new TimeSlot(day, hour);
            constraints[slot] = status.Equals("CLOSED", StringComparison.OrdinalIgnoreCase) || status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ? SlotState.Closed : SlotState.Open;
        }
        
        return constraints;
    }
    
    public void SaveConstraints(int classId, Dictionary<TimeSlot, SlotState> constraints)
    {
        _db.Execute($"DELETE FROM zaman_tablosu WHERE tip = 'sinif' AND ref_id = {classId}");
        
        var updates = new List<string>();
        
        foreach (var (slot, state) in constraints)
        {
            _db.Execute($"""
                INSERT INTO zaman_tablosu (tip, ref_id, gun, saat, durum) 
                VALUES ('sinif', {classId}, {slot.Day}, {slot.Hour}, '{(state == SlotState.Closed ? "CLOSED" : "OPEN")}')
            """);
            
            var col = $"d_{slot.Day}_{slot.Hour}";
            var val = state == SlotState.Closed ? "KAPALI" : "";
            updates.Add($"{col} = '{val}'");
        }
        
        // Clear all existing "KAPALI" values first to ensure removed constraints are reflected
        var clearSql = new List<string>();
        for (int d = 1; d <= 7; d++)
            for (int h = 1; h <= 12; h++)
                clearSql.Add($"d_{d}_{h} = CASE WHEN d_{d}_{h} = 'KAPALI' THEN '' ELSE d_{d}_{h} END");
        
        _db.Execute($"UPDATE sinif SET {string.Join(", ", clearSql)} WHERE id = {classId}");
        
        if (updates.Count > 0)
        {
            _db.Execute($"UPDATE sinif SET {string.Join(", ", updates)} WHERE id = {classId}");
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
    
    private void SaveSchedule(int classId, Dictionary<string, string> schedule)
    {
        if (schedule.Count == 0) return;
        
        var updates = new List<string>();
        for (int day = 1; day <= 7; day++)
        {
            for (int hour = 1; hour <= 12; hour++)
            {
                var key = $"d_{day}_{hour}";
                if (schedule.TryGetValue(key, out var value))
                {
                    var escaped = DatabaseManager.Escape(value);
                    updates.Add($"{key} = '{escaped}'");
                }
            }
        }
        
        if (updates.Count > 0)
        {
            _db.Execute($"UPDATE sinif SET {string.Join(", ", updates)} WHERE id = {classId}");
        }
    }
}
