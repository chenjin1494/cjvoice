using NAudio.Wave;
using System.Net.Sockets;
using System.Threading.Channels;

namespace CJVoiceClient;

/// <summary>
/// TCP 客户端接收 Android 端 PCM 音频流，通过 NAudio 播放到指定输出设备。
/// 使用 VB-CABLE 的 "CABLE Input" 作为输出设备时，其他应用程序可将 "CABLE Output" 作为麦克风使用。
/// </summary>
public sealed class AudioReceiver : IDisposable
{
    public const int SampleRate = 16000;

    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _bufferedProvider;
    private readonly Channel<byte[]> _audioChannel;
    private int _totalBytesReceived;

    public event Action<ReceiverState>? StateChanged;
    public event Action<int>? BytesReceived; // 每秒字节数

    public string? ConnectedIp { get; private set; }
    public int Port { get; }
    public int TotalBytesReceived => _totalBytesReceived;
    public bool IsRunning { get; private set; }

    public AudioReceiver(int port = 12345)
    {
        Port = port;
        _audioChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    /// <summary>
    /// 连接到 Android 服务器并开始接收音频流
    /// </summary>
    public void Start(string ipAddress)
    {
        if (IsRunning) return;

        IsRunning = true;
        _cts = new CancellationTokenSource();
        ConnectedIp = ipAddress;
        _totalBytesReceived = 0;

        // 初始化 NAudio 输出
        InitAudioOutput();

        // 启动网络接收任务
        _receiveTask = Task.Run(() => ReceiveLoopAsync(ipAddress, _cts.Token));

        // 启动音频播放任务
        Task.Run(async () => await PlaybackLoopAsync(_cts.Token));

        StateChanged?.Invoke(ReceiverState.Connecting);
    }

    /// <summary>
    /// 断开连接并停止播放
    /// </summary>
    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();

        try
        {
            _tcpClient?.Close();
            _tcpClient?.Dispose();
        }
        catch { }
        _tcpClient = null;
        _networkStream = null;

        DisposeAudioOutput();
        StateChanged?.Invoke(ReceiverState.Disconnected);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    // ─── 音频输出初始化 ─────────────────────────────────────────

    private void InitAudioOutput()
    {
        // 优先使用 VB-CABLE Input
        var deviceNumber = FindVBCableDevice();
        if (deviceNumber >= 0)
        {
            Console.WriteLine($"使用 VB-CABLE 设备 (索引: {deviceNumber})");
        }
        else
        {
            Console.WriteLine("未找到 VB-CABLE，使用默认输出设备");
        }

        _waveOut = new WaveOutEvent
        {
            DeviceNumber = deviceNumber >= 0 ? deviceNumber : -1,
            DesiredLatency = 300,
            NumberOfBuffers = 4
        };

        _bufferedProvider = new BufferedWaveProvider(new WaveFormat(SampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true
        };

        _waveOut.Init(_bufferedProvider);
        _waveOut.Play();
    }

    private void DisposeAudioOutput()
    {
        try
        {
            _waveOut?.Stop();
            _waveOut?.Dispose();
        }
        catch { }
        _waveOut = null;
        _bufferedProvider = null;
    }

    // ─── TCP 网络接收 ───────────────────────────────────────────

    private async Task ReceiveLoopAsync(string ipAddress, CancellationToken ct)
    {
        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(ipAddress, Port, ct);
            _networkStream = _tcpClient.GetStream();

            StateChanged?.Invoke(ReceiverState.Connected);

            var buffer = new byte[8192];
            var byteCountTimer = new System.Timers.Timer(1000);
            int lastBytes = 0;
            byteCountTimer.Elapsed += (_, _) =>
            {
                var delta = _totalBytesReceived - lastBytes;
                lastBytes = _totalBytesReceived;
                BytesReceived?.Invoke(delta);
            };
            byteCountTimer.Start();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (bytesRead == 0) break; // 连接关闭

                    Interlocked.Add(ref _totalBytesReceived, bytesRead);

                    var chunk = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);
                    await _audioChannel.Writer.WriteAsync(chunk, ct);
                }
            }
            finally
            {
                byteCountTimer.Stop();
                byteCountTimer.Dispose();
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException ex)
        {
            Console.WriteLine($"Socket 错误: {ex.Message}");
            StateChanged?.Invoke(ReceiverState.Error);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"接收错误: {ex.Message}");
            StateChanged?.Invoke(ReceiverState.Error);
        }
        finally
        {
            _audioChannel.Writer.TryComplete();
            IsRunning = false;
            StateChanged?.Invoke(ReceiverState.Disconnected);
        }
    }

    // ─── 音频播放 ───────────────────────────────────────────────

    private async Task PlaybackLoopAsync(CancellationToken ct)
    {
        try
        {
            var reader = _audioChannel.Reader;
            await foreach (var chunk in reader.ReadAllAsync(ct))
            {
                _bufferedProvider?.AddSamples(chunk, 0, chunk.Length);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"播放错误: {ex.Message}");
        }
    }

    // ─── VB-CABLE 设备检测 ─────────────────────────────────────

    internal static int FindVBCableDevice()
    {
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            var name = caps.ProductName.ToLowerInvariant();
            // 匹配 "CABLE Input" 或 "VB-Cable" 或 "VB-Audio"
            if (name.Contains("cable input") || name.Contains("vb-cable") || name.Contains("vb-audio"))
            {
                return i;
            }
        }
        return -1;
    }
}

public enum ReceiverState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}
