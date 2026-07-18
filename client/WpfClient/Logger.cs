using System.Diagnostics;

namespace CJVoiceClient;

/// <summary>
/// 日志工具。
/// 普通模式：信息写入 Debug output（只对调试器可见）。
/// --debug 模式：信息同时写入控制台和 Debug output。
/// </summary>
internal static class Logger
{
    private static bool _debugMode;

    public static void EnableDebugMode()
    {
        _debugMode = true;
        // 为 GUI 应用分配控制台窗口
        NativeMethods.AllocConsole();
        Info("调试模式已启用");
    }

    [Conditional("DEBUG")]
    public static void Info(string message)
    {
        Write($"[INFO] {message}");
    }

    public static void InfoRelease(string message)
    {
        Write($"[INFO] {message}");
    }

    [Conditional("DEBUG")]
    public static void Warn(string message)
    {
        Write($"[WARN] {message}");
    }

    public static void WarnRelease(string message)
    {
        Write($"[WARN] {message}");
    }

    [Conditional("DEBUG")]
    public static void Error(string message)
    {
        Write($"[ERR] {message}");
    }

    public static void ErrorRelease(string message)
    {
        Write($"[ERR] {message}");
    }

    private static void Write(string message)
    {
        Debug.WriteLine(message);
        if (_debugMode)
        {
            Console.WriteLine(message);
        }
    }
}

/// <summary>
/// Win32 API 包装，用于条件性分配控制台。
/// </summary>
internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool AllocConsole();
}
