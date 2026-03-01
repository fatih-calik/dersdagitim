namespace DersDagitim.Models;

public class DistributionParameters
{
    public OperationMode OperationMode { get; set; } = OperationMode.Rebuild;
    public PlacementMode PlacementMode { get; set; } = PlacementMode.ClearAll;
    
    // Limits
    public int MaxDays { get; set; } = 5;
    public int MaxHours { get; set; } = 12; // Okul varsayılanı genelde
    public int MaxTimeInSeconds { get; set; } = 300; // 5 dakika (artırıldı)
    
    // Rules
    public bool UseStrictMode { get; set; } = false;   // Esnek mod - yerleşemeyenleri atla
    public bool UseAnalysisMode { get; set; } = false; // Teşhis modu (Kısıtları görmezden gelip raporla)
    
    // Weights / Penalties
    public int GapPenalty { get; set; } = 800000;
    public double MorningPenalty { get; set; } = 3.0; // Sabah önceliği puanı
    public int AdjacencyReward { get; set; } = 10000;
    public double BuildingSwitchPenalty { get; set; } = 50000.0;
    
    // Constraints
    public int MinDailyLessons { get; set; } = 2; // Öğretmen okula gelirse en az X ders
    public bool BalanceTeachers { get; set; } = true;

    // A. Boş Gün Oluşturma (Day Condensation)
    public bool MinimizeWorkingDays { get; set; } = false;
    
    // Improve Mode
    public double PerturbationRate { get; set; } = 0.0; // % kaçını bozup tekrar yapayım?
    
    // New Parameters (Swift Match)
    public int BalancePenalty { get; set; } = 100; // Yük dengeleme cezası
    public bool UseExternalPython { get; set; } = false; // Python scripti ile dağıt (Deneysel)
    
    // Engine Selection
    public int EngineVersion { get; set; } = 2; // 2=Native V2, 3=AI V3
    
    public int V3GapPenalty { get; set; } = 100;
}
