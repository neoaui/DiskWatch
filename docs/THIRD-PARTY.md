# 第三方组件

本项目直接引用 Microsoft.Data.Sqlite 10.0.15（MIT），传递引用 Microsoft.Data.Sqlite.Core 10.0.15（MIT）以及 SQLitePCLRaw 2.1.13（NuGet 元数据声明 Apache-2.0），实测 SQLite 引擎 3.53.3。具体依赖图保存在各项目 `obj/project.assets.json` 和发布包 `.deps.json`。

- Microsoft.Data.Sqlite：https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.15
- SQLitePCLRaw：https://github.com/ericsink/SQLitePCL.raw
- SQLite 引擎：https://www.sqlite.org/copyright.html

正式外部分发前应随包附上完整的适用许可文本和 notices；本次为本机开发验证，不做公开分发。

首次 .NET 10 审计发现之前 2.1.11 原生 SQLite 包受 CVE-2025-6965 影响，现已更新并增加实际引擎版本回归测试。参考：https://github.com/advisories/GHSA-2m69-gcr7-jv3q 。
