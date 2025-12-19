using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GalgameManager.Models;
using PotatoVN.App.PluginBase.Enums;

namespace PotatoVN.App.PluginBase.Services
{
    public class SaveFileDetector
    {
        private static byte? _fileTypeIndex;
        private static readonly ConcurrentBag<string> _detectedCandidates = new();
        private static readonly HashSet<string> _watchedDirectories = new();
        private static readonly List<FileSystemWatcher> _watchers = new();
        
        // Cache previous write times to detect changes
        private static readonly Dictionary<string, DateTime> _fileWriteTimes = new();

        public static async Task<string?> DetectSavePathAsync(Process process, Galgame? game, CancellationToken token)
        {
            if (game == null) return null;

            _detectedCandidates.Clear();
            _fileWriteTimes.Clear();
            _watchedDirectories.Clear();
            StopWatchers();
            
            var analyzer = new SavePathAnalyzer(game);
            
            Debug.WriteLine($"[SaveDetector] Started monitoring process {process.Id} ({process.ProcessName})");

            try
            {
                var currentAppPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                
                // Add standard watch paths
                AddStandardWatchers(game, currentAppPath, analyzer);

                while (!token.IsCancellationRequested)
                {
                    if (process.HasExited)
                    {
                        Debug.WriteLine("[SaveDetector] Process exited.");
                        break;
                    }

                    // 1. Polling Open Handles (Discovery)
                    var openFiles = GetOpenFiles(process.Id);
                    
                    foreach (var file in openFiles)
                    {
                         if (!File.Exists(file)) continue;
                         
                         var dir = Path.GetDirectoryName(file);
                         if (string.IsNullOrEmpty(dir)) continue;
                         dir = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                         if (SaveDetectionConstants.ShouldExcludePath(dir.AsSpan(), currentAppPath)) continue;

                         if (!_watchedDirectories.Contains(dir))
                         {
                             AddWatcher(dir, analyzer);
                         }

                         // Check LastWriteTime
                         CheckFileChange(file, analyzer);
                    }
                    
                    // 2. Check candidates using Analyzer
                    if (!_detectedCandidates.IsEmpty)
                    {
                        var candidates = _detectedCandidates.ToList();
                        var best = analyzer.FindBestSaveDirectory(candidates);
                        
                        if (best != null && candidates.Count >= 2)
                        {
                            return best;
                        }
                    }
                    
                    await Task.Delay(500, token); 
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SaveDetector] Error: {ex.Message}");
            }
            finally
            {
                StopWatchers();
            }

            return null;
        }

        private static void AddStandardWatchers(Galgame game, string currentAppPath, SavePathAnalyzer analyzer)
        {
            var paths = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Replace("Local", "LocalLow")
            };
            
            if (game.LocalPath != null) paths.Add(game.LocalPath);

            foreach (var p in paths)
            {
                if (Directory.Exists(p) && !SaveDetectionConstants.ShouldExcludePath(p.AsSpan(), currentAppPath))
                {
                    AddWatcher(p, analyzer);
                }
            }
        }

        private static void AddWatcher(string path, SavePathAnalyzer analyzer)
        {
            if (_watchedDirectories.Contains(path)) return;
            try
            {
                var watcher = new FileSystemWatcher(path);
                watcher.IncludeSubdirectories = true; 
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime;
                // Pass analyzer to handler using closure? No, event handler signature is fixed.
                // We'll use a lambda.
                watcher.Changed += (s, e) => OnFileChanged(e, analyzer);
                watcher.Created += (s, e) => OnFileChanged(e, analyzer);
                watcher.Renamed += (s, e) => OnFileChanged(e, analyzer);
                watcher.EnableRaisingEvents = true;
                
                _watchers.Add(watcher);
                _watchedDirectories.Add(path);
            }
            catch {}
        }

