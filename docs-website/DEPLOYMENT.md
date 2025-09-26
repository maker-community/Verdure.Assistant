# VitePress 文档部署指南

本文档说明如何将 VitePress 文档网站部署到 GitHub Pages。

## 🚀 自动部署设置

### 1. GitHub Pages 配置

1. 进入 GitHub 仓库设置页面
2. 点击左侧菜单中的 **Pages**
3. 在 **Source** 部分选择 **GitHub Actions**
4. 保存设置

### 2. 自定义域名（可选）

如果您已经配置了自定义域名：

1. **修改 GitHub Actions 工作流**：
   编辑 `.github/workflows/deploy-docs.yml` 文件：
   ```yaml
   - name: Build with VitePress
     run: npm run build
     working-directory: docs-website
     env:
       CUSTOM_DOMAIN: true  # 取消注释这一行

   - name: Add CNAME file (if using custom domain)
     run: |
       echo "your-domain.com" > docs-website-dist/CNAME  # 修改为您的域名
     if: true  # 将 false 改为 true
   ```

2. **GitHub Pages 设置**：
   - 在 GitHub 仓库的 Pages 设置中，在 **Custom domain** 字段输入您的域名
   - 建议启用 **Enforce HTTPS**

### 3. 触发部署

部署将在以下情况自动触发：
- 向 `main` 分支推送包含 `docs-website/` 目录更改的提交
- 手动在 GitHub Actions 页面运行 "Deploy Documentation to GitHub Pages" 工作流

## 🔧 本地开发

```bash
# 进入文档目录
cd docs-website

# 安装依赖
npm install

# 启动开发服务器
npm run dev

# 构建生产版本
npm run build

# 预览构建结果
npm run preview
```

## 📁 目录结构

```
docs-website/
├── .vitepress/
│   └── config.ts          # VitePress 配置文件
├── zh/                    # 中文文档
├── en/                    # 英文文档
├── public/                # 静态资源
├── package.json           # 项目配置
└── README.md             # 说明文档
```

## 🌐 访问地址

部署成功后，文档网站将可通过以下地址访问：

- **使用默认 GitHub Pages 域名**：`https://maker-community.github.io/Verdure.Assistant/`
- **使用自定义域名**：`https://your-domain.com/`

## 🐛 故障排除

### 部署失败

1. **检查工作流权限**：
   - 确保仓库的 Actions 权限设置正确
   - 检查 `GITHUB_TOKEN` 是否有足够权限

2. **检查依赖问题**：
   ```bash
   cd docs-website
   npm ci  # 重新安装依赖
   npm run build  # 本地测试构建
   ```

3. **检查配置文件**：
   - 验证 `.vitepress/config.ts` 语法正确
   - 确保 `base` 路径配置正确

### 页面显示问题

1. **样式或资源加载失败**：
   - 检查 `base` 配置是否与实际部署路径匹配
   - 如果使用自定义域名，确保 `CUSTOM_DOMAIN` 环境变量设置正确

2. **404 错误**：
   - 检查路由配置
   - 确保所有引用的文件都存在

### 多语言问题

1. **语言切换不正常**：
   - 检查 `locales` 配置
   - 确保对应语言的文件都存在

## 📋 部署检查清单

- [ ] GitHub Pages 设置为 "GitHub Actions"
- [ ] 工作流权限配置正确
- [ ] 如果使用自定义域名，已修改工作流配置
- [ ] 本地构建测试通过
- [ ] 推送更改到 main 分支
- [ ] 检查 Actions 运行状态
- [ ] 验证部署后的网站访问正常

## 📖 相关文档

- [VitePress 官方文档](https://vitepress.dev/)
- [GitHub Pages 文档](https://docs.github.com/pages)
- [GitHub Actions 文档](https://docs.github.com/actions)