# Town V2 多居民小镇架构

> 状态：目标架构，尚未实现。  
> 本文只定义边界、数据所有权和迁移方向，不表示当前 Unity 场景或 Python 网关已经具备这些能力。  
> 当前差距与逐文件证据见 `SINGLE_NPC_ASSUMPTIONS_AUDIT.md`。

## 1. 固定范围

Town V2 在不破坏现有单 NPC 胡萝卜种田闭环的前提下，引入四名固定居民：

| ResidentId | 显示名 | Persona 配置键 |
| --- | --- | --- |
| `resident-001` | 芽芽 | `persona-yaya` |
| `resident-002` | 阿木 | `persona-amu` |
| `resident-003` | 小穗 | `persona-xiaosui` |
| `resident-004` | 墨墨 | `persona-momo` |

`ResidentId` 是稳定、不透明、唯一且不可修改的身份键。显示名只用于 UI 和台词，不参与查找、权限、持久化关联、关系边或 AI 请求归属。

固定产品边界如下：

- 四人使用同一个 Unity `AiRequestCoordinator`、同一个 AI gateway client、同一个 Python 网关进程和同一份 provider/model 配置。
- 四人分别拥有独立 Persona、`ResidentRuntimeState`、Memory、Schedule 和 Relationships。
- 第一版 Conversation 恰好只有两名不同居民，不实现三人或四人群聊。
- 支持信息 A→B→C 传播，但 B、C 不得读取前一位居民的私有 Memory。
- 只实现一个四人共同参加的全镇活动；该活动是公开领域活动，不是群聊。
- 保留现有 3×3 胡萝卜播种、浇水、施肥、除草、成熟和收获流程及确定性本地 fallback。
- Python 网关完全关闭时，四人仍可执行本地日程、农事动作和双人本地对话。
- 不增加经济、恋爱、战斗、多作物或复杂职业系统。

## 2. 权威边界与总原则

Unity 是以下状态的唯一权威来源：

- 位置、导航、游戏时间、农田、共享演示背包；
- ResidentDefinition 的注册关系和每名居民的 RuntimeState；
- 私有记忆的 owner 与来源、日程、关系边；
- 动作、会话、全镇活动、交互点预约；
- AI 请求状态、预算、版本和结果是否仍可应用；
- 存档及恢复。

Python 网关是可移除、无 Unity 写权限的生成服务。它接收经过权限过滤的定长只读上下文，只返回严格 schema 内的候选高层 intent、有限台词、情绪/表达或反思文本。模型永远不能直接修改位置、时间、库存、农田、关系数值、日程或存档。

核心数据流：

~~~text
玩家输入 / 游戏时钟离散事件 / 本地感知事件
                    |
                    v
             TownEventBus
          /         |          \
         v          v           v
 ResidentRegistry  TownScheduler  ConversationCoordinator
         |          |           |
         |          +-----+-----+
         |                |
         v                v
 私有状态/记忆       本地高层 intent 请求
                          |
                          v
                 AiRequestCoordinator
                   |              |
                   v              v
          确定性本地 fallback   唯一共享 AI client
                                      |
                                      v
                            单一 Python 网关/模型配置

所有候选 intent -> 本地 schema/语义校验 -> TownScheduler/ConversationCoordinator
                -> ReservationService -> Unity 权威领域命令 -> 状态与可见事件
~~~

任何组件都不得通过可变全局 `CurrentResident` 路由。UI 的选中居民只是 ViewState，所有领域调用仍必须显式携带 `ResidentId`。

## 3. 强制不变量

