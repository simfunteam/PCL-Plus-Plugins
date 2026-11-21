@echo off
chcp 65001 >nul
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
cd /d "%~dp0"

echo [1/3] 构建注入器
"%CSC%" /nologo /target:exe /platform:anycpu /optimize+ /r:"..\bin\Mono.Cecil.dll" ^
  /out:"..\bin\PCL2Plus 启动器.exe" ^
  injector\Launcher.cs injector\LegalGate.cs injector\DispatchRestorer.cs || goto :err

echo [2/3] 构建插件管理器
"%CSC%" /nologo /target:winexe /platform:anycpu ^
  /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" /r:"%FW%\System.IO.Compression.dll" /r:"%FW%\System.IO.Compression.FileSystem.dll" ^
  /r:"%FW%\System.Web.Extensions.dll" /r:"%FW%\System.Windows.Forms.dll" ^
  /out:"..\bin\PCL-Plus_Plugins v1.0.0.exe" app\PCL-Plus_Plugins.cs || goto :err

echo [3/3] 打包插件包
..\bin\NiyouPacker.exe packages\modpack-drop ..\bin\plugins\modpack-drop-1.0.0.niyou
..\bin\NiyouPacker.exe packages\vanilla-install ..\bin\plugins\vanilla-install-1.0.0.niyou

echo 全部完成
exit /b 0
:err
echo 构建失败
exit /b 1