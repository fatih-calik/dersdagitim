using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DersDagitim
{
    /// <summary>
    /// Theme manager for handling light/dark mode switching and schedule colors
    /// </summary>
    public static class ThemeManager
    {
        public static bool IsDarkMode { get; private set; } = false;

        private static readonly Dictionary<string, string> _lessonNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Mat", "Matematik" },
            { "Trk", "Türkçe" },
            { "Fen", "Fen Bilimleri" },
            { "Sos", "Sosyal Bilgiler" },
            { "İng", "İngilizce" },
            { "Din", "Din Kültürü" },
            { "Müz", "Müzik" },
            { "Grsl", "Görsel Sanatlar" },
            { "Bed", "Beden Eğitimi" },
            { "Bil", "Bilişim Tek." },
            { "Reh", "Rehberlik" }
        };

        public static string GetLessonName(string abbreviation)
        {
            return _lessonNames.TryGetValue(abbreviation, out var name) ? name : abbreviation;
        }
        
        // Schedule color palette (20 distinct colors)
        private static readonly List<Color> LightScheduleColors = new()
        {
            Color.FromRgb(219, 234, 254), // Blue
            Color.FromRgb(209, 250, 229), // Green
            Color.FromRgb(254, 243, 199), // Yellow
            Color.FromRgb(252, 231, 243), // Pink
            Color.FromRgb(224, 231, 255), // Indigo
            Color.FromRgb(204, 251, 241), // Teal
            Color.FromRgb(254, 215, 170), // Orange
            Color.FromRgb(221, 214, 254), // Violet
            Color.FromRgb(167, 243, 208), // Emerald
            Color.FromRgb(254, 202, 202), // Red light
            Color.FromRgb(186, 230, 253), // Sky
            Color.FromRgb(253, 230, 138), // Amber
            Color.FromRgb(199, 210, 254), // Indigo light
            Color.FromRgb(153, 246, 228), // Cyan
            Color.FromRgb(251, 207, 232), // Pink light
            Color.FromRgb(252, 165, 165), // Red
            Color.FromRgb(134, 239, 172), // Green light
            Color.FromRgb(253, 224, 71),  // Lime
            Color.FromRgb(196, 181, 253), // Purple
            Color.FromRgb(103, 232, 249), // Cyan light
        };
        
        // Pastel Colors for Smart Grid Coloring (Backgrounds)
        public static readonly List<SolidColorBrush> LightPastelColors = new()
        {
            new SolidColorBrush(Color.FromRgb(254, 242, 242)), // Red-50
            new SolidColorBrush(Color.FromRgb(255, 251, 235)), // Amber-50
            new SolidColorBrush(Color.FromRgb(236, 253, 245)), // Emerald-50
            new SolidColorBrush(Color.FromRgb(239, 246, 255)), // Blue-50
            new SolidColorBrush(Color.FromRgb(245, 243, 255)), // Violet-50
            new SolidColorBrush(Color.FromRgb(253, 242, 248)), // Pink-50
            new SolidColorBrush(Color.FromRgb(250, 250, 250)), // Neutral-50
            new SolidColorBrush(Color.FromRgb(255, 247, 237)), // Orange-50
            new SolidColorBrush(Color.FromRgb(240, 253, 244)), // Green-50
            new SolidColorBrush(Color.FromRgb(236, 254, 255)), // Cyan-50
        };

        // Deep Colors for Borders (Matching indices)
        public static readonly List<SolidColorBrush> DeepPastelColors = new()
        {
            new SolidColorBrush(Color.FromRgb(254, 202, 202)), // Red-200
            new SolidColorBrush(Color.FromRgb(253, 230, 138)), // Amber-200
            new SolidColorBrush(Color.FromRgb(167, 243, 208)), // Emerald-200
            new SolidColorBrush(Color.FromRgb(191, 219, 254)), // Blue-200
            new SolidColorBrush(Color.FromRgb(221, 214, 254)), // Violet-200
            new SolidColorBrush(Color.FromRgb(251, 207, 232)), // Pink-200
            new SolidColorBrush(Color.FromRgb(229, 231, 235)), // Neutral-200
            new SolidColorBrush(Color.FromRgb(254, 215, 170)), // Orange-200
            new SolidColorBrush(Color.FromRgb(187, 247, 208)), // Green-200
            new SolidColorBrush(Color.FromRgb(165, 243, 252)), // Cyan-200
        };

        private static readonly List<Color> DarkScheduleColors = new()
        {
            Color.FromRgb(30, 58, 138),   // Blue dark
            Color.FromRgb(6, 78, 59),     // Green dark
            Color.FromRgb(120, 53, 15),   // Yellow dark  
            Color.FromRgb(131, 24, 67),   // Pink dark
            Color.FromRgb(55, 48, 163),   // Indigo dark
            Color.FromRgb(17, 94, 89),    // Teal dark
            Color.FromRgb(124, 45, 18),   // Orange dark
            Color.FromRgb(76, 29, 149),   // Violet dark
            Color.FromRgb(6, 95, 70),     // Emerald dark
            Color.FromRgb(127, 29, 29),   // Red dark
            Color.FromRgb(7, 89, 133),    // Sky dark
            Color.FromRgb(120, 53, 15),   // Amber dark
            Color.FromRgb(49, 46, 129),   // Indigo light dark
            Color.FromRgb(21, 94, 117),   // Cyan dark
            Color.FromRgb(112, 26, 117),  // Pink light dark
            Color.FromRgb(153, 27, 27),   // Red dark
            Color.FromRgb(20, 83, 45),    // Green light dark
            Color.FromRgb(101, 63, 23),   // Lime dark
            Color.FromRgb(88, 28, 135),   // Purple dark
            Color.FromRgb(22, 78, 99),    // Cyan light dark
        };
        
        // Light theme colors
        private static readonly Dictionary<string, Color> LightTheme = new()
        {
            { "PrimaryBrush", Color.FromRgb(37, 99, 235) },
            { "PrimaryHoverBrush", Color.FromRgb(29, 78, 216) },
            { "PrimaryLightBrush", Color.FromRgb(59, 130, 246) },
            { "BackgroundBrush", Color.FromRgb(248, 250, 252) },
            { "SurfaceBrush", Color.FromRgb(255, 255, 255) },
            { "SurfaceAltBrush", Color.FromRgb(241, 245, 249) },
            { "SidebarBrush", Color.FromRgb(30, 64, 175) },
            { "SidebarTextBrush", Color.FromRgb(255, 255, 255) },
            { "SidebarTextDimBrush", Color.FromRgb(147, 197, 253) },
            { "SidebarActiveBrush", Color.FromRgb(59, 130, 246) },
            { "TextPrimaryBrush", Color.FromRgb(30, 41, 59) },
            { "TextSecondaryBrush", Color.FromRgb(100, 116, 139) },
            { "TextMutedBrush", Color.FromRgb(148, 163, 184) },
            { "BorderBrush", Color.FromRgb(226, 232, 240) },
            { "BorderLightBrush", Color.FromRgb(241, 245, 249) },
            { "ClosedSlotBrush", Color.FromRgb(226, 232, 240) },
            { "ClosedSlotTextBrush", Color.FromRgb(148, 163, 184) },
        };
        
        // Dark theme colors
        private static readonly Dictionary<string, Color> DarkTheme = new()
        {
            { "PrimaryBrush", Color.FromRgb(59, 130, 246) },
            { "PrimaryHoverBrush", Color.FromRgb(96, 165, 250) },
            { "PrimaryLightBrush", Color.FromRgb(37, 99, 235) },
            { "BackgroundBrush", Color.FromRgb(15, 23, 42) },
            { "SurfaceBrush", Color.FromRgb(30, 41, 59) },
            { "SurfaceAltBrush", Color.FromRgb(51, 65, 85) },
            { "SidebarBrush", Color.FromRgb(15, 23, 42) },
            { "SidebarTextBrush", Color.FromRgb(241, 245, 249) },
            { "SidebarTextDimBrush", Color.FromRgb(148, 163, 184) },
            { "SidebarActiveBrush", Color.FromRgb(59, 130, 246) },
            { "TextPrimaryBrush", Color.FromRgb(241, 245, 249) },
            { "TextSecondaryBrush", Color.FromRgb(148, 163, 184) },
            { "TextMutedBrush", Color.FromRgb(100, 116, 139) },
            { "BorderBrush", Color.FromRgb(51, 65, 85) },
            { "BorderLightBrush", Color.FromRgb(30, 41, 59) },
            { "ClosedSlotBrush", Color.FromRgb(51, 65, 85) },
            { "ClosedSlotTextBrush", Color.FromRgb(100, 116, 139) },
        };
        
        /// <summary>
        /// Toggle between light and dark theme
        /// </summary>
        public static void ToggleTheme()
        {
            IsDarkMode = !IsDarkMode;
            ApplyTheme();
        }
        
        /// <summary>
        /// Apply current theme to application resources
        /// </summary>
        public static void ApplyTheme()
        {
            var theme = IsDarkMode ? DarkTheme : LightTheme;
            var resources = Application.Current.Resources;
            
            foreach (var kvp in theme)
            {
                if (resources.Contains(kvp.Key))
                {
                    resources[kvp.Key] = new SolidColorBrush(kvp.Value);
                }
            }
            
            // Update schedule colors
            var scheduleColors = IsDarkMode ? DarkScheduleColors : LightScheduleColors;
            for (int i = 0; i < scheduleColors.Count; i++)
            {
                var key = $"ScheduleColor{i + 1}";
                if (resources.Contains(key))
                {
                    resources[key] = new SolidColorBrush(scheduleColors[i]);
                }
            }
        }
        
        /// <summary>
        /// Get a consistent color for an entity (class or teacher) based on its ID
        /// </summary>
        public static SolidColorBrush GetScheduleColor(int entityId)
        {
            var colors = IsDarkMode ? DarkScheduleColors : LightScheduleColors;
            int index = (entityId & 0x7FFFFFFF) % colors.Count;
            return new SolidColorBrush(colors[index]);
        }
        
        /// <summary>
        /// Get a consistent color for a string-based key (lesson name, teacher name, etc.)
        /// </summary>
        public static SolidColorBrush GetScheduleColorByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return new SolidColorBrush(Colors.White);
            
            int hash = name.GetHashCode() & 0x7FFFFFFF;
            
            if (IsDarkMode)
            {
                var colors = DarkScheduleColors;
                int index = hash % colors.Count;
                return new SolidColorBrush(colors[index]);
            }
            else
            {
                var colors = LightScheduleColors;
                int index = hash % colors.Count;
                return new SolidColorBrush(colors[index]);
            }
        }
        
        /// <summary>
        /// Get text color that contrasts well with a given background color
        /// </summary>
        public static SolidColorBrush GetContrastTextColor(Color backgroundColor)
        {
            // Calculate luminance
            double luminance = (0.299 * backgroundColor.R + 0.587 * backgroundColor.G + 0.114 * backgroundColor.B) / 255;
            return luminance > 0.5 
                ? new SolidColorBrush(Color.FromRgb(30, 41, 59))   // Dark text
                : new SolidColorBrush(Color.FromRgb(255, 255, 255)); // Light text
        }
        
        /// <summary>
        /// Get closed slot brush
        /// </summary>
        public static SolidColorBrush GetClosedSlotBrush()
        {
            return IsDarkMode 
                ? new SolidColorBrush(Color.FromRgb(51, 65, 85)) 
                : new SolidColorBrush(Color.FromRgb(226, 232, 240));
        }
        
        /// <summary>
        /// Get closed slot text brush
        /// </summary>
        public static SolidColorBrush GetClosedSlotTextBrush()
        {
            return IsDarkMode 
                ? new SolidColorBrush(Color.FromRgb(100, 116, 139)) 
                : new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }
    }
}
