namespace DersDagitim.Models;

/// <summary>
/// Represents a single allocatable unit of a lesson
/// </summary>
public class LessonBlock
{
    public int Id { get; set; }
    public string LessonCode { get; set; } = string.Empty;
    public List<int> TeacherIds { get; set; } = new();
    public List<int> AssignmentIds { get; set; } = new();
    public int ClassId { get; set; }
    public int Duration { get; set; } = 1;
    public double MorningPriority { get; set; }
    public bool IsLocked { get; set; }
    public bool IsManual { get; set; }
    public int GroupId { get; set; } // Kardeş Ders ID

    
    /// <summary>
    /// Compatibility - first teacher ID
    /// </summary>
    public int TeacherId => TeacherIds.FirstOrDefault();
}

/// <summary>
/// Represents the assignment of a block to a specific time
/// </summary>
public class Placement
{
    public int BlockId { get; set; }
    public TimeSlot StartSlot { get; set; }
    
    public Placement(int blockId, TimeSlot startSlot)
    {
        BlockId = blockId;
        StartSlot = startSlot;
    }
}

/// <summary>
/// Complete state of the schedule at any point in time
/// </summary>
public class ScheduleState
{
    /// <summary>
    /// blockId -> Placement
    /// </summary>
    public Dictionary<int, Placement> Placements { get; set; } = new();

    /// <summary>
    /// teacherId -> [TimeSlot: BlockId]
    /// </summary>
    public Dictionary<int, Dictionary<TimeSlot, int>> TeacherSchedule { get; set; } = new();

    /// <summary>
    /// classId -> [TimeSlot: BlockId]
    /// </summary>
    public Dictionary<int, Dictionary<TimeSlot, int>> ClassSchedule { get; set; } = new();

    public int MaxDays { get; }
    public int MaxHours { get; }
    public Dictionary<int, Teacher> Teachers { get; }
    public Dictionary<int, SchoolClass> Classes { get; }
    public Dictionary<int, OrtakMekan> Rooms { get; }

    private readonly List<DistributionBlock> _allBlocks;
    private readonly SchoolInfo _schoolInfo;

    public ScheduleState()
    {
        MaxDays = 5;
        MaxHours = 8;
        Teachers = new();
        Classes = new();
        Rooms = new();
        _allBlocks = new();
        _schoolInfo = new();
    }

    public ScheduleState(
        List<DistributionBlock> blocks,
        Dictionary<int, Teacher> teachers,
        Dictionary<int, SchoolClass> classes,
        Dictionary<int, OrtakMekan> rooms,
        SchoolInfo schoolInfo,
        int maxDays,
        int maxHours)
    {
        _allBlocks = blocks;
        Teachers = teachers;
        Classes = classes;
        Rooms = rooms;
        _schoolInfo = schoolInfo;
        MaxDays = maxDays;
        MaxHours = maxHours;
    }

    public List<DistributionBlock> GetAllBlocks() => _allBlocks;

    public bool IsSchoolOpen(int day, int hour)
    {
        var slot = new TimeSlot(day, hour);
        if (_schoolInfo.DefaultTimetable.TryGetValue(slot, out var state))
            return state == SlotState.Open;
        return true;
    }

    public bool IsTeacherOpen(int teacherId, int day, int hour)
    {
        if (!Teachers.TryGetValue(teacherId, out var teacher)) return true;
        var slot = new TimeSlot(day, hour);
        if (teacher.Constraints.TryGetValue(slot, out var state))
            return state == SlotState.Open;
        return true;
    }

    public bool IsClassOpen(int classId, int day, int hour)
    {
        if (!Classes.TryGetValue(classId, out var cls)) return true;
        var slot = new TimeSlot(day, hour);
        if (cls.Constraints.TryGetValue(slot, out var state))
            return state == SlotState.Open;
        return true;
    }

    public bool IsRoomOpen(int roomId, int day, int hour)
    {
        if (!Rooms.TryGetValue(roomId, out var room)) return true;
        var slot = new TimeSlot(day, hour);
        if (room.Constraints.TryGetValue(slot, out var state))
            return state == SlotState.Open;
        return true;
    }
}

