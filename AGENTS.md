# CJ Voice - AGENTS.md

## 项目概览

将安卓手机变成电脑的无线麦克风。安卓端作为 TCP 服务端采集麦克风音频，Windows 客户端作为 TCP 客户端接收并通过 VB-CABLE 虚拟音频设备输出。

**音频格式（不可更改）：** 16kHz / 16-bit / 单声道 / PCM 无压缩

---

## 技术栈

| 端 | 技术 |
|---|---|
| Android (server/) | Kotlin, AudioRecord, TCP ServerSocket, Foreground Service |
| Windows (client/) | .NET 8, WPF, NAudio 2.2.1, VB-CABLE 虚拟音频驱动 |

---

## 目录结构与入口点

```
cjvoice/
└── server/                          # Android 项目 (Kotlin)
    ├── app/src/main/java/com/cjvoice/server/
    │   ├── MainActivity.kt          # UI + 权限 + 启动/停止服务
    │   └── AudioStreamService.kt    # 核心: AudioRecord 采集 → TCP Socket 发送
    └── app/src/main/AndroidManifest.xml  # 权限: RECORD_AUDIO, FOREGROUND_SERVICE, 通知
    └── build.gradle.kts             # AGP 8.2.0, Kotlin 1.9.20, minSdk=26, targetSdk=34
    └── settings.gradle.kts          # rootProject.name = "CJVoice"
    └── gradle/wrapper/              # Gradle 8.5
└── client/                          # Windows 项目 (C#)
    └── WpfClient/
        ├── CJVoiceClient.csproj     # .NET 8, WPF, NAudio 2.2.1, nullable enabled
        ├── App.xaml(.cs)            # 入口
        ├── MainWindow.xaml(.cs)     # UI + 交互逻辑
        ├── AudioReceiver.cs         # 核心: TCP 接收 PCM → NAudio WaveOut 播放
        └── Styles.xaml              # UI 主题 (Material Design 风格)
```

---

## 关键命令

### Android 端构建
```bash
cd server
./gradlew assembleDebug                      # Linux/macOS
gradlew.bat assembleDebug                     # Windows
```
APK 输出：`server/app/build/outputs/apk/debug/app-debug.apk`

### Windows 端构建
```bash
cd client/WpfClient
dotnet restore
dotnet build -c Release
# 或发布单文件
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

### CI（GitHub Actions）
- `android-build.yml`：server/ 路径变更触发，JDK 17 + Gradle 8.5 wrapper，产物 app-debug.apk
- `windows-build.yml`：client/ 路径变更触发，.NET 8 + dotnet publish 单文件 exe

---

## 架构要点

1. **Android 是服务端，Windows 是客户端。** 安卓启动 TCP ServerSocket（端口 12345），Windows 主动连接。所以安卓先启动，Windows 后连接——顺序不能反。

2. **AudioStreamService 是前台服务。** 使用 `startForeground()` + 通知栏，`foregroundServiceType="microphone"`。Android 13+ 需要 `POST_NOTIFICATIONS` 权限。

3. **音频源使用 `VOICE_COMMUNICATION`**（不是 `MIC`），针对语音通信优化。Android 26+ 的 `AudioRecord` 参数确保兼容性。

4. **Windows 端输出设备检测：** 按名称匹配 `"cable input"`、`"vb-cable"`、`"vb-audio"`（大小写不敏感）。无 VB-CABLE 时回退到默认设备但不报错。

5. **数据流：** `AudioRecord.read()` → `OutputStream.write()` → TCP → `NetworkStream.ReadAsync()` → `Channel<byte[]>` → `BufferedWaveProvider.AddSamples()` → `WaveOutEvent`

6. **没有测试、没有 lint/typecheck 配置。** 没有预提交钩子。手动验证。

---

## 开发注意事项

- Android 端修改音频参数（采样率、位深、声道）后，必须同步修改 Windows 端 `WaveFormat(SampleRate, 16, 1)` 和 `AudioReceiver.SampleRate` 常量。
- 端口 12345 硬编码在两端（`AudioStreamService.PORT`、`AudioReceiver` 默认构造参数）。
- Windows 端 `AudioReceiver` 使用 `Channel.CreateBounded<byte[]>(64)` + `DropOldest`，缓冲区满时丢弃旧数据（实时场景不需要回溯）。
- `AudioReceiver.MainWindow.xaml.cs:UpdateTrafficDisplay()` 显示的速率计算有 bug —— 它显示的是 `totalBytes / 1000` 而非每秒增量。如需修复，需使用 `BytesReceived` 事件的参数。
- 没有 `package.json`，只有 `.csproj` 和 Gradle 构建。不要试图添加 Node 或 npm 相关工具链。
