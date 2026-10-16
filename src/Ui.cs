using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SacExam.Services;

namespace SacExam;

/// <summary>共享 UI 构建助手（协会机考风格）。</summary>
public static class Ui
{
    private static System.Windows.Media.ImageSource _appIcon;

    /// <summary>应用图标（icon.ico 随程序集内嵌）。</summary>
    public static System.Windows.Media.ImageSource AppIcon
    {
        get
        {
            if (_appIcon == null)
            {
                try { _appIcon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/icon.ico")); }
                catch { }
            }
            return _appIcon;
        }
    }

    public static readonly Brush DeepBlue = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x92));
    public static readonly Brush AuxBlue = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED));
    public static readonly Brush ChipBlue = new SolidColorBrush(Color.FromRgb(0xBD, 0xD7, 0xEE));
    public static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xF5, 0x9A, 0x23));
    public static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xE3, 0x40, 0x40));
    public static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x22, 0xA4, 0x5D));
    public static readonly Brush TextMain = new SolidColorBrush(Color.FromRgb(0x22, 0x2B, 0x3A));
    public static readonly Brush TextSub = new SolidColorBrush(Color.FromRgb(0x7A, 0x86, 0x99));
    public static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0xE3, 0xE8, 0xF0));
    public static readonly Brush CardBg = Brushes.White;
    public static readonly Brush PageBg = new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xFA));

    public static LinearGradientBrush BlueGradient => new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops =
        {
            new GradientStop(Color.FromRgb(0x1E, 0x57, 0x99), 0),
            new GradientStop(Color.FromRgb(0x14, 0x3D, 0x74), 1),
        },
    };

    /// <summary>深蓝页头条：居中标题 + 右侧控件区。</summary>
    public static Border TopBar(string title, FrameworkElement right = null, double height = 64)
    {
        var grid = new Grid();
        if (right != null)
        {
            grid.Children.Add(right);
            right.VerticalAlignment = VerticalAlignment.Center;
            right.HorizontalAlignment = HorizontalAlignment.Right;
        }
        var t = new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(t);
        return new Border
        {
            Background = DeepBlue,
            Height = height,
            Child = grid,
            Padding = new Thickness(24, 0, 24, 0),
        };
    }

    public static Button MakeButton(string text, Action onClick, string style = "ghost")
    {
        var b = new Button { Content = text };
        b.SetResourceReference(FrameworkElement.StyleProperty, style switch
        {
            "primary" => "BtnPrimary",
            "orange" => "BtnOrange",
            "danger" => "BtnDanger",
            "green" => "BtnGreen",
            "orangeLine" => "BtnOrangeLine",
            _ => "BtnGhost",
        });
        if (onClick != null) b.Click += (_, _) => onClick();
        return b;
    }

    // ---------- 题目富文本（{{img:...}} 内联占位渲染，§5.9） ----------

    public static string ResolveImagePath(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (name.StartsWith(ImportService.StoredPrefix, StringComparison.Ordinal))
        {
            string f = Path.Combine(App.ImageDir, name.Substring(ImportService.StoredPrefix.Length));
            return File.Exists(f) ? f : null;
        }
        string inStore = Path.Combine(App.ImageDir, name);
        if (File.Exists(inStore)) return inStore;
        if (Path.IsPathRooted(name) && File.Exists(name)) return name;
        return null;
    }

    private static readonly Regex ImgRe = ImportService.ImgPlaceholder;

    /// <summary>把含 {{img:...}} 的文本渲染为「文本段 + 可点击图片」的 StackPanel。</summary>
    public static StackPanel BuildRichText(string text, double fontSize, TextAlignment alignment = TextAlignment.Left)
    {
        var panel = new StackPanel();
        if (string.IsNullOrEmpty(text)) return panel;
        text = text.Replace("\r\n", "\n");
        int pos = 0;
        foreach (Match m in ImgRe.Matches(text))
        {
            if (m.Index > pos) panel.Children.Add(MakeText(text.Substring(pos, m.Index - pos), fontSize));
            string name = m.Groups[1].Value.Trim();
            string path = ResolveImagePath(name);
            if (path != null)
            {
                var img = MakeZoomableImage(path, double.NaN, 240);
                img.Margin = new Thickness(0, 6, 0, 6);
                img.HorizontalAlignment = HorizontalAlignment.Left;
                panel.Children.Add(img);
            }
            else
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"[ 图片缺失：{name} ]",
                    Foreground = TextSub,
                    FontStyle = FontStyles.Italic,
                    FontSize = fontSize - 2,
                    Margin = new Thickness(0, 4, 0, 4),
                });
            }
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) panel.Children.Add(MakeText(text.Substring(pos), fontSize));
        return panel;
    }

    private static TextBlock MakeText(string s, double fontSize)
    {
        var tb = new TextBlock
        {
            Text = s,
            FontSize = fontSize,
            Foreground = TextMain,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = fontSize * 1.85,
        };
        return tb;
    }

    /// <summary>可点击放大的图片（§5.9 展示规则：点击放大查看原图，滚轮缩放）。</summary>
    public static Image MakeZoomableImage(string path, double width = double.NaN, double maxHeight = 240)
    {
        Image img = null;
        try
        {
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit();
            bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            if (!double.IsNaN(width) && !double.IsInfinity(width) && width > 0)
                bi.DecodePixelWidth = (int)Math.Min(width * 2, 1600);
            bi.EndInit();
            bi.Freeze();
            img = new Image { Source = bi, Stretch = Stretch.Uniform, MaxHeight = maxHeight, Cursor = Cursors.Hand };
            if (!double.IsNaN(width)) img.MaxWidth = width;
            img.ToolTip = "点击放大查看";
            img.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                new ImageViewerWindow(path).Show();
            };
        }
        catch
        {
            img = new Image { MaxHeight = 40, Opacity = 0.4 };
        }
        return img;
    }

    public static string FmtScore(double d) =>
        Math.Abs(d - Math.Round(d)) < 1e-9 ? Math.Round(d).ToString("0") : Math.Round(d, 1).ToString("0.0");

    public static string FmtClock(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public static string FmtUsed(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours} 分 {ts.Minutes} 分 {ts.Seconds} 秒"
             : $"{(int)ts.TotalMinutes} 分 {ts.Seconds} 秒";
    }

    public static string TypeInstruction(string qtype) => qtype switch
    {
        "单选题" => "下列每小题的四个选项中，只有一项是最符合题意的正确答案，多选、错选或不选均不得分。",
        "多选题" => "下列每小题的备选答案中，有两个或两个以上符合题意的正确答案，多选、少选、错选、不选均不得分。",
        "判断题" => "判断每小题的表述是否正确，选出你认为正确的选项。",
        _ => "每小题备选答案中，有一个或一个以上符合题意的正确答案，多选、少选、错选、不选均不得分。（不定项选择）",
    };

    public static FrameworkElement OptionIcon(bool isMulti, bool selected)
    {
        if (isMulti)
        {
            var box = new Border
            {
                Width = 18, Height = 18,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(selected ? 0 : 1.4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0xC3, 0xD4)),
                Background = selected ? AuxBlue : Brushes.White,
                Child = selected ? new TextBlock
                {
                    Text = "✓", Foreground = Brushes.White, FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1),
                } : null,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return box;
        }
        var circle = new Grid
        {
            Width = 18, Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
        };
        circle.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Stroke = selected ? AuxBlue : new SolidColorBrush(Color.FromRgb(0xB9, 0xC3, 0xD4)),
            StrokeThickness = 1.5,
            Fill = Brushes.White,
        });
        if (selected)
            circle.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 9, Height = 9,
                Fill = AuxBlue,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        return circle;
    }
}

