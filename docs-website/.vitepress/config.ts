import { defineConfig } from 'vitepress'

// 共享侧边栏配置
function getSidebar() {
  return {
    '/zh/guide/': [
      {
        text: '入门指南',
        items: [
          { text: '快速开始', link: '/zh/guide/getting-started' },
          { text: '环境搭建', link: '/zh/guide/environment-setup' },
          { text: '项目架构', link: '/zh/guide/architecture' },
          { text: '技术栈介绍', link: '/zh/guide/tech-stack' }
        ]
      },
      {
        text: '项目文档',
        items: [
          { text: 'API 服务 (树莓派)', link: '/zh/projects/api' },
          { text: 'MAUI 跨平台', link: '/zh/projects/maui' },
          { text: 'WinUI 桌面应用', link: '/zh/projects/winui' },
          { text: 'Console 控制台', link: '/zh/projects/console' }
        ]
      },
      {
        text: '开发指南',
        items: [
          { text: 'Visual Studio 开发', link: '/zh/development/visual-studio' },
          { text: 'VS Code 开发', link: '/zh/development/vs-code' },
          { text: '调试技巧', link: '/zh/development/debugging' },
          { text: '部署指南', link: '/zh/development/deployment' }
        ]
      }
    ],
    '/en/guide/': [
      {
        text: 'Getting Started',
        items: [
          { text: 'Quick Start', link: '/en/guide/getting-started' },
          { text: 'Environment Setup', link: '/en/guide/environment-setup' },
          { text: 'Project Architecture', link: '/en/guide/architecture' },
          { text: 'Tech Stack', link: '/en/guide/tech-stack' }
        ]
      },
      {
        text: 'Projects',
        items: [
          { text: 'API Service (Raspberry Pi)', link: '/en/projects/api' },
          { text: 'MAUI Cross-Platform', link: '/en/projects/maui' },
          { text: 'WinUI Desktop App', link: '/en/projects/winui' },
          { text: 'Console Application', link: '/en/projects/console' }
        ]
      },
      {
        text: 'Development',
        items: [
          { text: 'Visual Studio Development', link: '/en/development/visual-studio' },
          { text: 'VS Code Development', link: '/en/development/vs-code' },
          { text: 'Debugging Tips', link: '/en/development/debugging' },
          { text: 'Deployment Guide', link: '/en/development/deployment' }
        ]
      }
    ]
  }
}

// 共享导航配置
function getNav() {
  return {
    zh: [
      { text: '首页', link: '/zh/' },
      { text: '入门指南', link: '/zh/guide/getting-started' },
      { 
        text: '项目文档',
        items: [
          { text: 'API 服务', link: '/zh/projects/api' },
          { text: 'MAUI 应用', link: '/zh/projects/maui' },
          { text: 'WinUI 应用', link: '/zh/projects/winui' },
          { text: 'Console 应用', link: '/zh/projects/console' }
        ]
      },
      { text: '开发指南', link: '/zh/development/visual-studio' },
      { text: 'GitHub', link: 'https://github.com/maker-community/Verdure.Assistant' }
    ],
    en: [
      { text: 'Home', link: '/en/' },
      { text: 'Getting Started', link: '/en/guide/getting-started' },
      { 
        text: 'Projects',
        items: [
          { text: 'API Service', link: '/en/projects/api' },
          { text: 'MAUI App', link: '/en/projects/maui' },
          { text: 'WinUI App', link: '/en/projects/winui' },
          { text: 'Console App', link: '/en/projects/console' }
        ]
      },
      { text: 'Development', link: '/en/development/visual-studio' },
      { text: 'GitHub', link: 'https://github.com/maker-community/Verdure.Assistant' }
    ]
  }
}

export default defineConfig({
  title: 'Verdure Assistant',
  description: '绿荫助手 - 基于 .NET 9 的智能语音助手',
  
  // 多语言配置
  locales: {
    root: {
      label: '简体中文',
      lang: 'zh-CN',
      link: '/zh/',
      title: 'Verdure Assistant',
      description: '绿荫助手 - 基于 .NET 9 的智能语音助手',
      themeConfig: {
        nav: getNav().zh,
        sidebar: getSidebar(),
        editLink: {
          pattern: 'https://github.com/maker-community/Verdure.Assistant/edit/main/docs-website/:path',
          text: '在 GitHub 上编辑此页'
        },
        footer: {
          message: '基于 MIT 许可证发布',
          copyright: 'Copyright © 2025 Maker Community'
        },
        outline: {
          label: '页面导航'
        },
        lastUpdated: {
          text: '最后更新于',
          formatOptions: {
            dateStyle: 'short',
            timeStyle: 'medium'
          }
        }
      }
    },
    en: {
      label: 'English',
      lang: 'en-US',
      link: '/en/',
      title: 'Verdure Assistant',
      description: 'Verdure Assistant - Intelligent Voice Assistant based on .NET 9',
      themeConfig: {
        nav: getNav().en,
        sidebar: getSidebar(),
        editLink: {
          pattern: 'https://github.com/maker-community/Verdure.Assistant/edit/main/docs-website/:path',
          text: 'Edit this page on GitHub'
        },
        footer: {
          message: 'Released under the MIT License',
          copyright: 'Copyright © 2025 Maker Community'
        }
      }
    }
  },

  // 主题配置
  themeConfig: {
    logo: '/logo.png',
    siteTitle: 'Verdure Assistant',
    
    // 社交链接
    socialLinks: [
      { icon: 'github', link: 'https://github.com/maker-community/Verdure.Assistant' }
    ],
    
    // 搜索
    search: {
      provider: 'local'
    }
  },

  // Markdown 配置
  markdown: {
    lineNumbers: true
  },

  // 头部配置
  head: [
    ['link', { rel: 'icon', href: '/favicon.ico' }],
    ['meta', { name: 'theme-color', content: '#646cff' }],
    ['meta', { name: 'og:type', content: 'website' }],
    ['meta', { name: 'og:locale', content: 'zh-CN' }],
    ['meta', { name: 'og:site_name', content: 'Verdure Assistant' }]
  ],

  // 构建配置
  outDir: '../docs-website-dist',
  
  // 忽略死链接检查
  ignoreDeadLinks: true
})