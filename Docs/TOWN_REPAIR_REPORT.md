# AI 小镇修复与验收记录

本次直接修改原工作区 `master`，Unity 保持 **6000.5.10f1**。开始时工作区无未提交修改；未执行 reset/clean、未另建项目、未提交任何 Key。以下区分生产实现、受控测试和真实上游。

## 六组根因与修复位置

| 问题 | 已确认根因 | 接通后的实现 |
|---|---|---|
| 模型配置、离线与诊断 | 原适配器及启动逻辑绑定服务/模型；复用进程不能应用新输入；健康检查不等于推理。实机进一步发现 JsonUtility 将可空记忆来源 ID 写成空串，触发 Python 422；同一问题会把“沿用已存 Key”变成“清除 Key”。 | `Server/app/{protocols,providers,gateway_config,schemas,main,setup_ui}.py`；Unity `GatewayConnectionController`、`ApiGatewaySetupPanel`、`LocalAiGatewayProcess`、`AiGatewayJsonCodec`、`RemoteAiGatewayClient`。四个明确协议、自由地址/模型/可空 Key、三项推理探测、版本替换、错误分类、严格校验与本地降级。 |
| 只有芽芽可执行、全镇发呆 | 原静态场景只有芽芽的农事执行器，HUD 指令依赖旧单居民重规划器；其他居民主要运行地点日程。模型高层结果没有完整生活执行链。 | `GameBootstrap`、`DemoHud`、`TownLifeController`、`ResidentTaskContracts`、共享请求协调器及 `TownIntegration`。四人独立执行器和固定 ResidentId 请求；工作/移动/吃饭/休息/睡觉/社交的持续闭环。失败状态退出、导航退避、任务取消和资源预约释放。 |
| 农业不能循环 | 收获不返种子；水肥消耗没有闭环；旧全周期任务把等待与结束混在一起。 | `FarmPlot`、`FarmSimulation`、`FarmInventory`、`WeedAction`、`TownActivityResources`、`TownLifeController`。每株至少返 1 种子；井补水，除草得堆肥并在井旁制肥；一次性/循环/等待作物分开处理。 |
| 时间不真实影响生活 | 原演示成熟参数很短，模拟推进和动作时长来源不统一，旧测试默认直接 20×。 | `GameClock`、`DemoMode`、`DemoSceneConfig`、作物模拟和活动资源共用游戏秒。默认 720 现实秒/日、胡萝卜基础 172800 游戏秒；暂停/1/5/20×；速熟独立且默认关闭。保存时钟、生长累计、库存、循环任务与果树再生。 |
| 中文对话小、乱盖、不易回看 | 原世界空间 Canvas/TextMesh 随镜头缩小；旧阅读时间过短，无完整屏幕历史。 | `TownDialogueOverlay`、`TownUi`、旧气泡和社交展示接线。屏幕投影、24px、不自动缩字、完整中文字体、非交互气泡、按会话真实秒排队、4–12 秒阅读、滚动历史/居民过滤。长文本高度、前景面板层级、窗口比例也有修复。 |
| 钓鱼、果园只有装饰 | 原美术场景有池塘/果园，但无对应库存、计时、预约、任务或再生状态。 | `TownSceneExpansion` 保存编辑器可见地图，原约 56×44 扩至 100×82；道路、休息园、林间眺望点、2 钓位、4 果树、补给点。`TownActivityResources` 和 `ActivityResourceView` 接导航、动作进度、实际鱼果入库、食用和存档。 |

## 启动和使用

1. 用 Unity 6000.5.10f1 打开 `My project/`，打开 `Assets/AIFarm/Scenes/DemoScene.unity`，进入 Play。无需玩家指令，四人会开始本地自主生活。
2. 点击左上 **API SETUP**。直接在游戏中填写上游 Base URL / 完整端点、Model、可选 Key；高级协议按钮依次选择 Chat Completions、Responses、Anthropic、Gemini。点击“测试并应用”。Key 可粘贴、显隐；失败保留输入，配置可记住在仓库外本机用户目录。
3. 设置中明确显示两种地址：Unity→本机网关默认 `http://127.0.0.1:8000`；Python→上游是自由输入地址。不是让玩家把上游地址写进本机网关字段。三个探测全部成功才标记在线，Mock/规则回退不算在线。
4. 点居民模型或输入框上方四人按钮；确认“当前指令对象”。例如：`去农田`、`给1号地播种`、`给1号地浇水`、`持续照料全部九块农田，循环种胡萝卜`、`钓鱼`、`持续钓鱼`、`摘果`、`与阿木聊天`、`停止`。请求返回前切换选择不会改变提交时的居民。
5. 使用“农田/公共仓库”“居民诊断”“对话历史”。诊断包含行为、目标、任务、阻塞、最近决策、来源、下次请求和会话。左上暂停/倍速只影响模拟，不缩短网络冷却和阅读时间。F1 展开原详细面板，可保存/读档。
6. 集中数值在 `Assets/AIFarm/Config/DemoSceneConfig.asset`。场景扩展与所有居民执行组件已保存；需要重建可运行 `AIFarm` 菜单中的场景工具。

