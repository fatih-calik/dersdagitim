using DersDagitim.Models;
using DersDagitim.Persistence;
using System.Collections.Generic;
using System.Linq;

namespace DersDagitim.Services
{
    public class DetailedDiagnosisService
    {
        private readonly DatabaseManager _db = DatabaseManager.Shared;

        public List<DetailedClassNode> GetFullHierarchy()
        {
            var classes = FetchClasses();
            var classLessons = FetchClassLessons();
            var assignments = FetchAssignments();
            var blocks = FetchBlocks();
            var teachers = FetchTeachers();

            var result = new List<DetailedClassNode>();

            foreach (var cls in classes)
            {
                var classNode = new DetailedClassNode
                {
                    Id = cls.Id,
                    Name = cls.Name
                };

                var lessonsForClass = classLessons.Where(cl => cl.ClassId == cls.Id).ToList();
                foreach (var cl in lessonsForClass)
                {
                    var lessonNode = new DetailedLessonNode
                    {
                        Id = cl.Id,
                        LessonCode = cl.LessonCode,
                        LessonName = cl.LessonName,
                        TotalHours = cl.TotalHours,
                        KardesId = cl.KardesId
                    };

                    // Assignments for this lesson
                    var assignmentsForLesson = assignments.Where(a => a.ClassLessonId == cl.Id).ToList();
                    foreach (var a in assignmentsForLesson)
                    {
                        lessonNode.Assignments.Add(new DetailedAssignmentNode
                        {
                            Id = a.Id,
                            TeacherName = teachers.ContainsKey(a.TeacherId) ? teachers[a.TeacherId] : $"Bilinmeyen Öğretmen #{a.TeacherId}",
                            AssignedHours = a.AssignedHours
                        });
                    }

                    // Blocks for this lesson
                    var blocksForLesson = blocks.Where(b => b.ClassLessonId == cl.Id).ToList();
                    foreach (var b in blocksForLesson)
                    {
                        var blockNode = new DetailedBlockNode
                        {
                            Id = b.Id,
                            Duration = b.BlockDuration,
                            Day = b.Day,
                            Hour = b.Hour,
                            LessonName = b.LessonCode,
                            KardesId = b.KardesId,
                            Status = b.Day > 0 ? $"{b.Day}.Gün {b.Hour}.Saat" : "Yerleşmemiş"
                        };

                        // Add teacher names to block
                        if (b.Teacher1Id > 0 && teachers.ContainsKey(b.Teacher1Id)) blockNode.TeacherNames.Add(teachers[b.Teacher1Id]);
                        if (b.Teacher2Id > 0 && teachers.ContainsKey(b.Teacher2Id)) blockNode.TeacherNames.Add(teachers[b.Teacher2Id]);
                        if (b.Teacher3Id > 0 && teachers.ContainsKey(b.Teacher3Id)) blockNode.TeacherNames.Add(teachers[b.Teacher3Id]);
                        if (b.Teacher4Id > 0 && teachers.ContainsKey(b.Teacher4Id)) blockNode.TeacherNames.Add(teachers[b.Teacher4Id]);
                        if (b.Teacher5Id > 0 && teachers.ContainsKey(b.Teacher5Id)) blockNode.TeacherNames.Add(teachers[b.Teacher5Id]);

                        lessonNode.Blocks.Add(blockNode);
                    }

                    classNode.Lessons.Add(lessonNode);
                }

                classNode.TotalHours = classNode.Lessons.Sum(l => l.TotalHours);
                result.Add(classNode);
            }

            return result.OrderBy(c => c.Name).ToList();
        }

        public List<DetailedRoomNode> GetRoomHierarchy()
        {
            var rooms = FetchRooms();
            var blocks = FetchBlocks();
            var classes = FetchClasses().ToDictionary(c => c.Id, c => c.Name);
            var result = new List<DetailedRoomNode>();

            foreach (var room in rooms)
            {
                var roomNode = new DetailedRoomNode
                {
                    Id = room.Id,
                    Name = room.Name
                };

                // Check all 5 ortak_mekan slots
                var roomBlocks = blocks.Where(b => 
                    b.OrtakMekan1Id == room.Id || 
                    b.OrtakMekan2Id == room.Id || 
                    b.OrtakMekan3Id == room.Id || 
                    b.OrtakMekan4Id == room.Id || 
                    b.OrtakMekan5Id == room.Id).ToList();

                foreach (var b in roomBlocks)
                {
                    roomNode.Blocks.Add(new DetailedBlockNode
                    {
                        Id = b.Id,
                        Duration = b.BlockDuration,
                        Day = b.Day,
                        Hour = b.Hour,
                        LessonName = b.LessonCode,
                        KardesId = b.KardesId,
                        Status = classes.ContainsKey(b.ClassId) ? classes[b.ClassId] : $"Sınıf #{b.ClassId}"
                    });
                }

                roomNode.TotalHours = roomNode.Blocks.Sum(b => b.Duration);
                if (roomNode.Blocks.Count > 0) result.Add(roomNode);
            }

            return result.OrderBy(r => r.Name).ToList();
        }