| 不变量 | 强制机制 |
| --- | --- |
| 每名居民有稳定唯一 ResidentId | ResidentRegistry 在启动和读档时拒绝空、未知和重复 ID；所有引用以 ID 保存。 |
| 私有数据按居民隔离 | Runtime、Memory、Goal、Schedule、Reflection、AI request 均绑定 owner ID；Relationships 绑定有向 ID 对。 |
| 不存在可变全局当前居民 | 公开 API 要求显式 ID；UI selection 不进入领域状态。 |
| 私有记忆不可跨居民读取 | MemoryStore 创建时绑定 owner；提示构建器只接受该 owner 的受限 MemoryView。 |
| 知识来源受限 | 只有 Perception、Conversation、PlayerInput 或 PublicTownEvent 可以创建 KnowledgeRecord，且必须有来源 ID。 |
| 每居民至多一个动作和一个会话 | 共享活动租约原子检查。V2 采用更严格的单活动规则：动作与会话互斥。 |
| 会话居民不能进入另一会话 | ConversationCoordinator 开始会话时原子占用双方；任一方已有活动即失败关闭。 |
| 一个交互点最多一个 owner | ReservationService 使用单一有效 lease 和 fencing token。 |
| 所有远程调用走共享入口 | 只有 AiRequestCoordinator 能持有并调用共享 AI client/transport。 |
| 不从 Update() 启动网络 | Update 只推进本地模拟或发布本地事件；长驻异步 dispatcher 在启动时建立并消费队列。 |
| 模型只选允许的高层 intent | 请求携带本次 AllowedIntentSet；响应必须同时属于全局版本化白名单和该请求子集。 |
| 模型不直接改变世界 | 有效响应仍只是 proposal；只有 Unity 确定性处理器能提交领域命令。 |
| 所有模型输出严格校验 | Python 先校验，Unity 再校验关联字段、额外字段、枚举、长度、版本和语义前置条件。 |
| 每条远程路径有确定性 fallback | operation 注册时必须同时注册本地处理器；缺 fallback 的 operation 不得入队。 |
| 双人会话有硬上限 | V2 基线最多 6 个已交付话轮且最多 30 秒单调墙钟时间，先到者立即结束。 |
| stale 请求不能写回 | 状态失效时尽力取消；应用前版本不匹配则 `IgnoredStale`，不产生任何领域或 UI 写入。 |

### 3.1 单 NPC 危险模式的强制替代

| 禁止模式 | 唯一允许的目标结构 |
| --- | --- |
| 可变 `static CurrentNpc` / `CurrentResident` | 所有领域入口显式传 `ResidentId`；ResidentRegistry 是显式注入的实例服务，UI selection 仅是 ViewState。 |
| `FindObjectOfType<NpcBrain>`、按名字或 tag 找唯一 NPC | 居民场景对象携带序列化 ResidentId，在 composition root 注入的 ResidentRegistry 中注册；重复或未知 ID 失败关闭。 |
| 单个 `CurrentMemory` | 四个绑定 `OwnerResidentId` 的 MemoryStore；查询仅返回 owner/requester 校验后的 MemoryView。 |
| 全镇单个 `ActiveConversation` | ConversationCoordinator 同时维护 session-by-ID 和 resident-to-active-session 索引；会话恰好两人，互不共享参与者的两场会话可以并存。 |
| 单个居民 `CurrentGoal` 兼作全镇目标 | 居民 Goal 按 ResidentId/version 保存；公开 TownGoal 是独立实体，由 TownScheduler 产生带 assignee 的子任务。 |
| `SaveData.NpcState` 或其他单数 NPC 状态 | 版本化 TownSaveData：一份 sharedWorld 加恰好四条按 ResidentId 校验的 residents 记录，以及独立关系边。 |
| HUD/气泡直接引用固定芽芽 | HUD 从 registry 读取四居民投影；每个气泡绑定自己的 ResidentId；私人命令必须显式目标 ID。 |

这些替代规则不允许通过兼容层重新暴露无 owner 的单数属性。若旧接口在渐进迁移期仍需存在，它只能是显式绑定 `resident-001` 的只读适配器，并且不得被第二名居民、存档 V2、会话或 AI 请求路径调用；启用第二名居民前必须删除该适配器。

## 4. 数据所有权

### 4.1 共享小镇状态

以下对象继续只保存一份：FarmField、FarmInventory、GameClock、FarmSimulation、公共事件流、AI gateway 配置及 AI 请求协调器。共享不等于无所有权：每次居民引起的修改仍记录 `ActorResidentId`，并通过 Unity 的单一领域提交边界串行验证和提交。

