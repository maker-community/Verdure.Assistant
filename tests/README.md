# 测试项目

本目录包含绿荫助手（Verdure Assistant）项目的所有测试代码。

## 📁 目录结构

```
tests/
├── Verdure.Assistant.Core.Tests/     # 核心库单元测试
├── Verdure.Assistant.Console.Tests/  # 控制台应用测试
├── Verdure.Assistant.WinUI.Tests/    # WinUI应用测试
├── Integration.Tests/         # 集成测试
├── Performance.Tests/         # 性能测试
└── Test.Common/               # 测试通用库
```

## 🧪 测试类型

### 单元测试
- **Verdure.Assistant.Core.Tests** - 核心业务逻辑测试
- **Verdure.Assistant.Console.Tests** - 控制台应用逻辑测试
- **Verdure.Assistant.WinUI.Tests** - WinUI界面逻辑测试

### 集成测试
- **Integration.Tests** - 组件间集成测试
- 数据库连接测试
- 网络通信测试
- 音频处理流程测试

### 性能测试
- **Performance.Tests** - 性能基准测试
- 音频编解码性能
- 网络通信延迟
- 内存使用情况

### 现有测试项目（已迁移）
- **ApiCheck** - API接口检查工具
- **CodecTest** - 音频编解码测试
- **ApiCheck** - OpusSharp库API测试
- **DecodeTest** - 音频解码测试
- **OpusTest** - Opus编解码测试
- **OpusApiTest** - Opus API测试
- **OpusSharpTest** - OpusSharp库测试

## 🚀 运行测试

### 运行所有测试
```bash
dotnet test
```

### 运行特定测试项目
```bash
dotnet test tests/Verdure.Assistant.Core.Tests
```

### 运行带覆盖率的测试
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### 运行性能测试
```bash
dotnet test tests/Performance.Tests --configuration Release
```

## 📊 测试覆盖率

我们致力于维持高质量的测试覆盖率：

- **目标覆盖率**: 80%以上
- **核心库覆盖率**: 90%以上
- **用户界面覆盖率**: 60%以上

查看测试覆盖率报告：
```bash
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report
```

## 🔧 测试工具

### 使用的测试框架
- **xUnit** - 主要单元测试框架
- **Moq** - 模拟对象框架
- **FluentAssertions** - 流畅的断言库
- **AutoFixture** - 测试数据生成
- **BenchmarkDotNet** - 性能基准测试

### 测试辅助工具
- **Coverlet** - 代码覆盖率收集
- **ReportGenerator** - 覆盖率报告生成
- **FakeItEasy** - 替代模拟框架

## 📝 编写测试

### 测试命名约定
```csharp
public class ServiceNameTests
{
    [Fact]
    public void MethodName_When_Should_ExpectedResult()
    {
        // Arrange
        // Act
        // Assert
    }
}
```

### 测试结构
使用 AAA 模式（Arrange-Act-Assert）：

```csharp
[Fact]
public void VoiceChatService_StartVoiceChat_When_Connected_Should_ReturnSuccess()
{
    // Arrange
    var mockClient = new Mock<ICommunicationClient>();
    mockClient.Setup(x => x.IsConnected).Returns(true);
    var service = new VoiceChatService(mockClient.Object);

    // Act
    var result = await service.StartVoiceChatAsync();

    // Assert
    result.Should().BeTrue();
    mockClient.Verify(x => x.SendMessageAsync(It.IsAny<string>()), Times.Once);
}
```

## 🐛 调试测试

### Visual Studio
1. 在测试方法上设置断点
2. 右键选择"调试测试"

### VS Code
```bash
dotnet test --logger "console;verbosity=detailed"
```

### 命令行详细输出
```bash
dotnet test --verbosity diagnostic
```

## 🤝 贡献测试

编写测试时请遵循：

1. **测试命名清晰** - 使用描述性的测试名称
2. **单一职责** - 每个测试只验证一个行为
3. **独立性** - 测试之间不应有依赖关系
4. **可重复性** - 测试结果应该是确定的
5. **快速执行** - 避免长时间运行的测试

## 📈 持续集成

测试在以下情况下自动运行：
- 每次代码提交
- Pull Request创建时
- 发布版本时

GitHub Actions会自动运行所有测试并生成覆盖率报告。
