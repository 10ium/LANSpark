using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using LANSpark.Core.Config;
using LANSpark.Core.Network;
using LANSpark.Core.Sharing;

namespace LANSpark.UI
{
    public partial class MainWindow : Window
    {
        private readonly AppConfig _config;
        private readonly NetworkEngine _networkEngine;
        private readonly PeerDiscoveryService _discoveryService;
        private readonly AppShareManager _shareManager;
        private readonly SmbManager _smbManager;
        private readonly SecureChatService _chatService;
        private readonly UpdateManager _updateManager;

        // لیست پویا با قابلیت به‌روزرسانی لحظه‌ای رابط کاربری
        public ObservableCollection<PeerDevice> OnlinePeers { get; } = new();

        public MainWindow()
        {
            InitializeComponent();

            // ۱. راه‌اندازی و لود زیرسیستم‌ها
            _config = AppConfig.Load();
            _networkEngine = new NetworkEngine(_config);
            _discoveryService = new PeerDiscoveryService(_config);
            _shareManager = new AppShareManager(_config);
            _smbManager = new SmbManager(_config);
            _chatService = new SecureChatService(_config);
            _updateManager = new UpdateManager();

            // انتساب دیتای آنلاین کلاینت‌ها به لیست باکس روی فرم
            LstPeers.ItemsSource = OnlinePeers;

            // ۲. ثبت رویدادهای شبکه و چت
            _discoveryService.OnPeersListChanged += OnPeersChanged;
            _chatService.OnMessageReceived += OnChatReceived;

            // ۳. آغاز به کار سرورها
            _networkEngine.StartServer();
            _discoveryService.StartDiscovery();
            _shareManager.StartServer();
            _chatService.StartChatServer();

            // ۴. لود تنظیمات اولیه بر روی فرم گرافیکی
            InitializeUiState();
        }

        private void InitializeUiState()
        {
            TxtLocalName.Text = string.IsNullOrEmpty(_config.DefaultWindowsShareName) ? Environment.MachineName : _config.DefaultWindowsShareName;
            ChkSmbStatus.IsChecked = _smbManager.IsShareActive();
            CmbLanguage.SelectedIndex = _config.Language == "fa" ? 1 : 0;
            ApplyLanguageDictionary(_config.Language);
        }

        // ۵. مکانیزم جابه‌جایی داینامیک زبان‌ها در زمان اجرا (Run-time Translation)
        private void ApplyLanguageDictionary(string langCode)
        {
            try
            {
                var dictionary = new ResourceDictionary();
                string path = langCode == "fa" 
                    ? "pack://application:,,,/UI/Resources/Strings.fa.xaml" 
                    : "pack://application:,,,/UI/Resources/Strings.en.xaml";

                dictionary.Source = new Uri(path, UriKind.Absolute);

                // جایگزینی فرهنگ لغات جدید در منابع جاری برنامه
                this.Resources.MergedDictionaries.Clear();
                this.Resources.MergedDictionaries.Add(dictionary);

                // تغییر راست‌چین یا چپ‌چین بودن کل پنجره
                this.FlowDirection = langCode == "fa" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Language loading error: {ex.Message}");
            }
        }

        // ۶. دریافت رویداد تغییرات سیستم‌های شبکه و به‌روزرسانی زنده لیست
        private void OnPeersChanged()
        {
            Dispatcher.Invoke(() =>
            {
                OnlinePeers.Clear();
                foreach (var peer in _discoveryService.DiscoveredPeers.Values)
                {
                    OnlinePeers.Add(peer);
                }
            });
        }

