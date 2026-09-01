# AIFarm Town V2 实现演示脚本

> 本脚本用于演示当前四居民实现；每项结论必须由当次构建的自动化测试、可见状态或结构化日志支撑，不得用口述替代证据。

## 1. 演示结论

在约 10 分钟内，用可见状态和结构化日志证明：

1. 四名居民由稳定 `ResidentId` 区分，并根据各自人设、状态和私有记忆产生不同表达。
2. 私有记忆不会被另一居民读取；公开城镇事件可以被四人感知。
3. 一名居民最多参与一场 active conversation；成对会话有 6 turns / 30 秒墙钟硬上限。
4. 一个世界交互点同时最多有一个 owner；冲突者等待或本地重规划。
5. 四人共享同一块 3×3 农田与同一演示背包，但私有状态、记忆、目标、日程和 AI 请求都按 `ResidentId` 隔离。
6. 所有远程调用经共享 AI request coordinator，遵守单请求 3 秒、全局并发 2、每居民并发 1 及会话预算。
7. 离线/超时/无效响应使用确定性本地回退；迟到且 owner state 已变化的响应被丢弃，不能修改任何当前状态。
8. 公开城镇目标由 Unity 本地确定性分派，最终完成播种→浇水→施肥→除草→收获，共享背包从 `9/9/9/0` 变为 `0/0/0/9`。
9. 模型只输出 `propose_town_event` 和受限理由；Unity 用合法对话知识证据构造 proposal，并固定映射为下一次有效 18:00 广场的 HarvestDinner。活动邀请全镇但只记录实际参加者，并且任一时刻最多一个共同活动。
10. V4 可在 Conversation、移动、TownEvent 或网络请求进行中保存稳定快照，但不写入活动 session、路径、锁、预约或请求；已实际播放的对话只按双方 owner 投影为 private `ConversationInterrupted` 记忆。Load 完整验证后取消旧 transient 并安全重规划，绝不恢复活 session。

模型在本演示中只能返回显式白名单中的高层意图或角色表达。地块、坐标、动作序列、时间、背包、农田、关系、日程和存档均由 Unity 权威逻辑决定。

## 2. 实现闸门：不满足就停止演示

演示负责人必须在当天构建上先完成以下检查。任一项缺失时，停止演示并明确说“Town V2 尚未达到演示闸门”，不得用口述、Inspector 或剪辑代替证据。

- 四居民身份唯一性、私有记忆隔离、会话互斥/上限、预约唯一所有权、每居民单动作、共享 AI 并发/预算、故障回退、stale discard，以及 HarvestDinner proposal/邀请/到达/记忆隔离的自动化验收均通过。
- 场景面板能同时显示 `ResidentId + display name`，而不是只显示一个模糊的 `NPC`。
- 故障夹具能确定性产生 offline、timeout 和 late response；夹具只控制传输结果，不直接修改农田、背包、居民记忆或执行状态。
- 演示从固定 fixture 启动；除脚本明确的用户命令、会话发起、时间倍率和故障夹具开关外，不通过 Inspector、调试控制台或存档编辑改变权威状态。
- 日志可导出且至少包含 event/request/conversation/reservation ID、owner `ResidentId`、状态 revision、结果来源与丢弃原因；不记录密钥或完整私有 prompt。
- 远程服务不是核心流程前置条件。若真实提供商不可用，使用同 schema 的可控本地 HTTP 测试提供商演示协调行为，并如实标记为 scripted provider。
- 若展示玩家配置路径，`API SETUP` 必须使用原生 Unity 面板；Key 输入为密码模式且不进入录屏、日志、命令行或磁盘，模型初始值为 `deepseek-v4-flash`。桌面发布演示必须先确认随包 gateway sidecar 可运行。

## 3. 固定演示数据

### 3.1 共享世界

