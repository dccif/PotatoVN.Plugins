using GalgameManager.Models;
using PotatoVN.App.PluginBase.SaveDetection.Helpers;
using PotatoVN.App.PluginBase.SaveDetection.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PotatoVN.App.PluginBase.SaveDetection.Analyzers;

internal class VotingAnalyzer : ISavePathAnalyzer
{
    private List<string>? _cachedLowerVariants;
    private List<string>? _fuzzyMatchCoreVariants;
    private Galgame? _cachedGame;
    private const double SIMILARITY_THRESHOLD = 0.8;
    private readonly GameVariantHelper _variantHelper = new();

    public Action<string, LogLevel>? Logger { get; set; }

    public bool IsValidPath(string path, SaveDetectorOptions options, Galgame? game = null, IoOperation op = IoOperation.Unknown)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.Contains(':')) return false;

        // 0. Context Update
        if (game != null && game != _cachedGame)
        {
            Prepare(game);
        }

        // 1. Global Path Blacklist (Strict)
        if (IsBlacklisted(path, Constants.ExcludePathKeywords))
        {
            return false;
        }

        // 2. Game Variant Match (Layer 2 - The Primary Filter)
        bool matchesGameVariant = false;
        if (_cachedLowerVariants != null && _cachedLowerVariants.Count > 0)
        {
            var pathSpan = path.AsSpan();
            foreach (var variant in _cachedLowerVariants)
            {
                if (string.IsNullOrEmpty(variant)) continue;

                if (variant.Length < 4)
                {
                    if (ContainsWholeWord(pathSpan, variant.AsSpan()))
                    {
                        matchesGameVariant = true;
                        break;
                    }
                }
                else
                {
                    if (path.Contains(variant, StringComparison.OrdinalIgnoreCase))
                    {
                        matchesGameVariant = true;
                        break;
                    }
                }
            }
        }

        if (matchesGameVariant)
        {
            return true;
        }

        // 3. Whitelist / Heuristic Fallback (Layer 3)
        var ext = Path.GetExtension(path).TrimStart('.');
        bool isWhitelistedExt = Constants.SaveFileExtensions.Contains(ext);
        bool hasSaveKeyword = Constants.ContainsSaveKeyword(Path.GetFileName(path).AsSpan());

        bool isStandardPath = path.Contains("AppData", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("Saved Games", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("My Games", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("Documents", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("Steam", StringComparison.OrdinalIgnoreCase);

        // A. Known Save Extension -> Accept
        if (isWhitelistedExt) return true;

        // B. "Save" keyword + Standard Location -> Accept
        if (hasSaveKeyword && isStandardPath) return true;

        // C. WRITE operation in "Save" directory -> Accept
        if (op == IoOperation.Write || op == IoOperation.Rename)
        {
            var dirName = Path.GetFileName(Path.GetDirectoryName(path));
            if (!string.IsNullOrEmpty(dirName) && Constants.SaveDirectorySuffixPatterns.Any(p => dirName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    public void Prepare(Galgame game)
    {
        _cachedGame = game;
        _cachedLowerVariants = _variantHelper.GetVariants(game);

        var coreNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (game.Name?.Value is { } n) coreNames.Add(n);
        if (!string.IsNullOrEmpty(game.ChineseName)) coreNames.Add(game.ChineseName!);
        if (game.OriginalName?.Value is { } o) coreNames.Add(o);
        _fuzzyMatchCoreVariants = coreNames.Where(v => !string.IsNullOrEmpty(v)).Select(v => v.ToLowerInvariant()).Distinct().ToList();
    }

    private bool IsBlacklisted(string path, IEnumerable<string> blacklist)
    {
        var pathSpan = path.AsSpan();
        foreach (var blocked in blacklist)
        {
            if (ContainsWholeWord(pathSpan, blocked.AsSpan())) return true;
        }
        return false;
    }

    private bool ContainsWholeWord(ReadOnlySpan<char> text, ReadOnlySpan<char> word)
    {
        int index = 0;
        while (true)
        {
            var remaining = text.Slice(index);
            int found = remaining.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;

            int actualIndex = index + found;
            int endIndex = actualIndex + word.Length;

            bool startBoundary = actualIndex == 0 || !char.IsLetterOrDigit(text[actualIndex - 1]);
            bool endBoundary = endIndex == text.Length || !char.IsLetterOrDigit(text[endIndex]);

            if (startBoundary && endBoundary) return true;

            index = actualIndex + 1;
        }
    }

    public string? FindBestSaveDirectory(List<PathCandidate> candidates, SaveDetectorOptions options, Galgame? game = null)
    {
        if (candidates == null || candidates.Count == 0) return null;

        Logger?.Invoke($"[Voting] Starting analysis with {candidates.Count} candidates", LogLevel.Debug);

        if (game != null && game != _cachedGame)
        {
            Prepare(game);
        }

        var currentAppPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        var gameRoot = game?.LocalPath;

        var groupedCandidates = candidates
            .Select(c =>
            {
                var dir = Path.GetDirectoryName(c.Path);
                if (string.IsNullOrEmpty(dir)) return (Dir: string.Empty, Candidate: c);
                if (Directory.Exists(c.Path) && !File.Exists(c.Path)) dir = c.Path;
                return (Dir: dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Candidate: c);
            })
            .Where(x => !string.IsNullOrEmpty(x.Dir))
            .Where(x => !options.GenericRoots.Contains(x.Dir))
            .Where(x => !IsPathExcluded(x.Dir, currentAppPath, options, gameRoot))
            .GroupBy(x => x.Dir)
            .ToList();

        var directoryScores = new Dictionary<string, ScoredPath>(StringComparer.OrdinalIgnoreCase);

        var sortedGroups = groupedCandidates
            .Select(g => new
            {
                Dir = g.Key,
                Candidates = g.Select(x => x.Candidate).ToList(),
                LatestTime = g.Max(x => x.Candidate.DetectedTime),
                FileCount = g.Select(x => x.Candidate.Path).Distinct().Count()
            })
            .OrderByDescending(g => g.LatestTime)
            .ToList();

        for (int i = 0; i < sortedGroups.Count; i++)
        {
            var group = sortedGroups[i];
            var scored = new ScoredPath { Path = group.Dir };

            if (i == 0) scored.Score += 400;
            else if (i == 1) scored.Score += 200;
            else if (i == 2) scored.Score += 100;

            scored.Score += CalculateBehaviorScore(group.Candidates);

            foreach (var candidate in group.Candidates)
            {
                scored.Score += CalculateSaveFileScore(candidate.Path, options);
            }
            scored.VoteCount = group.Candidates.Count;

            scored.Score += Constants.GetPathStructureScore(group.Dir.AsSpan());

            scored.Score += CalculateDirectoryVariantMatchScore(group.Dir);

            if (group.Candidates.Any(c => c.Source == ProviderSource.ETW))
                scored.Score += options.EtwBonusWeight;

            directoryScores[group.Dir] = scored;
        }

        var similarityWinner = GetSimilarityMechanismWinner(candidates, options);
        if (similarityWinner != null)
        {
            var normalizedSimilarityDir = similarityWinner.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (directoryScores.TryGetValue(normalizedSimilarityDir, out var scored))
            {
                scored.Score += 500;
                Logger?.Invoke($"[Voting] Filename similarity winner '{similarityWinner}' received Bonus (+500)", LogLevel.Debug);
            }
        }

        foreach (var kvp in directoryScores)
        {
            Logger?.Invoke($"[Voting] Candidate: {kvp.Key} | Score: {kvp.Value.Score} | Votes: {kvp.Value.VoteCount}", LogLevel.Debug);
        }

        var best = directoryScores.Values
            .Where(v => v.VoteCount >= options.MinVoteCountThreshold)
            .OrderByDescending(v => v.Score)
            .FirstOrDefault(v => v.Score >= options.ConfidenceScoreThreshold);

        if (best != null)
        {
            Logger?.Invoke($"[Voting] Final Winner: {best.Path} (Score: {best.Score})", LogLevel.Debug);
        }
        else
        {
            Logger?.Invoke("[Voting] No winner found passing threshold.", LogLevel.Debug);
        }

        return best?.Path;
    }

    private double CalculateBehaviorScore(List<PathCandidate> candidates)
    {
        double score = 0;
        bool hasWrite = candidates.Any(c => c.Op == IoOperation.Write);
        bool hasRename = candidates.Any(c => c.Op == IoOperation.Rename);

        if (hasWrite) score += 50;
        if (hasRename) score += 30;
        if (hasWrite && hasRename) score += 100;

        var distinctFiles = candidates.Select(c => c.Path).Distinct().Count();
        if (distinctFiles >= 1 && distinctFiles <= 5) score += 50;
        else if (distinctFiles > 20) score -= 50;

        return score;
    }

    private string? GetSimilarityMechanismWinner(List<PathCandidate> candidates, SaveDetectorOptions options)
    {
        if (candidates.Count < 2) return null;

        var currentAppPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        var validCandidates = new List<(string Dir, string Name)>();
        foreach (var c in candidates)
        {
            var dir = Path.GetDirectoryName(c.Path);
            if (Directory.Exists(c.Path) && !File.Exists(c.Path)) dir = c.Path;
            if (string.IsNullOrEmpty(dir) || IsPathExcluded(dir, currentAppPath, options)) continue;
            validCandidates.Add((dir, Path.GetFileName(c.Path)));
        }

        if (validCandidates.Count < 2) return null;

        var similarities = new List<(string Dir1, string Dir2, double Sim)>();
        for (int i = 0; i < validCandidates.Count; i++)
        {
            for (int j = i + 1; j < validCandidates.Count; j++)
            {
                var sim = JaroWinkler(validCandidates[i].Name.AsSpan(), validCandidates[j].Name);
                if (sim >= SIMILARITY_THRESHOLD)
                {
                    similarities.Add((validCandidates[i].Dir, validCandidates[j].Dir, sim));
                }
            }
        }

        if (similarities.Count == 0) return null;

        var topTwo = similarities.OrderByDescending(s => s.Sim).Take(2).ToList();
        var dirCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in topTwo)
        {
            dirCounts[pair.Dir1] = dirCounts.GetValueOrDefault(pair.Dir1) + 1;
            dirCounts[pair.Dir2] = dirCounts.GetValueOrDefault(pair.Dir2) + 1;
        }

        int totalInvolvedPaths = topTwo.Count * 2;
        var winner = dirCounts.OrderByDescending(x => x.Value).FirstOrDefault(x => x.Value > totalInvolvedPaths / 2);

        return winner.Key;
    }

    private bool IsPathExcluded(string path, string appPath, SaveDetectorOptions options, string? gameRoot = null)
    {
        if (string.IsNullOrEmpty(path)) return true;
        if (!string.IsNullOrEmpty(appPath) && path.StartsWith(appPath, StringComparison.OrdinalIgnoreCase)) return true;

        if (gameRoot != null && path.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        return IsBlacklisted(path, Constants.ExcludePathKeywords);
    }

    private double CalculateSaveFileScore(string filePath, SaveDetectorOptions options)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > 100 * 1024 * 1024) return -500;
            if (fileInfo.Length == 0) return -10;

            var score = 0.0;
            if (fileInfo.Length > 100 && fileInfo.Length < 10 * 1024 * 1024) score += 30;

            if (Constants.ContainsSaveKeyword(Path.GetFileName(filePath).AsSpan()))
            {
                score += 100;
            }
            return score;
        }
        catch { return 0; }
    }

    private double CalculateDirectoryVariantMatchScore(string directory)
    {
        if (string.IsNullOrEmpty(directory) || _cachedLowerVariants == null) return 0;

        var totalScore = 0.0;
        var dirName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(dirName)) dirName = directory;

        var dirNameSpan = dirName.AsSpan();
        var directorySpan = directory.AsSpan();

        foreach (var variant in _cachedLowerVariants)
        {
            if (string.IsNullOrEmpty(variant)) continue;
            var variantSpan = variant.AsSpan();

            if (dirNameSpan.Equals(variantSpan, StringComparison.OrdinalIgnoreCase))
            {
                totalScore = Math.Max(totalScore, 300);
            }
            else
            {
                int index = directorySpan.IndexOf(variantSpan, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    bool startOk = index == 0 || IsSeparator(directory[index - 1]);
                    bool endOk = (index + variant.Length == directory.Length) || IsSeparator(directory[index + variant.Length]);
                    if (startOk && endOk) totalScore = Math.Max(totalScore, 80);
                }
            }
        }

        if (_fuzzyMatchCoreVariants != null)
        {
            foreach (var core in _fuzzyMatchCoreVariants)
            {
                if (JaroWinkler(dirNameSpan, core) > 0.8) totalScore = Math.Max(totalScore, 200);
            }
        }

        return totalScore;
    }

    private static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

    private static double JaroWinkler(ReadOnlySpan<char> s1, string s2)
    {
        if (s1.IsEmpty || string.IsNullOrEmpty(s2)) return 0;
        int n = s1.Length, m = s2.Length;
        int matchWindow = Math.Max(n, m) / 2 - 1;

        Span<bool> s1Matches = n <= 256 ? stackalloc bool[n] : new bool[n];
        Span<bool> s2Matches = m <= 256 ? stackalloc bool[m] : new bool[m];

        int matches = 0, transpositions = 0;
        for (int i = 0; i < n; i++)
        {
            int start = Math.Max(0, i - matchWindow);
            int end = Math.Min(i + matchWindow + 1, m);
            char c1 = char.ToLowerInvariant(s1[i]);
            for (int j = start; j < end; j++)
            {
                if (s2Matches[j] || c1 != s2[j]) continue;
                s1Matches[i] = s2Matches[j] = true;
                matches++;
                break;
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
        int prefix = 0;
        int maxPrefix = Math.Min(4, Math.Min(n, m));
        for (int i = 0; i < maxPrefix; i++)
        {
            if (char.ToLowerInvariant(s1[i]) == s2[i]) prefix++;
            else break;
        }

        return jaro + prefix * 0.1 * (1 - jaro);
    }
}