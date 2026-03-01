namespace DersDagitim.Models;

/// <summary>
/// School settings and info
/// </summary>
public class SchoolInfo
{
    public string Name { get; set; } = string.Empty;
    public string Principal { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public int Days { get; set; } = 5;
    public int DailyLessonCount { get; set; } = 8;
    public Dictionary<TimeSlot, SlotState> DefaultTimetable { get; set; } = new();
    public Dictionary<string, string> Schedule { get; set; } = new();
    public string[] LessonHours { get; set; } = new string[12];

    public string Version { get; set; } = "1.0.0.0";
    public string LastUpdateDate { get; set; } = "";
    public Dictionary<int, string> CommonRooms { get; set; } = new();
    public int V3GapPenalty { get; set; } = 100;
}