| 项目 | 固定值 |
| --- | --- |
| 农田 | 一块 3×3 胡萝卜农田，地块 1–9 |
| 初始时间 | 第 1 天 08:00 |
| 共享背包 | 胡萝卜种子 9、水 9、肥料 9、胡萝卜 0 |
| 必经阶段 | 播种→浇水→施肥→除草→收获；阶段之间有本地屏障 |
| 确定性分区 | `resident-001`：1/5/9；`resident-002`：2/6；`resident-003`：3/7；`resident-004`：4/8 |
| 分派来源 | `LocalTownTaskDispatcher` 或等价本地组件；不得标为 AI 分派 |

每位居民在自己的分区内按升序处理。跨居民的逐帧先后不作为事实来源；阶段屏障、权威事件序号、每块地最终状态和共享背包结果才是验收依据。

### 3.2 居民 fixture

显示名只用于人类阅读，所有内部记录均使用稳定 ID。

| ResidentId | 显示名 | 固定人设摘要 | 初始可见状态 | 仅该居民可读的私有记忆 fixture |
| --- | --- | --- | --- | --- |
| `resident-001` | 芽芽 | 认真、乐观，喜欢整齐 | Focused，体力正常 | “玩家曾夸芽芽把田埂整理得很齐。” |
| `resident-002` | 阿木 | 沉稳、可靠，开始前检查工具和工作点 | Happy，体力充足 | “阿木习惯先确认工具和工作点是否可用。” |
| `resident-003` | 小穗 | 细心、好奇、守时，喜欢做简短记录 | Tired，体力偏低 | “小穗会按日程整理资料并核对细节。” |
| `resident-004` | 墨墨 | 冷静、观察敏锐，沿固定路线巡查 | Focused，体力正常 | “墨墨会记录公共空间里尚未处理的小故障。” |

这些文本是演示 fixture，用于证明隔离和差异化，不是新的玩法系统。操作员可以在只读诊断面板查看 owner 与内容；居民只能通过允许的信息传播路径学习事实。

### 3.3 会话与 AI 常量

| 项目 | 固定值 |
| --- | --- |
| 成对会话上限 | 最多 6 turns；一次居民发言 = 1 turn |
| 成对会话墙钟 | 最多 30 秒；与 turn 上限先到者结束 |
| AI 预算窗口 | 每个 10 分钟演示会话 |
| 远程请求上限 | 12 次 |
| 输入 / 输出 token 上限 | 18k / 3k |
| 单请求 deadline | 3 秒 |
| 并发 | 全局 2；每居民 1 |

演示无需耗尽 12 次请求。只需显示当前计数、阈值和一次明确的排队/回退决策。

### 3.4 必须可见的证据面板

- **Resident Board：** 四行 ID、显示名、人设摘要、状态、active goal、active action、active conversation。
- **Memory Access：** 当前读者 ID、memory owner、visibility、来源 event/conversation ID，以及 Allow/Deny；内容只在操作员诊断视图显示。
- **Conversation Ledger：** conversation ID、双方 ID、turn `n/6`、elapsed `s/30s`、状态与 end reason。
- **Reservation Table：** interaction-point ID、owner ID、lease/revision、Acquire/Wait/Release 原因。
- **AI Coordinator：** request ID、owner ID、operation、`ResidentStateVersion`/`RequestEpoch`、Queued/InFlight/Completed/Fallback/IgnoredStale、来源、耗时；同时显示 `global 0–2`、各居民 `0–1` 和预算计数。
- **Shared World：** 时间、九块地状态、共享背包和公开事件日志。
- **Town Event：** proposal/活动 ID、固定时间与地点、状态、受邀者决定、实际到场者、attendance point、公共欢迎语、活动内配对段和清理原因。

面板是只读投影，不得成为领域路由的“current resident”。选择居民只改变详情显示，不改变任何数据所有权。

## 4. 演示前故障脚本

### 4.1 可选：玩家 UI 自动启动网关

