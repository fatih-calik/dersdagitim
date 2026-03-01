using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace LicenseDiagnostic;

class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== Lisans Tanılama Aracı ===\n");

        // 1. Get current MAC address
        var currentMac = GetMacAddress();
        Console.WriteLine($"✓ Şu anki MAC Adresi: {currentMac ?? "BULUNAMADI"}");

        // 2. Find database - check multiple locations
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var dbPath = FindDatabase(appDir);
        
        if (string.IsNullOrEmpty(dbPath))
        {
            Console.WriteLine("❌ Veritabanı bulunamadı!");
            Console.WriteLine($"   Aranan dizin: {appDir}");
            return;
        }

        Console.WriteLine($"✓ Veritabanı: {dbPath}\n");

        // 3. Read license from database
        var licenseCode = GetLicenseFromDb(dbPath);
        
        if (string.IsNullOrEmpty(licenseCode))
        {
            Console.WriteLine("❌ Veritabanında lisans bulunamadı!");
            return;
        }

        Console.WriteLine($"✓ Lisans kodu bulundu ({licenseCode.Length} karakter)");

        // 4. Decrypt license
        const string keyBSecret = "DersDagitimLicenseFileKey2024";
        var decrypted = DecryptLicense(licenseCode, keyBSecret);

        if (string.IsNullOrEmpty(decrypted))
        {
            Console.WriteLine("❌ Lisans şifresi çözülemedi!");
            return;
        }

        Console.WriteLine($"✓ Lisans çözüldü: {decrypted}\n");

        // 5. Parse license
        var parts = decrypted.Split('|');
        if (parts.Length >= 3)
        {
            var storedMac = parts[0];
            var schoolName = parts[1];
            var year = parts[2];

            Console.WriteLine("=== Lisans Detayları ===");
            Console.WriteLine($"Okul Adı: {schoolName}");
            Console.WriteLine($"Geçerlilik Yılı: {year}");
            Console.WriteLine($"Kayıtlı MAC: {storedMac}");
            Console.WriteLine($"Şu anki MAC: {currentMac}\n");

            if (storedMac == currentMac)
            {
                Console.WriteLine("✅ MAC adresleri eşleşiyor!");
            }
            else
            {
                Console.WriteLine("❌ MAC ADRESİ UYUŞMAZLIĞI!");
                Console.WriteLine("\nSorun: Lisanstaki MAC adresi ile şu anki MAC adresi farklı.");
                Console.WriteLine("Çözüm: Lisansı yeni MAC adresi ile yeniden oluşturmanız gerekiyor.");
            }
        }
        else
        {
            Console.WriteLine("❌ Lisans formatı geçersiz!");
        }
    }

    static string? GetMacAddress()
    {
        try
        {
            // Same logic as LicenseManager.cs
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            // 1. Try Ethernet (prefer active, but accept inactive)
            var nic = interfaces.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet && 
                                                      n.OperationalStatus == OperationalStatus.Up);
            nic ??= interfaces.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet);
            
            // 2. Try Wireless (prefer active, but accept inactive)
            if (nic == null)
            {
                nic = interfaces.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && 
                                                      n.OperationalStatus == OperationalStatus.Up);
                nic ??= interfaces.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
            }
            
            // 3. Fallback to any non-loopback interface (prefer active)
            if (nic == null)
            {
                nic = interfaces.FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up);
                nic ??= interfaces.FirstOrDefault();
            }
            
            if (nic == null) return null;
            
            var mac = nic.GetPhysicalAddress().ToString();
            
            // Format with colons
            if (mac.Length != 12) return mac;
            
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

    static string? FindDatabase(string appDir)
    {
        // Check data/ subfolder first, then root (backward compat)
        var dataDir = Path.Combine(appDir, "data");
        var searchDirs = Directory.Exists(dataDir)
            ? new[] { dataDir, appDir }
            : new[] { appDir };

        foreach (var dir in searchDirs)
        {
            // Check sabit.sqlite for active database
            var sabitPath = Path.Combine(dir, "sabit.sqlite");
            if (File.Exists(sabitPath))
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={sabitPath}");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT deger FROM ayarlar WHERE anahtar = 'aktif_veritabani'";
                    var result = cmd.ExecuteScalar();
                    if (result != null && File.Exists(result.ToString()))
                    {
                        return result.ToString();
                    }
                }
                catch { }
            }

            // Search for default database names
            var defaultPath = Path.Combine(dir, "ders_dagitim.sqlite");
            if (File.Exists(defaultPath)) return defaultPath;

            var legacyPath = Path.Combine(dir, "ders_dagitim.db");
            if (File.Exists(legacyPath)) return legacyPath;
        }

        // Search in parent directories (go up to 5 levels)
        var currentDir = new DirectoryInfo(appDir);
        for (int i = 0; i < 5 && currentDir != null; i++)
        {
            var sabitPath = Path.Combine(currentDir.FullName, "sabit.sqlite");
            if (File.Exists(sabitPath))
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={sabitPath}");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT deger FROM ayarlar WHERE anahtar = 'aktif_veritabani'";
                    var result = cmd.ExecuteScalar();
                    if (result != null && File.Exists(result.ToString()))
                    {
                        return result.ToString();
                    }
                }
                catch { }
            }

            var defaultPath = Path.Combine(currentDir.FullName, "ders_dagitim.sqlite");
            if (File.Exists(defaultPath)) return defaultPath;

            var legacyPath = Path.Combine(currentDir.FullName, "ders_dagitim.db");
            if (File.Exists(legacyPath)) return legacyPath;

            currentDir = currentDir.Parent;
        }

        return null;
    }

    static string? GetLicenseFromDb(string dbPath)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ls FROM okul LIMIT 1";
            var result = cmd.ExecuteScalar();
            return result?.ToString();
        }
        catch
        {
            return null;
        }
    }

    static string? DecryptLicense(string base64, string secret)
    {
        try
        {
            // Try AES-GCM first
            var result = DecryptGcm(base64, secret);
            if (result != null) return result;

            // Fallback to AES-CBC
            return DecryptLegacy(base64, secret);
        }
        catch
        {
            return null;
        }
    }

    static string? DecryptGcm(string base64, string secret)
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

    static string? DecryptLegacy(string base64, string secret)
    {
        try
        {
            var fullBytes = Convert.FromBase64String(base64);
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
            
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
    }

    static byte[] DeriveKey(string secret)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(secret));
    }
}
