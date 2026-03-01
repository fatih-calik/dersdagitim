namespace DersDagitim.Models;

/// <summary>
/// Represents a specific coordinate in the timetable
/// </summary>
public readonly struct TimeSlot : IEquatable<TimeSlot>
{
    /// <summary>
    /// Day of the week (1-7: Monday to Sunday)
    /// </summary>
    public int Day { get; }
    
    /// <summary>
    /// Hour/Period (1-12)
    /// </summary>
    public int Hour { get; }
    
    public TimeSlot(int day, int hour)
    {
        Day = day;
        Hour = hour;
    }
    
    public bool Equals(TimeSlot other) => Day == other.Day && Hour == other.Hour;
    
    public override bool Equals(object? obj) => obj is TimeSlot other && Equals(other);
    
    public override int GetHashCode() => HashCode.Combine(Day, Hour);
    
    public static bool operator ==(TimeSlot left, TimeSlot right) => left.Equals(right);
    
    public static bool operator !=(TimeSlot left, TimeSlot right) => !left.Equals(right);
    
    public override string ToString() => $"d_{Day}_{Hour}";
    
    /// <summary>
    /// Creates a TimeSlot from column key like "d_1_1"
    /// </summary>
    public static TimeSlot FromColumnKey(string key)
    {
        var parts = key.Replace("d_", "").Split('_');
        if (parts.Length == 2 && int.TryParse(parts[0], out int day) && int.TryParse(parts[1], out int hour))
        {
            return new TimeSlot(day, hour);
        }
        return new TimeSlot(0, 0);
    }
}
