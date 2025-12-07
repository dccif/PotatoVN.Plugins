using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Models;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;

namespace PotatoVN.App.PluginBase
{
    /// <summary>
    /// 插件版本的游戏存档检测器任务
    /// 基于原始GameSaveDetectorTask进行插件化改造
    /// </summary>
    public class PluginSaveDetectorTask_Broken : BgTaskBase
    {
        private readonly IPotatoVnApi _hostApi;
        private Galgame? _galgame;
        public List<string> DetectedSavePaths { get; set; } = new();
        public List<string> MonitoredPaths { get; set; } = new();
        public bool IsMonitoring { get; set; }
        public int SaveOperationCount { get; set; }

        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly List<string> _candidatePaths = new();
        private readonly List<string> _pendingMonitorPaths = new();
        private readonly Dictionary<string, DateTime> _pathFirstDetected = new();
        private DateTime _monitorStartTime;
        private const int DELAY_SECONDS = 10;
        private const int SAVE_COUNT_THRESHOLD = 3;

        private List<string>? _cachedVariants;
        private string _lastGameName = string.Empty;

        public PluginSaveDetectorTask_Broken(IPotatoVnApi hostApi, Galgame? galgame = null)
        {
            _hostApi = hostApi ?? throw new ArgumentNullException(nameof(hostApi));
            _galgame = galgame;
        }

        /// <summary>
        /// 设置要检测的游戏
        /// </summary>
        /// <param name="galgame">游戏对象</param>
        public void SetGalgame(Galgame galgame)
        {
            _galgame = galgame ?? throw new ArgumentNullException(nameof(galgame));
        }

        /// <summary>
        /// 插件版本的任务标题
        /// </summary>
        public override string Title => "Plugin Game Save Detector";