物品规则：本次四人共用真实公共仓库，采集/收获直接入库，补给和吃饭从同一库存结算；没有另设只在 UI 里存在的个人物品数。居民的人设、需求、任务和记忆仍独立。镜头可 WASD/方向键移动、右键拖动旋转、中键平移、滚轮缩放，Home 复位。

桌面构建：Unity 菜单 **AIFarm / Build Windows Town**，输出 `Builds/Windows/AIFarmTown.exe`，只打包网关源码及依赖清单。启动：

```powershell
.\Tools\RunTown.ps1 -PlayerPath '.\Builds\Windows\AIFarmTown.exe'
```

该脚本检查 Python 依赖，并向本次启动的玩家进程传入本项目的 Python/Server 路径，不复制 Key，不终止无关进程。

## 验证结果与证据

- Python 全量：**210 通过、1 跳过**，见 [Python-latest.xml](Verification/Python-latest.xml)。覆盖四协议真实 HTTP 契约、地址拼接、Key/URL 更新、配置版本、三项探测、鉴权/限流/超时/余额/无效 JSON、受控修复与空值 wire 回归。
- Unity EditMode 全量：**295 通过、0 失败、0 跳过**，见 [Unity-EditMode-latest.xml](Verification/Unity-EditMode-latest.xml)。包含四居民保存/重开执行器 ID、正常时间默认值、236 条可达路径、旧存档与资源、网关配置空值和模型协议回归。
- Unity PlayMode 全量：**39 通过、0 失败、2 显式跳过**，见 [Unity-PlayMode-latest.xml](Verification/Unity-PlayMode-latest.xml)。跳过项是原有要求手动运行/中断 8000 网关的集成测试，未删除；另以独立的 8011/8012 服务实测新链路和恢复，不影响用户原 8000 配置。期间一次自动化进度包装层误报超时，但 Unity 已导出实际结果；最终又完整重跑 41 项，工具状态与约 164 秒的原始 NUnit XML 均为通过。
- 屏幕 UI 专项：4/4 PlayMode 通过，包含 300 字中文布局/字形、真实 UI raycast 穿透、同会话双人顺序和暂停/20×阅读、完整历史与收发双方筛选，以及四人同时说话避让 HUD/指令对象。
- 真实 Unity→本机网关→受控 HTTP 上游：四居民无玩家指令实际采用远程决策分别 7/6/7/8 次，已有 7 鱼、7 果入公共仓库，发生工作/移动/休息；详见 [实机状态记录](Verification/controlled-live-town.txt)。模型名 `aifarm-contract-test`，这不是商用真实模型验证。
- 实机修复前含记忆的居民请求得到 5 个来源 ID 校验错误；修复后同类 Unity JSON 直接通过 Python Schema，且游戏中的请求进入上游并产生行为，不再停在 422。
- 最终受控场景中，002↔003、001↔004 两组居民自主发起面对面交流，真实 HTTP 对话进入逐轮气泡和历史。同一模型更换上游根地址和测试 Key 后配置 v2→v3，实际最终端点正确；见 [社交与热更新](Verification/controlled-social-and-reconfiguration.txt)。
- 上游 503 时网关仍可达、显示降级，居民继续采摘/休息；恢复后自动在线，四居民远程执行计数继续增加。见 [降级与恢复实机状态](Verification/controlled-outage-recovery.txt)。这不是关闭网关进程的测试。
- 社交导航实机曾出现 `SetDestination` 返回成功但下一帧无路径、居民不移动。增加一次有界完整路径重算/设置；未放宽到达距离、未瞬移兜底。真实接近与离场释放测试通过。三轮农业还暴露了 Unity 将空任务写成空对象的问题，v5 存档精确区分无任务和无效任务，读档回归通过。
- 农业真实场景三轮通过（约 1 分钟的加速回归）：27 播种、108 浇水、27 施肥、27 除草、27 收获；最终本轮证据中 27 胡萝卜 = 仓库 20 + 食用 7，种子仍 9，水/肥各 9。第二轮成长期实际保存/读档，最终停止并释放九块地。见 [逐项账目](Verification/agriculture-three-rounds.txt) 与 [实际画面](ArtPreview/agriculture-three-rounds.png)。测试仅加速模拟时钟和导航到达，不改生产两日成熟，不直接修改作物或结算库存；未做生产 1× 连续运行六个游戏日（约 72 现实分钟）的无瞬移浸泡测试。

