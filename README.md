# CJ Voice - WiFi 投屏麦克风工具

![Android](https://img.shields.io/badge/Android-5.0%2B-brightgreen)
![Windows](https://img.shields.io/badge/Windows-10%2F11-blue)
![License](https://img.shields.io/badge/license-MIT-green)

> 将安卓手机变成电脑的无线麦克风。安卓端采集麦克风音频，通过WiFi实时传输到电脑，电脑端接收并输出到虚拟麦克风设备，可被任何应用程序（如会议软件、录音软件）使用。

---

## 系统架构

```
┌──────────────────────────────────────┐
│  Android 端 (服务端)                    │
│                                      │
│  AudioRecord (16kHz/16bit/Mono)      │
│       │                               │
│       ▼                               │
│  TCP Server (端口 12345)              │
│       │  WiFi ▲                       │
└───────┼──────────────────────────────┘
        │ TCP Socket
┌───────┼──────────────────────────────┐
│       ▼  (WiFi)                      │
│  TcpClient 接收 PCM 音频流           │
│       │                               │
│       ▼  NAudio                       │
│  WaveOut → VB-CABLE Input             │
│               │                       │
│               ▼                       │
│  CABLE Output = 系统虚拟麦克风        │
│  (任何应用都可选作为麦克风输入)        │
│                                      │
│  Windows 客户端                       │
└──────────────────────────────────────┘
```

**音频格式：** 16 kHz 采样率 / 16-bit 位深 / 单声道 / PCM 无压缩

---

## 硬件/软件要求

- **Android**：Android 5.0（API 21）或更高
- **Windows**：Windows 10/11 64位
- **网络**：安卓手机与 Windows 电脑连接在同一个局域网WiFi下
- **VB-CABLE**（Windows）：虚拟音频驱动，用于接收网络音频并模拟操作系统麦克风

---

## 快速开始

### 1. 安装 Windows 依赖

1. 下载并安装 VB-CABLE 虚拟音频驱动：
   - 官方下载：[VB-AUDIO CABLE](https://vb-audio.com/Cable/)
   - 点击页面上的 "Download and Install VB-CABLE Driver Now"
   - 解压并运行 `VBCABLE_Setup.exe`（管理员权限）
   - 重启电脑

2. 安装完成后，打开 Windows 声音设置：
   - 进入 **系统 → 声音 → 声音控制面板**
   - 在 播放 选项卡下可看到 **CABLE Input**
   - 在 录制 选项卡下可看到 **CABLE Output**

> **注意**：安装 VB-CABLE 后，默认情况下音频会路由到 CABLE Input。这是正常的，你的电脑播放声音时你需要在喇叭设置中将默认设备改回你的扬声器/耳机。

### 2. 编译项目

#### Android 端

使用 Android Studio 打开 `server/` 目录，或者使用命令行：

```bash
cd server
# 如果有 Gradle Wrapper
./gradlew assembleDebug
# 或者用 gradle
gradle assembleDebug
```

生成 APK：`server/app/build/outputs/apk/debug/app-debug.apk`

#### Windows 端

需要 .NET 8 SDK。使用 Visual Studio 2022 或命令行：

```bash
# 方法一：使用 dotnet CLI（推荐）
cd client/WpfClient
dotnet restore
dotnet build -c Release

# 方法二：使用 Visual Studio 打开 .csproj
# 运行后 exe 文件在：WpfClient/bin/Release/net8.0-windows/CJVoiceClient.exe
```

如果没有安装 dotnet，可以从 [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0) 下载。

### 3. 运行

#### 安卓端启动：

1. 打开 APP，授予录音和通知权限
2. 点击 "启动服务" 按钮
3. 查看本机显示的 IP 地址（例如 `192.168.1.x`）和端口（12345）
4. 状态变为绿色监听中后，等待 Windows 端连接

#### Windows 客户端：

1. 运行 CJVoiceClient.exe
2. 输入 Android 端显示的 IP 地址（例如 `192.168.1.5`）
3. 点击 "连接" 按钮，绿色连接状态
4. 状态显示为 "已连接 - 正在接收音频传输"

#### 使用虚拟麦克风：

连接成功后，在任意应用（微信、腾讯会议、Zoom、OBS、录音机等）中：
- 选择音频输入设备为 **CABLE Output**（VB-CABLE Output）
- 该应用的麦克风输入即为安卓手机的麦克风采集

> 例：腾讯会议/微信 → 设置 → 音频 → 麦克风 → 选择 CABLE Output

---

## 项目结构

```
cjvoice/
├── server/                          # Android 项目
│   ├── settings.gradle.kts
│   ├── build.gradle.kts
│   ├── gradle.properties
│   ├── gradle/wrapper/
│   └── app/
│       ├── build.gradle.kts
│       └── src/main/
│           ├── AndroidManifest.xml
│           ├── java/com/cjvoice/server/
│           │   ├── MainActivity.kt          # UI 主活动
│           │   └── AudioStreamService.kt    # 核心音频采集 Socket 发送
│           └── res/
│               ├── layout/
│               │   └── activity_main.xml    # UI 布局
│               └── values/
│                   ├── colors.xml
│                   ├── strings.xml
│                   └── themes.xml
│
├── client/                          # Windows 项目
│   └── WpfClient/
│       ├── CJVoiceClient.csproj     # 项目依赖
│       ├── Program.cs / App.xaml     # 入口
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml           # UI 布局
│       ├── MainWindow.xaml.cs        # UI 逻辑
│       ├── AudioReceiver.cs          # 核心接收与播放
│       └── Styles.xaml               # UI 主题样式
│
└── README.md
```

## 常见问题

### 连接不上，怎么办？

1. 确保手机和电脑在同一个 WiFi 下
2. 检查 Windows 防火墙没有阻止客户端程序
3. 在安卓端检查屏幕显示的 IP 和端口是否正确
4. 尝试关闭 Windows Defender 防火墙后重试

### 声音卡顿、断断续续？

1. 使用 5GHz WiFi 以降低干扰
2. 关闭其他占用网络的应用
3. 如果持续卡顿，尝试重启 Android 设备
4. 检查 Wi-Fi 信号强度

### VB-CABLE 没检测到？

- 确保已安装 VB-CABLE 驱动并重启电脑
- 以管理员权限运行客户端
- 在声音控制面板确认 CABLE Input/Output 存在

### 录音听起来声音小/大？

可以在 Windows 声音设置中，调整 CABLE Output 录音级别：
1. 右键系统托盘声音图标 → 打开音量混合器
2. 在录制选项卡中选中 CABLE Output
3. 点击属性 → 级别，可调节麦克风增益

### 支持后台录音吗？

安卓端：
- 应用进入后台后，通知栏服务会保持运行
- 建议将应用锁定在最近任务菜单中

### 支持 iOS 吗？

暂时不支持。iOS 系统限制，无法进行持续后台音频采集。

## 技术栈

- **Android**：AudioRecord, TCP Server Sockets, Foreground Service
- **Windows**：.NET 8, WPF, NAudio, VB-CABLE Virtual Audio Device

## License

MIT
