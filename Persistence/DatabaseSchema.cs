using System.Collections.Generic;
using DersDagitim.Persistence;

namespace DersDagitim.Persistence;

/// <summary>
/// Database schema definitions matching the existing database exactly.
/// </summary>
public static class DatabaseSchema
{
    public const int CurrentVersion = 26;
    private static readonly Dictionary<string, HashSet<string>> _columnCache = new();
    
    public static readonly string[] CreateTables = new[]
    {
        "CREATE TABLE IF NOT EXISTS okul (id INTEGER PRIMARY KEY, ad TEXT, mudur TEXT, baslangic_tarihi TEXT, gun_sayisi INTEGER DEFAULT 5, ders_sayisi INTEGER DEFAULT 8, ls TEXT);",
        "CREATE TABLE IF NOT EXISTS ders (id INTEGER PRIMARY KEY, kod TEXT, ad TEXT, varsayilan_blok TEXT, sabah_onceligi INTEGER);",
        "CREATE TABLE IF NOT EXISTS sinif (id INTEGER PRIMARY KEY, ad TEXT);",
        "CREATE TABLE IF NOT EXISTS klubler (id INTEGER PRIMARY KEY AUTOINCREMENT, ad TEXT NOT NULL UNIQUE);",
        "CREATE TABLE IF NOT EXISTS nobet_yerleri (id INTEGER PRIMARY KEY AUTOINCREMENT, ad TEXT NOT NULL UNIQUE);",
        "CREATE TABLE IF NOT EXISTS ortak_mekan (id INTEGER PRIMARY KEY AUTOINCREMENT, ad TEXT NOT NULL UNIQUE);",
        "CREATE TABLE IF NOT EXISTS kardes_gruplar (id INTEGER PRIMARY KEY AUTOINCREMENT, ad TEXT);",
        "CREATE TABLE IF NOT EXISTS kardes_bloklar (id INTEGER PRIMARY KEY AUTOINCREMENT, kardes_id INTEGER, sinif_id INTEGER, ders_id INTEGER, sinif_ders_id INTEGER);",

        "CREATE TABLE IF NOT EXISTS ogretmen (id INTEGER PRIMARY KEY, ad_soyad TEXT, tc_kimlik_no TEXT, gorevi TEXT, brans TEXT, nobet_gunu TEXT, nobet_yeri TEXT, egitsel_klub TEXT, rehberlik INTEGER, max_ders INTEGER, ekders_durumu INTEGER);",
        "CREATE TABLE IF NOT EXISTS sinif_ders (id INTEGER PRIMARY KEY AUTOINCREMENT, sinif_id INTEGER, ders_id INTEGER, toplam_saat INTEGER);",
        "CREATE TABLE IF NOT EXISTS atama (id INTEGER PRIMARY KEY AUTOINCREMENT, sinif_ders_id INTEGER, ogretmen_id INTEGER, atanan_saat INTEGER);",
        "CREATE TABLE IF NOT EXISTS dagitim_bloklari (id INTEGER PRIMARY KEY AUTOINCREMENT, sinif_ders_id INTEGER, ders_kodu TEXT, sinif_id INTEGER, blok_suresi INTEGER, ogretmen_1_id INTEGER DEFAULT 0, ogretmen_2_id INTEGER DEFAULT 0, ogretmen_3_id INTEGER DEFAULT 0, ogretmen_4_id INTEGER DEFAULT 0, ogretmen_5_id INTEGER DEFAULT 0, ogretmen_6_id INTEGER DEFAULT 0, ogretmen_7_id INTEGER DEFAULT 0, gun INTEGER DEFAULT 0, saat INTEGER DEFAULT 0, yerlesim_tipi TEXT DEFAULT 'otomatik', kilitli INTEGER DEFAULT 0, manuel INTEGER DEFAULT 0, sabah_onceligi REAL DEFAULT 0, ogretmen_bosluk_skoru INTEGER DEFAULT 0, sinif_bosluk_skoru INTEGER DEFAULT 0, ayni_gun_ihlali INTEGER DEFAULT 0, komsulik_skoru INTEGER DEFAULT 0, toplam_skor INTEGER DEFAULT 0, kardes_id INTEGER DEFAULT 0);",
        "CREATE TABLE IF NOT EXISTS zaman_tablosu (id INTEGER PRIMARY KEY AUTOINCREMENT, tip TEXT, ref_id INTEGER, gun INTEGER, saat INTEGER, durum TEXT);",
        "CREATE TABLE IF NOT EXISTS ekders_aylik (id INTEGER PRIMARY KEY AUTOINCREMENT, ogretmen_id INTEGER NOT NULL, yil INTEGER NOT NULL, ay INTEGER NOT NULL, gun INTEGER NOT NULL, tur TEXT NOT NULL, deger INTEGER DEFAULT 0, UNIQUE(ogretmen_id, yil, ay, gun, tur));",
        "CREATE INDEX IF NOT EXISTS idx_ekders_aylik_teacher_period ON ekders_aylik (ogretmen_id, yil, ay);"
    };
    
