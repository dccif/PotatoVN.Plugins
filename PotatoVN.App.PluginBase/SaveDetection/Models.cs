using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PotatoVN.App.PluginBase.SaveDetection.Models;

/// <summary>
/// 存档检测相关的常量定义
/// </summary>
public static class Constants
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
        ["0"] = "零",
        ["1"] = "一",
        ["2"] = "二",
        ["3"] = "三",
        ["4"] = "四",
        ["5"] = "五",
        ["6"] = "六",
        ["7"] = "七",
        ["8"] = "八",
        ["9"] = "九",
        ["10"] = "十"
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
        "data", "save", "saves", "games", "game", "user", "profile", "config",
        "settings", "storage", "backup", "cache", "temp", "local", "roaming",
        "存档", "保存", "数据", "配置", "设置", "档案", "记录", "进度",
        "system", "content", "resources", "assets", "files", "documents",
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
        // PotatoVN相关路径
        "potatovn", ".potatovn", "potato vn", "potato-vn", "potato_vn",

        // Windows系统目录
        "windows", "system32", "syswow64", "drivers", "driverstore", "servicing",
        "Microsoft", "win", "winsxs", "temp", "tmp", "Temp", "Packages",

        // 程序安装目录
        "program files", "program files (x86)", "programdata", "program files (arm)", "program files (arm64)",

        // 游戏平台相关
        "steam", "steamapps", "epic games", "uplay", "ubisoft", "origin", "ea games", "gog",
        "battle.net", "blizzard", "riot games", "discord",

        // 开发工具目录
        "visual studio", "msbuild", "nuget", "dotnet", "sdk", "code", "vscode", "android", "android sdk",

        // 浏览器目录
        "google", "chrome", "mozilla", "firefox", "edge", "opera",

        // 社交通信软件
        "xwechat_files","Tencent Files", "WeChat", "QQ", "WeChat Files", "deskgo", "WeChatWork", "Tencent",

        // 硬件相关目录
        "nvidia", "amd", "intel", "cuda", "rocm",

        // 临时和缓存目录
        "cache", "caches", "temporary", "logs", "log", "recycle bin", "$recycle.bin", "system volume information",

        // 配置和系统文件目录
        "config", "configuration", "settings", "preferences", "registry", "repair", "backup", "system restore",

        // 用户特定应用数据（非游戏相关）
        "microsoft office", "office", "adobe", "photoshop", "autodesk", "skype", "teams", "zoom", "slack", "spotify", "itunes",

        // 防病毒和安全软件
        "kaspersky", "norton", "mcafee", "avast", "avg",

        // 其他系统工具
        "powershell", "cmd", "sysnative", "tasks", "startup", "start menu", "desktop", "wallpaper",
        "scoop", "chocolatey", "winget", "TrafficMonitor", "ditto",

        // 游戏资源相关目录
        "pac", "movie", "bgm", "voice", "sound", "media", "plugin", "plugins"
    };

    /// <summary>
    /// 文件后缀黑名单
    /// </summary>
    public static readonly string[] ExtensionBlacklist = {
        ".exe", ".dll", ".lnk", ".ini", ".log", ".tmp", ".pdb", ".msi",
        ".ypf", ".arc", ".pak", ".xp3", ".dat", ".bin", ".ogg", ".wav", ".mp4", ".wmv", ".bik",
        ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".webp", ".svg", ".ico", ".ttf", ".otf", ".woff", ".woff2"
    };

    public static HashSet<string> GetGenericRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            void AddRoot(string path)
            {
                if (!string.IsNullOrEmpty(path))
                    roots.Add(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var localLow = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)?.Replace("Local", "LocalLow");
            AddRoot(localLow ?? "");
            AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games"));
            AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Saved Games"));
            AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games"));
        }
        catch { { } }
        return roots;
    }

    public static bool IsSaveFileExtension(ReadOnlySpan<char> extension)
    {
        if (extension.Length == 0) return false;
        return SaveFileExtensions.Contains(extension.ToString());
    }

    public static bool ContainsSaveKeyword(ReadOnlySpan<char> fileName)
    {
        if (fileName.Length == 0) return false;
        foreach (var keyword in SaveFileKeywords)
        {
            if (fileName.Contains(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static int GetPathStructureScore(ReadOnlySpan<char> directory)
    {
        var score = 0;
        foreach (var pattern in SaveDirectoryPatterns)
        {
            if (directory.IndexOf(pattern.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) score += 8;
        }
        foreach (var suffix in ChineseLocalizationSuffixes)
        {
            if (directory.EndsWith(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                directory.IndexOf($"_{suffix}".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                directory.IndexOf($"-{suffix}".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 12;
            }
        }
        foreach (var suffix in SaveDirectorySuffixPatterns)
        {
            if (directory.EndsWith(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase)) score += 6;
            else if (directory.IndexOf($"_{suffix}".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                     directory.IndexOf($"-{suffix}".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                     directory.IndexOf($".{suffix}".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 4;
            }
        }
        foreach (var pattern in SpecialPathScores)
        {
            if (directory.IndexOf(pattern.Key.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) score += pattern.Value;
        }
        return score;
    }

    public static bool ShouldExcludePath(ReadOnlySpan<char> targetPath, string currentAppPath)
    {
        if (targetPath.IsEmpty || string.IsNullOrEmpty(currentAppPath)) return false;
        if (targetPath.StartsWith(currentAppPath.AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var keyword in ExcludePathKeywords)
        {
            if (targetPath.IndexOf(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }
}

public class SaveDetectorOptions
{
    // 性能配置
    public int MaxQueueSize { get; init; } = 1000;
    public int AnalysisIntervalMs { get; init; } = 1500;

    // 投票阈值
    public int MinVoteCountThreshold { get; init; } = 1;
    public float ConfidenceScoreThreshold { get; init; } = 12.0f;

    // 权值分配
    public float EtwBonusWeight { get; init; } = 2.0f;
    public float KeywordBonusWeight { get; init; } = 5.0f;

    // 通用系统根目录（这些目录会被监视，但不应作为最终结果返回）
    public HashSet<string> GenericRoots { get; } = Constants.GetGenericRoots();

    public bool AllowEtw { get; init; } = true;

    // 等待游戏进程启动的时间（秒）
    public int ProcessWaitTimeSeconds { get; init; } = 10;

    // FileSystemWatcher 重试配置
    public int WatcherRetryCount { get; init; } = 3;
    public int WatcherRetryIntervalMs { get; init; } = 5000;

    // 最大探测时间（秒），默认5分钟
    public int MaxDetectionTimeSeconds { get; init; } = 300;
}

public enum ProviderSource { ETW, FileSystemWatcher, Polling }
public enum IoOperation { Create, Write, Rename, Unknown }
public enum LogLevel { Debug, Info, Warning, Error }

public record PathCandidate(string Path, ProviderSource Source, DateTime DetectedTime, IoOperation Op = IoOperation.Unknown);

internal class ScoredPath
{
    public string Path { get; init; } = string.Empty;
    public double Score { get; set; }
    public int VoteCount { get; set; }
}

public interface ISaveDetectorLogger
{
    void Log(string message, LogLevel level);
}

internal class NullLogger : ISaveDetectorLogger { public void Log(string m, LogLevel l) { } }

public class DetectionContext
{
    public System.Diagnostics.Process TargetProcess { get; }
    public CancellationToken Token { get; }
    public SaveDetectorOptions Settings { get; }
    public ConcurrentQueue<PathCandidate> Candidates { get; } = new();
    public string? FinalPath { get; set; }
    private readonly ISaveDetectorLogger _logger;

    public GalgameManager.Models.Galgame? Game { get; set; }
    public ISaveCandidateProvider? ActiveProvider { get; set; }

    public DetectionContext(System.Diagnostics.Process p, CancellationToken t, ISaveDetectorLogger logger, SaveDetectorOptions? s = null)
    {
        TargetProcess = p; Token = t; _logger = logger;
        Settings = s ?? new SaveDetectorOptions();
    }

    [Conditional("DEBUG")]
    public void Log(string msg, LogLevel level = LogLevel.Info)
    {
#if DEBUG
        _logger.Log(msg, level);
#endif
    }
}
