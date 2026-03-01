using System;
using System.Collections.Generic;
using DersDagitim.Models;

namespace DersDagitim.Persistence;

public class DistributionRepository
{
    private readonly DatabaseManager _db;
    
    public DistributionRepository()
    {
        _db = DatabaseManager.Shared;
    }
    
    public List<DistributionBlock> GetAllBlocks()
    {
        return FetchBlocks("SELECT * FROM dagitim_bloklari");
    }
    
    public List<DistributionBlock> GetByTeacher(int teacherId)
    {
        // Check all 5 columns
        string sql = @$"SELECT * FROM dagitim_bloklari 
                        WHERE ogretmen_1_id = {teacherId} 
                           OR ogretmen_2_id = {teacherId}
                           OR ogretmen_3_id = {teacherId}
                           OR ogretmen_4_id = {teacherId}
                           OR ogretmen_5_id = {teacherId}
                           OR ogretmen_6_id = {teacherId}
                           OR ogretmen_7_id = {teacherId}";
                           
        return FetchBlocks(sql);
    }
    
    public List<DistributionBlock> FetchBlocks(string query)
    {
        var rows = _db.Query(query);
        var list = new List<DistributionBlock>();
        
        foreach (var row in rows)
        {
            var b = new DistributionBlock
            {
                Id = DatabaseManager.GetInt(row, "id"),
                ClassLessonId = DatabaseManager.GetInt(row, "sinif_ders_id"),
                LessonCode = DatabaseManager.GetString(row, "ders_kodu"),
                ClassId = DatabaseManager.GetInt(row, "sinif_id"),
                BlockDuration = DatabaseManager.GetInt(row, "blok_suresi"),
                Day = DatabaseManager.GetInt(row, "gun"),
                Hour = DatabaseManager.GetInt(row, "saat"),
                PlacementType = DatabaseManager.GetString(row, "yerlesim_tipi", "otomatik"),
                IsLocked = DatabaseManager.GetInt(row, "kilitli") == 1,
                IsManual = DatabaseManager.GetInt(row, "manuel") == 1,
                MorningPriority = DatabaseManager.GetDouble(row, "sabah_onceligi"),
                TeacherGapScore = DatabaseManager.GetInt(row, "ogretmen_bosluk_skoru"),
                ClassGapScore = DatabaseManager.GetInt(row, "sinif_bosluk_skoru"),
                TotalScore = DatabaseManager.GetInt(row, "toplam_skor"),
                // OrtakMekanId is derived from OrtakMekan1Id
                OrtakMekan1Id = DatabaseManager.GetInt(row, "ortak_mekan_1_id"),
                OrtakMekan2Id = DatabaseManager.GetInt(row, "ortak_mekan_2_id"),
                OrtakMekan3Id = DatabaseManager.GetInt(row, "ortak_mekan_3_id"),
                OrtakMekan4Id = DatabaseManager.GetInt(row, "ortak_mekan_4_id"),
                OrtakMekan5Id = DatabaseManager.GetInt(row, "ortak_mekan_5_id"),
                OrtakMekan6Id = DatabaseManager.GetInt(row, "ortak_mekan_6_id"),
                OrtakMekan7Id = DatabaseManager.GetInt(row, "ortak_mekan_7_id"),
                KardesId = DatabaseManager.GetInt(row, "kardes_id")
            };
            
            // Multiple Teachers
            // Assuming we have ogretmen_1_id ... 5 columns
            b.Teacher1Id = DatabaseManager.GetInt(row, "ogretmen_1_id");
            b.Teacher2Id = DatabaseManager.GetInt(row, "ogretmen_2_id");
            b.Teacher3Id = DatabaseManager.GetInt(row, "ogretmen_3_id");
            b.Teacher4Id = DatabaseManager.GetInt(row, "ogretmen_4_id");
            b.Teacher5Id = DatabaseManager.GetInt(row, "ogretmen_5_id");
            b.Teacher6Id = DatabaseManager.GetInt(row, "ogretmen_6_id");
            b.Teacher7Id = DatabaseManager.GetInt(row, "ogretmen_7_id");
            
            list.Add(b);
        }
        return list;
    }
    
