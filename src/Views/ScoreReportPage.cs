using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SacExam.Services;

namespace SacExam;

/// <summary>屏7：成绩报告页（还原 ui_成绩报告页.png）。</summary>
public class ScoreReportPage : UserControl
{
    private readonly MainWindow _main;
    private readonly ExamResultDto _result;
    private readonly string _subjectName;
    private readonly string _paperName;

    public ScoreReportPage(MainWindow main, ExamResultDto result, string subjectName = null, string paperName = null)
    {
        _main = main;
        _result = result;
        var rec = Db.Rows("SELECT s.name AS sn, p.name AS pn FROM exam_records er JOIN subjects s ON s.id=er.subject_id JOIN papers p ON p.id=er.paper_id WHERE er.id=@r",
            ("@r", result.RecordId)).FirstOrDefault();
        _subjectName = subjectName ?? rec?["sn"] as string ?? "";
        _paperName = paperName ?? rec?["pn"] as string ?? "";

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var chip = Ui.MakeButton("考试已结束", null, "orange");
        root.Children.Add(Ui.TopBar("考试成绩报告", chip));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(40, 30, 40, 34),
            Width = 860,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 28, 0, 28),
        };
        var sp = new StackPanel();

        sp.Children.Add(new TextBlock
        {
            Text = $"交卷成功 · {_subjectName}（{_paperName}）",
            Foreground = Ui.TextSub,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = Ui.FmtScore(result.GotScore),
            FontSize = 66,
            FontWeight = FontWeights.Bold,
            Foreground = Ui.TextMain,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0),
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"/ {Ui.FmtScore(result.TotalScore)} 分",
            Foreground = Ui.TextSub,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 12),
        });

        var passRate = Db.GetSubject(Db.Scalar<long>("SELECT subject_id FROM exam_records WHERE id=@r", ("@r", result.RecordId)))?.PassRate ?? 0.6;
        double passLine = Math.Round(result.TotalScore * passRate, 1);
        sp.Children.Add(new Border
        {
            Background = result.Pass ? new SolidColorBrush(Color.FromRgb(0xE7, 0xF7, 0xEE)) : new SolidColorBrush(Color.FromRgb(0xFD, 0xEC, 0xEC)),
            BorderBrush = result.Pass ? Ui.Green : Ui.Red,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(18, 8, 18, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = result.Pass ? $"✔ 通过（及格线 {Ui.FmtScore(passLine)} 分）" : $"✘ 未通过（及格线 {Ui.FmtScore(passLine)} 分）",
                Foreground = result.Pass ? Ui.Green : Ui.Red,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
            },
        });

        sp.Children.Add(new TextBlock
        {
            Text = $"用时 {Ui.FmtUsed(result.UsedSeconds)}　·　答对 {result.CorrectCount} / {result.QuestionCount} 题　·　正确率 {(result.QuestionCount == 0 ? 0 : result.CorrectCount * 100.0 / result.QuestionCount):0}%",
            Foreground = Ui.TextMain,
            FontSize = 13.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 20),
        });

        // 题型得分表
        var table = MakeScoreTable();
        sp.Children.Add(table);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
        btns.Children.Add(Ui.MakeButton("查看解析", () =>
            _main.Navigate(new AnalysisPage(_main, result, _subjectName, _paperName)), "primary"));
        btns.Children.Add(new Border { Width = 16 });
        btns.Children.Add(Ui.MakeButton("错题本", () => _main.Navigate(new WrongBookPage(_main)), "orangeLine"));
        btns.Children.Add(new Border { Width = 16 });
        btns.Children.Add(Ui.MakeButton("返回主页", () => _main.Navigate(new MainMenuPage(_main))));
        sp.Children.Add(btns);

        card.Child = sp;
        scroll.Content = card;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
    }

    private Grid MakeScoreTable()
    {
        var grid = new Grid { Width = 620, HorizontalAlignment = HorizontalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 表头
        foreach (var _ in _result.ByType) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void HeaderCell(string text, int c)
        {
            var b = new Border
            {
                Background = Ui.DeepBlue,
                Padding = new Thickness(12, 9, 12, 9),
                Child = new TextBlock { Text = text, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13 },
            };
            Grid.SetColumn(b, c);
            grid.Children.Add(b);
        }
        HeaderCell("题型", 0);
        HeaderCell("题量", 1);
        HeaderCell("答对", 2);
        HeaderCell("得分 / 满分", 3);

        int r = 1;
        foreach (var t in _result.ByType.OrderBy(x => TypeOrder(x.Type)))
        {
            var cells = new[]
            {
                $"{t.Type}（{Ui.FmtScore(t.Count > 0 && Math.Abs(t.FullScore / t.Count - 0.5) < 1e-9 ? 0.5 : 1)}分/题）",
                t.Count.ToString(),
                t.CorrectCount.ToString(),
                $"{Ui.FmtScore(t.Score)} / {Ui.FmtScore(t.FullScore)}",
            };
            var rowBorder = new Border
            {
                Background = r % 2 == 0 ? new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFD)) : Brushes.White,
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            var rowGrid = new Grid();
            foreach (var cd in grid.ColumnDefinitions) rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = cd.Width });
            for (int c = 0; c < 4; c++)
            {
                var tb = new TextBlock
                {
                    Text = cells[c],
                    Padding = new Thickness(12, 10, 12, 10),
                    FontSize = 13,
                    Foreground = Ui.TextMain,
                    FontWeight = c == 3 ? FontWeights.SemiBold : FontWeights.Regular,
                };
                Grid.SetColumn(tb, c);
                rowGrid.Children.Add(tb);
            }
            rowBorder.Child = rowGrid;
            Grid.SetRow(rowBorder, r);
            Grid.SetColumnSpan(rowBorder, 4);
            grid.Children.Add(rowBorder);
            r++;
        }
        return grid;
    }

    private static int TypeOrder(string t) => t switch
    {
        "单选题" => 0, "多选题" => 1, "判断题" => 2, _ => 3,
    };
}

