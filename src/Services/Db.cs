using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace SacExam.Services;

/// <summary>SQLite 访问层：建表、预置数据、通用查询助手（单机单线程友好）。</summary>
public static class Db
{
    private static readonly object _lock = new();
    private static SqliteConnection _conn;

    public static string DbPath => App.DbPath;

    public static void Init()
    {
        lock (_lock)
        {
            if (_conn != null) return;
            _conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());
            _conn.Open();
            CreateSchema();
            Seed();
        }
    }

    public static SqliteConnection Conn { get { lock (_lock) { if (_conn == null) Init(); return _conn; } } }

    private static void CreateSchema()
    {
        Exec(@"CREATE TABLE IF NOT EXISTS subjects (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL UNIQUE,
            category TEXT NOT NULL,
            duration_minutes INTEGER NOT NULL,
            pass_rate REAL NOT NULL DEFAULT 0.6)");

        Exec(@"CREATE TABLE IF NOT EXISTS papers (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            subject_id INTEGER NOT NULL REFERENCES subjects(id),
            duration_minutes INTEGER,
            total_score REAL,
            source TEXT NOT NULL DEFAULT 'import',
            import_batch_id INTEGER,
            created_at TEXT NOT NULL)");

        Exec(@"CREATE TABLE IF NOT EXISTS chapters (
            id INTEGER PRIMARY KEY,
            subject_id INTEGER NOT NULL REFERENCES subjects(id),
            sort_no INTEGER NOT NULL,
            name TEXT NOT NULL)");

        Exec(@"CREATE TABLE IF NOT EXISTS questions (
            id INTEGER PRIMARY KEY,
            paper_id INTEGER REFERENCES papers(id),
            subject_id INTEGER NOT NULL REFERENCES subjects(id),
            chapter_id INTEGER REFERENCES chapters(id),
            qtype TEXT NOT NULL CHECK (qtype IN ('单选题','多选题','判断题','综合题')),
            case_material TEXT,
            stem TEXT NOT NULL,
            answer TEXT NOT NULL,
            analysis TEXT,
            score REAL NOT NULL,
            sort_no INTEGER NOT NULL,
            orig_type TEXT)");

        Exec(@"CREATE TABLE IF NOT EXISTS options (
            id INTEGER PRIMARY KEY,
            question_id INTEGER NOT NULL REFERENCES questions(id) ON DELETE CASCADE,
            label TEXT NOT NULL,
            content TEXT NOT NULL,
            sort_no INTEGER NOT NULL)");

        Exec(@"CREATE TABLE IF NOT EXISTS images (
            id INTEGER PRIMARY KEY,
            sha256 TEXT NOT NULL UNIQUE,
            file_path TEXT NOT NULL,
            width INTEGER, height INTEGER, bytes INTEGER,
            created_at TEXT NOT NULL)");

        Exec(@"CREATE TABLE IF NOT EXISTS image_refs (
            id INTEGER PRIMARY KEY,
            image_id INTEGER NOT NULL REFERENCES images(id) ON DELETE CASCADE,
            question_id INTEGER NOT NULL REFERENCES questions(id) ON DELETE CASCADE,
            scope TEXT NOT NULL CHECK (scope IN ('stem','option','analysis')),
            option_label TEXT,
            sort_no INTEGER NOT NULL DEFAULT 0,
            caption TEXT)");

        Exec(@"CREATE TABLE IF NOT EXISTS exam_records (
            id INTEGER PRIMARY KEY,
            paper_id INTEGER NOT NULL REFERENCES papers(id),
            subject_id INTEGER NOT NULL REFERENCES subjects(id),
            mode TEXT NOT NULL CHECK (mode IN ('paper','random')),
            examinee_name TEXT,
            started_at TEXT NOT NULL, finished_at TEXT,
            duration_used INTEGER,
            total_score REAL, score REAL, pass INTEGER,
            detail_json TEXT,
            status TEXT NOT NULL DEFAULT 'ongoing')");

        Exec(@"CREATE TABLE IF NOT EXISTS exam_answers (
            record_id INTEGER NOT NULL REFERENCES exam_records(id) ON DELETE CASCADE,
            question_id INTEGER NOT NULL REFERENCES questions(id),
            answer TEXT,
            correct INTEGER,
            score REAL,
            marked INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (record_id, question_id))");

        Exec(@"CREATE TABLE IF NOT EXISTS wrong_book (
            id INTEGER PRIMARY KEY,
            question_id INTEGER NOT NULL REFERENCES questions(id) ON DELETE CASCADE,
            wrong_count INTEGER NOT NULL DEFAULT 1,
            last_wrong_at TEXT, last_answer TEXT,
            UNIQUE (question_id))");

        Exec(@"CREATE TABLE IF NOT EXISTS import_batches (
            id INTEGER PRIMARY KEY,
            file_name TEXT NOT NULL, paper_name TEXT,
            imported_at TEXT NOT NULL,
            total_rows INTEGER, ok_rows INTEGER, error_rows INTEGER,
            report_json TEXT)");

        Exec("CREATE TABLE IF NOT EXISTS settings ( key TEXT PRIMARY KEY, value TEXT NOT NULL )");

        Exec("CREATE INDEX IF NOT EXISTS ix_questions_paper ON questions(paper_id)");
        Exec("CREATE INDEX IF NOT EXISTS ix_options_question ON options(question_id)");
        Exec("PRAGMA foreign_keys = ON;");
    }

    private static void Seed()
    {
        if (Scalar<long>("SELECT COUNT(*) FROM subjects") > 0) return;

        Exec("INSERT INTO subjects(id,name,category,duration_minutes,pass_rate) VALUES(1,'金融市场基础知识','general',120,0.6)");
        Exec("INSERT INTO subjects(id,name,category,duration_minutes,pass_rate) VALUES(2,'证券市场基本法律法规','general',120,0.6)");
        Exec("INSERT INTO subjects(id,name,category,duration_minutes,pass_rate) VALUES(3,'证券投资顾问业务','special',180,0.6)");

        SettingsService.InitDefaults();
    }

    // ---------- 通用助手 ----------

    public static int Exec(string sql, params (string name, object value)[] ps)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            Bind(cmd, ps);
            return cmd.ExecuteNonQuery();
        }
    }

    public static T Scalar<T>(string sql, params (string name, object value)[] ps)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            Bind(cmd, ps);
            var r = cmd.ExecuteScalar();
            if (r == null || r is DBNull) return default;
            return (T)Convert.ChangeType(r, typeof(T));
        }
    }

    public static long Insert(string sql, params (string name, object value)[] ps)
    {
        Exec(sql, ps);
        return Scalar<long>("SELECT last_insert_rowid()");
    }

    public static List<Dictionary<string, object>> Rows(string sql, params (string name, object value)[] ps)
    {
        var list = new List<Dictionary<string, object>>();
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            Bind(cmd, ps);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var d = new Dictionary<string, object>();
                for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                list.Add(d);
            }
        }
        return list;
    }

    /// <summary>在单个事务中执行（导入整卷等批量写入场景）。回调内请使用 TxExec/TxInsert。</summary>
    public static void Transaction(Action<SqliteTransaction> action)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            try
            {
                action(tx);
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }

    public static int TxExec(SqliteTransaction tx, string sql, params (string name, object value)[] ps)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        Bind(cmd, ps);
        return cmd.ExecuteNonQuery();
    }

    public static long TxInsert(SqliteTransaction tx, string sql, params (string name, object value)[] ps)
    {
        TxExec(tx, sql, ps);
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)cmd.ExecuteScalar();
    }

    private static void Bind(SqliteCommand cmd, (string name, object value)[] ps)
    {
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    // ---------- 业务查询 ----------

    public static List<SubjectDto> GetSubjects()
    {
        var list = new List<SubjectDto>();
        foreach (var r in Rows("SELECT * FROM subjects ORDER BY id"))
            list.Add(new SubjectDto
            {
                Id = (long)r["id"],
                Name = (string)r["name"],
                Category = (string)r["category"],
                DurationMinutes = (int)(long)r["duration_minutes"],
                PassRate = (double)r["pass_rate"],
            });
        return list;
    }

    public static SubjectDto GetSubject(long id)
    {
        foreach (var r in Rows("SELECT * FROM subjects WHERE id=@id", ("@id", id)))
            return new SubjectDto
            {
                Id = (long)r["id"],
                Name = (string)r["name"],
                Category = (string)r["category"],
                DurationMinutes = (int)(long)r["duration_minutes"],
                PassRate = (double)r["pass_rate"],
            };
        return null;
    }

    public static string SubjectName(long id) => Scalar<string>("SELECT name FROM subjects WHERE id=@id", ("@id", id));

    public static int QuestionCountOfPaper(long paperId) =>
        Scalar<int>("SELECT COUNT(*) FROM questions WHERE paper_id=@p", ("@p", paperId));

    public static List<PaperDto> GetPapers(long? subjectId = null, bool includeRandom = true)
    {
        string sql = @"SELECT p.*, s.name AS subject_name,
                       (SELECT COUNT(*) FROM questions q WHERE q.paper_id=p.id) AS qc
                       FROM papers p JOIN subjects s ON s.id=p.subject_id WHERE 1=1";
        var ps = new List<(string, object)>();
        if (subjectId.HasValue) { sql += " AND p.subject_id=@s"; ps.Add(("@s", subjectId.Value)); }
        if (!includeRandom) sql += " AND p.source != 'random'";
        sql += " ORDER BY p.id DESC";
        var list = new List<PaperDto>();
        foreach (var r in Rows(sql, ps.ToArray()))
            list.Add(new PaperDto
            {
                Id = (long)r["id"],
                Name = (string)r["name"],
                SubjectId = (long)r["subject_id"],
                SubjectName = (string)r["subject_name"],
                DurationMinutes = r["duration_minutes"] == null ? null : (int)(long)r["duration_minutes"],
                TotalScore = r["total_score"] == null ? null : (double)r["total_score"],
                Source = (string)r["source"],
                ImportBatchId = r["import_batch_id"] == null ? null : (long)r["import_batch_id"],
                CreatedAt = (string)r["created_at"],
                QuestionCount = (int)(long)r["qc"],
            });
        return list;
    }

    /// <summary>删除整卷：连同其考试历史、错题关联一并清理（单机本地数据，删除即彻底删除）。</summary>
    public static void DeletePaper(long paperId)
    {
        Transaction(tx =>
        {
            // 先清答题明细：既包括本卷考试记录的，也包括其他考试（如随机卷）对本卷题目的引用
            TxExec(tx, @"DELETE FROM exam_answers WHERE question_id IN
                         (SELECT id FROM questions WHERE paper_id=@p)", ("@p", paperId));
            TxExec(tx, @"DELETE FROM exam_answers WHERE record_id IN
                         (SELECT id FROM exam_records WHERE paper_id=@p)", ("@p", paperId));
            TxExec(tx, "DELETE FROM exam_records WHERE paper_id=@p", ("@p", paperId));
            TxExec(tx, @"DELETE FROM wrong_book WHERE question_id IN
                         (SELECT id FROM questions WHERE paper_id=@p)", ("@p", paperId));
            TxExec(tx, @"DELETE FROM image_refs WHERE question_id IN
                         (SELECT id FROM questions WHERE paper_id=@p)", ("@p", paperId));
            TxExec(tx, "DELETE FROM options WHERE question_id IN (SELECT id FROM questions WHERE paper_id=@p)", ("@p", paperId));
            TxExec(tx, "DELETE FROM questions WHERE paper_id=@p", ("@p", paperId));
            TxExec(tx, "DELETE FROM papers WHERE id=@p", ("@p", paperId));
        });
    }
}
