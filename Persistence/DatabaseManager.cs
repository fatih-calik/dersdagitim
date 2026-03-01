using Microsoft.Data.Sqlite;
using System.IO;

namespace DersDagitim.Persistence;

/// <summary>
/// SQLite database manager - singleton pattern
/// </summary>
public sealed class DatabaseManager
{
    private static readonly Lazy<DatabaseManager> _instance = new(() => new DatabaseManager());
    public static DatabaseManager Shared => _instance.Value;
    
    private SqliteConnection? _connection;
    private readonly object _lock = new();
    
    public string? CurrentDatabasePath { get; private set; }
    
    private DatabaseManager() { }
    
    /// <summary>
    /// Opens a database connection
    /// </summary>
    public bool OpenDatabase(string path)
    {
        lock (_lock)
        {
            try
            {
                CloseDatabase();
                
                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadWriteCreate
                }.ToString();
                
                _connection = new SqliteConnection(connectionString);
                _connection.Open();
                CurrentDatabasePath = path;
                
                // Enable WAL mode for faster writes
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA journal_mode=WAL";
                    cmd.ExecuteNonQuery();
                }
                using (var cmd2 = _connection.CreateCommand())
                {
                    cmd2.CommandText = "PRAGMA synchronous=NORMAL";
                    cmd2.ExecuteNonQuery();
                }
                
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
    
    /// <summary>
    /// Closes the current database connection
    /// </summary>
    public void CloseDatabase()
    {
        lock (_lock)
        {
            if (_connection != null)
            {
                _connection.Close();
                _connection.Dispose();
                _connection = null;
                CurrentDatabasePath = null;
            }
        }
    }
    
    /// <summary>
    /// Executes a non-query SQL statement
    /// </summary>
    public void Execute(string sql)
    {
        lock (_lock)
        {
            if (_connection == null) return;

            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
            catch (Exception) { }
        }
    }
    
    /// <summary>
    /// Executes a SQL script with multiple statements
    /// </summary>
    public void ExecuteScript(string sql)
    {
        lock (_lock)
        {
            if (_connection == null) return;
            
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
            catch (Exception) { }
        }
    }
    
    /// <summary>
    /// Executes a query and returns results as list of dictionaries
    /// </summary>
    public List<Dictionary<string, object?>> Query(string sql)
    {
        lock (_lock)
        {
            var results = new List<Dictionary<string, object?>>();
            
            if (_connection == null) return results;

            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = sql;

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var name = reader.GetName(i);
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row[name] = value;
                    }
                    results.Add(row);
                }
            }
            catch (Exception) { }
            
            return results;
        }
    }
    
    /// <summary>
    /// Gets an integer value from a row
    /// </summary>
    public static int GetInt(Dictionary<string, object?> row, string key, int defaultValue = 0)
    {
        if (row.TryGetValue(key, out var value) && value != null)
        {
            return Convert.ToInt32(value);
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Gets a string value from a row
    /// </summary>
    public static string GetString(Dictionary<string, object?> row, string key, string defaultValue = "")
    {
        if (row.TryGetValue(key, out var value) && value != null)
        {
            return value.ToString() ?? defaultValue;
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Gets a double value from a row
    /// </summary>
    public static double GetDouble(Dictionary<string, object?> row, string key, double defaultValue = 0.0)
    {
        if (row.TryGetValue(key, out var value) && value != null)
        {
            return Convert.ToDouble(value);
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Escapes a string for SQL
    /// </summary>
    public static string Escape(string value) => value.Replace("'", "''");
}
