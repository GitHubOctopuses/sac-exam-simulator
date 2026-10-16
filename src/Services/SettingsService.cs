using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SacExam.Services;

/// <summary>设置模块（说明书 §8：规则全部可配置）。值以 JSON 存 settings 表。</summary>
public static class SettingsService
{
    public class TypePreset
    {
        public string Type { get; set; }      // 单选题/多选题/判断题/综合题
        public int Count { get; set; }
        public double Score { get; set; }
    }

    public class SubjectPreset
    {
        public long SubjectId { get; set; }
        public int DurationMinutes { get; set; }
        public List<TypePreset> Types { get; set; } = new();
    }

    public class AppSettings
    {
        public double PassRate { get; set; } = 0.6;
        public int WarnRemainMinutes { get; set; } = 5;
        public bool AutoSave { get; set; } = true;
        public bool ImageAutoCompress { get; set; } = true;
        public int ImageMaxSide { get; set; } = 1600;
        public bool RememberFullscreen { get; set; } = true;
        public bool RecoveryPrompt { get; set; } = true;
        public string NoticeText { get; set; }
        public List<string> CaseSplitMarkers { get; set; } = new()
        {
            "根据此背景回答下列问题。",
            "根据以上背景回答下列问题。",
            "根据上述背景回答下列问题。",
            "根据以下背景回答下列问题。",
            "根据下列背景回答下列问题。",
            "根据此资料回答下列问题。",
            "根据以上资料回答下列问题。",
            "根据下述背景回答下列问题。",
        };
        public List<SubjectPreset> Presets { get; set; } = new();
    }

    public const string Key = "app_settings";
    private static AppSettings _cache;

    public static void InitDefaults()
    {
        var s = new AppSettings
        {
            NoticeText = DefaultNotice,
            Presets = new List<SubjectPreset>
            {
                new() { SubjectId = 1, DurationMinutes = 120, Types = new List<TypePreset>
                {
                    new(){ Type="单选题", Count=40, Score=0.5 },
                    new(){ Type="多选题", Count=40, Score=1 },
                    new(){ Type="判断题", Count=30, Score=1 },
                    new(){ Type="综合题", Count=10, Score=1 },
                }},
                new() { SubjectId = 2, DurationMinutes = 120, Types = new List<TypePreset>
                {
                    new(){ Type="单选题", Count=40, Score=0.5 },
                    new(){ Type="多选题", Count=40, Score=1 },
                    new(){ Type="判断题", Count=30, Score=1 },
                    new(){ Type="综合题", Count=10, Score=1 },
                }},
                new() { SubjectId = 3, DurationMinutes = 180, Types = new List<TypePreset>
                {
                    new(){ Type="单选题", Count=40, Score=0.5 },
                    new(){ Type="多选题", Count=40, Score=1 },
                    new(){ Type="判断题", Count=20, Score=1 },
                    new(){ Type="综合题", Count=20, Score=1 },
                }},
            }
        };
        Save(s);
    }

    public static AppSettings Get()
    {
        if (_cache != null) return _cache;
        string raw = Db.Scalar<string>("SELECT value FROM settings WHERE key=@k", ("@k", Key));
        if (string.IsNullOrEmpty(raw)) { InitDefaults(); raw = Db.Scalar<string>("SELECT value FROM settings WHERE key=@k", ("@k", Key)); }
        _cache = JsonSerializer.Deserialize<AppSettings>(raw);
        return _cache;
    }

    public static void Save(AppSettings s)
    {
        s.NoticeText ??= DefaultNotice;
        string json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
        Db.Exec("INSERT INTO settings(key,value) VALUES(@k,@v) ON CONFLICT(key) DO UPDATE SET value=@v",
            ("@k", Key), ("@v", json));
        _cache = s;
    }

    public const string DefaultNotice = @"一、考场纪律
（一）考试一律在指定时间和考场内进行，考生应在开考前30分钟内进入指定考场，进行现场取相和签到。
（二）考试开始20分钟后，考生不得进入考场参加考试；开考20分钟内考生未能在考试机上登录并确认的，视为缺考，考试系统将不再接受该准考证号登录。
（三）考生进入考场后，直至离开该考场，应服从考场工作人员管理及安排。
（四）考生进入考场时必须携带准考证和有效身份证件（携带证件须与报名时填写证件一致，其他证件均属无效）。
（五）考生应在考试签到表上签到，并在""电子设备已关闭""处签字确认，确保手机等电子通讯设备已处于关闭状态直至离开考场（包括手机闹钟等）。考试过程中，如果发现考生手机未关闭或有翻看手机的情况，监考老师将该情况记录在《考场纪律单》上。

二、考试规则
（一）本次考试为闭卷机考，考生须在考试机上完成作答。
（二）考生输入准考证号登录考试机，核对考生信息无误后开始考试。
（三）答题过程中，选择选项的同时系统自动保存答案；题号右方""*""符号代表该小题尚未选择答案，""?""符号代表该小题做过标记。
（四）考试结束时系统自动交卷；考试全程可使用机考内置计算器。
（五）考生不得以任何方式将试题内容带出考场。

三、本系统说明
本系统为模拟练习用途软件，非中国证券业协会官方软件；卷内题目均为模拟题或用户自行导入内容，与真实题库无关。";
}
