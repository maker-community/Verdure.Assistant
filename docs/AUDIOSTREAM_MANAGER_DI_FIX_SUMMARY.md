# AudioStreamManager 依赖注入修复总结

## 问题描述

在调整AudioStreamManager的依赖为接口之后，修改了注入方式，出现了依赖注入错误：

```
System.InvalidOperationException: A suitable constructor for type 'Verdure.Assistant.Core.Services.AudioStreamManager' could not be located. Ensure the type is concrete and services are registered for all parameters of a public constructor.
```

## 错误原因分析

1. **单例模式冲突**：`AudioStreamManager` 采用了单例模式设计，具有私有构造函数：
   ```csharp
   private AudioStreamManager(ILogger<AudioStreamManager>? logger = null)
   {
       _logger = logger;
   }
   ```

2. **错误的注册方式**：在依赖注入配置中，使用了错误的注册方式：
   ```csharp
   services.AddSingleton<IAudioRecorder, AudioStreamManager>();
   services.AddSingleton<ISharedAudioRecorder, AudioStreamManager>();
   ```

3. **依赖注入系统无法创建实例**：依赖注入容器试图通过反射创建 `AudioStreamManager` 的新实例，但无法访问私有构造函数，导致注册失败。

## 修复方案

使用工厂模式注册单例实例：

```csharp
// Register AudioStreamManager as singleton using factory pattern
services.AddSingleton<AudioStreamManager>(provider =>
{
    var logger = provider.GetService<ILogger<AudioStreamManager>>();
    return AudioStreamManager.GetInstance(logger);
});
services.AddSingleton<IAudioRecorder>(provider => provider.GetRequiredService<AudioStreamManager>());
services.AddSingleton<ISharedAudioRecorder>(provider => provider.GetRequiredService<AudioStreamManager>());
```

## 修复原理

1. **工厂函数注册**：通过工厂函数调用 `AudioStreamManager.GetInstance()` 获取单例实例
2. **接口映射**：将不同接口映射到同一个单例实例
3. **依赖共享**：确保所有服务都使用同一个 `AudioStreamManager` 实例

## 已修复的文件

1. **Console 项目**：`src/Verdure.Assistant.Console/Program.cs`
2. **API 项目**：`src/Verdure.Assistant.Api/Program.cs`
3. **WinUI 项目**：`src/Verdure.Assistant.WinUI/App.xaml.cs`

## 验证结果

修复后，Console项目能够正常启动，AudioStreamManager依赖注入成功：

```
info: Verdure.Assistant.Core.Services.AudioStreamManager[0]
      共享音频流启动成功: 16000Hz, 1声道, 帧大小: 960
```

## 总结

这个问题是典型的单例模式与依赖注入框架集成的问题。解决方案是使用工厂模式来桥接单例实例和依赖注入容器，确保：

1. 保持单例模式的设计不变
2. 正确集成到依赖注入框架
3. 支持多接口注册到同一实例
4. 保证线程安全性

这种修复方式既保持了原有的架构设计，又解决了依赖注入的兼容性问题。