    public void ResetAllDistributions(bool keepManual = false, bool wipeAll = false)
    {
        _db.Execute("BEGIN TRANSACTION");
        try
        {
            // 1. Dagitim Bloklari temizligi
            var updates = new List<string>();
            for (int d = 1; d <= 7; d++)
                for (int h = 1; h <= 12; h++)
                    updates.Add($"d_{d}_{h} = ''");
            string dSet = string.Join(", ", updates);

            string where = "WHERE kilitli = 0 OR kilitli IS NULL";
            if (wipeAll)
                where = "";
            else if (keepManual)
                where = "WHERE (kilitli = 0 OR kilitli IS NULL) AND (manuel = 0 OR manuel IS NULL)";

            string setClause = $"gun = 0, saat = 0, yerlesim_tipi = NULL, {dSet}";
            if (wipeAll) setClause += ", kilitli = 0, manuel = 0";

            _db.Execute($"UPDATE dagitim_bloklari SET {setClause} {where}");

            // 2. Ogretmen ve Sinif Tablolari Temizligi (KAPALI korunur)
            var tcUpdates = new List<string>();
            for (int d = 1; d <= 7; d++)
                for (int h = 1; h <= 12; h++)
                {
                    var col = $"d_{d}_{h}";
                    tcUpdates.Add($"{col} = CASE WHEN {col} = 'KAPALI' THEN 'KAPALI' ELSE '' END");
                }
            string tcSet = string.Join(", ", tcUpdates);

            _db.Execute($"UPDATE ogretmen SET {tcSet}");
            _db.Execute($"UPDATE sinif SET {tcSet}");
            string mSet = string.Join(", ", updates);
            _db.Execute($"UPDATE ortak_mekan SET {mSet}");

            _db.Execute("COMMIT");
        }
        catch
        {
            _db.Execute("ROLLBACK");
            throw;
        }
    }
    
