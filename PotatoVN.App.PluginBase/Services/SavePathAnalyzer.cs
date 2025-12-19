using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GalgameManager.Models;
using PotatoVN.App.PluginBase.Enums;

namespace PotatoVN.App.PluginBase.Services
{
    public class SavePathAnalyzer
    {
        private readonly Galgame _game;
        private List<string>? _cachedLowerVariants;
        private List<string>? _fuzzyMatchCoreVariants;
        private const double SIMILARITY_THRESHOLD = 0.8;

        public SavePathAnalyzer(Galgame game)
        {
            _game = game;
            PrecomputeVariants();
        }

        private void PrecomputeVariants()
        {
            var allGeneratedVariants = GenerateAllVariants(_game);
            _cachedLowerVariants = allGeneratedVariants
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v.ToLowerInvariant())
                .Distinct()
                .ToList();

            var coreNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_game.Name?.Value is { } name) coreNames.Add(name);
            if (!string.IsNullOrEmpty(_game.ChineseName)) coreNames.Add(_game.ChineseName!);
            if (_game.OriginalName?.Value is { } original) coreNames.Add(original);
            if (_game.Developer?.Value is { } developer) coreNames.Add(developer);
            if (_game.Categories != null)
            {
                foreach (var category in _game.Categories)
                    if (!string.IsNullOrEmpty(category.Name)) coreNames.Add(category.Name);
            }

