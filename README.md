# PCL-Plus Plugins

## 项目介绍
由于**Plain Craft Launcher 2（PCL2）** 遭到了大量举报投诉，导致了部分功能下架并无法使用。为了能有更好的体验，通过大量开发，做出了
**PCL-PLUS Plugins 即PCL插件增强版 **

这是一个给 **Plain Craft Launcher 2（PCL2）** 用的插件体系：一个独立的插件管理器 + 一个 IL 注入器 + 自定义插件包格式（`.niyou`）。
目前开发了2款可注入式插件，后续会更新更多插件和功能，进一步完善或优化PCL2
由于受到技术因素，目前的多项测试中，仅能保证原版游戏下载修复插件是100%有效的，但是拖拽安装整合包仍需要优化与完善
该项目理论上只要有足够的技术，完全可以写出一个很丰富的插件生态以及许多实用型插件

PCL 本体保持官方原版、不做任何源码改动；所有功能都在**注入时**由外部工具施加，随时可一键还原。

---
```
注：免责声明，该项目由个人软件爱好者开发，所有代码与项目均与PCL2及其作者无关，该项目不会附带任何PCL2破解版、或者是PCL2的任何版本的下载方式，如果需要本体请前往官方网站仓库进行下载。这个项目仅提供我们的工具给大家使用。该软件仅限个人学习或使用，帮助新手小白更好的使用PCL2，请勿自行制作插件并上传至任何平台，否则后果自负。
```
---

## 目录结构

```
.
├─ bin/                        ← 可直接运行的分发
│  ├─ PCL-Plus_Plugins v1.0.0.exe   插件管理器（界面）
│  ├─ PCL2Plus 启动器.exe            注入器（IL 改写）
│  ├─ Mono.Cecil.dll                注入器依赖，必须与启动器同目录
│  ├─ PCL2Plus.dll                  插件宿主（拖拽接管等运行时功能）
│  ├─ NiyouPacker.exe               插件打包器
│  └─ plugins/
│     ├─ vanilla-install-1.0.0.niyou    原版游戏下载页恢复
│     └─ modpack-drop-1.0.0.niyou       整合包拖拽安装
├─ src/
│  ├─ app/       插件管理器源码（WPF，单文件）
│  ├─ injector/  IL 注入器源码（Mono.Cecil）
│  ├─ plugin/    插件宿主源码（被注入到 PCL 进程内）
│  └─ tools/     NiyouPacker / ScanLegal / DumpCatch
└─ docs/
   ├─ niyou-format.md     插件包格式
   └─ architecture.md     整体架构与注入点说明
```

---

## 快速开始

1. 把 `bin/` 里的全部文件放进**任意目录**（不要放进 PCL 的子目录）
2. 打开 `PCL-Plus_Plugins v1.0.0.exe`
3. 点右上角 **「选择 PCL…」**，选中你的 `Plain Craft Launcher 2.exe`
4. 左侧「插件列表」里确认要用的插件是 **已启用**
5. 点 **「应用到 PCL」** —— 注入器输出会显示在弹窗里，看到
   `插件钩子 : 已挂` / `下载补丁 : 已应用` 即成功
6. 点 **「启动 PCL」** 启动（推荐，见下方“注意事项”）

还原：点 **「还原原版」**，会从 `.pcl2plus.bak` 恢复官方 exe 并关闭下载补丁。

---
## 存在的BUG

目前的两个发行插件中，由于受到技术因素，目前的多项测试中，仅能保证原版游戏下载修复插件是100%有效的，但是拖拽安装整合包仍需要优化与完善

---

## 插件包 `.niyou`

`.niyou` 是一个 zip：

```
manifest.json
payload/            ← 原样复制到 PCL 所在目录（可选）
```

`manifest.json`：

```json
{
  "id": "modpack-drop",
  "name": "整合包拖拽安装",
  "version": "1.0.0",
  "author": "PCL-Plus",
  "description": "接管文件拖拽，自己解析 .mrpack 并安装为 PCL 实例",
  "patches": ["legal", "dispatch"],
  "hook": "PCL2Plus.dll"
}
```

