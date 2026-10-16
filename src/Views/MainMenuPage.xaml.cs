using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SacExam.Services;

namespace SacExam;

public partial class MainMenuPage : UserControl
{
    private readonly MainWindow _main;
    private long _selectedSubjectId = 1;
    private readonly List<Border> _subjectCards = new();

    public MainMenuPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Loaded += (_, _) => Build();
    }

    private void Build()
    {
        Body.Children.Clear();
        _subjectCards.Clear();

        // ---- 恢复未完成考试横幅 ----
        var ongoing = ExamService.GetOngoingExams();
        if (ongoing.Count > 0 && SettingsService.Get().RecoveryPrompt)
        {
            var banner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xE0)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9A, 0x23)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 0, 16),
            };
            var row = new Grid();
            var txt = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Ui.TextMain,
                Text = $"检测到 {ongoing.Count} 场未完成的考试：{ongoing[0].session.SubjectName} · {ongoing[0].session.PaperName}（已答 {ongoing[0].answered} 题，剩余 {Math.Max(0, (int)(ongoing[0].session.Deadline - DateTime.Now).TotalMinutes)} 分钟）",
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var btn = Ui.MakeButton("继续考试", () => ResumeExam(ongoing[0].session), "orange");
            btn.HorizontalAlignment = HorizontalAlignment.Right;
            btn.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(txt);
            row.Children.Add(btn);
            txt.Margin = new Thickness(0, 0, 130, 0);
            banner.Child = row;
            Body.Children.Add(banner);
        }

        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(24) }, new ColumnDefinition { Width = new GridLength(340) } } };

        // ---- 左：三科大卡片 ----
        var stack = new StackPanel();
        foreach (var s in Db.GetSubjects())
        {
            int qCount = BankService.CountBySubject(s.Id);
            int paperCount = Db.GetPapers(s.Id, includeRandom: false).Count;
            var preset = SettingsService.Get().Presets.FirstOrDefault(p => p.SubjectId == s.Id);
            string compose = preset == null ? "" :
                string.Join("，", preset.Types.Select(t => $"{t.Type}{t.Count}题×{Ui.FmtScore(t.Score)}分")) + $"，满分{preset.Types.Sum(t => t.Count * t.Score):0}分";

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = s.Id == _selectedSubjectId ? Ui.AuxBlue : Ui.Line,
                BorderThickness = new Thickness(s.Id == _selectedSubjectId ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(22, 16, 22, 16),
                Margin = new Thickness(0, 0, 0, 14),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = s.Id,
            };
            card.MouseLeftButtonDown += (_, _) => { _selectedSubjectId = s.Id; RefreshSelection(); };

            var sp = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = s.Name, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Ui.TextMain, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(Chip(s.CategoryText, s.Category == "special" ? "#7A5AF8" : "#2F6FED", 8));
            head.Children.Add(Chip($"题库 {qCount} 题 · 整卷 {paperCount} 套", "#5B6B85", 12));
            sp.Children.Add(head);
            sp.Children.Add(new TextBlock { Text = $"考试时长 {s.DurationMinutes} 分钟 · 及格线 60 分（正确率 60%）", Foreground = Ui.TextSub, FontSize = 12, Margin = new Thickness(0, 6, 0, 2) });
            if (compose.Length > 0)
                sp.Children.Add(new TextBlock { Text = $"预设卷面：{compose}", Foreground = Ui.TextSub, FontSize = 12, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            btns.Children.Add(Ui.MakeButton("标准模拟卷", () => ShowPaperPicker(s), "primary"));
            btns.Children.Add(new Border { Width = 12 });
            btns.Children.Add(Ui.MakeButton("随机练习", () => ShowRandomDialog(s)));
            sp.Children.Add(btns);

            card.Child = sp;
            _subjectCards.Add(card);
            stack.Children.Add(card);
        }
        Grid.SetColumn(stack, 0);
        grid.Children.Add(stack);

        // ---- 右：功能入口 ----
        int wrongCount = ExamService.GetWrongBook().Count;
        int examCount = ExamService.GetHistory().Count(h => h.Status == "finished");
        var right = new StackPanel();
        right.Children.Add(MakeEntry("历史记录", $"已完成 {examCount} 场考试", () => _main.Navigate(new HistoryPage(_main))));
        right.Children.Add(MakeEntry("错题本", wrongCount > 0 ? $"{wrongCount} 道错题待重做" : "暂无错题，保持住！", () => _main.Navigate(new WrongBookPage(_main))));
        right.Children.Add(MakeEntry("题库管理", "整卷导入 · 题目编辑 · 导入历史", () => _main.Navigate(new BankManagePage(_main))));
        right.Children.Add(MakeEntry("设置", "卷面预设 · 须知文本 · 图片压缩", () => _main.Navigate(new SettingsPage(_main))));

        var about = new TextBlock
        {
            Text = "本软件为第三方模拟练习工具，界面与操作流程参照真实考试系统设计，仅供学习与备考演练之用，与中国证券业协会无任何隶属或授权关系，亦不含任何真实考试真题。练习试题需由使用者自行导入，软件本身不提供任何题库内容。",
            Foreground = Ui.TextSub,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0),
        };
        right.Children.Add(about);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        Body.Children.Add(grid);

        int totalQ = Db.GetSubjects().Sum(x => BankService.CountBySubject(x.Id));
        FooterStats.Text = $"本地题库共 {totalQ} 题 · 数据目录 {App.DataDir}";
    }

    private void RefreshSelection()
    {
        foreach (var card in _subjectCards)
        {
            bool sel = (long)card.Tag == _selectedSubjectId;
            card.BorderBrush = sel ? Ui.AuxBlue : Ui.Line;
            card.BorderThickness = new Thickness(sel ? 2 : 1);
        }
    }

    private TextBlock Chip(string text, string color, double margin)
    {
        return new TextBlock
        {
            Text = " " + text + " ",
            FontSize = 11,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            Background = new SolidColorBrush(Color.FromArgb(24, 0x2F, 0x6F, 0xED)),
            Padding = new Thickness(6, 2, 6, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(margin, 0, 0, 0),
        };
    }

    private Border MakeEntry(string title, string sub, Action onClick)
    {
        var sp = new StackPanel { Margin = new Thickness(2, 6, 2, 6) };
        sp.Children.Add(new TextBlock { Text = title, FontSize = 15.5, FontWeight = FontWeights.Bold, Foreground = Ui.TextMain });
        sp.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = Ui.TextSub, Margin = new Thickness(0, 3, 0, 0) });
        var b = new Border
        {
            Child = sp,
            Background = Brushes.White,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(18, 12, 18, 12),
            Margin = new Thickness(0, 0, 0, 10),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.MouseLeftButtonDown += (_, _) => onClick();
        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF8, 0xFF));
        b.MouseLeave += (_, _) => b.Background = Brushes.White;
        return b;
    }

    // ---------- 标准模拟卷选择 ----------

    private void ShowPaperPicker(SubjectDto s)
    {
        var papers = Db.GetPapers(s.Id, includeRandom: false);
        var dlg = new Window
        {
            Title = $"选择标准模拟卷 · {s.Name}",
            Width = 640,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = Brushes.White,
            ShowInTaskbar = false,
        };
        var root = new DockPanel { LastChildFill = true };
        var pickTop = Ui.TopBar($"标准模拟卷 · {s.Name}", height: 52);
        DockPanel.SetDock(pickTop, Dock.Top);
        root.Children.Add(pickTop);

        var body = new StackPanel { Margin = new Thickness(24) };
        if (papers.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "该科目暂无已导入的整卷。\n请到「题库管理」使用整卷导入（支持 xlsx / csv / json），或使用「随机练习」从题库抽题。",
                Foreground = Ui.TextSub,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 16),
                LineHeight = 24,
            });
            body.Children.Add(Ui.MakeButton("前往题库管理导入", () => { dlg.Close(); _main.Navigate(new BankManagePage(_main)); }, "primary"));
        }
        else
        {
            foreach (var p in papers)
            {
                var row = new Border
                {
                    BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10),
                    Background = Brushes.White,
                };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var left = new StackPanel();
                left.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Ui.TextMain });
                left.Children.Add(new TextBlock
                {
                    Text = $"{p.QuestionCount} 题 · 卷面总分 {Ui.FmtScore(p.TotalScore ?? 0)} 分 · 时长 {(p.DurationMinutes ?? s.DurationMinutes)} 分钟 · 导入于 {p.CreatedAt}",
                    Foreground = Ui.TextSub, FontSize = 12, Margin = new Thickness(0, 4, 0, 0),
                });
                var startBtn = Ui.MakeButton("开始考试", () =>
                {
                    dlg.Close();
                    _main.Navigate(new ExamLoginPage(_main, p.Id));
                }, "primary");
                startBtn.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(startBtn, 1);
                g.Children.Add(left);
                g.Children.Add(startBtn);
                row.Child = g;
                body.Children.Add(row);
            }
        }

        var close = Ui.MakeButton("关闭", () => dlg.Close());
        close.HorizontalAlignment = HorizontalAlignment.Center;
        close.Margin = new Thickness(0, 6, 0, 4);
        body.Children.Add(close);

        root.Children.Add(new ScrollViewer { MaxHeight = 460, Content = body });
        dlg.Content = root;
        dlg.ShowDialog();
    }

    // ---------- 随机组卷 ----------

    private void ShowRandomDialog(SubjectDto s)
    {
        var settings = SettingsService.Get();
        var preset = settings.Presets.FirstOrDefault(p => p.SubjectId == s.Id);
        if (preset == null)
        {
            SacDialog.Info(Window.GetWindow(this), "提示", "该科目缺少卷面预设，请到「设置」配置。");
            return;
        }

        var dlg = new Window
        {
            Width = 620,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = Brushes.White,
            ShowInTaskbar = false,
        };
        var root = new DockPanel { LastChildFill = true };
        var randTop = Ui.TopBar($"随机练习组卷 · {s.Name}", height: 52);
        DockPanel.SetDock(randTop, Dock.Top);
        root.Children.Add(randTop);
        var body = new StackPanel { Margin = new Thickness(26, 18, 26, 18) };

        body.Children.Add(new TextBlock { Text = "各题型数量与分值（可调整；单选 0.5 分/题，其余 1 分/题）", Foreground = Ui.TextSub, FontSize = 12 });
        var typeRows = new List<(string type, TextBox count, TextBox score)>();
        foreach (var t in preset.Types)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(new TextBlock { Text = t.Type, Width = 90, VerticalAlignment = VerticalAlignment.Center });
            var cnt = new TextBox { Text = t.Count.ToString(), Width = 70 };
            var sco = new TextBox { Text = Ui.FmtScore(t.Score), Width = 70 };
            row.Children.Add(cnt);
            row.Children.Add(new TextBlock { Text = " 题  ×  ", VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.TextSub });
            row.Children.Add(sco);
            row.Children.Add(new TextBlock { Text = " 分", VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.TextSub });
            body.Children.Add(row);
            typeRows.Add((t.Type, cnt, sco));
        }

        var durRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        durRow.Children.Add(new TextBlock { Text = "考试时长", Width = 90, VerticalAlignment = VerticalAlignment.Center });
        var dur = new TextBox { Text = preset.DurationMinutes.ToString(), Width = 70 };
        durRow.Children.Add(dur);
        durRow.Children.Add(new TextBlock { Text = " 分钟", VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.TextSub });
        body.Children.Add(durRow);

        var warn = new TextBlock { Foreground = Ui.Orange, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        body.Children.Add(warn);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        var go = Ui.MakeButton("生成并开始练习", () =>
        {
            var types = new List<SettingsService.TypePreset>();
            double total = 0;
            foreach (var (type, cnt, sco) in typeRows)
            {
                if (!int.TryParse(cnt.Text.Trim(), out int c) || c < 0) c = 0;
                double sc = double.TryParse(sco.Text.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0 ? v : (type == "单选题" ? 0.5 : 1);
                if (c > 0) { types.Add(new SettingsService.TypePreset { Type = type, Count = c, Score = sc }); total += c * sc; }
            }
            if (types.Count == 0) { warn.Text = "请至少设置一种题型数量大于 0。"; return; }
            if (!int.TryParse(dur.Text.Trim(), out int duration) || duration <= 0) duration = preset.DurationMinutes;

            var opts = new ExamService.RandomPaperOptions
            {
                SubjectId = s.Id,
                DurationMinutes = duration,
                Types = types,
            };
            var (paperId, randomIds, shortages) = ExamService.BuildRandomPaper(opts);
            dlg.Close();
            if (shortages.Count > 0)
                SacDialog.Info(Window.GetWindow(this), "题量提示",
                    "部分题型题库数量不足，已按现有题量组卷：\n" + string.Join("\n", shortages) + "\n\n练习即将开始。");
            _main.Navigate(new ExamLoginPage(_main, paperId, randomIds));
        }, "primary");
        btns.Children.Add(go);
        btns.Children.Add(new Border { Width = 14 });
        btns.Children.Add(Ui.MakeButton("取消", () => dlg.Close()));
        body.Children.Add(btns);

        root.Children.Add(new ScrollViewer { MaxHeight = 520, Content = body });
        dlg.Content = root;
        dlg.ShowDialog();
    }

    // ---------- 恢复考试 ----------

    public static void ResumeExam(ExamSession session)
    {
        var main = (MainWindow)Application.Current.MainWindow;
        if (session.Deadline <= DateTime.Now)
        {
            // 超时：自动交卷
            var res = ExamService.Submit(session);
            SacDialog.Info(main, "考试结束", "该场考试的剩余时间已用完，系统已按当前作答自动交卷。");
            main.Navigate(new ScoreReportPage(main, res));
            return;
        }
        main.Navigate(new ExamPage(main, session));
    }
}
