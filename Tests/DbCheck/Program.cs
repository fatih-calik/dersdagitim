using Microsoft.Data.Sqlite;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== Mükerrer Kayıt Analizi ===\n");

var dbPath = @"c:\Users\fth\Desktop\ders.dagıtım\windows\bin\Debug\net8.0-windows\win-x64\main.db";
Console.WriteLine($"Veritabanı: {dbPath}\n");

try
{
    using var conn = new SqliteConnection($"Data Source={dbPath}");
    conn.Open();
    
    // Query to find duplicate sinif_ders entries and their blocks
    string sql = @"
        SELECT 
            s.ad as Sinif, 
            d.ad as Ders, 
            sd.id as SinifDersId,
            (SELECT COUNT(*) FROM dagitim_bloklari db WHERE db.sinif_ders_id = sd.id) as BlokSayisi,
            sd.toplam_saat as SaatYuk
        FROM sinif_ders sd
        JOIN sinif s ON sd.sinif_id = s.id
        JOIN ders d ON sd.ders_id = d.id
        WHERE EXISTS (
            SELECT 1 FROM sinif_ders sd2 
            WHERE sd2.sinif_id = sd.sinif_id 
              AND sd2.ders_id = sd.ders_id 
              AND sd2.id != sd.id
        )
        ORDER BY s.ad, d.ad;
    ";

    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    
    using var reader = cmd.ExecuteReader();
    
    Console.WriteLine($"{"Sınıf",-15} | {"Ders",-25} | {"Atama ID",-10} | {"Blok Sayısı",-12} | {"Saat Yükü",-10}");
    Console.WriteLine(new string('-', 85));

    int count = 0;
    while (reader.Read())
    {
        count++;
        Console.WriteLine($"{reader.GetString(0),-15} | {reader.GetString(1),-25} | {reader.GetInt32(2),-10} | {reader.GetInt32(3),-12} | {reader.GetInt32(4),-10}");
    }

    if (count == 0)
    {
        Console.WriteLine("\nHiç mükerrer kayıt bulunamadı.");
    }
    else
    {
        Console.WriteLine($"\nToplam {count} adet şüpheli (mükerrer) atama kaydı bulundu.");
    }
    
    // Also show total block count in the table
    cmd.CommandText = "SELECT COUNT(*) FROM dagitim_bloklari";
    var totalBlocks = cmd.ExecuteScalar();
    Console.WriteLine($"\nTablodaki Toplam Blok Sayısı: {totalBlocks}");

    conn.Close();
}
catch (Exception ex)
{
    Console.WriteLine($"Hata: {ex.Message}");
}

Console.WriteLine("\n=== Analiz Tamamlandı ===");
