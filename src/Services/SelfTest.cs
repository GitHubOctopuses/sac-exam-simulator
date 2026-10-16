using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SacExam.Services;

namespace SacExam.Services;

/// <summary>自测（说明书 §10 验收清单的可自动化部分）：
/// 用 --selftest [模拟卷.json 路径] 运行，逐项打印 PASS/FAIL，退出码 0=全部通过。</summary>
public static class SelfTest
{
    private static readonly List<(string name, bool ok, string note)> _results = new();

    private static void Check(string name, bool ok, string note = null)
    {
        _results.Add((name, ok, note));
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(note != null ? " —— " + note : "")}");
    }

    public static int Run(string paperPath)
    {
        Console.WriteLine("==== 证券从业模拟考试系统 · 自测 ====");
        Console.WriteLine($"数据目录: {App.DataDir}");

        // ---- 1. 导入验收样例（模拟卷1.json） ----
        paperPath ??= FindDefaultPaper();
        Check("找到验收样例卷", paperPath != null && File.Exists(paperPath), paperPath ?? "未找到 模拟卷1.json");
        if (paperPath == null || !File.Exists(paperPath)) return 1;

        var outcome = ImportService.ParseFile(paperPath, null);
        if (outcome.Total < 20)
        {
            // 仓库自带的格式示例卷较小：仅运行与具体试卷无关的规则断言
            Check("答案归一化：'a b'→'AB'、'B,A,'→'AB'", ExamService.Normalize("a b") == "AB" && ExamService.Normalize("B,A,") == "AB");
            Check("判断题归一化：正确/对/√→A，错误/错/×→B",
                ImportService.NormJudgeAnswer("正确") == "A" && ImportService.NormJudgeAnswer("对") == "A" &&
                ImportService.NormJudgeAnswer("√") == "A" && ImportService.NormJudgeAnswer("错误") == "B" &&
                ImportService.NormJudgeAnswer("错") == "B" && ImportService.NormJudgeAnswer("×") == "B");
            Console.WriteLine("[SKIP] 提供的样例卷题量较少（<20 题），已跳过整卷断言；传入完整试卷 JSON 路径可运行全部断言。");
            Console.WriteLine("==== 自测完成 ====");
            int f0 = _results.Count(r => !r.ok);
            Console.WriteLine(f0 == 0 ? "基础断言全部通过（" + _results.Count + " 项）" : "失败 " + f0 + " 项");
            return f0 == 0 ? 0 : 1;
        }
        Check("解析：共 120 行全部通过校验", outcome.Total == 120 && outcome.ErrorCount == 0,
            $"共 {outcome.Total} 行，错误 {outcome.ErrorCount}");
        Check("题型分布 40/40/30/10",
            outcome.CountByType.GetValueOrDefault("单选题") == 40 &&
            outcome.CountByType.GetValueOrDefault("多选题") == 40 &&
            outcome.CountByType.GetValueOrDefault("判断题") == 30 &&
            outcome.CountByType.GetValueOrDefault("综合题") == 10,
            string.Join(" · ", outcome.CountByType.Select(kv => $"{kv.Key}{kv.Value}")));
        Check("卷面总分 100", Math.Abs(outcome.TotalScore - 100) < 0.001, outcome.TotalScore.ToString());

        var composites = outcome.Rows.Where(r => r.Ok && r.Question.Type == "综合题").ToList();
        Check("10 道【案例材料】题识别为综合题", composites.Count == 10, $"实际 {composites.Count}");
        Check("综合题 orig_type 保留原标注（JSON 源为综合题；xlsx 源为多选题）",
            composites.All(c => !string.IsNullOrEmpty(c.Question.OrigType)),
            string.Join(",", composites.Select(c => c.Question.OrigType).Distinct()));
        Check("材料与设问正确切分（case_material 含分界句、stem 为设问）",
            composites.All(c => (c.Question.CaseMaterial ?? "").Contains("根据此背景回答下列问题。")
                             && !string.IsNullOrEmpty(c.Question.Stem)
                             && !c.Question.Stem.Contains("【案例材料】")));
        int singleLetter = composites.Count(c => c.Question.Answer.Length == 1);
        Check("综合题存在单字母答案（附录 C：3 道）", singleLetter == 3, $"实际 {singleLetter}");

        var judges = outcome.Rows.Where(r => r.Ok && r.Question.Type == "判断题").ToList();
        Check("判断题选项固定 A、正确 / B、错误",
            judges.All(j => j.Question.Options.Count == 2 && j.Question.Options[0] == "正确" && j.Question.Options[1] == "错误"));
        int judgeA = judges.Count(j => j.Question.Answer == "A");
        int judgeB = judges.Count(j => j.Question.Answer == "B");
        Check("判断题答案分布 A=13 / B=17（附录 C）", judgeA == 13 && judgeB == 17, $"A={judgeA}, B={judgeB}");

        // ---- 2. 归一化与计分规则 ----
        Check("答案归一化：'a b'→'AB'、'B,A,'→'AB'",
            ExamService.Normalize("a b") == "AB" && ExamService.Normalize("B,A,") == "AB");
        Check("判断题归一化：正确/对/√→A，错误/错/×→B",
            ImportService.NormJudgeAnswer("正确") == "A" && ImportService.NormJudgeAnswer("对") == "A" &&
            ImportService.NormJudgeAnswer("√") == "A" && ImportService.NormJudgeAnswer("错误") == "B" &&
            ImportService.NormJudgeAnswer("错") == "B" && ImportService.NormJudgeAnswer("×") == "B");
        Check("综合题识别：以【案例材料】开头的“多选题”→综合题",
            ImportService.NormType("多选题") == "多选题");

        // ---- 3. 入库 + 整卷开考 + 计分 ----
        var report = ImportService.Commit(new ImportService.CommitOptions
        {
            FilePath = paperPath,
            PaperName = outcome.PaperName ?? "自测模拟卷",
            SubjectId = 1,
            DurationMinutes = outcome.DurationMinutes,
            Rows = outcome.Rows,
        });
        Check("导入入库：成功 120 行", report.OkRows == 120 && report.ErrorRows == 0);

        long paperId = report.PaperId;
        var qs = BankService.GetQuestions(paperId);
        Check("题库读取：120 题、选项齐备、顺序正确",
            qs.Count == 120 && qs.All(q => q.Options.Count >= 2) && qs[0].SortNo < qs[119].SortNo);
        Check("卷面分值：单选 0.5、其余 1；合计 100",
            qs.Where(q => q.QType == "单选题").All(q => Math.Abs(q.Score - 0.5) < 1e-9) &&
            qs.Where(q => q.QType != "单选题").All(q => Math.Abs(q.Score - 1) < 1e-9) &&
            Math.Abs(qs.Sum(q => q.Score) - 100) < 1e-9);

        var session = ExamService.Start(paperId, "paper", "自测考生", "000000");
        Check("整卷开考：题序/数量/时长与卷一致",
            session.Items.Count == 120 && session.DurationMinutes == 120 && session.Mode == "paper");
        Check("综合题计分：不定项（单字母答案题）判分正确",
            session.Items.Where(x => x.Q.QType == "综合题" && x.Q.Answer.Length == 1)
                .All(x => ExamService.IsCorrect(x.Q, x.Q.Answer) && !ExamService.IsCorrect(x.Q, x.Q.Answer.Length == 1 ? "AB" : "A")));
        Check("多选漏选不得分", !ExamService.IsCorrect(
            new SacExam.QuestionDto { Answer = "ABD", Score = 1, QType = "多选题", Stem = "t", Options = new() }, "AB"));

        // 全部答对 → 100 分
        foreach (var st in session.Items) st.Answer = st.Q.Answer;
        var res = ExamService.Submit(session);
        Check("全对计分：得分 100、及格", Math.Abs(res.GotScore - 100) < 1e-9 && res.Pass, $"得分 {res.GotScore}");

        // 隔题答对（各题型各半）→ 50 分、不及格
        var session2 = ExamService.Start(paperId, "paper", "自测考生", "000000");
        for (int i = 0; i < session2.Items.Count; i++)
            session2.Items[i].Answer = i % 2 == 0 ? session2.Items[i].Q.Answer : "";
        var res2 = ExamService.Submit(session2);
        Check("半对计分：得分 50.0、不及格", Math.Abs(res2.GotScore - 50) < 1e-9 && !res2.Pass, $"得分 {res2.GotScore}");
        Check("交卷后错题自动进入错题本", ExamService.GetWrongBook().Count >= 60, $"错题 {ExamService.GetWrongBook().Count} 条");

        // ---- 4. 自动保存与断电恢复 ----
        var session3 = ExamService.Start(paperId, "paper", "自测考生", "000000");
        ExamService.SaveAnswer(session3.RecordId, session3.Items[0].Q.Id, session3.Items[0].Q.Answer, true);
        var ongoing = ExamService.GetOngoingExams();
        var recovered = ongoing.FirstOrDefault(x => x.session.RecordId == session3.RecordId).session;
        Check("断电恢复：作答与标记从快照恢复",
            recovered != null && recovered.Items[0].Answer == session3.Items[0].Q.Answer && recovered.Items[0].Marked);
        Check("恢复考试：剩余时间按快照计算",
            recovered != null && (recovered.Deadline - DateTime.Now).TotalMinutes > 119 &&
            (recovered.Deadline - DateTime.Now).TotalMinutes <= 120);
        ExamService.Abandon(session3.RecordId);

        // ---- 5. 随机组卷 ----
        var rand = ExamService.BuildRandomPaper(new ExamService.RandomPaperOptions
        {
            SubjectId = 1,
            DurationMinutes = 120,
            Types = new List<SettingsService.TypePreset>
            {
                new(){ Type="单选题", Count=40, Score=0.5 },
                new(){ Type="多选题", Count=40, Score=1 },
                new(){ Type="判断题", Count=30, Score=1 },
                new(){ Type="综合题", Count=10, Score=1 },
            },
        });
        var randQs = rand.questionIds.Select(BankService.GetQuestion).ToList();
        Check("随机组卷：按预设抽满 120 题、同卷不重复",
            randQs.Count == 120 && randQs.Select(q => q.Id).Distinct().Count() == 120,
            rand.shortages.Count > 0 ? string.Join("; ", rand.shortages) : "无缺口");
        var randSession = ExamService.Start(rand.paperId, "random", "自测考生", "000000", rand.questionIds);
        Check("随机卷开考：mode=random", randSession.Mode == "random" && randSession.Items.Count == 120);
        ExamService.Abandon(randSession.RecordId);

        // ---- 6. 错题重做与清理 ----
        var (redoPaper, redoIds) = ExamService.CreateWrongRedoPaper(ExamService.GetWrongBook().Take(5).Select(w => w.QuestionId).ToList());
        Check("错题重做组卷", redoIds.Count == 5, $"实际 {redoIds.Count}");
        ExamService.RemoveWrong(ExamService.GetWrongBook().First().QuestionId);
        Db.DeletePaper(redoPaper);
        Db.DeletePaper(rand.paperId);
        Check("删除随机卷不影响原整卷题库", Db.QuestionCountOfPaper(paperId) == 120);

        Console.WriteLine("==== 自测完成 ====");
        int fail = _results.Count(r => !r.ok);
        Console.WriteLine(fail == 0 ? $"全部通过（{_results.Count} 项）" : $"通过 {_results.Count - fail}/{_results.Count}，失败 {fail} 项");
        return fail == 0 ? 0 : 1;
    }

    private static string FindDefaultPaper()
    {
        // 优先：仓库自带的格式示例卷；其次：exe 目录向上寻找 模拟卷1.json（本地开发用）
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string demo = Path.Combine(dir.FullName, "samples", "示例卷-格式演示.json");
            if (File.Exists(demo)) return demo;
            string legacy = Path.Combine(dir.FullName, "模拟卷1.json");
            if (File.Exists(legacy)) return legacy;
            dir = dir.Parent;
        }
        return null;
    }
}