实际 Unity Game View 证据（并非网页效果图）：

- [无指令居民活动与产出](ArtPreview/controlled-town-1080p.png)
- [1080p 白天中文气泡](ArtPreview/dialogue-1080p-day.png)
- [720p 夜晚中文气泡](ArtPreview/dialogue-720p-night.png)
- [720p 完整历史](ArtPreview/dialogue-history-720p.png)
- [无指令的受控上游会话](ArtPreview/controlled-town-final-1080p.png)
- [扩展后小镇全景](ArtPreview/expanded-town-1080p.png)
- [1440×900 窗口与游戏内模型设置](ArtPreview/model-settings-1440x900.png)
- [近景钓鱼与实际 19% 进度](ArtPreview/fishing-1440x900.png)

标注“界面验收文本”的截图是显式 UI 长文本夹具，只验证排版，不冒充模型交流。标注“受控模型对话”的交流仅验证 HTTP/会话执行链。

最后的边界修复：离线解析 `播种1号地` 曾错误扩大为全部九块地，已在 Python 和 Unity 同步精确修复，0/10 号地等无效目标明确拒绝。`钓鱼3次`、`摘三个苹果` 支持有限 1—99 次；次数不会当作地块或钓位编号。带明确数量的采集即使写“持续”也按有限次数完成；不带数量的“持续钓鱼”才循环。聊天现在等真实会话终态再结束任务；采集已完成次数进入存档，恢复后只完成剩余次数。

## 真实上游边界

按 OpenAI Docs 复核了两种输出格式的位置：Chat 的 `response_format`、Responses 的 `text.format`；JSON 模式只保证 JSON，不保证业务 Schema，所以项目保留本地结构/业务校验，不强迫兼容服务使用原生 Schema。参见 [官方 Structured Outputs 文档](https://developers.openai.com/api/docs/guides/structured-outputs)。本次不据模型名字推断协议。

本机已有真实服务配置已尝试最小文本、居民决策、居民对话三项；均为 **authentication_failed**，未修改原 Key。因没有有效凭据，**没有任何协议可写“真实模型验证通过”**。四协议为契约测试通过、真实上游未验证；真实模型自主生活与人设交流仍需有效配置验收。

## 桌面与剩余边界

Windows Development 最终构建已实际成功（约 189 MB，0 构建错误），见 [构建结果](Verification/windows-build.txt)。初次直接从临时脚本执行构建，Input System 枚举临时程序集时出现编码异常；脚本域刷新后通过正式 Unity 菜单构建成功，未升级 Unity/包或更换项目。最终有 52 个构建警告，主要包括旧 Find API 弃用，尚未进行全项目警告清理。

`RunTown.ps1 -CheckOnly` 通过；生成的 EXE 已以无图形烟雾测试方式实际运行，启动自有 Python 子进程并监听 8000，加载仓库外原有本机配置。四居民持续收到本地降级决策，真实上游状态准确为 `authentication_failed`；没有 NullReference/MissingReference 异常。启动期间原生网络层有取消/超时日志，随后正常继续降级请求，未把这些日志隐藏。见 [桌面运行日志](Verification/desktop-player.log)。桌面渲染交互未单独截图验收；本记录的画面均来自真正 Unity Game View。

桌面实测还发现 Windows 虚拟环境启动器会产生实际监听端口的 Python 子进程。停止逻辑已由只结束父进程改为结束自有进程树（优先 `Kill(true)`，否则隐藏的 exact-PID `taskkill /T`，上限 2 秒），不按名称或端口杀进程。在隔离 8013 端口重新实际启动网关并调用生产 `StopOwnedGateway` 后，监听数从 1 变为 0；所有测试进程已按所有权清理。

明确尚未完成或未验证的部分：

- 真实模型自主生活、四协议真实上游：**环境受阻**，有效凭据缺失；三项实际调用均鉴权失败。
- **功能边界**：本地解析是明确的受限语法，不能保证任意复合、省略指令；循环聊天、多次独立农事数量目前明确拒绝。需要循环农事请用持续照料任务。物品使用公共仓库，不另外实现个人搬运背包。
- **未做长期验证**：无生产 1× 六游戏日无瞬移连续运行、数小时对话历史压力测试；对话历史与会话索引目前持续累积，没有分页落盘/归档机制。
- **视觉策略**：遮挡时气泡保持可读并加深背景，不按场景深度隐藏；气泡过多会让位，完整消息仍保留在历史。钓鱼为计时进度与角色动作反馈，不是独立操作小游戏。

所有正式场景、配置和默认网关均已恢复为正常生产入口；受控验收服务不作为默认模型配置交付。没有提交 Git 或改动用户原本机 Key。
