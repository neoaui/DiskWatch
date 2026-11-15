DiskWatch 0.1.0 使用说明
=======================

运行环境
  Windows 10 或 Windows 11（64 位）。本压缩包已包含运行环境，不必另装 .NET。

如何使用
  1. 把本压缩包完整解压到一个空文件夹（不要只拿出 DiskWatch.exe）。
  2. 双击 DiskWatch.exe。
  3. 启动时会请求管理员权限：
     - 点「是」：可使用 NTFS 快速扫描
     - 点「否」：仍可使用，并自动改用普通扫描，其他功能相同

关于「Windows 已保护你的电脑」
  本程序尚未进行数字签名，SmartScreen 可能显示此提示。可点「更多信息」，再点「仍要运行」。

数据保存在哪里
  扫描结果、设置和日志保存在本机：
  %LOCALAPPDATA%\DiskWatch
  （一般是 C:\Users\你的用户名\AppData\Local\DiskWatch）
  不会写入解压目录，也不会上传到网络。

如何卸载
  删除解压出的文件夹，再删除上面的数据目录即可。
  本程序不会安装服务，也不会注册开机启动或计划任务。

源代码
  https://github.com/neoaui/DiskWatch
  本程序依照 GNU 通用公共许可证第 3 版（GPL-3.0）发布，完整文本见同目录中的 LICENSE。