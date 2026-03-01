using System.IO;
using Microsoft.Data.Sqlite;

namespace DersDagitim.Services;

/// <summary>
/// Manages sabit.sqlite - stores application settings like active database path
/// All database files are stored in the "data" subfolder under the application directory.
/// </summary>
public class ConfigManager
{
    private static readonly Lazy<ConfigManager> _instance = new(() => new ConfigManager());
    public static ConfigManager Shared => _instance.Value;

    private readonly string _configDbPath;
    private readonly string _appDirectory;
    private readonly string _dataDirectory;

    /// <summary>
    /// The directory where all database files are stored (app/data/)
    /// </summary>
    public string DataDirectory => _dataDirectory;

    private ConfigManager()
    {
        _appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        _dataDirectory = Path.Combine(_appDirectory, "data");

        // Ensure data directory exists
        Directory.CreateDirectory(_dataDirectory);

        // Migrate old databases from root to data/
        MigrateDatabasesToDataFolder();

        _configDbPath = Path.Combine(_dataDirectory, "sabit.sqlite");
        InitializeDatabase();
    }

    /// <summary>
    /// Migrates .sqlite and .db files from app root to data/ subfolder (one-time)
    /// </summary>
    private void MigrateDatabasesToDataFolder()
    {
        try
        {
            var patterns = new[] { "*.sqlite", "*.db" };
            foreach (var pattern in patterns)
            {
                foreach (var file in Directory.GetFiles(_appDirectory, pattern))
                {
                    var fileName = Path.GetFileName(file);
                    var destPath = Path.Combine(_dataDirectory, fileName);
                    if (!File.Exists(destPath))
                    {
                        File.Move(file, destPath);
                    }
                }
            }

            // Update stored active database path if it points to old root location
            UpdateStoredActiveDatabasePath();
        }
        catch { }
    }

