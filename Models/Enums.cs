namespace DersDagitim.Models;

/// <summary>
/// Days of the week (1-7: Monday to Sunday)
/// </summary>
public enum Day
{
    Monday = 1,
    Tuesday = 2,
    Wednesday = 3,
    Thursday = 4,
    Friday = 5,
    Saturday = 6,
    Sunday = 7
}

/// <summary>
/// Slot state for scheduling constraints
/// </summary>
public enum SlotState
{
    Open,   // Can be assigned
    Closed  // Cannot be assigned (Hard constraint)
}

/// <summary>
/// Lock state for placements
/// </summary>
public enum LockState
{
    Unlocked,
    Locked  // User manually locked this placement
}

/// <summary>
/// License validation status
/// </summary>
public enum LicenseStatus
{
    Valid,
    Missing,
    Invalid,
    Expired
}

/// <summary>
/// Operation mode for distribution engine
/// </summary>
public enum OperationMode
{
    Rebuild,
    Improve,
    Analyze
}

public enum PlacementMode
{
    ClearAll,
    KeepPlaced,
    KeepManual,
    KeepLocked,
    KeepCurrent  // Yerleşmiş olanları hiç silme, sadece yerleşmeyenlere yer ara
}

/// <summary>
/// Teacher position/role types
/// </summary>
public enum TeacherRole
{
    Ogretmen,       // Teacher
    Mudur,          // Principal
    MudurBasYrd,    // Vice Principal
    MudurYrd,       // Assistant Principal
    Rehberlik,      // Guidance Counselor
    Formator,       // Trainer
    Sozlesme,       // Contract Teacher
    MudurPansiyonYrd // Dormitory Assistant Principal
}

/// <summary>
/// Education level
/// </summary>
public enum EducationLevel
{
    Lisans,         // Bachelor's
    YuksekLisans,   // Master's
    Doktora         // PhD
}
