# 单 NPC 假设审计（Town V2）

> 文档性质：当前实现审计与迁移建议，不是实现完成声明。  
> 审计基线：`HEAD 14fcde3`，2026-08-31。  
> 本轮范围：只读检查 C#、Python、场景生成器和测试；未修改代码、Scene、Prefab、Package 或资源。

## 1. 固定目标

Town V2 固定为四名居民：

| ResidentId | 显示名 |
| --- | --- |
| `resident-001` | 芽芽 |
| `resident-002` | 阿木 |
| `resident-003` | 小穗 |
| `resident-004` | 墨墨 |

四人共用一个 Python AI 网关和同一份模型配置，但各自拥有独立的 Persona、`ResidentRuntimeState`、Memory、Schedule 和 Relationships。第一版只允许双人对话，必须支持 A 告诉 B、B 再告诉 C 的可追溯信息传播，并支持一次四人共同参加的全镇活动。现有胡萝卜播种、浇水、施肥、除草、成熟和收获闭环不得退化；网络不可用时四人仍须活动并使用确定性本地对话。

本阶段不引入经济、恋爱、战斗、多作物或复杂职业系统。

## 2. 审计结论

当前仓库是结构完整的单 NPC MVP，不能通过复制三个 NPC GameObject 安全扩展为四居民。

- 没有发现运行时可变 Singleton、全局 `CurrentNpc` / `CurrentResident`，也没有发现运行时 `FindObjectOfType`、`FindFirstObjectByType` 或 `FindAnyObjectByType`。这是可保留的良好基础。
- 没有 `NpcBrain` 类型；其职责目前主要集中在 `ReplanController`，并与单个 `NpcPlanExecutor`、`NpcRuntimeState`、`MemoryStore` 和 `IAiGatewayClient` 形成一套固定引用图。
- `NpcPlanExecutor` 和 `MemoryStore` 都是实例对象，理论上可以各创建四份；但它们及动作、事件、请求、存档均没有 `ResidentId` 所有权，当前隔离只依赖“场景里恰好只有一套对象”。
- UI、存档服务、场景生成器和测试都显式绑定唯一 NPC；Persona 和反思提示还直接写死“芽芽”。
- 当前没有领域级 Conversation、Schedule、Relationships、Reservation 或全镇活动模型。
- `WorldEventLog` 是当前 EventBus 的近似物，但事件没有 actor、audience 或实际 observer；`ObservationService` 会把所有新事件写入传入的那一个 MemoryStore。
- AI 请求和响应没有 `residentId`、端到端 `requestId`、状态版本、会话版本或截止时间；每个 `ReplanController` 还可自行创建网关客户端，无法形成四居民共享配额、取消和公平调度。
- 单 NPC 的本地规划、严格 JSON 校验、远程失败后的本地 fallback、权威 Unity 农田/背包/时间和动作完成前复核，可以作为 V2 迁移基础。

在启用第二名居民前，必须先完成稳定身份、私有数据归属、事件可见性、动作与会话互斥、交互点预约和请求归属；否则最直接的故障是记忆串读、同一点双占、迟到响应写错居民及存档覆盖。

### 2.1 七项危险模式的处置决议

以下结论是后续实现的阻断性约束。“当前未发现”表示必须保持为零，不代表可以在迁移期间临时引入；“等价风险存在”表示即使当前符号名不同，也必须按目标结构完成迁移。

