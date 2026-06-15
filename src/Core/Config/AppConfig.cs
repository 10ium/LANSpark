using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace LANSpark.Core.Config
{
    // کلاس مدل برای پوشه‌های اشتراک‌گذاری شده با امکانات کنترلی پیشرفته
    public class SharedFolder
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FolderPath { get; set; } = string.Empty;
        public string FolderName => Path.GetFileName(FolderPath);
        public bool IsPublic { get; set; } = true; // عمومی برای همه یا خصوصی
        public string TargetMachineId { get; set; } = string.Empty; // در صورت خصوصی بودن، آی‌دی سیستم مقصد
        public bool IsPaused { get; set; } = false; // توقف موقتی اشتراک‌گذاری
    }

    public class AppConfig
    {
        // تنظیمات کاربری
        public string Language { get; set; } = "fa"; // "fa" یا "en"
        public string AppTheme { get; set; } = "Dark"; // "Dark" یا "Light"

        // تنظیمات درگاه‌های شبکه
        public int TransferPort { get; set; } = 45055;
        public int ChunkSize { get; set; } = 65536; 
        public int MaxParallelConnections { get; set; } = 8; 
        public bool EnableCompression { get; set; } = true; 

        public int ChatPort { get; set; } = 45056;
        public string ChatProtocol { get; set; } = "TLS_TCP"; 
        
        public bool AutoEnableWindowsSMB { get; set; } = true;
        public string DefaultWindowsShareName { get; set; } = "LANSparkShare";

        // لیست پیشرفته پوشه‌های به اشتراک گذاشته شده در نرم‌افزار
        public List<SharedFolder> SharedFolders { get; set; } = new List<SharedFolder>();

        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(this, options);
                File.WriteAllText(ConfigPath, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving configuration: {ex.Message}");
            }
        }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string jsonString = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<AppConfig>(jsonString) ?? new AppConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
            }
            return new AppConfig();
        }
    }
}
