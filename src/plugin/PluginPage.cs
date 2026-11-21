// PCL2Plus —— 插件页（照 PageInstanceMod 的 XAML 结构用 XamlReader 构造，确保控件事件正常）
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using PCL;

namespace PCL2Plus
{
    internal sealed class PluginItem
    {
        public string Id;
        public string Name;
        public string SubTitle;
        public string Desc;
        public bool Enabled;
        public bool Builtin;
        public string File;
    }

    internal static class PluginStore
    {
        internal static string Dir { get { return PluginHost.PluginDir; } }

        internal static List<PluginItem> Load()
        {
            var list = new List<PluginItem>();
            list.Add(new PluginItem
            {
                Id = "download-restore",
                Name = "下载功能恢复",
                SubTitle = "1.0.0 | 内置",
                Desc = "恢复 2.99 中被拦截的「原版游戏」下载页，解除 LoaderInit 拦截并还原左侧入口",
                Builtin = true,
                Enabled = File.Exists(Path.Combine(Dir, "download-restore.enabled"))
                          || File.Exists(Path.Combine(Dir, "download-restore.pending"))
            });
            try
            {
                foreach (string f in Directory.GetFiles(Dir, "*.dll"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (string.Equals(name, "PCL2Plus", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new PluginItem
                    {
                        Id = name,
                        Name = name,
                        SubTitle = (new FileInfo(f).Length / 1024) + " KB | 外部",
                        Desc = f,
                        File = f,
                        Enabled = !File.Exists(f + ".off")
                    });
                }
            }
            catch { }
            return list;
        }

        internal static void SetEnabled(PluginItem p, bool on)
        {
            if (p.Builtin)
            {
                if (on) PluginHost.ApplyDownloadPatch(); else PluginHost.ClearDownloadPatch();
                return;
            }
            try
            {
                if (on && File.Exists(p.File + ".off")) File.Delete(p.File + ".off");
                else if (!on && !File.Exists(p.File + ".off")) File.WriteAllText(p.File + ".off", "");
            }
            catch (Exception ex) { PluginHost.Log("切换插件失败：" + ex.Message); }
        }

        internal static bool Install(string src, out string err)
        {
            err = null;
            try
            {
                string ext = Path.GetExtension(src).ToLowerInvariant();
                Directory.CreateDirectory(Dir);
                if (ext == ".dll")
                {
                    File.Copy(src, Path.Combine(Dir, Path.GetFileName(src)), true);
                    return true;
                }
                if (ext == ".zip")
                {
                    using (var zip = System.IO.Compression.ZipFile.OpenRead(src))
                    {
                        int n = 0;
                        foreach (var e in zip.Entries)
                        {
                            if (!e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                            if (e.FullName.Contains("/")) continue;
                            using (var s = e.Open())
                            using (var f = File.Create(Path.Combine(Dir, Path.GetFileName(e.FullName))))
                                s.CopyTo(f);
                            n++;
                        }
                        if (n > 0) return true;
                    }
                    err = "压缩包内没有找到 .dll";
                    return false;
                }
                err = "只支持 .dll 或包含 .dll 的 .zip";
                return false;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }
    }

    /// <summary>左侧导航</summary>
    public class PluginLeft
    {
        private const string XamlText = @"
<local:MyPageLeft xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                  xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
                  xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 2"">
  <StackPanel Margin=""15,55,12,10"">
    <local:MyRadioButton Name=""NavList"" Text=""插件列表"" ColorType=""Highlight"" HorizontalAlignment=""Left"" Margin=""0,3"" Checked=""True""/>
    <local:MyRadioButton Name=""NavAdd"" Text=""添加插件"" ColorType=""Highlight"" HorizontalAlignment=""Left"" Margin=""0,3""/>
    <local:MyRadioButton Name=""NavMarket"" Text=""插件市场"" ColorType=""Highlight"" HorizontalAlignment=""Left"" Margin=""0,3""/>
  </StackPanel>
</local:MyPageLeft>";

        internal FrameworkElement Root;
        private FrameworkElement _nameRoot;
        internal Action<int> OnSelect;
        private MyRadioButton[] _items;

        public PluginLeft()
        {
            var _pl = (MyPageLeft)XamlReader.Parse(XamlText);
            _nameRoot = _pl;
            Root = _pl;
            _items = new MyRadioButton[]
            {
                (MyRadioButton)_nameRoot.FindName("NavList"),
                (MyRadioButton)_nameRoot.FindName("NavAdd"),
                (MyRadioButton)_nameRoot.FindName("NavMarket")
            };
            for (int i = 0; i < _items.Length; i++)
            {
                int idx = i;
                _items[i].Check += delegate(object s, bool byMouse)
                {
                    PluginHost.Log("左栏点击 " + idx);
                    if (OnSelect != null) OnSelect(idx);
                };
            }
        }
    }
    public class PluginPage
    {
        // 与 PageInstanceMod 同构：ScrollViewer > StackPanel > 搜索框 / 工具栏卡 / 列表卡 / 底部操作条
        private const string XamlText = @"
<local:MyPageRight xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                   xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
                   xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 2"">
  <Grid>
    <Grid Name=""PanAllBack"">
      <ScrollViewer VerticalScrollBarVisibility=""Auto"" HorizontalScrollBarVisibility=""Disabled"">
        <StackPanel Name=""PanMain"" Margin=""25,10,25,10"">
          <local:MySearchBox Name=""SearchBox"" Margin=""0,15"" HintText=""搜索插件名称 / 描述""/>
          <local:MyCard Name=""PanManage"" Margin=""0,0,0,15"">
            <StackPanel Orientation=""Horizontal"" Margin=""15,15,0,15"" Height=""35"">
              <local:MyButton Name=""BtnOpen"" Text=""打开文件夹"" MinWidth=""110"" Padding=""13,0"" Margin=""0,0,15,0"" ColorType=""Highlight""/>
              <local:MyButton Name=""BtnFile"" Text=""从文件安装"" MinWidth=""110"" Padding=""13,0"" Margin=""0,0,15,0""/>
              <local:MyButton Name=""BtnRefresh"" Text=""刷新列表"" MinWidth=""110"" Padding=""13,0"" Margin=""0,0,15,0""/>
            </StackPanel>
          </local:MyCard>
          <local:MyCard Name=""PanFilterCard"" Title="" "" MinHeight=""40"" Margin=""0,0,0,10"">
            <StackPanel Name=""PanFilter"" Orientation=""Horizontal"" Margin=""15,8,0,8"" Height=""28"">
              <local:MyRadioButton Name=""BtnFilterAll"" ColorType=""Highlight"" VerticalAlignment=""Center"" Margin=""2,0"" Text=""全部"" Checked=""True""/>
              <local:MyRadioButton Name=""BtnFilterOn"" ColorType=""Highlight"" VerticalAlignment=""Center"" Margin=""2,0"" Text=""已启用""/>
              <local:MyRadioButton Name=""BtnFilterOff"" ColorType=""Highlight"" VerticalAlignment=""Center"" Margin=""2,0"" Text=""已禁用""/>
            </StackPanel>
          </local:MyCard>
          <local:MyCard Name=""PanListBack"" Title="" "" MinHeight=""55"" Margin=""0,0,0,14"">
            <StackPanel Name=""PanList"" Margin=""18,14,18,16"" VerticalAlignment=""Top""/>
          </local:MyCard>
        </StackPanel>
      </ScrollViewer>
    </Grid>
    <local:MyCard Name=""CardSelect"" Visibility=""Collapsed"" HorizontalAlignment=""Center"" VerticalAlignment=""Bottom"" Margin=""25,25,25,0"">
      <TextBlock Name=""LabSelect"" Text=""已选择 1 个插件"" HorizontalAlignment=""Center"" Margin=""9""/>
      <StackPanel Orientation=""Horizontal"" Margin=""5,28,5,5"">
        <local:MyButton Name=""BtnEnable"" Text=""启用"" Padding=""13,0"" MinWidth=""90"" Margin=""2,0""/>
        <local:MyButton Name=""BtnDisable"" Text=""禁用"" Padding=""13,0"" MinWidth=""90"" Margin=""2,0""/>
        <local:MyButton Name=""BtnUninstall"" Text=""卸载"" Padding=""13,0"" MinWidth=""90"" Margin=""2,0""/>
        <local:MyButton Name=""BtnCancelSel"" Text=""取消选择"" Padding=""13,0"" MinWidth=""90"" Margin=""2,0""/>
      </StackPanel>
    </local:MyCard>
  </Grid>
</local:MyPageRight>";

        internal FrameworkElement Root;
        private FrameworkElement _nameRoot;
        private MySearchBox _search;
        private MyRadioButton _fAll, _fOn, _fOff;
        private StackPanel _panList;
        private MyCard _cardSelect;
        private TextBlock _labSelect;
        private MyCard _manageCard, _filterCard, _listCard, _addCard, _marketCard;
        private MyListItem[] _navItems;
        private bool _syncing;
        private StackPanel _addView, _marketView;
        private PluginItem _selected;
        private bool _chipOn, _chipOff;

        public PluginPage()
        {
            var _pr = (MyPageRight)XamlReader.Parse(XamlText);
            _nameRoot = _pr;
            var _grid = (FrameworkElement)_pr.Child;
            _pr.Child = null;   // 断开逻辑父级，否则注入时会报 AddLogicalChild
            Root = _grid;
            _search = (MySearchBox)_nameRoot.FindName("SearchBox");
            _fAll = (MyRadioButton)_nameRoot.FindName("BtnFilterAll");
            _fOn = (MyRadioButton)_nameRoot.FindName("BtnFilterOn");
            _fOff = (MyRadioButton)_nameRoot.FindName("BtnFilterOff");
            _panList = (StackPanel)_nameRoot.FindName("PanList");
            _cardSelect = (MyCard)_nameRoot.FindName("CardSelect");
            _labSelect = (TextBlock)_nameRoot.FindName("LabSelect");
            _manageCard = (MyCard)_nameRoot.FindName("PanManage");
            _filterCard = (MyCard)_nameRoot.FindName("PanFilterCard");
            _listCard = (MyCard)_nameRoot.FindName("PanListBack");

            _search.TextChanged += delegate { RefreshList(); };
            _fAll.Check += delegate(object s, bool b) { _chipOn = false; _chipOff = false; RefreshList(); };
            _fOn.Check += delegate(object s, bool b) { _chipOn = true; _chipOff = false; RefreshList(); };
            _fOff.Check += delegate(object s, bool b) { _chipOn = false; _chipOff = true; RefreshList(); };

            ((MyButton)_nameRoot.FindName("BtnOpen")).Click += delegate
            {
                PluginHost.Log("按钮点击: 打开文件夹");
                try { System.Diagnostics.Process.Start("explorer.exe", PluginStore.Dir); } catch { }
            };
            ((MyButton)_nameRoot.FindName("BtnFile")).Click += delegate
            {
                PluginHost.Log("按钮点击: 从文件安装");
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "插件文件|*.dll;*.zip|所有文件|*.*" };
                if (dlg.ShowDialog() == true) Install(dlg.FileName);
            };
            ((MyButton)_nameRoot.FindName("BtnRefresh")).Click += delegate
            {
                PluginHost.Log("按钮点击: 刷新列表");
                RefreshList();
            };
            ((MyButton)_nameRoot.FindName("BtnEnable")).Click += delegate { SetSelEnabled(true); };
            ((MyButton)_nameRoot.FindName("BtnDisable")).Click += delegate { SetSelEnabled(false); };
            ((MyButton)_nameRoot.FindName("BtnUninstall")).Click += delegate { Uninstall(); };
            ((MyButton)_nameRoot.FindName("BtnCancelSel")).Click += delegate { _selected = null; UpdateCardSelect(); };

            AddView();
            AddMarket();
            RefreshList();
        }

        private void AddView()
        {
            var pan = (StackPanel)_nameRoot.FindName("PanMain");
            var card = new MyCard { Title = "添加插件", Margin = new Thickness(0, 0, 0, 14), Visibility = Visibility.Collapsed };
            var inner = new StackPanel { Margin = new Thickness(21, 40, 21, 16) };
            var dz = new Border
            {
                AllowDrop = true,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0xDD, 0xE3)),
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFC)),
                Height = 150
            };
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            txt.Children.Add(new TextBlock
            {
                Text = "把插件文件拖到这里，或点「从文件安装」",
                HorizontalAlignment = HorizontalAlignment.Center
            });
            txt.Children.Add(new TextBlock
            {
                Text = "支持 .dll，或包含 .dll 的 .zip",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 12, Opacity = 0.5, Margin = new Thickness(0, 6, 0, 0)
            });
            dz.Child = txt;
            dz.Drop += delegate(object s, DragEventArgs e)
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) Install(files[0]);
                e.Handled = true;
            };
            inner.Children.Add(dz);
            card.BorderChild = inner;
            pan.Children.Add(card);
            _addView = inner;
            _addCard = card;
        }



        private void Install(string path)
        {
            string err;
            if (PluginStore.Install(path, out err))
            {
                ModMain.Hint("插件已安装：" + Path.GetFileName(path), ModMain.HintType.Blue);
                RefreshList();
            }
            else ModMain.Hint("安装失败：" + err, ModMain.HintType.Red);
        }

        internal void RefreshList()
        {
            if (_panList == null) return;
            _panList.Children.Clear();
            var all = PluginStore.Load();
            string kw = _search == null ? "" : (_search.Text ?? "").Trim();

            int nAll = all.Count, nOn = 0;
            foreach (var p in all) if (p.Enabled) nOn++;
            if (_fAll != null) _fAll.Text = "全部 (" + nAll + ")";
            if (_fOn != null) _fOn.Text = "已启用 (" + nOn + ")";
            if (_fOff != null) _fOff.Text = "已禁用 (" + (nAll - nOn) + ")";

            foreach (var p in all)
            {
                if (kw.Length > 0
                    && p.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0
                    && p.Desc.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (_chipOn && !p.Enabled) continue;
                if (_chipOff && p.Enabled) continue;

                var cap = p;
                var item = new MyListItem
                {
                    Type = MyListItem.CheckType.CheckBox,
                    Title = p.Name,
                    Info = p.SubTitle + " · " + p.Desc,
                    Checked = p.Enabled,
                    Height = 44,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                item.Click += delegate { _selected = cap; PluginHost.Log("select " + cap.Name); UpdateCardSelect(); };
                _panList.Children.Add(item);
            }

            if (_panList.Children.Count == 0)
                _panList.Children.Add(new TextBlock
                {
                    Text = "没有匹配的插件", FontSize = 12, Opacity = 0.5,
                    Margin = new Thickness(2, 12, 0, 12)
                });

            UpdateCardSelect();
        }

        private void UpdateCardSelect()
        {
            if (_cardSelect == null) return;
            if (_selected == null) { _cardSelect.Visibility = Visibility.Collapsed; return; }
            _labSelect.Text = "已选择 1 个插件：" + _selected.Name;
            _cardSelect.Visibility = Visibility.Visible;
        }

        private void SetSelEnabled(bool on)
        {
            if (_selected == null) return;
            _selected.Enabled = on;
            PluginStore.SetEnabled(_selected, on);
            PluginHost.Log("set " + _selected.Name + " -> " + on);
            RefreshList();
            PluginHost.PromptRestart((on ? "已启用插件：" : "已禁用插件：") + _selected.Name);
        }

        private void Uninstall()
        {
            if (_selected == null) return;
            if (_selected.Builtin) { ModMain.Hint("内置插件不能卸载", ModMain.HintType.Red); return; }
            try
            {
                if (File.Exists(_selected.File)) File.Delete(_selected.File);
                if (File.Exists(_selected.File + ".off")) File.Delete(_selected.File + ".off");
                ModMain.Hint("已卸载：" + _selected.Name, ModMain.HintType.Blue);
                _selected = null;
                RefreshList();
            }
            catch (Exception ex) { ModMain.Hint("卸载失败：" + ex.Message, ModMain.HintType.Red); }
        }

        internal void ShowTab(int idx)
        {
            PluginHost.Log("ShowTab " + idx);

            bool list = idx == 0;
            if (_search != null) _search.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            if (_manageCard != null) _manageCard.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            if (_filterCard != null) _filterCard.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            if (_listCard != null) _listCard.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            if (_addCard != null) _addCard.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (_marketCard != null) _marketCard.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (_cardSelect != null) _cardSelect.Visibility = (list && _selected != null) ? Visibility.Visible : Visibility.Collapsed;
            if (list) RefreshList();
        }

        private void AddMarket()
        {
            var pan = (StackPanel)_nameRoot.FindName("PanMain");
            var card = new MyCard { Title = "插件市场", Margin = new Thickness(0, 0, 0, 14), Visibility = Visibility.Collapsed };
            var inner = new StackPanel { Margin = new Thickness(21, 40, 21, 40) };
            inner.Children.Add(new Grid
            {
                Width = 72, Height = 72,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 14),
                Children =
                {
                    new System.Windows.Shapes.Ellipse
                    {
                        Width = 72, Height = 72,
                        Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0x3B, 0x3B))
                    },
                    new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M24,24 L48,48 M48,24 L24,48"),
                        Stroke = Brushes.White, StrokeThickness = 7,
                        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                    }
                }
            });
            inner.Children.Add(new TextBlock
            {
                Text = "插件市场尚未开放", FontSize = 16, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            inner.Children.Add(new TextBlock
            {
                Text = "当前只能从本地安装插件。",
                FontSize = 12, Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            });
            card.BorderChild = inner;
            pan.Children.Add(card);
            _marketCard = card;
        }
    }
}