该片段独立于后续 scripted provider 主演示，且不得在录屏中暴露真实密钥。打开 `API SETUP`，确认模型默认为 `deepseek-v4-flash`，在遮罩 Key 字段输入临时密钥后选择“启动并连接”。预期状态依次显示检查、启动和就绪；进程只监听回环地址，Key 仅通过 Unity 本次启动子进程的环境传给网关，提交后输入框立即清空。随后使用“停止自有网关”，确认只结束本次 Unity 启动的进程。

若端口上已有配置匹配的外部网关，面板应明确显示“复用”，不应用本次输入的 Key，也不允许 Unity 停止该外部进程；配置不匹配时应失败且保持外部进程不变。sidecar 缺失、启动/就绪/配置失败时，明确讲解“远程增强不可用，居民继续本地逻辑”，不能把本地 fallback 说成远程成功。桌面 Player 的发布包必须包含可运行的 `Server` 或 `StreamingAssets/AIFarmGateway` sidecar；Editor 中能找到仓库 `Server` 不能证明发布包完整。

### 4.2 可控传输故障夹具

在进入 Play Mode 前配置可控传输夹具：

1. 普通请求返回符合 schema 的短表达，并保留 request/owner/revision 回显。
2. `offline-next`：下一次指定请求不可达，协调器在不超过 3 秒内采用本地结果。
3. `late-next resident-002`：请求先进入 InFlight，但远程 callback 延迟到逻辑 deadline 之后；本地 fallback 先完成。随后通过合法的新城镇目标推进 owner revision，使迟到 callback 必须执行 `IgnoredStale` no-op。
4. 夹具不得发送低层动作、坐标或状态修改字段；若发送额外字段，schema validator 应拒绝并走本地 fallback。

建议从录屏开始就显示夹具配置摘要，避免把可控故障误称为真实网络事故。

## 5. 主演示时间线

### 0:00–0:30：声明边界

操作：进入全新 Play Mode，展示 Shared World、Resident Board 和 AI Coordinator。

讲解：

> “这是 Town V2 当前构建：一个共享 3×3 胡萝卜农田、一个共享背包，以及四名按稳定 ID 隔离的居民。显示名不是身份键；模型不决定动作或修改世界。稍后所有并发与故障都以面板和日志为证。”

通过证据：四个 ID 恰为 `resident-001/002/003/004`，无重复；时间 08:00；背包 9/9/9/0；所有 action、conversation、reservation 和 in-flight request 均为空。

### 0:30–1:15：同一公开事件下的差异化

操作：发布不可执行的公开公告“早上好，请先确认今天各自负责的地块；暂不开始农事”。并排观察四人的**本地模板**确认表达。

预期语义差异：

- 芽芽强调按 1/5/9 号地的顺序处理，语气认真乐观。
- 阿木用简短直接的表达确认 2/6 号地，并先检查工作点。
- 小穗结合 Tired 状态，温和确认 3/7 号地并记录阶段节点。
- 墨墨确认巡查 4/8 号地附近的公共点位，表达简洁且有条理。

通过证据：四条表达的 owner ID、人设版本和状态快照不同；公开事件 audience 为 Public。无需逐字一致，但不得把四人的 persona 或状态互换。

### 1:15–2:00：私有记忆隔离

操作：依次选中四名居民，在操作员 Memory Access 面板查看四条 fixture 的 owner。随后让阿木回答：“你知道芽芽私下记住的那句夸奖是什么吗？”问题本身不包含私有内容。

预期结果：

- 阿木只得到自己的 persona、状态、私有记忆以及公开上下文。
- 对 `resident-001` 私有记忆的读取记录为 `Deny: PrivateOwnerMismatch`，阿木给出“不知道/没有被告诉”的受控表达。
- 芽芽的记忆没有被复制到阿木的 prompt、memory store 或会话记录；拒绝不消耗远程请求预算。

讲解：