`patches` 可选值：

| 值 | 作用 |
|---|---|
| `download` | 恢复“原版游戏”下载页（放行 `ItemInstall`、把 Loader 指向 `DlClientListLoader`） |
| `legal` | 清除 `throw new Exception("…法律法规…")` 硬拦截（不含下载页，避免留下空数据） |
| `dispatch` | 恢复被删掉的整合包格式派发（`InstallPackModrinth`） |

`hook`：需要注入到 PCL 进程的宿主 DLL 名（留空表示纯 IL 补丁）。

打包：

```bash
NiyouPacker.exe <插件工程目录> <输出.niyou>
NiyouPacker.exe --list <某个.niyou>
```

---

## 从源码构建

需要 .NET Framework 4.x 自带的 `csc.exe`（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`），无需 Visual Studio。

```bat
:: 注入器
csc /target:exe /platform:anycpu /optimize+ /r:Mono.Cecil.dll ^
    /out:"PCL2Plus 启动器.exe" ^
    src\injector\Launcher.cs src\injector\LegalGate.cs src\injector\DispatchRestorer.cs

:: 插件管理器
csc /target:winexe /platform:anycpu ^
    /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll ^
    /r:System.Xaml.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
    /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll ^
    /out:"PCL-Plus_Plugins v1.0.0.exe" src\app\PCL-Plus_Plugins.cs
```

插件宿主（`PCL2Plus.dll`）需要引用 PCL 本体的 `Plain Craft Launcher 2.exe`：

```bat
csc /target:library /platform:anycpu ^
    /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll ^
    /r:System.Xaml.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
    /r:System.Web.Extensions.dll /r:"Plain Craft Launcher 2.exe" ^
    /out:PCL2Plus.dll src\plugin\PCL2Plus.cs src\plugin\PluginPage.cs src\plugin\ModpackInstaller.cs
```

---

## 注意事项

- **PCL 会自动更新**：它可能把自己替换回官方 exe，补丁随之消失。所以请用管理器里的「启动 PCL」按钮启动——那条路径会先检查并重新打补丁。
- **`Mono.Cecil.dll` 必须和 `PCL2Plus 启动器.exe` 在同一目录**，否则注入器一启动就退出，表现为“提示成功但没生效”。
- **插件目录**默认是 `<PCL目录>\PCL\Plugins`（PCL 首次启动后才会创建）。也可以在下拉里手动指定。
- **整合包下载** 走 Modrinth CDN 与 CurseForge 镜像 `mod.mcimirror.top`；网络不通时可在 `PCL\Plugins\mirror.txt` 填镜像前缀。失败文件会重试 3 轮，仍在失败会生成 `PCL-Plus-安装失败清单.txt`。
- **磁盘空间**：整合包动辄 2–3 GB，注意目标盘剩余空间。

---

## 已知限制

- 插件页（PCL 内的独立页面）已移除，插件功能只保留拖拽接管与下载页恢复
- 整合包安装生成的版本 JSON 使用 Fabric 官方 profile（`meta.fabricmc.net`）；NeoForge / Forge 目前只写 `PCL\Setup.ini` 的加载器字段，加载器本体需由 PCL 自行安装
- CurseForge 包按 `projectID/fileID` 经镜像取文件名与直链，部分文件可能 404

---

## 目录约定（运行时）

```
<PCL目录>\
  Plain Craft Launcher 2.exe            目标
  Plain Craft Launcher 2.exe.pcl2plus.bak   官方原件备份
  PCL2Plus.dll                          插件宿主
  PCL\Plugins\                          开关标记 / 日志 / baseline
  PCL\Plugins\PCL2Plus.log              插件日志
  PCL\Plugins\baseline\PCL2.base.exe    基线副本
```

---

## License

MIT