| 危险模式 | 当前判定 | 处置决议 | 启用第二名居民前的验收证据 |
| --- | --- | --- | --- |
| `static CurrentNpc` / `static CurrentResident` | 当前未发现 | 永久禁止可变静态居民引用、选中居民、Memory、Goal、Conversation 或 request pending。允许纯函数、不可变常量和只读 definition。 | 扫描全部运行时 C# 静态字段为零命中；切换 HUD 选中行不改变任何领域 owner。 |
| `FindObjectOfType<NpcBrain>` 及同类唯一 NPC 查找 | 当前未发现；仓库也没有 `NpcBrain` 类型 | 永久禁止通过类型、GameObject 名或 tag 查找“唯一 NPC”。居民表现对象只携带序列化 `ResidentId`，并向显式注入的 ResidentRegistry 注册。 | 运行时源码对 `FindObjectOfType`、`FindFirstObjectByType`、`FindAnyObjectByType` 和固定 NPC `GameObject.Find` 为零命中；重复/未知 ID 启动失败。 |
| 单个 `CurrentMemory` 或无 owner MemoryStore | 等价风险存在 | 每个 MemoryStore 构造时强制绑定唯一 `OwnerResidentId`；所有读写经 owner/requester 校验或受限 MemoryView。不存在可切换的 CurrentMemory。 | A/B/C/D 四个 store 均为不同实例和 owner；A 写入后 B/C/D 查询不到；伪造 requester 被拒绝且无写入。 |
| 单个 `ActiveConversation` | 当前 Conversation 领域整体缺失 | ConversationCoordinator 维护 `sessionsById` 与 `activeConversationByResidentId`；每个 session 恰好两人，每居民最多映射一个 session。禁止全局单个 ActiveConversation 代表全镇。 | 可并行存在两场互不共享参与者的双人会话；同一居民加入第二场被原子拒绝；结束、超时、取消后双方索引均释放。 |
| 单个 `CurrentGoal` | 等价风险存在：`ReplanController.activeGoal` | 私人/居民目标归属 `ResidentId`，由 ResidentRuntimeState 或按 ID 的 GoalRepository 保存；公开 TownGoal 单独建模，不得冒充某居民私有 CurrentGoal。 | 四名居民可同时持有不同 goal/version；更新 A 不改变 B/C/D；公开农田目标经 TownScheduler 分解后仍保留 actor/assignee。 |
| `SaveData.NpcState` 或其他单数 NPC 存档 | 等价风险存在：`npc`、`npcRuntime`、`executor` 等均为单数 | Save schema 显式升级；共享世界与 `residents[]` 分离，residents 必须恰好含四个稳定唯一 ID，每项保存自己的 runtime、memory、goal、schedule 和动作恢复意图。 | 四居民 round-trip 后 ID、私有状态、Memory、Schedule 与关系边不串位；重复/缺失/未知 ID 导致整次加载失败且不部分应用。 |
| UI 直接引用固定芽芽 | 明确存在 | HUD 只依赖 ResidentRegistry 的只读投影；`SelectedResidentId` 只属于 ViewState。所有私人命令显式携带目标 ID，公开命令显式标为 public；每个头顶气泡绑定自己的 ResidentId。 | 四行居民摘要同时可见；切换选择只改变详情；向阿木提交私人输入不会改变芽芽、小穗或墨墨；UI 源码不再写死芽芽作为数据路由。 |

处置优先级为：先禁止全局/查找回归并引入 ResidentId，再迁移 Memory 与 Goal，然后建立 ConversationCoordinator，最后升级 SaveData 和 UI。前五项未通过前，不得在场景中启用第二名居民；存档与 UI 未通过前，不得宣称 Town V2 可演示。

## 3. 建议修改阶段

| 阶段 | 目标 |
| --- | --- |
| V2-0 契约与身份 | 冻结 `ResidentId`、事件可见性、AI 请求信封、响应白名单和版本规则；保持场景仍只有芽芽。 |
| V2-1 居民数据隔离 | 引入四份 `ResidentDefinition` / `ResidentRuntimeState`、`ResidentRegistry`，并按 ID 隔离 Persona、Memory、目标、日程和反思。 |
| V2-2 本地小镇协作 | 引入 `TownScheduler`、`SocialGraph`、`ConversationCoordinator`、`ReservationService`，先完全离线跑通四居民活动、双人对话、A→B→C 和全镇活动。 |
| V2-3 共享 AI | 只通过一个 `AiRequestCoordinator` 和一个共享网关客户端调用同一 Python 网关/模型配置；加入预算、取消、deadline 和 stale guard。 |
| V2-4 持久化与呈现 | 升级 SaveData、UI、场景生成器和测试，验证四居民恢复、可观察性与旧种田闭环回归。 |

## 4. 逐文件审计