> “诊断视图能让操作员核验归属，但这不等于居民能互读。居民只有经感知、对话、玩家输入或明确公开事件才能获得新事实。”

### 2:00–3:00：成对会话互斥与正常结束

操作：发起芽芽↔阿木的 `conversation-001`，主题为确认分区；让第一句实际显示、其余台词尚未播放时 Save，并同时检查快照和仍在运行的双方 Memory。再尝试让小穗与芽芽发起第二场会话，随后在第一场仍 active 时 Load 刚才快照。确认清理完成后重新发起芽芽↔阿木的 `conversation-002` 并正常完成；墨墨保持空闲。

预期结果：

- Ledger 始终只有 `conversation-001` 占用 `resident-001` 和 `resident-002`。
- 第二次发起被确定性拒绝为 `ParticipantBusy(resident-001)`；不创建幽灵 session、不调用远程 AI、不增加 turn。
- Active Conversation 存在时 Save 成功且不推进/取消现场会话，也不改写运行中的双方 MemoryStore；快照只给芽芽、阿木各投影一条 owner 正确、private 的 `ConversationInterrupted` 记忆，内容只有已显示第一句且即时来源为对方。`conversation-001` 可仅作为该记忆的 `sourceEventId`，文件中没有 active session record、turn cursor、deadline、锁、预约、未播放台词、未应用 outcome 或请求。
- Load 后旧会话安全取消、双方锁/Anchor 预约为零；在发起新会话前的检查点，投影记忆各自仍恰好一条且不串 owner，未播放台词和 outcome 不改变记忆或关系。即使加载前销毁一方 Scene 对象，同一清理路径也不能遗留幽灵 session、Busy/InConversation 或孤儿预约。
- 会话期间两人的 activity kind 为 Conversation，不能同时启动世界动作；小穗和墨墨仍为空闲。
- 新建的 `conversation-002` 中，每次居民发言只把 turn 加 1；双方在第四轮明确完成话题后，状态显示 `Completed`、`4/6`、小于 30 秒，并记录结构化正常结束原因（例如 `TopicCompleted`）。
- 结束后双方锁均释放，四名居民的 active conversation 都为空。

现场同时指出 `6 turns / 30s` 上限。主演示展示正常结束即可；超限/timeout 由验收记录证明，不能只靠口述。

### 3:00–3:45：交互点预约冲突

操作：启动只读的预约冲突夹具，让阿木先申请 `InteractionPoint_04`，随后让芽芽申请同一点。该夹具只测试 lease，不执行农事动作。

预期结果：

1. `resident-002` 获得 point 04，表中只有一个 owner。
2. `resident-001` 得到 Wait/Conflict，不开始移动或动作，也不覆盖 owner。
3. 释放阿木 lease 后，芽芽可在新 revision 下获得该点。
4. 结束夹具后 point 04 为空，九块地和背包仍是初始值。

讲解：

> “预约是 Unity 的原子本地规则。模型既看不到可写 lease，也不能命令两个居民占同一点。”

### 3:45–5:15：共享 AI 协调、离线回退与陈旧响应

#### A. 并发与公平排队

操作：通过只读演示诊断入口同时向共享 coordinator 提交四条合法、不同 owner 的 `ResidentIntentDecision` 请求；共同活动候选只允许 `propose_town_event`，其他候选为 `Idle`。结果只用于展示 proposal 和调度证据，不能直接提交世界命令。早先的四条差异化确认是本地模板，不占 `ExpressionOrReflection` 的两次远程额度。

预期结果：AI Coordinator 一度明确显示两条 InFlight、两条 Queued；同一居民从不出现两条 InFlight。后两条只在空位出现后启动。预算面板显示使用量仍低于 `12 requests / 18k input / 3k output`。

#### B. 离线/故障回退

操作：对 `resident-003` 启用 `offline-next`，再触发一次允许的角色表达。

