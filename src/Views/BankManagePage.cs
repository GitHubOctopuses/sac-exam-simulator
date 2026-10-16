using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using SacExam.Services;

namespace SacExam;

/// <summary>屏9：题库管理后台（整卷列表 + 导入向导入口 + 导入历史 + 数据备份）。</summary>
public class BankManagePage : UserControl
{
    private readonly MainWindow _main;
    private readonly StackPanel _paperList = new();
    private readonly StackPanel _historyList = new();

    public BankManagePage(MainWindow main)
    {
        _main = main;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.TopBar("题库管理", BackButton()));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new StackPanel { Margin = new Thickness(26, 18, 26, 20) };

        // 工具条
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        tools.Children.Add(Ui.MakeButton("整卷导入", () => _main.Navigate(new ImportWizardPage(_main)), "primary"));
        tools.Children.Add(new Border { Width = 10 });
        tools.Children.Add(Ui.MakeButton("备份题库（zip）", BackupZip));
        tools.Children.Add(new Border { Width = 10 });
        tools.Children.Add(Ui.MakeButton("从备份恢复", RestoreZip));
        tools.Children.Add(new Border { Width = 10 });
        tools.Children.Add(Ui.MakeButton("刷新", Rebuild));
        tools.Children.Add(new Border { Width = 16 });
        tools.Children.Add(new TextBlock
        {
            Text = $"数据目录：{App.DataDir}",
            Foreground = Ui.TextSub,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        body.Children.Add(tools);

        body.Children.Add(SectionTitle($"整卷列表（导入的试卷，可原样开考）"));
        body.Children.Add(_paperList);
        body.Children.Add(new Border { Height = 10 });
        body.Children.Add(SectionTitle("导入历史"));
        body.Children.Add(_historyList);

        scroll.Content = body;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
        Rebuild();
    }

    private Button BackButton() => Ui.MakeButton("返回主页", () => _main.Navigate(new MainMenuPage(_main)));

    private FrameworkElement SectionTitle(string text) => new TextBlock
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.Bold,
        Foreground = Ui.TextMain,
        Margin = new Thickness(0, 8, 0, 10),
    };

