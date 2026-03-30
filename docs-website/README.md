# Verdure Assistant 文档网站

基于 VitePress 构建的绿荫助手项目文档网站，提供完整的中英文双语文档。

## 🌟 特性

- 📖 **全面文档**：涵盖 API、MAUI、WinUI、Console 四个项目的详细文档
- 🌍 **双语支持**：中文和英文完整文档，中文作为默认语言
- 🎯 **初学者友好**：详细的零基础上手指南和技术栈解释  
- 🏗️ **架构深度**：深入的系统架构和设计模式讲解
- 🛠️ **开发指南**：Visual Studio、VS Code 开发环境配置
- 🚀 **部署方案**：生产环境部署和 Docker 容器化指南
- 🔍 **搜索功能**：内置全文搜索，快速定位内容

## 📁 文档结构

```
docs-website/
├── zh/                     # 中文文档
│   ├── guide/              # 入门指南
│   │   ├── getting-started.md      # 快速开始
│   │   ├── environment-setup.md    # 环境搭建
│   │   ├── tech-stack.md          # 技术栈介绍
│   │   └── architecture.md        # 项目架构
│   ├── projects/           # 项目文档
│   │   ├── api.md         # API 服务项目
│   │   ├── maui.md        # MAUI 跨平台项目
│   │   ├── winui.md       # WinUI 桌面项目
│   │   └── console.md     # Console 控制台项目
│   └── development/        # 开发指南
│       ├── visual-studio.md       # VS 开发环境
│       ├── vs-code.md             # VS Code 环境
│       ├── debugging.md           # 调试技巧
│       └── deployment.md          # 部署指南
└── en/                     # English documentation
    └── (same structure as zh/)
```

## 🚀 快速开始

### 本地开发

```bash
# 进入文档目录
cd docs-website

# 安装依赖
npm install

# 启动开发服务器
npm run dev
```

访问 http://localhost:5173 查看文档网站

### 构建生产版本

```bash
# 构建静态网站
npm run build

# 预览构建结果
npm run preview
```

构建结果在 `docs-website-dist/` 目录中

## 🚀 部署

### GitHub Pages 自动部署

项目已配置 GitHub Actions 自动部署到 GitHub Pages：

1. **自动触发**：向 `main` 分支推送 `docs-website/` 目录的更改
2. **手动运行**：在 GitHub Actions 页面手动触发
3. **部署地址**：https://maker-community.github.io/Verdure.Assistant/

详细部署配置请参考 [DEPLOYMENT.md](./DEPLOYMENT.md)

## 📝 内容概览

### 入门指南 (Guide)

- **快速开始**：5分钟上手体验所有项目
- **环境搭建**：详细的开发环境配置步骤
- **技术栈介绍**：.NET 10、C# 14、音频处理等技术详解
- **项目架构**：分层架构、设计模式、状态管理等

### 项目文档 (Projects)

#### API 服务项目
- 树莓派机器人后端服务
- RESTful API 接口设计
- WebSocket 实时通信
- Docker 容器化部署
- IoT 设备集成

#### MAUI 跨平台项目
- Android、iOS、Windows、macOS 支持
- 平台特定功能实现
- 自定义控件开发
- 权限管理和安全
- 应用打包分发

#### WinUI 桌面项目
- 现代 Windows 应用界面
- MVVM 架构模式
- 自定义控件和主题
- 系统托盘集成
- 应用打包 MSIX

#### Console 控制台项目
- 命令行界面设计
- 交互式菜单系统
- 命令行参数处理
- 跨平台兼容性
- Docker 部署方案

### 开发指南 (Development)

- **Visual Studio 2026**：完整的 IDE 配置和使用技巧
- **VS Code**：轻量级编辑器的扩展和配置
- **调试技巧**：断点调试、性能分析、故障排除
- **部署指南**：生产环境部署、服务配置、监控

## 🎯 目标读者

### 初学者
- .NET 零基础开发者
- 希望学习现代 .NET 开发的学生
- 从其他语言转向 C# 的开发者

### 有经验开发者
- 希望了解 .NET 10 新特性的开发者
- 需要跨平台解决方案的团队
- 智能语音应用开发者

### 特定领域
- **桌面应用开发者**：学习 WinUI 3 现代界面开发
- **移动应用开发者**：掌握 .NET MAUI 跨平台开发
- **后端开发者**：了解 Web API 和 WebSocket 实时通信
- **IoT 开发者**：树莓派和嵌入式设备开发
- **DevOps 工程师**：应用部署和容器化最佳实践

## 💡 特色内容

### 代码示例丰富
每个概念都配有详细的代码示例和解释，涵盖：
- 完整的项目配置
- 核心功能实现
- 最佳实践代码
- 常见问题解决

### 生产就绪指南
不仅是学习资料，更是生产环境的实践指南：
- Docker 容器化部署
- 系统服务配置
- 性能监控方案
- 故障排除方法

### 渐进式学习路径
根据不同经验水平提供学习建议：
```
初学者：Console → WinUI → API → MAUI
有经验：根据需求直接选择感兴趣的项目
全栈：完整学习所有项目和技术栈
```

## 🔧 技术实现

### VitePress 静态站点生成
- 基于 Vue 3 和 Vite 构建
- 支持 Markdown 扩展语法
- 内置搜索和导航
- 响应式设计，移动端友好

### 多语言支持
- 中文作为默认语言
- 完整的英文翻译
- 语言切换功能
- 本地化配置

### 搜索和导航
- 全文搜索功能
- 结构化侧边栏导航
- 面包屑导航
- 页面间链接

## 📚 学习建议

### 推荐学习路径

1. **快速体验**：[快速开始](/zh/guide/getting-started) → 选择一个项目运行
2. **环境配置**：[环境搭建](/zh/guide/environment-setup) → 配置开发环境
3. **理论基础**：[技术栈](/zh/guide/tech-stack) → [架构](/zh/guide/architecture)
4. **实践项目**：根据兴趣选择 [项目文档](/zh/projects/)
5. **深入开发**：[开发指南](/zh/development/) → 生产部署

### 项目选择建议

- **零基础学习者**：从 Console 项目开始
- **桌面应用开发**：重点学习 WinUI 项目  
- **移动应用开发**：专注 MAUI 项目
- **后端服务开发**：深入研究 API 项目
- **全栈开发者**：按顺序学习所有项目

## 🤝 贡献指南

文档持续更新中，欢迎贡献：

1. 发现错误或改进建议：提交 [GitHub Issue](https://github.com/maker-community/Verdure.Assistant/issues)
2. 文档改进：提交 Pull Request
3. 翻译改进：完善英文翻译或添加其他语言
4. 示例代码：补充更多实用示例

## 📞 技术支持

- **GitHub Issues**：[问题反馈](https://github.com/maker-community/Verdure.Assistant/issues)
- **GitHub Discussions**：[技术讨论](https://github.com/maker-community/Verdure.Assistant/discussions)
- **项目主页**：[Verdure.Assistant](https://github.com/maker-community/Verdure.Assistant)

---

开始您的 Verdure Assistant 学习之旅！🚀