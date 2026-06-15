using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace LANSpark.Core.Config
{
    // ساختار پاسخ جی‌سان دریافتی از ریپازیتوری گیت‌هاب
    public class GitHubReleaseInfo
    {
        public string tag_name { get; set; } = string.Empty;
        public string html_url { get; set; } = string.Empty;
        public List<GitHubAsset> assets { get; set; } = new();
    }

    public class GitHubAsset
    {
        public string name { get; set; } = string.Empty;
        public string browser_download_url { get; set; } = string.Empty;
    }

    public class UpdateManager
    {
        private readonly string _githubUser = "YourGitHubUsername"; // نام کاربری گیت‌هاب شما
        private readonly string _githubRepo = "YourRepositoryName"; // نام ریپازیتوری شما
        private readonly HttpClient _httpClient;

        public UpdateManager()
        {
            _httpClient = new HttpClient();
            // گیت‌هاب برای پاسخ به درخواست‌های API نیاز به مشخص بودن هدر User-Agent دارد
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "LANSpark-AutoUpdater");
        }

        // ۱. دریافت نسخه فعلی کامپایل شده روی سیستم کاربر
        public string GetCurrentVersion()
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            return version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v1.0.0";
        }

        // ۲. بررسی وجود نسخه جدید در بخش Release گیت‌هاب
        public async Task<(bool IsNewVersionAvailable, string LatestVersion, string DownloadUrl)> CheckForUpdatesAsync()
        {
            try
            {
                string url = $"https://api.github.com/repos/{_githubUser}/{_githubRepo}/releases/latest";
                string jsonResponse = await _httpClient.GetStringAsync(url);
                
                var releaseInfo = JsonSerializer.Deserialize<GitHubReleaseInfo>(jsonResponse);
                if (releaseInfo == null || string.IsNullOrEmpty(releaseInfo.tag_name))
                {
                    return (false, string.Empty, string.Empty);
                }

                string currentVerStr = GetCurrentVersion().Replace("v", "");
                string latestVerStr = releaseInfo.tag_name.Replace("v", "");

                var currentVersion = new Version(currentVerStr);
                var latestVersion = new Version(latestVerStr);

                // مقایسه نسخه‌ها به صورت استاندارد
                if (latestVersion > currentVersion && releaseInfo.assets.Count > 0)
                {
                    // استخراج اولین لینک دانلود فایل نصبی یا پرتابل زیپ شده
                    string downloadUrl = releaseInfo.assets[0].browser_download_url;
                    return (true, releaseInfo.tag_name, downloadUrl);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update check failed: {ex.Message}");
            }

            return (false, string.Empty, string.Empty);
        }

        // ۳. دانلود نسخه جدید و اجرای سناریوی نصب خودکار و ریستارت برنامه
        public async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl)
        {
            try
            {
                string tempFolder = Path.Combine(Path.GetTempPath(), "LANSparkUpdate");
                if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
                Directory.CreateDirectory(tempFolder);

                string zipPath = Path.Combine(tempFolder, "update.zip");

                // دانلود فایل بافر شده با نهایت سرعت پهنای باند
                byte[] fileBytes = await _httpClient.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(zipPath, fileBytes);

                string appPath = AppDomain.CurrentDomain.BaseDirectory;
                string processName = Process.GetCurrentProcess().ProcessName + ".exe";

                // ایجاد یک اسکریپت باتم‌آپ (Batch) برای بستن برنامه فعلی، اکسترکت زیپ و جایگزینی فایل جدید و بالا آوردن مجدد نرم‌افزار
                string batchScript = $@"
@echo off
timeout /t 2 /nobreak > nul
powershell -Command ""Expand-Archive -Path '{zipPath}' -DestinationPath '{tempFolder}\extracted' -Force""
xcopy /y /s /e ""{tempFolder}\extracted\*"" ""{appPath}""
rd /s /q ""{tempFolder}""
start """" /d ""{appPath}"" ""{processName}""
exit
";
                string batchFilePath = Path.Combine(Path.GetTempPath(), "install_update.bat");
                await File.WriteAllTextAsync(batchFilePath, batchScript, Encoding.GetEncoding("windows-1256"));

                // اجرای اسکریپت نصب و بستن برنامه جاری جهت بازنویسی بایت‌های اجرایی
                var processInfo = new ProcessStartInfo
                {
                    FileName = batchFilePath,
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                Process.Start(processInfo);
                Environment.Exit(0); // خروج از نسخه قدیمی
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to apply update: {ex.Message}");
                return false;
            }
        }
    }
}
