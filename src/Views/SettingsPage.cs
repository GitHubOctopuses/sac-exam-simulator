using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SacExam.Services;

namespace SacExam;

/// <summary>设置页（§8）：各科目题型/题量/分值/时长预设、及格率、提醒、自动保存、图片压缩、须知文本、分界句。</summary>
public class SettingsPage : UserControl
{
    private readonly MainWindow _main;
    private readonly StackPanel _body = new();

    public SettingsPage(MainWindow main)
    {
        _main = main;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.TopBar("设置", Ui.MakeButton("返回主页", () => _main.Navigate(new MainMenuPage(_main)))));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _body.Margin = new Thickness(0, 20, 0, 24);
        _body.Width = 880;
        _body.HorizontalAlignment = HorizontalAlignment.Center;
        scroll.Content = _body;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
        Build();
    }

    private List<(SettingsService.TypePreset preset, TextBox cnt, TextBox sco)> _presetBoxes = new();
    private List<(long subjectId, TextBox dur)> _durBoxes = new();
    private TextBox _noticeBox, _markersBox;
    private TextBox _passRateBox, _warnBox, _maxSideBox;
    private CheckBox _autoSave, _compress, _recovery, _fullscreen;

    private void Build()
    {
        _body.Children.Clear();
        var s = SettingsService.Get();
        _presetBoxes = new List<(SettingsService.TypePreset, TextBox, TextBox)>();
        _durBoxes = new List<(long, TextBox)>();

        foreach (var subject in Db.GetSubjects())
        {
            var preset = s.Presets.FirstOrDefault(p => p.SubjectId == subject.Id);
            var card = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 14) };
            var sp = new StackPanel();
            card.Child = sp;
            _body.Children.Add(card);

            sp.Children.Add(new TextBlock
            {
                Text = $"{subject.Name}（{subject.CategoryText}）· 随机组卷预设",
                FontSize = 15.5,
                FontWeight = FontWeights.Bold,
                Foreground = Ui.TextMain,
                Margin = new Thickness(0, 0, 0, 10),
            });

            if (preset != null)
            {
                foreach (var t in preset.Types)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    row.Children.Add(new TextBlock { Text = t.Type, Width = 90, VerticalAlignment = VerticalAlignment.Center });
                    var cnt = new TextBox { Text = t.Count.ToString(), Width = 70 };
                    var sco = new TextBox { Text = Ui.FmtScore(t.Score), Width = 70 };
                    row.Children.Add(cnt);
                    row.Children.Add(new TextBlock { Text = " 题 × ", VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.TextSub });
                    row.Children.Add(sco);
                    row.Children.Add(new TextBlock { Text = " 分", VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.TextSub });
                    sp.Children.Add(row);
                    _presetBoxes.Add((t, cnt, sco));
                }
                var durRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                durRow.Children.Add(new TextBlock { Text = "考试时长", Width = 90, VerticalAlignment = VerticalAlignment.Center });
                var dur = new TextBox { Text = preset.DurationMinutes.ToString(), Width = 70 };
                durRow.Children.Add(dur);
                durRow.Children.Add(new TextBlock { Text = " 分钟（整卷自带时长时以整卷为准）", VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.TextSub });
                sp.Children.Add(durRow);
                _durBoxes.Add((subject.Id, dur));
            }
        }

        // 通用设置卡
        var card2 = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 14) };
        var sp2 = new StackPanel();
        card2.Child = sp2;
        _body.Children.Add(card2);
        sp2.Children.Add(new TextBlock { Text = "考试规则", FontSize = 15.5, FontWeight = FontWeights.Bold, Foreground = Ui.TextMain, Margin = new Thickness(0, 0, 0, 10) });

        var passRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        passRow.Children.Add(new TextBlock { Text = "及格率", Width = 170, VerticalAlignment = VerticalAlignment.Center });
        _passRateBox = new TextBox { Text = s.PassRate.ToString("0.##"), Width = 70 };
        passRow.Children.Add(_passRateBox);
        passRow.Children.Add(new TextBlock { Text = "（0.6 = 得分 ≥ 卷面总分×60% 即通过；多选/综合固定“全对才得分”，不提供按比例给分）", Foreground = Ui.TextSub, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        sp2.Children.Add(passRow);

        var warnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        warnRow.Children.Add(new TextBlock { Text = "倒计时红色提醒", Width = 170, VerticalAlignment = VerticalAlignment.Center });
        _warnBox = new TextBox { Text = s.WarnRemainMinutes.ToString(), Width = 70 };
        warnRow.Children.Add(_warnBox);
        warnRow.Children.Add(new TextBlock { Text = " 分钟（最后 N 分钟变红闪烁；归零自动交卷并 5 秒后自动确定）", Foreground = Ui.TextSub, VerticalAlignment = VerticalAlignment.Center });
        sp2.Children.Add(warnRow);

        _autoSave = new CheckBox { Content = "自动保存（选择选项的同时即时落库；关闭后仍在交卷/退出时保存）", IsChecked = s.AutoSave, Margin = new Thickness(0, 6, 0, 0) };
        sp2.Children.Add(_autoSave);
        _recovery = new CheckBox { Content = "启动时提示恢复未完成考试", IsChecked = s.RecoveryPrompt, Margin = new Thickness(0, 6, 0, 0) };
        sp2.Children.Add(_recovery);
        _fullscreen = new CheckBox { Content = "记忆全屏状态（F11 切换）", IsChecked = s.RememberFullscreen, Margin = new Thickness(0, 6, 0, 0) };
        sp2.Children.Add(_fullscreen);

        // 图片设置卡
        var card3 = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 14) };
        var sp3 = new StackPanel();
        card3.Child = sp3;
        _body.Children.Add(card3);
        sp3.Children.Add(new TextBlock { Text = "图片（§5.9）", FontSize = 15.5, FontWeight = FontWeights.Bold, Foreground = Ui.TextMain, Margin = new Thickness(0, 0, 0, 10) });
        _compress = new CheckBox { Content = "超大图导入时自动等比压缩", IsChecked = s.ImageAutoCompress, Margin = new Thickness(0, 0, 0, 6) };
        sp3.Children.Add(_compress);
        var sideRow = new StackPanel { Orientation = Orientation.Horizontal };
        sideRow.Children.Add(new TextBlock { Text = "压缩至长边", Width = 170, VerticalAlignment = VerticalAlignment.Center });
        _maxSideBox = new TextBox { Text = s.ImageMaxSide.ToString(), Width = 70 };
        sideRow.Children.Add(_maxSideBox);
        sideRow.Children.Add(new TextBlock { Text = " px（支持 png/jpg/jpeg/gif/bmp/webp；单图 ≤5MB；同内容 SHA-256 自动合并）", Foreground = Ui.TextSub, VerticalAlignment = VerticalAlignment.Center });
        sp3.Children.Add(sideRow);

        // 须知文本
        var card4 = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 14) };
        var sp4 = new StackPanel();
        card4.Child = sp4;
        _body.Children.Add(card4);
        sp4.Children.Add(new TextBlock { Text = "考场须知文本（考前须知页展示，可自行修改）", FontSize = 15.5, FontWeight = FontWeights.Bold, Foreground = Ui.TextMain, Margin = new Thickness(0, 0, 0, 8) });
        _noticeBox = new TextBox
        {
            Text = s.NoticeText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 200,
            FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        sp4.Children.Add(_noticeBox);

        // 综合题分界句
        var card5 = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 14) };
        var sp5 = new StackPanel();
        card5.Child = sp5;
        _body.Children.Add(card5);
        sp5.Children.Add(new TextBlock
        {
            Text = "综合题材料/设问分界句（每行一条；题干中位于分界句之前的内容存为案例材料）",
            FontSize = 15.5,
            FontWeight = FontWeights.Bold,
            Foreground = Ui.TextMain,
            Margin = new Thickness(0, 0, 0, 8),
        });
        _markersBox = new TextBox
        {
            Text = string.Join("\n", s.CaseSplitMarkers),
            AcceptsReturn = true,
            Height = 110,
            FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        sp5.Children.Add(_markersBox);

        // 数据卡
        var card6 = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 14) };
        var sp6 = new StackPanel();
        card6.Child = sp6;
        _body.Children.Add(card6);
        sp6.Children.Add(new TextBlock { Text = "数据", FontSize = 15.5, FontWeight = FontWeights.Bold, Foreground = Ui.TextMain, Margin = new Thickness(0, 0, 0, 8) });
        sp6.Children.Add(new TextBlock { Text = $"SQLite 数据库：{App.DbPath}\n图片目录：{App.ImageDir}", Foreground = Ui.TextSub, TextWrapping = TextWrapping.Wrap });
        var dataRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        dataRow.Children.Add(Ui.MakeButton("打开数据文件夹", () =>
        {
            try { Process.Start("explorer.exe", App.DataDir); } catch { }
        }));
        dataRow.Children.Add(new Border { Width = 10 });
        dataRow.Children.Add(Ui.MakeButton("恢复默认须知文本", () => _noticeBox.Text = SettingsService.DefaultNotice));
        sp6.Children.Add(dataRow);

        // 保存
        var save = Ui.MakeButton("保存全部设置", () =>
        {
            try
            {
                s.PassRate = double.TryParse(_passRateBox.Text.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pr) && pr is > 0 and <= 1 ? pr : 0.6;
                s.WarnRemainMinutes = int.TryParse(_warnBox.Text.Trim(), out var wm) && wm > 0 ? wm : 5;
                s.AutoSave = _autoSave.IsChecked == true;
                s.ImageAutoCompress = _compress.IsChecked == true;
                s.ImageMaxSide = int.TryParse(_maxSideBox.Text.Trim(), out var ms) && ms >= 400 ? ms : 1600;
                s.RecoveryPrompt = _recovery.IsChecked == true;
                s.RememberFullscreen = _fullscreen.IsChecked == true;
                s.NoticeText = _noticeBox.Text;
                s.CaseSplitMarkers = _markersBox.Text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 1).Select(x => x.EndsWith("。") ? x : x + "。").ToList();
                foreach (var (preset, cnt, sco) in _presetBoxes)
                {
                    if (int.TryParse(cnt.Text.Trim(), out var c) && c >= 0) preset.Count = c;
                    if (double.TryParse(sco.Text.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0) preset.Score = v;
                }
                foreach (var (sid, dur) in _durBoxes)
                {
                    var preset = s.Presets.FirstOrDefault(p => p.SubjectId == sid);
                    if (preset != null && int.TryParse(dur.Text.Trim(), out var dm) && dm > 0) preset.DurationMinutes = dm;
                }
                SettingsService.Save(s);
                SacDialog.Info(Window.GetWindow(this), "已保存", "设置已生效（随机组卷预设立即应用）。");
            }
            catch (Exception ex)
            {
                SacDialog.Info(Window.GetWindow(this), "保存失败", ex.Message);
            }
        }, "primary");
        save.HorizontalAlignment = HorizontalAlignment.Center;
        save.Margin = new Thickness(0, 4, 0, 0);
        _body.Children.Add(save);
    }
}