    public void PlaceBlock(DistributionBlock block, string method = "otomatik")
    {
        // 1. Update Block
        string sql = $"UPDATE dagitim_bloklari SET gun = {block.Day}, saat = {block.Hour}, yerlesim_tipi = '{DatabaseManager.Escape(method)}' WHERE id = {block.Id}";
        _db.Execute(sql);
        
        // 2. Teacher/Class names for live view
        var cQuery = _db.Query($"SELECT ad FROM sinif WHERE id={block.ClassId}");
        var cName = cQuery.Count > 0 ? DatabaseManager.GetString(cQuery[0], "ad") : "";
        
        // NEW: Get names of ALL teachers for the class schedule
        var allTeacherIds = block.TeacherIds;
        var tRows = _db.Query($"SELECT id, ad_soyad FROM ogretmen WHERE id IN ({string.Join(",", allTeacherIds)})");
        var teacherDict = new Dictionary<int, string>();
        foreach(var tRow in tRows) 
        {
            teacherDict[DatabaseManager.GetInt(tRow, "id")] = DatabaseManager.GetString(tRow, "ad_soyad");
        }

        // Ordered names based on TeacherIds list
        var tNames = string.Join(", ", allTeacherIds.Select(id => teacherDict.ContainsKey(id) ? teacherDict[id] : "").Where(n => !string.IsNullOrEmpty(n)));
        
        // Primary teacher name for primary slot display
        var primaryTeacherName = allTeacherIds.Count > 0 && teacherDict.ContainsKey(allTeacherIds[0]) ? teacherDict[allTeacherIds[0]] : "";
        if (allTeacherIds.Count > 1) primaryTeacherName += $" (+{allTeacherIds.Count - 1})";
        
        // 3. Update Cells (Teacher & Class Tables)
        var tSets = new List<string>();
        var cSets = new List<string>();
        var bSets = new List<string>();
        
        for (int i = 0; i < block.BlockDuration; i++)
        {
            int h = block.Hour + i;
            if (h > 12) continue;
            
            string col = $"d_{block.Day}_{h}";
            
            // For Block Table (XXXX)
            bSets.Add($"{col} = 'XXXX'");
            
            // For Teacher Table (Class Name + Lesson)
            tSets.Add($"{col} = '{DatabaseManager.Escape(cName)}    {DatabaseManager.Escape(block.LessonCode)}'");
            
            // For Class Table (Lesson + ALL Teachers)
            cSets.Add($"{col} = '{DatabaseManager.Escape(block.LessonCode)} - {DatabaseManager.Escape(tNames)}'");
        }
        
        if (bSets.Count > 0)
        {
             // Blok tablosu (XXXX) için append gerekmez, zaten blok ID'ye göre update ediyoruz
            _db.Execute($"UPDATE dagitim_bloklari SET {string.Join(", ", bSets)} WHERE id={block.Id}");
        }
        
        if (tSets.Count > 0)
        {
            foreach(var tid in block.TeacherIds)
            {
                // Mevcut değer varsa üzerine ekle (Append)
                var updates = tSets.Select(s => 
                {
                    // s format: d_1_1 = 'Value'
                    // We need: d_1_1 = CASE WHEN d_1_1 IS NULL OR d_1_1 = '' THEN 'Value' ELSE d_1_1 || char(10) || 'Value' END
                    var parts = s.Split('=');
                    var colName = parts[0].Trim();
                    var value = parts[1].Trim(); // 'Value'
                    return $"{colName} = CASE WHEN {colName} IS NULL OR {colName} = '' THEN {value} ELSE {colName} || '\n' || {value} END";
                });
                
                _db.Execute($"UPDATE ogretmen SET {string.Join(", ", updates)} WHERE id={tid}");
            }
        }
        
        if (cSets.Count > 0)
        {
            var updates = cSets.Select(s => 
            {
                var parts = s.Split('=');
                var colName = parts[0].Trim();
                var value = parts[1].Trim(); 
                return $"{colName} = CASE WHEN {colName} IS NULL OR {colName} = '' THEN {value} ELSE {colName} || '\n' || {value} END";
            });
            _db.Execute($"UPDATE sinif SET {string.Join(", ", updates)} WHERE id={block.ClassId}");
        }

        // Ortak Mekanlara Yazma (Specific Teacher -> Specific Room mapping)
        var pairs = new List<(int RoomId, int TeacherId)>();
        if (block.OrtakMekan1Id > 0 && block.Teacher1Id > 0) pairs.Add((block.OrtakMekan1Id, block.Teacher1Id));
        if (block.OrtakMekan2Id > 0 && block.Teacher2Id > 0) pairs.Add((block.OrtakMekan2Id, block.Teacher2Id));
        if (block.OrtakMekan3Id > 0 && block.Teacher3Id > 0) pairs.Add((block.OrtakMekan3Id, block.Teacher3Id));
        if (block.OrtakMekan4Id > 0 && block.Teacher4Id > 0) pairs.Add((block.OrtakMekan4Id, block.Teacher4Id));
        if (block.OrtakMekan5Id > 0 && block.Teacher5Id > 0) pairs.Add((block.OrtakMekan5Id, block.Teacher5Id));
        if (block.OrtakMekan6Id > 0 && block.Teacher6Id > 0) pairs.Add((block.OrtakMekan6Id, block.Teacher6Id));
        if (block.OrtakMekan7Id > 0 && block.Teacher7Id > 0) pairs.Add((block.OrtakMekan7Id, block.Teacher7Id));

        foreach (var pair in pairs)
        {
             // Get specific teacher name for this room
             var tQuery = _db.Query($"SELECT ad_soyad FROM ogretmen WHERE id={pair.TeacherId}");
             var teacherName = tQuery.Count > 0 ? DatabaseManager.GetString(tQuery[0], "ad_soyad") : "";

             // Format: Class Lesson Teacher(Specific)
             string mContent = $"{DatabaseManager.Escape(cName)}    {DatabaseManager.Escape(block.LessonCode)}    {DatabaseManager.Escape(teacherName)}";
             
             var mUpdates = new List<string>();
             for (int i = 0; i < block.BlockDuration; i++)
             {
                int h = block.Hour + i;
                if (h > 12) continue;
                string col = $"d_{block.Day}_{h}";
                string val = $"'{mContent}'";
                mUpdates.Add($"{col} = CASE WHEN {col} IS NULL OR {col} = '' THEN {val} ELSE {col} || '\n' || {val} END");
             }
             
             if (mUpdates.Count > 0)
             {
                 _db.Execute($"UPDATE ortak_mekan SET {string.Join(", ", mUpdates)} WHERE id={pair.RoomId}");
             }
        }
    }

