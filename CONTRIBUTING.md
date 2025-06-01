# 贡献指南

感谢您对绿荫助手（Verdure Assistant）项目的关注！我们欢迎所有形式的贡献。

## 开发环境要求

- .NET 9.0 SDK 或更高版本
- Visual Studio 2022 或 Visual Studio Code
- Windows 10/11（用于WinUI开发）

## 如何贡献

### 报告Bug

1. 检查是否已有相关的Issue
2. 使用Bug报告模板创建新Issue
3. 提供详细的重现步骤和环境信息

### 提交功能请求

1. 检查是否已有相关的Issue或讨论
2. 使用功能请求模板创建新Issue
3. 详细描述功能需求和使用场景

### 提交代码

1. Fork项目到您的GitHub账户
2. 创建新的特性分支：`git checkout -b feature/your-feature-name`
3. 进行开发并提交：`git commit -am 'Add some feature'`
4. 推送到分支：`git push origin feature/your-feature-name`
5. 创建Pull Request

## 代码规范

### C#代码风格

- 使用PascalCase命名类、方法、属性
- 使用camelCase命名局部变量和参数
- 使用有意义的变量和方法名
- 添加适当的XML文档注释
- 遵循.NET编码约定

### 提交信息格式

```
type(scope): subject

body

footer
```

类型包括：
- `feat`: 新功能
- `fix`: Bug修复
- `docs`: 文档更新
- `style`: 代码格式调整
- `refactor`: 重构
- `test`: 测试相关
- `chore`: 构建过程或辅助工具的变动

示例：
```
feat(core): add voice chat service

实现了语音聊天服务的核心功能，包括：
- 语音录制和播放
- WebSocket通信
- 状态管理

Closes #123
```

## 项目结构

```
xiaozhi-dotnet/
├── src/                    # 源代码
│   ├── Verdure.Assistant.Core/      # 核心库
│   ├── Verdure.Assistant.Console/   # 控制台应用
│   └── Verdure.Assistant.WinUI/     # WinUI应用
├── tests/                  # 测试项目
├── samples/               # 示例代码
├── docs/                  # 文档
├── scripts/               # 构建脚本
├── assets/                # 资源文件
└── build/                 # 构建输出
```

## 测试

在提交代码前，请确保：

1. 所有现有测试通过
2. 新功能有相应的测试
3. 代码覆盖率不降低

运行测试：
```bash
dotnet test
```

## 发布流程

1. 更新版本号
2. 更新CHANGELOG.md
3. 创建Release Tag
4. GitHub Actions自动构建和发布

## 联系方式

如有疑问，可以通过以下方式联系：

- 创建Issue进行讨论
- 参与GitHub Discussions

再次感谢您的贡献！
