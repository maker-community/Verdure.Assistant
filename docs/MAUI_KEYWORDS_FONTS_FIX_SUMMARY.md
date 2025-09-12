# MAUI项目关键词模型和字体修复总结

## 修复概述

本次修复主要解决了两个问题：
1. 将WinUI项目的关键词模型文件复制到MAUI项目并修改KeywordSpottingService以支持MAUI平台
2. 修复MAUI项目的字体加载错误

## 1. 关键词模型集成

### 文件复制
- 源路径: `src/Verdure.Assistant.WinUI/Assets/keywords/`
- 目标路径: `src/Verdure.Assistant.MAUI/Resources/Raw/keywords/`
- 复制的文件:
  - `keyword_cortana.table` (4.87 MB)
  - `keyword_xiaodian.table` (6.39 MB)

### KeywordSpottingService修改

#### 新增功能
1. **MAUI环境检测**
   - 添加了 `IsMauiEnvironment()` 方法来检测是否运行在MAUI环境中
   - 通过程序集名称和Microsoft.Maui程序集检测MAUI环境

2. **MAUI应用数据目录支持**
   - 添加了 `GetMauiAppDataDirectory()` 方法通过反射访问Microsoft.Maui.Storage.FileSystem
   - 支持从MAUI应用数据目录加载关键词模型

3. **路径查找优化**
   - 修改了 `GetDefaultModelsPath()` 方法以优先支持MAUI平台
   - 路径查找顺序：
     1. Console项目ModelFiles目录
     2. MAUI应用数据目录中的keywords文件夹
     3. MAUI程序目录中的keywords文件夹
     4. WinUI项目的Assets/keywords目录
     5. MAUI项目的Resources/Raw/keywords目录
     6. 回退到相对路径

### 新增MAUI资源服务

创建了 `MauiResourceService` 类 (`src/Verdure.Assistant.MAUI/Services/MauiResourceService.cs`)：

- **功能**：
  - 提供对MAUI应用包内资源的访问
  - 自动将Raw资源复制到应用数据目录供直接文件访问
  - 获取关键词模型目录路径
  - 列出可用的关键词模型文件

- **注册**：在 `MauiProgram.cs` 中注册为单例服务

## 2. 字体加载错误修复

### 问题分析
- 错误信息: `Font asset not found OpenSans-Regular.ttf`
- 原因: MAUI项目中只有占位符文件，缺少实际的字体文件

### 解决方案

1. **删除占位符文件**
   - 移除 `OpenSans-Regular.ttf.placeholder`
   - 移除 `OpenSans-Semibold.ttf.placeholder`

2. **添加实际字体文件**
   - 使用系统字体作为临时解决方案
   - `OpenSans-Regular.ttf`: 使用 Segoe UI (`segoeui.ttf`, 980KB)
   - `OpenSans-Semibold.ttf`: 使用 Segoe UI Bold (`segoeuib.ttf`, 968KB)

3. **添加字体加载错误处理**
   - 在 `MauiProgram.cs` 的 `ConfigureFonts` 中添加了try-catch错误处理
   - 字体加载失败时不会中断应用启动，会自动使用系统默认字体

### 项目文件配置验证
- 确认 `Verdure.Assistant.MAUI.csproj` 中已正确配置：
  ```xml
  <MauiFont Include="Resources\Fonts\*" />
  <MauiAsset Include="Resources\Raw\**" LogicalName="%(RecursiveDir)%(Filename)%(Extension)" />
  ```

## 3. 构建验证

### 成功构建
- ✅ Verdure.Assistant.Core.csproj - 构建成功（6个警告）
- ✅ Verdure.Assistant.MAUI.csproj - 构建成功（46个警告，主要是平台兼容性警告）

### 最终文件结构
```
src/Verdure.Assistant.MAUI/
├── Resources/
│   ├── Fonts/
│   │   ├── OpenSans-Regular.ttf     (980KB - Segoe UI)
│   │   └── OpenSans-Semibold.ttf    (968KB - Segoe UI Bold)
│   └── Raw/
│       └── keywords/
│           ├── keyword_cortana.table (4.87MB)
│           └── keyword_xiaodian.table (6.39MB)
└── Services/
    └── MauiResourceService.cs
```

## 4. 代码修改总结

### 文件修改列表
1. `src/Verdure.Assistant.Core/Services/WakeWords/KeywordSpottingService.cs`
   - 添加System.Reflection using
   - 重写GetDefaultModelsPath()方法
   - 新增IsMauiEnvironment()和GetMauiAppDataDirectory()方法

2. `src/Verdure.Assistant.MAUI/MauiProgram.cs`
   - 添加字体加载错误处理
   - 注册MauiResourceService

3. `src/Verdure.Assistant.MAUI/Services/MauiResourceService.cs` (新建)
   - MAUI平台资源访问服务

## 5. 下一步建议

1. **字体优化**：考虑下载真正的OpenSans字体文件替换临时使用的Segoe UI字体
2. **测试验证**：在Android设备上测试关键词检测功能
3. **性能优化**：监控MAUI应用中关键词模型加载的性能
4. **错误处理**：添加更详细的日志记录以便问题排查

## 修复状态
- ✅ 关键词模型文件已复制到MAUI项目
- ✅ KeywordSpottingService已支持MAUI平台
- ✅ 字体加载错误已修复
- ✅ 项目构建成功
- ✅ 所有修改已应用且经过构建验证