    private void Rebuild()
    {
        _paperList.Children.Clear();
        _historyList.Children.Clear();

        var papers = Db.GetPapers(null, includeRandom: false);
        if (papers.Count == 0)
        {
            _paperList.Children.Add(EmptyHint("还没有导入整卷。点击上方「整卷导入」，支持 xlsx / csv / json 三种格式（与《整卷导入模板.xlsx》同构）。"));
        }
        foreach (var p in papers) _paperList.Children.Add(PaperRow(p));

        var batches = Db.Rows("SELECT * FROM import_batches ORDER BY id DESC LIMIT 30");
        if (batches.Count == 0)
            _historyList.Children.Add(EmptyHint("暂无导入记录。"));
        foreach (var b in batches)
        {
            var row = new Border
            {
                Background = Brushes.White,
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 0, 8),
            };
            row.Child = new TextBlock
            {
                Text = $"[{b["imported_at"]}] {b["file_name"]} → {b["paper_name"]}　成功 {(long)b["ok_rows"]} / {(long)b["total_rows"]} 行，失败 {(long)b["error_rows"]} 行",
                FontSize = 12.5,
                Foreground = (long)b["error_rows"] > 0 ? Ui.Red : Ui.TextMain,
            };
            _historyList.Children.Add(row);
        }
    }

    private static FrameworkElement EmptyHint(string text) => new Border
    {
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFD)),
        BorderBrush = Ui.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(18, 14, 18, 14),
        Margin = new Thickness(0, 0, 0, 10),
        Child = new TextBlock { Text = text, Foreground = Ui.TextSub, TextWrapping = TextWrapping.Wrap, LineHeight = 24 },
    };

    private FrameworkElement PaperRow(PaperDto p)
    {
        var g = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.Bold, FontSize = 14.5, Foreground = Ui.TextMain, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock
        {
            Text = $" {p.SubjectName} ",
            FontSize = 11,
            Foreground = Ui.AuxBlue,
            Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF)),
            Padding = new Thickness(6, 2, 6, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        });
        left.Children.Add(head);
        left.Children.Add(new TextBlock
        {
            Text = $"{p.QuestionCount} 题 · 总分 {Ui.FmtScore(p.TotalScore ?? 0)} 分 · 时长 {(p.DurationMinutes ?? Db.GetSubject(p.SubjectId).DurationMinutes)} 分钟 · 导入于 {p.CreatedAt}",
            Foreground = Ui.TextSub,
            FontSize = 12,
            Margin = new Thickness(0, 5, 0, 0),
        });
        Grid.SetColumn(left, 0);
        g.Children.Add(left);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        btns.Children.Add(Ui.MakeButton("开考", () => _main.Navigate(new ExamLoginPage(_main, p.Id)), "primary"));
        btns.Children.Add(new Border { Width = 8 });
        btns.Children.Add(Ui.MakeButton("题目编辑", () => new QuestionEditorWindow(p).ShowDialog()));
        btns.Children.Add(new Border { Width = 8 });
        btns.Children.Add(Ui.MakeButton("导出", () =>
        {
            var dlg = new OpenFolderDialog { Title = "选择导出位置" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true)
            {
                string dir = ExportService.ExportPaper(p.Id, dlg.FolderName);
                SacDialog.Info(Window.GetWindow(this), "导出完成", $"已导出到：\n{dir}\n\n包含模板 xlsx 与 images 图片文件夹，可直接再次整卷导入。");
            }
        }));
        btns.Children.Add(new Border { Width = 8 });
        btns.Children.Add(Ui.MakeButton("重命名", () =>
        {
            var dlg = new Window
            {
                Title = "重命名试卷",
                Width = 380,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
            };
            var sp = new StackPanel { Margin = new Thickness(14) };
            var tb = new TextBox { Text = p.Name, FontSize = 14 };
            sp.Children.Add(tb);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = Ui.MakeButton("确定", () => { Db.Exec("UPDATE papers SET name=@n WHERE id=@p", ("@n", tb.Text.Trim()), ("@p", p.Id)); dlg.Close(); Rebuild(); }, "primary");
            row.Children.Add(ok);
            row.Children.Add(new Border { Width = 8 });
            row.Children.Add(Ui.MakeButton("取消", () => dlg.Close()));
            sp.Children.Add(row);
            dlg.Content = sp;
            dlg.ShowDialog();
        }));
        btns.Children.Add(new Border { Width = 8 });
        btns.Children.Add(Ui.MakeButton("删除", () =>
        {
            if (SacDialog.Confirm(Window.GetWindow(this), "删除整卷",
                    $"将删除试卷「{p.Name}」及其 {p.QuestionCount} 道题，连同该卷的历史考试记录与错题关联。\n此操作不可恢复。确定删除？", dangerOk: true))
            {
                Db.DeletePaper(p.Id);
                Rebuild();
            }
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

    private void BackupZip()
    {
        var dlg = new SaveFileDialog
        {
            Title = "备份题库（数据库 + 图片）",
            FileName = $"题库备份_{DateTime.Now:yyyyMMdd_HHmm}.zip",
            Filter = "ZIP 备份|*.zip",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            ExportService.ExportZip(dlg.FileName);
            SacDialog.Info(Window.GetWindow(this), "备份完成", $"已备份到：\n{dlg.FileName}");
        }
    }

    private void RestoreZip()
    {
        if (!SacDialog.Confirm(Window.GetWindow(this), "从备份恢复",
                "恢复将覆盖当前全部数据（题库、图片、考试记录）。确定继续？", dangerOk: true))
            return;
        var dlg = new OpenFileDialog { Title = "选择备份 zip", Filter = "ZIP 备份|*.zip" };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            try
            {
                ExportService.ImportZip(dlg.FileName);
                SacDialog.Info(Window.GetWindow(this), "恢复完成", "数据已恢复，请重启程序以重新加载数据库。");
            }
            catch (Exception ex)
            {
                SacDialog.Info(Window.GetWindow(this), "恢复失败", ex.Message);
            }
        }
    }
}
