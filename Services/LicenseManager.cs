using System.Security.Cryptography;
using System.Text;
using System.Net.NetworkInformation;
using DersDagitim.Models;
using DersDagitim.Persistence;

namespace DersDagitim.Services;

/// <summary>
/// License validation result
/// </summary>
public class LicenseValidationResult
{
    public LicenseStatus Status { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public int ExpirationYear { get; set; }
    public string LicensedMac { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public class LicenseManager
{
    // ... (Singleton & Keys same) ...
    // Note: Re-stating keys and singleton is not needed for tool, just context.
    // Focusing on ValidateLicense implementation update.

    private static readonly Lazy<LicenseManager> _instance = new(() => new LicenseManager());
    public static LicenseManager Shared => _instance.Value;
    
    private const string KeyASecret = "DersDagitimRequestCodeKey2024";
    private const string KeyBSecret = "DersDagitimLicenseFileKey2024";
    
    private LicenseManager() { }
    
    /// <summary>
    /// Gets the request code (encrypted MAC address)
    /// </summary>
    public string GetRequestCode()
    {
        var mac = GetMacAddress();
        if (string.IsNullOrEmpty(mac))
        {
            return "MAC-ERISIM-YOK";
        }
        
        try
        {
            return Encrypt(mac, KeyASecret) ?? "CRYPTO-ERROR";
        }
        catch
        {
            return "CRYPTO-ERROR";
        }
    }

    /// <summary>
    /// Validates the current license
    /// </summary>
    public LicenseValidationResult ValidateLicense()
    {
        var repo = new SchoolRepository();
        var encryptedContent = repo.GetLicenseCode();
        
        if (string.IsNullOrEmpty(encryptedContent))
        {
            return new LicenseValidationResult { Status = LicenseStatus.Missing };
        }
        
        // DEBUG INFO
        // System.Windows.MessageBox.Show($"Lisans Kontrol: DB'den okunan ({encryptedContent.Length} karakter):\n{encryptedContent.Substring(0, Math.Min(20, encryptedContent.Length))}...", "Debug");

        // Check corruption marker
        bool isCorrupt = encryptedContent.StartsWith("+%");
        string contentToDecrypt = isCorrupt ? encryptedContent.Substring(2) : encryptedContent;
        
        // Decrypt license content FIRST
        var decrypted = Decrypt(contentToDecrypt, KeyBSecret);
        
        if (string.IsNullOrEmpty(decrypted))
        {
            // System.Windows.MessageBox.Show("Decrypt Başarısız! Anahtar veya veri hatalı.", "Debug");
            return new LicenseValidationResult { Status = LicenseStatus.Invalid, Error = "Şifre Çözülemedi" };
        }

        // System.Windows.MessageBox.Show($"Çözülen Veri: {decrypted}", "Debug");

        // --- DEMO LICENSE CHECK ---
        if (decrypted.StartsWith("DEMO:"))
        {
            // Format: DEMO:yyyy-MM-dd:Counter
            var parts = decrypted.Split(':');
            if (parts.Length < 3)
            {
                return new LicenseValidationResult { Status = LicenseStatus.Invalid, Error = "Demo Format Hatası" };
            }

            if (!DateTime.TryParse(parts[1], out DateTime lastRunDate))
            {
                return new LicenseValidationResult { Status = LicenseStatus.Invalid, Error = "Demo Tarih Hatası" };
            }

            if (!int.TryParse(parts[2], out int counter))
            {
                return new LicenseValidationResult { Status = LicenseStatus.Invalid, Error = "Demo Sayaç Hatası" };
            }

            // 1. Check Date Manipulation
            if (lastRunDate.Date > DateTime.Now.Date)
            {
                repo.SaveLicenseCode(""); // Remove license
                return new LicenseValidationResult { Status = LicenseStatus.Expired, Error = "Sistem saati geri alınmış (Demo İptal)" };
            }

            // 2. Check Counter
            if (counter > 40)
            {
                repo.SaveLicenseCode(""); // Remove license
                return new LicenseValidationResult { Status = LicenseStatus.Expired, Error = "Demo kullanım hakkı doldu" };
            }

            // --- UPDATE DEMO STATE ---
            counter++;
            var newDemoContent = $"DEMO:{DateTime.Now:yyyy-MM-dd}:{counter}";
            var newEncrypted = Encrypt(newDemoContent, KeyBSecret);
            
            if (!string.IsNullOrEmpty(newEncrypted))
            {
                repo.SaveLicenseCode(newEncrypted);
                repo.UpdateSchoolName($"DEMO OKUL ({counter}. Kullanım)");
            }

            return new LicenseValidationResult 
            { 
                Status = LicenseStatus.Valid, 
                SchoolName = "DEMO OKUL",
                ExpirationYear = DateTime.Now.Year + 1,
                LicensedMac = "DEMO-USER"
            };
        }
        // --------------------------
        
        // Parse Standard License (MAC|SCHOOL|YEAR)
        var splitParts = decrypted.Split('|');
        if (splitParts.Length < 3)
        {
            // Console.WriteLine("LicenseManager: Invalid format");
            return new LicenseValidationResult { Status = LicenseStatus.Invalid, Error = "Geçersiz Format" };
        }
        
        var fileMac = splitParts[0];
        var schoolName = splitParts[1];
        var yearString = splitParts[2];
        
        int.TryParse(yearString, out int licenseYear);
        
        var result = new LicenseValidationResult 
        { 
            SchoolName = schoolName,
            ExpirationYear = licenseYear,
            LicensedMac = fileMac
        };
        
        if (isCorrupt)
        {
             result.Status = LicenseStatus.Expired;
             result.Error = "Süresi Dolmuş (İşaretli)";
             return result;
        }
        
        // Check MAC integrity - Check if licensed MAC exists on ANY network adapter
        var allMacs = GetAllMacAddresses();
        if (allMacs.Count == 0)
        {
            result.Status = LicenseStatus.Invalid;
            result.Error = "Ağ Adaptörü Bulunamadı";
            return result;
        }
        
        // Check if the licensed MAC exists on any adapter (active or inactive)
        if (!allMacs.Contains(fileMac))
        {
            // Console.WriteLine($"LicenseManager: MAC not found. Licensed: {fileMac}, Available: {string.Join(", ", allMacs)}");
            result.Status = LicenseStatus.Invalid;
            result.Error = "MAC Adresi Uyuşmazlığı";
            return result;
        }
        
        // Check year expiration
        var currentYear = DateTime.Now.Year;
        // Logic: License is valid FOR the year specified. E.g. 2025 license is valid until 31.12.2025.
        // If currentYear > licenseYear, it is expired.
        if (licenseYear > 0 && currentYear > licenseYear)
        {
            InvalidateLicense();
            result.Status = LicenseStatus.Expired;
            result.Error = "Süresi Dolmuş";
            return result;
        }
        
        result.Status = LicenseStatus.Valid;
        return result;
    }
    
    /// <summary>
    /// Gets the license expiry date
    /// </summary>
    public DateTime? GetExpiryDate()
    {
        var result = ValidateLicense();
        if (result.Status == LicenseStatus.Missing || result.ExpirationYear == 0)
            return null;
        
        // Return end of the licensed year (Dec 31)
        return new DateTime(result.ExpirationYear, 12, 31);
    }
    
    /// <summary>
    /// Installs a license code
    /// </summary>
    public bool InstallLicense(string code)
    {
        // Validate before saving
        var decrypted = Decrypt(code, KeyBSecret);
        if (string.IsNullOrEmpty(decrypted))
        {
            // Console.WriteLine("Install license error: Cannot decrypt provided code");
            return false;
        }
        
        new SchoolRepository().SaveLicenseCode(code);
        ConfigManager.Shared.SetBackupLicense(code);
        return true;
    }
    
    /// <summary>
    /// Invalidates the current license
    /// </summary>
    public void InvalidateLicense()
    {
        var repo = new SchoolRepository();
        var currentCode = repo.GetLicenseCode();
        
        if (string.IsNullOrEmpty(currentCode) || currentCode.StartsWith("+%"))
        {
            return;
        }
        
        repo.SaveLicenseCode("+%" + currentCode);
    }
    
    /// <summary>
    /// Generates license content (for key generator)
    /// </summary>
    public string? GenerateLicenseContent(string mac, string school, int year)
    {
        var content = $"{mac}|{school}|{year}";
        return Encrypt(content, KeyBSecret);
    }
    
    /// <summary>
    /// Decrypts a request code
    /// </summary>
    public string? DecryptRequestCode(string code)
    {
        return Decrypt(code, KeyASecret);
    }
    
    /// <summary>
    /// Gets the primary MAC address (Only active interfaces - for request code generation)
    /// </summary>
    private static string? GetMacAddress()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && 
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            // 1. Try Ethernet
            var nic = interfaces.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet);
            
            // 2. Try Wireless
            if (nic == null)
            {
                nic = interfaces.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
            }
            
            // 3. Fallback to any active interface
            nic ??= interfaces.FirstOrDefault();
            
            if (nic == null) return null;
            
            var mac = nic.GetPhysicalAddress().ToString();
            
            // Format with colons xx:xx:xx:xx:xx:xx
            if (mac.Length != 12) return mac; // Should not happen for standard MACs
            
            var formatted = new StringBuilder();
            for (int i = 0; i < mac.Length; i += 2)
            {
                if (i > 0) formatted.Append(':');
                formatted.Append(mac.Substring(i, 2));
            }
            
            return formatted.ToString();
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Gets all MAC addresses from all network adapters (active and inactive)
    /// </summary>
    private static List<string> GetAllMacAddresses()
    {
        var macAddresses = new List<string>();
        
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            foreach (var nic in interfaces)
            {
                try
                {
                    var mac = nic.GetPhysicalAddress().ToString();
                    
                    // Skip empty MAC addresses
                    if (string.IsNullOrEmpty(mac) || mac == "000000000000")
                        continue;
                    
                    // Format with colons xx:xx:xx:xx:xx:xx
                    if (mac.Length == 12)
                    {
                        var formatted = new StringBuilder();
                        for (int i = 0; i < mac.Length; i += 2)
                        {
                            if (i > 0) formatted.Append(':');
                            formatted.Append(mac.Substring(i, 2));
                        }
                        macAddresses.Add(formatted.ToString());
                    }
                    else if (mac.Length > 0)
                    {
                        macAddresses.Add(mac);
                    }
                }
                catch
                {
                    // Skip adapters that can't be read
                    continue;
                }
            }
        }
        catch
        {
            // Return empty list on error
        }
        
        return macAddresses;
    }
    
    /// <summary>
    /// Encrypts text using AES-GCM (Matching Swift CryptoKit)
    /// Output Format: nonce(12) + ciphertext(n) + tag(16) -> Base64
    /// </summary>
    private static string? Encrypt(string text, string secret)
    {
        try
        {
            var key = DeriveKey(secret); // 32 bytes (256-bit)
            var nonce = new byte[12]; // 96-bit nonce
            RandomNumberGenerator.Fill(nonce);
            
            var plainBytes = Encoding.UTF8.GetBytes(text);
            var cipherBytes = new byte[plainBytes.Length];
            var tag = new byte[16]; // 128-bit tag

            using var aesGcm = new AesGcm(key, 16); // 16 bytes tag size is default, but explicit for clarity
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag); // Available in .NET Core 3.0+ / .NET 5+

            // Combine: Nonce + Cipher + Tag
            var result = new byte[nonce.Length + cipherBytes.Length + tag.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, nonce.Length, cipherBytes.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length + cipherBytes.Length, tag.Length);

            return Convert.ToBase64String(result);
        }
        catch
        {
            // Console.WriteLine($"Encrypt Error: {ex.Message}");
            return null;
        }
    }
    
