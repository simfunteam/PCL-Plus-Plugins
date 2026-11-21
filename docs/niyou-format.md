# .niyou 插件包格式 v1

`.niyou` 本质是 zip（UTF-8 文件名），用于分发 PCL-Plus 插件。

## 结构

```
plugin.niyou
├── manifest.json        必需
└── payload/             可选：原样复制到 PCL 所在目录
    └── PCL2Plus.dll
```

## manifest.json 字段

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `id` | string | ✓ | 唯一标识，建议 kebab-case |
| `name` | string | ✓ | 显示名 |
| `version` | string | ✓ | 语义化版本 |
| `author` | string |  | 作者 |
| `description` | string |  | 一句话说明 |
| `patches` | string[] |  | 要施加的 IL 补丁：`download` / `legal` / `dispatch` |
| `hook` | string |  | 需要注入 PCL 进程的 DLL 文件名，必须存在于 `payload/` |

## 补丁语义

- `download`：恢复「原版游戏」下载页——放行 `PageDownloadLeft` 里的 `ItemInstall`/`LabResource`，并把 Loader 指向 `ModDownload.DlClientListLoader`
- `legal`：把 `throw new Exception("…法律法规…")` 替换成正常返回；**跳过 `PageDownloadInstall`**，否则下载页会因空数据报 NRE
- `dispatch`：在 `ModModpack.ModpackInstall` 原本抛“法律”的位置插回对 `InstallPackModrinth` 的调用

补丁都基于 `PCL\Plugins\baseline\PCL2.base.exe` 重新生成，**不会叠加**。

## 版本与兼容

- `patches` 里的符号与 PCL 2.99.0.1 的内部结构对应；PCL 大版本升级后可能需要更新
- `--status` 可检查目标 exe 是否已挂补丁