共享背包只是现有农场演示资源，不扩展为经济系统。居民没有独立货币或交易库存。

### 4.2 居民私有聚合

每个 `ResidentId` 对应且只对应一个私有聚合：

- 一份 ResidentDefinition 引用；
- 一份 ResidentRuntimeState；
- 一份绑定 owner 的 MemoryStore 和观察进度；
- 一份独立 ResidentSchedule；
- 一组从该居民视角出发的 RelationshipState；
- 当前目标、动作执行器和表达/反思历史；
- 该居民的 request epoch 和状态版本。

禁止返回可被任意调用者修改的全局居民字典。跨居民读取必须经过明确授权的公共视图；私有 Memory、私人玩家输入、个人 Goal/Reflection 不属于公共视图。

## 5. 核心组件定义

### 5.1 ResidentDefinition

`ResidentDefinition` 是不可变的设计期定义，不保存运行时数值。每名固定居民有一份，至少包含：

| 字段 | 约束 |
| --- | --- |
| `ResidentId` | 必填、稳定、唯一；与显示名分离。 |
| `DisplayName` | 芽芽、阿木、小穗、墨墨；可用于呈现，不是 key。 |
| `PersonaProfileId` | 四份独立配置键；不允许缺省回退为芽芽。 |
| Persona 内容 | 角色背景、性格、说话风格、偏好、禁区和本地表达模板键；有严格长度上限。 |
| `SpawnPointId` | 场景中的稳定出生点 ID，不直接保存 Transform 引用到领域数据。 |
| `DefaultScheduleId` | 该居民自己的初始日程模板；实例化后进入独立 ResidentSchedule。 |

四份 definition 可以使用同一模型，但 Persona 上下文必须从请求 owner 的 definition 构建。Definition 不包含 model 名、API key、base URL 或 provider 选择，防止形成每居民模型配置。

Definition 的修改走内容版本；存档记录 definition ID/version，不以内嵌提示文本覆盖当前定义。

### 5.2 ResidentRuntimeState

`ResidentRuntimeState` 是单一居民的可变运行时状态，构造时必须显式绑定 `ResidentId`，且 owner 终身不变。至少包含：

- `ResidentId` 和 `ResidentStateVersion`；
- `RequestEpoch`，用于使旧 AI 请求失效；
- 当前本地状态（Idle、Moving、Acting、Conversing、AttendingTownActivity 等）；
- 当前 GoalId/GoalVersion；
- 可选 `ActiveActionId` 和对应 activity lease；
- 可选 `ActiveConversationId`；
- 当前位置的权威场景引用映射或稳定位置快照；
- 当前 mood/expression 及独立反思历史；
- 存档恢复所需的动作意图，不保存网络 coroutine 或请求 callback。

Memory、Schedule 和 Relationships 虽属于该居民，但由专门容器管理，RuntimeState 只保存稳定关联或只读投影，避免同一可变数据有两个权威副本。

任一会影响 AI 结果适用性的状态变化都会递增 `ResidentStateVersion` 或 `RequestEpoch`。加载存档、销毁居民、替换目标、结束会话和重新开始 Demo 必须使相关在途请求失效。

### 5.3 ResidentRegistry

`ResidentRegistry` 是四居民身份和运行时聚合的唯一注册入口，但不是可变全局 Singleton。它由场景 composition root 创建并显式注入依赖者。

职责：

- 在启动时注册且只注册四个固定 ResidentId；
- 校验 definition、runtime、Memory owner、Schedule owner 和场景表现对象的 ID 一致；
- 通过显式 `ResidentId` 解析只读 definition、runtime view 或受限服务句柄；
- 提供稳定排序的四居民目录，排序键为固定配置顺序再以 ResidentId 兜底；
- 读档时在临时 registry 中验证完整集合后原子替换；
- 拒绝重复、未知、空 ID 以及显示名查找。

非职责：

- 不保存 `CurrentResident`；
- 不替 TownScheduler 做日程/动作决策；
- 不允许一个居民读取另一居民的 MemoryStore；
- 不直接调用 AI 或修改共享世界。

