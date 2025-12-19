using System;
using System.Collections.Generic;
using System.Linq;
using GalgameManager.Helpers;

namespace PotatoVN.App.PluginBase.Enums;

/// <summary>
/// 存档检测相关的常量定义
/// </summary>
public static class SaveDetectionConstants
{
    /// <summary>
    /// 存档文件扩展名（不带点号）
    /// </summary>
    public static readonly HashSet<string> SaveFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "sav", "dat", "save", "sfs", "rpgsave", "rvdata", "rvdata2",
        "json", "xml", "yaml", "yml", "cfg", "config", "backup"
    };

    /// <summary>
    /// 存档文件关键词
    /// </summary>
    public static readonly HashSet<string> SaveFileKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "save", "sav", "slot", "data", "record", "progress", "file",
        "state", "status", "profile", "account", "user", "game",
        "session", "checkpoint", "quick", "auto"
    };

    /// <summary>
    /// 特定游戏开发商的已知变体
    /// </summary>
    public static readonly Dictionary<string, List<string>> DeveloperVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["asa"] = new List<string>
        {
            "asaproject", "asa project", "asa_project", "asa-project",
            "AsaProject", "ASA Project", "ASA_PROJECT", "asaproj",
            "asa_proj", "asa-proj", "AsaProj", "asaprojects",
            "asa projects", "asa_projects", "asa-projects"
        },
        ["key"] = new List<string>
        {
            "key", "visualarts", "visual arts", "visual_arts"
        },
        ["typemoon"] = new List<string>
        {
            "typemoon", "type-moon", "type_moon"
        }
    };

    /// <summary>
    /// 常见词汇的简化映射
    /// </summary>
    public static readonly Dictionary<string, List<string>> WordSimplifications = new(StringComparer.OrdinalIgnoreCase)
    {
        ["project"] = new List<string> { "proj", "p" },
        ["game"] = new List<string> { "gm" },
        ["visual"] = new List<string> { "vis" },
        ["novel"] = new List<string> { "vn", "nov" },
        ["story"] = new List<string> { "stry", "st" },
        ["adventure"] = new List<string> { "adv", "adven" },
        ["chronicles"] = new List<string> { "chron", "chr" },
        ["legend"] = new List<string> { "leg", "lgd" },
        ["fantasy"] = new List<string> { "fan", "fnt" },
        ["world"] = new List<string> { "wrld", "wd" }
    };

    /// <summary>
    /// 日语词汇的罗马音映射
    /// </summary>
    public static readonly Dictionary<string, List<string>> JapaneseMappings = new()
    {
        ["プロジェクト"] = new List<string> { "project", "purojekuto" },
        ["ウォーズ"] = new List<string> { "wars", "waruzu" },
        ["ストーリー"] = new List<string> { "story", "story", "sutori" },
        ["ファンタジー"] = new List<string> { "fantasy", "fantesi" },
        ["アドベンチャー"] = new List<string> { "adventure", "adobencha" },
        ["クロニクル"] = new List<string> { "chronicle", "kuronikuru" }
    };

    /// <summary>
    /// 数字映射（阿拉伯数字转中文）
    /// </summary>
    public static readonly Dictionary<string, string> NumberMappings = new()
    {
        ["0"] = "零", ["1"] = "一", ["2"] = "二", ["3"] = "三",
        ["4"] = "四", ["5"] = "五", ["6"] = "六", ["7"] = "七",
        ["8"] = "八", ["9"] = "九", ["10"] = "十"
    };

    /// <summary>
    /// 常见的存档目录模式
    /// </summary>
    public static readonly string[] SaveDirectoryPatterns =
    {
        "save", "saves", "savedata", "save_data", "userdata", "user_data",
        "data", "game", "games", "appdata", "local", "roaming"
    };

    /// <summary>
    /// 汉化文件夹后缀模式（用于识别汉化版本的存档目录）
    /// </summary>
    public static readonly string[] ChineseLocalizationSuffixes =
    {
        "chs", "cht", "cn", "zh", "zhcn", "zhtw", "sc", "tc", "chinese",
        "简体", "繁体", "中文", "汉化", "汉化版", "steam简中"
    };

    /// <summary>
    /// 存档目录常见末尾字符模式（用于更灵活的匹配）
    /// </summary>
    public static readonly string[] SaveDirectorySuffixPatterns =
    {
        // 英文后缀
        "data", "save", "saves", "games", "game", "user", "profile", "config",
        "settings", "storage", "backup", "cache", "temp", "local", "roaming",

        // 汉化后缀
        "存档", "保存", "数据", "配置", "设置", "档案", "记录", "进度",

        // 常见游戏特定后缀
        "system", "content", "resources", "assets", "files", "documents",

        // 日语相关
        "セーブ", "データ", "設定", "システム", "コンフィグ"
    };

    /// <summary>
    /// 特殊路径加分配置
    /// </summary>
    public static readonly Dictionary<string, int> SpecialPathScores = new(StringComparer.OrdinalIgnoreCase)
    {
        ["appdata\\roaming"] = 15,
        ["appdata\\local"] = 12,
        ["my games"] = 10,
        ["saved games"] = 10
    };

    /// <summary>
    /// 分隔符定义
    /// </summary>
    public static readonly char[] Separators = { ' ', '_', '-', '.', '\0' };

    /// <summary>
    /// 当前分隔符（用于分割）
    /// </summary>
    public static readonly char[] CurrentSeparators = { ' ', '_', '-', '.' };

    /// <summary>
    /// 排除路径关键词（这些路径通常包含程序文件而非存档文件）
    /// </summary>
    public static readonly HashSet<string> ExcludePathKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "potatovn", ".potatovn", "potato vn", "potato-vn", "potato_vn",
        "windows", "system32", "syswow64", "drivers", "driverstore", "servicing",
        "Microsoft", "win", "winsxs", "temp", "tmp", "Temp", "Packages",
        "program files", "program files (x86)", "programdata", "program files (arm)", "program files (arm64)",
        "steam", "steamapps", "epic games", "uplay", "ubisoft", "origin", "ea games", "gog",
        "battle.net", "blizzard", "riot games", "discord",
        "visual studio", "msbuild", "nuget", "dotnet", "sdk", "code", "vscode", "android", "android sdk",
        "google", "chrome", "mozilla", "firefox", "edge", "opera",
        "xwechat_files","Tencent Files", "WeChat", "QQ", "WeChat Files", "deskgo", "WeChatWork", "Tencent",
        "nvidia", "amd", "intel", "cuda", "rocm",
        "cache", "caches", "temporary", "logs", "log", "recycle bin", "$recycle.bin", "system volume information",
        "config", "configuration", "settings", "preferences", "registry", "repair", "backup", "system restore",
        "microsoft office", "office", "adobe", "photoshop", "autodesk", "skype", "teams", "zoom", "slack", "spotify", "itunes",
        "kaspersky", "norton", "mcafee", "avast", "avg",
        "powershell", "cmd", "sysnative", "tasks", "startup", "start menu", "desktop", "wallpaper",
        "scoop", "chocolatey", "winget", "TrafficMonitor", "ditto"
    };

    /// <summary>
    /// 检查扩展名是否为存档文件扩展名
    /// </summary>
    public static bool IsSaveFileExtension(ReadOnlySpan<char> extension)
    {
        if (extension.Length == 0) return false;
        return SaveFileExtensions.Contains(extension.ToString());
    }

    /// <summary>
    /// 检查文件名是否包含存档关键词
    /// </summary>
    public static bool ContainsSaveKeyword(ReadOnlySpan<char> fileName)
    {
        if (fileName.Length == 0) return false;
        foreach (var keyword in SaveFileKeywords)
        {
            if (fileName.Contains(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 获取指定开发商的变体列表
    /// </summary>
    public static List<string> GetDeveloperVariants(string developer)
    {
        if (string.IsNullOrEmpty(developer)) return new List<string>();
        var lowerDev = developer.ToLowerInvariant();
        foreach (var kvp in DeveloperVariants)
        {
            if (lowerDev.Contains(kvp.Key)) return kvp.Value;
        }
        return new List<string>();
    }

    /// <summary>
    /// 应用词汇简化
    /// </summary>
    public static void ApplyWordSimplifications(string name, ISet<string> variants)
    {
        var lowerName = name.ToLowerInvariant();
        foreach (var simplification in WordSimplifications)
        {
            if (lowerName.Contains(simplification.Key))
            {
                foreach (var replacement in simplification.Value)
                {
                    var simplified = lowerName.Replace(simplification.Key, replacement);
                    variants.Add(simplified);
                    var noSepVersion = simplified.Replace("_", "").Replace("-", "").Replace(" ", "");
                    if (noSepVersion != simplified) variants.Add(noSepVersion);
                }
            }
        }
    }

    /// <summary>
    /// 应用日语词汇转换
    /// </summary>
    public static void ApplyJapaneseConversions(string name, ISet<string> variants)
    {
        foreach (var mapping in JapaneseMappings)
        {
            if (name.Contains(mapping.Key))
            {
                foreach (var variant in mapping.Value)
                {
                    var converted = name.ToLowerInvariant().Replace(mapping.Key, variant);
                    variants.Add(converted);
                }
            }
        }
    }

    /// <summary>
    /// 应用数字转换
    /// </summary>
    public static List<string> ApplyNumberConversions(string name)
    {
        var variants = new List<string>();
        var result = name;
        foreach (var mapping in NumberMappings)
        {
            result = result.Replace(mapping.Key, mapping.Value);
            variants.Add(result);
        }
        return variants;
    }

    /// <summary>
    /// 检查路径是否符合存档目录模式
    /// </summary>
    public static int GetPathStructureScore(ReadOnlySpan<char> directory)
    {
        var score = 0;
        foreach (var pattern in SaveDirectoryPatterns)
            if (directory.IndexOf(pattern.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) score += 8;

        foreach (var suffix in ChineseLocalizationSuffixes)
            if (directory.EndsWith(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                directory.IndexOf($"_{suffix}".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                directory.IndexOf($"-{suffix}".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) score += 12;

        foreach (var suffix in SaveDirectorySuffixPatterns)
            if (directory.EndsWith(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase)) score += 6;
            else if (directory.IndexOf($"_{suffix}".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                     directory.IndexOf($"-{suffix}".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                     directory.IndexOf($".{suffix}".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) score += 4;

        foreach (var pattern in SpecialPathScores)
            if (directory.IndexOf(pattern.Key.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) score += pattern.Value;

        return score;
    }

    /// <summary>
    /// 检查路径是否应该被排除
    /// </summary>
    public static bool ShouldExcludePath(ReadOnlySpan<char> targetPath, string currentAppPath)
    {
        if (targetPath.IsEmpty || string.IsNullOrEmpty(currentAppPath)) return false;
        if (targetPath.StartsWith(currentAppPath.AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var keyword in ExcludePathKeywords)
            if (targetPath.IndexOf(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
}
