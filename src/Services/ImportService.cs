using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace SacExam.Services;

/// <summary>整卷导入（说明书 §5）：Excel/CSV/JSON 三通道，逐行校验，预览模型，事务入库，错误报告。</summary>
public static class ImportService
{
    public static readonly Regex ImgPlaceholder = new(@"\{\{img:([^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>提交入库后改写文本中的内联图片占位为库内文件名的前缀。</summary>
    public const string StoredPrefix = "@STORED@";

    // ===================== 通道入口 =====================

    public static ParseOutcome ParseFile(string filePath, string imagesFolder)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".json" => ParseJson(filePath, imagesFolder),
            ".csv" => ParseCsv(filePath, imagesFolder),
            ".xlsx" or ".xlsm" or ".xls" => ParseXlsx(filePath, imagesFolder),
            _ => throw new ArgumentException("仅支持 xlsx / csv / json 三种整卷文件"),
        };
    }

    // ===================== JSON（sac-mock-exam-paper，附录 B 基准） =====================

    public static ParseOutcome ParseJson(string path, string imagesFolder)
    {
        var json = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        }).RootElement;

        var outcome = new ParseOutcome();
        if (json.TryGetProperty("paper", out var paper) && paper.ValueKind == JsonValueKind.Object)
        {
            if (paper.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) outcome.PaperName = n.GetString();
            if (paper.TryGetProperty("subject", out var s) && s.ValueKind == JsonValueKind.String) outcome.PaperSubject = s.GetString();
            if (paper.TryGetProperty("duration_minutes", out var d) && d.TryGetInt32(out int dm)) outcome.DurationMinutes = dm;
        }

        if (!json.TryGetProperty("questions", out var qs) || qs.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("JSON 缺少 questions 数组（sac-mock-exam-paper 格式）");

        var imgDirs = ImageDirCandidates(path, imagesFolder);
        int idx = 0;
        foreach (var q in qs.EnumerateArray())
        {
            idx++;
            var pq = new ParsedQuestion { RowIndex = TryGetInt(q, "no") ?? idx, FromJson = true };

            if (q.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Object)
            {
                if (imgs.TryGetProperty("stem", out var si) && si.ValueKind == JsonValueKind.Array)
                    foreach (var f in si.EnumerateArray())
                        if (f.ValueKind == JsonValueKind.String) pq.StemImages.Add(f.GetString());
                if (imgs.TryGetProperty("analysis", out var ai) && ai.ValueKind == JsonValueKind.Array)
                    foreach (var f in ai.EnumerateArray())
                        if (f.ValueKind == JsonValueKind.String) pq.AnalysisImages.Add(f.GetString());
                if (imgs.TryGetProperty("options", out var oi) && oi.ValueKind == JsonValueKind.Object)
                    foreach (var op in oi.EnumerateObject())
                    {
                        var list = new List<string>();
                        if (op.Value.ValueKind == JsonValueKind.Array)
                            foreach (var f in op.Value.EnumerateArray())
                                if (f.ValueKind == JsonValueKind.String) list.Add(f.GetString());
                        if (list.Count > 0) pq.OptionImages[op.Name.ToUpperInvariant()] = list;
                    }
            }

            pq.OrigType = GetStr(q, "type");
            pq.Type = NormType(pq.OrigType);
            pq.CaseMaterial = NullIfEmpty(GetStr(q, "case_material"));
            pq.Stem = GetStr(q, "stem");
            pq.AnswerRaw = GetStr(q, "answer");
            pq.Analysis = GetStr(q, "analysis");
            pq.Chapter = GetStr(q, "chapter");
            if (q.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number)
                pq.Score = sc.GetDouble();
            if (q.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                foreach (var o in opts.EnumerateArray()) pq.Options.Add(o.GetString() ?? "");

            var row = Validate(pq);
            ResolveImageFiles(row, imgDirs);
            outcome.Rows.Add(row);
        }
        Summarize(outcome);
        return outcome;
    }

    // ===================== CSV =====================

    public static ParseOutcome ParseCsv(string path, string imagesFolder)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        else
        {
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch { text = Encoding.GetEncoding("GB18030").GetString(bytes); }
        }
        return ParseGrid(SplitCsv(text), path, imagesFolder);
    }

    /// <summary>带引号、可含换行的标准 CSV 切分。</summary>
    public static List<string[]> SplitCsv(string text)
    {
        var rows = new List<string[]>();
        var cur = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuote)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuote = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuote = true;
            else if (c == ',') { cur.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                cur.Add(sb.ToString()); sb.Clear();
                rows.Add(cur.ToArray()); cur = new List<string>();
            }
            else sb.Append(c);
        }
        if (sb.Length > 0 || cur.Count > 0) { cur.Add(sb.ToString()); rows.Add(cur.ToArray()); }
        return rows;
    }

    // ===================== Excel（NPOI，9/12 列模板 + 内嵌图片尽力解析） =====================

    public static ParseOutcome ParseXlsx(string path, string imagesFolder)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        IWorkbook wb = WorkbookFactory.Create(fs);
        ISheet sheet = wb.GetSheetAt(0);
        if (sheet == null) throw new ArgumentException("Excel 首个工作表为空");

        var grid = new List<string[]>();
        for (int i = sheet.FirstRowNum; i <= sheet.LastRowNum; i++)
        {
            var row = sheet.GetRow(i);
            if (row == null) { grid.Add(Array.Empty<string>()); continue; }
            int last = row.LastCellNum > 0 ? row.LastCellNum : 0;
            var cells = new string[last];
            for (int j = 0; j < last; j++) cells[j] = CellStr(row.GetCell(j));
            grid.Add(cells);
        }

        var outcome = ParseGrid(grid, path, imagesFolder);

        // §5.9 来源②：xlsx 内嵌图片（直接解析 zip 内 drawing 锚点，按行归属，尽力解析）
        try
        {
            var rowImgs = ExtractEmbeddedImages(path);
            foreach (var kv in rowImgs)
            {
                var target = outcome.Rows.FirstOrDefault(r => r.RowIndex == kv.Key);
                if (target?.Question != null)
                {
                    target.Question.StemImages.AddRange(kv.Value);
                    target.Question.Warnings.Add("xlsx内嵌图片已归入题干（可在编辑器调整）");
                }
            }
        }
        catch { /* 尽力解析：失败不阻塞导入 */ }

        return outcome;
    }

    /// <summary>从 xlsx（zip）中提取内嵌图片：解析 xl/drawings/drawing*.xml 的锚点行号与 r:embed 关系。</summary>
    public static Dictionary<int, List<string>> ExtractEmbeddedImages(string xlsxPath)
    {
        var result = new Dictionary<int, List<string>>();
        string tmpDir = Path.Combine(Path.GetTempPath(), "sacexam_import_embedded");
        Directory.CreateDirectory(tmpDir);

        using var zip = ZipFile.OpenRead(xlsxPath);
        foreach (var entry in zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"^xl/drawings/drawing\d+\.xml$")))
        {
            string drawingName = Path.GetFileName(entry.FullName);
            var relMap = new Dictionary<string, string>();
            var relsEntry = zip.GetEntry($"xl/drawings/_rels/{drawingName}.rels");
            if (relsEntry != null)
            {
                string relsXml = new StreamReader(relsEntry.Open()).ReadToEnd();
                foreach (Match m in Regex.Matches(relsXml, @"Id=""([^""]+)""[^>]*Target=""([^""]+)"""))
                    relMap[m.Groups[1].Value] = m.Groups[2].Value;
            }

            string xml;
            using (var sr = new StreamReader(entry.Open())) xml = sr.ReadToEnd();
            foreach (Match m in Regex.Matches(xml,
                @"<(?:\w+:)?(?:twoCellAnchor|oneCellAnchor)\b.*?</(?:\w+:)?(?:twoCellAnchor|oneCellAnchor)>",
                RegexOptions.Singleline))
            {
                string block = m.Value;
                var rowMo = Regex.Match(block, @"<(?:\w+:)?from\b.*?<(?:\w+:)?row[^>]*>(\d+)<", RegexOptions.Singleline);
                var blipMo = Regex.Match(block, @"(?:r|ns\d+):embed=""([^""]+)""");
                if (!rowMo.Success || !blipMo.Success) continue;
                if (!int.TryParse(rowMo.Groups[1].Value, out int anchorRow)) continue;
                int sheetRow = anchorRow + 1; // 1 基行号
                if (!relMap.TryGetValue(blipMo.Groups[1].Value, out string target)) continue;

                string mediaPath = target.Replace("../", "xl/").Replace("~/", "xl/").Replace('\\', '/');
                if (mediaPath.StartsWith("/")) mediaPath = mediaPath.TrimStart('/');
                if (!mediaPath.StartsWith("xl/", StringComparison.Ordinal)) mediaPath = "xl/" + mediaPath;
                var media = zip.GetEntry(mediaPath);
                if (media == null) continue;
                byte[] data;
                using (var ms = new MemoryStream())
                {
                    media.Open().CopyTo(ms);
                    data = ms.ToArray();
                }
                if (data.Length == 0) continue;
                string file = Path.Combine(tmpDir, $"emb_{sheetRow}_{result.Values.Sum(l => l.Count)}_{Path.GetFileName(mediaPath)}");
                File.WriteAllBytes(file, data);
                if (!result.TryGetValue(sheetRow, out var list)) result[sheetRow] = list = new List<string>();
                list.Add(file);
            }
        }
        return result;
    }

    private static string CellStr(ICell c)
    {
        if (c == null) return "";
        switch (c.CellType)
        {
            case CellType.String: return c.StringCellValue ?? "";
            case CellType.Numeric:
                if (DateUtil.IsCellDateFormatted(c)) return Convert.ToDateTime(c.DateCellValue).ToString("yyyy-MM-dd");
                return NumStr(c.NumericCellValue);
            case CellType.Boolean: return c.BooleanCellValue ? "TRUE" : "FALSE";
            case CellType.Formula:
                switch (c.CachedFormulaResultType)
                {
                    case CellType.String: return c.StringCellValue ?? "";
                    case CellType.Numeric: return NumStr(c.NumericCellValue);
                    default: return c.ToString() ?? "";
                }
            default: return (c.ToString() ?? "").Trim();
        }
    }

    private static string NumStr(double d) =>
        d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture)
        : d.ToString("0.######", CultureInfo.InvariantCulture);

    public static string SniffExt(byte[] d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50) return ".png";
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8) return ".jpg";
        if (d.Length >= 6 && d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46) return ".gif";
        if (d.Length >= 2 && d[0] == 0x42 && d[1] == 0x4D) return ".bmp";
        if (d.Length >= 12 && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P') return ".webp";
        return ".png";
    }

    // ===================== 网格（表头 → 列映射 → 逐行解析） =====================

    private static ParseOutcome ParseGrid(List<string[]> grid, string filePath, string imagesFolder)
    {
        var outcome = new ParseOutcome();
        if (grid.Count == 0) throw new ArgumentException("文件为空");

        // 定位表头行：前 5 行内找到含“题型/题目”的行
        int headerRow = -1;
        var colMap = new Dictionary<string, int>();
        for (int i = 0; i < Math.Min(5, grid.Count); i++)
        {
            var m = BuildColMap(grid[i]);
            if (m.ContainsKey("题型") || m.ContainsKey("题目"))
            {
                headerRow = i; colMap = m; break;
            }
        }
        if (headerRow < 0)
        {
            // 无表头：按位置固定 9 列（图片列仅在有列时生效）
            colMap = new Dictionary<string, int>
            {
                ["题型"] = 0, ["题目"] = 1, ["选项1"] = 2, ["选项2"] = 3, ["选项3"] = 4, ["选项4"] = 5,
                ["正确答案"] = 6, ["答案解析"] = 7, ["分值"] = 8,
            };
            if (grid.Max(g => g.Length) >= 12)
            {
                colMap["题干图片"] = 9; colMap["选项图片"] = 10; colMap["解析图片"] = 11;
            }
        }

        var imgDirs = ImageDirCandidates(filePath, imagesFolder);
        for (int i = headerRow + 1; i < grid.Count; i++)
        {
            string[] cells = grid[i];
            int rowNo = i + 1; // 1 基行号（与 Excel 行号一致）
            if (cells.All(string.IsNullOrWhiteSpace)) continue; // 规则10：整行空跳过

            string Get(int col) => (col >= 0 && col < cells.Length) ? cells[col] : "";
            var pq = new ParsedQuestion { RowIndex = rowNo };

            pq.OrigType = Get(Col("题型"));
            pq.Type = NormType(pq.OrigType);
            pq.Stem = Get(Col("题目"));
            pq.Options = new List<string>
            {
                Get(Col("选项1")), Get(Col("选项2")), Get(Col("选项3")), Get(Col("选项4")),
            };
            pq.Analysis = Get(Col("答案解析"));
            pq.AnswerRaw = Get(Col("正确答案"));
            string scoreRaw = Get(Col("分值"));
            if (double.TryParse(scoreRaw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double sc))
                pq.Score = sc;
            pq.StemImages.AddRange(SplitNames(Get(Col("题干图片"))));
            pq.AnalysisImages.AddRange(SplitNames(Get(Col("解析图片"))));
            string optImgRaw = Get(Col("选项图片"));
            if (!string.IsNullOrWhiteSpace(optImgRaw)) pq.OptionImages = ParseOptionImagesSpec(optImgRaw);

            var row = Validate(pq);
            ResolveImageFiles(row, imgDirs);
            outcome.Rows.Add(row);
        }

        Summarize(outcome);
        return outcome;

        int Col(string key) => colMap.TryGetValue(key, out int v) ? v : -1;
    }

    private static Dictionary<string, int> BuildColMap(string[] header)
    {
        var map = new Dictionary<string, int>();
        for (int j = 0; j < header.Length; j++)
        {
            string h = (header[j] ?? "").Trim();
            if (h.Length == 0) continue;
            string key = h switch
            {
                "题型" or "类型" => "题型",
                "题目" or "题干" or "试题" => "题目",
                "正确答案" or "答案" => "正确答案",
                "答案解析" or "解析" or "答案与解析" => "答案解析",
                "分值" or "分数" or "每题分值" => "分值",
                "题干图片" or "题目图片" => "题干图片",
                "选项图片" => "选项图片",
                "解析图片" => "解析图片",
                _ => null,
            };
            if (key != null && !map.ContainsKey(key)) { map[key] = j; continue; }
            var mo = Regex.Match(h, @"^选项\s*([1-4A-Da-d])$");
            if (mo.Success)
            {
                string c = mo.Groups[1].Value.ToUpperInvariant();
                int idx = char.IsDigit(c[0]) ? c[0] - '1' : c[0] - 'A';
                map[$"选项{idx + 1}"] = j;
            }
        }
        return map;
    }

    private static List<string> SplitNames(string raw) =>
        (raw ?? "").Split(';', '；', ',', '，').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>选项图片列格式：A:tu_a.png;C:tu_c.png 或按 ; 顺序对应 A、B、C、D。</summary>
    private static Dictionary<string, List<string>> ParseOptionImagesSpec(string raw)
    {
        var dict = new Dictionary<string, List<string>>();
        var parts = SplitNames(raw);
        bool labeled = parts.All(p => Regex.IsMatch(p, @"^\s*[A-Ha-h]\s*[:：]"));
        for (int i = 0; i < parts.Count; i++)
        {
            if (labeled)
            {
                var mo = Regex.Match(parts[i], @"^\s*([A-Ha-h])\s*[:：]\s*(.+)$");
                if (mo.Success)
                    dict[mo.Groups[1].Value.ToUpperInvariant()] = SplitNames(mo.Groups[2].Value);
            }
            else if (i < 4)
            {
                dict[((char)('A' + i)).ToString()] = new List<string> { parts[i] };
            }
        }
        return dict;
    }

    private static List<string> ImageDirCandidates(string filePath, string explicitFolder)
    {
        var dirs = new List<string>();
        void Add(string d) { if (!string.IsNullOrEmpty(d) && Directory.Exists(d) && !dirs.Contains(d)) dirs.Add(d); }
        Add(explicitFolder);
        string dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        Add(dir);
        Add(Path.Combine(dir, "images"));
        Add(Path.Combine(dir, "图片"));
        return dirs;
    }

    private static string ResolveImage(string name, List<string> dirs)
    {
        if (Path.IsPathRooted(name) && File.Exists(name)) return name;
        foreach (var d in dirs)
        {
            string p = Path.Combine(d, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static void ResolveImageFiles(RowResult row, List<string> dirs)
    {
        if (row.Question == null) return;
        ResolveList(row.Question.StemImages);
        ResolveList(row.Question.AnalysisImages);
        foreach (var k in row.Question.OptionImages.Keys.ToList())
            ResolveList(row.Question.OptionImages[k]);

        void ResolveList(List<string> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                string resolved = ResolveImage(list[i], dirs);
                if (resolved != null) list[i] = resolved;
            }
        }
    }

    // ===================== 归一化与校验（§5.3/§5.4/§5.5） =====================

    public static string NormType(string t)
    {
        t = (t ?? "").Trim();
        if (t.Length == 0) return null;
        if (t.Contains("综合") || t.Contains("不定项") || t.Contains("案例")) return "综合题";
        if (t.Contains("判断")) return "判断题";
        if (t.Contains("多选")) return "多选题";
        if (t.Contains("单选")) return "单选题";
        return null;
    }

    private static readonly Regex OptionPrefix = new(@"^\s*[A-Ha-h][、,\.．:：\)）]\s*", RegexOptions.Compiled);

    public static string StripOptionPrefix(string s) =>
        s == null ? "" : OptionPrefix.Replace(s, "").Trim();

    public static bool StartsWithCaseMarker(string stem) => (stem ?? "").TrimStart().StartsWith("【案例材料】", StringComparison.Ordinal);

    /// <summary>综合题材料/设问切分（§5.3 规则 3）。返回 true 表示完成切分。</summary>
    public static bool TrySplitCase(string stem, List<string> markers, out string material, out string question)
    {
        material = null; question = stem ?? "";
        if (string.IsNullOrEmpty(stem)) return false;
        foreach (var m in markers)
        {
            int idx = stem.IndexOf(m, StringComparison.Ordinal);
            if (idx >= 0)
            {
                material = stem.Substring(0, idx + m.Length).Trim();
                question = stem.Substring(idx + m.Length).Trim();
                if (question.Length == 0) question = stem; // 分界句后为空则整段保留
                return true;
            }
        }
        return false;
    }

    /// <summary>判断题答案归一化（§5.4）：A/B/正确/错误/对/错/√/× → A/B。</summary>
    public static string NormJudgeAnswer(string raw)
    {
        string t = (raw ?? "").Trim().TrimEnd('.', '。').Trim();
        if (t.Length == 0) return null;
        string head = t[0].ToString().ToUpperInvariant();
        switch (head)
        {
            case "A": return "A";
            case "B": return "B";
            case "√":
            case "✓": return "A";
            case "×":
            case "✗": return "B";
            case "对": return "A";
            case "错": return "B";
        }
        if (t == "正确") return "A";
        if (t == "错误") return "B";
        return null;
    }

    /// <summary>逐行校验并完成归一化（§5.3 综合题识别 / §5.4 判断归一化 / §5.5 校验规则）。</summary>
    private static RowResult Validate(ParsedQuestion pq)
    {
        var row = new RowResult { RowIndex = pq.RowIndex };
        var settings = SettingsService.Get();

        // 规则1：题型
        if (pq.Type == null)
        {
            row.Ok = false;
            row.Reason = $"题型无法识别：“{pq.OrigType}”（应为 单选题/多选题/判断题/综合题）";
            return row;
        }

        // §5.3 规则2：题干以【案例材料】开头 → 一律综合题（orig_type 保留原标注）
        if (pq.Type != "判断题" && StartsWithCaseMarker(pq.Stem) && pq.Type != "综合题")
        {
            pq.OrigType = pq.OrigType ?? pq.Type;
            pq.Type = "综合题";
            row.Recognition = "案例→不定项";
        }
        else if (pq.Type == "综合题" && pq.OrigType != null && pq.OrigType.Contains("不定项"))
        {
            row.Recognition = "旧称→综合题";
        }
        else if (pq.Type == "综合题" && pq.OrigType != null && !pq.OrigType.Contains("综合"))
        {
            row.Recognition = "案例→不定项";
        }
        else row.Recognition = "正常";

        // 综合题：材料/设问切分
        if (pq.Type == "综合题")
        {
            if (string.IsNullOrEmpty(pq.CaseMaterial))
            {
                if (TrySplitCase(pq.Stem ?? "", settings.CaseSplitMarkers, out string material, out string question))
                {
                    pq.CaseMaterial = material;
                    pq.Stem = question;
                }
                else pq.Stem = (pq.Stem ?? "").Trim(); // 找不到分界句：整段存 stem
            }
            else pq.Stem = (pq.Stem ?? "").Trim();
        }
        else pq.Stem = (pq.Stem ?? "").Trim();

        // 规则2：题干非空（综合题材料也算内容）
        if (string.IsNullOrWhiteSpace(pq.Stem) && string.IsNullOrWhiteSpace(pq.CaseMaterial))
        {
            row.Ok = false; row.Reason = "题干为空"; return row;
        }
        if ((pq.Stem ?? "").Length > 5000)
        {
            row.Ok = false; row.Reason = "题干超过 5000 字符上限"; return row;
        }

        // 选项清洗：去 “A、” 前缀，空串剔除
        var cleaned = new List<string>();
        foreach (var o in pq.Options)
        {
            string c = StripOptionPrefix(o);
            if (c.Length > 0) cleaned.Add(c);
        }
        if (pq.Type == "判断题" && cleaned.Count == 0)
        {
            cleaned.Add("正确"); cleaned.Add("错误"); // §5.4 固定两选项
        }
        pq.Options = cleaned;

        // 规则6：选项数量
        if (pq.Options.Count < 2)
        {
            row.Ok = false; row.Reason = $"选项数量不足（实际 {pq.Options.Count} 个，至少 2 个）"; return row;
        }

        // 答案规范化（规则3/4/5）
        string rawAnswer = pq.AnswerRaw;
        if (pq.Type == "判断题")
        {
            string na = NormJudgeAnswer(rawAnswer);
            if (na == null)
            {
                row.Ok = false;
                row.Reason = $"判断题答案无法归一化：“{rawAnswer}”（兼容 A/B/正确/错误/对/错/√/×）";
                return row;
            }
            pq.Answer = na;
        }
        else
        {
            string letters = new((rawAnswer ?? "").ToUpperInvariant().Where(char.IsAsciiLetter).ToArray());
            if (letters.Length == 0) { row.Ok = false; row.Reason = "正确答案为空或无合法字母"; return row; }
            if (pq.Type == "单选题" && letters.Length != 1)
            {
                row.Ok = false; row.Reason = $"单选题答案应为单个字母，实际为“{rawAnswer}”"; return row;
            }
            if (pq.Type == "多选题" && letters.Length < 2)
            {
                row.Ok = false; row.Reason = $"多选题答案应至少 2 个字母，实际为“{rawAnswer}”"; return row;
            }
            if (pq.Type == "综合题" && letters.Length > 4)
            {
                row.Ok = false; row.Reason = "综合题答案字母数超上限（>4）"; return row;
            }
            letters = new string(letters.Distinct().OrderBy(c => c).ToArray());
            if (letters.Any(c => c - 'A' >= pq.Options.Count))
            {
                row.Ok = false; row.Reason = $"答案 {letters} 超出实际选项数 {pq.Options.Count}"; return row;
            }
            pq.Answer = letters;
        }

        // 分值（规则7）：留空按预设补齐（单选 0.5、其余 1）；非法值报错
        if (!pq.Score.HasValue) pq.Score = pq.Type == "单选题" ? 0.5 : 1;
        else if (pq.Score.Value <= 0)
        {
            row.Ok = false; row.Reason = $"分值非法（{pq.Score.Value}），须为正数"; return row;
        }

        // 规则8/9：图片缺失或格式问题 → 警告（不拦截）
        foreach (var missing in MissingImages(pq))
            pq.Warnings.Add($"图片缺失：{missing}（导入后显示占位提示）");

        row.Ok = true;
        row.Question = pq;
        return row;
    }

    private static IEnumerable<string> MissingImages(ParsedQuestion pq)
    {
        foreach (var f in pq.StemImages.Concat(pq.AnalysisImages))
            if (!File.Exists(f) && !ImageStore.ExistsByFileName(f))
                yield return f;
        foreach (var kv in pq.OptionImages)
            foreach (var f in kv.Value)
                if (!File.Exists(f) && !ImageStore.ExistsByFileName(f))
                    yield return f;
        // {{img:}} 内联引用
        foreach (var text in InlineTexts(pq))
            foreach (Match mo in ImgPlaceholder.Matches(text))
            {
                string name = mo.Groups[1].Value.Trim();
                if (!ImageStore.ExistsByFileName(name) &&
                    !pq.StemImages.Concat(pq.AnalysisImages).Any(p => Path.GetFileName(p) == name))
                    yield return name;
            }
    }

    private static IEnumerable<string> InlineTexts(ParsedQuestion pq)
    {
        yield return pq.Stem ?? "";
        yield return pq.Analysis ?? "";
        foreach (var o in pq.Options) yield return o ?? "";
    }

    private static void Summarize(ParseOutcome outcome)
    {
        outcome.CountByType = outcome.Rows.Where(r => r.Ok)
            .GroupBy(r => r.Question.Type)
            .ToDictionary(g => g.Key, g => g.Count());
        outcome.TotalScore = Math.Round(outcome.Rows.Where(r => r.Ok).Sum(r => r.Question.Score ?? 0), 3);
    }

    // ===================== 提交入库 =====================

    public class CommitOptions
    {
        public string FilePath;
        public string ImagesFolder;
        public string PaperName;
        public long SubjectId;
        public int? DurationMinutes;
        public List<RowResult> Rows;      // 已按 OnlyValidRows 过滤或原样（错误行自动跳过）
    }

    public static ImportReport Commit(CommitOptions opt)
    {
        var okRows = opt.Rows.Where(r => r.Ok).Select(r => r.Question).ToList();
        var report = new ImportReport();

        var imgDirs = new List<string>();
        void AddDir(string d) { if (!string.IsNullOrEmpty(d) && Directory.Exists(d) && !imgDirs.Contains(d)) imgDirs.Add(d); }
        AddDir(opt.ImagesFolder);
        AddDir(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(opt.FilePath)), "images"));
        AddDir(Path.Combine(Path.GetTempPath(), "sacexam_import_embedded"));

        Db.Transaction(tx =>
        {
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            long batchId = Db.TxInsert(tx, @"INSERT INTO import_batches(file_name,paper_name,imported_at,total_rows,ok_rows,error_rows,report_json)
                VALUES(@f,@pn,@t,@tr,@ok,@er,@rj)",
                ("@f", Path.GetFileName(opt.FilePath)), ("@pn", opt.PaperName), ("@t", now),
                ("@tr", (long)opt.Rows.Count), ("@ok", (long)okRows.Count),
                ("@er", (long)(opt.Rows.Count - okRows.Count)), ("@rj", "[]"));
            report.BatchId = batchId;

            double totalScore = okRows.Sum(q => q.Score ?? 0);
            long paperId = Db.TxInsert(tx, @"INSERT INTO papers(name,subject_id,duration_minutes,total_score,source,import_batch_id,created_at)
                VALUES(@n,@s,@d,@ts,'import',@b,@t)",
                ("@n", opt.PaperName), ("@s", opt.SubjectId),
                ("@d", opt.DurationMinutes.HasValue ? (object)(long)opt.DurationMinutes.Value : DBNull.Value),
                ("@ts", totalScore), ("@b", batchId), ("@t", now));
            report.PaperId = paperId;

            foreach (var pq in okRows)
            {
                string stem = pq.Stem, analysis = pq.Analysis ?? "", caseMat = pq.CaseMaterial;
                var optionTexts = pq.Options.ToList();

                // {{img:}} 内联占位：把可解析的文件入库并改写为库内文件名
                stem = InlineToStored(stem, pq, imgDirs);
                analysis = InlineToStored(analysis, pq, imgDirs);
                if (caseMat != null) caseMat = InlineToStored(caseMat, pq, imgDirs);
                for (int i = 0; i < optionTexts.Count; i++)
                    optionTexts[i] = InlineToStored(optionTexts[i], pq, imgDirs);

                long qid = Db.TxInsert(tx, @"INSERT INTO questions(paper_id,subject_id,chapter_id,qtype,case_material,stem,answer,analysis,score,sort_no,orig_type)
                    VALUES(@p,@s,@c,@t,@cm,@st,@a,@an,@sc,@no,@ot)",
                    ("@p", paperId), ("@s", opt.SubjectId),
                    ("@c", DBNull.Value), // 章节体系已按需求方要求移除
                    ("@t", pq.Type),
                    ("@cm", (object)caseMat ?? DBNull.Value),
                    ("@st", stem), ("@a", pq.Answer),
                    ("@an", (object)pq.Analysis ?? DBNull.Value),
                    ("@sc", pq.Score ?? 1), ("@no", (long)pq.RowIndex),
                    ("@ot", (object)pq.OrigType ?? DBNull.Value));

                for (int i = 0; i < optionTexts.Count; i++)
                {
                    Db.TxExec(tx, "INSERT INTO options(question_id,label,content,sort_no) VALUES(@q,@l,@c,@n)",
                        ("@q", qid), ("@l", ((char)('A' + i)).ToString()), ("@c", optionTexts[i]), ("@n", (long)i));
                }

                AttachImages(tx, qid, pq.StemImages, "stem", null, imgDirs, pq);
                AttachImages(tx, qid, pq.AnalysisImages, "analysis", null, imgDirs, pq);
                foreach (var kv in pq.OptionImages)
                    AttachImages(tx, qid, kv.Value, "option", kv.Key, imgDirs, pq);
            }

            report.OkRows = okRows.Count;
            report.ErrorRows = opt.Rows.Count - okRows.Count;
            report.ErrorRowsDetail = opt.Rows.Where(r => !r.Ok).ToList();
        });

        Db.Exec("UPDATE import_batches SET report_json=@r WHERE id=@i",
            ("@r", JsonSerializer.Serialize(report.ErrorRowsDetail.Select(r => new { r.RowIndex, r.Reason }).ToList())),
            ("@i", report.BatchId));
        return report;
    }

    /// <summary>{{img:名称}} → 若能定位文件则入库并改写为 {{img:@STORED@sha.ext}}，否则保留原样（展示为缺图占位）。</summary>
    private static string InlineToStored(string text, ParsedQuestion pq, List<string> dirs)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{img:", StringComparison.OrdinalIgnoreCase)) return text;
        var stored = new Dictionary<string, string>();
        foreach (Match mo in ImgPlaceholder.Matches(text))
        {
            string name = mo.Groups[1].Value.Trim();
            if (stored.ContainsKey(name)) continue;
            string path = ResolveImage(name, dirs) ?? (ImageStore.ExistsByFileName(name) ? ImageStore.PathOfFileName(name) : null);
            if (path == null) { pq.Warnings.Add($"内联图片缺失：{name}"); continue; }
            try
            {
                var st = ImageStore.StoreFile(path);
                stored[name] = StoredPrefix + st.FileName;
            }
            catch (Exception ex) { pq.Warnings.Add($"内联图片 {name} 导入失败：{ex.Message}"); }
        }
        return ImgPlaceholder.Replace(text, mo =>
        {
            string name = mo.Groups[1].Value.Trim();
            return stored.TryGetValue(name, out var s) ? $"{{{{img:{s}}}}}" : mo.Value;
        });
    }

    private static void AttachImages(SqliteTransaction tx, long qid, List<string> files, string scope, string optionLabel, List<string> dirs, ParsedQuestion pq)
    {
        int n = 0;
        foreach (var raw in files)
        {
            string path = raw;
            if (!File.Exists(path) && ImageStore.ExistsByFileName(path)) path = ImageStore.PathOfFileName(path);
            if (!File.Exists(path)) continue;
            try
            {
                var st = ImageStore.StoreFile(path);
                ImageStore.AddRef(tx, st.ImageId, qid, scope, optionLabel, n++);
                if (st.Warning != null) pq.Warnings.Add(st.Warning);
            }
            catch (Exception ex)
            {
                pq.Warnings.Add($"图片 {Path.GetFileName(path)} 导入失败：{ex.Message}");
            }
        }
    }

    /// <summary>导出错误报告 CSV（§5.6 完成页），UTF-8 带 BOM。</summary>
    public static string ExportErrorReport(IEnumerable<RowResult> errors, string savePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("行号,错误原因");
        foreach (var e in errors)
            sb.AppendLine($"{e.RowIndex},\"{(e.Reason ?? "").Replace("\"", "\"\"")}\"");
        File.WriteAllText(savePath, sb.ToString(), new UTF8Encoding(true));
        return savePath;
    }

    // ---------- 小工具 ----------

    private static string GetStr(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
        return null;
    }

    private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static int? TryGetInt(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        return e.TryGetProperty(name, out var v) && v.TryGetInt32(out int i) ? i : null;
    }
}