### 5.4 TownScheduler

`TownScheduler` 是本地、确定性的城镇日程与工作调度器。它管理 `Dictionary<ResidentId, ResidentSchedule>`，而不是一份隐式“NPC 日程”。

职责：

- 根据 GameClock 的离散时间边界触发日程项，保证同一边界只触发一次；
- 将公开完整农田目标拆为 Unity 本地 FarmTask，并按固定规则分配给四名居民；
- 在居民空闲、活动租约可得、前置条件成立且交互点可预约时才下发动作；
- 调度唯一全镇活动，并为四名居民创建各自的 attendance 任务；
- 使用稳定排序处理同一 tick 的竞争，不依赖字典遍历或网络完成顺序；
- 在网络不可用、AI 预算不足或响应无效时继续使用本地规则。

V2 基线优先级：

1. 让已开始的原子动作或合法话轮完成；
2. 处理已经到期且四人共同参加的全镇活动；
3. 继续已接受的完整胡萝卜农务子任务；
4. 处理该居民仍有效的个人日程；
5. 合法的双人对话提案；
6. Idle。

AI 可以在本次明确允许的高层 intent 中提出 `AttendTownActivity`、`RequestConversation` 或 `Idle`，但不能创建/修改日程、分配地块、改变优先级或跳过本地前置条件。

### 5.5 SocialGraph

`SocialGraph` 保存四名居民的独立 Relationships。关系采用有向边：

`RelationshipKey = (OwnerResidentId, OtherResidentId)`

因此“芽芽如何看阿木”和“阿木如何看芽芽”是两条不同边；不允许 self edge，四居民最多有 12 条有向边。

`RelationshipState` 保持第一版最小化，只包含：

- owner、other 和 relation version；
- 有界 `Familiarity`；
- 有界 `Trust`；
- 最后一次确定性变化的领域事件 ID。

不包含恋爱、婚姻、交易、阵营或职业树。关系变化只能由 Unity 的确定性规则根据已完成的合法会话、共同活动或明确公共事件产生；模型可以读取 owner 自己的有界关系摘要，但不能返回或写入关系数值。

SocialGraph 的查询必须显式给出 owner ID。AI 上下文只能包含该 owner 的 outgoing edge 受限视图，不能把另一居民的私人看法当作 owner 已知事实。

### 5.6 ConversationCoordinator

`ConversationCoordinator` 是所有双人会话的唯一创建、推进和结束入口。第一版不允许绕过它直接显示“对话结果”。

`ConversationSession` 至少包含：

- 稳定 `ConversationId`；
- 恰好两个不同 `ParticipantResidentIds`；
- `TurnOwnerResidentId`、已交付 `TurnNumber`；
- `ConversationVersion`；
- 单调墙钟 `StartedAt` / `DeadlineAt`；
- 状态 `Proposed -> Active -> Completed | TimedOut | Cancelled | Rejected`；
- 结束原因和已交付 utterance/event ID 列表。

开始规则：

- 按规范化 ResidentId 顺序原子获取双方活动租约，避免死锁；
- 任一方已有动作、会话或全镇活动任务时，本次提案失败且不留下半占用；
- 会话只占用这两名居民，不影响另外两名居民；
- 同一居民不得同时加入第二场会话。

推进与结束规则：

- 每交付一名居民的一次发言计 1 turn；第 6 个 turn 交付后立即结束；
- 从 Active 起 30 秒单调墙钟超时，游戏暂停或倍速不改变该安全上限；
- 远程失败、超时、无效输出或预算拒绝时，本回合使用该 speaker Persona 的确定性本地模板；
- 结束时取消/忽略全部关联 AI 请求，递增 session version，幂等释放双方活动租约；
- 晚到回复若 conversation/version/turn/deadline 不匹配，不显示、不写记忆、不增加 turn。

对话内容仍受 schema 和知识权限限制。模型只能从本次显式允许的 shareable fact ID 和高层 intent 中选择，不能读取参与者完整 MemoryStore。

### 5.7 AiRequestCoordinator

