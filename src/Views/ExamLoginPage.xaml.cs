using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SacExam.Services;

namespace SacExam;

/// <summary>屏2：考前登录/考生信息确认（还原 img01/img02）。</summary>
public class ExamLoginPage : UserControl
{
    private readonly MainWindow _main;
    private readonly long _paperId;
    private readonly List<long> _randomIds;
    private readonly SubjectDto _subject;
    private readonly string _paperName;
    private readonly int _duration;

    private TextBox _tbNo, _tbName, _tbId;
    private Border _loginCard, _confirmCard;
    private StackPanel _confirmFields;
    private (string no, string name, string id) _examinee;

    public ExamLoginPage(MainWindow main, long paperId, List<long> randomIds = null)
    {
        _main = main;
        _paperId = paperId;
        _randomIds = randomIds;
        var paper = Db.Rows("SELECT * FROM papers WHERE id=@p", ("@p", paperId)).FirstOrDefault();
        _subject = Db.GetSubject(paper != null ? (long)paper["subject_id"] : 1);
        _paperName = paper?["name"] as string ?? "";
        _duration = paper?["duration_minutes"] is long d && d > 0 ? (int)d : _subject.DurationMinutes;

        var root = new Grid { Background = Ui.BlueGradient };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var outer = new StackPanel { Margin = new Thickness(0, 30, 0, 30) };

        outer.Children.Add(new TextBlock
        {
            Text = "证券行业专业人员水平评价测试（模拟）",
            Foreground = Brushes.White,
            FontSize = 34,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2, Opacity = 0.45 },
        });
        outer.Children.Add(new TextBlock
        {
            Text = _paperName,
            Foreground = new SolidColorBrush(Color.FromRgb(0xBD, 0xD7, 0xEE)),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 26),
        });

        BuildLoginCard();
        BuildConfirmCard();
        _confirmCard.Visibility = Visibility.Collapsed;
        outer.Children.Add(_loginCard);
        outer.Children.Add(_confirmCard);

        var back = Ui.MakeButton("返回主页", () => _main.Navigate(new MainMenuPage(_main)));
        var bkWrap = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = back,
            Margin = new Thickness(0, 10, 0, 0),
        };
        outer.Children.Add(bkWrap);

        scroll.Content = outer;
        root.Children.Add(scroll);
        Content = root;
        Loaded += (_, _) => _tbNo.Focus();
    }

    private void BuildLoginCard()
    {
        var card = new Border
        {
            Width = 560,
            Background = new SolidColorBrush(Color.FromRgb(0xE9, 0xF1, 0xF9)),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.3 },
        };

        var ribbon = BuildRibbon();
        ribbon.Clip = new RectangleGeometry(new Rect(0, 0, 240, 240), 8, 8); // 贴合卡片 8px 圆角

        var grid = new Grid { Margin = new Thickness(46, 42, 46, 36) };

        var form = new StackPanel { Margin = new Thickness(0, 30, 0, 0) };
        string lastNo = "", lastName = "";
        try
        {
            string raw = Db.Scalar<string>("SELECT value FROM settings WHERE key='last_examinee'");
            if (!string.IsNullOrEmpty(raw))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                lastNo = doc.RootElement.TryGetProperty("no", out var n) ? n.GetString() ?? "" : "";
                lastName = doc.RootElement.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
            }
        }
        catch { }

        var (rowNo, tbNo) = MakeInputRow("准考证：", lastNo);
        var (rowName, tbName) = MakeInputRow("姓　名：", lastName);
        var (rowId, tbId) = MakeInputRow("证件号码：", "");
        _tbNo = tbNo; _tbName = tbName; _tbId = tbId;
        form.Children.Add(rowNo);
        form.Children.Add(rowName);
        form.Children.Add(rowId);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
        btns.Children.Add(Ui.MakeButton("登 录", DoLogin, "orange"));
        btns.Children.Add(new Border { Width = 26 });
        btns.Children.Add(Ui.MakeButton("取 消", () => _main.Navigate(new MainMenuPage(_main)), "primary"));
        form.Children.Add(btns);
        form.Children.Add(new TextBlock
        {
            Text = "说明：本页为模拟机考流程，准考证号与个人信息可任意填写（仅保存在本机）。",
            Foreground = Ui.TextSub, FontSize = 11.5, Margin = new Thickness(0, 20, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        grid.Children.Add(form);

        var root = new Grid();
        root.Children.Add(grid);
        root.Children.Add(ribbon);
        card.Child = root;
        _loginCard = card;
    }

    /// <summary>登录卡左上角 45° 斜色带（还原 img01）：多边形直接按 x+y=c1/c2 两条边界绘制，远离表单文字。</summary>
    private static FrameworkElement BuildRibbon()
    {
        const double c2 = 95;   // 覆盖左上角的等腰直角三角形（斜边 x+y=95，与表单文字区不相交）
        var polygon = new System.Windows.Shapes.Polygon
        {
            Points = new PointCollection
            {
                new Point(0, 0), new Point(c2, 0), new Point(0, c2),
            },
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xF7, 0xA2, 0x3B), 0),
                    new GradientStop(Color.FromRgb(0xEE, 0x7A, 0x23), 1),
                },
            },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var text = new TextBlock
        {
            Text = "LOGIN",
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            LayoutTransform = new RotateTransform(-45),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, 0),
        };
        var g = new Grid { IsHitTestVisible = false };
        g.Children.Add(polygon);
        g.Children.Add(text);
        return g;
    }

    private (StackPanel row, TextBox tb) MakeInputRow(string label, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 7) };
        row.Children.Add(new TextBlock
        {
            Text = label, Width = 96, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Ui.TextMain, FontSize = 14.5,
        });
        var tb = new TextBox
        {
            Text = text, Width = 270, MinHeight = 38, FontSize = 14.5,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(tb);
        return (row, tb);
    }

    private void BuildConfirmCard()
    {
        var card = new Border
        {
            Width = 640,
            Background = new SolidColorBrush(Color.FromRgb(0xE9, 0xF1, 0xF9)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(40, 30, 40, 30),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.3 },
        };
        var wrap = new StackPanel();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

        _confirmFields = new StackPanel();
        grid.Children.Add(_confirmFields);

        var photo = new StackPanel();
        photo.Children.Add(new Border
        {
            Width = 150, Height = 175,
            Background = Brushes.White,
            CornerRadius = new CornerRadius(2),
            Child = new TextBlock
            {
                Text = "👤\n\n模拟照片占位",
                FontSize = 24,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0xBC)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        });
        photo.Children.Add(new TextBlock
        {
            Text = "此照片是模拟流程拍摄",
            FontSize = 11.5,
            Foreground = Ui.TextSub,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        });
        Grid.SetColumn(photo, 1);
        grid.Children.Add(photo);
        wrap.Children.Add(grid);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
        btns.Children.Add(Ui.MakeButton("正 确", () =>
        {
            _loginCard.Visibility = Visibility.Collapsed;
            _confirmCard.Visibility = Visibility.Visible;
            _main.Navigate(new NoticePage(_main, _paperId, _examinee.no, _examinee.name, _randomIds));
        }, "orange"));
        btns.Children.Add(new Border { Width = 26 });
        btns.Children.Add(Ui.MakeButton("修 改", () =>
        {
            _confirmCard.Visibility = Visibility.Collapsed;
            _loginCard.Visibility = Visibility.Visible;
        }, "primary"));
        wrap.Children.Add(btns);
        card.Child = wrap;
        _confirmCard = card;
    }

    private void DoLogin()
    {
        string no = _tbNo.Text.Trim();
        string name = _tbName.Text.Trim();
        string id = _tbId.Text.Trim();
        if (no.Length == 0) { SacDialog.Info(Window.GetWindow(this), "提示", "请输入准考证号（模拟环境可任意填写）。"); return; }
        if (name.Length == 0) { SacDialog.Info(Window.GetWindow(this), "提示", "请输入姓名。"); return; }
        _examinee = (no, name, id);
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(new { no, name, id });
            Db.Exec("INSERT INTO settings(key,value) VALUES('last_examinee',@v) ON CONFLICT(key) DO UPDATE SET value=@v", ("@v", json));
        }
        catch { }

        _confirmFields.Children.Clear();
        AddField("姓　　名：", name);
        AddField("性　　别：", "—（模拟）");
        AddField("准 考 证：", no);
        AddField("证件号码：", id.Length > 0 ? MaskId(id) : "（未填写）");
        AddField("考试科目：", _subject.Name);
        AddField("试卷名称：", _paperName);
        AddField("考试时长：", $"{_duration} 分钟");

        _loginCard.Visibility = Visibility.Collapsed;
        _confirmCard.Visibility = Visibility.Visible;
    }

    private static string MaskId(string id) =>
        id.Length <= 8 ? new string('*', id.Length) : id.Substring(0, 4) + new string('*', id.Length - 8) + id.Substring(id.Length - 4);

    private void AddField(string label, string value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 112, TextAlignment = TextAlignment.Right, FontSize = 14.5, Foreground = Ui.TextMain, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = "  " + value, FontSize = 14.5, Foreground = Ui.TextMain, VerticalAlignment = VerticalAlignment.Center });
        _confirmFields.Children.Add(row);
    }
}

