using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using LANSpark.Core.Config;

namespace LANSpark.Core.Network
{
    // کلاس مدل برای ذخیره اطلاعات سیستم‌های کشف شده در شبکه
    public class PeerDevice
    {
        public string MachineId { get; set; } = string.Empty;
        public string CustomName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public DateTime LastSeen { get; set; }
        public string OsPlatform { get; set; } = "Windows";
    }

    public class PeerDiscoveryService
    {
        private readonly AppConfig _config;
        private readonly int _discoveryPort = 45057; // پورت اختصاصی برای بوق حضور (Heartbeat)
        private UdpClient? _udpListener;
        private UdpClient? _udpSender;
        private bool _isSearching;
        private readonly string _localMachineId;
        private string _customLocalName;

        // لیست پویا و امن از تمامی کامپیوترهای آنلاین متصل در شبکه
        public ConcurrentDictionary<string, PeerDevice> DiscoveredPeers { get; } = new();

        public event Action? OnPeersListChanged;

        public PeerDiscoveryService(AppConfig config)
        {
            _config = config;
            _localMachineId = Environment.MachineName + "_" + Environment.UserName;
            // اگر نام سفارشی تنظیم نشده بود، نام پیش‌فرض سیستم استفاده می‌شود
            _customLocalName = string.IsNullOrEmpty(_config.DefaultWindowsShareName) 
                ? Environment.MachineName 
                : _config.DefaultWindowsShareName;
        }

        // تغییر نام نمایشی سیستم در کل شبکه
        public void UpdateLocalComputerName(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            _customLocalName = newName;
            _config.DefaultWindowsShareName = newName;
            _config.Save(); // ذخیره‌سازی دائمی در تنظیمات
            
            // ارسال سریع اطلاعات جدید به شبکه
            _ = BroadcastPresenceAsync();
        }

        // شروع کارکرد سرویس شناسایی دوطرفه
        public void StartDiscovery()
        {
            _isSearching = true;
            _udpListener = new UdpClient(_discoveryPort);
            _udpSender = new UdpClient();
            _udpSender.EnableBroadcast = true;

            // آغاز گوش دادن به بوق‌های حضور سیستم‌های دیگر
            Task.Run(ListenForPeersAsync);

            // آغاز ارسال بوق حضور خود به شبکه به صورت دوره‌ای (هر ۳ ثانیه)
            Task.Run(BroadcastLoopAsync);

            // پاک‌سازی خودکار سیستم‌هایی که آفلاین شده‌اند (هر ۵ ثانیه)
            Task.Run(CleanOfflinePeersLoopAsync);
        }

        public void StopDiscovery()
        {
            _isSearching = false;
            _udpListener?.Close();
            _udpSender?.Close();
        }

        private async Task BroadcastLoopAsync()
        {
            while (_isSearching)
            {
                await BroadcastPresenceAsync();
                await Task.Delay(3000); // ارسال پالس حضور هر ۳ ثانیه یکبار
            }
        }

        private async Task BroadcastPresenceAsync()
        {
            try
            {
                var localIp = GetLocalIpAddress();
                var peerInfo = new PeerDevice
                {
                    MachineId = _localMachineId,
                    CustomName = _customLocalName,
                    IpAddress = localIp,
                    LastSeen = DateTime.UtcNow
                };

                string json = JsonSerializer.Serialize(peerInfo);
                byte[] data = Encoding.UTF8.GetBytes(json);
                
                // ارسال پیام به کل گستره IPهای متصل به LAN
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
                await _udpSender!.SendAsync(data, data.Length, endPoint);
            }
            catch
            {
                // مدیریت خطاهای احتمالی کارت شبکه مجازی یا مسدود بودن پورت توسط فایروال
            }
        }

        private async Task ListenForPeersAsync()
        {
            while (_isSearching && _udpListener != null)
            {
                try
                {
                    UdpReceiveResult result = await _udpListener.ReceiveAsync();
                    string json = Encoding.UTF8.GetString(result.Buffer);
                    var peer = JsonSerializer.Deserialize<PeerDevice>(json);

                    if (peer != null && peer.MachineId != _localMachineId)
                    {
                        peer.LastSeen = DateTime.UtcNow;
                        
                        // اضافه یا به‌روزرسانی سیستم کشف شده در لیست متصل‌ها
                        DiscoveredPeers.AddOrUpdate(peer.MachineId, peer, (key, existing) => 
                        {
                            existing.CustomName = peer.CustomName;
                            existing.IpAddress = peer.IpAddress;
                            existing.LastSeen = peer.LastSeen;
                            return existing;
                        });

                        OnPeersListChanged?.Invoke();
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch { /* خطای پکت‌های نامعتبر نادیده گرفته می‌شود */ }
            }
        }

        private async Task CleanOfflinePeersLoopAsync()
        {
            while (_isSearching)
            {
                await Task.Delay(5000);
                var now = DateTime.UtcNow;
                bool changed = false;

                foreach (var kvp in DiscoveredPeers)
                {
                    // اگر سیستمی بیش از ۸ ثانیه پالس حضور ارسال نکرد، آفلاین تلقی می‌شود
                    if ((now - kvp.Value.LastSeen).TotalSeconds > 8)
                    {
                        if (DiscoveredPeers.TryRemove(kvp.Key, out _))
                        {
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    OnPeersListChanged?.Invoke();
                }
            }
        }

        private string GetLocalIpAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !ip.ToString().StartsWith("127."))
                {
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        }
    }
}