        private static void StopWatchers()
        {
            foreach (var w in _watchers) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch {} }
            _watchers.Clear();
            _watchedDirectories.Clear();
        }

        private static void OnFileChanged(FileSystemEventArgs e, SavePathAnalyzer analyzer)
        {
            if (File.Exists(e.FullPath))
            {
                CheckFileChange(e.FullPath, analyzer);
            }
        }

        private static void CheckFileChange(string path, SavePathAnalyzer analyzer)
        {
            try
            {
                if (!analyzer.IsPotentialSaveFile(path)) return;

                var info = new FileInfo(path);
                var lastWrite = info.LastWriteTime;

                bool isNewWrite = false;
                lock (_fileWriteTimes)
                {
                    if (!_fileWriteTimes.ContainsKey(path))
                    {
                        _fileWriteTimes[path] = lastWrite;
                        if ((DateTime.Now - lastWrite).TotalSeconds < 10) isNewWrite = true;
                    }
                    else
                    {
                        if (_fileWriteTimes[path] < lastWrite)
                        {
                            _fileWriteTimes[path] = lastWrite;
                            isNewWrite = true;
                        }
                    }
                }

                if (isNewWrite)
                {
                    _detectedCandidates.Add(path);
                    Debug.WriteLine($"[SaveDetector] Change Detected: {path}");
                }
            }
            catch {}
        }

        // --- Low Level Handle Logic (Discovery) ---
        private static List<string> GetOpenFiles(int processId)
        {
            var result = new List<string>();
            var handleInfoSize = 0x10000;
            var ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.AllocHGlobal(handleInfoSize);
                int length;
                while (NativeMethods.NtQuerySystemInformation(NativeMethods.CnstSystemHandleInformation, ptr, handleInfoSize, out length) == NativeMethods.StatusInfoLengthMismatch)
                {
                    handleInfoSize = length;
                    Marshal.FreeHGlobal(ptr);
                    ptr = Marshal.AllocHGlobal(handleInfoSize);
                }

                int handleCount = Marshal.ReadInt32(ptr);
                var offset = IntPtr.Size; 
                var is64Bit = IntPtr.Size == 8;
                var entrySize = is64Bit ? 24 : 16; 

                for (int i = 0; i < handleCount; i++)
                {
                    var currentPtr = ptr + offset + (i * entrySize);
                    int pid = Marshal.ReadInt32(currentPtr);
                    if (pid != processId) continue;

                    byte objectType = Marshal.ReadByte(currentPtr + 4);
                    if (_fileTypeIndex.HasValue && objectType != _fileTypeIndex.Value) continue;

                    ushort handleValue = (ushort)Marshal.ReadInt16(currentPtr + 6);
                    uint access = (uint)Marshal.ReadInt32(currentPtr + (is64Bit ? 16 : 12));

                    bool hasWrite = (access & 0x40000000) != 0 || (access & 0x0002) != 0 || (access & 0x0004) != 0;
                    if (!hasWrite) continue;

                    var path = GetPathFromHandle(processId, handleValue, objectType);
                    if (!string.IsNullOrEmpty(path))
                    {
                        path = NormalizeDevicePath(path);
                        if (!string.IsNullOrEmpty(path) && File.Exists(path)) result.Add(path);
                    }
                }
            }
            catch {}
            finally { if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
            return result;
        }

        private static string? GetPathFromHandle(int pid, ushort handleValue, byte objectType)
        {
            IntPtr hProcess = IntPtr.Zero;
            IntPtr hTarget = IntPtr.Zero;
            try
            {
                hProcess = NativeMethods.OpenProcess(NativeMethods.ProcessAccessFlags.DupHandle, false, pid);
                if (hProcess == IntPtr.Zero) return null;

                if (!NativeMethods.DuplicateHandle(hProcess, (IntPtr)handleValue, NativeMethods.GetCurrentProcess(), out hTarget, 0, false, 2)) return null;

                if (!_fileTypeIndex.HasValue)
                {
                    var typeName = GetObjectTypeName(hTarget);
                    if (typeName == "File") _fileTypeIndex = objectType;
                    else return null;
                }
                else if (objectType != _fileTypeIndex.Value) return null;

                return GetObjectName(hTarget);
            }
            catch { return null; }
            finally
            {
                if (hTarget != IntPtr.Zero) NativeMethods.CloseHandle(hTarget);
                if (hProcess != IntPtr.Zero) NativeMethods.CloseHandle(hProcess);
            }
        }

        private static string? GetObjectTypeName(IntPtr handle)
        {
            int length = 0x1000;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.AllocHGlobal(length);
                int retLen;
                if (NativeMethods.NtQueryObject(handle, NativeMethods.ObjectTypeInformation, ptr, length, out retLen) == 0)
                {
                    ushort strLen = (ushort)Marshal.ReadInt16(ptr);
                    IntPtr buffer = Marshal.ReadIntPtr(ptr + (IntPtr.Size == 8 ? 8 : 4));
                    if (buffer != IntPtr.Zero && strLen > 0) return Marshal.PtrToStringUni(buffer, strLen / 2);
                }
            }
            catch { }
            finally { if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
            return null;
        }

        private static string? GetObjectName(IntPtr handle)
        {
            int length = 0x2000;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.AllocHGlobal(length);
                int retLen;
                if (NativeMethods.NtQueryObject(handle, NativeMethods.ObjectNameInformation, ptr, length, out retLen) == 0)
                {
                    ushort strLen = (ushort)Marshal.ReadInt16(ptr);
                    IntPtr buffer = Marshal.ReadIntPtr(ptr + (IntPtr.Size == 8 ? 8 : 4));
                    if (buffer != IntPtr.Zero && strLen > 0) return Marshal.PtrToStringUni(buffer, strLen / 2);
                }
            }
            catch { }
            finally { if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
            return null;
        }

        private static string NormalizeDevicePath(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return string.Empty;
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    string driveRoot = drive.Name.Substring(0, 2); 
                    string dosDevice = QueryDosDevice(driveRoot);
                    if (!string.IsNullOrEmpty(dosDevice) && devicePath.StartsWith(dosDevice))
                        return driveRoot + devicePath.Substring(dosDevice.Length);
                }
                catch { }
            }
            return devicePath;
        }

        [DllImport("kernel32.dll")]
        private static extern uint QueryDosDevice(string lpDeviceName, IntPtr lpTargetPath, uint ucchMax);

        private static string QueryDosDevice(string driveLetter)
        {
            IntPtr ptr = Marshal.AllocHGlobal(1024);
            try
            {
                if (QueryDosDevice(driveLetter, ptr, 1024) > 0) return Marshal.PtrToStringAnsi(ptr) ?? "";
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return "";
        }
    }
}