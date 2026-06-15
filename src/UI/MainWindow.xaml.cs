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
    // کلاس کمکی برای نگه‌داری وضعیت زنده صف دانلودهای موازی
    public class DownloadJob
    {
        public string FileName { get; set; } = string.Empty;
        public int ProgressValue { get; set; }
        public string SpeedString { get; set; } = "0 KB/s";
        public string EtaString { get; set; } = "Calculating...";
    }

    public partial class MainWindow : Window
    {
        private readonly AppConfig _config;
        private readonly NetworkEngine _networkEngine;
        private readonly PeerDiscoveryService _discoveryService;
        private readonly AppShareManager _shareManager;
        private readonly SmbManager _smbManager;
        private readonly SecureChatService _chatService;
        private readonly UpdateManager _updateManager;

        public ObservableCollection<PeerDevice> OnlinePeers { get; } = new();
        public ObservableCollection<DownloadJob> ActiveDownloads { get; } = new();
        public ObservableCollection<SharedFolder> MySharedFolders { get; } = new();

        public MainWindow()
        {
            InitializeComponent();

            _config = AppConfig.Load();
            _networkEngine = new NetworkEngine(_config);
            _discoveryService = new PeerDiscoveryService(_config);
            _shareManager = new AppShareManager(_config);
            _smbManager = new SmbManager(_config);
            _chatService = new SecureChatService(_config);
            _updateManager = new UpdateManager();

            LstPeers.ItemsSource = OnlinePeers;
            LstDownloadsQueue.ItemsSource = ActiveDownloads;
            LstMyShares.ItemsSource = MySharedFolders;

            _discoveryService.OnPeersListChanged += OnPeersChanged;
            _chatService.OnMessageReceived += OnChatReceived;

            _networkEngine.StartServer();
            _discoveryService.StartDiscovery();
            _shareManager.StartServer();
            _chatService.StartChatServer();

            InitializeUiState();
        }

        private void InitializeUiState()
        {
            TxtLocalName.Text = Environment.MachineName;
            TxtLocalIp.Text = "Scanning...";
            ChkSmbStatus.IsChecked = _smbManager.IsShareActive();
            CmbLanguage.SelectedIndex = _config.Language == "fa" ? 1 : 0;
            CmbTheme.SelectedIndex = _config.AppTheme == "Light" ? 1 : 0;
            
            ApplyLanguageDictionary(_config.Language);
            ApplyThemeDictionary(_config.AppTheme);
            RefreshMySharedFoldersList();
            CheckPeersStatusAndShowWarning();
        }

        // ۱. مکانیزم جابه‌جایی داینامیک تم‌های تیره و روشن (Dark/Light Dynamic Swapping)
        private void ApplyThemeDictionary(string themeName)
        {
            try
            {
                var dictionary = new ResourceDictionary();
                string path = themeName == "Light" 
                    ? "pack://application:,,,/UI/Resources/Theme.Light.xaml" 
                    : "pack://application:,,,/UI/Resources/Theme.Dark.xaml";

                dictionary.Source = new Uri(path, UriKind.Absolute);

                // بازسازی استایل‌های منابع برنامه بدون ایجاد تداخل با دیکشنری متون چندزبانه
                var existingTheme = this.Resources.MergedDictionaries.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Theme"));
                if (existingTheme != null)
                {
                    this.Resources.MergedDictionaries.Remove(existingTheme);
                }
                this.Resources.MergedDictionaries.Add(dictionary);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Theme loading error: {ex.Message}");
            }
        }

        private void ApplyLanguageDictionary(string langCode)
        {
            try
            {
                var dictionary = new ResourceDictionary();
                string path = langCode == "fa" 
                    ? "pack://application:,,,/UI/Resources/Strings.fa.xaml" 
                    : "pack://application:,,,/UI/Resources/Strings.en.xaml";

                dictionary.Source = new Uri(path, UriKind.Absolute);

                var existingLang = this.Resources.MergedDictionaries.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Strings"));
                if (existingLang != null)
                {
                    this.Resources.MergedDictionaries.Remove(existingLang);
                }
                this.Resources.MergedDictionaries.Add(dictionary);

                this.FlowDirection = langCode == "fa" 
                    ? System.Windows.FlowDirection.RightToLeft 
                    : System.Windows.FlowDirection.LeftToRight;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Language loading error: {ex.Message}");
            }
        }

        // ۲. پایش هوشمند کلاینت‌ها و فعال‌سازی کادر هشدار عدم اتصال شبکه محلی
        private void OnPeersChanged()
        {
            Dispatcher.Invoke(() =>
            {
                OnlinePeers.Clear();
                CmbChatTarget.Items.Clear();
                CmbChatTarget.Items.Add("Group Chat");
                CmbChatTarget.SelectedIndex = 0;

                foreach (var peer in _discoveryService.DiscoveredPeers.Values)
                {
                    OnlinePeers.Add(peer);
                    CmbChatTarget.Items.Add(peer.CustomName);
                }

                CheckPeersStatusAndShowWarning();
            });
        }

        private void CheckPeersStatusAndShowWarning()
        {
            if (OnlinePeers.Count == 0)
            {
                BrdNoNetworkWarn.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                BrdNoNetworkWarn.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void BtnCopyPeerIp_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            string? ip = button?.CommandParameter as string;
            if (!string.IsNullOrEmpty(ip))
            {
                System.Windows.Clipboard.SetText(ip);
                System.Windows.MessageBox.Show((string)this.FindResource("StatusCopied"), "LANSpark", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ۳. مدیریت اشتراک‌گذاری‌های محلی کلاینت
        private void RefreshMySharedFoldersList()
        {
            MySharedFolders.Clear();
            foreach (var folder in _config.SharedFolders)
            {
                MySharedFolders.Add(folder);
            }
        }

        private void BtnAddShare_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    bool isPublic = CmbShareType.SelectedIndex == 0;
                    var newFolder = new SharedFolder
                    {
                        FolderPath = dialog.SelectedPath,
                        IsPublic = isPublic
                    };

                    _config.SharedFolders.Add(newFolder);
                    _config.Save();
                    RefreshMySharedFoldersList();
                    System.Windows.MessageBox.Show("Folder Shared Successfully!", "LANSpark", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        // ۴. بخش چت پیشرفته رمزنگاری شده با تراز خودکار فارسی/انگلیسی (Auto RTL)
        private async void BtnSendChat_Click(object sender, RoutedEventArgs e)
        {
            string message = TxtMessageInput.Text;
            if (string.IsNullOrWhiteSpace(message)) return;

            TxtMessageInput.Clear();
            AppendChatMessage("Me", message, System.Windows.Media.Brushes.LightSkyBlue, true);

            // بررسی حالت خصوصی یا گروهی ارسال پیام چت
            if (CmbChatTarget.SelectedIndex == 0) // ارسال به گروه (برودکست)
            {
                foreach (var peer in OnlinePeers)
                {
                    await _chatService.SendMessageSecurelyAsync(peer.IpAddress, message);
                }
            }
            else // ارسال خصوصی به کلاینت منتخب
            {
                string targetName = CmbChatTarget.SelectedItem.ToString() ?? "";
                var peer = OnlinePeers.FirstOrDefault(p => p.CustomName == targetName);
                if (peer != null)
                {
                    await _chatService.SendMessageSecurelyAsync(peer.IpAddress, message);
                }
            }
        }

        private void OnChatReceived(ChatMessage message)
        {
            Dispatcher.Invoke(() =>
            {
                AppendChatMessage(message.SenderName, message.MessageText, System.Windows.Media.Brushes.LightGreen, false);
            });
        }

        // متد هوشمند تشخیص جهت و تراز متن چت بر پایه یونیکد کلمات غالب جمله
        private void AppendChatMessage(string sender, string text, System.Windows.Media.Brush color, bool isMe)
        {
            Paragraph para = new Paragraph();

            // تشخیص وجود کاراکترهای پارسی در بدنه متن پیام چت
            bool isFarsi = IsMostlyFarsi(text);
            para.TextAlignment = isFarsi ? System.Windows.TextAlignment.Right : System.Windows.TextAlignment.Left;

            Run nameRun = new Run($"[{sender}] {DateTime.Now.ToShortTimeString()}: ") { Foreground = color, FontWeight = FontWeights.Bold };
            Run textRun = new Run(text) { Foreground = System.Windows.Media.Brushes.White };
            
            para.Inlines.Add(nameRun);
            para.Inlines.Add(textRun);
            RtbChatLog.Document.Blocks.Add(para);
            RtbChatLog.ScrollToEnd();
        }

        private bool IsMostlyFarsi(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            // بررسی رنج کاراکترهای حروف الفبای فارسی و عربی
            int farsiCount = text.Count(c => (c >= 0x0600 && c <= 0x06FF) || (c >= 0xFB50 && c <= 0xFDFF));
            return farsiCount > (text.Length / 2);
        }

        // ۵. تنظیمات زبان و تغییر پوسته داینامیک برنامه
        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_config == null) return;
            string newLang = CmbLanguage.SelectedIndex == 1 ? "fa" : "en";
            _config.Language = newLang;
            _config.Save();
            ApplyLanguageDictionary(newLang);
        }

        private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_config == null) return;
            string newTheme = CmbTheme.SelectedIndex == 1 ? "Light" : "Dark";
            _config.AppTheme = newTheme;
            _config.Save();
            ApplyThemeDictionary(newTheme);
        }

        private void ChkSmb_Checked(object sender, RoutedEventArgs e)
        {
            if (!_smbManager.IsUserAdministrator())
            {
                System.Windows.MessageBox.Show("Administrator privileges are required to configure Windows Sharing.", "LANSpark", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show((string)this.FindResource("MsgCheckingUpdates"), "LANSpark");
            var (isNewAvailable, latestVersion, downloadUrl) = await _updateManager.CheckForUpdatesAsync();

            if (isNewAvailable)
            {
                var result = System.Windows.MessageBox.Show((string)this.FindResource("MsgNewVersionFound") + $" ({latestVersion})", "LANSpark", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    await _updateManager.DownloadAndInstallUpdateAsync(downloadUrl);
                }
            }
            else
            {
                System.Windows.MessageBox.Show((string)this.FindResource("MsgNoUpdate"), "LANSpark", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

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
