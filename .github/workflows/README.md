# GitHub Actions 工作流说明

本目录包含项目的 GitHub Actions 工作流配置文件。

## 工作流文件

### `build.yml`
构建和测试工作流，支持多平台（Windows、Linux、macOS）和多 .NET 版本。

### `ci.yml`
持续集成工作流，包含代码分析和安全扫描。

### `release.yml`
发布工作流（如果存在）。

### `deploy-docs.yml`
文档部署工作流，自动将 VitePress 文档网站部署到 GitHub Pages。

#### 使用说明

1. **自动触发**：当向 `main` 分支推送包含 `docs-website/` 目录变更的提交时自动触发
2. **手动触发**：可在 GitHub Actions 页面手动运行
3. **自定义域名**：如果配置了自定义域名，请：
   - 在工作流文件中取消注释相应的环境变量设置
   - 修改 CNAME 文件创建步骤，填入你的域名
   - 将 `if: false` 改为 `if: true`

#### 权限要求

工作流需要以下权限：
- `contents: read` - 读取仓库内容
- `pages: write` - 写入 GitHub Pages
- `id-token: write` - 部署到 Pages 环境

#### 环境配置

确保在仓库设置中启用 GitHub Pages：
1. 进入 Settings > Pages
2. 选择 Source 为 "GitHub Actions"
3. 如果使用自定义域名，在 Custom domain 中配置域名

## 故障排除

- 如果部署失败，检查 GitHub Pages 设置
- 确保 `docs-website/package.json` 中的依赖正确
- 检查 VitePress 配置文件中的 base 路径设置