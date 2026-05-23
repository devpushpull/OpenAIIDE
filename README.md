# AIIDE — AI 驱动的集成开发环境

AIIDE 是一款基于 WPF (.NET) 的 AI 编程助手，集成代码编辑、终端、Git 管理、MCP 服务等功能，支持通过大模型进行智能对话、代码生成、文件操作和项目构建。

## 技术栈

- **框架**: .NET 10 (net10.0-windows), WPF
- **语言**: C# 13
- **架构**: MVVM
- **编辑器**: AvalonEdit 6.x
- **数据库**: SQLite (Microsoft.Data.Sqlite)
- **AI 模型**: DeepSeek / OpenAI 兼容 API

## 项目结构

```
AIIDE/
├── Controls/          # 自定义 WPF 控件
├── Converters/        # XAML 值转换器
├── Models/            # 数据模型
├── ViewModels/        # MVVM 视图模型
├── Views/             # WPF 窗口与控件
├── Services/          # 业务逻辑服务
├── setting/           # 应用配置
├── docs/              # 项目文档
│   └── research/      # 技术调研文档
├── logs/              # 运行日志与构建输出
└── Tests/             # 单元测试项目
```

## 构建与运行

```bash
# 还原依赖
dotnet restore

# 编译
dotnet build

# 运行
dotnet run --project AIIDEWPF.csproj
```

## 核心功能

- **AI 对话**: 支持流式响应、深度推理、多模型对比
- **代码编辑器**: 语法高亮、代码补全、内联编辑
- **终端**: 集成 PowerShell，支持危险命令确认
- **Git 集成**: 提交、推送、Blame、Diff 可视化
- **MCP 服务**: 工具市场安装/卸载管理
- **项目管理**: .aiide 配置、规则文件、计划存储
- **安全**: API Key DPAPI 加密存储

## 版本

当前版本: **1.0.0**