/// <summary>
/// Represents a lesson assigned to a class (without teacher info)
/// </summary>
public class ClassLesson
{
    public int Id { get; set; }
    public int ClassId { get; set; }
    public int LessonId { get; set; }
    public int TotalHours { get; set; }
    public int KardesId { get; set; } // Unified Lesson Group ID
}

/// <summary>
/// Represents a teacher assignment to a class lesson
/// </summary>
public class TeacherAssignment
{
    public int Id { get; set; }
    public int ClassLessonId { get; set; }
    public int TeacherId { get; set; }
    public int AssignedHours { get; set; }
}

/// <summary>
/// Represents a distribution block in dagitim_bloklari table
/// </summary>
public class DistributionBlock
{
    public int Id { get; set; }
    public int ClassLessonId { get; set; }
    public string LessonCode { get; set; } = string.Empty;
    public int ClassId { get; set; }
    public int BlockDuration { get; set; }
    
    // Multi-teacher support
    public int Teacher1Id { get; set; }
    public int Teacher2Id { get; set; }
    public int Teacher3Id { get; set; }
    public int Teacher4Id { get; set; }
    public int Teacher5Id { get; set; }
    public int Teacher6Id { get; set; }
    public int Teacher7Id { get; set; }
    
    // Placement information
    public int Day { get; set; }
    public int Hour { get; set; }
    public string PlacementType { get; set; } = "otomatik";
    public bool IsLocked { get; set; }
    public bool IsManual { get; set; }
    
    // Scoring fields
    public double MorningPriority { get; set; }
    public int TeacherGapScore { get; set; }
    public int ClassGapScore { get; set; }
    public int SameDayViolation { get; set; }
    public int AdjacencyScore { get; set; }
    public int TotalScore { get; set; }
    public int KardesId { get; set; } // Unified Lesson ID
    
    // New Individual Room Assignments per Teacher
    public int OrtakMekan1Id { get; set; }
    public int OrtakMekan2Id { get; set; }
    public int OrtakMekan3Id { get; set; }
    public int OrtakMekan4Id { get; set; }
    public int OrtakMekan5Id { get; set; }
    public int OrtakMekan6Id { get; set; }
    public int OrtakMekan7Id { get; set; }
    
    /// <summary>
    /// Helper to get all assigned room IDs for this block
    /// </summary>
    public List<int> GetOrtakMekanIds()
    {
        var list = new List<int>();
        if (OrtakMekan1Id > 0) list.Add(OrtakMekan1Id);
        if (OrtakMekan2Id > 0) list.Add(OrtakMekan2Id);
        if (OrtakMekan3Id > 0) list.Add(OrtakMekan3Id);
        if (OrtakMekan4Id > 0) list.Add(OrtakMekan4Id);
        if (OrtakMekan5Id > 0) list.Add(OrtakMekan5Id);
        if (OrtakMekan6Id > 0) list.Add(OrtakMekan6Id);
        if (OrtakMekan7Id > 0) list.Add(OrtakMekan7Id);
        return list.Distinct().ToList();
    }

    /// <summary>
    /// Backward compatibility helper (returns first non-zero room or 0)
    /// </summary>
    public int OrtakMekanId => OrtakMekan1Id; 
    
    /// <summary>
    /// All teacher IDs (non-zero)
    /// </summary>
    public List<int> TeacherIds => new[] { Teacher1Id, Teacher2Id, Teacher3Id, Teacher4Id, Teacher5Id, Teacher6Id, Teacher7Id }
        .Where(id => id > 0).ToList();

    public int TeacherId => TeacherIds.FirstOrDefault();
    
    public bool IsPlaced => Day > 0 && Hour > 0;

    public DistributionBlock Clone()
    {
        return (DistributionBlock)this.MemberwiseClone();
    }
}

/// <summary>
/// Club
/// </summary>
public class Club
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Duty location
/// </summary>
public class DutyLocation
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Building/Location
/// </summary>
public class Building
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#808080";
}

/// <summary>
/// Ortak Mekan (Shared Location)
/// </summary>
public class OrtakMekan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// TimeSlot -> Content (Class + Lesson + Teacher)
    /// </summary>
    public Dictionary<TimeSlot, string> ScheduleInfo { get; set; } = new();

    /// <summary>
    /// Availability constraints (Open/Closed)
    /// </summary>
    public Dictionary<TimeSlot, SlotState> Constraints { get; set; } = new();
}
