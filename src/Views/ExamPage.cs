using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SacExam.Services;

namespace SacExam;

/// <summary>屏4：答题页（协会机考风格三区布局，还原 ui_答题页.png / img04-06）。</summary>
public class ExamPage : UserControl
{
    private readonly MainWindow _main;
    private readonly ExamSession _session;

    private TextBlock _timerText;
    private Border _timerBox;
    private TextBlock _progressText;
    private StackPanel _sectionsPanel;
    private ScrollViewer _questionScroll;
    private StackPanel _questionPanel;
    private CheckBox _markCheck;
    private readonly List<Border> _chips = new();
    private readonly List<TextBlock> _chipMarks = new();
    private DispatcherTimer _timer;
    private bool _blink;
    private bool _finished;
    private bool _submitting;
    private bool _suppressMarkEvent;
    private CalculatorWindow _calcWindow;

    public bool IsFinished => _finished;

    public ExamPage(MainWindow main, ExamSession session)
    {
        _main = main;
        _session = session;
        Focusable = true;
        Loaded += (_, _) =>
        {
            Focus();
            Build();
            StartTimer();
        };
    }

    public void DetachTimer() => _timer?.Stop();

    /// <summary>打开计算器：持有窗口引用，防止 GC 回收导致窗口闪退；重复点击仅激活。</summary>
    private void OpenCalc()
    {
        if (_calcWindow != null) { _calcWindow.Activate(); return; }
        _calcWindow = new CalculatorWindow(Window.GetWindow(this));
        _calcWindow.Closed += (_, _) => _calcWindow = null;
        _calcWindow.Show();
    }

    // ===================== 布局 =====================

