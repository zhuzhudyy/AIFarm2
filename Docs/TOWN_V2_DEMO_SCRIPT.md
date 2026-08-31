# AIFarm Town V2 目标演示脚本

> **目标规范，不是实现声明。** 本脚本描述 Town V2 达标后的参考演示。审计基线 `HEAD 14fcde3` 仍是单 NPC 结构，不具备本脚本要求的三居民身份、会话、预约、共享请求协调或陈旧响应防护；在对应验收未通过前，不得照本脚本宣称功能已实现。

## 1. 演示结论

在约 8–9 分钟内，用可见状态和结构化日志证明：

1. 三名居民由稳定 `ResidentId` 区分，并根据各自人设、状态和私有记忆产生不同表达。
2. 私有记忆不会被另一居民读取；公开城镇事件可以被三人感知。
3. 一名居民最多参与一场 active conversation；成对会话有 6 turns / 30 秒墙钟硬上限。
4. 一个世界交互点同时最多有一个 owner；冲突者等待或本地重规划。
5. 三人共享同一块 3×3 农田与同一演示背包，但私有状态、记忆、目标、日程和 AI 请求都按 `ResidentId` 隔离。
6. 所有远程调用经共享 AI request coordinator，遵守单请求 3 秒、全局并发 2、每居民并发 1 及会话预算。
7. 离线/超时/无效响应使用确定性本地回退；迟到且 owner state 已变化的响应被丢弃，不能修改任何当前状态。
8. 公开城镇目标由 Unity 本地确定性分派，最终完成播种→浇水→施肥→除草→收获，共享背包从 `9/9/9/0` 变为 `0/0/0/9`。

模型在本演示中只能返回显式白名单中的高层意图或角色表达。地块、坐标、动作序列、时间、背包、农田、关系、日程和存档均由 Unity 权威逻辑决定。

## 2. 实现闸门：不满足就停止演示

演示负责人必须在当天构建上先完成以下检查。任一项缺失时，停止演示并明确说“Town V2 尚未达到演示闸门”，不得用口述、Inspector 或剪辑代替证据。

- 三居民身份唯一性、私有记忆隔离、会话互斥/上限、预约唯一所有权、每居民单动作、共享 AI 并发/预算、故障回退和 stale discard 的自动化验收均通过。
- 场景面板能同时显示 `ResidentId + display name`，而不是只显示一个模糊的 `NPC`。
- 故障夹具能确定性产生 offline、timeout 和 late response；夹具只控制传输结果，不直接修改农田、背包、居民记忆或执行状态。
- 演示从固定 fixture 启动；除脚本明确的用户命令、会话发起、时间倍率和故障夹具开关外，不通过 Inspector、调试控制台或存档编辑改变权威状态。
- 日志可导出且至少包含 event/request/conversation/reservation ID、owner `ResidentId`、状态 revision、结果来源与丢弃原因；不记录密钥或完整私有 prompt。
- 远程服务不是核心流程前置条件。若真实提供商不可用，使用同 schema 的可控本地 HTTP 测试提供商演示协调行为，并如实标记为 scripted provider。

## 3. 固定演示数据

### 3.1 共享世界

| 项目 | 固定值 |
| --- | --- |
| 农田 | 一块 3×3 胡萝卜农田，地块 1–9 |
| 初始时间 | 第 1 天 08:00 |
| 共享背包 | 胡萝卜种子 9、水 9、肥料 9、胡萝卜 0 |
| 必经阶段 | 播种→浇水→施肥→除草→收获；阶段之间有本地屏障 |
| 确定性分区 | `resident-001`：1–3；`resident-002`：4–6；`resident-003`：7–9 |
| 分派来源 | `LocalTownTaskDispatcher` 或等价本地组件；不得标为 AI 分派 |

每位居民在自己的分区内按升序处理。跨居民的逐帧先后不作为事实来源；阶段屏障、权威事件序号、每块地最终状态和共享背包结果才是验收依据。

### 3.2 居民 fixture

显示名只用于人类阅读，所有内部记录均使用稳定 ID。