`AiRequestCoordinator` 是 Unity 中所有远程 AI 路径的唯一入口。整个小镇只有一个 coordinator、一个共享 `IAiGatewayClient` 和一份只读 `AiGatewayConfig`；配置包含 gateway URL、provider/model 选择、timeout 和预算，但不按居民覆盖。

请求来源只能是明确领域事件：

- 玩家提交公开或私人命令；
- TownScheduler 请求一次合法高层决策；
- ConversationCoordinator 请求当前合法 speaker 的下一话轮；
- 明确的表达或目标完成反思事件。

`Update()`、executor tick 和 UI 刷新只能发布本地事件，不得直接创建 coroutine、HTTP 请求或调用 transport。长驻 dispatcher 在 bootstrap 时启动，随后从队列取件。

每个 `AiRequestEnvelope` 至少包含：

~~~text
RequestId
Operation
ResidentId
ResidentStateVersion
RequestEpoch
WorldSnapshotVersion
GoalId + GoalVersion（可选）
ConversationId + ConversationVersion + TurnNumber（可选）
AllowedIntentSet
AllowedEntityIds / AllowedFactIds（按 operation 可选）
SchemaVersion + PromptVersion
CreatedAtMonotonic + DeadlineAtMonotonic
权限过滤且定长的只读上下文
确定性 fallback key
~~~

协调器职责：

- 按 ResidentId 公平排队，实施全局和每居民并发限制；
- 统一预算、deadline、缓存、拒绝原因和 Remote/Local 来源遥测；
- 入队、派发前、响应到达和应用前四次检查 owner 与版本；
- 状态失效时取消；传输无法取消时标记 ignore；
- schema/语义失败、网络失败、超时、预算不足均且只执行一次确定性 fallback；
- 绝不记录 API key、完整私有 Memory 或未经裁剪的对话原文。

Python 网关收到并校验关联字段后必须原样回显，但不保存权威居民状态。服务端内部 request ID 或 provider request ID 只用于遥测，不能替代 Unity 生成的端到端 RequestId。

### 5.8 ReservationService

`ReservationService` 管理所有具有排他占用要求的世界交互点，例如农田操作点、对话站位和全镇活动席位。

一个 `ReservationLease` 至少包含：

- 稳定 `InteractionPointId`；
- owner `ResidentId`；
- `ActivityId` / `ActionId`；
- lease generation 和不可复用的 fencing token；
- 获取时间与到期时间；
- 状态 `Active | Released | Expired`。

规则：

- 同一 InteractionPointId 任意时刻最多一个有效 owner；
- `TryAcquire`、`Renew`、`Validate` 和 `Release` 原子且幂等；
- 导航前获取，抵达和世界提交前再次验证 fencing token；
- 动作完成、失败、取消、居民禁用、场景卸载和读档均释放；
- 旧 token 在 lease 到期或新 generation 创建后永久无效；
- 同时争用按固定业务优先级、入队序号、ResidentId 排序，失败者本地等待或重规划，不请求模型裁决。

全镇活动为四名居民配置四个不同 attendance point，不能让四人共同预约同一个点。双人会话同理使用两个配对站位或不需要排他站位的明确配置。

## 6. 事件可见性与记忆

目标 `TownEventBus` 发布不可变 `TownEventEnvelope`：

| 字段 | 含义 |
| --- | --- |
| `EventId` | 全局稳定且唯一，用于记忆来源和去重。 |
| `Kind` / `GameTime` / `WorldVersion` | 事件类型、权威游戏时间和世界版本。 |
| `ActorResidentId` | 可空；居民行为必须填写。 |
| `SubjectIds` | 受影响地块、活动、会话或居民的稳定 ID。 |
| `Visibility` | `Private`、`Conversation`、`Perceivable` 或 `PublicTownEvent`。 |
| `AudienceIds` | 私人 owner、会话参与者或公开四居民集合。 |
| `ObserverResidentIds` | 对 Perceivable 事件由 Unity 本地感知规则计算的实际观察者。 |
| `Correlation/CausationId` | 关联动作、会话、请求或来源事件。 |
| `Payload` | 类型化且有界的数据；UI 文本不是权威事实。 |

