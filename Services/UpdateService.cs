using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DersDagitim.Services;

public static class UpdateService
{
    // GitHub repo bilgileri
    private const string GitHubOwner = "fatih-calik";
    private const string GitHubRepo = "dersdagitim";
    private const string LatestReleaseApiUrl =
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    private static readonly HttpClient _httpClient = new();

    static UpdateService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "DersDagitim-Updater");
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    /// <summary>
    /// GitHub'dan son release'in ilk ZIP asset'ini bulur ve indirir.
    /// </summary>
    public static async Task<string> DownloadUpdateAsync(
        IProgress<(int percent, string status)> progress,
        CancellationToken ct = default)
    {
        progress.Report((0, "GitHub'dan sürüm bilgisi alınıyor..."));

        // 1. Son release bilgisini al
        var releaseResponse = await _httpClient.GetAsync(LatestReleaseApiUrl, ct);
        releaseResponse.EnsureSuccessStatusCode();

        var json = await releaseResponse.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 2. İlk ZIP asset'i bul
        string? downloadUrl = null;
        string fileName = "DersDagitim_Update.zip";
        long fileSize = 0;

        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    fileName = name;
                    fileSize = asset.GetProperty("size").GetInt64();
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(downloadUrl))
            throw new Exception("Release'de ZIP dosyası bulunamadı.");

        progress.Report((5, $"Bulundu: {fileName}"));

        // 3. ZIP'i indir
        var tempDir = Path.Combine(Path.GetTempPath(), "DersDagitim_Update");
        Directory.CreateDirectory(tempDir);

        var zipPath = Path.Combine(tempDir, fileName);

        if (File.Exists(zipPath))
            File.Delete(zipPath);

        var response = await _httpClient.GetAsync(downloadUrl,
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? fileSize;
        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(zipPath, FileMode.Create,
            FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int lastReportedPercent = 0;

        int read;
        while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            bytesRead += read;

            if (totalBytes > 0)
            {
                var percent = (int)(bytesRead * 100 / totalBytes);
                if (percent != lastReportedPercent)
                {
                    lastReportedPercent = percent;
                    var mbDone = bytesRead / (1024.0 * 1024.0);
                    var mbTotal = totalBytes / (1024.0 * 1024.0);
                    progress.Report((percent,
                        $"İndiriliyor... {mbDone:F1}/{mbTotal:F1} MB"));
                }
            }
            else
            {
                var mbDone = bytesRead / (1024.0 * 1024.0);
                progress.Report((-1, $"İndiriliyor... {mbDone:F1} MB"));
            }
        }

        progress.Report((100, "İndirme tamamlandı."));
        return zipPath;
    }

    /// <summary>
    /// Batch script yazar: ZIP çıkart (.sqlite/.db koru), uygulamayı yeniden başlat.
    /// </summary>
    public static void ApplyUpdate(string zipPath)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var appExe = Path.Combine(appDir, "DersDagitim.exe");
        var tempDir = Path.GetTempPath();
        var ps1Path = Path.Combine(tempDir, "DersDagitim_Update.ps1");
        var batchPath = Path.Combine(tempDir, "DersDagitim_Update.bat");

        var escapedZipPath = zipPath.Replace("'", "''");
        var escapedAppDir = appDir.TrimEnd('\\').Replace("'", "''");

        // PowerShell scriptini ayrı dosyaya yaz
        var ps1Content = "Add-Type -AssemblyName System.IO.Compression.FileSystem\r\n"
            + "$zip = [System.IO.Compression.ZipFile]::OpenRead('" + escapedZipPath + "')\r\n"
            + "foreach ($entry in $zip.Entries) {\r\n"
            + "    if ($entry.Name -like '*.sqlite' -or $entry.Name -like '*.db' -or $entry.Name -like '*.db-*' -or $entry.Name -like 'repository.fth') { continue }\r\n"
            + "    $target = Join-Path '" + escapedAppDir + "' $entry.FullName\r\n"
            + "    if ([string]::IsNullOrEmpty($entry.Name)) {\r\n"
            + "        if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }\r\n"
            + "    } else {\r\n"
            + "        $parent = Split-Path $target\r\n"
            + "        if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }\r\n"
            + "        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)\r\n"
            + "    }\r\n"
            + "}\r\n"
            + "$zip.Dispose()\r\n";

        var noBom = new System.Text.UTF8Encoding(false);
        File.WriteAllText(ps1Path, ps1Content, noBom);

        var batchContent = "@echo off\r\n"
            + "chcp 65001 >nul\r\n"
            + "echo ========================================\r\n"
            + "echo   Ders Dagitim Guncelleme\r\n"
            + "echo ========================================\r\n"
            + "echo.\r\n"
            + "echo Uygulama kapanmasi bekleniyor...\r\n"
            + "timeout /t 3 /nobreak >nul\r\n"
            + "echo Dosyalar cikartiliyor...\r\n"
            + "powershell -NoProfile -ExecutionPolicy Bypass -File \"" + ps1Path + "\"\r\n"
            + "if %ERRORLEVEL% neq 0 (\r\n"
            + "    echo.\r\n"
            + "    echo HATA: Dosya cikartma basarisiz!\r\n"
            + "    pause\r\n"
            + "    exit /b 1\r\n"
            + ")\r\n"
            + "echo.\r\n"
            + "echo Gecici dosyalar temizleniyor...\r\n"
            + "del /q \"" + zipPath + "\" 2>nul\r\n"
            + "del /q \"" + ps1Path + "\" 2>nul\r\n"
            + "echo Uygulama yeniden baslatiliyor...\r\n"
            + "start \"\" \"" + appExe + "\"\r\n"
            + "del /q \"" + batchPath + "\" 2>nul\r\n"
            + "exit\r\n";

        File.WriteAllText(batchPath, batchContent, noBom);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batchPath}\"",
            UseShellExecute = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal
        };

        System.Diagnostics.Process.Start(startInfo);
        System.Windows.Application.Current.Shutdown();
    }
}
