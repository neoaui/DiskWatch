# DiskWatch2 界面文案对照

按 `docs/UI-COPY-GUIDE.md` 将主界面说明改为普通软件写法。不改变功能、数据和统计口径。保留用语：隐性占用空间 / 未解释差额、瘦身建议、导出 AI 提示词、显著/快速/轻微增长与红橙黄分级、提醒档位 20 / 11 / 3 GiB。

共更改 **175** 处文案（含测试断言同步）。源码备份已从公开副本中省略。

| 位置 | 原文 | 新文案 |
| --- | --- | --- |
| `src/DiskWatch.App/InsightsPanels.cs`（应用排行副标题） | 谁在吃 {s.Root} · 按应用 / 来源看增长 | {s.Root} 各应用及来源的占用与增长 |
| `src/DiskWatch.App/InsightsPanels.cs`（应用排行空状态） | 这次扫描没有应用归属数据（更新前的旧扫描不记录）。下一次扫描后显示当前占用，再扫一次就能看到增长。 | 这次扫描没有应用占用记录。完成一次新的扫描后可查看当前占用，再扫描一次后可查看增长。 |
| `src/DiskWatch.App/InsightsPanels.cs`（未识别目录提示） | 没有匹配到规则表的目录，按上层文件夹归组。可在 settings.json 的 AppRules 里补规则。 | 未能识别为已安装应用，已按所在文件夹归组。 |
| `src/DiskWatch.App/InsightsPanels.cs` | 扫描到的文件净变化 {Labels.SignedSize(r.NetBytes)} · 增长合计 {Labels.SmartSize(r.GrowthTotal)} | 扫描文件净变化 {Labels.SignedSize(r.NetBytes)}，增长合计 {Labels.SmartSize(r.GrowthTotal)} |
| `src/DiskWatch.App/InsightsPanels.cs` | 当前扫描到 {Labels.SmartSize(r.Current.Total)} · 共 {r.Rows.Count} 个应用 / 来源 | 当前占用 {Labels.SmartSize(r.Current.Total)}，共 {r.Rows.Count} 个应用 / 来源 |
| `src/DiskWatch.App/InsightsPanels.cs` | 两次扫描之间没有应用 / 来源增长。 | 两次扫描之间没有应用或来源增长。 |
| `src/DiskWatch.App/InsightsPanels.cs`（塞满预测提示） | 按每天剩余空间最低值的中位数趋势估算，只用本机采样数据。 | 根据近期剩余空间变化估算。数据不足时仅供参考，不给出天数。 |
| `src/DiskWatch.App/InsightsPanels.cs`（设置开关名称） | 骤降时自动抓现场（可选，默认关闭） | 空间骤降时自动扫描该磁盘 |
| `src/DiskWatch.App/InsightsPanels.cs`（骤降开关说明） | 开启后，骤降提醒一触发就自动把那个盘扫一遍，然后在提醒里告诉你是哪些应用 / 文件夹长大了，不用等每天的定时扫描。 | 可用空间突然下降时，自动扫描该磁盘，并在提醒中列出增长较多的应用和文件夹。 |
| `src/DiskWatch.App/InsightsPanels.cs`（骤降开关补充说明（去掉实测时间和不会删除保证）） | 代价：扫描会占用几分钟磁盘和 CPU（今天实测常规扫描 C 盘约 5 分钟；以管理员身份运行时会用 MFT 快速扫描，快很多）。同一个盘 30 分钟内最多自动扫一次；已有扫描在进行时不会重复扫。扫描方式和设置里一样，结果能和平时的扫描对比。只扫描和提示，不会删除任何文件。 | 扫描可能需要几分钟。同一磁盘 30 分钟内最多自动扫描一次；已有扫描进行时不会重复开始。 |
| `src/DiskWatch.App/ModernShell.cs`（占用分类副标题） | {s.Buckets.Select(b=>b.Category).Distinct().Count()} 个有记录的分类 · 按占用从大到小 | {s.Buckets.Select(b=>b.Category).Distinct().Count()} 个分类，按占用从大到小 |
| `src/DiskWatch.App/ModernShell.cs` | 尚无扫描结果。点击右上角“开始扫描”，即可查看空间去向。 | 还没有扫描结果。点击右上角「开始扫描」。 |
| `src/DiskWatch.App/ModernShell.cs` | 按面积看空间去向 · 点击文件夹进入下一层 | 按面积查看空间占用，点击文件夹进入下一层 |
| `src/DiskWatch.App/ModernShell.cs` | 按日、周、月回顾空间变化。 | 按日、周、月查看空间变化。 |
| `src/DiskWatch.App/ModernShell.cs` | 按需调整扫描、提醒和分类规则。 | 扫描、提醒与分类。 |
| `src/DiskWatch.App/ModernShell.cs` | 看看空间是在什么时间发生变化的。 | 查看空间随时间的变化。 |
| `src/DiskWatch.App/ModernShell.cs` | 栏目含义与空间统计口径 · 离线可用 | 栏目含义与统计说明 |
| `src/DiskWatch.App/MainWindow.cs` | 先选择磁盘，再查看你关心的内容。 | 选择磁盘，查看空间占用。 |
| `src/DiskWatch.App/MainWindow.cs` | 已就绪 · 选择磁盘查看空间去向 | 已就绪，选择磁盘查看占用 |
| `src/DiskWatch.App/ModernShell.cs` | 红色标记「不可删除」的是系统或已安装软件需要的文件，请不要手动删除。 | 红色标记为「不可删除」，表示系统或已安装软件需要的文件。 |
| `src/DiskWatch.App/ModernShell.cs` | 查看占用最大的前 20 项 · {lockedCount} 项不可删除（红色） | 占用最大的前 20 项，其中 {lockedCount} 项不可删除 |
| `src/DiskWatch.App/ModernShell.cs` | 该路径下已记录的大文件（不是完整文件清单） | 该路径下已记录的大文件（不是完整文件列表） |
| `src/DiskWatch.App/ModernShell.cs` | 此目录没有进入大文件记录列表的项目。 | 此目录没有达到记录条件的大文件。 |
| `src/DiskWatch.App/ModernShell.cs` | 面积代表分配空间，颜色代表分类，红框表示「不可删除」。点击文件夹进入下一层，用上方路径返回。目录最多到扫描记录的第 4 层。 | 色块大小表示占用空间，颜色表示分类，红框表示不可删除。点击文件夹进入下一层，点击上方路径返回。 |
| `src/DiskWatch.App/ModernShell.cs` | 展开分类查看目录，再展开目录查看已记录的大文件。数值为分配空间。 | 展开分类查看目录，再展开查看已记录的大文件。 |
| `src/DiskWatch.App/ModernShell.cs` | 查看统计口径与覆盖范围 | 查看统计说明与覆盖范围 |
| `src/DiskWatch.App/ModernShell.cs` | 分类表格、目录差值与 CSV 导出 | 分类、目录变化与导出 |
| `src/DiskWatch.App/ModernShell.cs`（瘦身建议说明（保留确认后再处理，去掉只读保证的重复强调，保留不可删除不出现）） | 根据本次快照里的目录统计和大文件给出建议，仅供参考。DiskWatch 不会自动删除或移动任何文件，请确认用途后自行处理。标记为「不可删除」的文件不会出现在这里。 | 根据本次扫描给出可转移或可删除的项目，仅供参考。请确认后再自行处理。标记为「不可删除」的文件不会出现在这里。 |
| `src/DiskWatch.App/ModernShell.cs`（导出 AI 提示词说明） | 生成一份可直接交给 AI 的清理提示词（含不可删除清单和安全规则），复制到剪贴板并保存到 exports 文件夹。只生成文字，不执行任何操作。 | 生成可交给 AI 的清理提示词，含不可删除清单和安全规则。复制到剪贴板，并保存到 exports 文件夹。 |
| `src/DiskWatch.App/ModernShell.cs` | {copied}并保存到 {file}。粘贴给 AI 后，它会先核实并列计划，每一步都等你确认。 | {copied}并保存到 {file}。交给 AI 后，应先核实并列出计划，每一步都需确认后再执行。 |
| `src/DiskWatch.App/ModernShell.cs` | 可以转移 {Labels.Size(plan.MoveBytes)} · 可以删除 {Labels.Size(plan.DeleteBytes)} · 仅供参考 | 可转移 {Labels.Size(plan.MoveBytes)}，可删除 {Labels.Size(plan.DeleteBytes)}，仅供参考 |
| `src/DiskWatch.App/ModernShell.cs` | 暂未发现明显可转移或可删除的项目 | 暂时没有可转移或可删除的项目 |
| `src/DiskWatch.App/ModernShell.cs` | 暂未发现符合条件的项目。 | 暂时没有符合条件的项目。 |
| `src/DiskWatch.App/ModernShell.cs` | 未解释差额 {Labels.Size(residual)}。可能包含未覆盖区域、文件系统元数据和扫描时差，并非可清理空间。 | 未解释差额 {Labels.Size(residual)}。可能包含未覆盖区域、文件系统开销和扫描时差，并不等于可清理空间。 |
| `src/DiskWatch.App/ModernShell.cs` | 子目录扫描不计算全卷未解释差额。 | 当前为文件夹扫描，不计算整盘未解释差额。 |
| `src/DiskWatch.App/ModernShell.cs` | {s.Errors} 项读取问题 · 不影响已保存的数据 | {s.Errors} 项读取失败，已保存的数据不受影响 |
| `src/DiskWatch.App/ModernShell.cs` | {s.Finished.ToLocalTime():MM月dd日 HH:mm}  ·  {s.Files:N0} 个文件  ·  {(s.Finished-s.Started).TotalMinutes:N1} 分钟 | {s.Finished.ToLocalTime():MM月dd日 HH:mm}，{s.Files:N0} 个文件，{(s.Finished-s.Started).TotalMinutes:N1} 分钟 |
| `src/DiskWatch.App/ModernShell.cs` |   ·  最近一次扫描 | ，最近一次扫描 |
| `src/DiskWatch.App/ModernShell.cs` | 扫描结果已保存，可从下面的分类开始查看。 | 扫描结果已保存，可从下方分类查看。 |
| `src/DiskWatch.App/ModernShell.cs` | 选择这次要扫描的范围。点“开始扫描”后才开始；不会改设置里保存的范围。 | 选择本次扫描范围，点击「开始扫描」后开始。不会更改设置中保存的范围。 |
| `src/DiskWatch.App/ModernShell.cs` | 只在需要时查看低空间和骤降记录 | 查看低空间和骤降记录 |
| `src/DiskWatch.App/ModernShell.cs` | {counted} · {errors:N0} 项读取问题 · 已用 {ScanEstimator.Duration(e.Elapsed)} · {remaining} | {counted}，{errors:N0} 项读取失败，已用时 {ScanEstimator.Duration(e.Elapsed)}，{remaining} |
| `src/DiskWatch.App/ModernShell.cs` | 已扫 {files:N0} 个文件 · {Labels.Size(bytes)} | 已扫描 {files:N0} 个文件，{Labels.Size(bytes)} |
| `src/DiskWatch.App/MainWindow.cs` | DiskWatch2 · 磁盘空间观察 · 开发预览 | DiskWatch2 |
| `src/DiskWatch.App/MainWindow.cs` | MFT 快速扫描（实验）：以管理员身份运行时默认开启，仅限 NTFS；没有管理员权限或出错时自动改用目录遍历 | 在 NTFS 磁盘上加快扫描。需管理员权限，不可用时自动改用普通扫描。 |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | MFT 快速扫描 | NTFS 快速扫描 |
| `src/DiskWatch.App/MainWindow.cs` | 读取安装注册表和 Steam 库配置，辅助分类（不读用户文件正文） | 读取已安装应用和游戏库信息，用于识别应用占用。 |
| `src/DiskWatch.App/MainWindow.cs` | 低空间与突然增长时提醒你 | 剩余空间不足或突然下降时发出提醒 |
| `src/DiskWatch.App/MainWindow.cs` | 空间恢复后会重新启用提醒；相同骤降提醒间隔至少 30 分钟。 | 空间恢复后会重新启用提醒。同类骤降提醒至少间隔 30 分钟。 |
| `src/DiskWatch.App/MainWindow.cs` | 10 分钟内可用空间减少多少 GiB 时提醒 | 10 分钟内可用空间减少达到多少 GiB 时提醒 |
| `src/DiskWatch.App/MainWindow.cs` | 低空间提醒档位 / GiB（用逗号分隔） | 低空间提醒档位（GiB，用逗号分隔） |
| `src/DiskWatch.App/MainWindow.cs` | 勾选要扫描的盘或文件夹 | 勾选要扫描的磁盘或文件夹 |
| `src/DiskWatch.App/MainWindow.cs` | 将目录及其子项归入指定分类。仅影响下一次扫描。 | 将目录及其内容归入指定分类，下次扫描生效。 |
| `src/DiskWatch.App/MainWindow.cs` | 让分类更符合你的文件习惯 | 调整文件分类 |
| `src/DiskWatch.App/MainWindow.cs` | 仅记录文件元数据，本地保存，不读取文件正文。程序不会自动删除或移动文件。关闭窗口会留在托盘；退出程序后停止监测。 | 记录保存在本机。关闭窗口后仍在托盘运行，退出后停止监测。 |
| `src/DiskWatch.App/MainWindow.cs` | 设置已保存。没有注册系统任务或自启。 | 设置已保存。 |
| `src/DiskWatch.App/MainWindow.cs` | 此范围尚无扫描快照。容量采样不等于目录扫描。 | 此范围还没有扫描结果。容量监测不会代替目录扫描。 |
| `src/DiskWatch.App/MainWindow.cs` | 警告：出现负差额，结果需要校准；没有截零或分摊。 | 注意：出现负差额，结果需要核对，未做凑平处理。 |
| `src/DiskWatch.App/MainWindow.cs` | 子目录扫描不与全卷占用凑总。 | 当前为文件夹扫描，不与整盘占用合计。 |
| `src/DiskWatch.App/MainWindow.cs` | 目录行是互不重复的统计桶，不是可再次叠加的父子目录总量。分类纠正只影响下一次扫描。 | 各目录互不重复统计。分类纠正在下次扫描后生效。 |
| `src/DiskWatch.App/MainWindow.cs` | 目录（互不重复的统计桶） | 目录 |
| `src/DiskWatch.App/MainWindow.cs` | 卷已用－已解释分配空间；可能包括权限、元数据、采样时差 | 整盘已用减去已解释占用，可能包括权限限制、文件系统开销和扫描时差 |
| `src/DiskWatch.App/MainWindow.cs` | 文件分配空间；硬链接只计一次 | 按磁盘占用计算，硬链接只计一次 |
| `src/DiskWatch.App/MainWindow.cs` | 未解释差额，非系统占用定论 | 未解释差额，不能视为系统占用或可清理空间 |
| `src/DiskWatch.App/MainWindow.cs` | 扫描已结束，完整总结已保存；请查看“扫描总结”中的完成状态和覆盖说明。 | 扫描已结束，总结已保存。可在「扫描总结」中查看完成状态和覆盖说明。 |
| `src/DiskWatch.App/MainWindow.cs` | 。历史完整快照保留，可手动重新扫描。 | 已保留此前的完整结果，可重新扫描。 |
| `src/DiskWatch.App/MainWindow.cs` | 已有扫描运行，请等待或取消。 | 已有扫描正在进行，请等待或取消。 |
| `src/DiskWatch.App/MainWindow.cs` | 选择要扫描的真实目录（不跟随 junction） | 选择要扫描的文件夹 |
| `src/DiskWatch.App/MainWindow.cs` | 读取 MFT 索引 | 正在读取文件目录 |
| `src/DiskWatch.App/MainWindow.cs` | 扫描 {p.Root} · {p.Files:N0} 文件 · {Labels.Size(p.AllocatedBytes)} · {p.Errors} 项读取问题 · {p.CurrentDirectory} | 正在扫描 {p.Root}，已扫描 {p.Files:N0} 个文件，{Labels.Size(p.AllocatedBytes)}，{p.Errors} 项读取失败，{p.CurrentDirectory} |
| `src/DiskWatch.App/MainWindow.cs` | {s.Root} · {s.Finished.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {s.Coverage}\n已解释 {Labels.Size(s.AllocatedBytes)} / 逻辑 {Labels.Size(s.LogicalBytes)}。 | {s.Root}，{s.Finished.ToLocalTime():yyyy-MM-dd HH:mm:ss}，{s.Coverage}\n已解释占用 {Labels.Size(s.AllocatedBytes)}，逻辑大小 {Labels.Size(s.LogicalBytes)}。 |
| `src/DiskWatch.App/MainWindow.cs` | {snapshot.Root} 扫描结束：{snapshot.Coverage}，{snapshot.Files:N0} 文件，耗时 {(snapshot.Finished - snapshot.Started).TotalSeconds:N0} 秒。 | {snapshot.Root} 扫描结束：{snapshot.Coverage}，{snapshot.Files:N0} 个文件，用时 {(snapshot.Finished - snapshot.Started).TotalSeconds:N0} 秒。 |
| `src/DiskWatch.App/MainWindow.cs` | （普通权限：启动时未获管理员权限） | （普通权限） |
| `src/DiskWatch.App/MainWindow.cs` | 启用每日目录扫描（程序运行时） | 程序运行时每日自动扫描 |
| `src/DiskWatch.App/MainWindow.cs` | 数据与运行说明 | 数据与运行 |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | 对比两次扫描中每个文件的大小和修改时间：蓝色为新增，灰色删除线为已删除；目录按增长幅度分级（红 显著增长、橙 快速增长、黄 轻微增长）。只读展示，不会删除任何文件。 | 对比两次扫描的文件变化。蓝色为新增，灰色删除线为已删除；目录按增长幅度分为红色显著增长、橙色快速增长、黄色轻微增长。 |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | “变化明细”里目录和文件按增长幅度分级（与 11 个原因分类并存）。阈值为“严格大于”。 | 目录和文件按增长幅度分级，阈值为严格大于。 |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | 快速增长还要求增长率超过多少 %（原来没有的目录按无穷大计） | 快速增长还需增长率超过多少 %（新建目录视为超过该比例） |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | 每个扫描范围保留几份文件级明细（0 = 不记录；更早的快照仍保留目录级统计） | 每个扫描范围保留几份文件明细（0 表示不记录；此前扫描仍保留目录统计） |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | 从下一次完整扫描开始记录文件级明细，再扫一次即可对比 | 将从下一次完整扫描开始记录文件明细。再扫描一次后即可对比。 |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | 已在设置中关闭文件级明细 | 已在设置中关闭文件明细 |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | 暂无同卷、同方式且覆盖完整的两份明细；读取问题不会被判断为删除 | 还没有可对比的两次完整扫描。读取失败的文件不会被当作已删除。 |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | 对比 {p.Before.Finished.ToLocalTime():MM-dd HH:mm} 与 {p.After.Finished.ToLocalTime():MM-dd HH:mm} 两次扫描 · 文件级新增 / 变更 / 删除 | 对比 {p.Before.Finished.ToLocalTime():MM-dd HH:mm} 与 {p.After.Finished.ToLocalTime():MM-dd HH:mm} 两次扫描的文件新增、变更和删除 |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | 对比：上次 {b.Finished.ToLocalTime():yyyy-MM-dd HH:mm}（{b.Files:N0} 个文件）→ 当前 {a.Finished.ToLocalTime():yyyy-MM-dd HH:mm}（{a.Files:N0} 个文件），两次都是{MethodName(a.RuleVersion)}、同一套规则。大小按“占… | 对比：上次 {b.Finished.ToLocalTime():yyyy-MM-dd HH:mm}（{b.Files:N0} 个文件）→ 当前 {a.Finished.ToLocalTime():yyyy-MM-dd HH:mm}（{a.Files:N0} 个文件）。大小按磁盘占用计算。 |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | 文件级明细保存失败（快照已保存）： | 文件明细保存失败（扫描结果已保存）： |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | 文件级明细已保存：{snapshot.Root}，{details.Files.Count:N0} 个文件，{details.Directories.Count:N0} 个目录，压缩后 {Labels.SmartSize(bytes)}（约 {bytes / (double)Math.Max(1, details.Files.Count):N1} 字节… | 文件明细已保存：{snapshot.Root}，{details.Files.Count:N0} 个文件，{details.Directories.Count:N0} 个目录，{Labels.SmartSize(bytes)} |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | 正在对比两次扫描的文件明细… | 正在对比两次扫描的文件变化… |
| `src/DiskWatch.App/ChangeDetailsPanel.cs` | 显著增长 / 快速增长阈值与明细保留份数 | 增长分级阈值与明细保留份数 |
| `src/DiskWatch.App/ChangeDetailsView.cs` | 本目录内没有直接的文件变化（看左侧子目录）。 | 本目录内没有直接的文件变化，请查看左侧子目录。 |
| `src/DiskWatch.App/ChangeDetailsView.cs` | {current.Path} · 上次 {Labels.SmartSize(current.BeforeBytes)} · 当前 {Labels.SmartSize(current.AfterBytes)} · 变化 {Labels.SignedSize(current.Delta)}（{Growth.Name(grade)}）· 新增 {curren… | {current.Path}，上次 {Labels.SmartSize(current.BeforeBytes)}，当前 {Labels.SmartSize(current.AfterBytes)}，变化 {Labels.SignedSize(current.Delta)}（{Growth.Name(grade)}），新增 {current.Added… |
| `src/DiskWatch.App/WeeklyPanel.cs` | 各盘空间变化、增长最多的应用、提醒和塞满预测。 | 各盘空间变化、增长较多的应用、提醒和塞满预测。 |
| `src/DiskWatch.App/WeeklyPanel.cs` | 每天一根柱子表示当天最后一次采样的已用空间；没有采样的日子空着，不会用 0 来充数。 | 柱状图表示当天已用空间，没有记录的日期不显示柱子。 |
| `src/DiskWatch.App/WeeklyPanel.cs` | 这一周还没有容量采样。程序运行并开始记录之后，这里会出现每天的占用变化。 | 这一周还没有容量记录。程序运行并开始监测后，将显示每天的占用变化。 |
| `src/DiskWatch.App/WeeklyPanel.cs` | 这一周还没有这个盘的容量采样。 | 这一周还没有这个磁盘的容量记录。 |
| `src/DiskWatch.App/WeeklyPanel.cs` | 程序运行并开始记录之后，这里会出现每天的占用柱状图。 | 开始监测后，将在这里显示每天的占用变化。 |
| `src/DiskWatch.App/WeeklyPanel.cs` | 按每天剩余空间最低值的中位数趋势估算，只用容量采样；数据不足时不给出天数。 | 根据近期剩余空间变化估算，数据不足时不给出天数。 |
| `src/DiskWatch.App/WeeklyPanel.cs` | 还没有可对比的两次扫描。需要同一种扫描方式、同一套规则的两次扫描，才会出现增长排行。 | 还没有可对比的两次扫描。完成两次完整扫描后即可查看增长排行。 |
| `src/DiskWatch.App/WeeklyPanel.cs` | 两次可对比的扫描之间，没有发现增长的应用 / 来源。 | 两次扫描之间没有发现增长的应用或来源。 |
| `src/DiskWatch.App/WeeklyPanel.cs` |  · 有采样间断（例如关机或退出程序） | ，期间监测有中断（例如关机或退出程序） |
| `src/DiskWatch.App/WeeklyPanel.cs` |  · 统计到生成时 | ，统计至生成时 |
| `src/DiskWatch.App/WeeklyPanel.cs` | 这天没有容量采样（程序未运行或刚开始记录） | 这一天没有容量记录（程序未运行或刚开始监测） |
| `src/DiskWatch.App/WeeklyPanel.cs` | 塞满预测：没有足够的采样，暂不预测 | 塞满预测：记录不足，暂不预测 |
| `src/DiskWatch.App/WeeklyPanel.cs` | 已用净变化 · 累计增长 {row.ObservedGrowthGiB:N2} GiB，释放 {row.ObservedReleaseGiB:N2} GiB | 已用净变化，累计增长 {row.ObservedGrowthGiB:N2} GiB，释放 {row.ObservedReleaseGiB:N2} GiB |
| `src/DiskWatch.App/WeeklyPanel.cs` | 现在 {Labels.SmartSize(row.CurrentBytes)} · 占所在盘本次增长 {row.Share:P0} | 当前 {Labels.SmartSize(row.CurrentBytes)}，占所在磁盘本次增长 {row.Share:P0} |
| `src/DiskWatch.App/OverviewPanels.cs` | 你的磁盘 | 磁盘 |
| `src/DiskWatch.App/OverviewPanels.cs` | 尚无扫描快照。点击“扫描配置范围”开始扫描。 | 还没有扫描结果。请先选择范围并开始扫描。 |
| `src/DiskWatch.App/TrendChart.cs` | 此时间范围暂无采样。程序运行后每分钟记录一次容量。 | 此时间范围还没有容量记录。程序运行后会定期记录剩余空间。 |
| `src/DiskWatch.App/TrendChart.cs` | 横轴：本地时间 · 悬停查看数值 · 采样间断处断线 · 净变化以所选范围首个样本为基线 | 横轴为本地时间。鼠标悬停可查看数值，监测中断处曲线会断开。净变化以所选范围的第一个记录为基准。 |
| `src/DiskWatch.App/OverviewPanels.cs` | {trendSamples.Count:N0} 个历史容量样本；每 {settings.SampleSeconds} 秒采样。曲线反映卷容量变化，不代表具体目录或写入进程。选择单个卷可查看细微变化。 | {trendSamples.Count:N0} 条容量记录，每 {settings.SampleSeconds} 秒记录一次。曲线反映整盘容量变化，不对应具体目录或进程。选择单个磁盘可查看更细的变化。 |
| `src/DiskWatch.App/ScopePanel.cs` | 扫描范围：勾选要扫描的磁盘或文件夹 | 勾选要扫描的磁盘或文件夹 |
| `src/DiskWatch.App/ScopePanel.cs` | 没有检测到可扫描的磁盘。可用「添加文件夹…」选择范围。 | 没有检测到可扫描的磁盘。请使用「添加文件夹…」选择范围。 |
| `src/DiskWatch.App/ScopePanel.cs` | 排除目录：这些文件夹不扫描（会在覆盖说明中标注） | 排除目录：这些文件夹不参与扫描，并在覆盖说明中标注 |
| `src/DiskWatch.App/ScopePanel.cs` | 例如扫描范围、排除目录或「MFT 快速扫描」。不保存的话，这次会按旧设置扫描： | 例如扫描范围、排除目录或「NTFS 快速扫描」。若不保存，本次将按原来的设置扫描： |
| `src/DiskWatch.App/ScopePanel.cs` | 设置页有未保存的更改 | 设置中有未保存的更改 |
| `src/DiskWatch.App/GlossaryPanel.cs` | 搜索栏目或术语，展开即可阅读说明。 | 搜索栏目或术语，展开查看说明。 |
| `src/DiskWatch.App/WatchtowerProgress.cs` | 哨塔扫描进度 | 扫描进度 |
| `src/DiskWatch.App/WatchtowerProgress.cs` | 正在读取目录，进度估算中 | 正在读取目录，正在估算进度 |
| `src/DiskWatch.App/ModernShell.cs` | 哨塔图标 | 程序图标 |
| `src/DiskWatch.Core/Models.cs` | 已获管理员权限：可用 MFT 快速扫描 | 已获得管理员权限，可使用 NTFS 快速扫描 |
| `src/DiskWatch.Core/Models.cs` | 已获管理员权限：MFT 快速扫描已在设置中关闭，使用常规扫描 | 已获得管理员权限，快速扫描已关闭，将使用普通扫描 |
| `src/DiskWatch.Core/Models.cs` | 未获管理员权限：使用常规扫描（下次启动时在管理员确认窗口点「是」即可使用 MFT 快速扫描） | 未获得管理员权限，将使用普通扫描。下次启动时在权限确认窗口点击「是」，即可使用快速扫描 |
| `src/DiskWatch.Core/ScanEstimator.cs` | 正在读取文件目录（MFT） | 正在读取文件目录 |
| `src/DiskWatch.Core/ScanEstimator.cs` | 正在扫描（没有可参照的总量，无法估算百分比） | 正在扫描，正在估算进度 |
| `src/DiskWatch.Core/AppRanking.cs` | 这次扫描没有应用归属数据（更新前的旧扫描不记录）。下一次扫描后显示当前占用，再扫一次就能看到增长。 | 这次扫描没有应用占用记录。完成一次新的扫描后可查看当前占用，再扫描一次后可查看增长。 |
| `src/DiskWatch.Core/AppRanking.cs` | 还没有可对比的上一次扫描（需要同一个盘、同一种扫描方式、同一套规则），先按当前占用排序。 | 还没有可对比的上一次扫描，先按当前占用排序。 |
| `src/DiskWatch.Core/AppRanking.cs` |  只按文件所在目录归属，不代表一定是该程序写入。 |  按文件所在位置归类，不一定是该程序写入。 |
| `src/DiskWatch.Core/AppRanking.cs` | 对比 {baseline.Finished.ToLocalTime():MM-dd HH:mm} → {current.Finished.ToLocalTime():MM-dd HH:mm}（相隔 {Span(span)}）的两次扫描，同一种扫描方式、同一套规则。 | 对比 {baseline.Finished.ToLocalTime():MM-dd HH:mm} 至 {current.Finished.ToLocalTime():MM-dd HH:mm}（间隔 {Span(span)}）的两次扫描。 |
| `src/DiskWatch.Core/AppRanking.cs` |  两次分别有 {baseline.Errors:N0} / {current.Errors:N0} 项读取问题，读不到的文件不计入。 |  两次分别有 {baseline.Errors:N0} / {current.Errors:N0} 项读取失败，未读到的文件不计入。 |
| `src/DiskWatch.Core/AppRanking.cs` |  没有更早的可比扫描，实际只覆盖 {Span(span)}。 |  没有更早的可对比扫描，实际只覆盖 {Span(span)}。 |
| `src/DiskWatch.Core/WeeklySummary.cs` | 本周没有可对比的两次扫描（需要同一种扫描方式、同一套规则） | 本周没有可对比的两次扫描 |
| `src/DiskWatch.Core/WeeklySummary.cs` | 本周没有容量采样记录 | 本周没有容量记录 |
| `src/DiskWatch.Core/WeeklySummary.cs` | （有采样间断，如关机或退出程序） | （监测有中断，例如关机或退出程序） |
| `src/DiskWatch.Core/WeeklySummary.cs` | 数据来源：容量采样、目录扫描和提醒记录。 | 数据来源：容量记录、目录扫描和提醒记录。 |
| `src/DiskWatch.Core/WeeklySummary.cs` | 采样还不够，暂不预测 | 记录还不够，暂不预测 |
| `src/DiskWatch.Core/WeeklySummary.cs` | 这一周还没有容量采样 | 这一周还没有容量记录 |
| `src/DiskWatch.Core/FillForecast.cs` | 近 {WindowDays} 天：采样不足 1 天，暂不预测 | 近 {WindowDays} 天：记录不足 1 天，暂不预测 |
| `src/DiskWatch.Core/Alerts.cs` | {sample.Root} 剩余 {free:N2} GiB，已达到 {crossed.Min():G} GiB 提醒档位。请检查占用来源。 | {sample.Root} 剩余 {free:N2} GiB，已达到 {crossed.Min():G} GiB 提醒档位。请查看占用来源。 |
| `src/DiskWatch.Core/Alerts.cs` | {sample.Root} 在 {(sample.Time - peak.Time).TotalMinutes:N1} 分钟内减少 {Labels.Size(peak.FreeBytes - sample.FreeBytes)} 可用空间。目录扫描后可查看来源。 | {sample.Root} 在 {(sample.Time - peak.Time).TotalMinutes:N1} 分钟内减少 {Labels.Size(peak.FreeBytes - sample.FreeBytes)} 可用空间。扫描完成后可查看来源。 |
| `src/DiskWatch.Core/AutoCapture.cs` | {head}：没有可对比的上一次同方式扫描，本次结果已保存为基线。触发原因：{alertMessage} | {head}：没有可对比的上一次扫描，本次结果已保存为基线。触发原因：{alertMessage} |
| `src/DiskWatch.Core/AutoCapture.cs` | {head}：和 {previous.Finished.ToLocalTime():MM-dd HH:mm} 的扫描相比，扫描到的文件没有明显增长；空间可能被读不到的区域、还原点或已删除的临时文件占用过。触发原因：{alertMessage} | {head}：与 {previous.Finished.ToLocalTime():MM-dd HH:mm} 的扫描相比，扫描到的文件没有明显增长。空间变化可能来自未能读取的区域、还原点或已删除的临时文件。触发原因：{alertMessage} |
| `src/DiskWatch.Core/FileDiff.cs` | 覆盖不完整、卷身份或扫描口径不一致，不能判断文件新增 / 删除。 | 两次扫描的范围或完整性不一致，不能判断文件新增或删除。 |
| `src/DiskWatch.Core/ScanSummary.cs` | 磁盘扫描总结\n说明：以下是各范围最近保存的快照，具体时间见每节；不是同一时刻的全盘快照。\n | 磁盘扫描总结\n说明：以下是各范围最近一次扫描结果，具体时间见各节。不是同一时刻的整盘快照。\n |
| `src/DiskWatch.Core/ScanSummary.cs` |   基线不足、覆盖不完整或规则发生变化：不将目录差值解释为确定增长/删除。请结合卷容量曲线和占用列表核查。 |   缺少可对比的完整扫描，或不具备对比条件：不将目录差值视为确定的增长或删除。请结合容量曲线和占用列表核对。 |
| `src/DiskWatch.Core/ScanSummary.cs` | 主要占用目录（互不重复统计桶，前 5 项）： | 主要占用目录（前 5 项，互不重复）： |
| `src/DiskWatch.Core/ScanSummary.cs` | 值得查看的大文件（分配空间前 5 项，不代表可删除）： | 占用较大的文件（前 5 项，不代表可以删除）： |
| `src/DiskWatch.Core/ScanSummary.cs` | 没有记录到达到大文件阈值的项目。 | 没有达到记录条件的大文件。 |
| `src/DiskWatch.Core/ScanSummary.cs` | 此范围为子目录，不计算全卷未解释差额。 | 当前为文件夹扫描，不计算整盘未解释差额。 |
| `src/DiskWatch.Core/ScanSummary.cs` | 注意：差额为负，可能存在扫描时差或计量误差，需要校准。 | 注意：差额为负，可能存在扫描时差或计量误差，需要核对。 |
| `src/DiskWatch.Core/ScanSummary.cs` | 未解释差额 {Labels.Size(residual)}；包括未覆盖区域、文件系统元数据和扫描时差，不等同于还原点或可清理空间。 | 未解释差额 {Labels.Size(residual)}。可能包括未覆盖区域、文件系统开销和扫描时差，不等于还原点或可清理空间。 |
| `src/DiskWatch.Core/ScanSummary.cs` | \n覆盖说明：读取问题 {s.Errors:N0}；跳过重解析点 {s.ReparsePointsSkipped:N0}；硬链接去重 {s.HardLinksSkipped:N0}。 | \n覆盖说明：读取失败 {s.Errors:N0} 项；跳过链接点 {s.ReparsePointsSkipped:N0}；硬链接已去重 {s.HardLinksSkipped:N0}。 |
| `src/DiskWatch.Core/ScanSummary.cs` | 关注：剩余空间已低于或等于 20 GiB，请优先检查占用来源。 | 注意：剩余空间已不高于 20 GiB，请优先查看占用来源。 |
| `src/DiskWatch.Core/ScanSummary.cs` | 相比 {previous.Finished.ToLocalTime():MM-dd HH:mm}：卷已用净变化 {(s.After.UsedBytes-previous.After.UsedBytes)/1073741824d:+0.000;-0.000;0.000} GiB（卷容量观测，不等于文件归因）。 | 相比 {previous.Finished.ToLocalTime():MM-dd HH:mm}：已用空间净变化 {(s.After.UsedBytes-previous.After.UsedBytes)/1073741824d:+0.000;-0.000;0.000} GiB（整盘容量变化，不等于文件增减合计）。 |
| `src/DiskWatch.Core/ScanSummary.cs` | 完成于 {s.Finished.ToLocalTime():yyyy-MM-dd HH:mm:ss} · 耗时 {(s.Finished-s.Started).TotalSeconds:N0} 秒 · {s.Files:N0} 个文件 | 完成于 {s.Finished.ToLocalTime():yyyy-MM-dd HH:mm:ss}，用时 {(s.Finished-s.Started).TotalSeconds:N0} 秒，{s.Files:N0} 个文件 |
| `src/DiskWatch.Core/CleanupPrompt.cs` | > 导出时间：{(now ?? DateTimeOffset.Now).ToLocalTime():yyyy-MM-dd HH:mm}。本提示词由 DiskWatch 根据最近一次扫描快照自动生成，只是建议清单；请按下面的安全规则逐步执行，每个会删除或移动文件的步骤都必须先得到我的确认。 | > 导出时间：{(now ?? DateTimeOffset.Now).ToLocalTime():yyyy-MM-dd HH:mm}。本提示词根据最近一次扫描结果生成，仅作建议清单。请按下面的安全规则逐步执行，每个会删除或移动文件的步骤都必须先得到我的确认。 |
| `src/DiskWatch.Core/CleanupPrompt.cs` | - 系统：Windows，你可以使用 PowerShell（普通用户权限）。 | - 系统：Windows。可使用 PowerShell（普通用户权限）。 |
| `src/DiskWatch.Core/CleanupPrompt.cs` | 不可删除清单（绝对不要触碰） | 不可删除清单（一律不碰） |
| `src/DiskWatch.Core/CleanupPrompt.cs` | 以下路径和规则范围内的任何文件，都不得删除、移动、重命名、修改权限或属性；即使看起来占用很大也不行。如果某个操作会波及它们，立即停止并告诉我。 | 以下路径和规则范围内的任何文件，都不得删除、移动、重命名或修改权限与属性，即使占用很大也不行。如果某项操作会波及这些文件，立即停止并说明情况。 |
| `tests/DiskWatch.Tests/Program.cs` | idx.Phase.Contains("MFT") | idx.Phase.Contains("文件目录") |
| `tests/DiskWatch.UiChecks/Program.cs` | 正在读取文件目录（MFT） | 正在读取文件目录 |
| `tests/DiskWatch.UiChecks/Program.cs` | 读取 MFT 索引 | 正在读取文件目录 |
| `tests/DiskWatch.Tests/FeatureTests.cs` | 本周没有容量采样记录 | 本周没有容量记录 |
| `tests/DiskWatch.Tests/FeatureTests.cs` | 数据来源：容量采样、目录扫描和提醒记录。 | 数据来源：容量记录、目录扫描和提醒记录。 |
| `tests/DiskWatch.Tests/FeatureTests.cs` | @"D:\：没有足够的采样" | @"D:\：没有足够的记录" |
| `tests/DiskWatch.Tests/Program.cs`（同步扫描总结断言） | 不将目录差值解释为确定增长 | 不将目录差值视为确定的增长 |
| `tests/DiskWatch.Tests/Program.cs`（同步扫描总结断言） | 不计算全卷未解释差额 | 不计算整盘未解释差额 |
| `src/DiskWatch.App/InsightsPanels.cs`（应用排行长说明移入提示） | (长说明正文) | 改为面板提示：按安装位置识别应用，对比最近两次扫描计算增长。 |
| `src/DiskWatch.App/ModernShell.cs`（总览副标题） | 先选择磁盘，再查看你关心的内容。 | 选择磁盘，查看空间占用。 |
| `src/DiskWatch.App/ModernShell.cs`（大文件副标题） | 查看占用最大的前 20 项 | 占用最大的前 20 项 |
| `src/DiskWatch.App/MainWindow.cs`（设置项拆成名称和说明） | MFT 快速扫描（作为勾选框全文） | NTFS 快速扫描（短名称）+ 帮助：在 NTFS 磁盘上加快扫描。需管理员权限，不可用时自动改用普通扫描。 |
| `src/DiskWatch.App/MainWindow.cs`（数据与运行副标题） | 本地保存 · 无自动清理 | 记录保存在本机 |
| `src/DiskWatch.App/Program.cs`（单实例提示） | DiskWatch2 已在当前会话运行，请从系统托盘打开。 | DiskWatch2 已在运行，请从托盘打开。 |
| `src/DiskWatch.App/Program.cs`（跨权限单实例提示） | DiskWatch2 已在其他权限的会话中运行，请从托盘打开已有实例。 | DiskWatch2 已在运行（权限不同），请从托盘打开已有窗口。 |
| `src/DiskWatch.Core/WeeklySummary.cs`（周报预测空状态） | 没有足够的采样，暂不预测 | 没有足够的记录，暂不预测 |
| `src/DiskWatch.App/ModernShell.cs`（进度条识别读取目录阶段） | e.Phase.Contains("MFT") | e.Phase.Contains("文件目录") |
| `src/DiskWatch.Windows/MftScanner.cs`（扫描进度） | 读取 MFT 索引 | 正在读取文件目录 |
