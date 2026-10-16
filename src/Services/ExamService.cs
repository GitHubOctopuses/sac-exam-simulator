using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SacExam.Services;

/// <summary>题库查询：试卷题目、选项、图片引用装载。</summary>
public static class BankService
{
    public static List<QuestionDto> GetQuestions(long paperId)
    {
        var list = new List<QuestionDto>();
        foreach (var r in Db.Rows("SELECT * FROM questions WHERE paper_id=@p ORDER BY sort_no, id", ("@p", paperId)))
        {
            var q = Map(r);
            q.Options = Db.Rows("SELECT * FROM options WHERE question_id=@q ORDER BY sort_no", ("@q", q.Id))
                .Select(o => new OptionDto { Label = (string)o["label"], Content = (string)o["content"] })
                .ToList();
            ImageStore.LoadRefsInto(q);
            list.Add(q);
        }
        return list;
    }

    public static QuestionDto GetQuestion(long questionId)
    {
        foreach (var r in Db.Rows("SELECT * FROM questions WHERE id=@q", ("@q", questionId)))
        {
            var q = Map(r);
            q.Options = Db.Rows("SELECT * FROM options WHERE question_id=@q ORDER BY sort_no", ("@q", q.Id))
                .Select(o => new OptionDto { Label = (string)o["label"], Content = (string)o["content"] })
                .ToList();
            ImageStore.LoadRefsInto(q);
            return q;
        }
        return null;
    }

    private static QuestionDto Map(Dictionary<string, object> r) => new()
    {
        Id = (long)r["id"],
        PaperId = (long)r["paper_id"],
        SubjectId = (long)r["subject_id"],
        QType = (string)r["qtype"],
        CaseMaterial = r["case_material"] as string,
        Stem = (string)r["stem"],
        Answer = (string)r["answer"],
        Analysis = r["analysis"] as string,
        Score = Convert.ToDouble(r["score"]),
        SortNo = (int)(long)r["sort_no"],
        OrigType = r["orig_type"] as string,
    };

    public static int CountByType(long subjectId, string qtype) =>
        Db.Scalar<int>("SELECT COUNT(*) FROM questions WHERE subject_id=@s AND qtype=@t", ("@s", subjectId), ("@t", qtype));

    public static int CountBySubject(long subjectId) =>
        Db.Scalar<int>("SELECT COUNT(*) FROM questions WHERE subject_id=@s", ("@s", subjectId));

    public static string PaperName(long paperId) =>
        Db.Scalar<string>("SELECT name FROM papers WHERE id=@p", ("@p", paperId));
}

/// <summary>考试流程：开始/作答自动保存/交卷评分/恢复快照（说明书 §6）。</summary>
public static class ExamService
{
    private class Snapshot
    {
        public List<long> QuestionIds { get; set; }
        public DateTime StartedAt { get; set; }
        public int DurationMinutes { get; set; }
        public string ExamineeName { get; set; }
        public string ExamineeNo { get; set; }
        public string PaperName { get; set; }
    }

    // ---------- 计分（§6.3） ----------

