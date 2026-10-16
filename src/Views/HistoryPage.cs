using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SacExam.Services;

namespace SacExam;

/// <summary>历史记录列表：科目/卷名/得分/用时/日期，可回看解析、可删除；未完成的可继续。</summary>
public class HistoryPage : UserControl
{
    private readonly MainWindow _main;
    private readonly StackPanel _list = new();

    public HistoryPage(MainWindow main)
    {
        _main = main;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.TopBar("考试历史", BackButton("返回主页")));

        var stats = ExamService.GetHistory().Where(h => h.Status == "finished" && h.Score.HasValue).ToList();
        var bar = new Border { Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(26, 12, 26, 12) };
        if (stats.Count > 0)
            bar.Child = new TextBlock
            {
                Text = $"共 {stats.Count} 场考试 · 平均分 {stats.Average(h => h.Score.Value):0.0} · 最高分 {stats.Max(h => h.Score.Value):0.#} · 通过 {stats.Count(h => h.Pass == true)} 场（通过率 {stats.Count(h => h.Pass == true) * 100.0 / stats.Count:0}%）",
                Foreground = Ui.TextMain,
            };
        else bar.Child = new TextBlock { Text = "还没有考试记录，去主页开始第一场吧。", Foreground = Ui.TextSub };
        Grid.SetRow(bar, 1);
        root.Children.Add(bar);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _list.Margin = new Thickness(26, 16, 26, 20);
        scroll.Content = _list;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        Content = root;
        Rebuild();
    }

    private Button BackButton(string text) => Ui.MakeButton(text, () => _main.Navigate(new MainMenuPage(_main)));

    private void Rebuild()
    {
        _list.Children.Clear();
        var rows = ExamService.GetHistory();
        if (rows.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "暂无记录",
                Foreground = Ui.TextSub,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0),
            });
            return;
        }
        foreach (var h in rows) _list.Children.Add(MakeRow(h));
    }

    private FrameworkElement MakeRow(ExamService.HistoryRow h)
    {
        bool ongoing = h.Status == "ongoing";
        var g = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = h.PaperName, FontWeight = FontWeights.Bold, FontSize = 14.5, Foreground = Ui.TextMain, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(Chip(h.SubjectName, "#2F6FED"));
        titleRow.Children.Add(Chip(h.Mode == "paper" ? "标准整卷" : "随机/重做", "#7A5AF8"));
        if (ongoing) titleRow.Children.Add(Chip("进行中", "#F59A23"));
        else titleRow.Children.Add(Chip(h.Pass == true ? "通过" : "未通过", h.Pass == true ? "#22A45D" : "#E34040"));
        left.Children.Add(titleRow);
        left.Children.Add(new TextBlock
        {
            Text = ongoing
                ? $"开始于 {h.StartedAt} · 已答 {h.QuestionCount} 题（作答自动保存）"
                : $"{h.StartedAt} · 得分 {Ui.FmtScore(h.Score ?? 0)}/{Ui.FmtScore(h.TotalScore ?? 0)} · 答对 {h.QuestionCount} 题 · 用时 {Ui.FmtUsed(h.DurationUsed ?? 0)}",
            Foreground = Ui.TextSub,
            FontSize = 12,
            Margin = new Thickness(0, 5, 0, 0),
        });
        Grid.SetColumn(left, 0);
        g.Children.Add(left);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (ongoing)
        {
            var cont = Ui.MakeButton("继续考试", () =>
            {
                var s = ExamService.GetOngoingExams().FirstOrDefault(x => x.session.RecordId == h.RecordId).session;
                if (s != null) MainMenuPage.ResumeExam(s);
            }, "orange");
            btns.Children.Add(cont);
        }
        else
        {
            btns.Children.Add(Ui.MakeButton("查看解析", () =>
            {
                var res = ExamService.LoadFinished(h.RecordId);
                if (res != null)
                    _main.Navigate(new AnalysisPage(_main, res, h.SubjectName, h.PaperName));
            }, "primary"));
        }
        var del = Ui.MakeButton("删除", () =>
        {
            if (SacDialog.Confirm(Window.GetWindow(this), "删除记录", "删除后该场考试的作答明细与解析回放将不可恢复。确定删除？", dangerOk: true))
            {
                ExamService.DeleteHistory(h.RecordId);
                Rebuild();
            }
        });
        del.Margin = new Thickness(10, 0, 0, 0);
        btns.Children.Add(del);
        Grid.SetColumn(btns, 1);
        g.Children.Add(btns);

        return new Border
        {
            Child = g,
            Background = Brushes.White,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(18, 12, 18, 12),
            Margin = new Thickness(0, 0, 0, 10),
        };
    }

    private static TextBlock Chip(string text, string colorHex)
    {
        var c = (Color)ColorConverter.ConvertFromString(colorHex);
        return new TextBlock
        {
            Text = " " + text + " ",
            FontSize = 11,
            Foreground = new SolidColorBrush(c),
            Background = new SolidColorBrush(Color.FromArgb(24, c.R, c.G, c.B)),
            Padding = new Thickness(6, 2, 6, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
    }
}

/// <summary>错题本：按科目/题型筛选，重做错题（交卷后答对自动移出），单题移除。</summary>
public class WrongBookPage : UserControl
{
    private readonly MainWindow _main;
    private readonly ComboBox _subjectBox = new() { Width = 190, Height = 34 };
    private readonly ComboBox _typeBox = new() { Width = 140, Height = 34, Margin = new Thickness(10, 0, 0, 0) };
    private readonly StackPanel _list = new();

    public WrongBookPage(MainWindow main)
    {
        _main = main;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.TopBar("错题本", BackButton()));

        _subjectBox.Items.Add("全部科目");
        foreach (var s in Db.GetSubjects()) _subjectBox.Items.Add(s.Name);
        _subjectBox.SelectedIndex = 0;
        _typeBox.Items.Add("全部题型");
        foreach (var t in new[] { "单选题", "多选题", "判断题", "综合题" }) _typeBox.Items.Add(t);
        _typeBox.SelectedIndex = 0;
        _subjectBox.SelectionChanged += (_, _) => Rebuild();
        _typeBox.SelectionChanged += (_, _) => Rebuild();

        var bar = new Border { Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(26, 10, 26, 10) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_subjectBox);
        row.Children.Add(_typeBox);
        var redoBtn = Ui.MakeButton("重做当前筛选的错题", RedoFiltered, "orange");
        redoBtn.Margin = new Thickness(20, 0, 0, 0);
        row.Children.Add(redoBtn);
        var clearBtn = Ui.MakeButton("清空错题本", () =>
        {
            if (SacDialog.Confirm(Window.GetWindow(this), "清空错题本", "清空后需重新答错才会再次收录。确定清空？", dangerOk: true))
            {
                ExamService.ClearWrongBook(null);
                Rebuild();
            }
        });
        clearBtn.Margin = new Thickness(10, 0, 0, 0);
        row.Children.Add(clearBtn);
        var note = new TextBlock
        {
            Text = "重做交卷后：答对的题自动移出错题本",
            Foreground = Ui.TextSub,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0),
        };
        row.Children.Add(note);
        bar.Child = row;
        Grid.SetRow(bar, 1);
        root.Children.Add(bar);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _list.Margin = new Thickness(26, 16, 26, 20);
        scroll.Content = _list;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        Content = root;
        Rebuild();
    }

    private Button BackButton() => Ui.MakeButton("返回主页", () => _main.Navigate(new MainMenuPage(_main)));

    private (long? subjectId, string type) CurrentFilter()
    {
        long? sid = _subjectBox.SelectedIndex > 0 ? Db.GetSubjects()[_subjectBox.SelectedIndex - 1].Id : null;
        string type = _typeBox.SelectedIndex > 0 ? (string)_typeBox.SelectedItem : null;
        return (sid, type);
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var (sid, type) = CurrentFilter();
        var wrongs = ExamService.GetWrongBook(sid, type);
        if (wrongs.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "错题本是空的 —— 保持住！答错的题会自动收录到这里。",
                Foreground = Ui.TextSub,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0),
            });
            return;
        }
        foreach (var w in wrongs) _list.Children.Add(MakeRow(w));
    }

    private FrameworkElement MakeRow(ExamService.WrongRow w)
    {
        var g = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = w.Q.QType, FontSize = 11, Foreground = Ui.AuxBlue, Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF)), Padding = new Thickness(6, 2, 6, 3), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock { Text = $" 错 {w.WrongCount} 次 · 最近 {w.LastWrongAt}", FontSize = 11.5, Foreground = Ui.TextSub, VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(head);
        left.Children.Add(new TextBlock
        {
            Text = (w.Q.CaseMaterial != null ? "【案例】" : "") + w.Q.Stem,
            FontSize = 13.5,
            Foreground = Ui.TextMain,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 5, 0, 0),
            MaxHeight = 42,
        });
        left.Children.Add(new TextBlock
        {
            Text = $"上次错答：{(string.IsNullOrWhiteSpace(w.LastAnswer) ? "未作答" : w.LastAnswer)}　正确答案：{w.Q.Answer}",
            FontSize = 12,
            Foreground = Ui.TextSub,
            Margin = new Thickness(0, 4, 0, 0),
        });
        Grid.SetColumn(left, 0);
        g.Children.Add(left);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        btns.Children.Add(Ui.MakeButton("移除", () =>
        {
            ExamService.RemoveWrong(w.QuestionId);
            Rebuild();
        }));
        Grid.SetColumn(btns, 1);
        g.Children.Add(btns);

        return new Border
        {
            Child = g,
            Background = Brushes.White,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(18, 12, 18, 12),
            Margin = new Thickness(0, 0, 0, 10),
        };
    }

    private void RedoFiltered()
    {
        var (sid, type) = CurrentFilter();
        var ids = ExamService.GetWrongBook(sid, type).Select(w => w.QuestionId).ToList();
        if (ids.Count == 0)
        {
            SacDialog.Info(Window.GetWindow(this), "提示", "当前筛选下没有错题。");
            return;
        }
        if (!SacDialog.Confirm(Window.GetWindow(this), "错题重做",
                $"共 {ids.Count} 题，将生成一场限时练习（每题约 1.2 分钟，最少 10 分钟）。\n交卷后答对的题自动移出错题本。开始？", okText: "开始"))
            return;
        var (paperId, redoIds) = ExamService.CreateWrongRedoPaper(ids);
        // 与主页随机练习一致：走一遍轻量考前流程
        string name = "", no = "";
        try
        {
            string raw = Db.Scalar<string>("SELECT value FROM settings WHERE key='last_examinee'");
            if (!string.IsNullOrEmpty(raw))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                no = doc.RootElement.TryGetProperty("no", out var nn) ? nn.GetString() ?? "" : "";
            }
        }
        catch { }
        var main = _main;
        var session = ExamService.Start(paperId, "random", string.IsNullOrEmpty(name) ? "考生" : name, string.IsNullOrEmpty(no) ? "000000" : no, redoIds);
        main.Navigate(new ExamPage(main, session));
    }
}
