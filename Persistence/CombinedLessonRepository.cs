using System.Collections.Generic;
using System.Linq;
using DersDagitim.Models;

namespace DersDagitim.Persistence;

/// <summary>
/// Repository for Unified Lessons (Kardeş Dersler)
/// </summary>
public class CombinedLessonRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;

    // --- GRUPLAR ---
    public List<KardesGrup> GetAllGroups()
    {
        var rows = _db.Query("SELECT * FROM kardes_gruplar ORDER BY id DESC");
        return rows.Select(r => new KardesGrup 
        { 
            Id = DatabaseManager.GetInt(r, "id"), 
            Ad = DatabaseManager.GetString(r, "ad") 
        }).ToList();
    }

    public int CreateGroup(string name)
    {
        _db.Execute($"INSERT INTO kardes_gruplar (ad) VALUES ('{DatabaseManager.Escape(name)}')");
        var res = _db.Query("SELECT last_insert_rowid() as id");
        if (res.Count > 0) return DatabaseManager.GetInt(res[0], "id");
        return 0;
    }

    public void DeleteGroup(int groupId)
    {
        // 1. Reset dagitim_bloklari.kardes_id for all lessons in this group
        // First get lessons in this group
        var lessons = _db.Query($"SELECT sinif_ders_id FROM kardes_bloklar WHERE kardes_id = {groupId}");
        if (lessons.Count > 0)
        {
            var ids = string.Join(",", lessons.Select(l => DatabaseManager.GetInt(l, "sinif_ders_id")));
            _db.Execute($"UPDATE dagitim_bloklari SET kardes_id = 0 WHERE sinif_ders_id IN ({ids})");
        }

        // 2. Delete relationships
        _db.Execute($"DELETE FROM kardes_bloklar WHERE kardes_id = {groupId}");

        // 3. Delete group
        _db.Execute($"DELETE FROM kardes_gruplar WHERE id = {groupId}");
    }
    
    public void UpdateGroupName(int groupId, string name)
    {
        _db.Execute($"UPDATE kardes_gruplar SET ad = '{DatabaseManager.Escape(name)}' WHERE id = {groupId}");
    }

    // --- GRUP İÇERİĞİ ---
    public List<CombinedLessonView> GetGroupContent(int groupId)
    {
        var sql = $@"
            SELECT kb.id, kb.sinif_ders_id, s.ad as sinif_adi, d.kod as ders_kodu, d.ad as ders_adi, sd.toplam_saat
            FROM kardes_bloklar kb
            JOIN sinif_ders sd ON kb.sinif_ders_id = sd.id
            JOIN sinif s ON sd.sinif_id = s.id
            JOIN ders d ON sd.ders_id = d.id
            WHERE kb.kardes_id = {groupId}
            ORDER BY s.ad";

        var rows = _db.Query(sql);
        return rows.Select(r => new CombinedLessonView
        {
            Id = DatabaseManager.GetInt(r, "id"), // Relationship ID
            ClassLessonId = DatabaseManager.GetInt(r, "sinif_ders_id"),
            ClassName = DatabaseManager.GetString(r, "sinif_adi"),
            LessonCode = DatabaseManager.GetString(r, "ders_kodu"),
            LessonName = DatabaseManager.GetString(r, "ders_adi"),
            TotalHours = DatabaseManager.GetInt(r, "toplam_saat")
        }).ToList();
    }

    public void AddLessonToGroup(int groupId, int classLessonId)
    {
        // Check duplication
        var check = _db.Query($"SELECT id FROM kardes_bloklar WHERE kardes_id = {groupId} AND sinif_ders_id = {classLessonId}");
        if (check.Count > 0) return;
        
        // Also check if lesson belongs to ANOTHER group? Ideally yes, a lesson can't be in two groups
        // If so, remove from old group first
        var oldGroup = _db.Query($"SELECT id FROM kardes_bloklar WHERE sinif_ders_id = {classLessonId}");
        if (oldGroup.Count > 0)
        {
            // Remove from old
             _db.Execute($"DELETE FROM kardes_bloklar WHERE sinif_ders_id = {classLessonId}");
        }

        // Get sinif_id and ders_id for completeness (though sinif_ders_id is enough)
        var detail = _db.Query($"SELECT sinif_id, ders_id FROM sinif_ders WHERE id = {classLessonId}");
        int sId = 0, dId = 0;
        if (detail.Count > 0)
        {
            sId = DatabaseManager.GetInt(detail[0], "sinif_id");
            dId = DatabaseManager.GetInt(detail[0], "ders_id");
        }

        _db.Execute($"INSERT INTO kardes_bloklar (kardes_id, sinif_id, ders_id, sinif_ders_id) VALUES ({groupId}, {sId}, {dId}, {classLessonId})");

        // INSTANT UPDATE: Update dagitim_bloklari
        _db.Execute($"UPDATE dagitim_bloklari SET kardes_id = {groupId} WHERE sinif_ders_id = {classLessonId}");
    }

    public void RemoveLessonFromGroup(int relationshipId)
    {
        // 1. Get class_lesson_id to clean up dagitim
        var rel = _db.Query($"SELECT sinif_ders_id FROM kardes_bloklar WHERE id = {relationshipId}");
        if (rel.Count == 0) return;
        int cld = DatabaseManager.GetInt(rel[0], "sinif_ders_id");

        // 2. Clean dagitim
        _db.Execute($"UPDATE dagitim_bloklari SET kardes_id = 0 WHERE sinif_ders_id = {cld}");

        // 3. Delete
        _db.Execute($"DELETE FROM kardes_bloklar WHERE id = {relationshipId}");
    }
}

public class KardesGrup { public int Id { get; set; } public string Ad { get; set; } }
public class CombinedLessonView 
{ 
    public int Id { get; set; } // Relationship ID for deletion
    public int ClassLessonId { get; set; }
    public string ClassName { get; set; } 
    public string LessonCode { get; set; }
    public string LessonName { get; set; }
    public int TotalHours { get; set; }
    
    // UI Helper
    public string DisplayText => $"{ClassName} - {LessonName} ({TotalHours} Saat)";
}
