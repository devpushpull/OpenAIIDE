# 更新日志 (Changelog)

## 2026-05-18

### 计划执行增强
- 新增计划详情面板，点击"查看详情"按钮可展开/收起 AI 生成的完整计划内容
- 新增"确认并执行"后自动按步骤顺序执行功能，实时更新每步状态
- 执行中显示当前步骤名称和进度指示器
- 新增 `PlanDetail` / `PlanDetailExpanded` / `CurrentExecutingStep` 属性（ViewModels.cs）
- 新增 `ShowPlanDetailCommand` / `HidePlanDetailCommand` 命令（MainViewModel.cs）
- 增强 `OnAITodoWrite`：自动保存计划详情并追踪当前执行步骤

### 文件操作增强
- 新增 `move_file` 工具：移动文件或目录到新位置，支持跨目录移动
- 新增 `copy_file` 工具：复制文件到目标位置，目标存在时自动报错
- 新增 `read_multiple_files` 工具：批量读取最多10个文件，可选行数限制
- 新增 `ListDirectoryRecursive`：递归列出目录内容，支持关键词过滤和深度控制
- 新增 `GetProjectStructure`：获取带统计信息的项目结构摘要
- 新增 `GlobalSearchReplace`：跨文件批量搜索和替换，支持预览模式

### AI 提示词增强
- 新增文件操作语义理解规则（规则 24：移动→move_file, 复制→copy_file 等）
- 新增路径解析规则（规则 23：相对路径自动解析为绝对路径）
- 新增项目结构感知规则（规则 25：大规模重构前自动扫描项目结构）
- BasePrompt 中新增 move_file / copy_file / read_multiple_files 工具描述

### 开发文档
- 更新 `docs/开发文档.md`：补充计划执行、文件操作、AI 提示词增强等章节
- 新增 `docs/CHANGELOG.md`（本文件）

### 涉及文件
- `ViewModels/ViewModels.cs` — 新增 6 个属性和 3 个后备字段
- `Views/WorkspaceView.xaml` — 新增计划详情面板和查看按钮
- `ViewModels/MainViewModel.cs` — 新增命令和进度反馈逻辑
- `Services/FileOperationService.cs` — 新增 Move/Copy/ReadMultipleFiles/ListDirectoryRecursive/GetProjectStructure 等 6 个方法
- `Services/AIService.cs` — 新增 3 个工具定义、dispatch 和 handler
- `Services/PromptService.cs` — 新增工具描述和 3 条规则
- `docs/开发文档.md` — 更新
- `docs/CHANGELOG.md` — 新建
