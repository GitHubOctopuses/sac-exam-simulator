using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SacExam.Services;

namespace SacExam;

/// <summary>题目编辑器（§5.8）：增删改题目、选项、解析；支持 Ctrl+V 粘贴剪贴板图片与本地上传，自动入库建引用。</summary>
public class QuestionEditorWindow : Window
{
    private readonly PaperDto _paper;
    private List<QuestionDto> _questions = new();
    private QuestionDto _current;

    private readonly TextBox _search = new() { Width = 220, FontSize = 13 };
    private readonly ComboBox _typeFilter = new() { Width = 120, Height = 32 };
    private readonly ListBox _list = new() { MinWidth = 330, BorderBrush = Ui.Line };
    private readonly StackPanel _editor = new();

    // 编辑控件
    private ComboBox _edType;
    private TextBox _edScore, _edStem, _edCase, _edAnalysis;
    private List<TextBox> _edOptions = new();
    private TextBox _edAnswer;
    private StackPanel _stemImgBar, _analysisImgBar;
    private List<StackPanel> _optionImgBars = new();
    private ComboBox _pasteTarget;

    public QuestionEditorWindow(PaperDto paper)
    {
        _paper = paper;
        Title = $"题目编辑 · {paper.Name}";
        Icon = Ui.AppIcon;
        Width = 1180;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current.MainWindow;
        Background = Ui.PageBg;

        var root = new Grid { Margin = new Thickness(14) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(370) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ---- 左：筛选 + 列表 ----
        var left = new DockPanel { LastChildFill = true };
        var top = new StackPanel();
        var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _typeFilter.Items.Add("全部题型");
        foreach (var t in new[] { "单选题", "多选题", "判断题", "综合题" }) _typeFilter.Items.Add(t);
        _typeFilter.SelectedIndex = 0;
        _typeFilter.SelectionChanged += (_, _) => ReloadList();
        _search.TextChanged += (_, _) => ReloadList();
        filterRow.Children.Add(_typeFilter);
        filterRow.Children.Add(_search);
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        btnRow.Children.Add(Ui.MakeButton("＋ 新增题目", AddQuestion, "primary"));
        btnRow.Children.Add(new Border { Width = 8 });
        btnRow.Children.Add(Ui.MakeButton("删除当前题", DeleteQuestion, "danger"));
        top.Children.Add(filterRow);
        top.Children.Add(btnRow);
        DockPanel.SetDock(top, Dock.Top);
        left.Children.Add(top);

        _list.SelectionChanged += (_, _) => { _current = _list.SelectedItem as QuestionDto; RenderEditor(); };
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) PasteImage();
        };
        left.Children.Add(_list);

