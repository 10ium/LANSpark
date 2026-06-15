using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using LANSpark.Core.Config;

namespace LANSpark.Core.Sharing
{
    public class SharedItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
    }

    public class AppShareManager
    {
        private readonly AppConfig _config;
        private HttpListener? _httpListener;
        private bool _isRunning;
        private readonly int _serverPort = 45058; // پورت اختصاصی سرور فایل داخلی

        public AppShareManager(AppConfig config)
        {
            _config = config;
        }

        // شروع کارکرد وب‌سرور داخلی اشتراک‌گذاری
        public void StartServer()
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://*:{_serverPort}/");
            _httpListener.Start();
            _isRunning = true;

            Task.Run(ListenForRequestsAsync);
        }

        public void StopServer()
        {
            _isRunning = false;
            _httpListener?.Stop();
        }

        private async Task ListenForRequestsAsync()
        {
            while (_isRunning && _httpListener != null)
            {
                try
                {
                    HttpListenerContext context = await _httpListener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequestAsync(context));
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception)
                {
                    // خطاهای مربوط به پورت‌های مسدود شده یا دسترسی‌ها
                }
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            // تنظیم هدر برای پاسخ به همه دامنه‌ها (CORS) جهت وب‌گردی آسان
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

            try
            {
                string path = request.Url?.AbsolutePath.ToLower() ?? "";

                if (path == "/api/list")
                {
                    await HandleListRequestAsync(response);
                }
                else if (path == "/api/search")
                {
                    string query = request.QueryString["q"] ?? "";
                    await HandleSearchRequestAsync(response, query);
                }
                else if (path == "/download")
                {
                    string filePath = request.QueryString["file"] ?? "";
                    await HandleDownloadRequestAsync(request, response, filePath);
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.Close();
                }
            }
            catch (Exception)
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.Close();
            }
        }

        // ۱. متد ارائه‌دهنده لیست فایل‌ها و پوشه‌های اشتراک گذاشته شده
        private async Task HandleListRequestAsync(HttpListenerResponse response)
        {
            var items = new List<SharedItem>();

            foreach (var dir in _config.LocalSharedDirectories)
            {
                if (Directory.Exists(dir))
                {
                    items.Add(new SharedItem { Name = Path.GetFileName(dir), Path = dir, IsDirectory = true });
                }
            }

            response.ContentType = "application/json";
            byte[] buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items));
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }

        // ۲. متد جستجوی پیشرفته درون فایل‌های اشتراک‌گذاری شده
        private async Task HandleSearchRequestAsync(HttpListenerResponse response, string query)
        {
            var results = new List<SharedItem>();

            if (!string.IsNullOrWhiteSpace(query))
            {
                foreach (var dir in _config.LocalSharedDirectories)
                {
                    if (Directory.Exists(dir))
                    {
                        var files = Directory.GetFiles(dir, $"*{query}*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            var info = new FileInfo(file);
                            results.Add(new SharedItem 
                            { 
                                Name = info.Name, 
                                Path = file, 
                                IsDirectory = false, 
                                Size = info.Length 
                            });
                        }
                    }
                }
            }

            response.ContentType = "application/json";
            byte[] buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(results));
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }

        // ۳. متد دانلود فوق‌سریع با پشتیبانی از هدرهای Range (دانلود چند بخش همزمان شبیه IDM)
        private async Task HandleDownloadRequestAsync(HttpListenerRequest request, HttpListenerResponse response, string filePath)
        {
            // اطمینان از قرار داشتن فایل در پوشه‌های مجاز اشتراک‌گذاری شده به دلایل امنیتی
            bool isAllowed = _config.LocalSharedDirectories.Any(dir => filePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
            if (!isAllowed || !File.Exists(filePath))
            {
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                response.Close();
                return;
            }

            var fileInfo = new FileInfo(filePath);
            long fileLength = fileInfo.Length;

            response.Headers.Add("Accept-Ranges", "bytes");
            response.ContentType = "application/octet-stream";
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"{Uri.EscapeDataString(fileInfo.Name)}\"");

            long startByte = 0;
            long endByte = fileLength - 1;

            // پردازش درخواست‌های Range جهت دانلود قطعه به قطعه
            string? rangeHeader = request.Headers["Range"];
            if (!string.IsNullOrEmpty(rangeHeader))
            {
                string range = rangeHeader.Replace("bytes=", "");
                string[] parts = range.Split('-');
                startByte = long.Parse(parts[0]);
                if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                {
                    endByte = long.Parse(parts[1]);
                }
                
                response.StatusCode = (int)HttpStatusCode.PartialContent;
                response.Headers.Add("Content-Range", $"bytes {startByte}-{endByte}/{fileLength}");
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.OK;
            }

            long contentLength = endByte - startByte + 1;
            response.ContentLength64 = contentLength;

            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fileStream.Seek(startByte, SeekOrigin.Begin);
                    byte[] buffer = new byte[65536]; // بافر بزرگ ۶۴ کیلوبایتی جهت بهره‌وری دیسک و پردازنده
                    long bytesRemaining = contentLength;

                    while (bytesRemaining > 0)
                    {
                        int bytesToRead = (int)Math.Min(buffer.Length, bytesRemaining);
                        int bytesRead = await fileStream.ReadAsync(buffer, 0, bytesToRead);
                        if (bytesRead == 0) break;

                        await response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                        bytesRemaining -= bytesRead;
                    }
                }
            }
            catch
            {
                // لغو ناگهانی دانلود توسط کلاینت به سادگی مدیریت می‌شود
            }
            finally
            {
                response.Close();
            }
        }
    }
}
