using System.Windows;

namespace CJVoiceClient;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 解析命令行参数
        if (e.Args.Length > 0 && e.Args.Contains("--debug", StringComparer.OrdinalIgnoreCase))
        {
            Logger.EnableDebugMode();
        }

        Logger.InfoRelease("CJ Voice Client 启动中...");
        Logger.InfoRelease($"命令行参数: {string.Join(" ", e.Args)}");

        base.OnStartup(e);
    }
}