    public static void Initialize(DatabaseManager db)
    {
        foreach (var sql in CreateTables)
        {
            db.Execute(sql);
        }
        
        // Remove unused tables
        db.Execute("DROP TABLE IF EXISTS mekanlar");
        db.Execute("DROP TABLE IF EXISTS birlikte_ders_ogretmenleri");
        db.Execute("DROP TABLE IF EXISTS dagitim_bloklari_backup");
        db.Execute("DROP TABLE IF EXISTS degisiklik_log");
        db.Execute("DROP TABLE IF EXISTS kulupler");
        db.Execute("DROP TABLE IF EXISTS schema_version");
        
        // Cleanup Legacy Room/Building Columns (User Request)
        string[] cleanups = {
            "ALTER TABLE ders DROP COLUMN mekan_id",
            "ALTER TABLE sinif_ders DROP COLUMN mekan_id",
            "ALTER TABLE atama DROP COLUMN mekan_id",
            "ALTER TABLE dagitim_bloklari DROP COLUMN mekan_id",
            "ALTER TABLE dagitim_bloklari DROP COLUMN ortak_mekan_id",
            "ALTER TABLE atama DROP COLUMN gun",
            "ALTER TABLE atama DROP COLUMN saat",
            "ALTER TABLE atama DROP COLUMN yerlesim_tipi",
            "ALTER TABLE atama DROP COLUMN kilitli"
        };
        foreach(var cmd in cleanups) {
            try { db.Execute(cmd); } catch { }
        }

        // Ensure critical columns exist for new installations
        CacheTableColumns(db, "ogretmen");
        AddColumnIfNotExists(db, "ogretmen", "verilen_ders_saati", "INTEGER DEFAULT 0");
        AddColumnIfNotExists(db, "ogretmen", "max_gunluk_ders", "INTEGER DEFAULT 8");
        AddColumnIfNotExists(db, "ogretmen", "brans", "TEXT DEFAULT ''");
        
        // Add Unified Lesson column to Distribution Blocks
        CacheTableColumns(db, "dagitim_bloklari");
        AddColumnIfNotExists(db, "dagitim_bloklari", "kardes_id", "INTEGER DEFAULT 0");
        
        // Add Teacher Columns (up to 7) - For existing DB that might stop at 5
        for (int i=1; i<=7; i++)
        {
            AddColumnIfNotExists(db, "dagitim_bloklari", $"ogretmen_{i}_id", "INTEGER DEFAULT 0");
        }
        
        // Add Multiple Room Columns for Multi-Teacher support
        for (int i=1; i<=7; i++)
        {
            AddColumnIfNotExists(db, "dagitim_bloklari", $"ortak_mekan_{i}_id", "INTEGER DEFAULT 0");
        }
        
        try 
        {
             // Cleanup Legacy Password Columns if they exist (User Request)
             db.Execute("ALTER TABLE okul DROP COLUMN giris_sifresi");
             db.Execute("ALTER TABLE okul DROP COLUMN sifre_etkin");
             
             // Cleanup other unused columns 
             db.Execute("ALTER TABLE okul DROP COLUMN lisans_data");
             db.Execute("ALTER TABLE ogretmen DROP COLUMN max_hours_day");
             db.Execute("ALTER TABLE ogretmen DROP COLUMN ogretim_durumu");
             db.Execute("ALTER TABLE ogretmen DROP COLUMN haftalik_ders_saati");
             db.Execute("ALTER TABLE atama DROP COLUMN skor");
        } 
        catch { /* Column might not exist or old sqlite version, ignore */ }
    }

    private static void AddEkDersColumns(DatabaseManager db)
    {
        string[] prefixes = { 
            "101gunduz", "102gece", "103fazlagunduz", "104fazlagece", 
            "106belleticilik", "107sinav", "108egzersiz", "109hizmetici",
            "110edygg", "111edygggece", "112edyggfazlagunduz", "113edyggfazlagece",
            "114atis", "115cezaevi", "116takviye", "117takviyegece", "118belleticifazla", "119nobet" 
        };
        string[] days = { "pazartesi", "sali", "carsamba", "persembe", "cuma", "cumartesi", "pazar" };

        foreach (var prefix in prefixes)
        {
            foreach (var day in days)
            {
                AddColumnIfNotExists(db, "ogretmen", $"{prefix}_{day}", "INTEGER DEFAULT 0");
            }
        }
    }
    
    private static void CacheTableColumns(DatabaseManager db, string table)
    {
        if (_columnCache.ContainsKey(table)) return;
        
        var result = db.Query($"PRAGMA table_info({table})");
        var columns = new HashSet<string>();
        foreach (var row in result)
        {
            columns.Add(DatabaseManager.GetString(row, "name"));
        }
        _columnCache[table] = columns;
    }
    
    private static void AddColumnIfNotExists(DatabaseManager db, string table, string column, string type)
    {
        if (!_columnCache.TryGetValue(table, out var columns))
        {
            CacheTableColumns(db, table);
            columns = _columnCache[table];
        }
        
        if (!columns.Contains(column))
        {
            try { db.Execute($"ALTER TABLE {table} ADD COLUMN \"{column}\" {type}"); } catch { }
            columns.Add(column);
        }
    }
    
    private static void AddDColumns(DatabaseManager db)
    {
        var tables = new[] { "ogretmen", "sinif", "okul", "ortak_mekan" };
        foreach (var table in tables)
        {
            for (int day = 1; day <= 7; day++)
            {
                for (int hour = 1; hour <= 12; hour++)
                {
                    AddColumnIfNotExists(db, table, $"d_{day}_{hour}", "TEXT DEFAULT ''");
                }
            }
        }
    }
    
    private static void AddSchoolColumns(DatabaseManager db)
    {
        for (int i = 1; i <= 12; i++)
            AddColumnIfNotExists(db, "okul", $"saat{i}", "TEXT DEFAULT ''");


        AddColumnIfNotExists(db, "okul", "versiyon", "TEXT DEFAULT '1.0.0.0'");
        AddColumnIfNotExists(db, "okul", "son_guncelleme", "TEXT DEFAULT ''");
        AddColumnIfNotExists(db, "okul", "v3_gap_penalty", "INTEGER DEFAULT 100");
    }
}
