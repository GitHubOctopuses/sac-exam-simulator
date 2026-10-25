using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using SacExam.Services;

namespace SacExam;

public partial class App : Application
{
    /// <summary>数据目录：默认 exe 旁 data/，不可写时回退到 %LOCALAPPDATA%\SacExam\data。</summary>
    public static string DataDir { get; private set; }

    public static string DbPath => Path.Combine(DataDir, "sacexam.db");
    public static string ImageDir => Path.Combine(DataDir, "images");

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args ?? Array.Empty<string>();
        bool selftest = false, seed = false, calcMode = false;
        string dbOverride = null, paperArg = null, seedSubject = null, seedName = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--selftest", StringComparison.OrdinalIgnoreCase)) selftest = true;
            else if (string.Equals(args[i], "--seed", StringComparison.OrdinalIgnoreCase)) seed = true;
            else if (string.Equals(args[i], "--calc", StringComparison.OrdinalIgnoreCase)) calcMode = true;
            else if (string.Equals(args[i], "--subject", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) seedSubject = args[++i];
            else if (string.Equals(args[i], "--name", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) seedName = args[++i];
            else if (string.Equals(args[i], "--db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) dbOverride = args[++i];
            else paperArg ??= args[i];
        }

        DataDir = ResolveDataDir(dbOverride);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ImageDir);

        Db.Init();
        try { Services.ExamService.CleanupOrphanRandomPapers(); } catch { /* 清理失败不影响启动 */ }

        if (seed)
        {
            AttachConsole(-1);
            try
            {
                var subjects = Services.Db.GetSubjects();
                var subject = subjects.FirstOrDefault(s => s.Name == seedSubject) ?? subjects.FirstOrDefault();
                var outcome = Services.ImportService.ParseFile(paperArg, null);
                var report = Services.ImportService.Commit(new Services.ImportService.CommitOptions
                {
                    FilePath = paperArg,
                    PaperName = seedName ?? outcome.PaperName ?? Path.GetFileNameWithoutExtension(paperArg),
                    SubjectId = subject.Id,
                    DurationMinutes = outcome.DurationMinutes,
                    Rows = outcome.Rows,
                });
                Console.WriteLine($"[seed] 已导入「{report.OkRows}」行到科目「{subject.Name}」，试卷 ID {report.PaperId}，失败 {report.ErrorRows} 行");
                Environment.Exit(report.ErrorRows == 0 ? 0 : 1);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[seed] 失败: " + ex.Message);
                Environment.Exit(1);
            }
            return;
        }

        if (calcMode)
        {
            // 独立计算器模式（诊断/演示用）：进程驻留直至窗口关闭
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var w = new CalculatorWindow(null);
            w.Show();
            return;
        }

        if (selftest)
        {
            AttachConsole(-1); // 让 WinExe 也能把结果打印到调用它的终端
            int code;
            try { code = SelfTest.Run(paperArg); }
            catch (Exception ex)
            {
                Console.WriteLine("[FAIL] 自测发生异常: " + ex);
                code = 1;
            }
            Environment.Exit(code);
            return;
        }

        new MainWindow().Show();
    }

    private static string ResolveDataDir(string dbOverride)
    {
        if (!string.IsNullOrEmpty(dbOverride))
            return Path.GetDirectoryName(Path.GetFullPath(dbOverride));

        try
        {
            string baseDir = AppContext.BaseDirectory;
            string candidate = Path.Combine(baseDir, "data");
            Directory.CreateDirectory(candidate);
            using (File.OpenWrite(Path.Combine(candidate, ".wtest"))) { }
            File.Delete(Path.Combine(candidate, ".wtest"));
            return candidate;
        }
        catch
        {
            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SacExam", "data");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
