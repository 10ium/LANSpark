using System;
using System.IO;
using System.Diagnostics;
using System.Security.Principal;
using LANSpark.Core.Config;

namespace LANSpark.Core.Sharing
{
    public class SmbManager
    {
        private readonly AppConfig _config;
        private readonly string _defaultFolderPath;
        private readonly string _shareName;

        public SmbManager(AppConfig config)
        {
            _config = config;
            _shareName = _config.DefaultWindowsShareName;
            // مسیر ایجاد پوشه اشتراک‌گذاری پیش‌فرض سیستم
            _defaultFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "LANSparkShare");
        }

        // ۱. بررسی اینکه آیا کاربر دسترسی Administrator دارد یا خیر
        public bool IsUserAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        // ۲. فعال‌سازی خودکار پوشه اشتراک‌گذاری ویندوز
        public bool EnableDefaultShare()
        {
            try
            {
                if (!Directory.Exists(_defaultFolderPath))
                {
                    Directory.CreateDirectory(_defaultFolderPath);
                }

                if (!IsUserAdministrator())
                {
                    // برای ویرایش شبکه ویندوز نیاز به ادمین است
                    return false;
                }

                // حذف اشتراک‌گذاری قدیمی در صورت وجود برای جلوگیری از تداخل
                DisableDefaultShare();

                // دستور به اشتراک‌گذاری پوشه در ویندوز برای همه کاربران شبکه به صورت خواندن/نوشتن
                string arguments = $"share {_shareName}=\"{_defaultFolderPath}\" /grant:everyone,full";
                return ExecuteNetCommand(arguments);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enabling SMB share: {ex.Message}");
                return false;
            }
        }

        // ۳. غیرفعال‌سازی پوشه اشتراک‌گذاری ویندوز
        public bool DisableDefaultShare()
        {
            try
            {
                if (!IsUserAdministrator()) return false;

                string arguments = $"share {_shareName} /delete";
                return ExecuteNetCommand(arguments);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disabling SMB share: {ex.Message}");
                return false;
            }
        }

        // ۴. بررسی وضعیت زنده بودن اشتراک‌گذاری ویندوزی پوشه
        public bool IsShareActive()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "net",
                    Arguments = "share",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        return output.Contains(_shareName, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch { }
            return false;
        }

        // اجرای دستورات بخش net share سیستم‌عامل به صورت امن و بدون باز شدن پنجره سیاه CMD
        private bool ExecuteNetCommand(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "net",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas" // درخواست ارتقا سطح دسترسی
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit();
                        return process.ExitCode == 0;
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