    private void Build()
    {
        var root = new Grid { Background = Ui.PageBg };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- 顶栏 ----
        var top = new Border { Background = Ui.DeepBlue, Padding = new Thickness(20, 0, 20, 0) };
        var topGrid = new Grid();
        var leftInfo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        leftInfo.Children.Add(new TextBlock
        {
            Text = $"{_session.SubjectName}　考生：{_session.ExamineeName}　准考证号：{_session.ExamineeNo}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD9, 0xF2)),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _progressText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0),
        };
        leftInfo.Children.Add(_progressText);

        var title = new TextBlock
        {
            Text = _session.PaperName,
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 640,
        };

        var rightBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        _timerBox = new Border
        {
            Background = Ui.Orange,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(14, 8, 14, 8),
            Child = new TextBlock { Name = "TT", Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Bold },
        };
        _timerText = (TextBlock)_timerBox.Child;
        var submitTop = Ui.MakeButton("交卷", () => SubmitFlow(auto: false), "danger");
        submitTop.Margin = new Thickness(14, 0, 0, 0);
        rightBox.Children.Add(_timerBox);
        rightBox.Children.Add(submitTop);

        topGrid.Children.Add(leftInfo);
        topGrid.Children.Add(title);
        topGrid.Children.Add(rightBox);
        top.Child = topGrid;
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        // ---- 中部：左题号区 + 右试题区 ----
        var mid = new Grid();
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) });
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftPanel = BuildLeftPanel();
        Grid.SetColumn(leftPanel, 0);
        mid.Children.Add(leftPanel);

        _questionScroll = new ScrollViewer
        {
            Background = Brushes.White,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(4, 0, 4, 0),
        };
        _questionPanel = new StackPanel { Margin = new Thickness(30, 24, 34, 30) };
        _questionScroll.Content = _questionPanel;
        var qBorder = new Border
        {
            Background = Brushes.White,
            Margin = new Thickness(14, 14, 14, 0),
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(1),
            Child = _questionScroll,
        };
        Grid.SetColumn(qBorder, 1);
        mid.Children.Add(qBorder);

        Grid.SetRow(mid, 1);
        root.Children.Add(mid);

        // ---- 底部操作条 ----
        var bottomWrap = new StackPanel();
        var bottom = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFD)),
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 12, 24, 12),
        };
        var bg = new Grid();
        var leftBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var calcBtn = Ui.MakeButton("计 算 器", OpenCalc, "ghost");
        calcBtn.Padding = new Thickness(18, 7, 18, 7);
        _markCheck = new CheckBox { Content = "标记", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0), FontSize = 13.5 };
        _markCheck.Checked += (_, _) => { if (!_suppressMarkEvent) SetMark(true); };
        _markCheck.Unchecked += (_, _) => { if (!_suppressMarkEvent) SetMark(false); };
        var prevBtn = Ui.MakeButton("上一题", () => Move(-1), "ghost");
        prevBtn.Padding = new Thickness(20, 7, 20, 7);
        prevBtn.Margin = new Thickness(18, 0, 0, 0);
        var nextBtn = Ui.MakeButton("下一题", () => Move(1), "primary");
        nextBtn.Padding = new Thickness(20, 7, 20, 7);
        nextBtn.Margin = new Thickness(10, 0, 0, 0);
        leftBtns.Children.Add(calcBtn);
        leftBtns.Children.Add(_markCheck);
        leftBtns.Children.Add(prevBtn);
        leftBtns.Children.Add(nextBtn);

        var submitBottom = Ui.MakeButton("交 卷", () => SubmitFlow(auto: false), "danger");
        submitBottom.Padding = new Thickness(34, 8, 34, 8);
        submitBottom.HorizontalAlignment = HorizontalAlignment.Right;

        bg.Children.Add(leftBtns);
        bg.Children.Add(submitBottom);
        bottom.Child = bg;
        bottomWrap.Children.Add(bottom);
        Grid.SetRow(bottomWrap, 2);
        root.Children.Add(bottomWrap);

        Content = root;

        RefreshChips();
        ShowQuestion(0);
    }

    private Border BuildLeftPanel()
    {
        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(0, 0, 1, 0),
        };
        var dock = new DockPanel { LastChildFill = true };

        // 顶部考生信息（img05 左栏顶部）
        var headGrid = new Grid { Margin = new Thickness(12, 10, 12, 8) };
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        headGrid.ColumnDefinitions.Add(new ColumnDefinition());
        headGrid.Children.Add(new Border
        {
            Width = 60, Height = 72,
            Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF3, 0xFA)),
            Child = new TextBlock
            {
                Text = "👤", FontSize = 30,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        infoStack.Children.Add(new TextBlock { Text = _session.ExamineeName, FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Ui.TextMain });
        infoStack.Children.Add(new TextBlock { Text = _session.ExamineeNo, FontSize = 11.5, Foreground = Ui.TextSub, Margin = new Thickness(0, 3, 0, 0) });
        infoStack.Children.Add(new TextBlock { Text = "此照片是考场\n考前拍摄（模拟）", FontSize = 10, Foreground = Ui.TextSub, Margin = new Thickness(0, 3, 0, 0), LineHeight = 13 });
        Grid.SetColumn(infoStack, 1);
        headGrid.Children.Add(infoStack);
        DockPanel.SetDock(headGrid, Dock.Top);
        dock.Children.Add(headGrid);

        // 底部红色说明（img05 底部原文）
        var legend = new TextBlock
        {
            Foreground = Ui.Red,
            FontSize = 11.5,
            LineHeight = 20,
            Margin = new Thickness(14, 8, 10, 12),
            TextWrapping = TextWrapping.Wrap,
            Text = "说明：\n1.题号右方“*”符号代表该小题尚未选择答案。\n2.题号右方“?”符号代表该小题做过标记。\n3.选择选项的同时自动保存答案。",
        };
        DockPanel.SetDock(legend, Dock.Bottom);
        dock.Children.Add(legend);

        _sectionsPanel = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _sectionsPanel };
        dock.Children.Add(scroll);

        border.Child = dock;
        return border;
    }

    private void RefreshChips()
    {
        _chips.Clear();
        _chipMarks.Clear();
        _sectionsPanel.Children.Clear();

        int displayNo = 0;
        foreach (var group in _session.Items.GroupBy(x => x.Q.QType)) // 保持卷内顺序分组
        {
            var items = group.ToList();
            int start = _session.Items.IndexOf(items[0]) + 1;
            int end = start + items.Count - 1;
            double perScore = items[0].Q.Score;
            double sum = items.Sum(x => x.Q.Score);

            var header = new Border
            {
                Background = Ui.DeepBlue,
                Padding = new Thickness(0, 5, 0, 5),
                Margin = new Thickness(0, 4, 0, 8),
                Child = new TextBlock
                {
                    Text = $"{group.Key}（{start}-{end}）",
                    Foreground = Brushes.White,
                    FontSize = 12.5,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            };
            _sectionsPanel.Children.Add(header);

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var it in items)
            {
                displayNo = _session.Items.IndexOf(it) + 1;
                var numText = new TextBlock
                {
                    Text = displayNo.ToString(),
                    FontSize = 11.5,
                    Foreground = Ui.TextMain,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var mark = new TextBlock
                {
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Ui.Red,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 1, 0),
                };
                var chipGrid = new Grid { Width = 27, Height = 25 };
                chipGrid.Children.Add(numText);
                chipGrid.Children.Add(mark);
                var chip = new Border
                {
                    Width = 28, Height = 26,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0xCF, 0xE2)),
                    BorderThickness = new Thickness(1),
                    Background = Brushes.White,
                    Margin = new Thickness(1.5),
                    Child = chipGrid,
                    Cursor = Cursors.Hand,
                    Tag = displayNo - 1,
                };
                chip.MouseLeftButtonDown += (_, _) => ShowQuestion((int)chip.Tag);
                wrap.Children.Add(chip);
                _chips.Add(chip);
                _chipMarks.Add(mark);
            }
            _sectionsPanel.Children.Add(wrap);
        }
        UpdateChipStates();
    }

    private void UpdateChipStates()
    {
        for (int i = 0; i < _session.Items.Count && i < _chips.Count; i++)
        {
            var st = _session.Items[i];
            bool answered = ExamService.Normalize(st.Answer).Length > 0;
            _chips[i].Background = answered ? Ui.ChipBlue : Brushes.White;
            _chipMarks[i].Text = st.Marked ? "?" : (answered ? "" : "*");
        }
        int answeredCount = _session.Items.Count(x => ExamService.Normalize(x.Answer).Length > 0);
        _progressText.Text = $"已答 {answeredCount} / {_session.Items.Count} 题";
    }

    // ===================== 试题区 =====================

    private int _current = -1;

    private void ShowQuestion(int idx)
    {
        if (idx < 0 || idx >= _session.Items.Count) return;
        _current = idx;
        var st = _session.Items[idx];
        var q = st.Q;

        // 题号格红框当前题
        for (int i = 0; i < _chips.Count; i++)
        {
            _chips[i].BorderBrush = i == idx ? Ui.Red : new SolidColorBrush(Color.FromRgb(0xC4, 0xCF, 0xE2));
            _chips[i].BorderThickness = new Thickness(i == idx ? 2 : 1);
        }
        _suppressMarkEvent = true;
        _markCheck.IsChecked = st.Marked;
        _suppressMarkEvent = false;

        _questionPanel.Children.Clear();
        _questionScroll.ScrollToTop();

        // 题型统计行
        var sameType = _session.Items.Where(x => x.Q.QType == q.QType).ToList();
        int tStart = _session.Items.IndexOf(sameType[0]) + 1;
        int tEnd = tStart + sameType.Count - 1;
        double perScore = sameType[0].Q.Score;
        double sum = sameType.Sum(x => x.Q.Score);
        _questionPanel.Children.Add(new TextBlock
        {
            Text = $"{q.QType}（共 {sameType.Count} 题，每小题 {Ui.FmtScore(perScore)} 分。共 {Ui.FmtScore(sum)} 分）",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Ui.TextMain,
        });
        _questionPanel.Children.Add(new TextBlock
        {
            Text = Ui.TypeInstruction(q.QType),
            FontSize = 12.5,
            Foreground = Ui.TextSub,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        _questionPanel.Children.Add(new Border
        {
            Height = 1,
            Background = Ui.Line,
            Margin = new Thickness(0, 14, 0, 14),
        });

        // 第 N 题（x 分）
        _questionPanel.Children.Add(new TextBlock
        {
            Text = $"第 {idx + 1} 题　（{Ui.FmtScore(q.Score)} 分）",
            FontSize = 16.5,
            FontWeight = FontWeights.Bold,
            Foreground = Ui.TextMain,
            Margin = new Thickness(0, 0, 0, 10),
        });

        // 案例材料框（综合题）
        if (!string.IsNullOrEmpty(q.CaseMaterial))
        {
            var matScroll = new ScrollViewer { MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            matScroll.Content = Ui.BuildRichText(q.CaseMaterial, 14);
            var matBox = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFA)),
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 12),
                Child = matScroll,
            };
            _questionPanel.Children.Add(matBox);
        }

        // 题干（含 {{img:}} 内联与题干图片）
        _questionPanel.Children.Add(Ui.BuildRichText(q.Stem, 15));
        foreach (var imgPath in q.StemImages)
        {
            var img = Ui.MakeZoomableImage(imgPath, double.NaN, 300);
            img.Margin = new Thickness(0, 8, 0, 4);
            _questionPanel.Children.Add(img);
        }

        bool isMulti = q.QType is "多选题" or "综合题";
        var optionWrap = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };

        foreach (var opt in q.Options)
        {
            int i = q.Options.IndexOf(opt);
            bool selected = ExamService.Normalize(st.Answer).Contains(opt.Label);
            var row = BuildOptionRow(opt, i, isMulti, selected);
            optionWrap.Children.Add(row);
        }
        _questionPanel.Children.Add(optionWrap);

        if (q.QType == "综合题")
        {
            _questionPanel.Children.Add(new TextBlock
            {
                Text = "综合题提示：题干含【案例材料】，为不定项选择，可能单选也可能多选；多选、漏选、错选均不得分。",
                Foreground = Ui.TextSub,
                FontSize = 12,
                Margin = new Thickness(0, 16, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        UpdateChipStates();
    }

    private Border BuildOptionRow(OptionDto opt, int index, bool isMulti, bool selected)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = Ui.OptionIcon(isMulti, selected);
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var textCol = new StackPanel();
        var label = new TextBlock { FontSize = 14.5, Foreground = Ui.TextMain, TextWrapping = TextWrapping.Wrap, LineHeight = 27 };
        label.Inlines.Add(new System.Windows.Documents.Run($"{opt.Label}、") { FontWeight = FontWeights.SemiBold });
        label.Inlines.Add(opt.Content);
        textCol.Children.Add(label);
        foreach (var p in opt.ImageFiles)
        {
            var img = Ui.MakeZoomableImage(p, 320, 260); // §5.9：选项图缩略宽 ≤320，点击放大
            img.Margin = new Thickness(0, 6, 0, 2);
            img.HorizontalAlignment = HorizontalAlignment.Left;
            textCol.Children.Add(img);
        }
        Grid.SetColumn(textCol, 2);
        row.Children.Add(textCol);

        var border = new Border
        {
            Child = row,
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(0, 0, 0, 8),
            BorderBrush = selected ? Ui.AuxBlue : new SolidColorBrush(Color.FromRgb(0xDC, 0xE3, 0xEE)),
            BorderThickness = new Thickness(selected ? 1.6 : 1),
            Background = selected ? new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF)) : Brushes.White,
            CornerRadius = new CornerRadius(3),
            Cursor = Cursors.Hand,
            Tag = index,
        };
        border.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (isMulti) ToggleOption(index);
            else PickSingle(index);
        };
        return border;
    }

    // ===================== 作答 =====================

    private void PickSingle(int optionIndex)
    {
        var st = _session.Items[_current];
        string label = st.Q.Options[optionIndex].Label;
        bool already = ExamService.Normalize(st.Answer) == label;
        st.Answer = already ? "" : label; // 再点一次取消（真实机考也允许重选）
        PersistAnswer(st);
        ShowQuestion(_current);
    }

    private void ToggleOption(int optionIndex)
    {
        var st = _session.Items[_current];
        string label = st.Q.Options[optionIndex].Label;
        var set = ExamService.LetterSet(st.Answer);
        if (!set.Add(label)) set.Remove(label);
        st.Answer = string.Concat(set.OrderBy(c => c));
        PersistAnswer(st);
        ShowQuestion(_current);
    }

    private void SetMark(bool marked)
    {
        var st = _session.Items[_current];
        if (st.Marked == marked) return;
        st.Marked = marked;
        ExamService.SaveMark(_session.RecordId, st.Q.Id, marked);
        UpdateChipStates();
    }

    private void PersistAnswer(ExamQuestionState st)
    {
        ExamService.SaveAnswer(_session.RecordId, st.Q.Id, st.Answer, st.Marked);
    }

    private void Move(int delta)
    {
        int next = Math.Clamp(_current + delta, 0, _session.Items.Count - 1);
        if (next != _current) ShowQuestion(next);
    }

    // ===================== 计时与交卷 =====================

    private void StartTimer()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    private void Tick()
    {
        if (_finished) { _timer.Stop(); return; }
        var remain = _session.Deadline - DateTime.Now;
        if (remain.TotalSeconds <= 0)
        {
            _timerText.Text = "考试剩余时间：0:00:00";
            _timer.Stop();
            SubmitFlow(auto: true);
            return;
        }
        _timerText.Text = $"考试剩余时间：{Ui.FmtClock(remain.TotalSeconds)}";
        int warnMin = SettingsService.Get().WarnRemainMinutes;
        if (remain.TotalMinutes <= warnMin)
        {
            _blink = !_blink;
            _timerBox.Background = Ui.Red;
            _timerBox.Opacity = _blink ? 1 : 0.55;
        }
    }

    private void SubmitFlow(bool auto)
    {
        if (_submitting || _finished) return;
        _submitting = true;
        try
        {
            int total = _session.Items.Count;
            int unanswered = _session.Items.Count(x => ExamService.Normalize(x.Answer).Length == 0);
            string msg = auto
                ? $"考试时间已到。\n共 {total} 题，未答 {unanswered} 题\n确定交卷？"
                : $"共 {total} 题，未答 {unanswered} 题\n确定交卷？";
            bool ok = SacDialog.Confirm(Window.GetWindow(this), "中国证券行业专业人员水平评价测试（模拟）",
                msg, okText: "确 定",
                autoOkSeconds: auto ? 5 : null,
                highlight: ("，未答 ", " 题"));
            if (!ok) return;

            var res = ExamService.Submit(_session);
            _finished = true;
            DetachTimer();

            // “考试结束”过场 1 秒 → 成绩报告
            var overlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x10, 0x2A, 0x4C)),
                Child = new TextBlock
                {
                    Text = "考试结束",
                    Foreground = Brushes.White,
                    FontSize = 46,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var root = (Grid)Content;
            root.Children.Add(overlay);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                _main.Navigate(new ScoreReportPage(_main, res));
            };
            t.Start();
        }
        finally
        {
            _submitting = false;
        }
    }
}