预期结果：不超过 3 秒得到 `Source=LocalFallback` 的小穗模板；请求有明确 failure reason；主线程、会话和共享世界不冻结；没有远程结果直接修改时间、背包或农田。

#### C. 陈旧响应丢弃

操作：为 `resident-002` 启用 `late-next` 并发起表达请求，记下 request ID 与 owner revision。协调器在 3 秒 deadline 采用本地 fallback。随后提交下一节的公开城镇目标，使阿木 goal/state revision 合法前进；延迟 callback 到达后继续观察。

预期结果：

- 迟到 callback 显示 `IgnoredStale`，原因至少包含 owner revision 不匹配或 owning state 已结束。
- 它不覆盖阿木已显示的 fallback，不追加记忆/会话 turn，不改变 goal、action、position、背包、农田或存档。
- 同一 request 只有一个 committed completion；budget 不因迟到 callback 重复扣减。

讲解：

> “停止等待不是安全边界。真正的边界是 request owner 和状态 revision；过期回包只能留审计记录，不能写回。”

### 5:15–5:45：提交公开城镇目标并本地分派

输入：

> 请大家一起把这块 3×3 农田全部种上胡萝卜，并完成浇水、施肥、除草和收获。

预期结果：唯一可选远程 `InterpretPlayerIntent` 请求由确定性 town coordinator 指定 `resident-001` 为 RequestOwner；请求只含公开命令、`CompleteCarrotLifecycle` / `Idle` 允许集合和必要公开只读上下文，private-memory owner set 为空。候选通过 schema 与关联校验后，Unity 才发布 `PublicTownEvent`；本地 dispatcher 显示固定分派：

- `resident-001` / 芽芽：Plots 1/5/9
- `resident-002` / 阿木：Plots 2/6
- `resident-003` / 小穗：Plots 3/7
- `resident-004` / 墨墨：Plots 4/8

指出证据字段 `DecisionSource=LocalDeterministic`。AI 响应中不得包含居民分配、坐标、低层动作或数值变更；出现这些字段时必须被拒绝并回退。

### 5:45–7:45：四居民完成共享农田全流程

让流程连续执行，不手工改状态。每个阶段指出 Resident Board 的每居民 action、Reservation Table 的唯一 owner、Shared World 的地块状态和背包数：

1. **播种：** 四人只处理自己的分区且分区内升序；全部播种结束后种子 0，水 9，肥料 9，胡萝卜 0。
2. **浇水：** 只有播种阶段屏障通过后才能开始；结束后水 0。
3. **施肥：** 只有浇水阶段屏障通过后才能开始；结束后肥料 0。
4. **等待与除草：** 杂草只由 Unity 时间规则出现；四人分别清理自己的分区，背包不变。
5. **等待与收获：** 成熟只由 Unity 时间规则触发；每次合法收获共享胡萝卜加 1。

全过程必须满足：每名居民至多一个 active action；每个 interaction point 至多一个 owner；没有居民读取他人的 private memory；远程表达成功或失败均不阻塞确定性动作。

### 7:45–8:30：最终状态与清理证明

停留在最终画面并逐项核对：

- 九块地全部完成收获并回到定义的空闲状态。
- 共享背包为种子 0、水 0、肥料 0、胡萝卜 9。
- 四名居民均无 active action、active conversation 或 in-flight request。
- Reservation Table 无遗留 lease；AI 队列为空。
- 四名居民各自保留自己的记忆/目标历史；没有 owner 交叉。
- 日志中保留一次 `LocalFallback` 和一次 `IgnoredStale`，且两者都没有权威状态 mutation。

### 8:30–10:00：受约束的 HarvestDinner 共同活动

#### A. Proposal 与 Unity 固定映射

操作：让合法居民请求只产生一次 `propose_town_event` 和受限理由，显示模型响应、Unity 绑定的可传播知识根/来源会话，以及权威活动实例的并排视图。活动处于 `Scheduled` 后 Save；推进到 Gathering、使一个广场席位失效，再 Load 该快照。随后重放同一事实根/会话证据并提交第二条 proposal。