        // ۷. کپی آدرس آی‌پی سیستم‌ها به حافظه موقت (Clipboard)
        private void BtnCopyIp_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TxtLocalName.Text);
            MessageBox.Show((string)this.FindResource("StatusCopied"), "LANSpark", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnCopyPeerIp_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            string? ip = button?.CommandParameter as string;
            if (!string.IsNullOrEmpty(ip))
            {
                Clipboard.SetText(ip);
                MessageBox.Show((string)this.FindResource("StatusCopied"), "LANSpark", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ۸. مدیریت اشتراک‌گذاری پوشه محلی سفارشی جدید
        private void BtnAddShare_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string path = dialog.SelectedPath;
                    if (!_config.LocalSharedDirectories.Contains(path))
                    {
                        _config.LocalSharedDirectories.Add(path);
                        _config.Save();
                        MessageBox.Show("Folder shared successfully within the app!", "LANSpark", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        // ۹. بخش پیام‌رسانی امن (ارسال و دریافت رمزنگاری شده)
        private async void BtnSendChat_Click(object sender, RoutedEventArgs e)
        {
            string message = TxtMessageInput.Text;
            if (string.IsNullOrWhiteSpace(message)) return;

            var selectedPeer = LstPeers.SelectedItem as PeerDevice;
            if (selectedPeer == null)
            {
                MessageBox.Show("Please select a target computer from the list first.", "LANSpark", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TxtMessageInput.Clear();
            AppendChatMessage("Me", message, System.Windows.Media.Brushes.LightSkyBlue);

            bool success = await _chatService.SendMessageSecurelyAsync(selectedPeer.IpAddress, message);
            if (!success)
            {
                AppendChatMessage("System", "Failed to send message. Peer might be offline.", System.Windows.Media.Brushes.Red);
            }
        }

        private void OnChatReceived(ChatMessage message)
        {
            Dispatcher.Invoke(() =>
            {
                AppendChatMessage(message.SenderName, message.MessageText, System.Windows.Media.Brushes.LightGreen);
            });
        }

        // رفع خطای تداخل قلم‌موها با استفاده از آدرس صریح سیستم ترسیم گرافیک WPF
        private void AppendChatMessage(string sender, string text, System.Windows.Media.Brush color)
        {
            Paragraph para = new Paragraph();
            Run nameRun = new Run($"[{sender}] {DateTime.Now.ToShortTimeString()}: ") { Foreground = color, FontWeight = FontWeights.Bold };
            Run textRun = new Run(text) { Foreground = System.Windows.Media.Brushes.White };
            para.Inlines.Add(nameRun);
            para.Inlines.Add(textRun);
            RtbChatLog.Document.Blocks.Add(para);
            RtbChatLog.ScrollToEnd();
        }

        // ۱۰. مدیریت رویداد تغییرات زنده زبان
        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_config == null) return;
            string newLang = CmbLanguage.SelectedIndex == 1 ? "fa" : "en";
            _config.Language = newLang;
            _config.Save();
            ApplyLanguageDictionary(newLang);
        }

        // ۱۱. مدیریت اشتراک‌گذاری پیش‌فرض ویندوز SMB
        private void ChkSmb_Checked(object sender, RoutedEventArgs e)
        {
            if (!_smbManager.IsUserAdministrator())
            {
                MessageBox.Show("Administrator privileges are required to configure Windows Sharing.", "LANSpark", MessageBoxButton.OK, MessageBoxImage.Warning);
                ChkSmbStatus.IsChecked = false;
                return;
            }
            _smbManager.EnableDefaultShare();
        }

        private void ChkSmb_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_smbManager.IsUserAdministrator())
            {
                _smbManager.DisableDefaultShare();
            }
        }

        // ۱۲. موتور بررسی آپدیت خودکار
        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show((string)this.FindResource("MsgCheckingUpdates"), "LANSpark");
            var (isNewAvailable, latestVersion, downloadUrl) = await _updateManager.CheckForUpdatesAsync();

            if (isNewAvailable)
            {
                var result = MessageBox.Show((string)this.FindResource("MsgNewVersionFound") + $" ({latestVersion})", "LANSpark", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    await _updateManager.DownloadAndInstallUpdateAsync(downloadUrl);
                }
            }
            else
            {
                MessageBox.Show((string)this.FindResource("MsgNoUpdate"), "LANSpark", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ۱۳. آزاد کردن منابع در هنگام بسته شدن اپلیکیشن
        protected override void OnClosed(EventArgs e)
        {
            _networkEngine.StopServer();
            _discoveryService.StopDiscovery();
            _shareManager.StopServer();
            _chatService.StopChatServer();
            base.OnClosed(e);
        }
    }
}