事件不能因为“发生在世界里”就自动公开。KnowledgeProjectionService 先验证 visibility 和实际 observer，再写入目标居民的 MemoryStore。

每条 `KnowledgeRecord` 至少记录：

- `OwnerResidentId`；
- `FactId`；
- `SourceKind`；
- `SourceEventId`；
- `ImmediateSourceResidentId`（对话学习时）；
- `RootFactId` / `ParentKnowledgeId`（传播链需要时）；
- `LearnedAtGameTime`；
- 结构化、有界事实和重要度。

私有 Memory 的原文不得写入公共日志、跨居民缓存键或其他居民提示。

## 7. A→B→C 信息传播

信息传播通过实际已交付话轮发生，不通过 MemoryStore 直连。

示例流程：

1. A 通过合法 Perception、PlayerInput 或 PublicTownEvent 得到 `Fact-X`；只有 A 的 MemoryStore 写入 owner=A 的记录。
2. A 与 B 的双人会话中，ConversationCoordinator 只把 A 当前允许分享的 fact ID 暴露给 A 的话轮生成路径。
3. A 的话轮实际交付后，TownEventBus 发布 `Conversation(participants=A,B)` 事件；B 写入一条 owner=B、immediate source=A、root fact=Fact-X 的记录。
4. B 之后与 C 建立另一场双人会话。只有 B 已知且仍允许分享的 Fact-X 可进入本次 AllowedFactIds。
5. B 的话轮交付后，C 写入 owner=C、immediate source=B、root fact=Fact-X 的记录。

C 从未读取 A 或 B 的 MemoryStore；C 只知道 B 实际说出的内容。若第一场话轮被取消、超时或 stale，B 不学习；若第二场未实际交付，C 不学习。远程模型不可用时，本地模板与相同的 provenance 规则仍完成传播。

## 8. 现有胡萝卜流程的四居民调度

公开玩家目标仍是“完成 3×3 农田的胡萝卜全周期”。模型最多选择是否接受该高层 intent；Unity 决定所有地块和动作。

参考确定性 owner 规则为：

`ownerIndex = (plotNumber - 1) mod 4`

| 居民 | 固定地块 |
| --- | --- |
| 芽芽 / `resident-001` | 1、5、9 |
| 阿木 / `resident-002` | 2、6 |
| 小穗 / `resident-003` | 3、7 |
| 墨墨 / `resident-004` | 4、8 |

同一轮完整目标期间 owner 不变；AI 不得改派。TownScheduler 保留现有动作和本地前置条件：

1. 播种；
2. 浇水；
3. 施肥；
4. 等待本地时间触发杂草/生长；
5. 除草；
6. 成熟后收获。

每个阶段内可由不同居民并行，但共享背包扣减、地块状态变更和收获增加必须在 Unity 领域提交边界串行、原子完成。居民导航前必须持有目标交互点的有效 reservation。

现有回归合同保持：

- 九块地仍只种胡萝卜；
- 初始演示背包 `种子/水/肥料/胡萝卜 = 9/9/9/0`；
- 成功完成后为 `0/0/0/9`；
- 动作完成前仍复核权威前置条件；
- 远程 AI 关闭、超时、预算耗尽或输出无效时，动作顺序和最终世界结果仍由本地规则完成；
- 旧单 NPC 完整闭环测试继续保留，另加四居民等价结果测试，防止“多居民成功但种田退化”。

## 9. 唯一全镇活动

第一版只配置一个 `TownActivityDefinition`，参考活动为“全镇收获聚会”：

- `ActivityId` 固定且稳定；
- required participants 恰好为四个固定 ResidentId；
- 四名居民各自在自己的 ResidentSchedule 中保存同一 ActivityId 的 attendance 条目；
- 活动开始事件是 `PublicTownEvent`，四人都可合法学习；
- TownScheduler 等待四人都可取得活动租约后原子开始；不会中断已开始的原子动作或会话；
- ReservationService 为四人分配四个不同 attendance point；
- 活动结束由本地时钟和规则决定，不需要远程 AI；
- 网络不可用时照常集合、参加和结束。