    public void ClearBlock(int blockId)
    {
        // 1. Get block info to clear teacher/class cells
        var query = _db.Query($"SELECT * FROM dagitim_bloklari WHERE id={blockId}");
        if (query.Count == 0) return;
        var row = query[0];
        
        int day = DatabaseManager.GetInt(row, "gun");
        int hour = DatabaseManager.GetInt(row, "saat");
        int classId = DatabaseManager.GetInt(row, "sinif_id");
        int duration = DatabaseManager.GetInt(row, "blok_suresi");
        var tIds = new List<int>();
        for(int i=1; i<=7; i++) {
            int tid = DatabaseManager.GetInt(row, $"ogretmen_{i}_id");
            if (tid > 0) tIds.Add(tid);
        }
        
        var mIds = new List<int>();
        for(int i=1; i<=7; i++) {
            int mid = DatabaseManager.GetInt(row, $"ortak_mekan_{i}_id");
            if (mid > 0) mIds.Add(mid);
        }

        // 2. Clear current day/hour
        _db.Execute($"UPDATE dagitim_bloklari SET gun = 0, saat = 0, yerlesim_tipi = NULL WHERE id = {blockId}");

        // 3. Clear visual cells (d_X_Y) in all 3 tables
        if (day > 0 && hour > 0)
        {
            var sets = new List<string>();
            for (int i = 0; i < duration; i++)
            {
                int h = hour + i;
                if (h <= 12) sets.Add($"d_{day}_{h} = ''");
            }

            if (sets.Count > 0)
            {
                string setsJoined = string.Join(", ", sets);
                _db.Execute($"UPDATE dagitim_bloklari SET {setsJoined} WHERE id={blockId}");
                _db.Execute($"UPDATE sinif SET {setsJoined} WHERE id={classId}");
                foreach(var tid in tIds) {
                    _db.Execute($"UPDATE ogretmen SET {setsJoined} WHERE id={tid}");
                }
                foreach(var mid in mIds)
                {
                    _db.Execute($"UPDATE ortak_mekan SET {setsJoined} WHERE id={mid}");
                }
            }
        }
    }
    public int GetUnplacedCount()
    {
        var result = _db.Query("SELECT COUNT(*) as cnt FROM dagitim_bloklari WHERE gun = 0 OR gun IS NULL");
        if (result.Count > 0)
            return DatabaseManager.GetInt(result[0], "cnt");
        return 0;
    }

    public void SaveDistributionAtomically(List<DistributionBlock> blocks, bool keepManual = true)
    {
        _db.Execute("BEGIN TRANSACTION");
        try
        {
            // --- RESET LOGIC START ---
            var updates = new List<string>();
            for (int d = 1; d <= 7; d++)
                for (int h = 1; h <= 12; h++)
                    updates.Add($"d_{d}_{h} = ''"); 

            string dSet = string.Join(", ", updates);
            string where = keepManual ? "WHERE (kilitli = 0 OR kilitli IS NULL) AND (manuel = 0 OR manuel IS NULL)" : "WHERE kilitli = 0 OR kilitli IS NULL";
            
            string sql = $"UPDATE dagitim_bloklari SET gun = 0, saat = 0, yerlesim_tipi = NULL, {dSet} {where}";
            _db.Execute(sql);
            
            var tcUpdates = new List<string>();
            for (int d = 1; d <= 7; d++)
                for (int h = 1; h <= 12; h++)
                {
                    var col = $"d_{d}_{h}";
                    tcUpdates.Add($"{col} = CASE WHEN {col} = 'KAPALI' THEN 'KAPALI' ELSE '' END");
                }
                var tcSet = string.Join(", ", tcUpdates);
            
            _db.Execute($"UPDATE ogretmen SET {tcSet}");
            _db.Execute($"UPDATE sinif SET {tcSet}");
            
            // FIX: Preserve 'KAPALI' in ortak_mekan table as well
            var mUpdates = new List<string>();
            for (int d = 1; d <= 7; d++)
                for (int h = 1; h <= 12; h++)
                {
                    var col = $"d_{d}_{h}";
                    mUpdates.Add($"{col} = CASE WHEN {col} = 'KAPALI' THEN 'KAPALI' ELSE '' END");
                }
            string mSet = string.Join(", ", mUpdates);
            _db.Execute($"UPDATE ortak_mekan SET {mSet}");
            // --- RESET LOGIC END ---

            // --- SAVE LOGIC START ---
            // --- SAVE LOGIC START ---
            foreach(var b in blocks)
            {
                // We MUST call PlaceBlock for LOCKED blocks too, because we wiped the 'ogretmen' and 'sinif' tables above/
                // PlaceBlock updates the visual cells.
                
                if (b.Day > 0 && b.Hour > 0)
                {
                    // Use existing type if locked, otherwise use new type
                    string pType = b.IsLocked ? (b.PlacementType ?? "manuel") : "otomatik";
                    PlaceBlock(b, pType);
                }
            }
            // --- SAVE LOGIC END ---

            _db.Execute("COMMIT");
        }
        catch (Exception ex)
        {
            _db.Execute("ROLLBACK");
            throw new Exception("Dağıtım kaydedilirken hata oluştu: " + ex.Message);
        }
    }