    public static string Normalize(string given)
    {
        if (string.IsNullOrEmpty(given)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (char c in given.ToUpperInvariant())
            if (c >= 'A' && c <= 'H') sb.Append(c);
        var chars = sb.ToString().Distinct().OrderBy(c => c).ToArray();
        return new string(chars);
    }

    /// <summary>把答案字符串转为「单字母字符串集合」（选项 Label 比较）。</summary>
    public static HashSet<string> LetterSet(string answer) =>
        Normalize(answer).Select(c => c.ToString()).ToHashSet();

    public static bool IsCorrect(QuestionDto q, string given)
    {
        string g = Normalize(given);
        if (g.Length == 0) return false;
        return g == Normalize(q.Answer);
    }

    // ---------- 随机组卷 ----------

    public class RandomPaperOptions
    {
        public long SubjectId;
        public int DurationMinutes;
        public List<SettingsService.TypePreset> Types; // 题型/数量（分值沿用题型预设）
    }

    public static (long paperId, List<long> questionIds, List<string> shortages) BuildRandomPaper(RandomPaperOptions opt)
    {
        string paperName = $"随机练习卷 · {Db.SubjectName(opt.SubjectId)} · {DateTime.Now:yyyyMMdd-HHmmss}";
        long paperId = Db.Insert(@"INSERT INTO papers(name,subject_id,duration_minutes,total_score,source,created_at)
            VALUES(@n,@s,@d,0,'random',@t)",
            ("@n", paperName), ("@s", opt.SubjectId), ("@d", (long)opt.DurationMinutes),
            ("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

        // 随机快照题不落 questions 表（§4）：题目保留原卷归属，抽题结果存于考试快照 detail_json
        var ids = new List<long>();
        var shortages = new List<string>();
        double total = 0;
        foreach (var t in opt.Types)
        {
            string sql = "SELECT id FROM questions WHERE subject_id=@s AND qtype=@t ORDER BY RANDOM() LIMIT @n";
            var picked = Db.Rows(sql, ("@s", opt.SubjectId), ("@t", t.Type), ("@n", (long)t.Count));
            if (picked.Count < t.Count)
                shortages.Add($"{t.Type} 题库仅 {picked.Count} 题（需求 {t.Count}）");
            foreach (var r in picked)
            {
                ids.Add((long)r["id"]);
                total += t.Score;
            }
        }
        Db.Exec("UPDATE papers SET total_score=@ts WHERE id=@p", ("@ts", total), ("@p", paperId));
        return (paperId, ids, shortages);
    }

    // ---------- 开始 / 保存 / 恢复 ----------

    public static ExamSession Start(long paperId, string mode, string examineeName, string examineeNo, List<long> randomQuestionIds = null)
    {
        var paper = Db.Rows("SELECT * FROM papers WHERE id=@p", ("@p", paperId)).FirstOrDefault()
            ?? throw new ArgumentException("试卷不存在");
        List<QuestionDto> questions;
        if (mode == "random" && randomQuestionIds is { Count: > 0 })
        {
            questions = randomQuestionIds.Select(BankService.GetQuestion).Where(q => q != null).ToList();
        }
        else
        {
            questions = BankService.GetQuestions(paperId);
        }
        if (questions.Count == 0) throw new ArgumentException("该试卷没有题目");

        int duration = paper["duration_minutes"] == null
            ? Db.GetSubject((long)paper["subject_id"]).DurationMinutes
            : (int)(long)paper["duration_minutes"];
        // 随机卷 duration 可能为 0（未更新总分前创建）→ 兜底科目默认
        if (duration <= 0) duration = Db.GetSubject((long)paper["subject_id"]).DurationMinutes;

        var now = DateTime.Now;
        var snap = new Snapshot
        {
            QuestionIds = questions.Select(q => q.Id).ToList(),
            StartedAt = now,
            DurationMinutes = duration,
            ExamineeName = examineeName,
            ExamineeNo = examineeNo,
            PaperName = (string)paper["name"],
        };

        long recordId = Db.Insert(@"INSERT INTO exam_records(paper_id,subject_id,mode,examinee_name,started_at,total_score,detail_json,status)
            VALUES(@p,@s,@m,@en,@st,@ts,@dj,'ongoing')",
            ("@p", paperId), ("@s", (long)paper["subject_id"]), ("@m", mode),
            ("@en", (object)examineeName ?? DBNull.Value), ("@st", now.ToString("yyyy-MM-dd HH:mm:ss")),
            ("@ts", Convert.ToDouble(paper["total_score"] ?? 0)),
            ("@dj", JsonSerializer.Serialize(snap)));

        foreach (var q in questions)
            Db.Exec(@"INSERT OR IGNORE INTO exam_answers(record_id,question_id,answer,marked) VALUES(@r,@q,'',0)",
                ("@r", recordId), ("@q", q.Id));

        return new ExamSession
        {
            RecordId = recordId,
            PaperId = paperId,
            PaperName = snap.PaperName,
            SubjectId = (long)paper["subject_id"],
            SubjectName = Db.SubjectName((long)paper["subject_id"]),
            Mode = mode,
            ExamineeName = examineeName,
            ExamineeNo = examineeNo,
            Items = questions.Select(q => new ExamQuestionState { Q = q }).ToList(),
            StartedAt = now,
            DurationMinutes = duration,
            Deadline = now.AddMinutes(duration),
        };
    }

    /// <summary>作答/标记即时落库（真实机考口径：选择选项的同时自动保存答案）。</summary>
    public static void SaveAnswer(long recordId, long questionId, string answer, bool marked)
    {
        Db.Exec(@"UPDATE exam_answers SET answer=@a, marked=@m WHERE record_id=@r AND question_id=@q",
            ("@a", (object)answer ?? ""), ("@m", marked ? 1L : 0L), ("@r", recordId), ("@q", questionId));
    }

    public static void SaveMark(long recordId, long questionId, bool marked)
    {
        Db.Exec(@"UPDATE exam_answers SET marked=@m WHERE record_id=@r AND question_id=@q",
            ("@m", marked ? 1L : 0L), ("@r", recordId), ("@q", questionId));
    }

    public static List<(ExamSession session, int answered)> GetOngoingExams()
    {
        var list = new List<(ExamSession, int)>();
        foreach (var r in Db.Rows("SELECT * FROM exam_records WHERE status='ongoing' ORDER BY id"))
        {
            var s = LoadOngoing(r);
            if (s != null)
            {
                int answered = Db.Scalar<int>(
                    "SELECT COUNT(*) FROM exam_answers WHERE record_id=@r AND answer IS NOT NULL AND answer != ''",
                    ("@r", s.RecordId));
                list.Add((s, answered));
            }
        }
        return list;
    }

    private static ExamSession LoadOngoing(Dictionary<string, object> r)
    {
        try
        {
            var snap = JsonSerializer.Deserialize<Snapshot>((string)r["detail_json"]);
            if (snap == null) return null;
            var byId = new Dictionary<long, QuestionDto>();
            var items = new List<ExamQuestionState>();
            foreach (long qid in snap.QuestionIds)
            {
                var q = BankService.GetQuestion(qid);
                if (q == null) continue;
                byId[qid] = q;
                items.Add(new ExamQuestionState { Q = q });
            }
            if (items.Count == 0) return null;

            var answers = Db.Rows("SELECT * FROM exam_answers WHERE record_id=@r", ("@r", (long)r["id"]));
            foreach (var a in answers)
            {
                long qid = (long)a["question_id"];
                if (!byId.TryGetValue(qid, out var q)) continue;
                var st = items.First(x => x.Q.Id == qid);
                st.Answer = a["answer"] as string ?? "";
                st.Marked = Convert.ToInt64(a["marked"] ?? 0) == 1;
            }

            return new ExamSession
            {
                RecordId = (long)r["id"],
                PaperId = (long)r["paper_id"],
                PaperName = snap.PaperName ?? "",
                SubjectId = (long)r["subject_id"],
                SubjectName = Db.SubjectName((long)r["subject_id"]),
                Mode = (string)r["mode"],
                ExamineeName = snap.ExamineeName,
                ExamineeNo = snap.ExamineeNo,
                Items = items,
                StartedAt = snap.StartedAt,
                DurationMinutes = snap.DurationMinutes,
                Deadline = snap.StartedAt.AddMinutes(snap.DurationMinutes),
            };
        }
        catch { return null; }
    }

    public static void Abandon(long recordId) =>
        Db.Exec("UPDATE exam_records SET status='abandoned', finished_at=@f WHERE id=@r",
            ("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), ("@r", recordId));

    // ---------- 交卷评分 ----------

    public static ExamResultDto Submit(ExamSession s, bool removeFromWrongOnCorrect = false)
    {
        double totalScore = s.Items.Sum(x => x.Q.Score);
        double got = 0;
        int correct = 0;
        var byType = new Dictionary<string, TypeScoreDto>();

        Db.Transaction(tx =>
        {
            foreach (var st in s.Items)
            {
                bool ok = IsCorrect(st.Q, st.Answer);
                st.Correct = ok;
                st.GotScore = ok ? st.Q.Score : 0;
                if (ok) { got += st.Q.Score; correct++; }

                if (!byType.TryGetValue(st.Q.QType, out var ts))
                    byType[st.Q.QType] = ts = new TypeScoreDto { Type = st.Q.QType };
                ts.Count++;
                ts.FullScore += st.Q.Score;
                if (ok) { ts.CorrectCount++; ts.Score += st.Q.Score; }

                Db.TxExec(tx, @"UPDATE exam_answers SET correct=@c, score=@sc WHERE record_id=@r AND question_id=@q",
                    ("@c", ok ? 1L : 0L), ("@sc", (double)st.GotScore), ("@r", s.RecordId), ("@q", st.Q.Id));

                // 错题本（§6.3：交卷后错题自动进入错题本，记录错答）
                if (!ok)
                {
                    Db.TxExec(tx, @"INSERT INTO wrong_book(question_id,wrong_count,last_wrong_at,last_answer)
                        VALUES(@q,1,@t,@a)
                        ON CONFLICT(question_id) DO UPDATE SET wrong_count=wrong_count+1, last_wrong_at=@t, last_answer=@a",
                        ("@q", st.Q.Id), ("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), ("@a", (object)st.Answer ?? ""));
                }
                else if (removeFromWrongOnCorrect)
                {
                    Db.TxExec(tx, "DELETE FROM wrong_book WHERE question_id=@q", ("@q", st.Q.Id));
                }
            }

            double usedSec = (DateTime.Now - s.StartedAt).TotalSeconds;
            double passRate = Db.GetSubject(s.SubjectId)?.PassRate ?? SettingsService.Get().PassRate;
            bool pass = totalScore > 0 && got >= totalScore * passRate;

            Db.TxExec(tx, @"UPDATE exam_records SET finished_at=@f, duration_used=@du, total_score=@ts,
                        score=@sc, pass=@p, status='finished' WHERE id=@r",
                ("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                ("@du", (long)usedSec), ("@ts", totalScore), ("@sc", got),
                ("@p", pass ? 1L : 0L), ("@r", s.RecordId));

            s.Finished = true;
        });

        return new ExamResultDto
        {
            RecordId = s.RecordId,
            TotalScore = totalScore,
            GotScore = Math.Round(got, 1),
            Pass = totalScore > 0 && got >= totalScore * (Db.GetSubject(s.SubjectId)?.PassRate ?? 0.6),
            QuestionCount = s.Items.Count,
            CorrectCount = correct,
            UsedSeconds = (DateTime.Now - s.StartedAt).TotalSeconds,
            ByType = s.Items.GroupBy(x => x.Q.QType)
                .Select(g => new TypeScoreDto
                {
                    Type = g.Key,
                    Count = g.Count(),
                    CorrectCount = g.Count(x => x.Correct),
                    Score = g.Sum(x => x.GotScore),
                    FullScore = g.Sum(x => x.Q.Score),
                }).ToList(),
            Items = s.Items,
        };
    }

    // ---------- 历史记录 ----------

    public class HistoryRow
    {
        public long RecordId;
        public long PaperId;
        public string PaperName;
        public string SubjectName;
        public string Mode;
        public string ExamineeName;
        public string StartedAt;
        public string FinishedAt;
        public int? DurationUsed;
        public double? TotalScore;
        public double? Score;
        public bool? Pass;
        public string Status;
        public int QuestionCount;
    }

    public static List<HistoryRow> GetHistory()
    {
        var list = new List<HistoryRow>();
        foreach (var r in Db.Rows(@"SELECT er.*, p.name AS paper_name, s.name AS subject_name,
                 (SELECT COUNT(*) FROM exam_answers ea WHERE ea.record_id=er.id) AS qc
                 FROM exam_records er JOIN papers p ON p.id=er.paper_id JOIN subjects s ON s.id=er.subject_id
                 ORDER BY er.id DESC"))
        {
            list.Add(new HistoryRow
            {
                RecordId = (long)r["id"],
                PaperId = (long)r["paper_id"],
                PaperName = (string)r["paper_name"],
                SubjectName = (string)r["subject_name"],
                Mode = (string)r["mode"],
                ExamineeName = r["examinee_name"] as string,
                StartedAt = (string)r["started_at"],
                FinishedAt = r["finished_at"] as string,
                DurationUsed = r["duration_used"] == null ? null : (int)(long)r["duration_used"],
                TotalScore = r["total_score"] == null ? null : Convert.ToDouble(r["total_score"]),
                Score = r["score"] == null ? null : Convert.ToDouble(r["score"]),
                Pass = r["pass"] == null ? null : Convert.ToInt64(r["pass"]) == 1,
                Status = (string)r["status"],
                QuestionCount = (int)(long)r["qc"],
            });
        }
        return list;
    }

    public static void DeleteHistory(long recordId) =>
        Db.Exec("DELETE FROM exam_records WHERE id=@r", ("@r", recordId));

    /// <summary>从历史记录装载已完成的答题明细（解析回放用）。</summary>
    public static ExamResultDto LoadFinished(long recordId)
    {
        var r = Db.Rows("SELECT * FROM exam_records WHERE id=@r", ("@r", recordId)).FirstOrDefault();
        if (r == null) return null;
        var snap = JsonSerializer.Deserialize<Snapshot>((string)r["detail_json"]);
        var items = new List<ExamQuestionState>();
        foreach (long qid in snap.QuestionIds)
        {
            var q = BankService.GetQuestion(qid);
            if (q == null) continue;
            items.Add(new ExamQuestionState { Q = q });
        }
        var answers = Db.Rows("SELECT * FROM exam_answers WHERE record_id=@r", ("@r", recordId));
        foreach (var a in answers)
        {
            long qid = (long)a["question_id"];
            var st = items.FirstOrDefault(x => x.Q.Id == qid);
            if (st == null) continue;
            st.Answer = a["answer"] as string ?? "";
            st.Marked = Convert.ToInt64(a["marked"] ?? 0) == 1;
            st.Correct = a["correct"] != null && Convert.ToInt64(a["correct"]) == 1;
            st.GotScore = a["score"] == null ? 0 : Convert.ToDouble(a["score"]);
        }
        int durationMin = snap?.DurationMinutes ?? 120;
        return new ExamResultDto
        {
            RecordId = recordId,
            TotalScore = Convert.ToDouble(r["total_score"] ?? 0),
            GotScore = Convert.ToDouble(r["score"] ?? 0),
            Pass = r["pass"] != null && Convert.ToInt64(r["pass"]) == 1,
            QuestionCount = items.Count,
            CorrectCount = items.Count(x => x.Correct),
            UsedSeconds = r["duration_used"] == null ? 0 : (double)(long)r["duration_used"],
            ByType = items.GroupBy(x => x.Q.QType)
                .Select(g => new TypeScoreDto
                {
                    Type = g.Key,
                    Count = g.Count(),
                    CorrectCount = g.Count(x => x.Correct),
                    Score = g.Sum(x => x.GotScore),
                    FullScore = g.Sum(x => x.Q.Score),
                }).ToList(),
            Items = items,
        };
    }

    // ---------- 错题本 ----------

    public class WrongRow
    {
        public long QuestionId;
        public int WrongCount;
        public string LastWrongAt;
        public string LastAnswer;
        public QuestionDto Q;
    }

    public static List<WrongRow> GetWrongBook(long? subjectId = null, string qtype = null)
    {
        string sql = @"SELECT wb.*, s.id AS sid FROM wrong_book wb
                       JOIN questions q ON q.id=wb.question_id JOIN subjects s ON s.id=q.subject_id WHERE 1=1";
        var ps = new List<(string, object)>();
        if (subjectId.HasValue) { sql += " AND q.subject_id=@s"; ps.Add(("@s", subjectId.Value)); }
        if (!string.IsNullOrEmpty(qtype)) { sql += " AND q.qtype=@t"; ps.Add(("@t", qtype)); }
        sql += " ORDER BY wb.last_wrong_at DESC";

        var list = new List<WrongRow>();
        foreach (var r in Db.Rows(sql, ps.ToArray()))
        {
            var q = BankService.GetQuestion((long)r["question_id"]);
            if (q == null) continue; // 题目已被删除
            list.Add(new WrongRow
            {
                QuestionId = q.Id,
                WrongCount = (int)(long)r["wrong_count"],
                LastWrongAt = r["last_wrong_at"] as string,
                LastAnswer = r["last_answer"] as string,
                Q = q,
            });
        }
        return list;
    }

    public static void RemoveWrong(long questionId) =>
        Db.Exec("DELETE FROM wrong_book WHERE question_id=@q", ("@q", questionId));

    public static void ClearWrongBook(long? subjectId = null)
    {
        if (subjectId.HasValue)
            Db.Exec("DELETE FROM wrong_book WHERE question_id IN (SELECT id FROM questions WHERE subject_id=@s)", ("@s", subjectId.Value));
        else
            Db.Exec("DELETE FROM wrong_book");
    }

    /// <summary>错题重做：以这些题生成随机卷（mode=random；题目不搬移原卷归属）。</summary>
    public static (long paperId, List<long> questionIds) CreateWrongRedoPaper(List<long> questionIds)
    {
        string name = $"错题重做 · {DateTime.Now:yyyy-MM-dd HH:mm}";
        var ids = new List<long>();
        double total = 0;
        long subjectId = 1;
        foreach (var qid in questionIds)
        {
            var q = BankService.GetQuestion(qid);
            if (q == null) continue;
            if (ids.Count == 0) subjectId = q.SubjectId;
            ids.Add(qid);
            total += q.Score;
        }
        int duration = Math.Max(10, (int)Math.Ceiling(Math.Max(ids.Count, 1) * 1.2));
        long paperId = Db.Insert(@"INSERT INTO papers(name,subject_id,duration_minutes,total_score,source,created_at)
            VALUES(@n,@s,@d,0,'random',@t)",
            ("@n", name), ("@s", subjectId), ("@d", (long)duration),
            ("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        Db.Exec("UPDATE papers SET total_score=@ts WHERE id=@p", ("@ts", total), ("@p", paperId));
        return (paperId, ids);
    }
}