        public List<DetailedCombinedGroupNode> GetCombinedHierarchy()
        {
            var blocks = FetchBlocks();
            var classes = FetchClasses().ToDictionary(c => c.Id, c => c.Name);
            var classLessons = FetchClassLessons().ToDictionary(cl => cl.Id, cl => cl);
            var groupNames = FetchGroupNames();
            var result = new List<DetailedCombinedGroupNode>();

            var groups = blocks.Where(b => b.KardesId > 0).GroupBy(b => b.KardesId);

            foreach (var grp in groups)
            {
                var groupNode = new DetailedCombinedGroupNode
                {
                    Id = grp.Key,
                    GroupName = groupNames.ContainsKey(grp.Key) ? groupNames[grp.Key] : "İsimsiz Grup",
                    BlockCount = grp.Count(),
                    TotalHours = grp.Sum(b => b.BlockDuration)
                };

                // Group by ClassLessonId to get the unique assignments in this group
                var subGroups = grp.GroupBy(b => b.ClassLessonId);
                string firstLessonCode = "";
                string firstLessonName = "";

                foreach (var sg in subGroups)
                {
                    var lessonId = sg.Key;
                    var firstBlock = sg.First();
                    
                    var item = new DetailedGroupItem();
                    item.ClassName = classes.ContainsKey(firstBlock.ClassId) ? classes[firstBlock.ClassId] : $"Sınıf #{firstBlock.ClassId}";
                    
                    if (classLessons.ContainsKey(lessonId))
                    {
                        var cl = classLessons[lessonId];
                        item.LessonName = cl.LessonName;
                        item.TotalHours = cl.TotalHours;
                        
                        if (string.IsNullOrEmpty(firstLessonCode)) { firstLessonCode = cl.LessonCode; firstLessonName = cl.LessonName; }
                    }
                    else
                    {
                        item.LessonName = firstBlock.LessonCode;
                        item.TotalHours = sg.Sum(b => b.BlockDuration);
                        if (string.IsNullOrEmpty(firstLessonCode)) firstLessonCode = firstBlock.LessonCode;
                    }
                    
                    groupNode.GroupItems.Add(item);
                }
                
                groupNode.LessonName = firstLessonCode;
                groupNode.Title = $"{groupNode.GroupName} - {firstLessonCode} {firstLessonName}".Trim(' ', '-');

                result.Add(groupNode);
            }

            return result.OrderBy(g => g.Id).ToList();
        }

        private Dictionary<int, string> FetchGroupNames()
        {
            return _db.Query("SELECT id, ad FROM kardes_gruplar").ToDictionary(
                row => DatabaseManager.GetInt(row, "id"),
                row => DatabaseManager.GetString(row, "ad")
            );
        }

        private List<SchoolClass> FetchClasses()
        {
            return _db.Query("SELECT id, ad FROM sinif").Select(row => new SchoolClass
            {
                Id = DatabaseManager.GetInt(row, "id"),
                Name = DatabaseManager.GetString(row, "ad")
            }).ToList();
        }

        private List<DetailedLessonInfo> FetchClassLessons()
        {
            return _db.Query(@"
                SELECT sd.*, d.kod, d.ad, IFNULL(kb.kardes_id, 0) as kardes_id 
                FROM sinif_ders sd 
                JOIN ders d ON sd.ders_id = d.id
                LEFT JOIN kardes_bloklar kb ON sd.id = kb.sinif_ders_id")
            .Select(row => new DetailedLessonInfo
            {
                Id = DatabaseManager.GetInt(row, "id"),
                ClassId = DatabaseManager.GetInt(row, "sinif_id"),
                LessonName = DatabaseManager.GetString(row, "ad"),
                LessonCode = DatabaseManager.GetString(row, "kod"),
                TotalHours = DatabaseManager.GetInt(row, "toplam_saat"),
                KardesId = DatabaseManager.GetInt(row, "kardes_id")
            }).ToList();
        }

        private List<TeacherAssignment> FetchAssignments()
        {
            return _db.Query("SELECT * FROM atama").Select(row => new TeacherAssignment
            {
                Id = DatabaseManager.GetInt(row, "id"),
                ClassLessonId = DatabaseManager.GetInt(row, "sinif_ders_id"),
                TeacherId = DatabaseManager.GetInt(row, "ogretmen_id"),
                AssignedHours = DatabaseManager.GetInt(row, "atanan_saat")
            }).ToList();
        }

        private List<DistributionBlock> FetchBlocks()
        {
            return _db.Query("SELECT * FROM dagitim_bloklari").Select(row => new DistributionBlock
            {
                Id = DatabaseManager.GetInt(row, "id"),
                ClassLessonId = DatabaseManager.GetInt(row, "sinif_ders_id"),
                ClassId = DatabaseManager.GetInt(row, "sinif_id"),
                LessonCode = DatabaseManager.GetString(row, "ders_kodu"),
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
                KardesId = DatabaseManager.GetInt(row, "kardes_id")
            }).ToList();
        }

        private List<OrtakMekan> FetchRooms()
        {
            return _db.Query("SELECT id, ad FROM ortak_mekan").Select(row => new OrtakMekan
            {
                Id = DatabaseManager.GetInt(row, "id"),
                Name = DatabaseManager.GetString(row, "ad")
            }).ToList();
        }

        private Dictionary<int, string> FetchTeachers()
        {
            return _db.Query("SELECT id, ad_soyad FROM ogretmen").ToDictionary(
                row => DatabaseManager.GetInt(row, "id"),
                row => DatabaseManager.GetString(row, "ad_soyad")
            );
        }

        private class DetailedLessonInfo
        {
            public int Id { get; set; }
            public int ClassId { get; set; }
            public string LessonName { get; set; } = "";
            public string LessonCode { get; set; } = "";
            public int TotalHours { get; set; }
            public int KardesId { get; set; }
        }
    }
}