这不是四人 Conversation。活动期间可以显示确定性公共表达，但不创建三人/四人话轮、群聊 Memory 或群聊 AI 请求。

## 10. AI 契约

### 10.1 高层 intent 白名单

V2 全局可执行 intent 保持最小：

- `CompleteCarrotLifecycle`；
- `AttendTownActivity`；
- `RequestConversation`；
- `ContinueConversation`；
- `ShareKnownFact`；
- `Idle`。

每个 operation 只开放更小子集，并附 AllowedEntityIds/AllowedFactIds。例如农田命令解释不能返回对话 intent，会话回合不能返回农田动作。表达和反思使用独立非执行 schema。

响应不允许包含：

- 世界坐标、路径、动画或低层动作序列；
- 地块分配或农田状态写入；
- 时间推进或日程增删改；
- 背包增减；
- 关系数值写入；
- SaveData 内容；
- 任意脚本、工具名或未声明字段。

### 10.2 校验与应用

Python 严格 schema 校验后，Unity 再执行：

1. JSON 大小、精确字段和字符串长度检查；
2. schema/prompt 版本检查；
3. RequestId、ResidentId、state/epoch/world version 精确匹配；
4. intent 同时属于全局及本请求白名单；
5. 可选目标 resident、fact、conversation、turn 和 reservation 仍在允许集合；
6. 本地语义前置条件仍成立。

通过校验的结果也只能创建本地 proposal。TownScheduler、ConversationCoordinator 或确定性 IntentHandler 决定是否形成领域命令；模型响应本身没有写权限。

### 10.3 确定性 fallback

| Operation | 本地 fallback |
| --- | --- |
| 玩家农田命令解释 | 现有白名单本地解释器；不支持则明确拒绝。 |
| 居民日程/活动决策 | 按 ResidentId、Schedule、公共任务和固定优先级决定。 |
| 双人会话话轮 | 按 speaker ResidentDefinition、turn、允许事实和稳定模板生成或结束。 |
| 表达 | 按 owner Persona、事件种类和稳定 variant 选择模板。 |
| 反思 | 只读取 owner 私有 MemoryView 与合法公开事实，生成有界本地模板。 |

相同权威快照、输入、ResidentId 和版本必须得到相同 fallback 结果，不使用墙钟随机数或网络完成顺序作为种子。

## 11. 存档与恢复

Town V2 必须升级存档版本，概念结构为：

~~~text
TownSaveData
  sharedWorld
    clock
    farm
    inventory
    simulation
    townActivityState
  residents[4]
    residentId
    definitionId + definitionVersion
    runtimeState
    privateMemories
    goals
    schedule
    recoverableActionIntent
  relationships[12 directed edges]
  deliveredConversationSummaries / provenance
~~~

规则：

- `residents[]` 必须恰好包含四个固定且唯一 ID；数组顺序不承担身份语义；
- 私有 Memory 记录的 owner 必须等于其居民条目，来源和跨引用必须合法；
- Schedule 条目和关系边必须引用已注册 ID；
- 共享世界只保存一份；
- 不持久化 API key、AI client、coroutine、取消令牌、在途请求或有效 reservation lease；
- 读档关闭未完成会话，保留已经交付的话轮/provenance，清空旧预约；
- 读档递增四名居民 RequestEpoch，使读档前回包全部 stale；
- 未完成动作只保存可恢复意图，加载后经 TownScheduler 和 ReservationService 重新验证；
- 先在临时模型完整验证，再原子替换；任何重复 ID、跨居民私有引用或非法状态都使整次加载失败，不做部分应用。

## 12. UI、场景与可观察性

场景 composition root 只创建一次共享服务，并创建四个带序列化 ResidentId 的居民表现根对象。居民对象启动时向 ResidentRegistry 注册；不得使用显示名、`GameObject.Find` 或 `FindObjectOfType` 解析“唯一 NPC”。

HUD 至少提供四居民摘要：

- ResidentId + 显示名；
- 当前本地状态、动作、会话对象；
- Schedule 当前项；
- reservation；
- AI request 状态及 Remote/Local/fallback/stale 结果。

