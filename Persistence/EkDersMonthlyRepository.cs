using System;
using System.Collections.Generic;

namespace DersDagitim.Persistence;

/// <summary>
/// Repository for monthly Ek Ders data (ekders_aylik table)
/// Stores: [TeacherId, Year, Month, Day, Type, Value]
/// </summary>
public class EkDersMonthlyRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;
    
    /// <summary>
    /// Load grid data for a specific teacher/month/year
    /// Returns: Dictionary[TypeCode, Dictionary[Day, Value]]
    /// </summary>
    public Dictionary<string, Dictionary<int, int>> Load(int teacherId, int year, int month)
    {
        var results = _db.Query($@"
            SELECT gun, tur, deger FROM ekders_aylik 
            WHERE ogretmen_id = {teacherId} AND yil = {year} AND ay = {month}
        ");
        
        var grid = new Dictionary<string, Dictionary<int, int>>();
        
        foreach (var row in results)
        {
            int day = DatabaseManager.GetInt(row, "gun");
            string type = DatabaseManager.GetString(row, "tur");
            int value = DatabaseManager.GetInt(row, "deger");
            
            if (!grid.ContainsKey(type))
                grid[type] = new Dictionary<int, int>();
            
            grid[type][day] = value;
        }
        
        return grid;
    }
    
    /// <summary>
    /// Save grid data for a specific teacher/month/year
    /// Input: Dictionary[TypeCode, Dictionary[Day, Value]]
    /// Note: Skips zero values to save space
    /// </summary>
    public void Save(int teacherId, int year, int month, Dictionary<string, Dictionary<int, int>> grid)
    {
        // Delete existing data first
        Delete(teacherId, year, month);
        
        foreach (var (type, dayValues) in grid)
        {
            // Skip types where all values are 0
            int typeTotal = 0;
            foreach (var v in dayValues.Values) typeTotal += v;
            if (typeTotal == 0) continue;
            
            foreach (var (day, value) in dayValues)
            {
                if (value == 0) continue;
                
                var escapedType = type.Replace("'", "''");
                _db.Execute($@"
                    INSERT OR REPLACE INTO ekders_aylik 
                    (ogretmen_id, yil, ay, gun, tur, deger) 
                    VALUES ({teacherId}, {year}, {month}, {day}, '{escapedType}', {value})
                ");
            }
        }
    }
    
    /// <summary>
    /// Check if data exists for a given period
    /// </summary>
    public bool HasData(int teacherId, int year, int month)
    {
        var results = _db.Query($@"
            SELECT COUNT(*) as cnt FROM ekders_aylik 
            WHERE ogretmen_id = {teacherId} AND yil = {year} AND ay = {month}
        ");
        if (results.Count > 0)
            return DatabaseManager.GetInt(results[0], "cnt") > 0;
        return false;
    }
    
    /// <summary>
    /// Delete data for a given period (useful for reset)
    /// </summary>
    public void Delete(int teacherId, int year, int month)
    {
        _db.Execute($@"
            DELETE FROM ekders_aylik 
            WHERE ogretmen_id = {teacherId} AND yil = {year} AND ay = {month}
        ");
    }
    
    /// <summary>
    /// Delete ALL monthly ek ders data
    /// </summary>
    public void DeleteAll()
    {
        _db.Execute("DELETE FROM ekders_aylik");
    }
    
    /// <summary>
    /// Get total hours for specified month/year grouped by teacher
    /// </summary>
    public Dictionary<int, int> GetMonthlyTotalsByTeacher(int year, int month)
    {
        var results = _db.Query($@"
            SELECT ogretmen_id, SUM(deger) as toplam FROM ekders_aylik 
            WHERE yil = {year} AND ay = {month}
            GROUP BY ogretmen_id
        ");
        
        var totals = new Dictionary<int, int>();
        foreach (var row in results)
        {
            int teacherId = DatabaseManager.GetInt(row, "ogretmen_id");
            int total = DatabaseManager.GetInt(row, "toplam");
            totals[teacherId] = total;
        }
        return totals;
    }
}
