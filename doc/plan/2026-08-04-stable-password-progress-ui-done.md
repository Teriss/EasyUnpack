# 密码等待记录与进度条闪烁修复

实施范围：合并密码库探测与人工输入为单一操作记录，并按 Exact > Estimated > Indeterminate 合并并发进度事件，避免界面降级和进度条重建。

完成记录（2026-08-05）：

- 密码库候选和人工重试共用一个密码操作 ID；错误候选不产生 Failed 事件，等待输入无超时，取消时才终止该操作。
- 操作范围内合并精度、字节数、文件数和单调百分比；应用窗口只消费操作事件，保留行和控件实例不变。
- 更新 `doc/architecture/progress-reporting.md` 与 `doc/architecture/engine-adapters.md`，增加回归测试。
- `dotnet test EasyUnpack.slnx --configuration Release --no-restore`：85 项通过；`dotnet build EasyUnpack.slnx --configuration Release --no-restore`：0 警告、0 错误。
- 使用 Inno Setup 6.7.3 构建并覆盖安装 `artifacts/installer/EasyUnpack-Setup.exe`；安装版本保持 `1.0.3` 且信息版本包含构建提交哈希，开始菜单快捷方式无参数，壳扩展注册和独立 ICO 校验通过，启动和退出后无残留引擎进程。
- 本轮仅创建本地提交；未推送、未移动标签、未修改 GitHub Release。保留用户未跟踪的 `2026-07-27` 计划文件。