        /// <summary>
        /// 恢复JSON序列化后的状态
        /// </summary>
        protected override Task RecoverFromJsonInternal()
        {
            InitializeCandidatePaths();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 主要执行逻辑
        /// </summary>
        protected override async Task RunInternal()
        {
            if (_galgame == null) return;

            ChangeProgress(0, 1, "Initializing plugin game save detector...");

            InitializeCandidatePaths();

            Debug.WriteLine($"[PluginGameSaveDetector] Starting save detection for game '{_galgame.Name?.Value}'");
            Debug.WriteLine($"[PluginGameSaveDetector] Candidate paths count: {_candidatePaths.Count}");

            Debug.WriteLine("[PluginGameSaveDetector] Starting file system monitoring");
            await MonitorFileSystemAsync();

            var finalPaths = FilterDetectedPaths();
            Debug.WriteLine($"[PluginGameSaveDetector] Filtered candidate paths count: {finalPaths.Count}");

            if (finalPaths.Count > 0 && _galgame != null)
            {
                var saveDirectory = FindBestSaveDirectory(finalPaths);
                Debug.WriteLine($"[PluginGameSaveDetector] Selected save directory: {saveDirectory}");

                if (!string.IsNullOrEmpty(saveDirectory))
                {
                    _galgame.DetectedSavePosition = saveDirectory;
                    ChangeProgress(1, 1, $"Successfully detected save directory: {saveDirectory}");
                }
                else
                {
                    var fallbackDirectory = Path.GetDirectoryName(finalPaths[0]);
                    if (!string.IsNullOrEmpty(fallbackDirectory))
                    {
                        _galgame.DetectedSavePosition = fallbackDirectory;
                        Debug.WriteLine($"[PluginGameSaveDetector] Using fallback directory: {fallbackDirectory}");
                        ChangeProgress(1, 1, $"Successfully detected save directory: {fallbackDirectory}");
                    }
                    else
                    {
                        ChangeProgress(1, 1, "Failed to detect save directory");
                    }
                }
            }
            else
            {
                Debug.WriteLine("[PluginGameSaveDetector] No suitable save directory found");
                ChangeProgress(1, 1, "No save directory detected");
            }
        }

        /// <summary>
        /// 初始化候选路径
        /// </summary>
        private void InitializeCandidatePaths()
        {
            if (_galgame == null) return;

            Debug.WriteLine("[PluginGameSaveDetector] Initializing candidate paths");

            // 游戏安装目录（如果是本地的）
            if (!string.IsNullOrEmpty(_galgame.LocalPath))
            {
                _candidatePaths.Add(_galgame.LocalPath);
                Debug.WriteLine($"[PluginGameSaveDetector] Added game install directory: {_galgame.LocalPath}");
            }

            // 用户文档目录
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _candidatePaths.Add(documentsPath);
            _candidatePaths.Add(Path.Combine(documentsPath, "My Games"));
            _candidatePaths.Add(Path.Combine(documentsPath, "Saved Games"));

            // 用户主目录下的Saved Games目录
            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _candidatePaths.Add(userProfilePath);
            _candidatePaths.Add(Path.Combine(userProfilePath, "Saved Games"));

            // AppData 目录
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localLowPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Replace("Local", "LocalLow");

            _candidatePaths.Add(appDataPath);
            _candidatePaths.Add(localAppDataPath);
            _candidatePaths.Add(localLowPath);

            Debug.WriteLine($"[PluginGameSaveDetector] Final candidate paths count: {_candidatePaths.Count}");
        }

        /// <summary>
        /// 启动延迟文件系统监听
        /// </summary>
        private async Task MonitorFileSystemAsync()
        {
            IsMonitoring = true;
            Debug.WriteLine("[PluginGameSaveDetector] Starting delayed file system monitoring");

            var maxMonitorTime = TimeSpan.FromMinutes(2);
            _monitorStartTime = DateTime.Now;

            while (IsMonitoring && (DateTime.Now - _monitorStartTime) < maxMonitorTime)
            {
                var count = DetectedSavePaths.Count;
                ChangeProgress(0, 1, $"Monitoring for Save Files... Found {count} Potential Files");

                await Task.Delay(1000);
            }

            StopFileSystemMonitoring();

            var finalPaths = FilterDetectedPaths();
            Debug.WriteLine($"[PluginGameSaveDetector] Filtered candidate paths count: {finalPaths.Count}");

            if (finalPaths.Count > 0 && _galgame != null)
            {
                var saveDirectory = FindBestSaveDirectory(finalPaths);
                Debug.WriteLine($"[PluginGameSaveDetector] Selected save directory: {saveDirectory}");

                if (!string.IsNullOrEmpty(saveDirectory))
                {
                    _galgame.DetectedSavePosition = saveDirectory;
                    ChangeProgress(1, 1, $"Successfully detected save directory: {saveDirectory}");
                }
                else
                {
                    var fallbackDirectory = Path.GetDirectoryName(finalPaths[0]);
                    if (!string.IsNullOrEmpty(fallbackDirectory))
                    {
                        _galgame.DetectedSavePosition = fallbackDirectory;
                        Debug.WriteLine($"[PluginGameSaveDetector] Using fallback directory: {fallbackDirectory}");
                        ChangeProgress(1, 1, $"Successfully detected save directory: {fallbackDirectory}");
                    }
                    else
                    {
                        ChangeProgress(1, 1, "Failed to detect save directory");
                    }
                }
            }
            else
            {
                Debug.WriteLine("[PluginGameSaveDetector] No suitable save directory found");
                ChangeProgress(1, 1, "No save directory detected");
            }
        }

        /// <summary>
        /// 停止文件系统监听
        /// </summary>
        private void StopFileSystemMonitoring()
        {
            IsMonitoring = false;
            Debug.WriteLine($"[PluginGameSaveDetector] Stopping file system monitoring, cleaning up {_watchers.Count} watchers");

            foreach (var watcher in _watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                    Debug.WriteLine("[PluginGameSaveDetector] Successfully cleaned up one file system watcher");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginGameSaveDetector] Error cleaning up watcher: {ex.Message}");
                }
            }

            _watchers.Clear();
            MonitoredPaths.Clear();
            _cachedVariants = null;
            _pathFirstDetected.Clear();
            _pendingMonitorPaths.Clear();

            Debug.WriteLine("[PluginGameSaveDetector] File System Monitoring completely stopped, cache cleared");
        }

        /// <summary>
        /// 过滤检测到的路径
        /// </summary>
        private List<string> FilterDetectedPaths()
        {
            var filteredPaths = new List<string>();

            lock (DetectedSavePaths)
            {
                var uniquePaths = DetectedSavePaths.Distinct().ToList();
                Debug.WriteLine($"[PluginGameSaveDetector] Before filtering: {DetectedSavePaths.Count} paths, after dedup: {uniquePaths.Count}");

                filteredPaths.AddRange(uniquePaths.Take(10));
            }

            return filteredPaths;
        }

        /// <summary>
        /// 找到最佳存档目录
        /// </summary>
        private string FindBestSaveDirectory(List<string> paths)
        {
            if (paths.Count == 0) return string.Empty;

            Debug.WriteLine($"[PluginGameSaveDetector] Analyzing {paths.Count} paths to find best save directory");

            var directoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    var normalizedDir = directory.ToLowerInvariant().TrimEnd('\\', '/');
                    if (directoryCounts.ContainsKey(normalizedDir))
                        directoryCounts[normalizedDir]++;
                    else
                        directoryCounts[normalizedDir] = 1;
                }
            }

            var topDir = directoryCounts.OrderByDescending(kvp => kvp.Value).First();
            Debug.WriteLine($"[PluginGameSaveDetector] Selected directory: '{topDir.Key}' containing {topDir.Value} files");

            return topDir.Key;
        }
    }
}