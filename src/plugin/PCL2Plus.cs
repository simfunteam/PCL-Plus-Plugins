// PCL2Plus —— PCL2 插件宿主与「插件」页
// 由注入到 FormMain..ctor 末尾的一行 IL 调用 Bootstrap.Init() 启动
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCL;

namespace PCL2Plus
{
    public static class Bootstrap
    {
        /// <summary>插件入口：加载即接管，异常一律吞掉并写日志，确保不影响 PCL 启动</summary>
        public static void Init()
        {
            try
            {
                // 设置项在启动后期才应用（会再把该按钮 Collapsed），推迟到空闲期安装
                var disp = System.Windows.Application.Current != null
                    ? System.Windows.Application.Current.Dispatcher
                    : System.Windows.Threading.Dispatcher.CurrentDispatcher;
                disp.BeginInvoke(new Action(delegate { PluginHost.Activate(); }),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(PluginHost.LogPath,
                        DateTime.Now.ToString("HH:mm:ss") + " Bootstrap 失败：" + ex + "\r\n");
                }
                catch { }
            }
        }
    }

    internal static class PluginHost
    {
        internal static FormMain Frm;
        internal static PluginPage Page;
        internal static PluginLeft Left;
        internal static MyRadioButton NavButton;

        internal static string PluginDir
        {
            get
            {
                string root = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "PCL");
                string dir = Path.Combine(root, "Plugins");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        internal static string LogPath { get { return Path.Combine(PluginDir, "PCL2Plus.log"); } }
        internal static string FlagFile { get { return Path.Combine(PluginDir, "download-restore.pending"); } }
        internal static bool DownloadPatchActive
        {
            get { return File.Exists(Path.Combine(PluginDir, "download-restore.enabled")); }
        }

        internal static void Activate()
        {
            Frm = ModMain.FrmMain;
            if (Frm == null)
            {
                // 构造函数还没跑完时延迟到 Loaded
                EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent,
                    new RoutedEventHandler((s, e) => { /* 插件页已移除 */ }));
                return;
            }
            // 插件页已移除：不再注入导航项，只安装拖拽接管
            PluginHost.Log("插件页已禁用（仅保留拖拽接管）");
            try { DropHook.Install(); } catch (Exception ex) { Log("DropHook 安装失败：" + ex); }
        }

        private static void TryInstall(FormMain frm)
        {
            if (NavButton != null) return;
            Frm = frm;
            StackPanel sp = Find<StackPanel>(frm, "PanTitleSelect");
            if (sp == null || sp.Children.Count < 3) { Log("找不到导航栏 PanTitleSelect"); return; }

            // 复用第 3 个槽位（原「联机」，本版本已隐藏），索引不变，位置正好在 下载 与 设置 之间
            var btn = Find<MyRadioButton>(frm, "_BtnTitleSelect2");
            if (btn == null) btn = sp.Children.Count > 2 ? sp.Children[2] as MyRadioButton : null;
            if (btn == null) { Log("找不到联机槽位按钮"); return; }

            ClearEvent(btn, "Check");
            btn.Text = "插件";
            btn.Visibility = Visibility.Visible;
            btn.Tag = null;
            btn.Check += OnNavCheck;
            // 兜底：某些情况下 Check 事件不触发，再挂两条鼠标事件
            btn.PreviewClick += delegate { Log("入口: PreviewClick"); SafeShow(); };
            btn.MouseLeftButtonUp += delegate { Log("入口: MouseLeftButtonUp"); SafeShow(); };

            // 点其它导航时收起插件覆盖层（不干预 PCL 的切页）
            for (int i = 0; i < sp.Children.Count; i++)
            {
                if (i == 2) continue;
                MyRadioButton other = sp.Children[i] as MyRadioButton;
                if (other == null) continue;
                other.Check += delegate { HideOverlay(); };
            }

            NavButton = btn;
            Log("「插件」导航项已注入");
            Reassert(4);
        }

        /// <summary>设置系统会重置该按钮可见性，安装后多次重申</summary>
        private static void Reassert(int times)
        {
            if (NavButton == null || times <= 0) return;
            var disp = NavButton.Dispatcher;
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1200);
            timer.Tick += delegate
            {
                try
                {
                    if (NavButton.Visibility != Visibility.Visible) NavButton.Visibility = Visibility.Visible;
                    if (NavButton.Text != "插件") NavButton.Text = "插件";
                }
                catch { }
                if (--times <= 0) timer.Stop();
            };
            timer.Start();
        }