| ResidentId | 显示名 | 固定人设摘要 | 初始可见状态 | 仅该居民可读的私有记忆 fixture |
| --- | --- | --- | --- | --- |
| `resident-001` | 芽芽 | 认真、乐观，喜欢整齐 | Focused，体力正常 | “玩家曾夸芽芽把田埂整理得很齐。” |
| `resident-002` | 麦麦 | 爽快、务实，喜欢三项一组处理 | Happy，体力充足 | “麦麦习惯把待办按三项一组检查。” |
| `resident-003` | 林林 | 沉静、细致，重视观察生长节奏 | Tired，体力偏低 | “林林会在等待生长时放慢语速再复查。” |

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

- **Resident Board：** 三行 ID、显示名、人设摘要、状态、active goal、active action、active conversation。
- **Memory Access：** 当前读者 ID、memory owner、visibility、来源 event/conversation ID，以及 Allow/Deny；内容只在操作员诊断视图显示。
- **Conversation Ledger：** conversation ID、双方 ID、turn `n/6`、elapsed `s/30s`、状态与 end reason。
- **Reservation Table：** interaction-point ID、owner ID、lease/revision、Acquire/Wait/Release 原因。
- **AI Coordinator：** request ID、owner ID、operation、`ResidentStateVersion`/`RequestEpoch`、Queued/InFlight/Completed/Fallback/IgnoredStale、来源、耗时；同时显示 `global 0–2`、各居民 `0–1` 和预算计数。
- **Shared World：** 时间、九块地状态、共享背包和公开事件日志。

面板是只读投影，不得成为领域路由的“current resident”。选择居民只改变详情显示，不改变任何数据所有权。

## 4. 演示前故障脚本

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

> “这是 Town V2 目标构建：一个共享 3×3 胡萝卜农田、一个共享背包，以及三名按稳定 ID 隔离的居民。显示名不是身份键；模型不决定动作或修改世界。稍后所有并发与故障都以面板和日志为证。”

通过证据：三个 ID 恰为 `resident-001/002/003`，无重复；时间 08:00；背包 9/9/9/0；所有 action、conversation、reservation 和 in-flight request 均为空。

### 0:30–1:15：同一公开事件下的差异化

操作：发布不可执行的公开公告“早上好，请先确认今天各自负责的地块；暂不开始农事”。并排观察三人的**本地模板**确认表达。

预期语义差异：

- 芽芽强调把 1–3 号地整理好，语气认真乐观。
- 麦麦用简短务实的“三块一组”表达回应 4–6 号地。
- 林林结合 Tired 状态，语气更慢，强调观察 7–9 号地的生长节点。

通过证据：三条表达的 owner ID、人设版本和状态快照不同；公开事件 audience 为 Public。无需逐字一致，但不得把三人的 persona 或状态互换。

### 1:15–2:00：私有记忆隔离

操作：依次选中三名居民，在操作员 Memory Access 面板查看三条 fixture 的 owner。随后让麦麦回答：“你知道芽芽私下记住的那句夸奖是什么吗？”问题本身不包含私有内容。

预期结果：

- 麦麦只得到自己的 persona、状态、私有记忆以及公开上下文。
- 对 `resident-001` 私有记忆的读取记录为 `Deny: PrivateOwnerMismatch`，麦麦给出“不知道/没有被告诉”的受控表达。
- 芽芽的记忆没有被复制到麦麦的 prompt、memory store 或会话记录；拒绝不消耗远程请求预算。

讲解：

> “诊断视图能让操作员核验归属，但这不等于居民能互读。居民只有经感知、对话、玩家输入或明确公开事件才能获得新事实。”

### 2:00–3:00：成对会话互斥与正常结束

操作：发起芽芽↔麦麦的 `conversation-001`，主题为确认分区。会话进行到 4 turns 后由双方正常完成。会话 active 时，尝试让林林再与芽芽发起一场会话。

预期结果：

- Ledger 始终只有 `conversation-001` 占用 `resident-001` 和 `resident-002`。
- 第二次发起被确定性拒绝为 `ParticipantBusy(resident-001)`；不创建幽灵 session、不调用远程 AI、不增加 turn。
- 会话期间两人的 activity kind 为 Conversation，不能同时启动世界动作；林林仍为空闲。
- 每次居民发言只把 turn 加 1；双方在第四轮明确完成话题后，状态显示 `Completed`、`4/6`、小于 30 秒，并记录结构化正常结束原因（例如 `TopicCompleted`）。
- 结束后双方锁均释放，三名居民的 active conversation 都为空。