    /// <summary>
    /// Checks all blocks and ensures teacher/class/ortak_mekan tables are in sync with dagitim_bloklari.
    /// Run this after bulk operations to fix inconsistencies.
    /// </summary>
    public void SyncSignalTables()
    {
        var tcUpdates = new List<string>();
        for (int d = 1; d <= 7; d++)
        {
            for (int h = 1; h <= 12; h++)
            {
                var col = $"d_{d}_{h}";
                tcUpdates.Add($"{col} = CASE WHEN {col} = 'KAPALI' THEN 'KAPALI' ELSE '' END");
            }
        }
        string tcSet = string.Join(", ", tcUpdates);
        
        // Ortak mekan temizliği
        var clearUpdates = new List<string>();
        for (int d = 1; d <= 7; d++)
        {
             for (int h = 1; h <= 12; h++)
             {
                 clearUpdates.Add($"d_{d}_{h} = ''");
             }
        }
        string clearSet = string.Join(", ", clearUpdates);

        _db.Execute("BEGIN TRANSACTION");
        try
        {
            _db.Execute($"UPDATE ogretmen SET {tcSet}");
            _db.Execute($"UPDATE sinif SET {tcSet}");
            _db.Execute($"UPDATE ortak_mekan SET {clearSet}");
             
             // 2. Re-write all placed blocks
             var blocks = GetAllBlocks(); 
             var placedBlocks = blocks.Where(b => b.Day > 0 && b.Hour > 0).ToList();
             
             foreach(var block in placedBlocks)
             {
                // Re-apply visual cells
                PlaceBlock(block, block.PlacementType);
             }
             
             _db.Execute("COMMIT");
        }
        catch
        {
            _db.Execute("ROLLBACK");
            throw;
        }
    }