/// <summary>屏8：解析页（逐题回放：你的答案/正确答案/解析全文；只看错题、按题型筛选）。</summary>
public class AnalysisPage : UserControl
{
    private readonly MainWindow _main;
    private readonly ExamResultDto _result;
    private readonly string _subjectName, _paperName;
    private List<ExamQuestionState> _filtered = new();
    private int _idx;

    private readonly CheckBox _onlyWrong = new() { Content = "只看错题", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
    private readonly ComboBox _typeFilter = new() { Width = 140, Height = 32 };
    private readonly StackPanel _body = new();

    public AnalysisPage(MainWindow main, ExamResultDto result, string subjectName, string paperName)
    {
        _main = main;
        _result = result;
        _subjectName = subjectName;
        _paperName = paperName;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var rightChip = new Border
        {
            Background = Ui.Orange,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(16, 8, 16, 8),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = $"得分 {Ui.FmtScore(result.GotScore)} / {Ui.FmtScore(result.TotalScore)}", Foreground = Brushes.White, FontWeight = FontWeights.Bold },
        };
        root.Children.Add(Ui.TopBar($"答案解析 · {_paperName}", rightChip));

        // 筛选行
        var filterBar = new Border { Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(26, 10, 26, 10) };
        var fRow = new StackPanel { Orientation = Orientation.Horizontal };
        _typeFilter.Items.Add("全部题型");
        foreach (var t in result.Items.Select(x => x.Q.QType).Distinct()) _typeFilter.Items.Add(t);
        _typeFilter.SelectedIndex = 0;
        _typeFilter.SelectionChanged += (_, _) => Rebuild();
        _onlyWrong.Checked += (_, _) => Rebuild();
        _onlyWrong.Unchecked += (_, _) => Rebuild();
        fRow.Children.Add(_typeFilter);
        fRow.Children.Add(_onlyWrong);
        fRow.Children.Add(new TextBlock
        {
            Text = $"{_subjectName}　·　答对 {result.CorrectCount}/{result.QuestionCount} 题",
            Foreground = Ui.TextSub,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0),
        });
        var back = Ui.MakeButton("返回", () => _main.Navigate(new MainMenuPage(_main)));
        back.HorizontalAlignment = HorizontalAlignment.Right;
        var fGrid = new Grid();
        fGrid.Children.Add(fRow);
        fGrid.Children.Add(back);
        filterBar.Child = fGrid;
        Grid.SetRow(filterBar, 1);
        root.Children.Add(filterBar);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var wrap = new StackPanel { Margin = new Thickness(0, 18, 0, 20) };
        _body.Width = 900;
        _body.HorizontalAlignment = HorizontalAlignment.Center;
        wrap.Children.Add(_body);
        var nav = MakeNavRow();
        wrap.Children.Add(nav);
        scroll.Content = wrap;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        Content = root;
        Rebuild();
    }

    private FrameworkElement MakeNavRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        var prev = Ui.MakeButton("← 上一题", () => { if (_idx > 0) { _idx--; Render(); } });
        var next = Ui.MakeButton("下一题 →", () => { if (_idx < _filtered.Count - 1) { _idx++; Render(); } }, "primary");
        row.Children.Add(prev);
        row.Children.Add(new Border { Width = 12 });
        row.Children.Add(next);
        return row;
    }

    private void Rebuild()
    {
        string type = _typeFilter.SelectedItem as string;
        bool onlyWrong = _onlyWrong.IsChecked == true;
        _filtered = _result.Items
            .Where(x => (type == "全部题型" || type == null || x.Q.QType == type) && (!onlyWrong || !x.Correct))
            .ToList();
        _idx = 0;
        Render();
    }

    private void Render()
    {
        _body.Children.Clear();
        if (_filtered.Count == 0)
        {
            _body.Children.Add(new TextBlock
            {
                Text = "没有符合条件的题目（筛选“只看错题”且全部答对时无内容）。",
                Foreground = Ui.TextSub,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0),
            });
            return;
        }
        var st = _filtered[Math.Clamp(_idx, 0, _filtered.Count - 1)];
        var q = st.Q;

        int absNo = _result.Items.IndexOf(_result.Items.First(x => x.Q.Id == q.Id)) + 1;

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(30, 22, 30, 24),
        };
        var sp = new StackPanel();
        card.Child = sp;

        // 头部：题号 + 类型 + 对错
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(new TextBlock { Text = $"第 {absNo} 题", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Ui.TextMain, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(ChipText(q.QType, "#2F6FED"));
        head.Children.Add(ChipText($"{Ui.FmtScore(q.Score)} 分", "#5B6B85"));
        head.Children.Add(ChipText(st.Correct ? "✔ 答对" : "✘ 答错", st.Correct ? "#22A45D" : "#E34040"));
        sp.Children.Add(head);

        if (!string.IsNullOrEmpty(q.CaseMaterial))
        {
            sp.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFA)),
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 12),
                Child = Ui.BuildRichText(q.CaseMaterial, 14),
            });
        }

        sp.Children.Add(Ui.BuildRichText(q.Stem, 15));
        foreach (var p in q.StemImages)
        {
            var img = Ui.MakeZoomableImage(p, double.NaN, 300);
            img.Margin = new Thickness(0, 8, 0, 4);
            sp.Children.Add(img);
        }

        // 选项（着色：正确=绿、错选=红、漏选=绿描边）
        var picked = ExamService.LetterSet(st.Answer);
        var correct = ExamService.LetterSet(q.Answer);
        var opts = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var opt in q.Options)
        {
            bool isPicked = picked.Contains(opt.Label);
            bool isCorrect = correct.Contains(opt.Label);
            Brush borderBrush = isCorrect ? Ui.Green : isPicked ? Ui.Red : new SolidColorBrush(Color.FromRgb(0xDC, 0xE3, 0xEE));
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var tag = new TextBlock
            {
                Text = opt.Label + "、",
                FontWeight = FontWeights.SemiBold,
                Foreground = Ui.TextMain,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetColumn(tag, 0);
            row.Children.Add(tag);
            var content = new StackPanel();
            var txt = new TextBlock { Text = opt.Content, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = Ui.TextMain };
            content.Children.Add(txt);
            foreach (var p in opt.ImageFiles)
            {
                var img = Ui.MakeZoomableImage(p, 320, 240);
                img.Margin = new Thickness(0, 4, 0, 0);
                img.HorizontalAlignment = HorizontalAlignment.Left;
                content.Children.Add(img);
            }
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            var optBorder = new Border
            {
                Child = row,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(isCorrect && !isPicked ? 1.6 : 1.4),
                Background = isCorrect ? new SolidColorBrush(Color.FromRgb(0xEC, 0xFA, 0xF2))
                          : isPicked ? new SolidColorBrush(Color.FromRgb(0xFD, 0xEC, 0xEC))
                          : Brushes.White,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 6),
            };
            opts.Children.Add(optBorder);
        }
        sp.Children.Add(opts);

        // 答案行
        var ansRow = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        ansRow.Children.Add(new TextBlock
        {
            Text = "你的答案：",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });
        ansRow.Children.Add(new TextBlock
        {
            Text = picked.Count == 0 ? "未作答" : string.Join(" ", picked.OrderBy(c => c)),
            Foreground = picked.Count == 0 ? Ui.TextSub : picked.SetEquals(correct) ? Ui.Green : Ui.Red,
            FontWeight = FontWeights.Bold,
            FontSize = 14.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 24, 0),
        });
        ansRow.Children.Add(new TextBlock { Text = "正确答案：", FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        ansRow.Children.Add(new TextBlock
        {
            Text = string.Join(" ", correct.OrderBy(c => c)),
            Foreground = Ui.Green,
            FontWeight = FontWeights.Bold,
            FontSize = 14.5,
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(ansRow);

        // 解析
        sp.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF9, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC7, 0xD9, 0xF5)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 14, 0, 0),
            Child = new StackPanel(),
        });
        var anaBox = (Border)sp.Children[^1];
        var anaPanel = (StackPanel)anaBox.Child;
        anaPanel.Children.Add(new TextBlock { Text = "【答案解析】", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x92)), FontSize = 13.5, Margin = new Thickness(0, 0, 0, 6) });
        anaPanel.Children.Add(string.IsNullOrWhiteSpace(q.Analysis)
            ? new TextBlock { Text = "（本导入卷未提供解析）", Foreground = Ui.TextSub, FontStyle = FontStyles.Italic, FontSize = 13.5 }
            : Ui.BuildRichText(q.Analysis, 14));
        foreach (var p in q.AnalysisImages)
        {
            var img = Ui.MakeZoomableImage(p, double.NaN, 280);
            img.Margin = new Thickness(0, 8, 0, 0);
            anaPanel.Children.Add(img);
        }

        _body.Children.Add(card);
    }

    private static TextBlock ChipText(string text, string colorHex)
    {
        var c = (Color)ColorConverter.ConvertFromString(colorHex);
        return new TextBlock
        {
            Text = " " + text + " ",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(c),
            Background = new SolidColorBrush(Color.FromArgb(26, c.R, c.G, c.B)),
            Padding = new Thickness(7, 2, 7, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
    }
}
