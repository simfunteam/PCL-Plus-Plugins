modpack-drop 的 payload 里需要 PCL2Plus.dll（构建产物）。
若已按 README 编译出 PCL2Plus.dll，放到本目录 payload\ 下即可用 NiyouPacker 重新打包：

  NiyouPacker.exe packages\modpack-drop ..\bin\plugins\modpack-drop-1.0.0.niyou
