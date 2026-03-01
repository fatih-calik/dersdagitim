namespace DersDagitim.Models;

/// <summary>
/// Lesson definition
/// </summary>
public class Lesson
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultBlock { get; set; } = "2";
    public int MorningPriority { get; set; }
    // Building properties removed as requested
    // public int BuildingId { get; set; }
    // public string BuildingName { get; set; } = string.Empty;
    // public string BuildingColor { get; set; } = "#808080";
    
    // Initial letter for circle
    public string Initial => !string.IsNullOrEmpty(Code) ? Code.Substring(0, Math.Min(2, Code.Length)).ToUpper() : (!string.IsNullOrEmpty(Name) ? Name.Substring(0, 1).ToUpper() : "?");
    
    // Background color based on name length (pseudo-random)
    public string InitialColor 
    {
        get 
        {
            var colors = new[] { "#EFF6FF", "#F0FDF4", "#FEF2F2", "#FFF7ED", "#F5F3FF" }; // Blue, Green, Red, Orange, Violet (light)
            int index = (Name != null ? (Name.GetHashCode() & 0x7FFFFFFF) : 0) % colors.Length;
            return colors[index];
        }
    }

    public string InitialTextColor
    {
        get
        {
            var textColors = new[] { "#1D4ED8", "#15803D", "#B91C1C", "#C2410C", "#6D28D9" };
            int index = (Name != null ? (Name.GetHashCode() & 0x7FFFFFFF) : 0) % textColors.Length;
            return textColors[index];
        }
    }
}