        private static bool _showing;
        private static bool _entered;
        private static DateTime _lastEntry = DateTime.MinValue;

        /// <summary>一次点击会同时触发 PreviewClick / Check / MouseUp，这里做 300ms 去重</summary>
        private static bool ShouldHandle()
        {
            if ((DateTime.Now - _lastEntry).TotalMilliseconds < 300) return false;
            _lastEntry = DateTime.Now;
            return true;
        }

        private static void SafeShow()
        {
            if (_showing) { Log("SafeShow: 重入被拦截"); return; }
            if (!ShouldHandle()) { Log("SafeShow: 重复事件已忽略"); return; }
            _showing = true;
            Log("SafeShow: 开始");
            try
            {
                ShowPage();
            }
            catch (Exception ex)
            {
                Log("打开插件页失败：" + ex);
                try { ModMain.Hint("插件页打开失败：" + ex.Message, ModMain.HintType.Red); } catch { }
            }
            finally { _showing = false; }
        }

        private static void OnNavCheck(object sender, bool raiseByMouse)
        {
            Log("入口: Check (raiseByMouse=" + raiseByMouse + ")");
            SafeShow();
        }

        /// <summary>禁用/启用后提示重启，并调用启动器完成"重新生成 exe + 重启"</summary>
        internal static void PromptRestart(string what)
        {
            MessageBoxResult r = MessageBoxResult.No;
            try
            {
                r = System.Windows.MessageBox.Show(
                    what + "\n\n需要重启 PCL2 才能生效，是否立即重启？",
                    "插件", MessageBoxButton.YesNo, MessageBoxImage.Question);
            }
            catch { }
            Log("PromptRestart: 用户选择 " + r);
            if (r != MessageBoxResult.Yes) return;
            try
            {
                string exeDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string launcher = Path.Combine(exeDir, "PCL2Plus 启动器.exe");
                if (File.Exists(launcher))
                    System.Diagnostics.Process.Start(launcher,
                        "--run --wait-pid " + System.Diagnostics.Process.GetCurrentProcess().Id);
                else System.Diagnostics.Process.Start(Assembly.GetEntryAssembly().Location);
            }
            catch (Exception ex) { Log("重启失败：" + ex.Message); }
            // 必须立即结束进程：Application.Shutdown 可能被 PCL 的关闭流程拦住，
            // 进程不退 → exe 被占用 → 启动器写不进去（就是"启用后打不开"的原因）
            try { System.Windows.Application.Current.Shutdown(); } catch { }
            try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
            Environment.Exit(0);
        }

        private static Border _overlay;

