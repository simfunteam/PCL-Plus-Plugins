// PCL-Plus_Plugins v1.0.0 —— 独立的 PCL 插件管理器（不依赖 PCL 内部实现）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace PluginsApp
{
    internal sealed class PluginItem
    {
        public string Id;
        public string Name;
        public string Version;
        public string Desc;
        public string Author;
        public string Patches;      // "download,legal,dispatch"
        public string Hook;         // 需要挂钩子的 DLL 名，可空
        public bool Enabled;
        public bool Builtin;
        public string File;         // .niyou 路径
    }

    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            var app = new App();
            app.Run(new MainWindow());
        }
    }

    public class MainWindow : Window
    {
        // ---------- 主题 ----------
        static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xC8));
        static readonly Brush AccentLight = new SolidColorBrush(Color.FromRgb(0xE8, 0xF2, 0xFD));
        static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFA));
        static readonly Brush CardBg = Brushes.White;
        static readonly Brush Border = new SolidColorBrush(Color.FromRgb(0xE1, 0xE7, 0xEF));
        static readonly Brush Text1 = new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x37));
        static readonly Brush Text2 = new SolidColorBrush(Color.FromRgb(0x6A, 0x77, 0x86));
        static readonly FontFamily Font = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");

        string BaseDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        string CfgPath { get { return Path.Combine(BaseDir, "pclplus.cfg"); } }
        string _pclPath;
        string _pluginsDir;

        void LoadCfg()
        {
            try
            {
                if (File.Exists(CfgPath))
                    foreach (string line in File.ReadAllLines(CfgPath))
                    {
                        if (line.StartsWith("pcl=")) _pclPath = line.Substring(4).Trim();
                        if (line.StartsWith("plugins=")) _pluginsDir = line.Substring(8).Trim();
                    }
            }
            catch { }
        }

        void SaveCfg()
        {
            try
            {
                File.WriteAllText(CfgPath,
                    "pcl=" + (_pclPath ?? "") + Environment.NewLine +
                    "plugins=" + (_pluginsDir ?? "") + Environment.NewLine);
            }
            catch { }
        }
        string PluginsDir
        {
            get
            {
                if (!string.IsNullOrEmpty(_pluginsDir)) return _pluginsDir;
                return Path.Combine(PclDir, @"PCL\Plugins");
            }
        }
        string Launcher { get { return Path.Combine(BaseDir, "PCL2Plus 启动器.exe"); } }
        string HostDll { get { return Path.Combine(BaseDir, "PCL2Plus.dll"); } }
        string PclExe
        {
            get
            {
                if (!string.IsNullOrEmpty(_pclPath) && File.Exists(_pclPath)) return _pclPath;
                return Path.Combine(BaseDir, "Plain Craft Launcher 2.exe");
            }
        }
        string PclDir { get { return Path.GetDirectoryName(PclExe); } }

        StackPanel _listPan;
        Border _addDrop, _marketCard;
        TextBlock _statusText;
        Grid _content;
        PluginItem _selected;
        List<Button> _navBtns = new List<Button>();
        int _tab;

        internal static void AppLog(string m)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pclplus-app.log"), DateTime.Now.ToString("HH:mm:ss.fff ") + m + "\r\n"); }
            catch { }
        }

        public MainWindow()
        {
            try { AppLog("=== 启动 ==="); } catch { }
            try
            {
                // 清理上次遗留的注入器进程（它们会等待按键而挂住）
                foreach (var old in Process.GetProcessesByName("PCL2Plus 启动器")) { try { old.Kill(); } catch { } }
                AppLog("清理残留注入器");
            }
            catch { }
            Title = "PCL-Plus_Plugins v1.0.0";
            Width = 940; Height = 620;
            MinWidth = 820; MinHeight = 520;
            Background = Bg;
            FontFamily = Font;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new DockPanel();

            // 顶部标题栏
            var head = new Border
            {
                Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x5F, 0xAE), Color.FromRgb(0x2B, 0x7D, 0xD8), 0),
                Height = 54
            };
            var hg = new Grid { Margin = new Thickness(20, 0, 16, 0) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock
            {
                Text = "PCL-Plus",
                FontSize = 19, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            title.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "Plugins v1.0.0", FontSize = 11.5, Foreground = Brushes.White }
            });
            hg.Children.Add(title);

            var hb = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            hb.Children.Add(HeadBtn("选择 PCL…", delegate { PickPcl(); }));
            hb.Children.Add(HeadBtn("应用到 PCL", delegate { Apply(); }));
            hb.Children.Add(HeadBtn("启动 PCL", delegate { LaunchPcl(); }));
            hb.Children.Add(HeadBtn("还原原版", delegate { Restore(); }));
            hg.Children.Add(hb);
            head.Child = hg;
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            // 底部状态栏
            var foot = new Border
            {
                Background = Brushes.White,
                BorderBrush = Border, BorderThickness = new Thickness(0, 1, 0, 0),
                Height = 34
            };
            _statusText = new TextBlock
            {
                FontSize = 12, Foreground = Text2, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 0, 0, 0)
            };
            foot.Child = _statusText;
            DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(foot);

            // 左侧导航
            var nav = new Border
            {
                Width = 178,
                Background = Brushes.White,
                BorderBrush = Border, BorderThickness = new Thickness(0, 0, 1, 0)
            };
            var navPan = new StackPanel { Margin = new Thickness(12, 18, 12, 0) };
            navPan.Children.Add(NavBtn("插件列表", 0));
            navPan.Children.Add(NavBtn("添加插件", 1));
            navPan.Children.Add(NavBtn("插件市场", 2));
            var tip = new TextBlock
            {
                Text = "插件目录\n" + PluginsDir,
                FontSize = 10.5, Foreground = Text2, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 18, 4, 0), Opacity = 0.75
            };
            navPan.Children.Add(tip);
            var setDir = new Button
            {
                Content = "插件文件夹…", Height = 30, Margin = new Thickness(0, 10, 0, 0),
                FontSize = 12, Cursor = Cursors.Hand, Background = Brushes.White,
                BorderBrush = Border, BorderThickness = new Thickness(1), Foreground = Text1
            };
            setDir.Click += delegate { PickPluginsDir(); };
            navPan.Children.Add(setDir);
            nav.Child = navPan;
            DockPanel.SetDock(nav, Dock.Left);
            root.Children.Add(nav);

            // 内容区
            _content = new Grid { Margin = new Thickness(22, 18, 22, 18) };
            root.Children.Add(_content);

            Content = root;

            LoadCfg();
            BuildList();
            BuildAdd();
            BuildMarket();
            ShowTab(0);
            AppLog("UI 构建完成");
            Loaded += delegate
            {
                AppLog("窗口已显示");
                try { RefreshStatus(); } catch (Exception ex) { AppLog("RefreshStatus 失败：" + ex.Message); }
            };

            // 首次运行：必须先选 PCL，再进行后续操作
            if (string.IsNullOrEmpty(_pclPath) || !File.Exists(_pclPath))
            {
                Loaded += delegate
                {
                    // 等主窗口先渲染出来，再弹选择框，避免出现白窗口
                    Dispatcher.BeginInvoke(new Action(FirstRunPick), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                };
            }
        }

        void FirstRunPick()
        {
            MessageBox.Show(this,
                "请先选择 PCL 启动器（Plain Craft Launcher 2.exe）。\r\n\r\n" +
                "选择后本程序会把插件注入到该 PCL，之后每次启动 PCL 都带插件功能。",
                "首次使用", MessageBoxButton.OK, MessageBoxImage.Information);

            if (!PickPcl())
            {
                MessageBox.Show(this, "未选择 PCL，程序即将退出。", "PCL-Plus_Plugins",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
            }
        }

        // ---------- 通用控件 ----------
        Button HeadBtn(string text, Action act)
        {
            var b = new Button
            {
                Content = text,
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 12.5,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            b.Click += delegate { act(); };
            return b;
        }

        Button NavBtn(string text, int idx)
        {
            var b = new Button
            {
                Content = text,
                Height = 40,
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 13.5,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 0, 0, 0),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Text1
            };
            b.Click += delegate { ShowTab(idx); };
            _navBtns.Add(b);
            return b;
        }

        Border Card(UIElement child, double top)
        {
            return new Border
            {
                Background = CardBg,
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20, 16, 20, 18),
                Margin = new Thickness(0, top, 0, 0),
                Child = child,
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.06, Color = Colors.Black }
            };
        }

        TextBlock Head(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = 15.5, FontWeight = FontWeights.Bold,
                Foreground = Text1, Margin = new Thickness(0, 0, 0, 12)
            };
        }

        Button PrimaryBtn(string text, Action act)
        {
            var b = new Button
            {
                Content = text, Height = 34, Padding = new Thickness(18, 0, 18, 0),
                FontSize = 13, Foreground = Brushes.White, Background = Accent,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0)
            };
            b.Click += delegate { act(); };
            return b;
        }

        Button GhostBtn(string text, Action act)
        {
            var b = new Button
            {
                Content = text, Height = 34, Padding = new Thickness(16, 0, 16, 0),
                FontSize = 13, Foreground = Text1, Background = Brushes.White,
                BorderBrush = Border, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0)
            };
            b.Click += delegate { act(); };
            return b;
        }

        // ---------- 插件列表 ----------
        void BuildList()
        {
            var pan = new StackPanel();

            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            bar.Children.Add(PrimaryBtn("刷新", delegate { RefreshList(); }));
            bar.Children.Add(GhostBtn("打开插件目录", delegate
            {
                try { Process.Start("explorer.exe", PluginsDir); } catch { }
            }));
            pan.Children.Add(bar);

            _listPan = new StackPanel();
            pan.Children.Add(Card(_listPan, 0));

            _tab0 = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = pan
            };
            _content.Children.Add(_tab0);
        }

        ScrollViewer _tab0;

        void RefreshList()
        {
            if (_listPan == null) return;
            _listPan.Children.Clear();
            var items = Load();

            foreach (var p in items)
            {
                var cap = p;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new Border
                {
                    Width = 38, Height = 38, CornerRadius = new CornerRadius(9),
                    Background = p.Enabled ? AccentLight : new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5)),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock
                    {
                        Text = p.Builtin ? "核" : "插", FontSize = 15, FontWeight = FontWeights.Bold,
                        Foreground = p.Enabled ? Accent : Text2,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Grid.SetColumn(icon, 0);
                row.Children.Add(icon);

                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(new TextBlock
                {
                    Text = p.Name, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Text1
                });
                line.Children.Add(new TextBlock
                {
                    Text = "  " + p.Version, FontSize = 11.5, Foreground = Text2,
                    VerticalAlignment = VerticalAlignment.Center
                });
                line.Children.Add(new Border
                {
                    Background = p.Enabled ? AccentLight : new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5)),
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = p.Enabled ? "已启用" : "已禁用", FontSize = 10.5,
                        Foreground = p.Enabled ? Accent : Text2
                    }
                });
                texts.Children.Add(line);
                texts.Children.Add(new TextBlock
                {
                    Text = p.Desc, FontSize = 11.5, Foreground = Text2,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
                });
                Grid.SetColumn(texts, 1);
                row.Children.Add(texts);

                var switchBtn = new Button
                {
                    Content = p.Enabled ? "禁用" : "启用",
                    Height = 30, Padding = new Thickness(16, 0, 16, 0), FontSize = 12.5,
                    Foreground = p.Enabled ? Text1 : Brushes.White,
                    Background = p.Enabled ? Brushes.White : Accent,
                    BorderBrush = Border, BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center
                };
                switchBtn.Click += delegate { Toggle(cap); };
                Grid.SetColumn(switchBtn, 2);
                row.Children.Add(switchBtn);

                _listPan.Children.Add(new Border
                {
                    BorderBrush = Border, BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0, 0, 0, 10), Child = row
                });
            }

            if (items.Count == 0)
                _listPan.Children.Add(new TextBlock
                {
                    Text = "还没有插件", FontSize = 12.5, Foreground = Text2
                });
        }

        // ---------- 添加插件 ----------
        void BuildAdd()
        {
            var pan = new StackPanel();
            pan.Children.Add(Card(AddDropArea(), 0));
            _tab1 = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = pan
            };
            _content.Children.Add(_tab1);
        }

        ScrollViewer _tab1;

        UIElement AddDropArea()
        {
            var sp = new StackPanel();
            sp.Children.Add(Head("添加插件"));

            _addDrop = new Border
            {
                Height = 220,
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xDA, 0xE6)),
                BorderThickness = new Thickness(1.5),
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFC, 0xFE)),
                AllowDrop = true
            };
            var inner = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var circle = new Border
            {
                Width = 62, Height = 62, CornerRadius = new CornerRadius(31),
                Background = AccentLight, HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "+", FontSize = 30, Foreground = Accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -4, 0, 0)
                }
            };
            inner.Children.Add(circle);
            inner.Children.Add(new TextBlock
            {
                Text = "把插件文件拖到这里", FontSize = 14, Foreground = Text1,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0)
            });
            inner.Children.Add(new TextBlock
            {
                Text = "支持 .dll，或包含 .dll 的 .zip", FontSize = 11.5, Foreground = Text2,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 14)
            });
            var pick = new Button
            {
                Content = "选择文件…", Height = 34, Padding = new Thickness(20, 0, 20, 0),
                FontSize = 13, Foreground = Brushes.White, Background = Accent,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            pick.Click += delegate
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "插件文件|*.dll;*.zip|所有文件|*.*" };
                if (dlg.ShowDialog() == true) Install(dlg.FileName);
            };
            inner.Children.Add(pick);

            _addDrop.Child = inner;
            _addDrop.Drop += delegate(object s, DragEventArgs e)
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) Install(files[0]);
                e.Handled = true;
            };
            _addDrop.DragOver += delegate(object s, DragEventArgs e)
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            sp.Children.Add(_addDrop);
            return sp;
        }

        // ---------- 插件市场 ----------
        void BuildMarket()
        {
            var pan = new StackPanel();
            var sp = new StackPanel();
            sp.Children.Add(Head("插件市场"));

            var body = new StackPanel { Margin = new Thickness(0, 14, 0, 14) };
            var badge = new Grid
            {
                Width = 78, Height = 78, HorizontalAlignment = HorizontalAlignment.Center
            };
            badge.Children.Add(new Ellipse
            {
                Width = 78, Height = 78,
                Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0x3B, 0x3B))
            });
            badge.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M26,26 L52,52 M52,26 L26,52"),
                Stroke = Brushes.White, StrokeThickness = 7.5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            });
            body.Children.Add(badge);
            body.Children.Add(new TextBlock
            {
                Text = "插件市场尚未开放", FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = Text1, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 0)
            });
            body.Children.Add(new TextBlock
            {
                Text = "当前只能从本地安装插件；市场开放后会在这里提供在线浏览与一键安装。",
                FontSize = 12, Foreground = Text2, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(40, 8, 40, 0)
            });
            sp.Children.Add(body);

            pan.Children.Add(Card(sp, 0));
            _tab2 = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = pan
            };
            _content.Children.Add(_tab2);
        }

        ScrollViewer _tab2;

        // ---------- 逻辑 ----------
        void ShowTab(int idx)
        {
            _tab = idx;
            if (_tab0 != null) _tab0.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_tab1 != null) _tab1.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (_tab2 != null) _tab2.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;

            for (int i = 0; i < _navBtns.Count; i++)
            {
                var b = _navBtns[i];
                bool on = i == idx;
                b.Background = on ? AccentLight : Brushes.Transparent;
                b.Foreground = on ? Accent : Text1;
                b.FontWeight = on ? FontWeights.Bold : FontWeights.Normal;
            }
            if (idx == 0) RefreshList();
        }

        string PackDir { get { return Path.Combine(BaseDir, "plugins"); } }

        /// <summary>扫描 plugins\*.niyou，读取 manifest.json</summary>
        List<PluginItem> Load()
        {
            var list = new List<PluginItem>();
            try
            {
                Directory.CreateDirectory(PackDir);
                foreach (string f in Directory.GetFiles(PackDir, "*.niyou"))
                {
                    var pack = new PluginItem { File = f, Enabled = !File.Exists(f + ".off") };
                    try
                    {
                        using (var zip = System.IO.Compression.ZipFile.OpenRead(f))
                        {
                            var e = zip.GetEntry("manifest.json");
                            if (e != null)
                            {
                                string json;
                                using (var sr = new StreamReader(e.Open(), Encoding.UTF8)) json = sr.ReadToEnd();
                                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                                var o = (Dictionary<string, object>)ser.DeserializeObject(json);
                                pack.Id = o.ContainsKey("id") ? Convert.ToString(o["id"]) : Path.GetFileNameWithoutExtension(f);
                                pack.Name = o.ContainsKey("name") ? Convert.ToString(o["name"]) : pack.Id;
                                pack.Version = o.ContainsKey("version") ? Convert.ToString(o["version"]) : "?";
                                pack.Author = o.ContainsKey("author") ? Convert.ToString(o["author"]) : "";
                                pack.Desc = o.ContainsKey("description") ? Convert.ToString(o["description"]) : "";
                                pack.Hook = o.ContainsKey("hook") ? Convert.ToString(o["hook"]) : null;
                                if (o.ContainsKey("patches") && o["patches"] is object[])
                                    pack.Patches = string.Join(",", ((object[])o["patches"]).Select(x => Convert.ToString(x)).ToArray());
                            }
                        }
                    }
                    catch { pack.Name = Path.GetFileNameWithoutExtension(f); pack.Version = "?"; }
                    if (string.IsNullOrEmpty(pack.Id)) pack.Id = Path.GetFileNameWithoutExtension(f);
                    if (string.IsNullOrEmpty(pack.Name)) pack.Name = pack.Id;
                    list.Add(pack);
                }
            }
            catch { }
            return list;
        }
        void Toggle(PluginItem p)
        {
            p.Enabled = !p.Enabled;
            try
            {
                string off = p.File + ".off";
                if (p.Enabled) { if (File.Exists(off)) File.Delete(off); }
                else if (!File.Exists(off)) File.WriteAllText(off, "");
            }
            catch { }
            RefreshList();
            RefreshStatus();
        }
        void Install(string path)
        {
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".niyou")
                {
                    Directory.CreateDirectory(PackDir);
                    File.Copy(path, Path.Combine(PackDir, Path.GetFileName(path)), true);
                    RefreshList();
                    Msg("插件已添加：" + Path.GetFileName(path) + "\r\n\r\n需要生效请点右上角「应用到 PCL」。");
                    return;
                }
                Msg("只支持 .niyou 插件包（用 NiyouPacker 打包）。\r\n\r\n旧式 .dll/.zip 请放进 PCL 的 PCL\\Plugins 目录。");
            }
            catch (Exception ex) { Msg("添加失败：" + ex.Message); }
        }
        void Apply()
        {
            try
            {
                if (!File.Exists(PclExe)) { Msg("请先选择 PCL 启动器。"); return; }
                if (!File.Exists(Launcher)) { Msg("找不到 PCL2Plus 启动器.exe（与本程序同目录）。"); return; }

                var packs = Load();
                var patches = new List<string>();
                string hook = null;
                foreach (var p in packs)
                {
                    if (!p.Enabled) continue;
                    if (!string.IsNullOrEmpty(p.Patches))
                        foreach (string x in p.Patches.Split(','))
                        { string t = x.Trim(); if (t.Length > 0 && !patches.Contains(t)) patches.Add(t); }
                    if (string.IsNullOrEmpty(hook) && !string.IsNullOrEmpty(p.Hook)) hook = p.Hook;
                }
                if (patches.Count == 0 && hook == null) { Msg("没有勾选任何插件。"); return; }

                // 释放插件自带的 DLL 到 PCL 目录
                if (hook != null)
                {
                    foreach (var p in packs)
                    {
                        if (!p.Enabled || p.Hook != hook) continue;
                        try
                        {
                            using (var zip = System.IO.Compression.ZipFile.OpenRead(p.File))
                                foreach (var e in zip.Entries)
                                {
                                    if (!e.FullName.StartsWith("payload/")) continue;
                                    if (!string.Equals(Path.GetFileName(e.FullName), hook, StringComparison.OrdinalIgnoreCase)) continue;
                                    e.ExtractToFile(Path.Combine(PclDir, Path.GetFileName(e.FullName)), true);
                                }
                        }
                        catch { }
                        break;
                    }
                }

                var running = Process.GetProcessesByName("Plain Craft Launcher 2");
                if (running.Length > 0)
                {
                    var r0 = MessageBox.Show(this,
                        "检测到 PCL 正在运行，注入需要先结束它。\r\n\r\n是否现在结束 PCL 并继续？",
                        "PCL 正在运行", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r0 != MessageBoxResult.Yes) return;
                    foreach (var pr0 in running) { try { pr0.Kill(); } catch { } }
                    System.Threading.Thread.Sleep(1500);
                }

                string args = "--ensure --force --quiet --exe \"" + PclExe + "\"";
                if (patches.Count > 0) args += " --patch \"" + string.Join(",", patches.ToArray()) + "\"";
                if (hook != null) args += " --hook \"" + hook + "\"";
                AppLog("执行：" + Launcher + " " + args);

                if (_statusText != null) _statusText.Text = "正在注入，请稍候……";

                // 后台执行，界面不阻塞
                var th = new System.Threading.Thread(delegate ()
                {
                    string output = "";
                    bool ok = false;
                    try
                    {
                        var psi = new ProcessStartInfo(Launcher, args)
                        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                        var proc = Process.Start(psi);
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        while (!proc.HasExited && sw.ElapsedMilliseconds < 120000) System.Threading.Thread.Sleep(100);
                        if (!proc.HasExited) { try { proc.Kill(); } catch { } output = "[x] 注入超时（120 秒）"; }
                        else { try { output = proc.StandardOutput.ReadToEnd(); } catch { } ok = proc.ExitCode == 0; }
                    }
                    catch (Exception ex) { output = "[x] " + ex.Message; }
                    AppLog("注入输出：" + output.Replace("\r\n", " / "));
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            RefreshStatus();
                            Msg((ok ? "注入完成" : "注入失败") + "\r\n\r\n" + output.Trim());
                        }));
                    }
                    catch { }
                });
                th.IsBackground = true;
                th.Start();
            }
            catch (Exception ex) { Msg("注入异常：" + ex.Message); }
        }
        bool PickPcl()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PCL 启动器|*.exe|所有文件|*.*",
                Title = "选择 PCL 启动器（如 Plain Craft Launcher 2.exe）"
            };
            if (dlg.ShowDialog() != true) return false;
            _pclPath = dlg.FileName;
            _pluginsDir = null;
            SaveCfg();
            AppLog("已选择 PCL：" + _pclPath);
            RefreshList();
            RefreshStatus();
            return true;
        }

        void PickPluginsDir()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = "选择插件文件夹（默认 PCL\\Plugins）";
            if (Directory.Exists(Path.Combine(PclDir, "PCL"))) dlg.SelectedPath = Path.Combine(PclDir, "PCL");
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _pluginsDir = dlg.SelectedPath;
                SaveCfg();
                RefreshList();
                RefreshStatus();
            }
        }
        void LaunchPcl()
        {
            try
            {
                if (!File.Exists(PclExe)) { Msg("请先选择 PCL 启动器。"); return; }
                if (!File.Exists(Launcher)) { Process.Start(new ProcessStartInfo(PclExe) { WorkingDirectory = PclDir }); return; }

                // 关键：通过注入器启动，PCL 自动更新把 exe 换回原版时会被重新打补丁
                var packs = Load();
                var patches = new List<string>();
                string hook = null;
                foreach (var q in packs)
                {
                    if (!q.Enabled) continue;
                    if (!string.IsNullOrEmpty(q.Patches))
                        foreach (string x in q.Patches.Split(','))
                        { string t = x.Trim(); if (t.Length > 0 && !patches.Contains(t)) patches.Add(t); }
                    if (string.IsNullOrEmpty(hook) && !string.IsNullOrEmpty(q.Hook)) hook = q.Hook;
                }

                string args = "--run --force --exe \"" + PclExe + "\"";
                if (patches.Count > 0) args += " --patch \"" + string.Join(",", patches.ToArray()) + "\"";
                if (hook != null) args += " --hook \"" + hook + "\"";

                Process.Start(new ProcessStartInfo(Launcher, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = BaseDir
                });
            }
            catch (Exception ex) { Msg("启动失败：" + ex.Message); }
        }
        void Restore()
        {
            if (!File.Exists(Launcher)) { Msg("找不到 PCL2Plus 启动器.exe"); return; }
            try
            {
                Process.Start(new ProcessStartInfo(Launcher, "--restore") { UseShellExecute = true });
                Msg("已开始还原原版 PCL。");
                RefreshStatus();
            }
            catch (Exception ex) { Msg("还原失败：" + ex.Message); }
        }

        void RefreshStatus()
        {
            if (_statusText == null) return;
            UpdateStatusText(false);
            // 后台探测，绝不阻塞界面
            var th = new System.Threading.Thread(delegate ()
            {
                bool hooked = ProbeHooked();
                try { Dispatcher.BeginInvoke(new Action(delegate { UpdateStatusText(hooked); })); } catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        bool ProbeHooked()
        {
            try
            {
                if (!File.Exists(Launcher) || !File.Exists(PclExe)) return false;
                var psi = new ProcessStartInfo(Launcher, "--status --quiet --exe \"" + PclExe + "\"")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                var pr = Process.Start(psi);
                string outText = "";
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!pr.HasExited && sw.ElapsedMilliseconds < 8000) System.Threading.Thread.Sleep(50);
                if (!pr.HasExited) { try { pr.Kill(); } catch { } }
                try { outText = pr.StandardOutput.ReadToEnd(); } catch { }
                return outText != null && outText.Contains("插件钩子  : 已挂");
            }
            catch (Exception ex) { AppLog("ProbeHooked 失败：" + ex.Message); return false; }
        }

        void UpdateStatusText(bool hooked)
        {
            if (_statusText == null) return;
            try
            {
                string exeState = File.Exists(PclExe) ? "PCL: " + Path.GetFileName(PclExe) : "未选择 PCL";
                int enabled = 0;
                try { foreach (var q in Load()) if (q.Enabled) enabled++; } catch { }
                _statusText.Text = exeState +
                                   "   ·   已启用插件：" + enabled + " 个" +
                                   "   ·   注入状态：" + (hooked ? "已应用" : "未应用") +
                                   "   ·   插件目录：" + PluginsDir;
            }
            catch { }
        }
        void Msg(string text)
        {
            MessageBox.Show(this, text, "PCL-Plus_Plugins", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
