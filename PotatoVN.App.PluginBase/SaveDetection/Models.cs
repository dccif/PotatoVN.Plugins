using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace PotatoVN.App.PluginBase.SaveDetection.Models;

public class SaveDetectorOptions
{
    // 性能配置
    public int MaxQueueSize { get; init; } = 1000;
    public int AnalysisIntervalMs { get; init; } = 1500;

    // 投票阈值
    public int MinVoteCountThreshold { get; init; } = 3;
    public float ConfidenceScoreThreshold { get; init; } = 12.0f;

    // 权值分配
    public float EtwBonusWeight { get; init; } = 2.0f;
    public float KeywordBonusWeight { get; init; } = 5.0f;

    // 路径黑名单（包含这些关键词的路径将被跳过）
    public string[] PathBlacklist { get; init; } = { 
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

    // 文件后缀黑名单
    public string[] ExtensionBlacklist { get; init; } = { ".exe", ".dll", ".lnk", ".ini", ".log", ".tmp", ".pdb", ".msi" };

    // 存档文件后缀白名单
    public string[] SaveExtensionWhitelist { get; init; } = {
        "sav", "dat", "save", "sfs", "rpgsave", "rvdata", "rvdata2",
        "json", "xml", "yaml", "yml", "cfg", "config", "backup"
    };

    // 存档关键词白名单（文件名或目录名包含这些词加分）
    public string[] SaveKeywordWhitelist { get; init; } = {
        "save", "sav", "slot", "data", "record", "progress", "file",
        "state", "status", "profile", "account", "user", "game",
        "session", "checkpoint", "quick", "auto"
    };

    // 存档目录后缀模式（用于更灵活的匹配加分）
    public string[] SaveDirectorySuffixPatterns { get; init; } = {
        "data", "save", "saves", "games", "game", "user", "profile", "config",
        "settings", "storage", "backup", "cache", "temp", "local", "roaming",
        "存档", "保存", "数据", "配置", "设置", "档案", "记录", "进度",
        "system", "content", "resources", "assets", "files", "documents",
        "セーブ", "データ", "設定", "システム", "コンフィグ"
    };

    // 汉化文件夹后缀模式
    public string[] ChineseLocalizationSuffixes { get; init; } = {
        "chs", "cht", "cn", "zh", "zhcn", "zhtw", "sc", "tc", "chinese",
        "简体", "繁体", "中文", "汉化", "汉化版", "steam简中"
    };

    public bool AllowEtw { get; init; } = true;
    
    // 等待游戏进程启动的时间（秒）
    public int ProcessWaitTimeSeconds { get; init; } = 10;
}

public enum ProviderSource { ETW, FileSystemWatcher, Polling } 
public enum LogLevel { Debug, Info, Warning, Error } 

public record PathCandidate(string Path, ProviderSource Source, DateTime DetectedTime);

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
    public Process TargetProcess { get; }
    public CancellationToken Token { get; }
    public SaveDetectorOptions Settings { get; }
    public ConcurrentQueue<PathCandidate> Candidates { get; } = new();
    public string? FinalPath { get; set; }
    private readonly ISaveDetectorLogger _logger;

    // Extra context needed for FileSystemWatcherProvider
    public GalgameManager.Models.Galgame? Game { get; set; }
    public ISaveCandidateProvider? ActiveProvider { get; set; }

    public DetectionContext(Process p, CancellationToken t, ISaveDetectorLogger logger, SaveDetectorOptions? s = null)
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
