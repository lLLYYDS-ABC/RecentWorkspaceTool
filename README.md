# AI Workspace Launcher (最近工作空间启动器)

> 🎨 **VisionOS 毛玻璃风格 · 轻量便携 · 零记忆门槛 · 支持一键启动多款 AI 编程 Agent**

平时在不同项目之间切换写代码，每次都要手动打开终端、敲 `cd` 找目录、再敲命令启动 AI 工具？  
**AI Workspace Launcher** 让你按下 `Alt + W` 就能瞬间呼出精致面板，自动列出你最近写过的项目，**点一下你想用的 Agent，敲回车直接进！**

---

## ⚡ 3 步极简上手指南（通俗易懂）

### 第一步：呼出窗口
无论你在任何软件界面中，按下全局热键：
> ⌨️ **`Alt + W`**（或 `Alt + Space`）即可瞬间唤出/隐藏面板。

### 第二步：选择你想用的 AI Agent
看窗口右上角，鼠标轻轻点一下你想使用的 Agent 标签（默认是 OpenCode）：
- **`OpenCode`**：启动 OpenCode AI 终端
- **`Claude Code`**：启动 Claude 终端交互
- **`Codex`**：启动 OpenAI Codex 编程环境
- **`VS Code`**：直接在 VS Code 编辑器中打开当前项目

> 💡 *小技巧：搜索框为空时按键盘上的 `Tab` 键，也可以在各个 Agent 之间顺畅轮流切换。*

### 第三步：选中项目，直接回车！
- 在搜索框里输入项目名称，或者**中文拼音首字母**（例如搜索 `电商平台项目`，输入 `dspt` 即可实时匹配）；
- 用键盘方向键 `↑` / `↓` 移动选择，或者直接用鼠标点击卡片；
- **敲下最普通的 `Enter`（回车键）** —— 工具会自动为你打开新终端，进入项目目录并运行你刚刚选中的 Agent！

---

## 🎮 常用操作与按键（零难度）

所有操作都符合日常习惯，无需死记硬背任何反人类组合键：

| 您想做什么 | 操作方式 | 效果说明 |
|---|---|---|
| **呼出 / 隐藏面板** | 按 <kbd>Alt</kbd> + <kbd>W</kbd> | 任何界面下随时呼出，再次按下立即隐藏 |
| **启动项目** | 敲 <kbd>Enter</kbd>（回车）或鼠标单击 | 用右上角选中的 Agent 打开当前项目 |
| **切换 Agent** | 鼠标点击右上角标签 或 按 <kbd>Tab</kbd> | 在 OpenCode / Claude / Codex / VS Code 间切换 |
| **选第 1~9 个项目** | 按 <kbd>Alt</kbd> + <kbd>1~9</kbd> | 无需移动光标，直接启动前 9 个对应项目 |
| **右键菜单** | 鼠标**右键**点击任意项目 | 弹出直观菜单，随心选择启动工具或复制路径 |
| **复制项目路径** | 按 <kbd>Ctrl</kbd> + <kbd>C</kbd> | 将该项目的绝对路径一键复制到剪贴板 |
| **打开文件夹** | 按 <kbd>Ctrl</kbd> + <kbd>E</kbd> | 直接在 Windows 文件资源管理器中定位目录 |
| **退出 / 清空** | 按 <kbd>Esc</kbd> | 搜索框有字时清空，无字时直接关闭窗口 |

---

## 🌟 核心亮点

1. **零心智负担**：没有复杂的 `Shift+Alt+Ctrl` 组合键，眼睛看到选了什么，回车就启动什么。
2. **VisionOS 拟物美学**：精致微透磨砂毛玻璃与悬浮胶囊阴影，赏心悦目。
3. **全自动智能索引**：自动扫描并聚合 VS Code、Cursor、Windsurf、系统最近访问记录以及桌面开发目录。
4. **极速拼音简拼**：支持汉字首字母实时加权匹配，找中文项目不用切输入法。
5. **极其省电小巧**：单文件绿色便携，隐藏时后台内存仅占用 10~20MB，0ms 唤醒响应。

---

## 🛠️ 如何运行与编译

本项目无需安装庞大的运行库，支持双击一键编译：

### 方式 1：一键编译（推荐）
直接双击根目录下的 **`build.bat`** 脚本，系统将自动调用 Windows 自带的 C# 编译器，几秒内生成 `RecentWorkspaceWidget.exe`，双击即可运行。

### 方式 2：使用 .NET SDK
```bash
dotnet build -c Release
```

---

## 📁 项目代码结构

```
RecentWorkspaceTool/
├── src/
│   ├── Models/
│   │   ├── AgentInfo.cs            # Agent 枚举与实体定义
│   │   ├── WorkspaceItem.cs        # 工作空间项目数据结构
│   │   └── WorkspaceCardView.cs    # 视图卡片组件映射
│   ├── Native/
│   │   └── Win32Api.cs             # 全局热键与系统底层 API
│   ├── Services/
│   │   ├── AgentDetector.cs        # 自动检测本地已安装的 Agent
│   │   ├── WorkspaceScanner.cs     # 项目目录自动扫描聚合引擎
│   │   ├── ProcessLauncher.cs      # 统一进程唤起与启动服务
│   │   ├── MemoryOptimizer.cs      # 极致内存回收与工作集优化
│   │   └── PinyinHelper.cs         # 汉字拼音首字母转换算法
│   ├── UI/
│   │   ├── VisionOSPalette.cs      # 核心 UI 窗口、交互与分段切换栏
│   │   └── ThemeBrushes.cs         # 零内存分配的高刷冻结画刷
│   └── Program.cs                  # 程序入口与单实例互斥锁
├── RecentWorkspaceTool.csproj      # .NET 工程文件
├── build.bat                       # 一键原生编译脚本
├── app.ico                         # 应用图标
├── README.md                       # 本说明文档
├── LICENSE                         # 开源协议 (MIT)
└── .gitignore                      # Git 忽略配置
```

---

## 📄 开源协议

本项目采用 [MIT License](LICENSE) 开源，欢迎自由交流与使用！
