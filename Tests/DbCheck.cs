// Simple console test to check database and run distribution
using Microsoft.Data.Sqlite;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== Ders Dağıtım - Veritabanı Test ===\n");

var dbPath = args.Length > 0 ? args[0] : @"c:\Users\fth\Desktop\ders.dagıtım\ders_dagitim.db";
Console.WriteLine($"Veritabanı: {dbPath}\n");

try
{
    using var conn = new SqliteConnection($"Data Source={dbPath}");
    conn.Open();
    
    // Count teachers
    using var cmd1 = conn.CreateCommand();
    cmd1.CommandText = "SELECT COUNT(*) FROM ogretmen";
    var teachers = Convert.ToInt32(cmd1.ExecuteScalar());
    Console.WriteLine($"📊 Öğretmen sayısı: {teachers}");
    
    // Count classes
    using var cmd2 = conn.CreateCommand();
    cmd2.CommandText = "SELECT COUNT(*) FROM sinif";
    var classes = Convert.ToInt32(cmd2.ExecuteScalar());
    Console.WriteLine($"📊 Sınıf sayısı: {classes}");
    
    // Count class-lessons
    using var cmd3 = conn.CreateCommand();
    cmd3.CommandText = "SELECT COUNT(*) FROM sinif_ders";
    var classLessons = Convert.ToInt32(cmd3.ExecuteScalar());
    Console.WriteLine($"📊 Sınıf-ders ataması: {classLessons}");
    
    // Count assignments
    using var cmd4 = conn.CreateCommand();
    cmd4.CommandText = "SELECT COUNT(*) FROM atama";
    var assignments = Convert.ToInt32(cmd4.ExecuteScalar());
    Console.WriteLine($"📊 Öğretmen ataması: {assignments}");
    
    // Count distribution blocks
    using var cmd5 = conn.CreateCommand();
    cmd5.CommandText = "SELECT COUNT(*) FROM dagitim_bloklari";
    var totalBlocks = Convert.ToInt32(cmd5.ExecuteScalar());
    Console.WriteLine($"📊 Toplam dağıtım bloğu: {totalBlocks}");
    
    // Count placed blocks
    using var cmd6 = conn.CreateCommand();
    cmd6.CommandText = "SELECT COUNT(*) FROM dagitim_bloklari WHERE gun > 0 AND saat > 0";
    var placedBlocks = Convert.ToInt32(cmd6.ExecuteScalar());
    Console.WriteLine($"📊 Yerleşmiş blok: {placedBlocks}");
    
    var placementPercent = totalBlocks > 0 ? (double)placedBlocks / totalBlocks * 100 : 0;
    Console.WriteLine($"\n📈 Yerleşim oranı: %{placementPercent:F1}");
    
    // Show some placements if any
    if (placedBlocks > 0)
    {
        Console.WriteLine("\n📋 Son 5 yerleşim:");
        using var cmd7 = conn.CreateCommand();
        cmd7.CommandText = "SELECT ders_kodu, sinif_id, gun, saat, blok_suresi FROM dagitim_bloklari WHERE gun > 0 ORDER BY id DESC LIMIT 5";
        using var reader = cmd7.ExecuteReader();
        while (reader.Read())
        {
            Console.WriteLine($"   {reader.GetString(0)} -> Sınıf {reader.GetInt32(1)}, Gün {reader.GetInt32(2)}, Saat {reader.GetInt32(3)} ({reader.GetInt32(4)} saat)");
        }
    }
    
    conn.Close();
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Hata: {ex.Message}");
}

Console.WriteLine("\n=== Test Tamamlandı ===");