/// <summary>屏外小窗：图片放大查看（滚轮缩放、拖动平移、Esc/点击关闭，§5.9）。</summary>
public class ImageViewerWindow : Window
{
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x1B, 0x26)),
    };
    private readonly Image _image = new() { Cursor = Cursors.Hand };
    private double _zoom = 1;

    public ImageViewerWindow(string path)
    {
        Title = Path.GetFileName(path) + "（滚轮缩放 · 拖动平移 · Esc 关闭）";
        Width = 960;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current.MainWindow;
        Icon = Ui.AppIcon;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x1B, 0x26));
        try
        {
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit();
            bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            _image.Source = bi;
            _image.MaxWidth = 900;
            _image.MaxHeight = 640;
        }
        catch
        {
            _image.Source = null;
        }
        _scroll.Content = _image;
        Content = _scroll;

        _image.MouseWheel += (_, e) =>
        {
            _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.15, 8);
            _image.Width = _image.Source != null ? ((System.Windows.Media.Imaging.BitmapImage)_image.Source).PixelWidth * _zoom : double.NaN;
            _image.Height = _image.Source != null ? ((System.Windows.Media.Imaging.BitmapImage)_image.Source).PixelHeight * _zoom : double.NaN;
            _image.MaxWidth = double.PositiveInfinity;
            _image.MaxHeight = double.PositiveInfinity;
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseDown += (_, _) => Close();
    }
}
