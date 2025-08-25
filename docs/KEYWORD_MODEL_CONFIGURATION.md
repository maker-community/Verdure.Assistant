# 关键词模型动态配置指南

## 概述

现在可以通过 `VerdureConfig` 动态配置关键词模型，实现跨项目（Console、WinUI、Maui）共用同一套配置逻辑。

## 配置方式

### 1. 配置结构

```csharp
public class VerdureConfig
{
    // ... 其他配置 ...
    
    /// <summary>
    /// 关键词模型配置
    /// </summary>
    public KeywordModelConfig KeywordModels { get; set; } = new KeywordModelConfig();
}

public class KeywordModelConfig
{
    /// <summary>
    /// 关键词模型文件目录路径，如果为空则使用默认路径
    /// </summary>
    public string? ModelsPath { get; set; }
    
    /// <summary>
    /// 当前使用的关键词模型文件名（不含路径）
    /// </summary>
    public string CurrentModel { get; set; } = "keyword_xiaodian.table";
    
    /// <summary>
    /// 可用的关键词模型列表
    /// </summary>
    public string[] AvailableModels { get; set; } = 
    {
        "keyword_xiaodian.table",  // 小点唤醒词
        "keyword_cortana.table"    // Cortana唤醒词
    };
}
```

### 2. Console项目配置

在 `Program.cs` 的 `LoadConfiguration()` 方法中：

```csharp
static VerdureConfig LoadConfiguration()
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    var config = new VerdureConfig();
    configuration.Bind(config);
    
    // 为Console项目设置关键词模型配置
    if (string.IsNullOrEmpty(config.KeywordModels.ModelsPath))
    {
        // Console项目的模型文件在 ModelFiles 目录
        config.KeywordModels.ModelsPath = "ModelFiles";
    }
    
    return config;
}
```

### 3. WinUI项目配置

在 `HomePageViewModel.cs` 的构造函数中：

```csharp
_config = new VerdureConfig
{
    ServerUrl = ServerUrl,
    UseWebSocket = true,
    EnableVoice = true,
    AudioSampleRate = 16000,
    AudioChannels = 1,
    AudioFormat = "opus",
    AutoConnect = true,
    KeywordModels = new KeywordModelConfig
    {
        // WinUI项目的模型文件在 Assets/keywords 目录
        ModelsPath = null, // 使用默认自动检测
        CurrentModel = "keyword_xiaodian.table"
    }
};
```

### 4. 通过JSON配置文件

在 `appsettings.json` 中：

```json
{
  "ServerUrl": "wss://api.tenclass.net/xiaozhi/v1/",
  "EnableVoice": true,
  "KeywordModels": {
    "ModelsPath": "ModelFiles",
    "CurrentModel": "keyword_xiaodian.table",
    "AvailableModels": [
      "keyword_xiaodian.table",
      "keyword_cortana.table"
    ]
  }
}
```

## 使用方式

### 1. 切换关键词模型

在WinUI项目中：

```csharp
// 在 HomePageViewModel 中
public async Task<bool> SwitchKeywordModelAsync(string modelFileName)
{
    var result = await _voiceChatService.SwitchKeywordModelAsync(modelFileName);
    if (result)
    {
        _config.KeywordModels.CurrentModel = modelFileName;
        AddMessage($"[系统] 已切换关键词模型为: {modelFileName}", false);
    }
    return result;
}

// 使用示例
await SwitchKeywordModelAsync("keyword_cortana.table");
```

在Console项目中，可以通过VoiceChatService直接调用：

```csharp
var success = await _voiceChatService.SwitchKeywordModelAsync("keyword_cortana.table");
```

### 2. 获取可用模型列表

```csharp
// WinUI项目
var models = GetAvailableKeywordModels();

// Console项目 
var models = _config.KeywordModels.AvailableModels;
```

### 3. 获取当前模型

```csharp
// WinUI项目
var currentModel = GetCurrentKeywordModel();

// Console项目
var currentModel = _config.KeywordModels.CurrentModel;
```

## 路径解析逻辑

系统会按以下优先级解析模型文件路径：

1. **配置指定路径**：如果 `ModelsPath` 不为空，使用该路径
   - 绝对路径：直接使用
   - 相对路径：相对于程序执行目录

2. **自动检测路径**：如果 `ModelsPath` 为空，自动检测
   - Console项目：优先查找 `ModelFiles` 目录
   - WinUI项目：查找 `Assets/keywords` 目录
   - 回退：从解决方案根目录查找

## 模型文件位置

- **Console项目**：`src/Verdure.Assistant.Console/ModelFiles/`
- **WinUI项目**：`src/Verdure.Assistant.WinUI/Assets/keywords/`

## 支持的模型文件

目前支持两个关键词模型：

1. `keyword_xiaodian.table` - "小点"唤醒词
2. `keyword_cortana.table` - "Cortana"唤醒词

## 注意事项

1. 模型切换是实时的，如果关键词检测正在运行会自动重启
2. 如果指定的模型文件不存在，切换会失败
3. 配置更改后需要重新初始化服务才能生效
4. 路径配置支持相对路径和绝对路径
5. 模型文件必须是Microsoft认知服务格式的 `.table` 文件

## 示例用法

### Console项目示例

```csharp
// 在 Main 方法中切换模型
if (_voiceChatService != null)
{
    Console.WriteLine("正在切换到Cortana模型...");
    var success = await _voiceChatService.SwitchKeywordModelAsync("keyword_cortana.table");
    Console.WriteLine(success ? "切换成功" : "切换失败");
}
```

### WinUI项目示例

```csharp
// 在UI按钮事件中切换模型
private async void OnSwitchModelButtonClick(object sender, RoutedEventArgs e)
{
    var viewModel = DataContext as HomePageViewModel;
    if (viewModel != null)
    {
        var success = await viewModel.SwitchKeywordModelAsync("keyword_cortana.table");
        // UI会自动显示切换结果消息
    }
}
```

通过这种方式，您可以在不同项目中灵活配置和切换关键词模型，实现统一的配置管理。