/// <summary>屏3：考场须知（还原 img03；滚动到底激活“我已阅读” → 3 秒倒计时进答题页）。</summary>
public class NoticePage : UserControl
{
    private readonly MainWindow _main;
    private readonly long _paperId;
    private readonly List<long> _randomIds;
    private readonly string _no, _name;
    private Button _goBtn;
    private Border _overlay;

    public NoticePage(MainWindow main, long paperId, string no, string name, List<long> randomIds = null)
    {
        _main = main;
        _paperId = paperId;
        _no = no;
        _name = name;
        _randomIds = randomIds;

        var root = new Grid { Background = Ui.BlueGradient };
        var outer = new Grid { Margin = new Thickness(60, 26, 60, 26) };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        outer.Children.Add(new TextBlock
        {
            Text = "证券行业专业人员水平评价测试（模拟）考场须知",
            Foreground = Brushes.White,
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 18),
        });

        var scroll = new ScrollViewer
        {
            Background = Brushes.White,
            Padding = new Thickness(34, 24, 24, 24),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        scroll.Content = new TextBlock
        {
            Text = SettingsService.Get().NoticeText,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14.5,
            LineHeight = 32,
            Foreground = Ui.TextMain,
        };
        scroll.ScrollChanged += (_, e) =>
        {
            bool bottom = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 8 || e.ExtentHeight <= e.ViewportHeight + 1;
            _goBtn.IsEnabled = bottom;
        };
        Grid.SetRow(scroll, 1);
        outer.Children.Add(scroll);

        _goBtn = Ui.MakeButton("我已阅读考场须知（请先滚动到底部）", StartExam, "orange");
        _goBtn.IsEnabled = false;
        _goBtn.Height = 46;
        _goBtn.FontSize = 15.5;
        _goBtn.MinWidth = 380;
        _goBtn.HorizontalAlignment = HorizontalAlignment.Center;
        var backBtn = Ui.MakeButton("返回", () => _main.Navigate(new MainMenuPage(_main)));
        var row = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        var backWrap = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Child = backBtn };
        row.Children.Add(backWrap);
        row.Children.Add(_goBtn);
        Grid.SetRow(row, 2);
        outer.Children.Add(row);

        root.Children.Add(outer);
        Content = root;
    }

    private void StartExam()
    {
        // 3 秒倒计时过场 → 答题页
        _overlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x10, 0x2A, 0x4C)),
            Child = new TextBlock
            {
                Text = "考试开始",
                Foreground = Brushes.White,
                FontSize = 46,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            IsHitTestVisible = true,
        };
        var root = (Grid)Content;
        root.Children.Add(_overlay);

        bool isRandom = _randomIds is { Count: > 0 };
        var session = ExamService.Start(_paperId, isRandom ? "random" : "paper", _name, _no, _randomIds);
        int n = 3;
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        t.Tick += (_, _) =>
        {
            n--;
            if (n <= 0)
            {
                t.Stop();
                _main.Navigate(new ExamPage(_main, session));
            }
        };
        t.Start();
    }
}