| ID | 文件（当前证据） | 当前假设 | 风险 | 迁移方案 | 建议修改阶段 |
| --- | --- | --- | --- | --- | --- |
| SN-01 | `My project/Assets/AIFarm/Presentation/ReplanController.cs:21-43, 175-218`；仓库中无 `NpcBrain` 符号 | 没有独立 `NpcBrain`；一个 `ReplanController` 同时持有单个 executor、goal、planner、runtime、memory/reflection 服务、AI client 和 pending 状态，相当于单 NPC Brain。 | 简单复制组件会得到四套无 owner 的“脑”，共享世界事件与资源却无法证明状态和回包属于谁。 | 不新增全局“当前居民”；以 `ResidentId` 为所有入口参数，把居民私有聚合交给 `ResidentRegistry`，把城镇、会话、预约和 AI 调度职责拆到目标协调器。 | V2-0、V2-1 |
| SN-02 | `My project/Assets/AIFarm/Npc/NpcPersonaDefinition.cs:8-33` | 私有构造函数加静态只读 `Yaya` 是唯一 Persona；名字、角色和提示均写死芽芽。 | 四个 runtime 即使分开创建，也会表现成同一个芽芽；显示名容易被误当身份键。 | 用不可变 `ResidentDefinition` 注册芽芽、阿木、小穗、墨墨四份 Persona；稳定 `ResidentId` 是关联键，显示名只用于呈现。 | V2-1 |
| SN-03 | `My project/Assets/AIFarm/Npc/NpcRuntimeState.cs:17-38, 50-105`；`ReplanController.cs:200,329` | `NpcRuntimeState` 不含 ID，缺省 Persona 为芽芽，并自行创建一个无 owner 的 MemoryStore；周期与反思只在单实例内编号。 | 无法验证 runtime、目标、反思或迟到结果的所有者；重置时固定重新创建芽芽。 | 由构造边界强制传入 `ResidentId` 和 definition；改为 `ResidentRuntimeState`，保存 state version、request epoch、active action/conversation ID，并禁止缺省 Persona。 | V2-1 |
| SN-04 | `My project/Assets/AIFarm/Npc/MemoryStore.cs:17-31, 33-80, 109-158`；`MemoryEntry.cs:18-64` | MemoryStore 是有界实例容器，但没有 owner；所有读写 API 都不要求 resident/requester。MemoryEntry 只有 sequence、文本、重要度和可选事件类型。 | 隔离完全依赖调用者传对对象；任意组件可把 A 的 store 传给 B。记忆也缺少 source event ID、说话者、会话和传播链。 | 每个 store 在创建时绑定且永不改变 owner `ResidentId`；查询必须显式传 owner/requester 或返回受限视图。记录 `SourceKind`、`SourceEventId`、`ConversationId`、直接说话者及可选 root fact/provenance。 | V2-1 |
| SN-05 | `My project/Assets/AIFarm/Core/WorldEventLog.cs:22-57, 75-115` | 当前没有 `EventBus` 类型；`WorldEventLog` 充当同步事件日志。事件只有 sequence、时间、kind、message 和 plot number。 | 无 actor、target、audience、observer 或 event ID，无法区分私人输入、双人对话、可感知事件与公开全镇事件。所有订阅者都收到同一对象。 | 定义带稳定 EventId、actor、visibility 和实际 observer 集合的领域事件；至少支持 `Private`、`Conversation`、`Perceivable`、`PublicTownEvent`。只有可见性投影可写居民记忆。 | V2-0、V2-1 |
| SN-06 | `My project/Assets/AIFarm/Npc/ObservationService.cs:8-39, 42-80`；`ReplanController.cs:832-835` | 一个 ObservationService 只有一个全局游标，遍历整个日志并把每条新事件写入传入的单个 MemoryStore；没有观察者判定。 | 四份服务若订阅同一日志，会默认让四人知道全部事件；共享一份服务又会因单游标让其他居民漏读。容量 10 的日志还可能在慢消费者读取前淘汰事件。 | 观察进度按 `ResidentId` 保存；发布时由 Unity 本地感知规则确定实际 observers，投影时再次校验 audience，再写入对应 owner 的 store。可靠领域流与仅供 HUD 的短日志分离。 | V2-1 |
| SN-07 | `My project/Assets/AIFarm/Npc/ReflectionService.cs:13-18, 20-72`；`ReplanController.cs:38-43, 652-788` | ReflectionService 只持有一个 runtime，提示文本直接写死芽芽；pending 反思与表达状态位于单个 ReplanController。 | 其他居民会使用芽芽 Persona；回包不能证明属于哪位居民、哪个目标版本，重置或读档后的旧反思可能污染新状态。 | 上下文只从请求 owner 的 definition 与私有 memory view 构建；反思信封带 ResidentId、GoalId/version、RequestId 和 epoch，应用前做 stale 检查。每居民独立反思历史。 | V2-1、V2-3 |
| SN-08 | `My project/Assets/AIFarm/Presentation/NpcPlanExecutor.cs:29-58, 127-155, 297-330, 390-430` | 每个 executor 确实只有一个 current action 和一条队列，适合“一居民至多一个动作”；但 executor、action 和发出的事件均无 resident owner。 | 复制四个 executor 后无法审计谁启动/完成动作；系统也不能阻止同一居民被两套 executor 驱动，或在会话中开始动作。 | 每名居民恰好注册一个 executor；动作命令含 `ActionId + ResidentId`，由统一活动租约检查“一动作/一会话”互斥，事件记录 actor。 | V2-1、V2-2 |
| SN-09 | `My project/Assets/AIFarm/Npc/NpcActionContext.cs:9-33`；`INpcAction.cs:5-16` | 动作上下文只暴露共享农田、背包、时钟、模拟和事件日志；动作接口不含 actor、动作 ID 或预约 token。 | 多居民可以同时检查同一前置条件并走向同一点；动作完成时虽会复核世界状态，仍无法证明交互点所有权或动作归属。 | 领域命令携带 actor、action ID、期望 world version 和 reservation fencing token；Unity 在原子提交前复核 owner、租约、资源和前置条件。 | V2-2 |
| SN-10 | `My project/Assets/AIFarm/Npc/DeterministicFarmPlanner.cs:43-137`；`WorldStateQuery.cs` | 每个 planner 都会在完整目标中选择“第一个”需要处理的地块；当前只有一个 planner，所以顺序稳定。 | 四个 planner 会争抢同一最低编号地块和共享背包，产生重复导航、失败风暴和非确定演示。 | 由 `TownScheduler` 对九块地按固定规则分配 owner，并按现有农事阶段发布本地任务；居民 planner 只处理分给自己的合法任务，冲突时本地等待/重规划。 | V2-2 |
| SN-11 | `My project/Assets/AIFarm/Npc/FarmGoalSpec.cs:9-38`；`Server/app/schemas.py:85-102`；`AiGatewayJsonCodec.cs:46-80` | 单 NPC 目标固定包含完整 1–9 地块；远程模型输出契约仍包含 `target_plot_numbers` 和各低层阶段布尔值。 | 在 V2 中让模型选择地块或低层动作会绕过确定性分工，并违反“模型只选允许的高层意图”。 | V2 响应只允许版本化高层 intent（如接受完整胡萝卜闭环、请求会话、参加活动或 Idle）；具体居民、地块、顺序和动作全部由 Unity 本地规则决定。 | V2-0、V2-3 |
| SN-12 | `My project/Assets/AIFarm/Presentation/PlotInteractionPoint.cs:8-41`；`NpcNavigator.cs:92-130, 272-283` | 交互点只描述 plot number、位置和朝向；navigator 找到点后直接移动。仓库中没有 reservation/lease 实现。 | 两名居民可同时移动到并使用同一世界交互点，造成穿模、重复动作或后到者覆盖先到者。 | 所有导航前先向 `ReservationService` 原子预约稳定 InteractionPointId；租约记录 ResidentId、ActionId、过期时间和 fencing token，完成/失败/取消/读档均幂等释放。 | V2-2 |
| SN-13 | `My project/Assets/AIFarm/Ai/IAiGatewayClient.cs:7-26`；`AiGatewayJsonCodec.cs:16-43, 481-530` | 三类 AI 调用只带业务文本/goal 和 callback；序列化 DTO 没有 residentId、requestId、状态版本、会话或 deadline。 | 请求不能归属、关联、取消或去重；迟到回包无法判断其居民、会话 turn 或目标是否仍有效。 | 冻结 V2 请求/响应 envelope：`RequestId`、`ResidentId`、operation、resident/request epoch、world version、可选 conversation/turn/version、allowed intents 和绝对 deadline；响应原样回显关联字段。 | V2-0、V2-3 |
| SN-14 | `Server/app/schemas.py:122-134`；`Server/app/main.py:48-55, 101-111`；`Server/app/providers.py:212-245` | Python 请求 schema 没有 resident_id 或关联版本。FastAPI 应用创建一份 provider，provider 从环境读取一份 model 配置；这部分符合“共享网关/模型配置”的方向。 | Unity 复制客户端会绕过统一调度；服务端日志生成的内部 request ID 不能替代端到端 ID，也无法帮助 Unity 丢弃 stale response。 | 保留单 FastAPI provider 和单模型配置；扩展版本化 schema 以验证并回显请求信封，但不让服务端持有权威居民状态。服务端仍只做严格输出校验和可选远程调用。 | V2-0、V2-3 |
| SN-15 | `ReplanController.cs:40-44, 125-165, 791-800`；`RemoteAiGatewayClient.cs:9-38` | 每个 ReplanController 可持有或自行创建一个 gateway client，并只用本实例 bool/HashSet 限制 pending 请求。 | 四套 controller 没有共享并发、预算、公平性或按居民取消；也不能保证四人确实使用同一个 Unity 网关配置。 | 只有一个 `AiRequestCoordinator` 持有一个共享 `IAiGatewayClient` 和只读 `AiGatewayConfig`；居民组件只能入队，不能接触 transport。队列按 ResidentId 公平调度并记录每请求实际 Remote/Local 来源。 | V2-3 |
| SN-16 | `ReplanController.cs:390-440, 470-476, 546-568, 615-684`；`NpcPlanExecutor.cs:297-302, 324-330` | `Update()` 会调用 `TickReplan()`；目标完成可触发远程反思，executor 在 Update 中发出的 ActionStarted 事件又可触发远程表达。 | 远程请求存在从 Update 调用栈间接启动的路径，直接违反多居民不变量；四人会把帧驱动放大成请求风暴。 | Update 只推进纯本地模拟并发布本地领域事件；启动时建立的长驻 dispatcher 在 Update 调用栈之外消费明确请求事件。增加 spy transport 回归测试，断言 Update/tick 不调用网络。 | V2-3 |
| SN-17 | `ReplanController.cs:265-268, 322-325, 687-742`；`UnityWebRequestGatewayTransport.cs:41-48` | 重置/读档只 `StopAllCoroutines` 并清 pending；transport 无取消令牌，表达/反思完成后没有 owner/revision 检查便应用。 | 旧目标、旧会话或读档前请求的迟到结果可能写入当前 UI、反思或记忆。停止 coroutine 不是可靠的端到端 stale guard。 | coordinator 持有取消句柄；所有失效路径递增 epoch。无法取消时标记 ignore；回包应用前逐项核对 owner、request、state、goal/conversation version 和 deadline，不匹配执行确定性 no-op。 | V2-3 |
| SN-18 | `My project/Assets/AIFarm/Presentation/DemoHud.cs:15-40, 83-128, 202-260, 340-445, 478-523` | HUD 序列化并直接读取一个 executor 和一个 replanner；命令直接提交给该 controller，文本还写死“芽芽”。 | UI 选择会被误做领域“当前居民”，或所有命令、状态、记忆都投影到固定芽芽；无法同时诊断四人的动作/会话/请求。 | HUD 从 registry 读取四行只读摘要；选中 ResidentId 仅是 ViewState。玩家私人输入必须显式目标 ID，公开命令显式标记 public；详情面板按 ID 查询受限视图。 | V2-4 |
| SN-19 | `My project/Assets/AIFarm/Presentation/NpcDialogueBubble.cs:10-23, 28-70`；`NpcExpressionDirector.cs:8-21, 103-134` | “DialogueBubble”只轮询一个 replanner 的表达；`NpcExpressionDirector` 是单实例表达队列，不是 Conversation 领域模型。仓库中无会话参与者、turn、timeout 或互斥状态。 | 无法限制为双人对话、阻止一人进入两场对话，也无法可靠实现 A→B→C 信息传播；表达队列可能被误当会话历史。 | 新建 `ConversationCoordinator` 管理恰好两名不同参与者、原子占用、turn owner、严格 turn limit、单调时钟 timeout、版本和结束原因。气泡只显示自己 ResidentId 当前合法话轮。 | V2-2、V2-4 |
| SN-20 | `My project/Assets/AIFarm/Presentation/SaveData.cs:6-22, 68-148` | SaveData v1 只有单数 `npc`、`farmGoal`、`executor`、`recentMemories`、`recentReflections` 和 `npcRuntime`，且无 ResidentId。 | 四人保存时必然遗漏或互相覆盖；读档无法验证唯一 ID、私有 memory owner、关系边或日程归属。 | 升级版本：共享世界单独保存，`residents[]` 每项强制稳定唯一 ID，并保存各自 runtime、memory、goal、schedule、executor 恢复意图；`SocialGraph` 以有序 ID 对保存。 | V2-4 |
| SN-21 | `SaveGameService.cs:19-41, 147-195, 355-493, 669-755, 758-842`；`SaveGameController.cs:11-35, 41-92` | 存档服务和 UI 只接收一个 executor、replanner 和 NPC Transform；恢复固定构造 `NpcPersonaDefinition.Yaya`。 | 即使 SaveData 改成数组，捕获/验证/原子应用仍只处理一套对象；读档还可能保留旧请求、会话或预约。 | 存档服务从 registry、scheduler、social graph 读取四居民快照；先在临时模型完整校验再原子替换。读档关闭未完成会话、清预约、递增全部 request epoch，并本地重建合法动作。 | V2-4 |
| SN-22 | `My project/Assets/AIFarm/Editor/DemoSceneBuilder.cs:63-78, 299-349, 507-512, 842-876` | BuildDemoScene 只调用一次 `CreateNpc`，创建固定名 `NPC_Blockout_Capsule`，并把唯一 executor/replanner/transform 传给 HUD 与存档 UI。 | 当前生成器是“一 NPC 场景”的硬锁；手工复制对象会在下次重建场景时丢失且无法保证稳定 ID。 | 由四份 ResidentDefinition 配置生成四个带唯一 ResidentId 的居民根对象和各自表现/执行器；共享服务只创建一次。所有引用通过 registry/显式 ID 连接。 | V2-4 |
| SN-23 | `My project/Assets/AIFarm/Tests/EditMode/DemoSceneBuilderTests.cs:25-39, 67-80`；现有 save/HUD/end-to-end 测试 | 场景测试明确 `AssertSingleRoot(..., "NPC_Blockout_Capsule")`，并用 `GameObject.Find` 取唯一 NPC；多数 fixture 只构造一套 controller/executor。 | 测试会把单 NPC 结构固化为“正确”，且不覆盖重复 ID、私有记忆串读、双会话、资源争用、stale 回包和全镇活动。 | 保留单 executor 与旧完整种田回归；新增四居民 fixture 和 V2 专项验收。场景断言按 ResidentId 查询 registry，不按唯一名字或数组顺序。 | V2-4 |
| SN-24 | 全部 `My project/Assets` 运行时 C# 搜索；`DemoSceneBuilderTests.cs:41-120` | 未发现运行时 `FindObjectOfType` / `FindFirstObjectByType` / `FindAnyObjectByType`。`GameObject.Find` 仅出现在 Editor 测试；`Transform.Find` 也只用于生成场景断言。 | 当前没有“查找唯一 NPC”的运行时隐患，但若迁移时为省事新增此逻辑，会重新引入隐式唯一居民和非确定绑定。 | 将“运行时不得按类型/名字查找唯一 NPC”设为守护规则；场景对象只以序列化 ResidentId 注册并解析，未知/重复 ID 启动失败。 | 保留；V2-4 加守护测试 |
| SN-25 | `DemoSceneBuilder.cs:16-24`；`AiGatewayJsonCodec.cs:9-14`；`NpcActionCommandParser.cs:8`；`NpcPersonaDefinition.cs:8-15` | 静态成员主要是 Editor 构建器、无状态 codec/parser、只读常量和只读芽芽 Persona；没有可变运行时 Singleton。`GameBootstrap` 也是场景实例，不是静态单例。 | 无状态 static 本身没有跨居民串写风险；真正风险是把静态 `Yaya` 或未来可变 `CurrentResident` 当路由。 | 保留纯函数和不可变常量；Persona 改由 registry 的只读 definition 获取。禁止新增可变静态居民状态、全局选中居民或静态 request pending。 | V2-1；持续守护 |
| SN-26 | `My project/Assets/AIFarm` 与 `Server/app` 中无 Schedule、SocialGraph/Relationship、领域 Conversation、Reservation、ResidentId 或 TownActivity 实现 | 单 NPC 不需要日程冲突、关系边、双人会话互斥、信息传播和全镇活动，因此这些能力目前缺失而非“可配置未开启”。 | 直接在 UI 或提示词中模拟会导致状态不可保存、不可验证，模型可能成为权威状态源。 | 按目标架构新增 `TownScheduler`、`SocialGraph`、`ConversationCoordinator` 和 `ReservationService`；所有状态按 ID 保存并由 Unity 本地规则更新。全镇活动是公开领域事件，不伪装成四人群聊。 | V2-1、V2-2 |
| SN-27 | `RemoteAiGatewayClient.cs:42-214, 216-269`；`LocalAiGatewayClient.cs`；`LocalTemplateExpressionService.cs` | 远程失败后已有本地解释/表达/反思 fallback，单 NPC 断网闭环可用；但本地模板不接收 ResidentDefinition，表达差异不按居民隔离。 | 四人离线时可能说完全相同的话，或某条远程失败路径缺少对应居民/会话的确定性收尾。 | 每种远程 operation 必须同时注册按 ResidentId/Persona 的确定性本地处理器；断网时四人继续日程、动作和双人对话，且结果不依赖网络时序。 | V2-2、V2-3 |

