# AIFarm2

一个可持续运行的 Unity AI 小镇演示：四名居民可以自主生活、移动、务农、钓鱼、摘果和交流，也可以由玩家逐个指挥。Unity 工程使用 6000.5.10f1，网关位于 `Server/`。

## 最快开始

### 下载 Windows 可玩版（无需 Unity）

1. [直接下载 Windows x64 安装包](https://github.com/zhuzhudyy/AIFarm2/releases/download/v0.1.0-playable/AIFarmTown-Windows-x64.zip)，或到 [Release 页面](https://github.com/zhuzhudyy/AIFarm2/releases/tag/v0.1.0-playable) 的 **Assets** 下载 `AIFarmTown-Windows-x64.zip`。GitHub 自动生成的 **Source code** 是源码，不是游戏安装包。
2. **完整解压 ZIP**，进入 `AIFarmTown` 文件夹，双击 **AIFarmTown.exe**。不要在压缩包内直接运行，也不要只复制 EXE；同目录的 `AIFarmTown_Data`、DLL 和运行库必须保留。
3. 无需安装 Unity。未配置模型时，居民使用本地规则自主生活；可选择居民，输入 `钓鱼`、`摘果`、`持续照料全部九块农田` 或 `停止`，点击 **SUBMIT**。

安装包约 72 MB，包含 Windows 64 位游戏、网关源码和中文说明 `开始游戏.txt`，不包含 Python 运行时、个人存档或 API Key。Release 提供 `SHA256SUMS.txt` 用于校验下载文件。

**可选：连接真实 AI。** 安装 Python 3.11 或更新版本并加入 PATH，在解压后的 `AIFarmTown` 文件夹打开 PowerShell，执行：

```powershell
python -m venv Server/.venv
.\Server\.venv\Scripts\python.exe -m pip install -r Server/requirements.txt
```

完成后重新启动 `AIFarmTown.exe`，在 **API SETUP** 填写自己的模型服务地址、模型名和服务所需的 API Key，点击 **测试并应用**。本地 Python 网关地址默认保持 `http://127.0.0.1:8000`；上游模型地址填写在单独的 **Base URL / Endpoint** 字段。未连接成功时仍可使用本地规则玩法。

当前发布为 Development 演示版。已做 EXE 无图形启动检查，完整桌面渲染与交互尚未单独验收；真实商业模型尚未验证成功。启动日志中仍有 NavMesh 初始化和网络取消/超时提示，详细验证范围见 [验收记录](Docs/TOWN_REPAIR_REPORT.md)。

### Unity 编辑器

1. 安装 Unity **6000.5.10f1**（Windows 构建支持也建议安装）。
2. 用 Unity Hub 打开 `My project/`。
3. 打开 `Assets/AIFarm/Scenes/DemoScene.unity`，点击 Play。
4. 不输入指令也可以观察居民自主安排工作、休息、移动和交流。

### 配置 AI 模型

进入游戏左上角 **API SETUP**，填写：

- **Python 网关地址**：Unity 连接的本机网关，默认 `http://127.0.0.1:8000`。
- **上游 Base URL / Endpoint**：模型服务地址，可填写公网地址、反向代理地址或无鉴权本地服务。
- **Model**：服务接受的模型名称。
- **API Key**：可选；不要提交到 Git。

选择协议后点击 **测试并应用**。测试会执行最小文本、居民决策和居民对话推理；失败时输入内容会保留，状态会显示具体诊断。支持 OpenAI-compatible Chat Completions、OpenAI Responses、Anthropic Messages 和 Gemini 原生协议。配置可记住在本机用户目录，仓库不会保存密钥。

如果暂时没有可用上游，居民仍会使用本地规则继续生活；界面会明确显示“降级”，不会伪装成真实模型在线。

## 游戏操作

- 点击场景居民或底部居民按钮，确认“当前指令对象”。
- 在输入框输入自然语言并点击 **SUBMIT**。例如：`去农田`、`给第3块地浇水`、`持续照料全部九块农田`、`钓鱼`、`钓鱼3次`、`摘果`、`和阿木聊天`、`停止`。
- **农田 / 公共仓库** 查看九块地阶段、剩余时间和库存。
- **对话历史** 打开可滚动完整记录，可按居民筛选；居民头顶气泡是屏幕空间显示。
- **居民诊断** 查看当前动作、目标、任务状态、阻塞原因、AI 来源和下次计划。
- 左上角可切换暂停、1×、5×、20×。网络超时和对话阅读时间使用真实时间，不会因 20× 一闪而过。
- 镜头支持 WASD/方向键移动、右键旋转、中键平移、滚轮缩放，Home 复位。

农业会循环：种子 → 播种 → 浇水/施肥/除草 → 成熟 → 收获。正常收获返还种子；水井补水，杂草可制肥。池塘有两个钓位，果园有四棵可再生果树。

## 启动本地网关（可选）

```powershell
cd Server
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8000 --no-access-log
```

首次使用可按现有 `requirements.txt` 创建虚拟环境并安装依赖。网关浏览器设置页为 `http://127.0.0.1:8000/setup`，与游戏内设置共用配置。

## Windows 桌面版

在 Unity 菜单执行 **AIFarm → Build Windows Town**，输出到 `Builds/Windows/AIFarmTown.exe`。也可以先检查再启动：

```powershell
.\Tools\RunTown.ps1 -PlayerPath '.\Builds\Windows\AIFarmTown.exe' -CheckOnly
.\Tools\RunTown.ps1 -PlayerPath '.\Builds\Windows\AIFarmTown.exe'
```

脚本会检查打包的网关依赖，并只管理本次玩家进程拥有的网关进程树。

## 验证

```powershell
cd Server
.\.venv\Scripts\python.exe -m pytest -q
```

Unity 测试位于 `My project/Assets/AIFarm/Tests/EditMode/` 和 `PlayMode/`，可从 Unity Test Runner 执行。最新结果、实际场景证据、截图及真实上游验证边界见 [Docs/TOWN_REPAIR_REPORT.md](Docs/TOWN_REPAIR_REPORT.md)。

真实商业模型需要用户自己的有效凭据；受控 HTTP 契约服务只用于回归测试，不代表真实模型验证。