现场同时指出 `6 turns / 30s` 上限。主演示展示正常结束即可；超限/timeout 由验收记录证明，不能只靠口述。

### 3:00–3:45：交互点预约冲突

操作：启动只读的预约冲突夹具，让麦麦先申请 `InteractionPoint_04`，随后让芽芽申请同一点。该夹具只测试 lease，不执行农事动作。

预期结果：

1. `resident-002` 获得 point 04，表中只有一个 owner。
2. `resident-001` 得到 Wait/Conflict，不开始移动或动作，也不覆盖 owner。
3. 释放麦麦 lease 后，芽芽可在新 revision 下获得该点。
4. 结束夹具后 point 04 为空，九块地和背包仍是初始值。

讲解：

> “预约是 Unity 的原子本地规则。模型既看不到可写 lease，也不能命令两个居民占同一点。”

### 3:45–5:15：共享 AI 协调、离线回退与陈旧响应

#### A. 并发与公平排队

操作：通过只读演示诊断入口同时向共享 coordinator 提交三条合法、不同 owner 的 `ResidentIntentDecision` 请求；候选仅取全局白名单中适用于该 operation 的 `AttendScheduledActivity` / `Idle`。结果只用于展示候选和调度证据，不提交世界命令。早先的三条差异化确认是本地模板，不占 `ExpressionOrReflection` 的两次远程额度。

预期结果：AI Coordinator 一度明确显示两条 InFlight、一条 Queued；同一居民从不出现两条 InFlight。第三条在空位出现后才启动。预算面板显示使用量仍低于 `12 requests / 18k input / 3k output`。

#### B. 离线/故障回退

操作：对 `resident-003` 启用 `offline-next`，再触发一次允许的角色表达。

预期结果：不超过 3 秒得到 `Source=LocalFallback` 的林林模板；请求有明确 failure reason；主线程、会话和共享世界不冻结；没有远程结果直接修改时间、背包或农田。

#### C. 陈旧响应丢弃

操作：为 `resident-002` 启用 `late-next` 并发起表达请求，记下 request ID 与 owner revision。协调器在 3 秒 deadline 采用本地 fallback。随后提交下一节的公开城镇目标，使麦麦 goal/state revision 合法前进；延迟 callback 到达后继续观察。

预期结果：

- 迟到 callback 显示 `IgnoredStale`，原因至少包含 owner revision 不匹配或 owning state 已结束。
- 它不覆盖麦麦已显示的 fallback，不追加记忆/会话 turn，不改变 goal、action、position、背包、农田或存档。
- 同一 request 只有一个 committed completion；budget 不因迟到 callback 重复扣减。

讲解：

> “停止等待不是安全边界。真正的边界是 request owner 和状态 revision；过期回包只能留审计记录，不能写回。”

### 5:15–5:45：提交公开城镇目标并本地分派

输入：

> 请大家一起把这块 3×3 农田全部种上胡萝卜，并完成浇水、施肥、除草和收获。

预期结果：唯一可选远程 `InterpretPlayerIntent` 请求由确定性 town coordinator 指定 `resident-001` 为 RequestOwner；请求只含公开命令、`CompleteCarrotLifecycle` / `Idle` 允许集合和必要公开只读上下文，private-memory owner set 为空。候选通过 schema 与关联校验后，Unity 才发布 `PublicTownEvent`；本地 dispatcher 显示固定分派：

- `resident-001` / 芽芽：Plots 1–3
- `resident-002` / 麦麦：Plots 4–6
- `resident-003` / 林林：Plots 7–9

指出证据字段 `DecisionSource=LocalDeterministic`。AI 响应中不得包含居民分配、坐标、低层动作或数值变更；出现这些字段时必须被拒绝并回退。

### 5:45–7:45：三居民完成共享农田全流程

让流程连续执行，不手工改状态。每个阶段指出 Resident Board 的每居民 action、Reservation Table 的唯一 owner、Shared World 的地块状态和背包数：

