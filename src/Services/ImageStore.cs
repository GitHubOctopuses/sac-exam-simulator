using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace SacExam.Services;

/// <summary>图片体系（说明书 §5.9）：SHA-256 命名入库、自动合并同内容、超限压缩、引用登记。</summary>
public static class ImageStore
{
    public static string ImageDir => App.ImageDir;

    public static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    public class StoreResult
    {
        public long ImageId;
        public string FileName;     // <sha256>.<ext>
        public string FullPath;
        public int Width, Height, Bytes;
        public bool Compressed;
        public string Warning;      // 非空=导入警告（如 webp 无法压缩/读取）
    }

    /// <summary>把图片字节入库（内容哈希去重），返回引用信息。ext 用于首个落盘扩展名。</summary>
    public static StoreResult Store(byte[] data, string ext)
    {
        if (data == null || data.Length == 0) throw new ArgumentException("图片数据为空");
        string extLower = string.IsNullOrWhiteSpace(ext) ? ".png" : ext.ToLowerInvariant();
        if (!extLower.StartsWith(".")) extLower = "." + extLower;
        if (!AllowedExt.Contains(extLower)) throw new ArgumentException($"不支持的图片格式 {extLower}（仅 png/jpg/jpeg/gif/bmp/webp）");
        if (data.Length > 5 * 1024 * 1024)
        {
            // 单图超 5MB：尝试压缩后继续
            var resized = TryCompress(data, extLower, 1600);
            if (resized == null) throw new ArgumentException("单图超过 5MB 且无法压缩（格式不支持压缩），请更换图片");
            data = resized;
        }

        var st = new StoreResult { Bytes = data.Length };
        string sha = Sha256Hex(data);
        string fileName = sha + extLower;
        string fullPath = Path.Combine(ImageDir, fileName);
        File.WriteAllBytes(fullPath, data);
        st.FileName = fileName;
        st.FullPath = fullPath;

        // 读取宽高（webp 读取失败则记录 0）
        try
        {
            var bi = new BitmapImage();
            using (var ms = new MemoryStream(data))
            {
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
            }
            st.Width = bi.PixelWidth; st.Height = bi.PixelHeight;
        }
        catch { st.Warning = "该图片格式当前环境暂无法读取尺寸/预览（如 webp）"; }

        var settings = SettingsService.Get();
        if (settings.ImageAutoCompress && (st.Width > settings.ImageMaxSide || st.Height > settings.ImageMaxSide))
        {
            var smaller = TryCompress(data, extLower, settings.ImageMaxSide);
            if (smaller != null && smaller.Length < data.Length)
            {
                File.WriteAllBytes(fullPath, smaller);
                data = smaller;
                st.Bytes = data.Length;
                st.Compressed = true;
                try
                {
                    var bi2 = new BitmapImage();
                    using (var ms2 = new MemoryStream(data))
                    {
                        bi2.BeginInit(); bi2.CacheOption = BitmapCacheOption.OnLoad; bi2.StreamSource = ms2; bi2.EndInit();
                    }
                    st.Width = bi2.PixelWidth; st.Height = bi2.PixelHeight;
                }
                catch { }
            }
        }

        long existing = Db.Scalar<long>("SELECT COUNT(*) FROM images WHERE sha256=@h", ("@h", sha));
        if (existing > 0)
        {
            st.ImageId = Db.Scalar<long>("SELECT id FROM images WHERE sha256=@h", ("@h", sha));
        }
        else
        {
            st.ImageId = Db.Insert(@"INSERT INTO images(sha256,file_path,width,height,bytes,created_at)
                VALUES(@h,@p,@w,@h2,@b,@t)",
                ("@h", sha), ("@p", fileName), ("@w", (long)st.Width), ("@h2", (long)st.Height),
                ("@b", (long)st.Bytes), ("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        }
        return st;
    }

    /// <summary>从文件入库。</summary>
    public static StoreResult StoreFile(string path)
    {
        string ext = Path.GetExtension(path);
        if (!AllowedExt.Contains(ext)) throw new ArgumentException($"不支持的图片格式 {ext}（仅 png/jpg/jpeg/gif/bmp/webp）");
        return Store(File.ReadAllBytes(path), ext);
    }

    /// <summary>按扩展名列表连续尝试解码（如 png/jpg 混用扩展名错误时兜底）。</summary>
    public static byte[] TryCompress(byte[] data, string ext, int maxSide)
    {
        // WPF 解码（png/jpg/gif/bmp 支持；webp 不支持返回 null）
        try
        {
            var bi = new BitmapImage();
            using (var ms = new MemoryStream(data))
            {
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit();
            }
            int w = bi.PixelWidth, h = bi.PixelHeight;
            if (w <= maxSide && h <= maxSide) return null;
            double scale = Math.Min((double)maxSide / w, (double)maxSide / h);
            int nw = Math.Max(1, (int)(w * scale)), nh = Math.Max(1, (int)(h * scale));

            var tb = new System.Windows.Media.Imaging.TransformedBitmap(bi,
                new System.Windows.Media.ScaleTransform(nw / (double)w, nh / (double)h));

            string norm = ext == ".jpeg" ? ".jpg" : ext;
            BitmapEncoder enc = norm switch
            {
                ".png" => new PngBitmapEncoder(),
                ".jpg" => new JpegBitmapEncoder { QualityLevel = 88 },
                ".bmp" => new BmpBitmapEncoder(),
                ".gif" => new GifBitmapEncoder(),
                _ => new PngBitmapEncoder(),
            };
            using (var outMs = new MemoryStream())
            {
                enc.Frames.Add(BitmapFrame.Create(tb));
                enc.Save(outMs);
                return outMs.ToArray();
            }
        }
        catch { return null; }
    }

    public static string Sha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        var sb = new System.Text.StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static string PathOfFileName(string fileName) => Path.Combine(ImageDir, fileName);

    public static bool ExistsByFileName(string fileName) => File.Exists(PathOfFileName(fileName));

    /// <summary>登记题目-图片引用。</summary>
    public static void AddRef(SqliteTransaction tx, long imageId, long questionId, string scope, string optionLabel, int sortNo, string caption = null)
    {
        Db.TxExec(tx, @"INSERT INTO image_refs(image_id,question_id,scope,option_label,sort_no,caption)
                        VALUES(@i,@q,@s,@l,@n,@c)",
            ("@i", imageId), ("@q", questionId), ("@s", scope), ("@l", (object)optionLabel ?? DBNull.Value),
            ("@n", (long)sortNo), ("@c", (object)caption ?? DBNull.Value));
    }

    /// <summary>加载题目的全部图片引用，按 scope 归位。</summary>
    public static void LoadRefsInto(QuestionDto q)
    {
        foreach (var r in Db.Rows(@"SELECT ir.scope, ir.option_label, ir.sort_no, i.file_path
                                    FROM image_refs ir JOIN images i ON i.id=ir.image_id
                                    WHERE ir.question_id=@q ORDER BY ir.sort_no", ("@q", q.Id)))
        {
            string path = PathOfFileName((string)r["file_path"]);
            switch ((string)r["scope"])
            {
                case "stem": q.StemImages.Add(path); break;
                case "analysis": q.AnalysisImages.Add(path); break;
                case "option":
                    string label = r["option_label"] as string;
                    var opt = q.Options.Find(o => o.Label == label);
                    if (opt != null) opt.ImageFiles.Add(path);
                    break;
            }
        }
    }
}