        /// <summary>覆盖层：挂在主窗口根容器上，绝不修改 PCL 的页面对象</summary>
        internal static void ShowPage()
        {
            Log("ShowPage: 进入");
            try
            {
                if (Page == null) { Log("创建 PluginPage"); Page = new PluginPage(); }
                if (Left == null)
                {
                    Log("创建 PluginLeft");
                    Left = new PluginLeft();
                    Left.OnSelect = delegate(int idx) { Page.ShowTab(idx); };
                }

                if (_overlay == null)
                {
                    Panel root = Frm.Content as Panel;
                    if (root == null) { Log("找不到根容器"); return; }
                    var g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Grid.SetColumn(Left.Root, 0);
                    Grid.SetColumn(Page.Root, 1);
                    g.Children.Add(Left.Root);
                    g.Children.Add(Page.Root);
                    _overlay = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF8, 0xFC)),
                        Margin = new Thickness(0, 46, 0, 0),
                        Child = g,
                        Visibility = Visibility.Collapsed
                    };
                    root.Children.Add(_overlay);
                    Log("覆盖层已建立");
                }
                _overlay.Visibility = Visibility.Visible;
                UnlockAll(_overlay);
                Page.ShowTab(0);
                Log("覆盖层已显示");
            }
            catch (Exception ex) { Log("ShowPage 失败：" + ex); }
        }
        internal static void HideOverlay()
        {
            if (_overlay != null) _overlay.Visibility = Visibility.Collapsed;
        }
        // ---------------------------------------------------------------- 反射工具
        internal static T Find<T>(object obj, string name) where T : class
        {
            Type t = obj.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) return f.GetValue(obj) as T;
                PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (p != null) return p.GetValue(obj, null) as T;
                t = t.BaseType;
            }
            return null;
        }

        /// <summary>递归解除命中测试封锁（PCL 切页动画会把祖先 IsHitTestVisible 置 false）</summary>
        /// <summary>把状态钉到 ContentEnter：该状态下 PageOnEnter 是空操作，PCL 再调也不会抛</summary>
        private static void PinState(MyPageRight page)
        {
            try
            {
                PropertyInfo pi = typeof(MyPageRight).GetProperty("PageState",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null && pi.CanWrite)
                    pi.SetValue(page, Enum.Parse(pi.PropertyType, "ContentEnter"), null);
            }
            catch (Exception ex) { Log("PinState 失败：" + ex.Message); }
        }

        /// <summary>把 MyPageRight.PageState 重置为 Empty，避免 PCL 再次 PageOnEnter 时状态机报错</summary>
        private static System.Windows.Threading.DispatcherTimer _recheck;

        private static void ResetPageState(MyPageRight page)
        {
            try
            {
                Type t = typeof(MyPageRight);
                PropertyInfo pi = t.GetProperty("PageState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object target = page;
                Type pt = null;
                if (pi != null && pi.CanWrite) pt = pi.PropertyType;
                if (pi == null || pt == null)
                {
                    FieldInfo fi = t.GetField("_PageState", BindingFlags.Instance | BindingFlags.NonPublic)
                                   ?? t.GetField("PageState", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fi != null) { target = fi; pt = fi.FieldType; }
                }
                object empty = Enum.Parse(pt, "Empty");
                if (pi != null && pi.CanWrite) pi.SetValue(page, empty, null);
                else
                {
                    FieldInfo fi = target as FieldInfo;
                    if (fi != null) fi.SetValue(page, empty);
                }
                Log("PageState 已重置为 Empty");
            }
            catch (Exception ex) { Log("重置 PageState 失败：" + ex.Message); }
        }

        private static void UnlockAll(DependencyObject o)
        {
            if (o == null) return;
            UIElement ui = o as UIElement;
            if (ui != null) ui.IsHitTestVisible = true;
            FrameworkElement fe = o as FrameworkElement;
            // 被退场动画压到 0 的元素要还原，否则重新挂载后是空白
            if (fe != null && fe.Opacity < 1.0) fe.Opacity = 1.0;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(o);
            for (int i = 0; i < n; i++) UnlockAll(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
        }

        private static void SetChild(object host, object child)
        {
            if (host == null) return;
            Type t = host.GetType();
            PropertyInfo pc = t.GetProperty("Child", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pc != null && pc.CanWrite) { pc.SetValue(host, child, null); return; }
            PropertyInfo pcc = t.GetProperty("Content", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pcc != null && pcc.CanWrite) pcc.SetValue(host, child, null);
        }

        private static void ResetHost(object host)
        {
            FrameworkElement fe = host as FrameworkElement;
            if (fe == null) return;
            fe.IsHitTestVisible = true;
            fe.Opacity = 1.0;
            fe.Visibility = Visibility.Visible;
        }

        private static void ClearEvent(object obj, string eventName)
        {
            Type t = obj.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null && typeof(Delegate).IsAssignableFrom(f.FieldType))
                {
                    f.SetValue(obj, null);
                    return;
                }
                t = t.BaseType;
            }
        }

        internal static void Log(string msg)
        {
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " " + msg + "\r\n");
            }
            catch { }
        }

        // ---------------------------------------------------------------- 补丁动作
        internal static void ApplyDownloadPatch()
        {
            File.WriteAllText(FlagFile, DateTime.Now.ToString("s"));
            Log("已写入待应用标记：" + FlagFile);
        }

        internal static void ClearDownloadPatch()
        {
            if (File.Exists(FlagFile)) File.Delete(FlagFile);
            string en = Path.Combine(PluginDir, "download-restore.enabled");
            if (File.Exists(en)) File.Delete(en);
            Log("已清除下载补丁标记");
        }
    }
}