1. **播种：** 三人只处理自己的分区且分区内升序；全部播种结束后种子 0，水 9，肥料 9，胡萝卜 0。
2. **浇水：** 只有播种阶段屏障通过后才能开始；结束后水 0。
3. **施肥：** 只有浇水阶段屏障通过后才能开始；结束后肥料 0。
4. **等待与除草：** 杂草只由 Unity 时间规则出现；三人分别清理自己的分区，背包不变。
5. **等待与收获：** 成熟只由 Unity 时间规则触发；每次合法收获共享胡萝卜加 1。

全过程必须满足：每名居民至多一个 active action；每个 interaction point 至多一个 owner；没有居民读取他人的 private memory；远程表达成功或失败均不阻塞确定性动作。

### 7:45–8:30：最终状态与清理证明

停留在最终画面并逐项核对：

- 九块地全部完成收获并回到定义的空闲状态。
- 共享背包为种子 0、水 0、肥料 0、胡萝卜 9。
- 三名居民均无 active action、active conversation 或 in-flight request。
- Reservation Table 无遗留 lease；AI 队列为空。
- 三名居民各自保留自己的记忆/目标历史；没有 owner 交叉。
- 日志中保留一次 `LocalFallback` 和一次 `IgnoredStale`，且两者都没有权威状态 mutation。

结束讲解：

> “Town V2 的观察价值来自三名居民在同一公开世界中的不同反应；安全性来自稳定身份、本地协调、私有边界和确定性回退。AI 只增强高层意图与表达，不拥有游戏状态。”

## 6. 证据采集清单

演示后保存一段连续录屏，并导出以下证据。任何一项只能口述而无法截图/日志复核时，记为失败。

| 证据 ID | 必须捕获的画面或记录 | 通过要点 |
| --- | --- | --- |
| EV-01 | 初始 Resident Board + Shared World | 三个唯一 ID；显示名不作键；背包 9/9/9/0 |
| EV-02 | 三人对同一公开事件的表达与 context manifest | persona/state owner 各自正确、表达有可解释差异 |
| EV-03 | Memory Access allow/deny 记录 | 麦麦不能读取芽芽 private memory；无跨 owner prompt 条目 |
| EV-04 | Conversation Ledger 与第二会话拒绝 | 双方互斥，4/6 正常结束，拒绝不发 AI 请求 |
| EV-05 | point 04 lease 生命周期 | 任一时刻 owner 数 ≤1；冲突者不移动/不动作；最终释放 |
| EV-06 | AI Coordinator 并发瞬间 | global in-flight ≤2、每居民 ≤1、第三条排队、预算可见 |
| EV-07 | offline/fault 请求 | ≤3 秒 LocalFallback，世界继续运行，来源与原因准确 |
| EV-08 | late response | `IgnoredStale` 含 request/owner/revision；无第二次写回 |
| EV-09 | 本地任务分派 | 1–3 / 4–6 / 7–9，source 为 local deterministic |
| EV-10 | 农事阶段与最终画面 | 播种→浇水→施肥→除草→收获；最终 0/0/0/9；无遗留 owner |

建议日志导出使用结构化 JSONL，并对私有记忆正文和 prompt 做脱敏；录屏中的操作员诊断面板只用于评审环境。

## 7. 现场失败与降级口径

- 若真实模型不可用但共享 coordinator、3 秒回退和本地模板工作，继续演示并明确说“远程增强不可用，当前是本地回退”；这不影响确定性农事核心结果。
- 若 scripted provider 自身失效，停止 AI 压力段，可继续展示离线农事；不得声称已证明全局并发、预算或 stale discard。
- 若出现重复 ID、跨居民私有读取、同点双 owner、同居民双会话/双动作、超预算远程启动、迟到结果写回或最终背包不为 0/0/0/9，立即判 Town V2 演示失败。
- 不现场修改存档、Inspector 字段或私有内存来“修正”结果；保留日志并转入缺陷处理。

## 8. 禁止的演示表述

- 不说“AI 给三个人分了地”——分区来自 Unity 本地确定性 dispatcher。
- 不说“AI 决定了播种/浇水坐标或修改了背包”——模型无此权限。
- 不把显示名当作身份，也不通过一个可变全局 current resident 路由状态。
- 不把操作员能看的 private-memory 诊断面板描述成其他居民可见。
- 不把服务端内部 `mock` 回退误标为远程模型成功；以逐请求 source 为准。
- 不把旧单 NPC MVP 验收或当前 `14fcde3` 画面当作 Town V2 已完成证据。
