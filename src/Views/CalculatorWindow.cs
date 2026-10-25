using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SacExam;

/// <summary>屏5：机考内置科学计算器（还原 img07；十进制、角度制/弧度制、函数与位运算、记忆键）。</summary>
public class CalculatorWindow : Window
{
    private readonly List<string> _tokens = new();
    private readonly TextBox _display;
    private double? _memory;
    private double _lastResult;
    private bool _justEvaluated;
    private bool _angleDeg = true;
    private bool _inv;   // 上档功能
    private bool _hyper; // 双曲函数
    private TextBlock _memIndicator;

    public CalculatorWindow(Window owner)
    {
        Title = "计算器";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (owner != null) Owner = owner;
        Icon = Ui.AppIcon;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
        InputMethod.SetIsInputMethodEnabled(this, false);

        _display = new TextBox
        {
            FontSize = 22,
            Text = "0",
            TextAlignment = TextAlignment.Right,
            IsReadOnly = true,
            Height = 56,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0xCF, 0xE2)),
        };

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(_display);

        // 状态行：内存指示 + 角度制/弧度制
        var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 8, 0, 2) };
        _memIndicator = new TextBlock { Text = "", Foreground = Ui.AuxBlue, FontWeight = FontWeights.Bold, Width = 30, VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(_memIndicator);
        status.Children.Add(MakeRadio("角度制", true, () => _angleDeg = true));
        status.Children.Add(MakeRadio("弧度制", false, () => _angleDeg = false));
        status.Children.Add(new TextBlock
        {
            Text = "（仅十进制）",
            Foreground = Ui.TextSub,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        });
        root.Children.Add(status);

        var invRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(32, 2, 0, 8) };
        var inv = new CheckBox { Content = "上档功能", Margin = new Thickness(0, 0, 18, 0) };
        inv.Checked += (_, _) => _inv = true;
        inv.Unchecked += (_, _) => _inv = false;
        var hy = new CheckBox { Content = "双曲函数" };
        hy.Checked += (_, _) => _hyper = true;
        hy.Unchecked += (_, _) => _hyper = false;
        invRow.Children.Add(inv);
        invRow.Children.Add(hy);
        root.Children.Add(invRow);

        // 顶部功能行：退格 / 清屏 / 全清
        var topRow = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        for (int i = 0; i < 6; i++) topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Put(topRow, "退格", 0, Backspace);
        Put(topRow, "清屏", 1, () => ClearAll(false));
        Put(topRow, "全清", 2, () => ClearAll(true));
        Put(topRow, "PI", 3, () => InsertNumber(Math.PI));
        Put(topRow, "E", 4, () => InsertNumber(Math.E));
        Put(topRow, "d.ms", 5, () => ApplyPostfix("dms"));
        root.Children.Add(topRow);

        // 按键区（7 列 × 6 行）：函数列 / 幂与括号列 / 记忆与位运算列 / 数字与四则运算区
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        for (int i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });

        // 列0：括号与三角/对数函数
        Put(grid, "(", 0, 0, () => InsertToken("("));
        Put(grid, "sin", 1, 0, () => InsertFunc("sin"));
        Put(grid, "cos", 2, 0, () => InsertFunc("cos"));
        Put(grid, "tan", 3, 0, () => InsertFunc("tan"));
        Put(grid, "ln", 4, 0, () => InsertFunc(_inv ? "exp" : "ln"));
        Put(grid, "log", 5, 0, () => InsertFunc(_inv ? "10^" : "log"));

        // 列1：幂/阶乘/倒数
        Put(grid, ")", 0, 1, () => InsertToken(")"));
        Put(grid, "x^y", 1, 1, () => InsertToken("^"));
        Put(grid, "x^3", 2, 1, () => { InsertToken("^"); InsertNumber(3); });
        Put(grid, "n!", 3, 1, () => ApplyPostfix("!"));
        Put(grid, "x^2", 4, 1, () => { InsertToken("^"); InsertNumber(2); });
        Put(grid, "1/x", 5, 1, () => ApplyPostfix("recip"));

        // 列2：记忆与位运算
        Put(grid, "取存", 0, 2, MemRecall);
        Put(grid, "清存", 1, 2, () => { _memory = null; UpdateMem(); });
        Put(grid, "累存", 2, 2, () => MemAdd(1));
        Put(grid, "积存", 3, 2, () => MemAdd(-1));
        Put(grid, "mod", 4, 2, () => InsertToken("mod"));
        Put(grid, "取整", 5, 2, () => ApplyPostfix("floor"));

        // 列3-5：数字区
        Put(grid, "7", 0, 3, () => InsertDigit("7"));
        Put(grid, "8", 0, 4, () => InsertDigit("8"));
        Put(grid, "9", 0, 5, () => InsertDigit("9"));
        Put(grid, "4", 1, 3, () => InsertDigit("4"));
        Put(grid, "5", 1, 4, () => InsertDigit("5"));
        Put(grid, "6", 1, 5, () => InsertDigit("6"));
        Put(grid, "1", 2, 3, () => InsertDigit("1"));
        Put(grid, "2", 2, 4, () => InsertDigit("2"));
        Put(grid, "3", 2, 5, () => InsertDigit("3"));
        Put(grid, "0", 3, 3, () => InsertDigit("0"));
        Put(grid, "±", 3, 4, Negate);
        Put(grid, ".", 3, 5, () => InsertDigit("."));

        // 列6 与底部：四则与位运算
        Put(grid, "/", 0, 6, () => InsertToken("/"));
        Put(grid, "*", 1, 6, () => InsertToken("*"));
        Put(grid, "-", 2, 6, () => InsertToken("-"));
        Put(grid, "+", 3, 6, () => InsertToken("+"));
        Put(grid, "与", 4, 3, () => InsertToken("&"));
        Put(grid, "或", 4, 4, () => InsertToken("|"));
        Put(grid, "非", 4, 5, () => InsertToken("~"));
        Put(grid, "左移", 4, 6, () => InsertToken("<<"));
        Put(grid, "xor", 5, 3, () => InsertToken("xor"));
        var eq = Put(grid, "=", 5, 4, Evaluate, "primary");
        Grid.SetColumnSpan(eq, 3);

        root.Children.Add(grid);

        var tip = new TextBlock
        {
            Text = "运算符：+ − × ÷ ^ 幂 · mod 取余 · floor 取整 · 与/或/非/异或/左移 按整数位运算 · d.ms 度分秒→十进制度 · 上档功能：sin→asin、ln→eˣ、log→10ˣ",
            Foreground = Ui.TextSub,
            FontSize = 11,
            Margin = new Thickness(2, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        root.Children.Add(tip);

        Content = root;
        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); return; }
            if (e.Key >= Key.D0 && e.Key <= Key.D9) InsertDigit(((char)('0' + (e.Key - Key.D0))).ToString());
            else if (e.Key == Key.Decimal || e.Key == Key.OemPeriod) InsertDigit(".");
            else if (e.Key == Key.Enter) Evaluate();
            else if (e.Key == Key.Back) Backspace();
            else if (e.Key == Key.Add) InsertToken("+");
            else if (e.Key == Key.Subtract) InsertToken("-");
            else if (e.Key == Key.Multiply) InsertToken("*");
            else if (e.Key == Key.Divide) InsertToken("/");
        };
    }

    private static RadioButton MakeRadio(string label, bool isChecked, Action onCheck)
    {
        var rb = new RadioButton { Content = label, IsChecked = isChecked, Margin = new Thickness(0, 0, 14, 0) };
        rb.Checked += (_, _) => onCheck();
        return rb;
    }

    private static void Put(Grid g, string text, int col, Action action, string style = "ghost")
    {
        var b = Ui.MakeButton(text, action, style);
        b.Margin = new Thickness(2);
        b.FontSize = 13.5;
        Grid.SetColumn(b, col);
        g.Children.Add(b);
    }

    private static Button Put(Grid g, string text, int row, int col, Action action, string style = "ghost")
    {
        var b = Ui.MakeButton(text, action, style);
        b.Margin = new Thickness(2);
        b.FontSize = 13.5;
        Grid.SetRow(b, row);
        Grid.SetColumn(b, col);
        g.Children.Add(b);
        return b;
    }

    private void UpdateMem() => _memIndicator.Text = _memory.HasValue ? "M" : "";

    // ---------- 输入 ----------

    private void InsertDigit(string d)
    {
        if (_justEvaluated) { _tokens.Clear(); _justEvaluated = false; }
        if (_tokens.Count == 0 || IsOp(_tokens[^1]) || IsFuncOpen(_tokens[^1]) || _tokens[^1] == "(")
        {
            _tokens.Add(d == "." ? "0." : d);
        }
        else
        {
            var last = _tokens[^1];
            if (d == "." && last.Contains('.')) return;
            if (last == "0" && d != ".") _tokens[^1] = d;
            else _tokens[^1] = last + d;
        }
        Refresh();
    }

    private void InsertToken(string t)
    {
        if (_justEvaluated) _justEvaluated = false;
        if (_tokens.Count == 0)
        {
            if (t is "-" or "~" or "(") _tokens.Add(t);
            else
            {
                _tokens.Add(_lastResult.ToString("R", CultureInfo.InvariantCulture));
                _tokens.Add(t);
            }
        }
        else if (IsOp(_tokens[^1]) && IsOp(t) && t != "~")
        {
            _tokens[^1] = t;
        }
        else _tokens.Add(t);
        Refresh();
    }

    private void InsertFunc(string f) => InsertToken(f + "(");

    private void InsertNumber(double v)
    {
        if (_justEvaluated) { _tokens.Clear(); _justEvaluated = false; }
        _tokens.Add(v.ToString("R", CultureInfo.InvariantCulture));
        Refresh();
    }

    /// <summary>后缀单目算子（! / dms / recip / floor）。</summary>
    private void ApplyPostfix(string t)
    {
        if (_tokens.Count == 0) return;
        _tokens.Add(t);
        Refresh();
    }

    private void Negate()
    {
        if (_tokens.Count == 0) { _tokens.Add("-"); Refresh(); return; }
        if (_tokens.Count == 1 && double.TryParse(_tokens[0], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
        {
            _tokens[0] = _tokens[0].StartsWith("-", StringComparison.Ordinal) ? _tokens[0][1..] : "-" + _tokens[0];
        }
        else
        {
            _tokens.Insert(0, "-");
            _tokens.Insert(0, "(");
            _tokens.Add(")");
        }
        Refresh();
    }

    private void Backspace()
    {
        if (_tokens.Count == 0) return;
        var last = _tokens[^1];
        if (last.Length > 1 && double.TryParse(last, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            _tokens[^1] = last[..^1];
        else _tokens.RemoveAt(_tokens.Count - 1);
        Refresh();
    }

    private void ClearAll(bool clearMemory)
    {
        _tokens.Clear();
        _display.Text = "0";
        _justEvaluated = false;
        if (clearMemory) { _memory = null; UpdateMem(); }
    }

    private static bool IsOp(string t) => t is "+" or "-" or "*" or "/" or "^" or "mod" or "&" or "|" or "xor" or "<<" or "~";
    private static bool IsFuncOpen(string t) => t.EndsWith("(", StringComparison.Ordinal);

    // ---------- 记忆 ----------

    private void MemRecall()
    {
        if (_memory.HasValue) InsertNumber(_memory.Value);
    }

    private void MemAdd(int sign)
    {
        if (TryEvalCurrent(out var v))
        {
            _memory = (_memory ?? 0) + sign * v;
            UpdateMem();
        }
    }

    private bool TryEvalCurrent(out double v)
    {
        try
        {
            if (_tokens.Count == 0) { v = _lastResult; return true; }
            v = new ExprEval(_tokens, _angleDeg, _inv, _hyper).Parse();
            return true;
        }
        catch { v = 0; return false; }
    }

    private void Refresh()
    {
        _display.Text = _tokens.Count == 0 ? "0" : string.Join("", DisplayTokens());
    }

    private IEnumerable<string> DisplayTokens()
    {
        foreach (var t in _tokens)
        {
            if (IsFuncOpen(t)) yield return t;
            else if (t == ")" || t is "!" or "dms" or "recip" or "floor") yield return t;
            else if (t.Length == 1 && "+-*/^".Contains(t)) yield return " " + t + " ";
            else if (t is "mod" or "xor" or "<<" or "&" or "|" or "~") yield return " " + t + " ";
            else yield return t;
        }
    }

    // ---------- 求值 ----------

    private void Evaluate()
    {
        if (_tokens.Count == 0)
        {
            _display.Text = Fmt(_lastResult);
            return;
        }
        try
        {
            var val = new ExprEval(_tokens, _angleDeg, _inv, _hyper).Parse();
            if (double.IsNaN(val) || double.IsInfinity(val)) throw new ArgumentException("结果无效");
            _lastResult = val;
            _display.Text = Fmt(val);
            _tokens.Clear();
            _tokens.Add(val.ToString("R", CultureInfo.InvariantCulture));
            _justEvaluated = true;
        }
        catch
        {
            _display.Text = "错误";
            _tokens.Clear();
            _justEvaluated = false;
        }
    }

    internal static string Fmt(double v)
    {
        if (v == Math.Floor(v) && Math.Abs(v) < 1e15)
            return ((long)v).ToString();
        return v.ToString("G15", CultureInfo.InvariantCulture);
    }

    // ===================== 表达式求值器 =====================

    private class ExprEval
    {
        private readonly List<string> _t;
        private readonly bool _deg, _inv, _hyper;
        private int _p;

        public ExprEval(List<string> t, bool deg, bool inv, bool hyper)
        {
            _t = t; _deg = deg; _inv = inv; _hyper = hyper;
        }

        public double Parse()
        {
            double v = ParseXor();
            if (_p < _t.Count) throw new ArgumentException("表达式不完整");
            return v;
        }

        private string Peek => _p < _t.Count ? _t[_p] : null;
        private string Take() => _t[_p++];

        private double ParseXor()
        {
            var v = ParseOr();
            while (Peek == "xor") { Take(); v = (long)Math.Round(v) ^ (long)Math.Round(ParseOr()); }
            return v;
        }
        private double ParseOr()
        {
            var v = ParseAnd();
            while (Peek == "|") { Take(); v = (long)Math.Round(v) | (long)Math.Round(ParseAnd()); }
            return v;
        }
        private double ParseAnd()
        {
            var v = ParseShift();
            while (Peek == "&") { Take(); v = (long)Math.Round(v) & (long)Math.Round(ParseShift()); }
            return v;
        }
        private double ParseShift()
        {
            var v = ParseAdd();
            while (Peek == "<<") { Take(); v = (long)Math.Round(v) << (int)Math.Round(ParseAdd()); }
            return v;
        }
        private double ParseAdd()
        {
            var v = ParseMul();
            while (Peek == "+" || Peek == "-")
            {
                var op = Take();
                var r = ParseMul();
                v = op == "+" ? v + r : v - r;
            }
            return v;
        }
        private double ParseMul()
        {
            var v = ParseUnary();
            while (Peek is "*" or "/" or "mod")
            {
                var op = Take();
                var r = ParseUnary();
                v = op switch
                {
                    "*" => v * r,
                    "/" => r == 0 ? throw new DivideByZeroException() : v / r,
                    _ => (long)Math.Round(v) % (long)Math.Round(r),
                };
            }
            return v;
        }
        private double ParseUnary()
        {
            if (Peek == "-") { Take(); return -ParseUnary(); }
            if (Peek == "~") { Take(); return ~(long)Math.Round(ParseUnary()); }
            return ParsePow();
        }
        private double ParsePow()
        {
            var b = ParsePostfix();
            if (Peek == "^") { Take(); return Math.Pow(b, ParseUnary()); }
            return b;
        }
        private double ParsePostfix()
        {
            var v = ParsePrimary();
            while (Peek is "!" or "dms" or "recip" or "floor")
            {
                var op = Take();
                switch (op)
                {
                    case "!":
                        v = Factorial(v);
                        break;
                    case "dms":
                        double d = Math.Truncate(v);
                        double m = Math.Truncate((v - d) * 100);
                        double s = ((v - d) * 100 - m) * 100;
                        v = d + m / 60 + s / 3600;
                        break;
                    case "recip":
                        v = v == 0 ? throw new DivideByZeroException() : 1 / v;
                        break;
                    case "floor":
                        v = Math.Floor(v);
                        break;
                }
            }
            return v;
        }
        private double ParsePrimary()
        {
            var t = Peek ?? throw new ArgumentException("表达式不完整");
            if (t == "(")
            {
                Take();
                var v = ParseXor();
                if (Peek != ")") throw new ArgumentException("缺少右括号");
                Take();
                return v;
            }
            if (t.EndsWith("(", StringComparison.Ordinal))
            {
                Take();
                var arg = ParseXor();
                if (Peek != ")") throw new ArgumentException("缺少右括号");
                Take();
                return ApplyFunc(t[..^1], arg);
            }
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            {
                Take();
                return num;
            }
            throw new ArgumentException($"无法识别的记号 {t}");
        }

        private static double Factorial(double v)
        {
            if (v < 0 || v != Math.Floor(v)) throw new ArgumentException("n! 仅支持非负整数");
            if (v > 170) return double.PositiveInfinity;
            double r = 1;
            for (int i = 2; i <= (int)v; i++) r *= i;
            return r;
        }

        private double ApplyFunc(string f, double x)
        {
            return f switch
            {
                "sin" => _hyper ? (_inv ? Math.Asinh(x) : Math.Sinh(x)) : (_inv ? AsinDegRad(x) : Math.Sin(ToRad(x))),
                "cos" => _hyper ? (_inv ? Math.Acosh(x) : Math.Cosh(x)) : (_inv ? AcosDegRad(x) : Math.Cos(ToRad(x))),
                "tan" => _hyper ? (_inv ? Math.Atanh(x) : Math.Tanh(x)) : (_inv ? AtanDegRad(x) : Math.Tan(ToRad(x))),
                "ln" => _inv ? Math.Exp(x) : Math.Log(x),
                "log" => _inv ? Math.Pow(10, x) : Math.Log10(x),
                "exp" => Math.Exp(x),
                "10^" => Math.Pow(10, x),
                _ => throw new ArgumentException($"未知函数 {f}"),
            };
        }

        private double ToRad(double x) => _deg ? x * Math.PI / 180 : x;
        private double AsinDegRad(double x) => _deg ? Math.Asin(x) * 180 / Math.PI : Math.Asin(x);
        private double AcosDegRad(double x) => _deg ? Math.Acos(x) * 180 / Math.PI : Math.Acos(x);
        private double AtanDegRad(double x) => _deg ? Math.Atan(x) * 180 / Math.PI : Math.Atan(x);
    }
}