    /// <summary>
    /// Fixes the aktif_veritabani path in sabit.sqlite after migration
    /// </summary>
    private void UpdateStoredActiveDatabasePath()
    {
        var sabitPath = Path.Combine(_dataDirectory, "sabit.sqlite");
        if (!File.Exists(sabitPath)) return;

        try
        {
            using var connection = new SqliteConnection($"Data Source={sabitPath}");
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT deger FROM ayarlar WHERE anahtar = 'aktif_veritabani'";
            var result = cmd.ExecuteScalar();

            if (result != null && result != DBNull.Value)
            {
                var oldPath = result.ToString()!;
                if (!string.IsNullOrEmpty(oldPath) && !File.Exists(oldPath))
                {
                    var fileName = Path.GetFileName(oldPath);
                    var newPath = Path.Combine(_dataDirectory, fileName);
                    if (File.Exists(newPath))
                    {
                        cmd.CommandText = "UPDATE ayarlar SET deger = @v WHERE anahtar = 'aktif_veritabani'";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@v", newPath);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Creates sabit.sqlite if not exists and initializes default settings keys
    /// </summary>
    private void InitializeDatabase()
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={_configDbPath}");
            connection.Open();

            var cmd = connection.CreateCommand();

            // 1. Create Table
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ayarlar (
                    anahtar TEXT PRIMARY KEY,
                    deger TEXT
                )";
            cmd.ExecuteNonQuery();

            // 2. Insert Default Keys (This ensures 'fields' exist in the DB view)
            // We use transaction for performance
            using var transaction = connection.BeginTransaction();
            cmd.Transaction = transaction;

            var defaults = new Dictionary<string, string>
            {
                { "AppPasswordActive", "0" },      // Şifre Aktif mi? (0=Hayır, 1=Evet)
                { "AppPassword", "" },             // Şifre İçeriği
                { "TeacherHideWeekend", "0" },     // Öğretmen Tablosunda Haftasonu Gizle
                { "DistHideWeekend", "0" },        // Dağıtım Tablosunda Haftasonu Gizle
                { "yedek_lisans", "" },            // Yedek Lisans Anahtarı
                { "aktif_veritabani", "" }         // Seçili Veritabanı Yolu
            };

            foreach (var kvp in defaults)
            {
                cmd.CommandText = "INSERT OR IGNORE INTO ayarlar (anahtar, deger) VALUES (@k, @v)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@k", kvp.Key);
                cmd.Parameters.AddWithValue("@v", kvp.Value);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            // Console.WriteLine($"ConfigManager init error: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the active database path from sabit.sqlite
    /// </summary>
    public string GetActiveDatabase()
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={_configDbPath}");
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT deger FROM ayarlar WHERE anahtar = 'aktif_veritabani'";
            var result = cmd.ExecuteScalar();

            if (result != null && result != DBNull.Value)
            {
                var path = result.ToString()!;
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch (Exception ex)
        {
            // Console.WriteLine($"GetActiveDatabase error: {ex.Message}");
        }

        // Fallback: Look for ders_dagitim.sqlite in data directory
        var defaultPath = Path.Combine(_dataDirectory, "ders_dagitim.sqlite");
        if (File.Exists(defaultPath))
        {
            SetActiveDatabase(defaultPath);
            return defaultPath;
        }

        // Try .db extension
        var legacyPath = Path.Combine(_dataDirectory, "ders_dagitim.db");
        if (File.Exists(legacyPath))
        {
            SetActiveDatabase(legacyPath);
            return legacyPath;
        }

        // Return default (will be created)
        return defaultPath;
    }

    /// <summary>
    /// Sets the active database path in sabit.sqlite
    /// </summary>
    public void SetActiveDatabase(string path)
    {
        SetSetting("aktif_veritabani", path);
    }

    /// <summary>
    /// Backs up the license code to sabit.sqlite
    /// </summary>
    public void SetBackupLicense(string code)
    {
        SetSetting("yedek_lisans", code);
    }

    /// <summary>
    /// Retrieves the backed up license code from sabit.sqlite
    /// </summary>
    public string? GetBackupLicense()
    {
        return GetSetting("yedek_lisans");
    }

    public void SetSetting(string key, string value)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={_configDbPath}");
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO ayarlar (anahtar, deger) VALUES (@key, @value)";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) { /* Console.WriteLine($"SetSetting error: {ex.Message}"); */ }
    }

    public string? GetSetting(string key)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={_configDbPath}");
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT deger FROM ayarlar WHERE anahtar = @key";
            cmd.Parameters.AddWithValue("@key", key);
            return cmd.ExecuteScalar()?.ToString();
        }
        catch { return null; }
    }

    public void SetBool(string key, bool value) => SetSetting(key, value ? "1" : "0");

    public bool GetBool(string key, bool defaultValue = false)
    {
        var val = GetSetting(key);
        if (val == "1") return true;
        if (val == "0") return false;
        return defaultValue;
    }

    /// <summary>
    /// Lists all database files in data directory (excluding sabit.sqlite)
    /// </summary>
    public List<string> ListDatabases()
    {
        try
        {
            var files = Directory.GetFiles(_dataDirectory, "*.sqlite")
                .Concat(Directory.GetFiles(_dataDirectory, "*.db"))
                .Where(f => !Path.GetFileName(f).Equals("sabit.sqlite", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileName(f))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            return files;
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Gets full path for a database name
    /// </summary>
    public string GetDatabasePath(string name)
    {
        // If already full path
        if (Path.IsPathRooted(name))
            return name;

        // Add extension if missing
        if (!name.EndsWith(".sqlite") && !name.EndsWith(".db"))
        {
            var sqlitePath = Path.Combine(_dataDirectory, name + ".sqlite");
            if (File.Exists(sqlitePath))
                return sqlitePath;

            var dbPath = Path.Combine(_dataDirectory, name + ".db");
            if (File.Exists(dbPath))
                return dbPath;

            return sqlitePath; // Default to .sqlite
        }

        return Path.Combine(_dataDirectory, name);
    }

    /// <summary>
    /// Creates a new database file and copies license from backup or active database
    /// </summary>
    public bool CreateDatabase(string name)
    {
        try
        {
            var path = Path.Combine(_dataDirectory, name + ".sqlite");
            if (File.Exists(path))
                return false;

            // 1. Get license from backup or current active database
            string? currentLicense = GetBackupLicense();

            if (string.IsNullOrEmpty(currentLicense))
            {
                var activePath = GetActiveDatabase();
                if (File.Exists(activePath))
                {
                    try
                    {
                        using var sourceConn = new SqliteConnection($"Data Source={activePath}");
                        sourceConn.Open();
                        using var cmd = sourceConn.CreateCommand();
                        cmd.CommandText = "SELECT ls FROM okul LIMIT 1";
                        var result = cmd.ExecuteScalar();
                        currentLicense = result?.ToString();

                        // If we found it in active but not in backup, back it up now
                        if (!string.IsNullOrEmpty(currentLicense))
                        {
                            SetBackupLicense(currentLicense);
                        }
                    }
                    catch { }
                }
            }

            // 2. Create empty database
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();

                // 3. Initialize minimal okul table for license if we found one
                if (!string.IsNullOrEmpty(currentLicense))
                {
                    using var cmd = connection.CreateCommand();
                    // We create the full table structure to match DatabaseSchema.Initialize expectations
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS okul (id INTEGER PRIMARY KEY, ad TEXT, mudur TEXT, baslangic_tarihi TEXT, gun_sayisi INTEGER DEFAULT 5, ders_sayisi INTEGER DEFAULT 8, ls TEXT);";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "INSERT INTO okul (id, ls) VALUES (1, @ls);";
                    cmd.Parameters.AddWithValue("@ls", currentLicense);
                    cmd.ExecuteNonQuery();
                }

                connection.Close();
            }

            return true;
        }
        catch (Exception ex)
        {
            // Console.WriteLine($"CreateDatabase error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Duplicates a database
    /// </summary>
    public bool DuplicateDatabase(string sourceName, string newName)
    {
        try
        {
            var sourcePath = GetDatabasePath(sourceName);
            var destPath = Path.Combine(_dataDirectory, newName + ".sqlite");

            if (!File.Exists(sourcePath) || File.Exists(destPath))
                return false;

            File.Copy(sourcePath, destPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Renames a database
    /// </summary>
    public bool RenameDatabase(string oldName, string newName)
    {
        try
        {
            var oldPath = GetDatabasePath(oldName);
            var ext = Path.GetExtension(oldPath);
            var newPath = Path.Combine(_dataDirectory, newName + ext);

            if (!File.Exists(oldPath) || File.Exists(newPath))
                return false;

            // Don't rename if it's the active database - use FullPath for robust comparison
            var activePath = Path.GetFullPath(GetActiveDatabase());
            var currentOldPath = Path.GetFullPath(oldPath);

            if (currentOldPath.Equals(activePath, StringComparison.OrdinalIgnoreCase))
                return false;

            File.Move(oldPath, newPath);
            return true;
        }
        catch (Exception ex)
        {
            // Console.WriteLine($"RenameDatabase error: {ex.Message}");
            throw; // Let the UI handle it
        }
    }

    /// <summary>
    /// Deletes a database
    /// </summary>
    public bool DeleteDatabase(string name)
    {
        try
        {
            var path = GetDatabasePath(name);

            // Don't delete if it's the active database - use FullPath for robust comparison
            var activePath = Path.GetFullPath(GetActiveDatabase());
            var currentPath = Path.GetFullPath(path);

            if (currentPath.Equals(activePath, StringComparison.OrdinalIgnoreCase))
                return false;

            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            // Console.WriteLine($"DeleteDatabase error: {ex.Message}");
            throw; // Let the UI handle it
        }
    }
}