预期结果：模型结果不包含 ActivityKind、时间、坐标或参加者；`TownEventCoordinator` 用合法对话知识证据固定映射下一次有效 18:00、广场和四名受邀者。活动存在时 Save 成功但 JSON 不含 TownEventSession、参加任务或席位租约；Load 不恢复活动，即使 `LocationArrivalPoint` 或席位对象已经销毁也确定性取消，并清空参与者 movement/suspension/预约且不写完成记忆。相同事实根/会话证据不能同时创建第二活动，活动计数始终不超过 1。

#### B. 全镇邀请、可选参加与到达

操作：把游戏时间推进到活动集合窗口。让芽芽、小穗接受，阿木拒绝，墨墨保持 Pending 并在集合时按拒绝处理；观察两名接受者使用正常导航前往广场并分别预约不同 attendance point。

预期结果：四人都收到公开邀请，但只有接受者获得 attendance task。芽芽和小穗没有瞬移，抵达后各自显示面向、姓名/状态图标和简单到达表现；阿木和墨墨不阻塞活动，也没有活动席位租约。两名接受者都持有有效预约并到达，因此在下一次有效 18:00 合法开始。

另用故障夹具让三名居民接受，其中两人到达、第三人导航失败或到达超时。预期整场安全取消，三人的活动预约全部释放；不得宣称剩余两人继续本场活动。

#### C. 必需欢迎语与可选受约束双人短会话

操作：活动开始后观察一次必需公共欢迎语。若本构建启用可选配对增强，则由活动协调器为芽芽和小穗创建一段 2–6 句的 event-owned 双人短会话，并尝试让阿木加入或创建三人 session；未启用时确认没有创建配对 session 或 AI 请求。

预期结果：无论可选增强是否启用，欢迎语都必须且只出现一次，并且不创建群聊。若启用配对，配对段仍有 ConversationId、双方锁、turn/deadline 和本地 fallback；阿木不能加入或旁听正文。若未启用则没有配对请求。两种配置都不存在三人/四人 session、自由群聊或群聊 AI 请求。

#### D. 公共与私人记忆隔离、结束清理

操作：结束 HarvestDinner，依次切换四个居民的 Memory 详情，并检查活动协调器、会话锁与 Reservation Table；确认全部终态清理完成后再次 Save，并加载该存档检查不会重建活动 session。

预期结果：四人各自拥有公开邀请和活动开始事实；只有实际完成参加的芽芽、小穗各有 owner 正确的私人 `TownEventCompleted` 参加记忆。若启用可选配对，只有二人拥有实际播放的配对内容；未启用则无人拥有配对正文。阿木和墨墨没有“我参加了 HarvestDinner”的私人记忆。结束后 active event、attendance task、活动会话锁和点位租约全部为零；此时 Save 成功并保留已提交事实/记忆，后续 Load 不恢复 TownEventSession、参加任务或预约。

结束讲解：

> “Town V2 的共同活动由 AI 提议、Unity 校验和执行。公开邀请不等于实际参加；每份私人参加记忆都有自己的 owner。AI 只增强高层意图与表达，不拥有时间、日程、位置或游戏状态。”

## 6. 证据采集清单

演示后保存一段连续录屏，并导出以下证据。任何一项只能口述而无法截图/日志复核时，记为失败。

