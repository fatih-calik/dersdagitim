namespace DersDagitim.Models;

/// <summary>
/// School class entity model
/// </summary>
public class SchoolClass
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Dictionary<TimeSlot, SlotState> Constraints { get; set; } = new();
    public Dictionary<string, string> Schedule { get; set; } = new();
}
