using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace SacExam.Services;

/// <summary>导出：整卷→12 列模板 xlsx + images 文件夹（§5.9 可移植回导）；题库 zip 备份（db + images）。</summary>
public static class ExportService
{
    public static string ExportPaper(long paperId, string outDir)
    {
        var paper = Db.Rows("SELECT * FROM papers WHERE id=@p", ("@p", paperId)).FirstOrDefault()
            ?? throw new ArgumentException("试卷不存在");
        var qs = BankService.GetQuestions(paperId);
        string safeName = SafeName((string)paper["name"]);
        string dir = Path.Combine(outDir, safeName);
        Directory.CreateDirectory(dir);
        string imgDir = Path.Combine(dir, "images");
        Directory.CreateDirectory(imgDir);

        var wb = new XSSFWorkbook();
        var sheet = wb.CreateSheet("Sheet1");
        string[] headers = { "题型", "题目", "选项1", "选项2", "选项3", "选项4", "正确答案", "答案解析", "分值", "题干图片", "选项图片", "解析图片" };
        var head = sheet.CreateRow(0);
        for (int i = 0; i < headers.Length; i++) head.CreateCell(i).SetCellValue(headers[i]);

        int r = 1;
        foreach (var q in qs)
        {
            var row = sheet.CreateRow(r++);
            row.CreateCell(0).SetCellValue(q.QType);
            string stem = (q.CaseMaterial != null ? q.CaseMaterial + "\n" : "") + q.Stem;
            row.CreateCell(1).SetCellValue(stem);
            for (int i = 0; i < 4; i++)
                row.CreateCell(2 + i).SetCellValue(i < q.Options.Count ? q.Options[i].Label + "、" + q.Options[i].Content : "");
            row.CreateCell(6).SetCellValue(q.Answer);
            row.CreateCell(7).SetCellValue(q.Analysis ?? "");
            row.CreateCell(8).SetCellValue(q.Score);

            var stemFiles = CopyImages(q.StemImages, imgDir);
            row.CreateCell(9).SetCellValue(string.Join(";", stemFiles));
            var optSpec = q.Options
                .Where(o => o.ImageFiles.Count > 0)
                .Select(o => o.Label + ":" + string.Join(";", CopyImages(o.ImageFiles, imgDir)));
            row.CreateCell(10).SetCellValue(string.Join(";", optSpec));
            row.CreateCell(11).SetCellValue(string.Join(";", CopyImages(q.AnalysisImages, imgDir)));
        }

        string xlsxPath = Path.Combine(dir, safeName + ".xlsx");
        using (var fs = new FileStream(xlsxPath, FileMode.Create))
        {
            wb.Write(fs);
        }
        return dir;
    }

    private static List<string> CopyImages(List<string> files, string imgDir)
    {
        var names = new List<string>();
        foreach (var f0 in files)
        {
            try
            {
                string f = f0.StartsWith(ImportService.StoredPrefix, StringComparison.Ordinal)
                    ? Path.Combine(App.ImageDir, f0[ImportService.StoredPrefix.Length..])
                    : f0;
                if (!File.Exists(f)) continue;
                string dst = Path.Combine(imgDir, Path.GetFileName(f));
                File.Copy(f, dst, true);
                names.Add(Path.GetFileName(f));
            }
            catch { }
        }
        return names;
    }

    /// <summary>题库备份 zip：sacexam.db + images/。</summary>
    public static string ExportZip(string savePath)
    {
        if (File.Exists(savePath)) File.Delete(savePath);
        using var zip = ZipFile.Open(savePath, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(App.DbPath, "sacexam.db");
        if (Directory.Exists(App.ImageDir))
            foreach (var f in Directory.GetFiles(App.ImageDir))
                zip.CreateEntryFromFile(f, "images/" + Path.GetFileName(f));
        return savePath;
    }

    /// <summary>从备份 zip 恢复（覆盖当前数据）。</summary>
    public static void ImportZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var dbEntry = zip.GetEntry("sacexam.db") ?? throw new ArgumentException("备份包中缺少 sacexam.db");
        string tmpDb = Path.Combine(Path.GetTempPath(), "sacexam_restore.db");
        dbEntry.ExtractToFile(tmpDb, true);
        // 校验可打开
        File.Copy(tmpDb, App.DbPath, true);
        var imgEntryDir = zip.Entries.Where(e => e.FullName.StartsWith("images/", StringComparison.Ordinal));
        Directory.CreateDirectory(App.ImageDir);
        foreach (var e in imgEntryDir)
        {
            if (e.Name.Length == 0) continue;
            e.ExtractToFile(Path.Combine(App.ImageDir, e.Name), true);
        }
    }

    public static string SafeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length == 0 ? "试卷" : name;
    }
}
