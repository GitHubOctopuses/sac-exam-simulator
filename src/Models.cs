using System.Collections.Generic;

namespace SacExam;

// ===================== 基础数据（对应说明书 §4 数据模型） =====================

public class SubjectDto
{
    public long Id { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }      // general | special
    public int DurationMinutes { get; set; }
    public double PassRate { get; set; } = 0.6;
    public string CategoryText => Category == "special" ? "专项" : "一般业务";
}

public class PaperDto
{
    public long Id { get; set; }
    public string Name { get; set; }
    public long SubjectId { get; set; }
    public string SubjectName { get; set; }
    public int? DurationMinutes { get; set; } // 空=科目默认
    public double? TotalScore { get; set; }
    public string Source { get; set; }        // import | random | builtin
    public long? ImportBatchId { get; set; }
    public string CreatedAt { get; set; }
    public int QuestionCount { get; set; }
}

public class OptionDto
{
    public string Label { get; set; }   // 'A'..'H'
    public string Content { get; set; }
    public List<string> ImageFiles { get; set; } = new(); // 解析后可直接展示的图片路径
}

public class QuestionDto
{
    public long Id { get; set; }
    public long PaperId { get; set; }
    public long SubjectId { get; set; }
    public string QType { get; set; }         // 单选题/多选题/判断题/综合题
    public string CaseMaterial { get; set; }  // 综合题案例材料（可空）
    public string Stem { get; set; }
    public string Answer { get; set; }        // 规范化字母串 "C"/"ABD"/"A"(判断)
    public string Analysis { get; set; }
    public double Score { get; set; }
    public int SortNo { get; set; }
    public string OrigType { get; set; }
    public List<OptionDto> Options { get; set; } = new();
    public List<string> StemImages { get; set; } = new();
    public List<string> AnalysisImages { get; set; } = new();
}

// ===================== 导入 =====================

public class ParsedQuestion
{
    public int RowIndex;                    // 文件中的行号（1 起，含表头偏移）
    public string Type;                     // 归一化后：单选题/多选题/判断题/综合题
    public string OrigType;                 // 文件原始题型标注
    public string CaseMaterial;
    public string Stem;
    public List<string> Options = new();
    public string Answer;                   // 规范化字母串
    public string Analysis;
    public double? Score;                   // null=留空按预设
    public string Chapter;
    public List<string> StemImages = new();      // 文件名或已入库的绝对路径
    public Dictionary<string, List<string>> OptionImages = new(); // label -> files
    public List<string> AnalysisImages = new();
    public List<string> Warnings = new();
    public string AnswerRaw;                // 文件中的原始答案文本（归一化前）
    public bool FromJson;
}

public class RowResult
{
    public int RowIndex;
    public bool Ok;
    public string Reason;                   // 错误原因（仅错误行）
    public ParsedQuestion Question;         // Ok 时非空
    public string Recognition;              // 识别说明（如 "案例→不定项"、"正常"）
}

public class ParseOutcome
{
    public List<RowResult> Rows = new();
    public int Total => Rows.Count;
    public int OkCount => Rows.Count(r => r.Ok);
    public int ErrorCount => Rows.Count(r => !r.Ok);
    public Dictionary<string, int> CountByType = new();
    public double TotalScore;
    public string PaperSubject;             // JSON 卷自带的科目名（可空）
    public string PaperName;                // JSON 卷自带卷名（可空）
    public int? DurationMinutes;
    public List<string> GlobalWarnings = new();
}

public class ImportReport
{
    public long BatchId;
    public long PaperId;
    public int OkRows;
    public int ErrorRows;
    public List<RowResult> ErrorRowsDetail = new();
}

// ===================== 考试 =====================

public class ExamQuestionState
{
    public QuestionDto Q;
    public string Answer = "";      // 考生作答（规范化字母串，空=未答）
    public bool Marked;
    public bool Correct;            // 交卷后填充
    public double GotScore;
}

public class ExamSession
{
    public long RecordId;
    public long PaperId;
    public string PaperName;
    public long SubjectId;
    public string SubjectName;
    public string Mode;                       // paper | random
    public string ExamineeName;
    public string ExamineeNo;
    public List<ExamQuestionState> Items = new();
    public DateTime StartedAt;
    public DateTime Deadline;
    public int DurationMinutes;
    public bool Finished;
}

public class TypeScoreDto
{
    public string Type;
    public int Count;
    public int CorrectCount;
    public double Score;
    public double FullScore;
}

public class ExamResultDto
{
    public long RecordId;
    public double TotalScore;
    public double GotScore;
    public bool Pass;
    public int QuestionCount;
    public int CorrectCount;
    public double UsedSeconds;
    public List<TypeScoreDto> ByType = new();
    public List<ExamQuestionState> Items = new();
}
