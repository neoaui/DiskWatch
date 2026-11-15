# 第 1 期统计与提醒口径

## 分类和对账

每个可测量文件实体只分入一个主分类。优先级：用户最具体纠正、特殊文件、回收站、临时目录、Windows、开发、游戏库、缓存/应用数据、厂商、安装清单、Program Files、个人、其他。

第 10 类显示名在 `DiskWatch.Core/Models.cs` 的 `Labels.CategoryName` 中集中维护。内部枚举 `Unexplained` 不依赖最终界面文案。

卷级 11 类总和 = 10 个可扫描分类的分配空间 +（扫描结束时卷已用 − 已扫描分配空间）。差额是带符号对账项，不把其全部认定为 VSS、系统文件或无法访问 WindowsApps。负差额不截零、不按比例摊给其他分类。子目录扫描不做全卷对账。

目录桶只取相对根目录前 4 层，直接位于浅层目录的文件保存在该目录桶；深层文件保存在第 4 层桶。父桶不重复包含子桶总数。所有大小文件参与统计，50 MiB 门槛只控制大文件展示（前 100 项）。

文件标识去重用于多硬链接实体。junction、符号链接、云重解析点不跟随；其实际目标由实际卷扫描覆盖。扫描过程中重命名、大小变化、访问权限变化等仍可能使结果不一致，因此保存采集窗口和问题摘要。

## 提醒状态机

- 低空间：默认 20 / 11 / 3 GiB；每次空间从健康区进入低空间区，每档提醒一次。
- 首次运行已跨多个阈值，只发当前最严重的一条，并记录已跨档，避免连弹。
- 空间恢复超过该阈值且达到阈值 + 1 GiB 后重新布防。
- 骤降：默认连续采样在 10 分钟窗口内下降至少 2 GiB，30 分钟冷却。采样间隔超过 3 个周期、时钟回拨或卷变化，重建比较窗口。
- 采样、提醒历史和触发状态在 SQLite 同一事务提交。系统勿扰可能隐藏通知，但应用保留提醒历史。
- 用户暂停时取消进行中的扫描，停止后续采样；主动点击“刷新容量”仍会执行一次采样。

## 日周月报

UTC 存储、本地时区分组，周一为周界。跨期采样差值记入后一次观测所在期；显示实际首末观测时刻，不声称有精确午夜存量。增长 − 释放 = 观测净变化，跨期差值不重复计数。睡眠、关机和退出期间不补造瞬态过程。

## 限额

默认数据库上限 512 MiB（不含短期 WAL、导出与构建缓存），采样/提醒保留 400 天，每范围保留 60 份快照，日志约 2 MiB。磁盘不足 128 MiB 时暂停数据库新增写入。SQLite 写入失败显示错误，不静默丢弃或无限重试。目录桶/待处理目录/硬链接标识设置资源上限，触发后保存部分结果并标记 ResourceLimit。

## 官方资料与产品决策

- 更新空间需求通常为功能更新 6–11 GB 或更多、质量更新 2–3 GB 或更多：https://support.microsoft.com/en-gb/windows/deployment/updates-lifecycle/free-up-space-for-windows-updates
- 20 GB 参考来自 Windows Server 更新错误排障建议，不是 Windows 11 强制阈值：https://learn.microsoft.com/en-us/troubleshoot/windows-server/installing-updates-features-roles/troubleshoot-windows-update-error-0x80070070
- 产品采用 GiB 表示容量，20/11/3 GiB 为预警策略，不伪装成官方保证值。
- SQLite 事务：https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions
- 后期 USN 限制：https://learn.microsoft.com/en-us/windows/win32/fileio/change-journal-records