## 5. 可保留的实现基础

| 现有能力 | 可保留原因 | V2 必须补充的边界 |
| --- | --- | --- |
| `GameBootstrap` 中单份 FarmField、FarmInventory、GameClock 和 FarmSimulation | 它们是共享小镇世界，不是居民私有状态；Unity 已是权威来源。 | 共享修改必须记录 actor，并在单一领域提交边界原子执行。不要为每名居民复制世界。 |
| `NpcPlanExecutor` 的单 current action 与顺序队列 | 每居民一份后可支持“一居民至多一个动作”。 | 必须绑定 ResidentId，并与会话活动槽、ReservationService 和事件 actor 联动。 |
| 动作开始/完成前的本地前置条件复核 | 能阻止部分非法世界修改并保护现有种田流程。 | 不能替代任务所有权、交互点预约、版本检查或共享资源的原子提交。 |
| Python `StrictSchema` 与 Unity 精确字段/语义校验 | 已形成双端拒绝额外字段和无效枚举的基础。 | V2 schema 必须加入关联字段，并把模型输出收敛为白名单高层 intent。 |
| Remote→Local fallback 与本地确定性 planner | 是“网络不可用仍可活动和种田”的基础。 | fallback 必须覆盖每名居民的 Persona、日程、双人会话和所有 V2 AI operation。 |
| 有界 MemoryStore、表达历史和世界事件 HUD | 已有限长与基础观测能力。 | 必须增加 owner、可见性、来源链和每居民独立游标；HUD 只读投影。 |

