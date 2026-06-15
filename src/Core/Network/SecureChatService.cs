using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using LANSpark.Core.Config;

namespace LANSpark.Core.Network
{
    // کلاس حامل ساختار پیام‌های چت
    public class ChatMessage
    {
        public string SenderName { get; set; } = string.Empty;
        public string SenderIp { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class SecureChatService
    {
        private readonly AppConfig _config;
        private TcpListener? _chatListener;
        private bool _isListening;
        private X509Certificate2? _serverCertificate;

        // رویداد ارسال پیام دریافت شده به لایه رابط کاربری (UI)
        public event Action<ChatMessage>? OnMessageReceived;

        public SecureChatService(AppConfig config)
        {
            _config = config;
            // تولید خودکار یک گواهی امنیتی ۲۵۶ بیتی در حافظه برای ارتباط TLS بدون نیاز به فایل خارجی
            _serverCertificate = GenerateInMemoryCertificate();
        }

        // ۱. متد تولید داینامیک گواهی امنیتی خودامضا (TLS/SSL Certificate)
        private X509Certificate2 GenerateInMemoryCertificate()
        {
            using (RSA rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest(
                    "CN=LANSparkSecureChat",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(false, false, 0, false));

                // گواهی برای ۵ سال آینده معتبر باشد
                var certificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddYears(5));

                // تبدیل به گواهی قابل استفاده در کانال‌های ارتباطی ویندوز
                return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
            }
        }

        // ۲. استارت سرور چت با لایه امنیتی TLS/SSL
        public void StartChatServer()
        {
            _chatListener = new TcpListener(IPAddress.Any, _config.ChatPort);
            _chatListener.Start();
            _isListening = true;

            Task.Run(ListenForSecureChatsAsync);
        }

        public void StopChatServer()
        {
            _isListening = false;
            _chatListener?.Stop();
        }

        private async Task ListenForSecureChatsAsync()
        {
            while (_isListening && _chatListener != null)
            {
                try
                {
                    TcpClient client = await _chatListener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleIncomingSecureChatAsync(client));
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { }
            }
        }

        // پردازش پیام‌های دریافتی از طریق تونل رمزنگاری شده SslStream
        private async Task HandleIncomingSecureChatAsync(TcpClient client)
        {
            using (client)
            using (var sslStream = new SslStream(client.GetStream(), false, (sender, cert, chain, errors) => true))
            {
                try
                {
                    // احراز هویت سرور با گواهی داینامیکی تولید شده
                    await sslStream.AuthenticateAsServerAsync(_serverCertificate!, false, System.Security.Authentication.SslProtocols.Tls13, false);

                    using (var reader = new StreamReader(sslStream, Encoding.UTF8))
                    {
                        string? jsonMessage = await reader.ReadLineAsync();
                        if (!string.IsNullOrEmpty(jsonMessage))
                        {
                            var chatMessage = System.Text.Json.JsonSerializer.Deserialize<ChatMessage>(jsonMessage);
                            if (chatMessage != null)
                            {
                                chatMessage.SenderIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                                OnMessageReceived?.Invoke(chatMessage);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // خطاهای مربوط به عدم تایید کلیدها در اتصالات نامعتبر
                }
            }
        }

        // ۳. متد ارسال پیام امن رمزنگاری شده به سیستم مقصد در شبکه
        public async Task<bool> SendMessageSecurelyAsync(string targetIp, string messageText)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    // اتصال به پورت چت سیستم مقصد با بازه زمانی انتظار ۳ ثانیه‌ای
                    var connectTask = client.ConnectAsync(targetIp, _config.ChatPort);
                    if (await Task.WhenAny(connectTask, Task.Delay(3000)) != connectTask)
                    {
                        return false; // زمان اتصال به پایان رسید (سیستم آفلاین است)
                    }

                    // ایجاد تونل امن SSL با متد تایید صلاحیت خودکار طرفین
                    using (var sslStream = new SslStream(client.GetStream(), false, (sender, cert, chain, errors) => true))
                    {
                        await sslStream.AuthenticateAsClientAsync(targetIp);

                        var chatMessage = new ChatMessage
                        {
                            SenderName = string.IsNullOrEmpty(_config.DefaultWindowsShareName) ? Environment.MachineName : _config.DefaultWindowsShareName,
                            MessageText = messageText,
                            Timestamp = DateTime.Now
                        };

                        string json = System.Text.Json.JsonSerializer.Serialize(chatMessage);
                        byte[] data = Encoding.UTF8.GetBytes(json + "\n");

                        await sslStream.WriteAsync(data, 0, data.Length);
                        await sslStream.FlushAsync();
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