    private static string? Decrypt(string base64, string secret)
    {
        try
        {
            // 1. Try AES-GCM (New Format)
            string? result = DecryptGcm(base64, secret);
            if (result != null) return result;

            // 2. Try AES-CBC (Legacy Format)
            return DecryptLegacy(base64, secret);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decrypts text using AES-GCM
    /// </summary>
    private static string? DecryptGcm(string base64, string secret)
    {
        try
        {
            var fullBytes = Convert.FromBase64String(base64);
            if (fullBytes.Length < 28) return null;

            var key = DeriveKey(secret);
            var nonce = new byte[12];
            var tag = new byte[16];
            var cipherBytes = new byte[fullBytes.Length - 12 - 16];

            Buffer.BlockCopy(fullBytes, 0, nonce, 0, 12);
            Buffer.BlockCopy(fullBytes, 12, cipherBytes, 0, cipherBytes.Length);
            Buffer.BlockCopy(fullBytes, 12 + cipherBytes.Length, tag, 0, 16);
            
            var plainBytes = new byte[cipherBytes.Length];
            using var aesGcm = new AesGcm(key, 16); 
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decrypts text using AES-CBC (Legacy Fallback)
    /// </summary>
    private static string? DecryptLegacy(string base64, string secret)
    {
        try
        {
            var fullBytes = Convert.FromBase64String(base64);
            // IV is usually 16 bytes for AES
            if (fullBytes.Length <= 16) return null;

            var key = DeriveKey(secret);
            
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var iv = new byte[16];
            var cipherBytes = new byte[fullBytes.Length - 16];

            Buffer.BlockCopy(fullBytes, 0, iv, 0, 16);
            Buffer.BlockCopy(fullBytes, 16, cipherBytes, 0, cipherBytes.Length);

            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            
            // Try UTF8 first
            try 
            {
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // If invalid UTF8, maybe Windows-1254?
                // But generally UTF8 GetString doesn't throw, it replaces.
                return Encoding.UTF8.GetString(plainBytes);
            }
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Derives a 256-bit key from secret using SHA256
    /// </summary>
    private static byte[] DeriveKey(string secret)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(secret));
    }
}