    public List<string> ValidateAndFixDatabase(Action<string>? onLog = null)
    {
        var logs = new List<string>();
        void Log(string msg) { logs.Add(msg); onLog?.Invoke(msg); }

        Log("Veritabanı analizi başlatılıyor...");
        
        // 0. ORPHAN DATA CLEANING (Yetim Veri Temizliği)
        // Silinmiş sınıflara ait dersleri, atamaları ve blokları temizle
        Log("Yetim veri temizliği yapılıyor...");
        try
        {
             // 1. Orphan ClassLessons (Parent Class Deleted)
             var orphanCLs = _db.Query("SELECT id FROM sinif_ders WHERE sinif_id NOT IN (SELECT id FROM sinif)");
             if (orphanCLs.Count > 0)
             {
                 var clIds = orphanCLs.Select(r => DatabaseManager.GetInt(r, "id")).ToList();
                 string ids = string.Join(",", clIds);

                 Log($"TEMİZLİK: {clIds.Count} adet yetim Sınıf-Ders kaydı bulundu (Sınıfı silinmiş). Temizleniyor...");

                 _db.Execute($"DELETE FROM dagitim_bloklari WHERE sinif_ders_id IN ({ids})");
                 _db.Execute($"DELETE FROM atama WHERE sinif_ders_id IN ({ids})");
                 _db.Execute($"DELETE FROM sinif_ders WHERE id IN ({ids})");

                 Log("---> Yetim dersler, bloklar ve atamalar silindi.");
             }

             // 2. Orphan Blocks (No ClassLesson)
             var orphanBlocks = _db.Query("SELECT id FROM dagitim_bloklari WHERE sinif_ders_id NOT IN (SELECT id FROM sinif_ders)");
             if (orphanBlocks.Count > 0)
             {
                 var bIds = orphanBlocks.Select(r => DatabaseManager.GetInt(r, "id")).ToList();
                 Log($"TEMİZLİK: {bIds.Count} adet yetim Blok bulundu. Siliniyor...");
                 _db.Execute($"DELETE FROM dagitim_bloklari WHERE id IN ({string.Join(",", bIds)})");
             }

             // 3. Orphan Assignments (No ClassLesson)
             var orphanAssigns = _db.Query("SELECT id FROM atama WHERE sinif_ders_id NOT IN (SELECT id FROM sinif_ders)");
             if (orphanAssigns.Count > 0)
             {
                 var aIds = orphanAssigns.Select(r => DatabaseManager.GetInt(r, "id")).ToList();
                 Log($"TEMİZLİK: {aIds.Count} adet yetim Atama bulundu. Siliniyor...");
                 _db.Execute($"DELETE FROM atama WHERE id IN ({string.Join(",", aIds)})");
             }
        }
        catch (Exception ex)
        {
             Log($"HATA [Temizlik]: {ex.Message}");
        }

        Log("Çakışma kontrolü yapılıyor...");
        var allBlocks = GetAllBlocks();
        var placedBlocks = allBlocks.Where(b => b.Day > 0 && b.Hour > 0).ToList();

        // 1. Dagitim Bloklari Çakışma Kontrolü
        var classCollisions = placedBlocks
            .GroupBy(b => new { b.ClassId, b.Day, b.Hour })
            .Where(g => g.Count() > 1);

        int collisionCount = 0;
        int repairedCount = 0;

        foreach(var grp in classCollisions)
        {
             Log($"ÇAKIŞMA [Sınıf]: SınıfID {grp.Key.ClassId} - Gün {grp.Key.Day} Saat {grp.Key.Hour} -> {grp.Count()} blok (ID: {string.Join(",", grp.Select(b => b.Id))})");
             collisionCount++;

             // Otomatik Onarım: İlk blok hariç diğerlerini sıfırla (kilitli/manuel olanlar korunur)
             var conflicting = grp.OrderBy(b => b.Id).Skip(1).ToList();
             foreach (var cb in conflicting)
             {
                 if (cb.IsLocked || cb.IsManual)
                 {
                     Log($"---> Blok {cb.Id} kilitli/manuel olduğu için korunuyor.");
                     continue;
                 }
                 _db.Execute($"UPDATE dagitim_bloklari SET gun = 0, saat = 0 WHERE id = {cb.Id}");
                 Log($"---> ONARILDI: Blok {cb.Id} sıfırlandı (gün=0, saat=0).");
                 repairedCount++;
             }
        }

        // Öğretmen Çakışması
        var teacherOccupancy = new Dictionary<(int tid, int day, int hour), List<int>>();
        foreach(var b in placedBlocks)
        {
            for(int i=0; i<b.BlockDuration; i++)
            {
                int h = b.Hour + i;
                if(h > 12) continue;

                foreach(var tid in b.TeacherIds)
                {
                    var key = (tid, b.Day, h);
                    if (!teacherOccupancy.ContainsKey(key)) teacherOccupancy[key] = new List<int>();
                    teacherOccupancy[key].Add(b.Id);
                }
            }
        }

        // Onarım için çakışan blok ID'lerini topla
        var teacherConflictBlockIds = new HashSet<int>();
        foreach(var kvp in teacherOccupancy.Where(x => x.Value.Distinct().Count() > 1))
        {
             Log($"ÇAKIŞMA [Öğretmen]: ÖğretmenID {kvp.Key.tid} - Gün {kvp.Key.day} Saat {kvp.Key.hour} -> Bloklar: {string.Join(",", kvp.Value.Distinct())}");
             collisionCount++;

             // Otomatik Onarım: İlk blok hariç diğerlerini sıfırla
             var distinctIds = kvp.Value.Distinct().OrderBy(id => id).Skip(1).ToList();
             foreach (var blockId in distinctIds)
             {
                 if (teacherConflictBlockIds.Contains(blockId)) continue; // Zaten işlendi
                 var block = placedBlocks.FirstOrDefault(b => b.Id == blockId);
                 if (block != null && !block.IsLocked && !block.IsManual)
                 {
                     _db.Execute($"UPDATE dagitim_bloklari SET gun = 0, saat = 0 WHERE id = {blockId}");
                     Log($"---> ONARILDI: Blok {blockId} (öğretmen çakışması) sıfırlandı.");
                     teacherConflictBlockIds.Add(blockId);
                     repairedCount++;
                 }
                 else if (block != null && (block.IsLocked || block.IsManual))
                 {
                     Log($"---> Blok {blockId} kilitli/manuel olduğu için korunuyor.");
                 }
             }
        }

        // Ortak Mekan Çakışması
        var mekanOccupancy = new Dictionary<(int mid, int day, int hour), List<int>>();
        foreach(var b in placedBlocks)
        {
            foreach(var mid in b.GetOrtakMekanIds())
            {
                for(int i=0; i<b.BlockDuration; i++)
                {
                    int h = b.Hour + i;
                    if(h > 12) continue;

                    var key = (mid, b.Day, h);
                    if (!mekanOccupancy.ContainsKey(key)) mekanOccupancy[key] = new List<int>();
                    mekanOccupancy[key].Add(b.Id);
                }
            }
        }

        var roomConflictBlockIds = new HashSet<int>();
        foreach(var kvp in mekanOccupancy.Where(x => x.Value.Distinct().Count() > 1))
        {
             Log($"ÇAKIŞMA [Ortak Mekan]: MekanID {kvp.Key.mid} - Gün {kvp.Key.day} Saat {kvp.Key.hour} -> Bloklar: {string.Join(",", kvp.Value.Distinct())}");
             collisionCount++;

             // Otomatik Onarım: İlk blok hariç diğerlerini sıfırla
             var distinctIds = kvp.Value.Distinct().OrderBy(id => id).Skip(1).ToList();
             foreach (var blockId in distinctIds)
             {
                 if (roomConflictBlockIds.Contains(blockId) || teacherConflictBlockIds.Contains(blockId)) continue;
                 var block = placedBlocks.FirstOrDefault(b => b.Id == blockId);
                 if (block != null && !block.IsLocked && !block.IsManual)
                 {
                     _db.Execute($"UPDATE dagitim_bloklari SET gun = 0, saat = 0 WHERE id = {blockId}");
                     Log($"---> ONARILDI: Blok {blockId} (mekan çakışması) sıfırlandı.");
                     roomConflictBlockIds.Add(blockId);
                     repairedCount++;
                 }
                 else if (block != null && (block.IsLocked || block.IsManual))
                 {
                     Log($"---> Blok {blockId} kilitli/manuel olduğu için korunuyor.");
                 }
             }
        }

        if (repairedCount > 0)
            Log($"Çakışma onarımı: Toplam {repairedCount} blok sıfırlandı.");

        // 3. Eksik/Fazla Blok ve Saat Kontrolü
        Log("Blok bütünlük kontrolü yapılıyor...");
        var classLessonRepo = new ClassLessonRepository();
        var allClassLessons = classLessonRepo.GetAll();

        var lessonRepo = new LessonRepository();
        var allLessons = lessonRepo.GetAll().ToDictionary(l => l.Id);

        var classRepo = new ClassRepository();
        var allClasses = classRepo.GetAll().ToDictionary(c => c.Id);

        var teacherRepo = new TeacherRepository();
        var allTeachers = teacherRepo.GetAll().ToDictionary(t => t.Id);

        foreach(var cl in allClassLessons)
        {
            var clBlocks = placedBlocks.Where(x => x.ClassLessonId == cl.Id).Concat(allBlocks.Where(x => x.ClassLessonId == cl.Id && (x.Day == 0 || x.Hour == 0))).ToList();
            int currentDurationSum = clBlocks.Sum(x => x.BlockDuration);

            string cName = allClasses.ContainsKey(cl.ClassId) ? allClasses[cl.ClassId].Name : $"Sınıf#{cl.ClassId}";
            string lName = allLessons.ContainsKey(cl.LessonId) ? allLessons[cl.LessonId].Name : $"Ders#{cl.LessonId}";

            bool needRepair = false;
            if (currentDurationSum != cl.TotalHours)
            {
                Log($"HATA [Süre Uyumsuzluğu]: {cName} - {lName} (ID:{cl.Id}) -> Tanımlı: {cl.TotalHours} saat, Blok Toplamı: {currentDurationSum} saat. Otomatik onarılacak.");
                needRepair = true;
                collisionCount++;
            }

            if (currentDurationSum == 0 && cl.TotalHours > 0)
            {
                Log($"KRİTİK HATA [Blok Yok]: {cName} - {lName} (ID:{cl.Id}) için bloklar eksik. Otomatik oluşturulacak.");
                needRepair = true;
                collisionCount++;
            }

            if (needRepair)
            {
                try
                {
                    var blockRepo = new DistributionBlockRepository();
                    blockRepo.RegenerateBlocksForClassLesson(cl.Id);
                    Log($"---> ONARILDI: SınıfDersID {cl.Id} için bloklar veritabanında yeniden oluşturuldu.");
                }
                catch (Exception ex)
                {
                    Log($"---> ONARIM BAŞARISIZ: {ex.Message}");
                }
            }

            if (!needRepair && allLessons.TryGetValue(cl.LessonId, out var lessonInfo))
            {
                 var expectedPattern = DistributionBlockRepository.ParseBlockPattern(lessonInfo.DefaultBlock, cl.TotalHours);
                 expectedPattern.Sort();

                 var currentPattern = clBlocks.Select(x => x.BlockDuration).ToList();
                 currentPattern.Sort();

                 if (!expectedPattern.SequenceEqual(currentPattern))
                 {
                     Log($"UYARI [Blok Yapısı]: {cName} - {lName} (ID:{cl.Id}) -> Beklenen: {string.Join("+", expectedPattern)}, Mevcut: {string.Join("+", currentPattern)}. (Manuel düzenleme yapılmış olabilir)");
                 }
            }
        }

        // 4. Atama ve Blok Öğretmen Senkronizasyonu (Detaylı Kontrol ve Onarım)
        Log("Öğretmen senkronizasyonu kontrol ediliyor...");
        var repoAssignSync = new AssignmentRepository();
        var blockRepoSync = new DistributionBlockRepository();

        var distinctClassLessons = allBlocks.Select(b => b.ClassLessonId).Distinct().ToList();

        foreach(var clId in distinctClassLessons)
        {
            var assignments = repoAssignSync.GetByClassLessonId(clId).OrderBy(a => a.Id).ToList();

            int[] expectedTeachers = new int[7];
            for (int i = 0; i < Math.Min(assignments.Count, 7); i++)
                expectedTeachers[i] = assignments[i].TeacherId;

            var blocks = allBlocks.Where(b => b.ClassLessonId == clId).ToList();

            foreach (var block in blocks)
            {
                bool mismatch = false;
                int[] currentTeachers = {
                    block.Teacher1Id, block.Teacher2Id, block.Teacher3Id,
                    block.Teacher4Id, block.Teacher5Id, block.Teacher6Id, block.Teacher7Id
                };

                for (int i = 0; i < 7; i++)
                {
                    if (currentTeachers[i] != expectedTeachers[i])
                    {
                        mismatch = true;
                        break;
                    }
                }

                if (mismatch)
                {
                    var clInfo = allClassLessons.FirstOrDefault(x => x.Id == clId);
                    string infoStr = $"ID:{clId}";
                    if (clInfo != null)
                    {
                         string cn = allClasses.ContainsKey(clInfo.ClassId) ? allClasses[clInfo.ClassId].Name : "";
                         string ln = allLessons.ContainsKey(clInfo.LessonId) ? allLessons[clInfo.LessonId].Name : "";
                         infoStr = $"{cn} - {ln}";
                    }

                    Log($"HATA [Senkronizasyon]: {infoStr}, BlokID {block.Id} öğretmen listesi uyuşmuyor. Otomatik düzeltiliyor...");
                    collisionCount++;

                    block.Teacher1Id = expectedTeachers[0];
                    block.Teacher2Id = expectedTeachers[1];
                    block.Teacher3Id = expectedTeachers[2];
                    block.Teacher4Id = expectedTeachers[3];
                    block.Teacher5Id = expectedTeachers[4];
                    block.Teacher6Id = expectedTeachers[5];
                    block.Teacher7Id = expectedTeachers[6];

                    blockRepoSync.Save(block);
                    Log($"---> DÜZELTİLDİ: Blok {block.Id} öğretmenleri atama tablosuna göre eşitlendi.");
                }
            }
        }

        if (collisionCount == 0) Log("Tüm veri kontrolleri yapıldı, sorun bulunamadı.");
        else Log($"TOPLAM {collisionCount} sorun tespit edildi!");

        // 5. Senkronizasyon
        Log("Tablo senkronizasyonu başlatılıyor...");
        try
        {
            SyncSignalTables();
            Log("BAŞARILI: Görsel tablolar (d_X_Y) ve Ortak Mekanlar, dağıtım verisine göre yeniden oluşturuldu.");
        }
        catch(Exception ex)
        {
            Log($"HATA: Senkronizasyon başarısız: {ex.Message}");
        }

        Log("Tamamlandı.");
        return logs;
    }
}
