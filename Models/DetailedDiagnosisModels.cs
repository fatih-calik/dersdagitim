using System.Collections.Generic;

namespace DersDagitim.Models
{
    public class DetailedClassNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<DetailedLessonNode> Lessons { get; set; } = new();
        public int TotalHours { get; set; }
    }

    public class DetailedLessonNode
    {
        public int Id { get; set; } // sinif_ders_id
        public string LessonCode { get; set; } = string.Empty;
        public string LessonName { get; set; } = string.Empty;
        public int TotalHours { get; set; }
        public List<DetailedAssignmentNode> Assignments { get; set; } = new();
        public List<DetailedBlockNode> Blocks { get; set; } = new();
        public int KardesId { get; set; }
        public bool IsKardes => KardesId > 0;
    }

    public class DetailedAssignmentNode
    {
        public int Id { get; set; }
        public string TeacherName { get; set; } = string.Empty;
        public int AssignedHours { get; set; }
    }

    public class DetailedBlockNode
    {
        public int Id { get; set; }
        public int Duration { get; set; }
        public int Day { get; set; }
        public int Hour { get; set; }
        public string Status { get; set; } = string.Empty;
        public string LessonName { get; set; } = string.Empty;
        public int KardesId { get; set; }
        public List<string> TeacherNames { get; set; } = new();
        public bool IsPlaced => Day > 0 && Hour > 0;
        public bool IsKardes => KardesId > 0;
    }

    public class DetailedRoomNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TotalHours { get; set; }
        public List<DetailedBlockNode> Blocks { get; set; } = new();
    }

    public class DetailedCombinedGroupNode
    {
        public int Id { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public string LessonName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int BlockCount { get; set; }
        public int TotalHours { get; set; }
        public List<DetailedGroupItem> GroupItems { get; set; } = new();
    }

    public class DetailedGroupItem
    {
        public string ClassName { get; set; } = string.Empty;
        public string LessonName { get; set; } = string.Empty;
        public int TotalHours { get; set; }
    }
}
