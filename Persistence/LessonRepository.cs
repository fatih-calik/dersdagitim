using DersDagitim.Models;

namespace DersDagitim.Persistence;

/// <summary>
/// Lesson repository
/// </summary>
public class LessonRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;
    
    public List<Lesson> GetAll()
    {
        var sql = @"
            SELECT d.* 
            FROM ders d
            ORDER BY d.ad";
            
        var results = _db.Query(sql);
        return results.Select(row => new Lesson
        {
            Id = DatabaseManager.GetInt(row, "id"),
            Code = DatabaseManager.GetString(row, "kod"),
            Name = DatabaseManager.GetString(row, "ad"),
            DefaultBlock = DatabaseManager.GetString(row, "varsayilan_blok", "2"),
            MorningPriority = DatabaseManager.GetInt(row, "sabah_onceligi")
        }).ToList();
    }
    
    public Lesson? GetById(int id)
    {
        var sql = $"SELECT * FROM ders WHERE id = {id}";
        var results = _db.Query(sql);
        if (results.Count == 0) return null;
        
        var row = results[0];
        return new Lesson
        {
            Id = DatabaseManager.GetInt(row, "id"),
            Code = DatabaseManager.GetString(row, "kod"),
            Name = DatabaseManager.GetString(row, "ad"),
            DefaultBlock = DatabaseManager.GetString(row, "varsayilan_blok", "2"),
            MorningPriority = DatabaseManager.GetInt(row, "sabah_onceligi")
        };
    }
    
    public void Save(Lesson lesson)
    {
        var escapedCode = DatabaseManager.Escape(lesson.Code);
        var escapedName = DatabaseManager.Escape(lesson.Name);
        var escapedBlock = DatabaseManager.Escape(lesson.DefaultBlock);
        
        if (lesson.Id == 0)
        {
            _db.Execute($"""
                INSERT INTO ders (kod, ad, varsayilan_blok, sabah_onceligi) 
                VALUES ('{escapedCode}', '{escapedName}', '{escapedBlock}', {lesson.MorningPriority})
            """);
        }
        else
        {
            _db.Execute($"""
                UPDATE ders SET 
                kod = '{escapedCode}', 
                ad = '{escapedName}', 
                varsayilan_blok = '{escapedBlock}', 
                sabah_onceligi = {lesson.MorningPriority}
                WHERE id = {lesson.Id}
            """);
        }
    }
    
    public void Delete(int id)
    {
        // 1. Find all ClassLessons associated with this lesson
        var clList = _db.Query($"SELECT id FROM sinif_ders WHERE ders_id = {id}");
        if (clList.Count > 0)
        {
            // Gather IDs
            var clIds = clList.Select(r => DatabaseManager.GetInt(r, "id")).ToList();
            var idsStr = string.Join(",", clIds);
            
            // 2. Delete Distribution Blocks (dagitim_bloklari)
            _db.Execute($"DELETE FROM dagitim_bloklari WHERE sinif_ders_id IN ({idsStr})");
            
            // 3. Delete Assignments (atama)
            _db.Execute($"DELETE FROM atama WHERE sinif_ders_id IN ({idsStr})");
            
            // 4. Delete ClassLessons (sinif_ders)
            _db.Execute($"DELETE FROM sinif_ders WHERE id IN ({idsStr})");
        }
        
        // 5. Delete the Lesson Definition
        _db.Execute($"DELETE FROM ders WHERE id = {id}");
        
        // 6. Sync Teacher Hours (since assignments might have been deleted)
        new TeacherRepository().SyncAllTeacherHours();
    }
    
    public List<Lesson> Search(string query)
    {
        var escapedQuery = DatabaseManager.Escape(query);
        var sql = $@"
            SELECT d.* 
            FROM ders d
            WHERE d.ad LIKE '%{escapedQuery}%' OR d.kod LIKE '%{escapedQuery}%'
            ORDER BY d.ad";
            
        var results = _db.Query(sql);
        return results.Select(row => new Lesson
        {
            Id = DatabaseManager.GetInt(row, "id"),
            Code = DatabaseManager.GetString(row, "kod"),
            Name = DatabaseManager.GetString(row, "ad"),
            DefaultBlock = DatabaseManager.GetString(row, "varsayilan_blok", "2"),
            MorningPriority = DatabaseManager.GetInt(row, "sabah_onceligi")
        }).ToList();
    }
}
