using System;
using System.Windows;

namespace LANSpark
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    // استفاده صریح از فضای نام سیستم راه‌انداز WPF برای برطرف کردن ابهام نهایی کامپایلر
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // کدهای آماده‌سازی اولیه در صورت نیاز در اینجا اجرا می‌شوند
        }
    }
}
