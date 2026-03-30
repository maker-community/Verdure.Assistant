# Verdure.Assistant.MAUI

基于.NET MAUI的绿荫助手移动端应用，专为Android平台优化，提供完整的AI语音助手功能。

## 🚀 项目特性

### 核心功能
- 🎤 **语音对话** - 实时语音识别和AI对话
- 🎵 **音乐播放** - 集成酷我音乐服务，支持语音控制
- 📱 **后台录音** - Android前台服务支持后台语音处理
- 🔄 **状态同步** - 与服务器实时同步对话状态
- 🎨 **响应式界面** - 针对小屏设备优化的紧凑界面

### 技术架构
- **.NET MAUI 9.0** - 跨平台UI框架
- **MVVM模式** - 使用Verdure.Assistant.ViewModels共享业务逻辑
- **依赖注入** - 清晰的服务架构
- **Android前台服务** - 后台音频处理能力
- **Material Design** - 现代化Android界面设计

## 📱 界面设计

### 主页面 (HomePage)
- **紧凑布局** - 适配小尺寸设备
- **上下滑动** - 流畅的垂直滚动体验
- **状态显示区** - 连接状态、系统状态、TTS状态
- **音乐播放器** - 歌曲信息、播放控制、进度条
- **对话记录** - 消息列表显示
- **语音控制** - 长按录音、文本输入
- **快捷操作** - 清空记录、连接/断开服务

### 设置页面 (SettingsPage)
- **服务器设置** - WebSocket连接配置
- **语音设置** - 录音质量、语音激活
- **音乐设置** - 音量控制、服务选择
- **界面设置** - 深色模式、可视化选项
- **关于信息** - 版本信息、更新检查

## 🔧 技术实现

### Android前台服务
```csharp
// AudioForegroundService.cs
[Service(Exported = true)]
public class AudioForegroundService : Service
{
    // 后台音频录制和播放
    // 持久通知显示
    // 服务生命周期管理
}
```

### 音频服务管理
```csharp
// AudioServiceManager.cs  
public class AudioServiceManager : IAudioServiceManager
{
    // 跨平台服务接口实现
    // 服务连接和绑定管理
    // 录音控制和状态同步
}
```

### MAUI UI调度器
```csharp
// MauiUIDispatcher.cs
public class MauiUIDispatcher : IUIDispatcher
{
    // 线程安全的UI更新
    // 跨平台调度器实现
}
```

### Android音乐播放器
```csharp
// AndroidMusicAudioPlayer.cs
public class AndroidMusicAudioPlayer : IMusicAudioPlayer
{
    // Android MediaPlayer封装
    // 音乐播放控制
    // 状态事件处理
}
```

## 📋 权限要求

应用需要以下Android权限：

```xml
<!-- 音频相关权限 -->
<uses-permission android:name="android.permission.RECORD_AUDIO" />
<uses-permission android:name="android.permission.MODIFY_AUDIO_SETTINGS" />

<!-- 前台服务权限 -->
<uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_MICROPHONE" />

<!-- 通知权限 -->
<uses-permission android:name="android.permission.POST_NOTIFICATIONS" />

<!-- 存储权限 -->
<uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE" />
<uses-permission android:name="android.permission.READ_EXTERNAL_STORAGE" />

<!-- 网络权限 -->
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
```

## 🎯 使用流程

1. **启动应用** → 自动请求必要权限
2. **连接服务** → 点击连接按钮连接到AI服务器
3. **语音对话** → 长按麦克风按钮进行语音输入
4. **音乐播放** → 通过语音或界面控制音乐播放
5. **后台运行** → 应用可在后台继续处理语音

## 🏗️ 项目结构

```
Verdure.Assistant.MAUI/
├── Platforms/Android/           # Android平台特定代码
│   ├── Services/               # Android服务实现
│   │   ├── AudioForegroundService.cs
│   │   ├── AudioServiceConnection.cs
│   │   ├── AudioServiceManager.cs
│   │   └── AndroidMusicAudioPlayer.cs
│   ├── MainActivity.cs         # 主活动和权限处理
│   ├── MainApplication.cs      # 应用程序类
│   └── AndroidManifest.xml     # 权限和服务配置
├── Views/                      # MAUI视图
│   ├── HomePage.xaml           # 主页面
│   └── SettingsPage.xaml      # 设置页面
├── Services/                   # 服务接口
│   ├── IAudioServiceManager.cs
│   └── MauiUIDispatcher.cs
├── Converters/                 # 值转换器
│   └── ValueConverters.cs
├── Resources/                  # 资源文件
│   ├── Styles/                # 样式定义
│   ├── Images/                # 图片资源
│   └── Fonts/                 # 字体文件
├── App.xaml                   # 应用程序
├── AppShell.xaml              # Shell导航
└── MauiProgram.cs             # 依赖注入配置
```

## 🔄 状态管理

### 连接状态
- **未连接** - 红色指示，显示"连接"按钮
- **连接中** - 显示加载动画
- **已连接** - 绿色指示，显示"断开"按钮

### 录音状态  
- **待机** - 灰色麦克风图标
- **录音中** - 红色麦克风图标，脉冲动画
- **处理中** - 显示处理状态

### 音乐状态
- **停止** - 显示播放按钮
- **播放中** - 显示暂停按钮，进度条更新
- **暂停** - 显示播放按钮，保持进度

## 🎨 界面特性

### 紧凑设计
- **卡片布局** - 使用Frame组件分区显示
- **最小化间距** - 充分利用屏幕空间
- **圆角设计** - 现代化视觉效果
- **阴影效果** - 增强层次感

### 响应式布局
- **Grid布局** - 灵活的栅格系统
- **ScrollView** - 支持垂直滚动
- **自适应字体** - 根据设备调整大小
- **动态隐藏** - 根据状态显示/隐藏元素

### 主题支持
- **浅色主题** - 白色背景，深色文字
- **深色主题** - 深色背景，浅色文字
- **自适应图标** - 跟随系统主题变化
- **品牌色彩** - 使用Verdure主色调

## 🔧 开发注意事项

### 权限处理
- 运行时权限请求在`MainActivity.OnCreate`中处理
- 必须获得麦克风权限才能录音
- Android 13+需要通知权限

### 前台服务限制
- Android 8.0+对后台服务有严格限制
- 前台服务必须显示持久通知
- 服务类型必须声明为`FOREGROUND_SERVICE_MICROPHONE`

### 音频处理
- 使用`Plugin.Maui.Audio`进行跨平台音频操作
- 录音数据可以进一步集成语音识别服务
- 支持实时音频数据处理

## 📱 部署要求

- **Android API Level**: 21+ (Android 5.0)
- **目标框架**: .NET 10.0
- **Android版本**: 推荐Android 8.0+以获得最佳前台服务体验
- **存储空间**: 至少50MB可用空间
- **网络**: 需要网络连接进行AI对话

## 🔮 未来扩展

### 功能增强
- **离线模式** - 本地语音识别和TTS
- **多语言支持** - 国际化界面和语音
- **自定义主题** - 用户自定义颜色方案
- **手势控制** - 滑动和手势操作

### 性能优化
- **内存管理** - 优化大文件处理
- **电池优化** - 降低后台耗电
- **网络优化** - 数据压缩和缓存
- **启动速度** - 预加载和懒加载

### 平台扩展
- **iOS支持** - 扩展到iOS平台
- **Windows支持** - 桌面端适配
- **Web支持** - Blazor Hybrid集成

## 📄 许可证

此项目遵循MIT许可证。在生产环境中使用时，请确保遵守相关的隐私法规和用户许可要求。