        var leftBorder = new Border { Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10), Child = left };
        Grid.SetColumn(leftBorder, 0);
        root.Children.Add(leftBorder);

        // ---- 右：编辑区 ----
        var rightScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _editor.Margin = new Thickness(4);
        rightScroll.Content = _editor;
        var rightBorder = new Border { Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = rightScroll };
        Grid.SetColumn(rightBorder, 2);
        root.Children.Add(rightBorder);

        Content = root;

        _questions = BankService.GetQuestions(paper.Id);
        ReloadList();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) PasteImage();
        };
    }

    private void ReloadList()
    {
        string kw = _search.Text.Trim();
        string type = _typeFilter.SelectedItem as string;
        var filtered = _questions
            .Where(q => (type == "全部题型" || type == null || q.QType == type) &&
                        (kw.Length == 0 || q.Stem.Contains(kw)))
            .ToList();
        _list.ItemsSource = filtered;
        _list.DisplayMemberPath = "Stem";
        _list.SelectedIndex = _current == null ? (filtered.Count > 0 ? 0 : -1) : filtered.FindIndex(q => q.Id == _current.Id);
    }

    // ---------- 编辑器 ----------

    private void RenderEditor()
    {
        _editor.Children.Clear();
        _edOptions = new List<TextBox>();
        _optionImgBars = new List<StackPanel>();
        if (_current == null)
        {
            _editor.Children.Add(new TextBlock
            {
                Text = "← 选择左侧题目进行编辑，或新增题目。\n\n粘贴图片：复制截图后按 Ctrl+V，图片将按右侧选择的粘贴目标自动入库并建立引用。",
                Foreground = Ui.TextSub,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 26,
                Margin = new Thickness(6, 20, 0, 0),
            });
            return;
        }
        var q = _current;

        // 题型 + 分值 + 章节
        var meta = new Grid { Margin = new Thickness(6, 6, 6, 10) };
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _edType = new ComboBox { Height = 34 };
        foreach (var t in new[] { "单选题", "多选题", "判断题", "综合题" }) _edType.Items.Add(t);
        _edType.SelectedItem = q.QType;
        Grid.SetColumn(_edType, 1);
        meta.Children.Add(_edType);
        meta.Children.Add(Label("分值", 3));
        _edScore = new TextBox { Text = Ui.FmtScore(q.Score) };
        Grid.SetColumn(_edScore, 4);
        meta.Children.Add(_edScore);
        _editor.Children.Add(meta);

        // 案例材料（综合题）
        _editor.Children.Add(Label("案例材料（综合题；可空）"));
        _edCase = new TextBox { Text = q.CaseMaterial ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, FontSize = 13.5, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(6, 4, 6, 8) };
        _editor.Children.Add(_edCase);

        // 题干
        _editor.Children.Add(Label("题干（支持 {{img:文件名}} 内联占位）"));
        _edStem = new TextBox { Text = q.Stem, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110, FontSize = 13.5, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(6, 4, 6, 4) };
        _editor.Children.Add(_edStem);
        _stemImgBar = new StackPanel { Margin = new Thickness(6, 0, 6, 10) };
        _editor.Children.Add(_stemImgBar);

        // 选项
        _editor.Children.Add(Label("选项（A、B、…）"));
        foreach (var opt in q.Options)
        {
            var row = new Grid { Margin = new Thickness(6, 2, 6, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = opt.Label + ":", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            var tb = new TextBox { Text = opt.Content, FontSize = 13.5, VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tb, 1);
            row.Children.Add(tb);
            _editor.Children.Add(row);
            _edOptions.Add(tb);
            var imgBar = new StackPanel { Margin = new Thickness(36, 0, 6, 6) };
            _editor.Children.Add(imgBar);
            _optionImgBars.Add(imgBar);
        }
        var addOptRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 8) };
        addOptRow.Children.Add(Ui.MakeButton("＋ 加一个选项", () =>
        {
            q.Options.Add(new OptionDto { Label = ((char)('A' + q.Options.Count)).ToString(), Content = "（新选项）" });
            SaveChanges(keepSelection: true);
        }, "ghost"));
        _editor.Children.Add(addOptRow);

        // 答案
        var ansRow = new Grid { Margin = new Thickness(6, 4, 6, 8) };
        ansRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ansRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        ansRow.ColumnDefinitions.Add(new ColumnDefinition());
        ansRow.Children.Add(Label("正确答案"));
        _edAnswer = new TextBox { Text = q.Answer, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_edAnswer, 1);
        ansRow.Children.Add(_edAnswer);
        ansRow.Children.Add(new TextBlock
        {
            Text = q.QType == "判断题" ? "（A=正确 / B=错误）" : q.QType == "单选题" ? "（单个字母，如 C）" : "（字母串，如 ABD；综合题可 1-4 个）",
            Foreground = Ui.TextSub,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        });
        _editor.Children.Add(ansRow);

        // 解析
        _editor.Children.Add(Label("答案解析"));
        _edAnalysis = new TextBox { Text = q.Analysis ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110, FontSize = 13.5, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(6, 4, 6, 4) };
        _editor.Children.Add(_edAnalysis);
        _analysisImgBar = new StackPanel { Margin = new Thickness(6, 0, 6, 10) };
        _editor.Children.Add(_analysisImgBar);

        // 粘贴目标 + 图片操作
        var pasteRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 10) };
        pasteRow.Children.Add(Label("粘贴目标"));
        _pasteTarget = new ComboBox { Width = 190, Height = 34 };
        _pasteTarget.Items.Add("题干图片");
        foreach (var opt in q.Options) _pasteTarget.Items.Add("选项 " + opt.Label);
        _pasteTarget.Items.Add("解析图片");
        _pasteTarget.SelectedIndex = 0;
        pasteRow.Children.Add(_pasteTarget);
        pasteRow.Children.Add(new Border { Width = 10 });
        pasteRow.Children.Add(Ui.MakeButton("📋 粘贴截图 (Ctrl+V)", PasteImage, "primary"));
        pasteRow.Children.Add(new Border { Width = 10 });
        pasteRow.Children.Add(Ui.MakeButton("从文件选择…", () => UploadImage(), "ghost"));
        _editor.Children.Add(pasteRow);

        RenderImageBars();

        // 保存
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 10, 6, 6) };
        saveRow.Children.Add(Ui.MakeButton("保存本题修改", () => SaveChanges(false), "primary"));
        saveRow.Children.Add(new Border { Width = 10 });
        saveRow.Children.Add(new TextBlock
        {
            Text = "保存时重建选项与本题图片引用；图片以 SHA-256 入库（同内容自动合并）。",
            Foreground = Ui.TextSub,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _editor.Children.Add(saveRow);
    }

    private static TextBlock Label(string text, int col = -1)
    {
        var tb = new TextBlock { Text = text, Foreground = Ui.TextSub, FontSize = 12.5, Margin = new Thickness(6, 8, 0, 2) };
        if (col >= 0) { Grid.SetColumn(tb, col); tb.VerticalAlignment = VerticalAlignment.Center; }
        return tb;
    }

    private void RenderImageBars()
    {
        var q = _current;
        if (q == null) return;
        FillBar(_stemImgBar, "题干图片", q.StemImages);
        FillBar(_analysisImgBar, "解析图片", q.AnalysisImages);
        for (int i = 0; i < _optionImgBars.Count && i < q.Options.Count; i++)
            FillBar(_optionImgBars[i], $"选项 {q.Options[i].Label} 图片", q.Options[i].ImageFiles);
    }

    private void FillBar(StackPanel bar, string title, List<string> files)
    {
        bar.Children.Clear();
        if (files.Count == 0) return;
        bar.Children.Add(new TextBlock { Text = title, Foreground = Ui.TextSub, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 4) });
        var wrap = new WrapPanel();
        foreach (var f in files.ToList())
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 6) };
            var img = Ui.MakeZoomableImage(f, 120, 70);
            cell.Children.Add(img);
            var del = new Button { Content = "✕", Width = 22, Height = 22, Margin = new Thickness(4, 0, 0, 0), FontSize = 10 };
            del.SetResourceReference(StyleProperty, "BtnDanger");
            del.Click += (_, _) => { files.Remove(f); RenderImageBars(); };
            cell.Children.Add(del);
            wrap.Children.Add(cell);
        }
        bar.Children.Add(wrap);
    }

    // ---------- 图片 ----------

    private void PasteImage()
    {
        if (_current == null) { SacDialog.Info(this, "提示", "请先选择题目。"); return; }
        if (!Clipboard.ContainsImage())
        {
            SacDialog.Info(this, "提示", "剪贴板中没有图片。请先截图（Win+Shift+S）再按 Ctrl+V。");
            return;
        }
        var src = Clipboard.GetImage();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        try
        {
            var st = ImageStore.Store(ms.ToArray(), ".png");
            var target = _pasteTarget?.SelectedItem as string ?? "题干图片";
            if (target.StartsWith("选项 ", StringComparison.Ordinal))
                _current.Options.First(o => o.Label == target[^1].ToString()).ImageFiles.Add(st.FullPath);
            else if (target == "解析图片") _current.AnalysisImages.Add(st.FullPath);
            else _current.StemImages.Add(st.FullPath);
            RenderImageBars();
        }
        catch (Exception ex)
        {
            SacDialog.Info(this, "图片入库失败", ex.Message);
        }
    }

    private void UploadImage()
    {
        if (_current == null) { SacDialog.Info(this, "提示", "请先选择题目。"); return; }
        var dlg = new OpenFileDialog { Title = "选择图片", Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var st = ImageStore.StoreFile(dlg.FileName);
            var target = _pasteTarget?.SelectedItem as string ?? "题干图片";
            if (target.StartsWith("选项 ", StringComparison.Ordinal))
                _current.Options.First(o => o.Label == target[^1].ToString()).ImageFiles.Add(st.FullPath);
            else if (target == "解析图片") _current.AnalysisImages.Add(st.FullPath);
            else _current.StemImages.Add(st.FullPath);
            RenderImageBars();
        }
        catch (Exception ex)
        {
            SacDialog.Info(this, "图片入库失败", ex.Message);
        }
    }

    // ---------- 保存 / 增删 ----------

    private void SaveChanges(bool keepSelection)
    {
        var q = _current;
        if (q == null) return;
        try
        {
            q.QType = (string)_edType.SelectedItem;
            q.Score = double.TryParse(_edScore.Text.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var sc) && sc > 0 ? sc : (q.QType == "单选题" ? 0.5 : 1);
            q.Stem = _edStem.Text.Trim();
            q.CaseMaterial = string.IsNullOrWhiteSpace(_edCase.Text) ? null : _edCase.Text.Trim();
            q.Analysis = string.IsNullOrWhiteSpace(_edAnalysis.Text) ? null : _edAnalysis.Text.Trim();
            q.Answer = (q.QType == "判断题" && (_edAnswer.Text.Trim() == "正确" || _edAnswer.Text.Trim() == "A")) ? "A"
                     : (q.QType == "判断题" && (_edAnswer.Text.Trim() == "错误" || _edAnswer.Text.Trim() == "B")) ? "B"
                     : ExamService.Normalize(_edAnswer.Text);
            // 选项文本
            for (int i = 0; i < q.Options.Count && i < _edOptions.Count; i++)
                q.Options[i].Content = _edOptions[i].Text;

            if (string.IsNullOrEmpty(q.Stem)) { SacDialog.Info(this, "校验", "题干不能为空。"); return; }
            if (q.Options.Count < 2) { SacDialog.Info(this, "校验", "至少需要 2 个选项。"); return; }
            if (ExamService.Normalize(q.Answer).Length == 0) { SacDialog.Info(this, "校验", "答案不能为空。"); return; }
            if (q.Answer.Any(c => c - 'A' >= q.Options.Count)) { SacDialog.Info(this, "校验", "答案字母超出选项范围。"); return; }

            // 落库
            Db.Exec(@"UPDATE questions SET qtype=@t, case_material=@cm, stem=@st, answer=@a, analysis=@an, score=@sc
                      WHERE id=@q",
                ("@t", q.QType), ("@cm", (object)q.CaseMaterial ?? DBNull.Value), ("@st", q.Stem),
                ("@a", q.Answer), ("@an", (object)q.Analysis ?? DBNull.Value), ("@sc", q.Score), ("@q", q.Id));
            Db.Exec("DELETE FROM options WHERE question_id=@q", ("@q", q.Id));
            for (int i = 0; i < q.Options.Count; i++)
            {
                Db.Exec("INSERT INTO options(question_id,label,content,sort_no) VALUES(@q,@l,@c,@n)",
                    ("@q", q.Id), ("@l", q.Options[i].Label), ("@c", q.Options[i].Content), ("@n", (long)i));
            }
            Db.Exec("DELETE FROM image_refs WHERE question_id=@q", ("@q", q.Id));
            int n = 0;
            foreach (var f in q.StemImages) AddRef(q.Id, f, "stem", null, n++);
            n = 0;
            foreach (var f in q.AnalysisImages) AddRef(q.Id, f, "analysis", null, n++);
            foreach (var o in q.Options)
            {
                n = 0;
                foreach (var f in o.ImageFiles) AddRef(q.Id, f, "option", o.Label, n++);
            }

            // 更新试卷总分缓存
            Db.Exec("UPDATE papers SET total_score=(SELECT COALESCE(SUM(score),0) FROM questions WHERE paper_id=@p) WHERE id=@p",
                ("@p", _paper.Id));

            RenderImageBars();
            if (!keepSelection) SacDialog.Info(this, "已保存", "本题修改已保存。");
        }
        catch (Exception ex)
        {
            SacDialog.Info(this, "保存失败", ex.Message);
        }
    }

    private static void AddRef(long questionId, string file, string scope, string label, int sortNo)
    {
        try
        {
            string path = file.StartsWith(ImportService.StoredPrefix, StringComparison.Ordinal)
                ? Path.Combine(App.ImageDir, file[ImportService.StoredPrefix.Length..])
                : file;
            if (!File.Exists(path)) return;
            var st = ImageStore.StoreFile(path);
            Db.Exec(@"INSERT INTO image_refs(image_id,question_id,scope,option_label,sort_no) VALUES(@i,@q,@s,@l,@n)",
                ("@i", st.ImageId), ("@q", questionId), ("@s", scope), ("@l", (object)label ?? DBNull.Value), ("@n", (long)sortNo));
        }
        catch { }
    }

    private void AddQuestion()
    {
        long qid = Db.Insert(@"INSERT INTO questions(paper_id,subject_id,qtype,stem,answer,score,sort_no)
            VALUES(@p,@s,'单选题','（新题目）请编辑题干','A',0.5,@no)",
            ("@p", _paper.Id), ("@s", _paper.SubjectId),
            ("@no", (long)(_questions.Count + 1)));
        foreach (var content in new[] { "选项一", "选项二", "选项三", "选项四" })
            Db.Exec("INSERT INTO options(question_id,label,content,sort_no) VALUES(@q,@l,@c,@n)",
                ("@q", qid), ("@l", ((char)('A' + Array.IndexOf(new[] { "选项一", "选项二", "选项三", "选项四" }, content))).ToString()), ("@c", content), ("@n", (long)Array.IndexOf(new[] { "选项一", "选项二", "选项三", "选项四" }, content)));
        _questions.Add(BankService.GetQuestion(qid));
        ReloadList();
        _list.SelectedIndex = _list.Items.Count - 1;
    }

    private void DeleteQuestion()
    {
        if (_current == null) return;
        if (!SacDialog.Confirm(this, "删除题目", "确定删除当前题目？（不可恢复）", dangerOk: true)) return;
        Db.Exec("DELETE FROM image_refs WHERE question_id=@q", ("@q", _current.Id));
        Db.Exec("DELETE FROM options WHERE question_id=@q", ("@q", _current.Id));
        Db.Exec("DELETE FROM exam_answers WHERE question_id=@q", ("@q", _current.Id));
        Db.Exec("DELETE FROM wrong_book WHERE question_id=@q", ("@q", _current.Id));
        Db.Exec("DELETE FROM questions WHERE id=@q", ("@q", _current.Id));
        _questions.Remove(_current);
        _current = null;
        ReloadList();
        RenderEditor();
    }
}
