using DersDagitim.Models;

namespace DersDagitim.Persistence;

/// <summary>
/// Building/Location repository
/// </summary>
public class BuildingRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;
    
    public List<Building> GetAll()
    {
        var results = _db.Query("SELECT * FROM mekanlar");
        return results.Select(row => new Building
        {
            Id = DatabaseManager.GetInt(row, "id"),
            Name = DatabaseManager.GetString(row, "ad"),
            Color = DatabaseManager.GetString(row, "renk", "#808080")
        }).ToList();
    }
    
    public void Save(Building building)
    {
        var escapedName = DatabaseManager.Escape(building.Name);
        var escapedColor = DatabaseManager.Escape(building.Color);
        
        if (building.Id == 0)
        {
            _db.Execute($"INSERT INTO mekanlar (ad, renk) VALUES ('{escapedName}', '{escapedColor}')");
        }
        else
        {
            _db.Execute($"UPDATE mekanlar SET ad = '{escapedName}', renk = '{escapedColor}' WHERE id = {building.Id}");
        }
    }
    
    public void Delete(int id)
    {
        _db.Execute($"DELETE FROM mekanlar WHERE id = {id}");
    }
}

/// <summary>
/// Club repository
/// </summary>
public class ClubRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;
    
    public List<Club> GetAll()
    {
        var results = _db.Query("SELECT * FROM klubler");
        return results.Select(row => new Club
        {
            Id = DatabaseManager.GetInt(row, "id"),
            Name = DatabaseManager.GetString(row, "ad")
        }).ToList();
    }
    
    public void Save(Club club)
    {
        var escapedName = DatabaseManager.Escape(club.Name);
        
        if (club.Id == 0)
        {
            _db.Execute($"INSERT INTO klubler (ad) VALUES ('{escapedName}')");
        }
        else
        {
            _db.Execute($"UPDATE klubler SET ad = '{escapedName}' WHERE id = {club.Id}");
        }
    }
    
    public void Delete(int id)
    {
        _db.Execute($"DELETE FROM klubler WHERE id = {id}");
    }
}

/// <summary>
/// Duty location repository
/// </summary>
public class DutyLocationRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;
    
    public List<DutyLocation> GetAll()
    {
        var results = _db.Query("SELECT * FROM nobet_yerleri");
        return results.Select(row => new DutyLocation
        {
            Id = DatabaseManager.GetInt(row, "id"),
            Name = DatabaseManager.GetString(row, "ad")
        }).ToList();
    }
    
    public void Save(DutyLocation location)
    {
        var escapedName = DatabaseManager.Escape(location.Name);
        
        if (location.Id == 0)
        {
            _db.Execute($"INSERT INTO nobet_yerleri (ad) VALUES ('{escapedName}')");
        }
        else
        {
            _db.Execute($"UPDATE nobet_yerleri SET ad = '{escapedName}' WHERE id = {location.Id}");
        }
    }
    
    public void Delete(int id)
    {
        _db.Execute($"DELETE FROM nobet_yerleri WHERE id = {id}");
    }
}

/// <summary>
/// Ortak Mekan (Shared Location) repository
/// </summary>
public class OrtakMekanRepository
{
    private readonly DatabaseManager _db = DatabaseManager.Shared;
    
    public List<OrtakMekan> GetAll()
    {
        var results = _db.Query("SELECT * FROM ortak_mekan ORDER BY ad");
        return results.Select(row => 
        {
            var m = new OrtakMekan
            {
                Id = DatabaseManager.GetInt(row, "id"),
                Name = DatabaseManager.GetString(row, "ad")
            };
            
            for (int d = 1; d <= 7; d++)
            {
                for (int h = 1; h <= 12; h++)
                {
                    string key = $"d_{d}_{h}";
                    if (row.ContainsKey(key))
                    {
                        var val = DatabaseManager.GetString(row, key);
                        if (!string.IsNullOrEmpty(val))
                        {
                            m.ScheduleInfo[new Models.TimeSlot(d, h)] = val;
                            // NEW: Treat ANY text in the room's cell as a blocking constraint
                            m.Constraints[new Models.TimeSlot(d, h)] = SlotState.Closed;
                        }
                        else
                        {
                             m.Constraints[new Models.TimeSlot(d, h)] = SlotState.Open;
                        }
                    }
                }
            }
            return m;
        }).ToList();
    }
    
    public void Save(OrtakMekan ortakMekan)
    {
        var escapedName = DatabaseManager.Escape(ortakMekan.Name);
        
        if (ortakMekan.Id == 0)
        {
            _db.Execute($"INSERT INTO ortak_mekan (ad) VALUES ('{escapedName}')");
        }
        else
        {
            _db.Execute($"UPDATE ortak_mekan SET ad = '{escapedName}' WHERE id = {ortakMekan.Id}");
        }
    }
    
    public void Delete(int id)
    {
        // Clear references in dagitim_bloklari before deleting
        for (int i = 1; i <= 7; i++)
            _db.Execute($"UPDATE dagitim_bloklari SET ortak_mekan_{i}_id = 0 WHERE ortak_mekan_{i}_id = {id}");
        _db.Execute($"DELETE FROM ortak_mekan WHERE id = {id}");
    }
}