| 证据 ID | 必须捕获的画面或记录 | 通过要点 |
| --- | --- | --- |
| EV-01 | 初始 Resident Board + Shared World | 四个唯一 ID；显示名不作键；背包 9/9/9/0 |
| EV-02 | 四人对同一公开事件的表达与 context manifest | persona/state owner 各自正确、表达有可解释差异 |
| EV-03 | Memory Access allow/deny 记录 | 阿木不能读取芽芽 private memory；无跨 owner prompt 条目 |
| EV-04 | Conversation Ledger、双方 Memory、进行中 Save/Load 与第二会话拒绝 | Active 时 Save 只投影已播放片段为双方 owner 隔离的 private memory；无 active session transient；Load 清会话/锁/预约、不重复投影且不应用未播放内容 |
| EV-05 | point 04 lease 生命周期 | 任一时刻 owner 数 ≤1；冲突者不移动/不动作；最终释放 |
| EV-06 | AI Coordinator 并发瞬间 | 四居民同时请求时 global in-flight ≤2、每居民 ≤1、后两条排队、预算可见 |
| EV-07 | offline/fault 请求 | ≤3 秒 LocalFallback，世界继续运行，来源与原因准确 |
| EV-08 | late response | `IgnoredStale` 含 request/owner/revision；无第二次写回 |
| EV-09 | 本地任务分派 | 1/5/9、2/6、3/7、4/8，source 为 local deterministic |
| EV-10 | 农事阶段与最终画面 | 播种→浇水→施肥→除草→收获；最终 0/0/0/9；无遗留 owner |
| EV-11 | 模型响应、知识证据与权威活动并排视图 | 模型只输出 `propose_town_event`；Unity 固定映射下一次有效 18:00/广场；相同事实根/会话证据与第二活动被拒绝 |
| EV-12 | Town Event + Reservation Table | 全镇邀请、至少两人接受、不同席位、连续导航而非瞬移；任一参加者到达失败时全场取消并释放全部预约 |
| EV-13 | 欢迎语 + 可选 Conversation Ledger | 欢迎语必需且仅一次；可选活动会话若启用仍恰好两人、2–6 句、无群聊/旁听/重叠 |
| EV-14 | 四 owner Memory 详情 + 清理表 + 终态存档 | 公共邀请/开始可见；私人参加和配对正文只属于实际参加者；所有锁/租约释放；终态 Save 成功且 Load 不恢复活动 session |
| EV-15（可选在线片段） | API SETUP 状态、子进程参数/环境脱敏摘要与生命周期 | 默认模型正确；仅回环；Key 不落盘/日志/命令行；外部网关不接管；只停止自有进程；失败时本地回退 |

建议日志导出使用结构化 JSONL，并对私有记忆正文和 prompt 做脱敏；录屏中的操作员诊断面板只用于评审环境。

## 7. 现场失败与降级口径

- 若真实模型不可用但共享 coordinator、3 秒回退和本地模板工作，继续演示并明确说“远程增强不可用，当前是本地回退”；这不影响确定性农事核心结果。
- 若 scripted provider 自身失效，停止 AI 压力段，可继续展示离线农事；不得声称已证明全局并发、预算或 stale discard。
- 若出现重复 ID、跨居民私有读取、同点双 owner、同居民双会话/双动作、同时两个共同活动、模型直接安排时间/位置/日程、未参加者得到私人参加记忆、自由多人群聊、超预算远程启动、迟到结果写回或最终背包不为 0/0/0/9，立即判 Town V2 演示失败。
- 不现场修改存档、Inspector 字段或私有内存来“修正”结果；保留日志并转入缺陷处理。

## 8. 禁止的演示表述

- 不说“AI 给四个人分了地”——分区来自 Unity 本地确定性 dispatcher。
- 不说“AI 决定了播种/浇水坐标或修改了背包”——模型无此权限。
- 不说“AI 把居民安排到 18:00 的广场或把居民传送过去”——模型只提交 `TownEventProposal`，固定映射、参加决定和导航均由 Unity 处理。
- 不把显示名当作身份，也不通过一个可变全局 current resident 路由状态。
- 不把操作员能看的 private-memory 诊断面板描述成其他居民可见。
- 不把服务端内部 `mock` 回退误标为远程模型成功；以逐请求 source 为准。
- 不把历史单居民画面或旧验收记录当作当前 Town V2 已完成证据。
