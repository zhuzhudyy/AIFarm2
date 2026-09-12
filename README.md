# AIFarm2

一个可持续运行的 Unity AI 小镇演示：四名居民可以自主生活、移动、务农、钓鱼、摘果和交流，也可以由玩家逐个指挥。Unity 工程使用 6000.5.10f1，网关位于 `Server/`。

## 最快开始

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
