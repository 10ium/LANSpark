using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using LANSpark.Core.Config;

namespace LANSpark.Core.Network
{
    public class TransferSession
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public string FilePath { get; set; } = string.Empty;
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public bool IsPaused { get; set; }
        public CancellationTokenSource Cts { get; set; } = new CancellationTokenSource();
    }

    public class NetworkEngine
    {
        private readonly AppConfig _config;
        private TcpListener? _listener;
        private bool _isRunning;
        private readonly ConcurrentDictionary<string, TransferSession> _activeSessions = new();

        public NetworkEngine(AppConfig config)
        {
            _config = config;
        }

        // ۱. استارت سرور برای پذیرش اتصالات همزمان کاربران مختلف
        public void StartServer()
        {
            _listener = new TcpListener(IPAddress.Any, _config.TransferPort);
            _listener.Start();
            _isRunning = true;
            
            Task.Run(ListenForClientsAsync);
        }

        public void StopServer()
        {
            _isRunning = false;
            _listener?.Stop();
            foreach (var session in _activeSessions.Values)
            {
                session.Cts.Cancel();
            }
            _activeSessions.Clear();
        }

        private async Task ListenForClientsAsync()
        {
            while (_isRunning && _listener != null)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    // پردازش هر کلاینت در یک ترد مستقل برای پشتیبانی از دانلود همزمان چند سیستم
                    _ = Task.Run(() => HandleClientDownloadAsync(client));
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception)
                {
                    // مدیریت خطاها در صورت قطع اتصال شبکه
                }
            }
        }

        // ۲. ارسال فایل با قابلیت تقسیم‌بندی، مکث و لغو برای چند کلاینت همزمان
        private async Task HandleClientDownloadAsync(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            using (var reader = new BinaryReader(stream))
            using (var writer = new BinaryWriter(stream))
            {
                try
                {
                    // خواندن مسیر فایل درخواستی کلاینت و موقعیت بایت شروع (Offset) برای قابلیت Resume
                    string requestedFilePath = reader.ReadString();
                    long startOffset = reader.ReadInt64();

                    if (!File.Exists(requestedFilePath))
                    {
                        writer.Write(false); // فایل یافت نشد
                        return;
                    }

                    writer.Write(true); // فایل تایید شد
                    long fileLength = new FileInfo(requestedFilePath).Length;
                    writer.Write(fileLength);

                    var session = new TransferSession
                    {
                        FilePath = requestedFilePath,
                        TotalBytes = fileLength,
                        BytesTransferred = startOffset
                    };

                    _activeSessions.TryAdd(session.SessionId, session);

                    using (var fileStream = new FileStream(requestedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        fileStream.Seek(startOffset, SeekOrigin.Begin);
                        byte[] buffer = new byte[_config.ChunkSize];
                        
                        while (session.BytesTransferred < fileLength)
                        {
                            if (session.Cts.Token.IsCancellationRequested)
                                break;

                            // بررسی وضعیت توقف موقتی (Pause)
                            while (session.IsPaused)
                            {
                                await Task.Delay(500, session.Cts.Token);
                            }

                            int bytesToRead = (int)Math.Min(buffer.Length, fileLength - session.BytesTransferred);
                            int read = await fileStream.ReadAsync(buffer, 0, bytesToRead, session.Cts.Token);
                            
                            if (read == 0) break;

                            await stream.WriteAsync(buffer, 0, read, session.Cts.Token);
                            session.BytesTransferred += read;

                            // ارسال میزان پیشرفت (اختیاری برای کلاینت)
                        }
                    }

                    _activeSessions.TryRemove(session.SessionId, out _);
                }
                catch (Exception)
                {
                    // قطع ناگهانی ارتباط کلاینت به خوبی مدیریت می‌شود
                }
            }
        }

        // ۳. متدهای کنترل پنل برای توقف و لغو دستی انتقال‌ها
        public void PauseSession(string sessionId)
        {
            if (_activeSessions.TryGetValue(sessionId, out var session))
            {
                session.IsPaused = true;
            }
        }

        public void ResumeSession(string sessionId)
        {
            if (_activeSessions.TryGetValue(sessionId, out var session))
            {
                session.IsPaused = false;
            }
        }

        public void CancelSession(string sessionId)
        {
            if (_activeSessions.TryRemove(sessionId, out var session))
            {
                session.Cts.Cancel();
            }
        }
    }
}
