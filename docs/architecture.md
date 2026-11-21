# 架构

```
 官方 PCL2（不做任何修改）
      ↑ 注入（IL 改写 + 可选宿主 DLL）
 PCL2Plus 启动器（Mono.Cecil）
      ↑ 读取 manifest，拼装参数 --patch/--hook
 PCL-Plus_Plugins（WPF 管理器）
      ↑ 打包
 NiyouPacker（开发者出包工具） → *.niyou
```

## 注入流程

1. 首次注入时把目标 exe 复制为 `Plain Craft Launcher 2.exe.pcl2plus.bak`
2. 复制一份基线到 `PCL\Plugins\baseline\PCL2.base.exe`
3. 读取基线，按 `patches` 施加 IL 改写，必要时调用 `InjectHook` 在 `FormMain..ctor` 末尾插入 `call PCL2Plus.Bootstrap::Init()`
4. 写回目标 exe（重试 10×300ms，避开文件占用）
5. 还原时把 `.bak` 覆盖回去，并删除开关标记

## 运行期（hook 模式）

`PCL2Plus.Bootstrap.Init()` 在 UI 线程空闲时执行：

- 安装 `DropHook`：在窗口的 `DragEnter` 阶段判定 `.mrpack`/`.zip`，把 `Effects` 置 `None` 阻止 Windows 派发 `Drop`，并在后台线程启动安装
- 安装过程以 PCL 的 `LoaderTask` + `LoaderCombo` 提交，因此会出现在 PCL 的**任务管理**与右下角任务图标里

## 整合包安装

- `.mrpack`：解析 `modrinth.index.json`，按 `files[].downloads[0]` 下载，解压 `overrides/`，用 Fabric 官方 profile 生成版本 JSON，复制基版 jar，写 `PCL\Setup.ini`
- `.zip`（CurseForge）：解析 `manifest.json`，逐个用 `mod.mcimirror.top/curseforge/v1/mods/{pid}/files/{fid}` 取文件名与直链下载，解压 `overrides/`
- 失败文件在全部下载完后重试 3 轮，仍失败则写 `PCL-Plus-安装失败清单.txt`

## 目录

见 README 的「目录约定」。
