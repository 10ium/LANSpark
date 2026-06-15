using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace LANSpark.Core.Config
{
    public class AppConfig
    {
        // تنظیمات عمومی و زبان
        public string Language { get; set; } = "fa"; // "fa" یا "en"
        public string AppTheme { get; set; } = "Dark"; // "Dark" یا "Light"

        // تنظیمات انتقال فایل (برای سرعت بالا)
        public int TransferPort { get; set; } = 45055;
        public int ChunkSize { get; set; } = 65536; // 64KB برای افزایش راندمان بافرینگ
        public int MaxParallelConnections { get; set; } = 8; // شبیه‌ساز رفتار دانلود منیجر
        public bool EnableCompression { get; set; } = true; // فشرده‌سازی در حین انتقال

        // تنظیمات چت امن و شبکه
        public int ChatPort { get; set; } = 45056;
        public string ChatProtocol { get; set; } = "TLS_TCP"; // "TLS_TCP" به عنوان جایگزین مطمئن یا "gRPC"
        public bool EncryptChat { get; set; } = true;
        
        // تنظیمات اشتراک‌گذاری پیش‌فرض ویندوز (SMB)
        public bool AutoEnableWindowsSMB { get; set; } = true;
        public string DefaultWindowsShareName { get; set; } = "LANSparkShare";

        // لیست پوشه‌های اشتراک‌گذاری شده سفارشی در داخل خود اپلیکیشن
        public List<string> LocalSharedDirectories { get; set; } = new List<string>();

        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        // متد ذخیره تنظیمات روی فایل JSON
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
                // به منظور سادگی خطایابی، در بدنه اصلی لاگ خواهد شد
                Console.WriteLine($"Error saving configuration: {ex.Message}");
            }
        }

        // متد لود تنظیمات از روی فایل JSON
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
