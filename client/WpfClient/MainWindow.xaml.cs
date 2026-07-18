using NAudio.Wave;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CJVoiceClient;

public partial class MainWindow : Window
{
    private AudioReceiver? _receiver;
    private readonly System.Timers.Timer _uiTimer;
    private ReceiverState _lastState = ReceiverState.Disconnected;
    private int _currentBytesPerSec;

    public MainWindow()
    {
        InitializeComponent();

        // 更新设备描述信息
        ShowOutputDevice();

        Closed += (_, _) =>
        {
            _uiTimer.Stop();
            _uiTimer.Dispose();
            _receiver?.Dispose();
        };

        // 定期更新 UI
        _uiTimer = new System.Timers.Timer(500);
        _uiTimer.Elapsed += (_, _) => UpdateTrafficDisplay();
        _uiTimer.Start();
    }

    private void ShowOutputDevice()
    {
        var deviceNumber = AudioReceiver.FindVBCableDevice();
        if (deviceNumber >= 0)
        {
            var caps = WaveOut.GetCapabilities(deviceNumber);
            this.Dispatcher.Invoke(() =>
            {
                OutputDeviceText.Text = caps.ProductName;
            });
        }
        else
        {
            this.Dispatcher.Invoke(() =>
            {
                OutputDeviceText.Text = "未检测到 VB-CABLE，将使用默认设备。请安装 VB-CABLE 以使用虚拟麦克风功能。";
                OutputDeviceText.Foreground = Brushes.DarkOrange;
            });
        }
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var ip = IpAddressBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ip))
        {
            StatusText.Text = "请输入有效的 IP 地址";
            return;
        }

        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;

        _receiver = new AudioReceiver();
        _receiver.StateChanged += OnStateChanged;
        _receiver.BytesReceived += OnBytesReceived;

        _receiver.Start(ip);
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_receiver != null)
        {
            _receiver.StateChanged -= OnStateChanged;
            _receiver.BytesReceived -= OnBytesReceived;
            _receiver.Dispose();
            _receiver = null;
        }
        var oldState = _lastState;
        _lastState = ReceiverState.Disconnected;
        if (oldState != ReceiverState.Disconnected) // avoid double
        {
            UpdateUIToSync();
            RefreshButtonState(ReceiverState.Disconnected);
        }
        StatusText.Text = "未连接";
        ClientInfoText.Text = "";
        TrafficText.Text = "0 KB/s";
        TotalText.Text = "总计: 0 KB";
    }

    private void IpAddressBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var ip = IpAddressBox.Text.Trim();
        ConnectButton.IsEnabled = ip.Length > 7;
    }

    // ─── 事件回调 ───────────────────────────────────────────────

    private void OnStateChanged(ReceiverState state)
    {
        _lastState = state;
        UpdateUIToSync();
        RefreshButtonState(state);
    }

    private void UpdateUIToSync()
    {
        this.Dispatcher.Invoke(() =>
        {
            switch (_lastState)
            {
                case ReceiverState.Disconnected:
                    StatusText.Text = "未连接";
                    StatusText.Foreground = Brushes.Gray;
                    ClientInfoText.Text = "";
                    break;
                case ReceiverState.Connecting:
                    StatusText.Text = "正在连接...";
                    StatusText.Foreground = Brushes.DarkOrange;
                    ClientInfoText.Text = $"连接中：{IpAddressBox.Text.Trim()}:12345";
                    break;
                case ReceiverState.Connected:
                    StatusText.Text = "已连接 - 正在接收音频流";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 0));
                    ClientInfoText.Text = $"来源：{IpAddressBox.Text.Trim()}:12345";
                    break;
                case ReceiverState.Error:
                    StatusText.Text = "连接失败或出错";
                    StatusText.Foreground = Brushes.Red;
                    ClientInfoText.Text = "请检查IP地址是否正确，Android端是否启动";
                    break;
            }
        });
    }

    private void RefreshButtonState(ReceiverState state)
    {
        this.Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case ReceiverState.Disconnected:
                case ReceiverState.Error:
                    ConnectButton.IsEnabled = IpAddressBox.Text.Trim().Length > 7;
                    DisconnectButton.IsEnabled = false;
                    break;
                case ReceiverState.Connecting:
                    ConnectButton.IsEnabled = false;
                    DisconnectButton.IsEnabled = true;
                    break;
                case ReceiverState.Connected:
                    ConnectButton.IsEnabled = false;
                    DisconnectButton.IsEnabled = true;
                    break;
                default:
                    break;
            }
        });
    }

    private void OnBytesReceived(int bytesPerSec)
    {
        _currentBytesPerSec = bytesPerSec;
    }

    private void UpdateTrafficDisplay()
    {
        if (_receiver == null) return;

        this.Dispatcher.Invoke(() =>
        {
            var total = _receiver.TotalBytesReceived;
            var speedKb = _currentBytesPerSec / 1024.0;
            TrafficText.Text = speedKb >= 1.0
                ? $"{speedKb:0.0} KB/s"
                : _currentBytesPerSec > 0
                    ? $"{_currentBytesPerSec} B/s"
                    : "0 KB/s";
            TotalText.Text = total > 1024 * 1024
                ? $"总计: {total / (1024.0 * 1024.0):0.00} MB"
                : $"总计: {total / 1024.0:0} KB";
        });
    }
}
