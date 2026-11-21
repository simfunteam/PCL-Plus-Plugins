# 更新日志

## v1.0.0 (2026-11-22)

首个公开版本。

- 插件管理器：选择 PCL、插件列表（`.niyou`）、启停开关、应用到 PCL、启动 PCL、还原原版
- 注入器：`--ensure / --restore / --status / --run`，支持 `--exe / --patch / --hook / --no-ui / --force / --quiet / --wait-pid`
- 补丁：`download`（原版游戏下载页）、`legal`（清除法律法规拦截）、`dispatch`（恢复整合包派发）
- 插件包格式 `.niyou` + 打包器 `NiyouPacker.exe`
- 插件宿主：拖拽接管安装整合包（`.mrpack` / CurseForge `.zip`），提交到 PCL 任务管理
- 修复：注入器等待按键导致管理器假死；`--restore` 未生效；缺少 `Mono.Cecil.dll` 导致注入静默失败