选中一行只改变详情投影，不改变领域路由。详情只能读取选中 owner 的受限 Memory、Persona、Schedule 和 outgoing Relationships。玩家输入必须显式选择“私人给某 ResidentId”或“公开全镇事件”；不能依赖当前选中项隐式决定私有数据 owner。

每个头顶气泡绑定自己的 ResidentId，只显示该居民当前合法表达或已交付会话话轮。会话面板显示 ConversationId、双方、turn、deadline 和结束原因，便于证明双人限制。

## 13. 离线行为

Python 网关未启动、网络断开、请求超时、预算耗尽或 schema 无效时：

- TownScheduler 继续推进四份独立 Schedule；
- 四名居民继续本地导航、农事和 Idle 行为；
- ConversationCoordinator 使用双方 Persona 的本地模板完成或礼貌结束双人会话；
- A→B→C 仍通过实际本地话轮和相同 provenance 传播；
- 全镇活动按本地时钟开始/结束；
- 完整胡萝卜流程仍达到既定最终农田与背包状态；
- HUD 对每次请求显示明确 fallback 原因，不把 fallback 伪装为 Remote。

离线不是降级到“只剩芽芽”；四名居民的身份、私有状态、关系和日程必须继续存在。

## 14. 迁移顺序

1. **V2-0 契约与身份**：冻结四个 ResidentId、事件 visibility/observer、AI request/response envelope、高层 intent 白名单和 save v2 规则。场景暂时仍运行芽芽。
2. **V2-1 居民数据隔离**：引入 ResidentDefinition、ResidentRuntimeState 和 ResidentRegistry；让现有芽芽先通过 registry 运行且行为不变，再注册另外三份聚合。Memory、Goal、Schedule 和 Reflection 按 ID 隔离。
3. **V2-2 本地小镇协作**：实现 TownScheduler、SocialGraph、ConversationCoordinator、ReservationService、事件投影、A→B→C 和唯一全镇活动。全阶段先保持 Python 网关关闭。
4. **V2-3 共享 AI**：用单一 AiRequestCoordinator 接管全部远程入口，移除 Update 调用链的网络启动，加入公平队列、预算、取消、deadline 和 stale guard；再扩展 Python/Unity 双端 schema。
5. **V2-4 持久化与呈现**：升级存档、场景生成器、四居民 HUD/气泡和自动化测试；旧完整种田回归与 V2 回归同时通过后才能冻结演示。

任一步都不得用复制 ReplanController、引入全局 CurrentResident、扩大模型写权限或临时跨读 Memory 作为兼容方案。

## 15. 架构验收门槛

以下事实全部有自动化或可重复场景证据时，Town V2 才可称为实现：

- registry 中恰好存在芽芽、阿木、小穗、墨墨四个稳定唯一 ID，改显示名不改变关联；
- 四份 Persona、RuntimeState、Memory、Schedule 和 outgoing Relationships 独立保存/恢复；
- 任意居民不能读取或进入另一居民的私有 Memory 上下文；
- A→B→C 只能在两场实际双人会话的已交付话轮后成立，且 provenance 可追溯；
- 会话始终只有两人，同一居民无第二场会话，6-turn/30 秒限制和释放规则成立；
- 四人能参加唯一全镇活动，活动公开但不产生群聊；
- 每居民至多一个 active action；动作与会话互斥；
- 一个交互点始终最多一个有效 owner，旧 fencing token 不能提交；
- 全部远程调用只经单一 AiRequestCoordinator 和同一 gateway/model config；
- `Update()` 及其调用链不启动远程请求；
- 非法 intent、额外字段、直接状态修改和越权 fact 全部被拒绝且世界状态不变；
- stale response 应用数为零；
- Python 网关关闭时四人仍活动、本地双人对话、完成信息传播和全镇活动；
- 现有 3×3 胡萝卜完整流程不退化，成功终态仍为共享背包 `0/0/0/9`；
- 未出现经济、恋爱、战斗、多作物或复杂职业系统。
