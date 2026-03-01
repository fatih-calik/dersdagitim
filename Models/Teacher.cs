namespace DersDagitim.Models;

/// <summary>
/// Teacher definition
/// </summary>
public class Teacher
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string TcNo { get; set; } = "";
    public string Position { get; set; } = "ogretmen"; // Görevi (Müdür, Öğretmen vb.)
    
    // Duty (Nöbet)
    public string DutyDay { get; set; } = "";
    public string DutyLocation { get; set; } = "";
    public string Club { get; set; } = "";
    public string Branch { get; set; } = ""; // Branşı (Matematik, Türkçe vb.)
    
    // BranchIcon property removed as requested
    
    public int Guidance { get; set; } // Rehberlik (Legacy: Integer)
    public int MaxHours { get; set; } = 20;
    public int MaxHoursPerDay { get; set; } = 8;
    
    // Extra Lessons Status
    public bool HasExtraLessons { get; set; } // Ek ders var mı?
    
    // UI Helpers
    public string DutyLocationName { get; set; } = string.Empty;
    public string ClubName { get; set; } = string.Empty;
    
    // Initial letter for circle
    public string Initial => !string.IsNullOrEmpty(Name) ? Name.Substring(0, 1).ToUpper() : "?";
    
    // Background color based on name (consistent)
    public string InitialColor 
    {
        get 
        {
            var colors = new[] { "#EFF6FF", "#F0FDF4", "#FEF2F2", "#FFF7ED", "#F5F3FF" }; // Blue, Green, Red, Orange, Violet (light)
            int index = Name != null ? (Name.GetHashCode() & 0x7FFFFFFF) % colors.Length : 0;
            return colors[index];
        }
    }

    public string InitialTextColor
    {
        get
        {
            var textColors = new[] { "#1D4ED8", "#15803D", "#B91C1C", "#C2410C", "#6D28D9" };
            int index = Name != null ? (Name.GetHashCode() & 0x7FFFFFFF) % textColors.Length : 0;
            return textColors[index];
        }
    }
    
    // Timetable for constraints (Closed hours)
    // Format: "d_X_Y" -> "KAPALI"
    public Dictionary<TimeSlot, SlotState> Constraints { get; set; } = new();
    
    // Legacy Schedule Text (d_X_Y content when not KAPALI)
    public Dictionary<TimeSlot, string> ScheduleInfo { get; set; } = new();
    
    public int TotalAssignedHours { get; set; }

    // Ek Ders Arrays (7 days each: Pts-Pazar)
    public int[] EkDersGunduz101 { get; set; } = new int[7];
    public int[] EkDersGece102 { get; set; } = new int[7];
    public int[] EkDersFazlaGunduz103 { get; set; } = new int[7];
    public int[] EkDersFazlaGece104 { get; set; } = new int[7];
    public int[] EkDersBelleticilik106 { get; set; } = new int[7];
    public int[] EkDersSinav107 { get; set; } = new int[7];
    public int[] EkDersEgzersiz108 { get; set; } = new int[7];
    public int[] EkDersHizmetIci109 { get; set; } = new int[7];
    public int[] EkDersEDYGG110 { get; set; } = new int[7];
    public int[] EkDersEDYGGGece111 { get; set; } = new int[7];
    public int[] EkDersEDYGGFazlaGunduz112 { get; set; } = new int[7];
    public int[] EkDersEDYGGFazlaGece113 { get; set; } = new int[7];
    public int[] EkDersAtis114 { get; set; } = new int[7];
    public int[] EkDersCezaevi115 { get; set; } = new int[7];
    public int[] EkDersTakviye116 { get; set; } = new int[7];
    public int[] EkDersTakviyeGece117 { get; set; } = new int[7];
    public int[] EkDersBelleticiFazla118 { get; set; } = new int[7];
    public int[] EkDersNobet119 { get; set; } = new int[7];
    public int[] EkDersEk { get; set; } = new int[7];
    public int[] EkDersRehberlik { get; set; } = new int[7];
    public int[] EkDersSinav { get; set; } = new int[7]; // sinav_ column
}