            _fuzzyMatchCoreVariants = coreNames
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v.ToLowerInvariant())
                .Distinct()
                .ToList();
        }

        public bool IsPotentialSaveFile(string filePath)
        {
            try
            {
                ReadOnlySpan<char> filePathSpan = filePath.AsSpan();
                ReadOnlySpan<char> fileNameSpan = Path.GetFileName(filePathSpan);
                ReadOnlySpan<char> directorySpan = Path.GetDirectoryName(filePathSpan);

                if (_game.LocalPath != null && filePathSpan.StartsWith(_game.LocalPath.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    return true;

                var currentAppPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                if (SaveDetectionConstants.ShouldExcludePath(filePathSpan, currentAppPath)) return false;

                ReadOnlySpan<char> extensionSpan = Path.GetExtension(filePathSpan);
                if (extensionSpan.Length > 0 && SaveDetectionConstants.IsSaveFileExtension(extensionSpan.Slice(1))) return true;

                if (SaveDetectionConstants.ContainsSaveKeyword(fileNameSpan)) return true;

                if (MatchesHeuristicKeywords(directorySpan)) return true;

                return false;
            }
            catch { return false; }
        }

        public string? FindBestSaveDirectory(IEnumerable<string> paths)
        {
            var pathList = paths.ToList();
            if (pathList.Count == 0) return null;

            var directoryScores = new Dictionary<string, DirectoryScoreInfo>();
            var currentAppPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

            foreach (var path in pathList)
            {
                var directory = Path.GetDirectoryName(path);
                if (Directory.Exists(path) && !File.Exists(path)) directory = path;
                if (string.IsNullOrEmpty(directory)) continue;
                if (SaveDetectionConstants.ShouldExcludePath(directory.AsSpan(), currentAppPath)) continue;

                var normalizedDir = directory.ToLowerInvariant().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!directoryScores.ContainsKey(normalizedDir))
                    directoryScores[normalizedDir] = new DirectoryScoreInfo { Directory = directory };

                var scoreInfo = directoryScores[normalizedDir];
                scoreInfo.TotalScore += 2;
                scoreInfo.FileCount++;
                scoreInfo.TotalScore += CalculateSaveFileScore(path) * 0.1;
                scoreInfo.TotalScore += CalculateDirectoryVariantMatchScore(directory);
                scoreInfo.TotalScore += SaveDetectionConstants.GetPathStructureScore(directory.AsSpan());
            }

            if (directoryScores.Count == 0) return null;
            return directoryScores.OrderByDescending(kvp => kvp.Value.TotalScore).First().Value.Directory;
        }

        private bool MatchesHeuristicKeywords(ReadOnlySpan<char> pathSpan)
        {
            if (pathSpan.IsEmpty) return false;
            if (CheckSuffixList(pathSpan, SaveDetectionConstants.ChineseLocalizationSuffixes)) return true;
            if (CheckSuffixList(pathSpan, SaveDetectionConstants.SaveDirectorySuffixPatterns)) return true;
            if (_cachedLowerVariants != null)
            {
                foreach (var variant in _cachedLowerVariants)
                    if (pathSpan.IndexOf(variant.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return _fuzzyMatchCoreVariants != null && CheckFuzzyMatch(pathSpan);
        }

        private double CalculateSaveFileScore(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var score = 0.0;
                if (fileInfo.Length > 1024 && fileInfo.Length < 10 * 1024 * 1024) score += 30;
                var timeDiff = DateTime.Now - fileInfo.LastWriteTime;
                if (timeDiff.TotalMinutes < 10) score += 40;
                if (MatchesHeuristicKeywords(filePath.AsSpan())) score += 30;
                return score;
            }
            catch { return 0; }
        }

        private double CalculateDirectoryVariantMatchScore(string directory)
        {
            if (string.IsNullOrEmpty(directory) || _cachedLowerVariants == null) return 0;
            var dirName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(dirName)) dirName = directory;
            var dirNameSpan = dirName.AsSpan();
            var directorySpan = directory.AsSpan();
            var totalScore = 0.0;

            foreach (var variantLower in _cachedLowerVariants)
            {
                if (string.IsNullOrEmpty(variantLower)) continue;
                double score = 0;
                if (dirNameSpan.Equals(variantLower.AsSpan(), StringComparison.OrdinalIgnoreCase)) score = 500;
                else
                {
                    int index = directorySpan.IndexOf(variantLower.AsSpan(), StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        bool startOk = (index == 0) || IsSeparator(directory[index - 1]);
                        int end = index + variantLower.Length;
                        bool endOk = (end == directory.Length) || IsSeparator(directory[end]);
                        score = (startOk && endOk) ? 100 : 20;
                    }
                }
                totalScore = Math.Max(totalScore, score);
            }
            if (_fuzzyMatchCoreVariants != null)
            {
                foreach (var core in _fuzzyMatchCoreVariants)
                    if (JaroWinkler(dirNameSpan, core) > 0.8) totalScore = Math.Max(totalScore, 400);
            }
            return totalScore;
        }

        private bool CheckFuzzyMatch(ReadOnlySpan<char> pathSpan)
        {
            int start = 0;
            for (int i = 0; i <= pathSpan.Length; i++)
            {
                if (i == pathSpan.Length || IsSeparator(pathSpan[i]))
                {
                    if (i > start && _fuzzyMatchCoreVariants != null)
                    {
                        ReadOnlySpan<char> segment = pathSpan.Slice(start, i - start);
                        foreach (var core in _fuzzyMatchCoreVariants)
                            if (JaroWinkler(segment, core) > SIMILARITY_THRESHOLD) return true;
                    }
                    start = i + 1;
                }
            }
            return false;
        }

        private static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

        private bool CheckSuffixList(ReadOnlySpan<char> pathSpan, IEnumerable<string> suffixes)
        {
            foreach (var suffix in suffixes)
            {
                if (pathSpan.EndsWith(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
                if (pathSpan.IndexOf(($"_{suffix}").AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (pathSpan.IndexOf(($"-{suffix}").AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static double JaroWinkler(ReadOnlySpan<char> s1, string s2)
        {
            if (s1.IsEmpty || string.IsNullOrEmpty(s2)) return 0;
            int n = s1.Length, m = s2.Length, matchWindow = Math.Max(n, m) / 2 - 1;
            Span<bool> s1Matches = n <= 256 ? stackalloc bool[n] : new bool[n];
            Span<bool> s2Matches = m <= 256 ? stackalloc bool[m] : new bool[m];
            int matches = 0, transpositions = 0;
            for (int i = 0; i < n; i++)
            {
                int start = Math.Max(0, i - matchWindow), end = Math.Min(i + matchWindow + 1, m);
                char c1 = char.ToLowerInvariant(s1[i]);
                for (int j = start; j < end; j++)
                {
                    if (s2Matches[j] || c1 != s2[j]) continue;
                    s1Matches[i] = s2Matches[j] = true; matches++; break;
                }
            }
            if (matches == 0) return 0;
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                if (!s1Matches[i]) continue;
                while (!s2Matches[k]) k++;
                if (char.ToLowerInvariant(s1[i]) != s2[k]) transpositions++;
                k++;
            }
            double jaro = (matches / (double)n + matches / (double)m + (matches - transpositions / 2.0) / matches) / 3.0;
            int prefix = 0, maxPrefix = Math.Min(4, Math.Min(n, m));
            for (int i = 0; i < maxPrefix; i++) { if (char.ToLowerInvariant(s1[i]) == s2[i]) prefix++; else break; }
            return jaro + prefix * 0.1 * (1 - jaro);
        }

        private List<string> GenerateAllVariants(Galgame game)
        {
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GenerateNameVariants(game.Name?.Value, variants);
            GenerateNameVariants(game.ChineseName, variants);
            GenerateNameVariants(game.OriginalName?.Value, variants);
            GenerateDeveloperVariants(game.Developer?.Value, variants);
            if (game.Categories != null)
                foreach (var cat in game.Categories) if (!string.IsNullOrEmpty(cat.Name)) GenerateNameVariants(cat.Name, variants);
            return variants.ToList();
        }

        private void GenerateNameVariants(string? name, HashSet<string> variants)
        {
            if (string.IsNullOrEmpty(name)) return;
            variants.Add(name);
            variants.Add(name.ToLowerInvariant());
            foreach (var sep in SaveDetectionConstants.CurrentSeparators)
            {
                if (name.Contains(sep))
                {
                    var parts = name.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var newSep in SaveDetectionConstants.Separators)
                    {
                        if (newSep == sep || newSep == '\0') continue;
                        variants.Add(string.Join(newSep.ToString(), parts));
                    }
                    variants.Add(string.Join("", parts));
                }
            }
            SaveDetectionConstants.ApplyWordSimplifications(name, variants);
            SaveDetectionConstants.ApplyJapaneseConversions(name, variants);
            foreach (var v in SaveDetectionConstants.ApplyNumberConversions(name)) variants.Add(v);
        }

        private void GenerateDeveloperVariants(string? developer, HashSet<string> variants)
        {
            if (string.IsNullOrEmpty(developer)) return;
            GenerateNameVariants(developer, variants);
            foreach (var v in SaveDetectionConstants.GetDeveloperVariants(developer)) variants.Add(v);
        }

        private class DirectoryScoreInfo
        {
            public string Directory { get; set; } = string.Empty;
            public double TotalScore { get; set; }
            public int FileCount { get; set; }
        }
    }
}