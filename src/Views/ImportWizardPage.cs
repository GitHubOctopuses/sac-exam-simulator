using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SacExam.Services;

namespace SacExam;

/// <summary>屏9：整卷导入向导（三步：选择文件 → 解析预览与校验 → 确认导入，还原 ui_整卷导入.png）。</summary>
public class ImportWizardPage : UserControl
{
    private readonly MainWindow _main;

    private string _filePath;
    private string _imagesFolder;
    private ParseOutcome _outcome;
    private ImportReport _report;

    private readonly TextBox _fileBox = new() { IsReadOnly = true, FontSize = 13 };
    private readonly TextBox _imgDirBox = new() { FontSize = 13 };
    private readonly ComboBox _subjectBox = new() { Width = 230, Height = 34 };
    private readonly TextBox _nameBox = new() { FontSize = 13.5 };

    private readonly StackPanel _stepBody = new();
    private readonly List<(string, Border)> _stepChips = new();

    public ImportWizardPage(MainWindow main)
    {
        _main = main;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.TopBar("题库管理 · 整卷导入", BackButton()));

        // 步骤条
        var stepBar = new Border { Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 14, 0, 14) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        AddStep(row, "① 选择文件", true);
        row.Children.Add(Arrow());
        AddStep(row, "② 解析预览与校验", false);
        row.Children.Add(Arrow());
        AddStep(row, "③ 确认导入", false);
        stepBar.Child = row;
        Grid.SetRow(stepBar, 1);
        root.Children.Add(stepBar);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _stepBody.Margin = new Thickness(0, 20, 0, 24);
        var wrap = new StackPanel();
        _stepBody.Width = 880;
        _stepBody.HorizontalAlignment = HorizontalAlignment.Center;
        wrap.Children.Add(_stepBody);
        scroll.Content = wrap;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        Content = root;
        ShowStep1();
    }

    private Button BackButton() => Ui.MakeButton("返回题库管理", () => _main.Navigate(new BankManagePage(_main)));

    private static FrameworkElement Arrow() => new TextBlock
    {
        Text = "————→",
        Foreground = Ui.TextSub,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(14, 0, 14, 0),
        FontSize = 13,
    };

    private void AddStep(StackPanel parent, string text, bool active)
    {
        var b = new Border
        {
            Background = active ? Ui.AuxBlue : Brushes.White,
            BorderBrush = active ? Ui.AuxBlue : new SolidColorBrush(Color.FromRgb(0xC4, 0xCF, 0xE2)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(26, 9, 26, 9),
            Child = new TextBlock
            {
                Text = text,
                Foreground = active ? Brushes.White : Ui.TextSub,
                FontWeight = active ? FontWeights.Bold : FontWeights.Regular,
            },
        };
        _stepChips.Add((text, b));
        parent.Children.Add(b);
    }

    private void HighlightStep(int idx)
    {
        for (int i = 0; i < _stepChips.Count; i++)
        {
            var (_, b) = _stepChips[i];
            bool active = i == idx;
            b.Background = active ? Ui.AuxBlue : Brushes.White;
            b.BorderBrush = active ? Ui.AuxBlue : new SolidColorBrush(Color.FromRgb(0xC4, 0xCF, 0xE2));
            ((TextBlock)b.Child).Foreground = active ? Brushes.White : Ui.TextSub;
            ((TextBlock)b.Child).FontWeight = active ? FontWeights.Bold : FontWeights.Regular;
        }
    }

    // ===================== 第 1 步：选择文件 =====================

    private void ShowStep1()
    {
        HighlightStep(0);
        _outcome = null;
        _stepBody.Children.Clear();

        var card = new Border { Style = (Style)FindResource("Card") };
        var sp = new StackPanel();
        card.Child = sp;
        _stepBody.Children.Add(card);

        sp.Children.Add(new TextBlock { Text = "已选择文件", Foreground = Ui.TextSub, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
        var fileRow = new Grid();
        fileRow.ColumnDefinitions.Add(new ColumnDefinition());
        fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _fileBox.Text = _filePath ?? "";
        Grid.SetColumn(_fileBox, 0);
        fileRow.Children.Add(_fileBox);
        var browse = Ui.MakeButton("浏览…", () =>
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择整卷文件",
                Filter = "整卷文件 (xlsx/csv/json)|*.xlsx;*.xlsm;*.xls;*.csv;*.json|Excel 模板|*.xlsx;*.xlsm;*.xls|CSV|*.csv|JSON|*.json",
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true)
            {
                _filePath = dlg.FileName;
                _fileBox.Text = _filePath;
                _nameBox.Text = Path.GetFileNameWithoutExtension(_filePath);
            }
        }, "primary");
        browse.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(browse, 1);
        fileRow.Children.Add(browse);
        sp.Children.Add(fileRow);

        sp.Children.Add(new TextBlock { Text = "图片文件夹（可选；留空则自动在文件同目录及 images/ 子目录查找）", Foreground = Ui.TextSub, FontSize = 12, Margin = new Thickness(0, 16, 0, 6) });
        var imgRow = new Grid();
        imgRow.ColumnDefinitions.Add(new ColumnDefinition());
        imgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _imgDirBox.Text = _imagesFolder ?? "";
        Grid.SetColumn(_imgDirBox, 0);
        imgRow.Children.Add(_imgDirBox);
        var browseImg = Ui.MakeButton("浏览…", () =>
        {
            var dlg = new OpenFolderDialog { Title = "选择图片文件夹" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) _imgDirBox.Text = dlg.FolderName;
        });
        browseImg.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(browseImg, 1);
        imgRow.Children.Add(browseImg);
        sp.Children.Add(imgRow);

        sp.Children.Add(new TextBlock { Text = "所属科目（题库分类）", Foreground = Ui.TextSub, FontSize = 12, Margin = new Thickness(0, 16, 0, 6) });
        foreach (var s in Db.GetSubjects()) _subjectBox.Items.Add(s.Name);
        if (_subjectBox.Items.Count > 0 && _subjectBox.SelectedIndex < 0) _subjectBox.SelectedIndex = 0;
        sp.Children.Add(_subjectBox);

        sp.Children.Add(new TextBlock { Text = "试卷名称", Foreground = Ui.TextSub, FontSize = 12, Margin = new Thickness(0, 16, 0, 6) });
        _nameBox.Text = _filePath != null ? Path.GetFileNameWithoutExtension(_filePath) : "";
        sp.Children.Add(_nameBox);

        sp.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 18, 0, 14) });
        sp.Children.Add(new TextBlock
        {
            Text = "导入规则：整卷为单位导入（一文件一卷）· 不查重（相似题可共存）· 不强制章节 · 判断题答案兼容 A/B 与「正确/错误/对/错/√/×」· 以【案例材料】开头的题自动识别为综合题（不定项）· 支持 {{img:文件名}} 内联图片占位",
            Foreground = Ui.TextSub,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
        });

        var go = Ui.MakeButton("下一步：解析与校验", () =>
        {
            if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
            {
                SacDialog.Info(Window.GetWindow(this), "提示", "请先选择整卷文件（xlsx / csv / json）。");
                return;
            }
            if (_subjectBox.SelectedIndex < 0)
            {
                SacDialog.Info(Window.GetWindow(this), "提示", "请选择所属科目。");
                return;
            }
            _imagesFolder = string.IsNullOrWhiteSpace(_imgDirBox.Text) ? null : _imgDirBox.Text.Trim();
            try
            {
                _outcome = ImportService.ParseFile(_filePath, _imagesFolder);
                if (string.IsNullOrWhiteSpace(_nameBox.Text))
                    _nameBox.Text = _outcome.PaperName ?? Path.GetFileNameWithoutExtension(_filePath);
                ShowStep2();
            }
            catch (Exception ex)
            {
                SacDialog.Info(Window.GetWindow(this), "解析失败", ex.Message);
            }
        }, "primary");
        go.HorizontalAlignment = HorizontalAlignment.Right;
        go.Margin = new Thickness(0, 20, 0, 0);
        sp.Children.Add(go);
    }

    // ===================== 第 2 步：解析预览与校验 =====================

    private sealed class RowVm
    {
        public string Type { get; set; }
        public string Stem { get; set; }
        public string Answer { get; set; }
        public string Score { get; set; }
        public string Recognition { get; set; }
        public bool Ok { get; set; }
    }

    private void ShowStep2()
    {
        HighlightStep(1);
        _stepBody.Children.Clear();

        var card = new Border { Style = (Style)FindResource("Card") };
        var sp = new StackPanel();
        card.Child = sp;
        _stepBody.Children.Add(card);

        sp.Children.Add(new TextBlock { Text = "已选择文件", Foreground = Ui.TextSub, FontSize = 12 });
        sp.Children.Add(new TextBlock { Text = Path.GetFileName(_filePath), FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 10) });

        string summary = $"解析结果：共 {_outcome.Total} 题 · 单选 {_outcome.CountByType.GetValueOrDefault("单选题")} · 多选 {_outcome.CountByType.GetValueOrDefault("多选题")} · 判断 {_outcome.CountByType.GetValueOrDefault("判断题")} · 综合（不定项）{_outcome.CountByType.GetValueOrDefault("综合题")} · 卷面总分 {Ui.FmtScore(_outcome.TotalScore)} 分";
        sp.Children.Add(new TextBlock { Text = summary, FontSize = 13.5, Foreground = Ui.TextMain, Margin = new Thickness(0, 0, 0, 10) });

        var bannerText = _outcome.ErrorCount == 0
            ? "✔ 校验通过：无缺选项、无非法答案、综合题已按【案例材料】自动识别"
            : $"✘ 校验发现 {_outcome.ErrorCount} 行错误：可「仅导入无错误行」或返回修改文件";
        sp.Children.Add(new Border
        {
            Background = _outcome.ErrorCount == 0 ? new SolidColorBrush(Color.FromRgb(0xE7, 0xF7, 0xEE)) : new SolidColorBrush(Color.FromRgb(0xFD, 0xEC, 0xEC)),
            BorderBrush = _outcome.ErrorCount == 0 ? Ui.Green : Ui.Red,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 14),
            Child = new TextBlock
            {
                Text = bannerText,
                Foreground = _outcome.ErrorCount == 0 ? Ui.Green : Ui.Red,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
            },
        });

        // 预览表（错误行置顶红显）
        var grid = new DataGrid { MaxHeight = 330, FontSize = 12.5 };
        var c1 = new DataGridTextColumn { Header = "题型", Binding = new System.Windows.Data.Binding("Type"), Width = new DataGridLength(90) };
        var c2 = new DataGridTextColumn { Header = "题目（预览）/ 错误原因", Binding = new System.Windows.Data.Binding("Stem"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
        var c3 = new DataGridTextColumn { Header = "正确答案", Binding = new System.Windows.Data.Binding("Answer"), Width = new DataGridLength(90) };
        var c4 = new DataGridTextColumn { Header = "分值", Binding = new System.Windows.Data.Binding("Score"), Width = new DataGridLength(60) };
        var c5 = new DataGridTextColumn { Header = "识别 / 校验", Binding = new System.Windows.Data.Binding("Recognition"), Width = new DataGridLength(150) };
        grid.Columns.Add(c1); grid.Columns.Add(c2); grid.Columns.Add(c3); grid.Columns.Add(c4); grid.Columns.Add(c5);

        var vms = new List<RowVm>();
        foreach (var bad in _outcome.Rows.Where(r => !r.Ok))
            vms.Add(new RowVm { Type = "错误行", Stem = bad.Reason, Answer = "-", Score = "-", Recognition = $"第 {bad.RowIndex} 行", Ok = false });
        foreach (var ok in _outcome.Rows.Where(r => r.Ok).Take(400))
        {
            string recog = ok.Question.Warnings.Count > 0
                ? ok.Recognition + "（" + string.Join("；", ok.Question.Warnings.Take(2)) + "）"
                : ok.Recognition;
            vms.Add(new RowVm
            {
                Type = ok.Question.Type,
                Stem = (ok.Question.CaseMaterial != null ? "【案例材料】" : "") + ok.Question.Stem,
                Answer = ok.Question.Answer,
                Score = Ui.FmtScore(ok.Question.Score ?? 0),
                Recognition = recog,
                Ok = true,
            });
        }
        var fgStyle = new Style(typeof(TextBlock));
        fgStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,
            new System.Windows.Data.Binding("Ok") { Converter = new BoolToBrushConverter("#222B3A", "#E34040") }));
        fgStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        c2.ElementStyle = fgStyle;
        grid.ItemsSource = vms;
        sp.Children.Add(grid);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
        btns.Children.Add(Ui.MakeButton("重新选择文件", ShowStep1));
        btns.Children.Add(new Border { Width = 14 });
        if (_outcome.ErrorCount > 0)
        {
            btns.Children.Add(Ui.MakeButton("导出错误报告(CSV)", () =>
            {
                var dlg = new SaveFileDialog { FileName = "导入错误报告.csv", Filter = "CSV|*.csv" };
                if (dlg.ShowDialog(Window.GetWindow(this)) == true)
                {
                    ImportService.ExportErrorReport(_outcome.Rows.Where(r => !r.Ok), dlg.FileName);
                    SacDialog.Info(Window.GetWindow(this), "已导出", dlg.FileName);
                }
            }));
            btns.Children.Add(new Border { Width = 14 });
        }
        btns.Children.Add(Ui.MakeButton(_outcome.ErrorCount > 0 ? "仅导入无错误行" : "确认导入", Commit, "primary"));
        sp.Children.Add(btns);
    }

    // ===================== 第 3 步：提交 =====================

    private void Commit()
    {
        try
        {
            var report = ImportService.Commit(new ImportService.CommitOptions
            {
                FilePath = _filePath,
                ImagesFolder = _imagesFolder,
                PaperName = string.IsNullOrWhiteSpace(_nameBox.Text) ? Path.GetFileNameWithoutExtension(_filePath) : _nameBox.Text.Trim(),
                SubjectId = Db.GetSubjects()[_subjectBox.SelectedIndex].Id,
                DurationMinutes = _outcome.DurationMinutes,
                Rows = _outcome.Rows,
            });
            _report = report;
            ShowStep3();
        }
        catch (Exception ex)
        {
            SacDialog.Info(Window.GetWindow(this), "导入失败", ex.Message);
        }
    }

    private void ShowStep3()
    {
        HighlightStep(2);
        _stepBody.Children.Clear();

        var card = new Border { Style = (Style)FindResource("Card") };
        var sp = new StackPanel();
        card.Child = sp;
        _stepBody.Children.Add(card);

        sp.Children.Add(new TextBlock
        {
            Text = _report.ErrorRows == 0 ? "✔ 导入完成" : "⚠ 导入完成（部分行失败）",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = _report.ErrorRows == 0 ? Ui.Green : Ui.Orange,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"成功 {_report.OkRows} 行 · 失败 {_report.ErrorRows} 行 · 新试卷「{_nameBox.Text.Trim()}」已加入题库",
            FontSize = 14,
            Foreground = Ui.TextMain,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        });

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
        if (_report.ErrorRows > 0)
        {
            btns.Children.Add(Ui.MakeButton("导出错误报告(CSV)", () =>
            {
                var dlg = new SaveFileDialog { FileName = "导入错误报告.csv", Filter = "CSV|*.csv" };
                if (dlg.ShowDialog(Window.GetWindow(this)) == true)
                {
                    ImportService.ExportErrorReport(_report.ErrorRowsDetail, dlg.FileName);
                    SacDialog.Info(Window.GetWindow(this), "已导出", dlg.FileName);
                }
            }));
            btns.Children.Add(new Border { Width = 14 });
        }
        btns.Children.Add(Ui.MakeButton("立即用该卷开考", () =>
        {
            _main.Navigate(new ExamLoginPage(_main, _report.PaperId));
        }, "orange"));
        btns.Children.Add(new Border { Width = 14 });
        btns.Children.Add(Ui.MakeButton("返回题库管理", () => _main.Navigate(new BankManagePage(_main)), "primary"));
        sp.Children.Add(btns);
    }
}

/// <summary>布尔 → 前景画笔（预览表错误行红字）。</summary>
public class BoolToBrushConverter : System.Windows.Data.IValueConverter
{
    private readonly Brush _ok;
    private readonly Brush _bad;

    public BoolToBrushConverter(string okColor = "#222B3A", string badColor = "#E34040")
    {
        _ok = new SolidColorBrush((Color)ColorConverter.ConvertFromString(okColor));
        _bad = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badColor));
    }

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is true ? _ok : _bad;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}