/// <summary>模拟机考风格对话框（蓝头 + 白身 + 橙确定/白取消，还原 img08）。</summary>
public static class SacDialog
{
    public static bool Confirm(Window owner, string title, string message, string okText = "确定",
        string cancelText = "取消", bool dangerOk = false, int? autoOkSeconds = null,
        (string before, string after)? highlight = null)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Background = PageBgBrush(),
            ShowInTaskbar = false,
        };
        if (owner != null && owner.IsLoaded) dlg.Owner = owner;

        var body = new StackPanel { Margin = new Thickness(36, 28, 36, 12) };
        var msg = new TextBlock
        {
            FontSize = 15,
            Foreground = Ui.TextMain,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 30,
        };
        // 高亮数字：before/after 之间的文本显示为红色加粗
        if (highlight != null && message.Contains(highlight.Value.before) && message.Contains(highlight.Value.after))
        {
            int i1 = message.IndexOf(highlight.Value.before, StringComparison.Ordinal) + highlight.Value.before.Length;
            int i2 = message.IndexOf(highlight.Value.after, i1, StringComparison.Ordinal);
            msg.Inlines.Add(message.Substring(0, i1));
            msg.Inlines.Add(new System.Windows.Documents.Run(message.Substring(i1, i2 - i1))
            {
                Foreground = Ui.Red,
                FontWeight = FontWeights.Bold,
                FontSize = 17,
            });
            msg.Inlines.Add(message.Substring(i2));
        }
        else msg.Text = message;

        body.Children.Add(msg);
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 26, 0, 20),
        };
        bool? result = null;
        var okBtn = new Button { Content = okText, MinWidth = 140, Height = 40 };
        okBtn.SetResourceReference(FrameworkElement.StyleProperty, dangerOk ? "BtnDanger" : "BtnOrange");
        okBtn.FontSize = 15;
        var cancelBtn = new Button { Content = cancelText, MinWidth = 140, Height = 40 };
        cancelBtn.SetResourceReference(FrameworkElement.StyleProperty, "BtnGhost");
        cancelBtn.FontSize = 15;
        okBtn.Click += (_, _) => { result = true; dlg.Close(); };
        cancelBtn.Click += (_, _) => { result = false; dlg.Close(); };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(new Border { Width = 24 });
        btnRow.Children.Add(cancelBtn);
        body.Children.Add(btnRow);

        var dlgRoot = new DockPanel { LastChildFill = true };
        var dlgTop = Ui.TopBar(title, height: 52);
        DockPanel.SetDock(dlgTop, Dock.Top);
        dlgRoot.Children.Add(dlgTop);
        dlgRoot.Children.Add(body);
        dlg.Content = dlgRoot;
        dlg.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { result = false; dlg.Close(); }
            if (e.Key == Key.Enter) { result = true; dlg.Close(); }
        };

        if (autoOkSeconds.HasValue)
        {
            var remain = autoOkSeconds.Value;
            okBtn.Content = $"{okText}（{remain}）";
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (_, _) =>
            {
                remain--;
                if (remain <= 0) { t.Stop(); result = true; dlg.Close(); }
                else okBtn.Content = $"{okText}（{remain}）";
            };
            t.Start();
            dlg.Closed += (_, _) => t.Stop();
        }

        dlg.ShowDialog();
        return result == true;
    }

    public static void Info(Window owner, string title, string message) =>
        Confirm(owner, title, message, okText: "知道了", cancelText: null);

    private static Brush PageBgBrush() => new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xFA));
}
