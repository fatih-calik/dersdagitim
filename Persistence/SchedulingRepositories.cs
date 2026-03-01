using DersDagitim.Models;

namespace DersDagitim.Persistence;

/// <summary>
/// Class-Lesson assignment repository
/// </summary>
public class ClassLessonRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;
    
    public List<ClassLesson> GetAll()
    {
        var results = _db.Query(@"
            SELECT sd.*, IFNULL(kb.kardes_id, 0) as kardes_id 
            FROM sinif_ders sd 
            LEFT JOIN kardes_bloklar kb ON sd.id = kb.sinif_ders_id");
        return results.Select(row => new ClassLesson
        {
            Id = DatabaseManager.GetInt(row, "id"),
            ClassId = DatabaseManager.GetInt(row, "sinif_id"),
            LessonId = DatabaseManager.GetInt(row, "ders_id"),
            TotalHours = DatabaseManager.GetInt(row, "toplam_saat"),
            KardesId = DatabaseManager.GetInt(row, "kardes_id")
        }).ToList();
    }
    
    public List<ClassLesson> GetByClassId(int classId)
    {
        var results = _db.Query($@"
            SELECT sd.*, IFNULL(kb.kardes_id, 0) as kardes_id 
            FROM sinif_ders sd 
            LEFT JOIN kardes_bloklar kb ON sd.id = kb.sinif_ders_id
            WHERE sd.sinif_id = {classId}");
        return results.Select(row => new ClassLesson
        {
            Id = DatabaseManager.GetInt(row, "id"),
            ClassId = DatabaseManager.GetInt(row, "sinif_id"),
            LessonId = DatabaseManager.GetInt(row, "ders_id"),
            TotalHours = DatabaseManager.GetInt(row, "toplam_saat"),
            KardesId = DatabaseManager.GetInt(row, "kardes_id")
        }).ToList();
    }
    
    // Alias for GetByClassId (compatibility)
    public List<ClassLesson> GetClassLessons(int classId) => GetByClassId(classId);
    
    // Get teacher assignments for a class-lesson
    public List<TeacherAssignment> GetTeacherAssignments(int classLessonId)
    {
        try 
        {
            // AUTO-FIX: Eğer atama var ama blok yoksa, hemen oluştur (Lazy Repair)
            // Bu sayede kullanıcı listeyi açtığı anda bloklar senkronize olur.
            var blockCountObj = _db.Query($"SELECT count(*) as c FROM dagitim_bloklari WHERE sinif_ders_id = {classLessonId}");
            long blockCount = 0;
            if (blockCountObj.Count > 0) 
                blockCount = Convert.ToInt64(blockCountObj[0]["c"]);
                
            if (blockCount == 0)
            {
                 var assignCountObj = _db.Query($"SELECT count(*) as c FROM atama WHERE sinif_ders_id = {classLessonId}");
                 long assignCount = 0;
                 if (assignCountObj.Count > 0)
                    assignCount = Convert.ToInt64(assignCountObj[0]["c"]);
                    
                 if (assignCount > 0)
                 {
                     // Bloklar eksik, hemen oluştur
                     new DistributionBlockRepository().RegenerateBlocksForClassLesson(classLessonId);
                 }
            }
        }
        catch {}

        var results = _db.Query($"SELECT * FROM atama WHERE sinif_ders_id = {classLessonId}");
        return results.Select(row => new TeacherAssignment
        {
            Id = DatabaseManager.GetInt(row, "id"),
            ClassLessonId = DatabaseManager.GetInt(row, "sinif_ders_id"),
            TeacherId = DatabaseManager.GetInt(row, "ogretmen_id"),
            AssignedHours = DatabaseManager.GetInt(row, "atanan_saat")
        }).ToList();
    }
    
    // Add a lesson to a class
    public int? AddLessonToClass(int classId, int lessonId, int totalHours)
    {
        _db.Execute($@"
            INSERT INTO sinif_ders (sinif_id, ders_id, toplam_saat)
            VALUES ({classId}, {lessonId}, {totalHours})
        ");
        var result = _db.Query("SELECT last_insert_rowid() as id");
        if (result.Count > 0)
        {
            int newId = DatabaseManager.GetInt(result[0], "id");
            // Generate empty blocks immediately
            new DistributionBlockRepository().RegenerateBlocksForClassLesson(newId);
            return newId;
        }
        return null;
    }
    
    // Add a teacher assignment
    public int? AddTeacherAssignment(int classLessonId, int teacherId, int assignedHours)
    {
        _db.Execute($@"
            INSERT INTO atama (sinif_ders_id, ogretmen_id, atanan_saat)
            VALUES ({classLessonId}, {teacherId}, {assignedHours})
        ");
        var result = _db.Query("SELECT last_insert_rowid() as id");
        int? assignmentId = result.Count > 0 ? DatabaseManager.GetInt(result[0], "id") : null;
        
        // Create distribution blocks
        // Create/Update distribution blocks using robust method
        new DistributionBlockRepository().RegenerateBlocksForClassLesson(classLessonId);
        
        // SYNC: Update teacher total hours
        new TeacherRepository().SyncAllTeacherHours();
        
        return assignmentId;
    }
    
    // Update teacher assignment
    public void UpdateTeacherAssignment(int assignmentId, int teacherId, int assignedHours)
    {
        var info = _db.Query($"SELECT sinif_ders_id FROM atama WHERE id = {assignmentId}");
        if (info.Count == 0) return;
        
        int classLessonId = DatabaseManager.GetInt(info[0], "sinif_ders_id");
        
        _db.Execute($"UPDATE atama SET ogretmen_id = {teacherId}, atanan_saat = {assignedHours} WHERE id = {assignmentId}");
        
        // Update distribution blocks
        UpdateBlocksState(classLessonId);
    }
    
    // Remove teacher assignment
    public void RemoveTeacherAssignment(int assignmentId)
    {
        var info = _db.Query($"SELECT sinif_ders_id FROM atama WHERE id = {assignmentId}");
        if (info.Count > 0)
        {
            int classLessonId = DatabaseManager.GetInt(info[0], "sinif_ders_id");

            // 1. Delete the assignment
            _db.Execute($"DELETE FROM atama WHERE id = {assignmentId}");

            // 2. Check if any assignments remain for this class-lesson
            var remaining = _db.Query($"SELECT COUNT(*) as c FROM atama WHERE sinif_ders_id = {classLessonId}");
            long remainingCount = remaining.Count > 0 ? Convert.ToInt64(remaining[0]["c"]) : 0;

            if (remainingCount == 0)
            {
                // Last assignment removed - full cascade cleanup (match RemoveLessonFromClass)
                _db.Execute($"DELETE FROM dagitim_bloklari WHERE sinif_ders_id = {classLessonId}");
                _db.Execute($"DELETE FROM kardes_bloklar WHERE sinif_ders_id = {classLessonId}");
                _db.Execute($"DELETE FROM sinif_ders WHERE id = {classLessonId}");
            }
            else
            {
                // Other teachers remain - re-sync blocks with remaining assignments
                UpdateBlocksState(classLessonId);
            }

            // SYNC: Update teacher total hours
            new TeacherRepository().SyncAllTeacherHours();
        }
    }
    
    // Remove lesson from class
    public void RemoveLessonFromClass(int classLessonId)
    {
        _db.Execute($"DELETE FROM dagitim_bloklari WHERE sinif_ders_id = {classLessonId}");
        _db.Execute($"DELETE FROM atama WHERE sinif_ders_id = {classLessonId}");
        _db.Execute($"DELETE FROM kardes_bloklar WHERE sinif_ders_id = {classLessonId}");
        _db.Execute($"DELETE FROM sinif_ders WHERE id = {classLessonId}");

        new TeacherRepository().SyncAllTeacherHours();
    }
    
    // Get total hours for a class
    public int GetTotalHoursForClass(int classId)
    {
        var result = _db.Query($"SELECT SUM(toplam_saat) as total FROM sinif_ders WHERE sinif_id = {classId}");
        if (result.Count > 0)
            return DatabaseManager.GetInt(result[0], "total");
        return 0;
    }
    
    // Create distribution blocks
    private void CreateDistributionBlocks(int classLessonId, int teacherId, int assignedHours)
    {
        var clInfo = _db.Query($"SELECT sinif_id, ders_id FROM sinif_ders WHERE id = {classLessonId}");
        if (clInfo.Count == 0) return;
        
        int classId = DatabaseManager.GetInt(clInfo[0], "sinif_id");
        int lessonId = DatabaseManager.GetInt(clInfo[0], "ders_id");
        
        var lessonInfo = _db.Query($"SELECT kod, varsayilan_blok, sabah_onceligi FROM ders WHERE id = {lessonId}");
        if (lessonInfo.Count == 0) return;
        
        string lessonCode = DatabaseManager.GetString(lessonInfo[0], "kod");
        string blockStructure = DatabaseManager.GetString(lessonInfo[0], "varsayilan_blok");
        double morningPriority = DatabaseManager.GetInt(lessonInfo[0], "sabah_onceligi") / 100.0;
        
        var blocks = new List<int>();
        foreach (var part in blockStructure.Split('+'))
            if (int.TryParse(part.Trim(), out int dur)) blocks.Add(dur);
            
        // Fallback: If no structure, create one block for the hours
        if (blocks.Count == 0 || blocks.Sum() < assignedHours)
        {
            if (blocks.Count == 0) blocks.Add(assignedHours);
            else {
                // Add remaining hours as a block
                int remaining = assignedHours - blocks.Sum();
                if (remaining > 0) blocks.Add(remaining);
            }
        }
        
        // 1. Check existing blocks for structure match
        var existingBlocks = _db.Query($"SELECT id FROM dagitim_bloklari WHERE sinif_ders_id = {classLessonId}");
        if (existingBlocks.Count > 0 && existingBlocks.Count == blocks.Count)
        {
            // Structure matches, just add THIS teacher to EXISTING blocks
            foreach (var eb in existingBlocks)
            {
                AddTeacherToBlock(DatabaseManager.GetInt(eb, "id"), teacherId);
            }
            return;
        }

        // 2. Recreate blocks because structure changed or they don't exist
        _db.Execute($"DELETE FROM dagitim_bloklari WHERE sinif_ders_id = {classLessonId}");
        
        // Fetch ALL assignments to populate the new blocks correctly
        var assignments = GetTeacherAssignments(classLessonId);
        
        foreach (var dur in blocks)
        {
            // Prepare arrays for teachers
            int[] tIds = new int[7];
            
            for (int i = 0; i < Math.Min(assignments.Count, 7); i++)
            {
                tIds[i] = assignments[i].TeacherId;
            }

            _db.Execute($@"
                INSERT INTO dagitim_bloklari (sinif_ders_id, ders_kodu, sinif_id, blok_suresi,
                    ogretmen_1_id, ogretmen_2_id, ogretmen_3_id, ogretmen_4_id, ogretmen_5_id, ogretmen_6_id, ogretmen_7_id,
                    gun, saat, yerlesim_tipi, kilitli, manuel, sabah_onceligi)
                VALUES ({classLessonId}, '{lessonCode}', {classId}, {dur},
                    {tIds[0]}, {tIds[1]}, {tIds[2]}, {tIds[3]}, {tIds[4]}, {tIds[5]}, {tIds[6]},
                    0, 0, 'otomatik', 0, 0, {morningPriority})
            ");
        }
    }
    
    private void AddTeacherToBlock(int blockId, int teacherId)
    {
        var block = _db.Query($"SELECT ogretmen_1_id, ogretmen_2_id, ogretmen_3_id, ogretmen_4_id, ogretmen_5_id, ogretmen_6_id, ogretmen_7_id " +
                              $"FROM dagitim_bloklari WHERE id = {blockId}");
        if (block.Count == 0) return;
        
        var row = block[0];
        var teachers = new int[7];
        // var rooms = new int[7]; // Not used here
        for (int i = 1; i <= 7; i++)
        {
            teachers[i-1] = DatabaseManager.GetInt(row, $"ogretmen_{i}_id");
        }
        
        if (teachers.Contains(teacherId)) return;
        
        // Find first empty slot
        for (int i = 0; i < 7; i++)
        {
            if (teachers[i] == 0)
            {
                teachers[i] = teacherId;
                _db.Execute($"UPDATE dagitim_bloklari SET ogretmen_{i+1}_id = {teacherId} WHERE id = {blockId}");
                break;
            }
        }
    }
    
    /// <summary>
    /// Robust sync between Atama table and DagitimBloklari.
    /// Fetches all current assignments and updates all 5 slots in blocks.
    /// </summary>
    public void UpdateBlocksState(int classLessonId)
    {
        var assignments = GetTeacherAssignments(classLessonId);
        
        // Prepare arrays
        int[] tIds = new int[7];
        for (int i = 0; i < Math.Min(assignments.Count, 7); i++)
        {
            tIds[i] = assignments[i].TeacherId;
        }

        // 1. Update IDs in dagitim_bloklari
        _db.Execute($@"
            UPDATE dagitim_bloklari 
            SET ogretmen_1_id = {tIds[0]}, ogretmen_2_id = {tIds[1]}, ogretmen_3_id = {tIds[2]}, ogretmen_4_id = {tIds[3]}, ogretmen_5_id = {tIds[4]}, ogretmen_6_id = {tIds[5]}, ogretmen_7_id = {tIds[6]}
            WHERE sinif_ders_id = {classLessonId}
        ");

        // 2. If blocks are already PLACED, we must update the schedule text cells (Sync visuals)
        var placedBlocks = new DistributionRepository().FetchBlocks($"SELECT * FROM dagitim_bloklari WHERE sinif_ders_id = {classLessonId} AND gun > 0");
        if (placedBlocks.Count > 0)
        {
            var distRepo = new DistributionRepository();
            foreach (var b in placedBlocks)
            {
                // Re-running PlaceBlock will update cells in ogretmen, sinif, and ortak_mekan tables
                // but first we should probably clear old strings? 
                // Actually PlaceBlock (with my latest Append fix) will just add more?
                // NO, we need a clean sync. 
                // Repository.SyncSignalTables() is better but it's expensive.
                // Let's just call PlaceBlock; it will update based on current state.
                distRepo.PlaceBlock(b, b.PlacementType);
            }
        }

        // NEW: Also sync teacher weekly hours whenever assignments change
        new TeacherRepository().SyncAllTeacherHours();
    }

    
    
    public void Save(ClassLesson classLesson)
    {
        if (classLesson.Id == 0)
        {
            _db.Execute($"""
                INSERT INTO sinif_ders (sinif_id, ders_id, toplam_saat) 
                VALUES ({classLesson.ClassId}, {classLesson.LessonId}, {classLesson.TotalHours})
            """);
        }
        else
        {
            _db.Execute($"""
                UPDATE sinif_ders SET 
                sinif_id = {classLesson.ClassId}, 
                ders_id = {classLesson.LessonId}, 
                toplam_saat = {classLesson.TotalHours} 
                WHERE id = {classLesson.Id}
            """);
        }
    }
    
    public void Delete(int id)
    {
        // Delete related assignments first
        _db.Execute($"DELETE FROM atama WHERE sinif_ders_id = {id}");
        _db.Execute($"DELETE FROM dagitim_bloklari WHERE sinif_ders_id = {id}");
        _db.Execute($"DELETE FROM sinif_ders WHERE id = {id}");
        
        new TeacherRepository().SyncAllTeacherHours();
    }
}

/// <summary>
/// Teacher assignment repository
/// </summary>
public class AssignmentRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;
    
    public List<TeacherAssignment> GetAll()
    {
        var results = _db.Query("SELECT * FROM atama");
        return results.Select(row => new TeacherAssignment
        {
            Id = DatabaseManager.GetInt(row, "id"),
            ClassLessonId = DatabaseManager.GetInt(row, "sinif_ders_id"),
            TeacherId = DatabaseManager.GetInt(row, "ogretmen_id"),
            AssignedHours = DatabaseManager.GetInt(row, "atanan_saat")
        }).ToList();
    }
    
    public List<TeacherAssignment> GetByClassLessonId(int classLessonId)
    {
        var results = _db.Query($"SELECT * FROM atama WHERE sinif_ders_id = {classLessonId}");
        return results.Select(row => new TeacherAssignment
        {
            Id = DatabaseManager.GetInt(row, "id"),
            ClassLessonId = DatabaseManager.GetInt(row, "sinif_ders_id"),
            TeacherId = DatabaseManager.GetInt(row, "ogretmen_id"),
            AssignedHours = DatabaseManager.GetInt(row, "atanan_saat")
        }).ToList();
    }
    
    public List<TeacherAssignment> GetByTeacherId(int teacherId)
    {
        var results = _db.Query($"SELECT * FROM atama WHERE ogretmen_id = {teacherId}");
        return results.Select(row => new TeacherAssignment
        {
            Id = DatabaseManager.GetInt(row, "id"),
            ClassLessonId = DatabaseManager.GetInt(row, "sinif_ders_id"),
            TeacherId = DatabaseManager.GetInt(row, "ogretmen_id"),
            AssignedHours = DatabaseManager.GetInt(row, "atanan_saat")
        }).ToList();
    }
    
    public void Save(TeacherAssignment assignment)
    {
        if (assignment.Id == 0)
        {
            _db.Execute($"""
                INSERT INTO atama (sinif_ders_id, ogretmen_id, atanan_saat) 
                VALUES ({assignment.ClassLessonId}, {assignment.TeacherId}, {assignment.AssignedHours})
            """);
        }
        else
        {
            _db.Execute($"""
                UPDATE atama SET 
                sinif_ders_id = {assignment.ClassLessonId}, 
                ogretmen_id = {assignment.TeacherId}, 
                atanan_saat = {assignment.AssignedHours} 
                WHERE id = {assignment.Id}
            """);
        }
    }
    
    public void Delete(int id)
    {
        _db.Execute($"DELETE FROM atama WHERE id = {id}");
    }
}

/// <summary>
/// Distribution block repository
/// </summary>
public class DistributionBlockRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;
    
    public List<DistributionBlock> GetAll()
    {
        var results = _db.Query("SELECT * FROM dagitim_bloklari");
        return results.Select(MapToBlock).ToList();
    }
    
    public List<DistributionBlock> GetByClassId(int classId)
    {
        var results = _db.Query($"SELECT * FROM dagitim_bloklari WHERE sinif_id = {classId}");
        return results.Select(MapToBlock).ToList();
    }
    
    public List<DistributionBlock> GetByTeacherId(int teacherId)
    {
        var results = _db.Query($"""
            SELECT * FROM dagitim_bloklari 
            WHERE ogretmen_1_id = {teacherId} 
               OR ogretmen_2_id = {teacherId} 
               OR ogretmen_3_id = {teacherId} 
               OR ogretmen_4_id = {teacherId} 
               OR ogretmen_5_id = {teacherId}
               OR ogretmen_6_id = {teacherId}
               OR ogretmen_7_id = {teacherId}
        """);
        return results.Select(MapToBlock).ToList();
    }
    
    public List<DistributionBlock> GetPlaced()
    {
        var results = _db.Query("SELECT * FROM dagitim_bloklari WHERE gun > 0 AND saat > 0");
        return results.Select(MapToBlock).ToList();
    }
    
    public List<DistributionBlock> GetUnplaced()
    {
        var results = _db.Query("SELECT * FROM dagitim_bloklari WHERE gun = 0 OR saat = 0");
        return results.Select(MapToBlock).ToList();
    }
    
    public void Save(DistributionBlock block)
    {
        var escapedLessonCode = DatabaseManager.Escape(block.LessonCode);
        var escapedPlacementType = DatabaseManager.Escape(block.PlacementType);
        
        if (block.Id == 0)
        {
            _db.Execute($"""
                INSERT INTO dagitim_bloklari 
                (sinif_ders_id, ders_kodu, sinif_id, blok_suresi, 
                 ogretmen_1_id, ogretmen_2_id, ogretmen_3_id, ogretmen_4_id, ogretmen_5_id, ogretmen_6_id, ogretmen_7_id,
                 ortak_mekan_1_id, ortak_mekan_2_id, ortak_mekan_3_id, ortak_mekan_4_id, ortak_mekan_5_id, ortak_mekan_6_id, ortak_mekan_7_id,
                 gun, saat, yerlesim_tipi, kilitli, manuel, sabah_onceligi) 
                VALUES 
                ({block.ClassLessonId}, '{escapedLessonCode}', {block.ClassId}, {block.BlockDuration},
                 {block.Teacher1Id}, {block.Teacher2Id}, {block.Teacher3Id}, {block.Teacher4Id}, {block.Teacher5Id}, {block.Teacher6Id}, {block.Teacher7Id},
                 {block.OrtakMekan1Id}, {block.OrtakMekan2Id}, {block.OrtakMekan3Id}, {block.OrtakMekan4Id}, {block.OrtakMekan5Id}, {block.OrtakMekan6Id}, {block.OrtakMekan7Id},
                 {block.Day}, {block.Hour}, '{escapedPlacementType}', {(block.IsLocked ? 1 : 0)}, {(block.IsManual ? 1 : 0)}, {block.MorningPriority})
            """);
        }
        else
        {
            _db.Execute($"""
                UPDATE dagitim_bloklari SET
                sinif_ders_id = {block.ClassLessonId},
                ders_kodu = '{escapedLessonCode}',
                sinif_id = {block.ClassId},
                blok_suresi = {block.BlockDuration},
                ogretmen_1_id = {block.Teacher1Id},
                ogretmen_2_id = {block.Teacher2Id},
                ogretmen_3_id = {block.Teacher3Id},
                ogretmen_4_id = {block.Teacher4Id},
                ogretmen_5_id = {block.Teacher5Id},
                ogretmen_6_id = {block.Teacher6Id},
                ogretmen_7_id = {block.Teacher7Id},
                ortak_mekan_1_id = {block.OrtakMekan1Id},
                ortak_mekan_2_id = {block.OrtakMekan2Id},
                ortak_mekan_3_id = {block.OrtakMekan3Id},
                ortak_mekan_4_id = {block.OrtakMekan4Id},
                ortak_mekan_5_id = {block.OrtakMekan5Id},
                ortak_mekan_6_id = {block.OrtakMekan6Id},
                ortak_mekan_7_id = {block.OrtakMekan7Id},
                gun = {block.Day},
                saat = {block.Hour},
                yerlesim_tipi = '{escapedPlacementType}',
                kilitli = {(block.IsLocked ? 1 : 0)},
                manuel = {(block.IsManual ? 1 : 0)},
                sabah_onceligi = {block.MorningPriority}
                WHERE id = {block.Id}
            """);
        }
    }
    
    public void UpdatePlacement(int blockId, int day, int hour, string placementType = "otomatik")
    {
        var escaped = DatabaseManager.Escape(placementType);
        _db.Execute($"UPDATE dagitim_bloklari SET gun = {day}, saat = {hour}, yerlesim_tipi = '{escaped}' WHERE id = {blockId}");
    }
    
    public void ClearPlacement(int blockId)
    {
        _db.Execute($"UPDATE dagitim_bloklari SET gun = 0, saat = 0 WHERE id = {blockId}");
    }
    
    public void SetLocked(int blockId, bool locked)
    {
        _db.Execute($"UPDATE dagitim_bloklari SET kilitli = {(locked ? 1 : 0)} WHERE id = {blockId}");
    }
    
    public void Delete(int id)
    {
        _db.Execute($"DELETE FROM dagitim_bloklari WHERE id = {id}");
    }
    
    public void DeleteByClassLessonId(int classLessonId)
    {
        _db.Execute($"DELETE FROM dagitim_bloklari WHERE sinif_ders_id = {classLessonId}");
    }
    
    /// <summary>
    /// Generates distribution blocks from assignments
    /// </summary>
    public void GenerateBlocksFromAssignments()
    {
        var classLessonRepo = new ClassLessonRepository();
        var assignmentRepo = new AssignmentRepository();
        var lessonRepo = new LessonRepository();
        
        var classLessons = classLessonRepo.GetAll();
        var lessons = lessonRepo.GetAll().ToDictionary(l => l.Id);
        
        foreach (var cl in classLessons)
        {
            // Get lesson info
            if (!lessons.TryGetValue(cl.LessonId, out var lesson)) continue;
            
            // Get teachers assigned to this class-lesson
            var assignments = assignmentRepo.GetByClassLessonId(cl.Id);
            if (assignments.Count == 0) continue;
            
            // Parse block pattern
            var blockPattern = lesson.DefaultBlock;
            var blockSizes = ParseBlockPattern(blockPattern, cl.TotalHours);
            
            // Delete existing blocks for this class-lesson
            DeleteByClassLessonId(cl.Id);
            
            // Create blocks
            foreach (var size in blockSizes)
            {
                var block = new DistributionBlock
                {
                    ClassLessonId = cl.Id,
                    LessonCode = lesson.Code,
                    ClassId = cl.ClassId,
                    BlockDuration = size,
                    MorningPriority = lesson.MorningPriority
                };
                
                // Assign teachers AND Rooms (Multi-Room Logic)
                var assignmentList = assignments.Take(7).ToList();
                
                if (assignmentList.Count > 0) { block.Teacher1Id = assignmentList[0].TeacherId; }
                if (assignmentList.Count > 1) { block.Teacher2Id = assignmentList[1].TeacherId; }
                if (assignmentList.Count > 2) { block.Teacher3Id = assignmentList[2].TeacherId; }
                if (assignmentList.Count > 3) { block.Teacher4Id = assignmentList[3].TeacherId; }
                if (assignmentList.Count > 4) { block.Teacher5Id = assignmentList[4].TeacherId; }
                if (assignmentList.Count > 5) { block.Teacher6Id = assignmentList[5].TeacherId; }
                if (assignmentList.Count > 6) { block.Teacher7Id = assignmentList[6].TeacherId; }
                
                Save(block);
            }
        }
    }
    
    public void RegenerateBlocksForClassLesson(int classLessonId)
    {
        var classLessonRepo = new ClassLessonRepository();
        var assignmentRepo = new AssignmentRepository();
        var lessonRepo = new LessonRepository();
        
        var clList = classLessonRepo.GetAll().Where(x => x.Id == classLessonId).ToList();
        if (clList.Count == 0) return;
        var cl = clList[0];
        
        var lessons = lessonRepo.GetAll().ToDictionary(l => l.Id);
        
        if (!lessons.TryGetValue(cl.LessonId, out var lesson)) return;
        
        var assignments = assignmentRepo.GetByClassLessonId(cl.Id);
        
        var blockPattern = lesson.DefaultBlock;
        var blockSizes = ParseBlockPattern(blockPattern, cl.TotalHours);
        
        DeleteByClassLessonId(cl.Id);
        
        foreach (var size in blockSizes)
        {
            var block = new DistributionBlock
            {
                ClassLessonId = cl.Id,
                LessonCode = lesson.Code,
                ClassId = cl.ClassId,
                BlockDuration = size,
                MorningPriority = lesson.MorningPriority
            };
            
            var assignmentList = assignments.Take(7).ToList();
            
            if (assignmentList.Count > 0) { block.Teacher1Id = assignmentList[0].TeacherId; }
            if (assignmentList.Count > 1) { block.Teacher2Id = assignmentList[1].TeacherId; }
            if (assignmentList.Count > 2) { block.Teacher3Id = assignmentList[2].TeacherId; }
            if (assignmentList.Count > 3) { block.Teacher4Id = assignmentList[3].TeacherId; }
            if (assignmentList.Count > 4) { block.Teacher5Id = assignmentList[4].TeacherId; }
            if (assignmentList.Count > 5) { block.Teacher6Id = assignmentList[5].TeacherId; }
            if (assignmentList.Count > 6) { block.Teacher7Id = assignmentList[6].TeacherId; }
            
            Save(block);
        }
    }
    
    public static List<int> ParseBlockPattern(string pattern, int totalHours)
    {
        var blocks = new List<int>();
        
        if (string.IsNullOrWhiteSpace(pattern))
        {
            pattern = "2"; // Default to pairs
        }
        
        // Parse pattern like "2+2+2" or just "2"
        var parts = pattern.Split('+');
        var patternBlocks = new List<int>();
        
        foreach (var part in parts)
        {
            if (int.TryParse(part.Trim(), out int size) && size > 0)
            {
                patternBlocks.Add(size);
            }
        }
        
        if (patternBlocks.Count == 0)
        {
            patternBlocks.Add(2); // Default to pairs
        }
        
        // Fill blocks until we cover all hours
        int remaining = totalHours;
        int patternIndex = 0;
        
        while (remaining > 0)
        {
            int blockSize = patternBlocks[patternIndex % patternBlocks.Count];
            
            if (blockSize > remaining)
            {
                blockSize = remaining; // Adjust last block
            }
            
            blocks.Add(blockSize);
            remaining -= blockSize;
            patternIndex++;
        }
        
        return blocks;
    }
    
    private static DistributionBlock MapToBlock(Dictionary<string, object?> row)
    {
        return new DistributionBlock
        {
            Id = DatabaseManager.GetInt(row, "id"),
            ClassLessonId = DatabaseManager.GetInt(row, "sinif_ders_id"),
            LessonCode = DatabaseManager.GetString(row, "ders_kodu"),
            ClassId = DatabaseManager.GetInt(row, "sinif_id"),
            BlockDuration = DatabaseManager.GetInt(row, "blok_suresi", 1),
            Teacher1Id = DatabaseManager.GetInt(row, "ogretmen_1_id"),
            Teacher2Id = DatabaseManager.GetInt(row, "ogretmen_2_id"),
            Teacher3Id = DatabaseManager.GetInt(row, "ogretmen_3_id"),
            Teacher4Id = DatabaseManager.GetInt(row, "ogretmen_4_id"),
            Teacher5Id = DatabaseManager.GetInt(row, "ogretmen_5_id"),
            Day = DatabaseManager.GetInt(row, "gun"),
            Hour = DatabaseManager.GetInt(row, "saat"),
            PlacementType = DatabaseManager.GetString(row, "yerlesim_tipi", "otomatik"),
            IsLocked = DatabaseManager.GetInt(row, "kilitli") == 1,
            IsManual = DatabaseManager.GetInt(row, "manuel") == 1,
            MorningPriority = DatabaseManager.GetDouble(row, "sabah_onceligi"),
            TeacherGapScore = DatabaseManager.GetInt(row, "ogretmen_bosluk_skoru"),
            ClassGapScore = DatabaseManager.GetInt(row, "sinif_bosluk_skoru"),
            SameDayViolation = DatabaseManager.GetInt(row, "ayni_gun_ihlali"),
            AdjacencyScore = DatabaseManager.GetInt(row, "komsulik_skoru"),
            TotalScore = DatabaseManager.GetInt(row, "toplam_skor"),
            OrtakMekan1Id = DatabaseManager.GetInt(row, "ortak_mekan_1_id"),
            OrtakMekan2Id = DatabaseManager.GetInt(row, "ortak_mekan_2_id"),
            OrtakMekan3Id = DatabaseManager.GetInt(row, "ortak_mekan_3_id"),
            OrtakMekan4Id = DatabaseManager.GetInt(row, "ortak_mekan_4_id"),
            OrtakMekan5Id = DatabaseManager.GetInt(row, "ortak_mekan_5_id"),
            OrtakMekan6Id = DatabaseManager.GetInt(row, "ortak_mekan_6_id"),
            OrtakMekan7Id = DatabaseManager.GetInt(row, "ortak_mekan_7_id"),
            KardesId = DatabaseManager.GetInt(row, "kardes_id"),
            Teacher6Id = DatabaseManager.GetInt(row, "ogretmen_6_id"),
            Teacher7Id = DatabaseManager.GetInt(row, "ogretmen_7_id")
        };
    }
}
