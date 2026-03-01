using System.Windows.Media;

namespace DersDagitim.Core;

/// <summary>
/// Application color palette - matching Swift design system
/// </summary>
public static class AppColors
{
    // Brand Colors
    public static Color AppPrimary => FromHex("#2563EB");    // Vibrant Blue
    public static Color AppAccent => FromHex("#7C3AED");     // Deep Purple
    
    // Semantic Colors
    public static Color Success => FromHex("#10B981");       // Emerald Green
    public static Color Warning => FromHex("#F59E0B");       // Amber
    public static Color Error => FromHex("#EF4444");         // Red
    public static Color Info => FromHex("#3B82F6");          // Blue
    public static Color Neutral => FromHex("#6B7280");       // Gray
    
    // Backgrounds
    public static Color AppBackground => FromHex("#F9FAFB");
    public static Color CardBackground => FromHex("#FFFFFF");
    public static Color SurfaceBackground => FromHex("#FFFFFF");
    
    // Text Colors
    public static Color TextPrimary => FromHex("#111827");   // Gray 900
    public static Color TextSecondary => FromHex("#6B7280"); // Gray 500
    public static Color TextTertiary => FromHex("#9CA3AF");  // Gray 400
    
    // Component Colors
    public static Color Border => FromHex("#E5E7EB");
    
    // Subject Colors
    public static Color MathBlue => FromHex("#3B82F6");
    public static Color ScienceGreen => FromHex("#10B981");
    public static Color LanguageOrange => FromHex("#F59E0B");
    public static Color ArtsPurple => FromHex("#8B5CF6");
    public static Color SocialRed => FromHex("#EF4444");
    public static Color GuidanceTeal => FromHex("#14B8A6");
    
    // Extended Palette for lessons
    public static readonly Color[] ExtendedPalette = new[]
    {
        FromHex("#ef4444"), FromHex("#dc2626"), FromHex("#f43f5e"), FromHex("#ec4899"),
        FromHex("#f97316"), FromHex("#ea580c"), FromHex("#f59e0b"), FromHex("#d97706"),
        FromHex("#eab308"), FromHex("#ca8a04"), FromHex("#84cc16"), FromHex("#65a30d"),
        FromHex("#22c55e"), FromHex("#16a34a"), FromHex("#10b981"), FromHex("#059669"),
        FromHex("#14b8a6"), FromHex("#0d9488"), FromHex("#06b6d4"), FromHex("#0891b2"),
        FromHex("#0ea5e9"), FromHex("#0284c7"), FromHex("#3b82f6"), FromHex("#2563eb"),
        FromHex("#6366f1"), FromHex("#4f46e5"), FromHex("#8b5cf6"), FromHex("#7c3aed"),
        FromHex("#a855f7"), FromHex("#9333ea"), FromHex("#d946ef"), FromHex("#c026d3"),
        FromHex("#64748b"), FromHex("#475569"), FromHex("#78716c"), FromHex("#57534e"),
        FromHex("#1d4ed8"), FromHex("#1e40af")
    };
    
    /// <summary>
    /// Creates a SolidColorBrush from a Color
    /// </summary>
    public static SolidColorBrush ToBrush(this Color color) => new(color);
    
    /// <summary>
    /// Gets a brush for primary color
    /// </summary>
    public static SolidColorBrush PrimaryBrush => AppPrimary.ToBrush();
    public static SolidColorBrush AccentBrush => AppAccent.ToBrush();
    public static SolidColorBrush SuccessBrush => Success.ToBrush();
    public static SolidColorBrush WarningBrush => Warning.ToBrush();
    public static SolidColorBrush ErrorBrush => Error.ToBrush();
    public static SolidColorBrush InfoBrush => Info.ToBrush();
    public static SolidColorBrush BackgroundBrush => AppBackground.ToBrush();
    public static SolidColorBrush TextPrimaryBrush => TextPrimary.ToBrush();
    public static SolidColorBrush TextSecondaryBrush => TextSecondary.ToBrush();
    public static SolidColorBrush BorderBrush => Border.ToBrush();
    
    /// <summary>
    /// Parses hex color string
    /// </summary>
    private static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        
        if (hex.Length == 6)
        {
            return Color.FromRgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }
        else if (hex.Length == 8)
        {
            return Color.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16));
        }
        
        return Colors.Black;
    }
    
    /// <summary>
    /// Gets a color for a lesson based on index
    /// </summary>
    public static Color GetLessonColor(int index)
    {
        return ExtendedPalette[index % ExtendedPalette.Length];
    }
}

/// <summary>
/// Spacing constants
/// </summary>
public static class Spacing
{
    public const double XXS = 2;
    public const double XS = 4;
    public const double SM = 8;
    public const double MD = 12;
    public const double LG = 16;
    public const double XL = 24;
    public const double XXL = 32;
}

/// <summary>
/// Border radius constants
/// </summary>
public static class Radius
{
    public const double Small = 4;
    public const double Medium = 8;
    public const double Large = 12;
    public const double XL = 16;
    public const double Round = 9999;
}