## 6. 迁移停止条件

任一阶段出现以下情况时，不得继续启用更多居民或远程 AI：

- 空、重复或未知 `ResidentId`；
- 任何跨居民私有 Memory、Reflection、Goal 或 Schedule 读取；
- 一名居民同时存在第二个 active action，或同时动作和会话；
- 会话中的居民进入另一场会话，或第一版出现三人以上会话；
- 一个 InteractionPoint 同时存在两个有效 owner；
- AI 请求缺 owner/request/version，或绕过共享 AiRequestCoordinator；
- 从 `Update()` 调用栈启动远程请求；
- stale response 写入 UI、Memory、Goal、Conversation 或世界状态；
- 模型输出直接指定位置、时间、库存、农田状态、关系值、日程、存档或低层动作；
- 网络关闭后任一居民停止本地活动/对话，或现有完整胡萝卜种田闭环失败。

## 7. 复核入口

后续实现审计至少应重复以下只读检索，并结合自动化测试判断，不以“出现了新类名”代替行为证据：

- 搜索 `ResidentId` 是否贯穿 runtime、memory、goal、schedule、action、event、conversation、reservation、save 和 AI request/response；
- 搜索全部静态字段，确认不存在可变 `CurrentResident`、全局 pending、全局 memory 或关系状态；
- 搜索 `FindObjectOfType`、`GameObject.Find` 等唯一对象查找，确认运行时没有按类型/名字绑定居民；
- 追踪全部 `IAiGatewayClient` / transport 调用栈，确认只有 `AiRequestCoordinator` 可达且不来自 `Update()`；
- 通过失败测试证明重复 ID、私有记忆串读、双会话、同点双占、非法模型输出、超时和 stale 回包均被拒绝。
