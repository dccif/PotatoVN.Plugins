using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalgameManager.Models;
using PotatoVN.App.PluginBase.SaveDetection.Models;

namespace PotatoVN.App.PluginBase.SaveDetection.Analyzers;

internal class VotingAnalyzer : ISavePathAnalyzer
{
    private List<string>? _cachedLowerVariants;
    private List<string>? _fuzzyMatchCoreVariants;
    private Galgame? _cachedGame;
    private const double SIMILARITY_THRESHOLD = 0.8;

    public bool IsValidPath(string path, SaveDetectorOptions options, Galgame? game = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.Contains(':')) return false;

        // 1. Blacklists
        if (options.PathBlacklist.Any(b => path.Contains(b, StringComparison.OrdinalIgnoreCase)))
            return false;

        var ext = Path.GetExtension(path);
        if (options.ExtensionBlacklist.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            return false;

        // 2. Game Directory (Always allow if inside game folder)
        if (game?.LocalPath != null && path.StartsWith(game.LocalPath, StringComparison.OrdinalIgnoreCase))
            return true;

        // 3. Whitelists
        if (ext.Length > 1 && options.SaveExtensionWhitelist.Any(w => w.Equals(ext.Substring(1), StringComparison.OrdinalIgnoreCase)))
            return true;

        var fileName = Path.GetFileName(path);
        if (options.SaveKeywordWhitelist.Any(w => fileName.Contains(w, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }
    public string? FindBestSaveDirectory(List<PathCandidate> candidates, SaveDetectorOptions options, Galgame? game = null)
    {
        if (candidates == null || candidates.Count == 0) return null;

        // Update variants if game context is provided
        if (game != null && game != _cachedGame)
        {
            PrecomputeVariants(game);
            _cachedGame = game;
        }

        var directoryScores = new Dictionary<string, ScoredPath>(StringComparer.OrdinalIgnoreCase);
        var currentAppPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

        foreach (var candidate in candidates)
        {
            var path = candidate.Path;
            var directory = Path.GetDirectoryName(path);
            if (Directory.Exists(path) && !File.Exists(path)) directory = path;

            if (string.IsNullOrEmpty(directory)) continue;
            if (IsPathExcluded(directory, currentAppPath, options)) continue;

            var normalizedDir = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!directoryScores.TryGetValue(normalizedDir, out var scored))
            {
                scored = new ScoredPath { Path = directory };
                directoryScores[normalizedDir] = scored;
            }

            // 1. Base Score & Vote Count
            scored.Score += 2;
            scored.VoteCount++;

            // 2. File Quality Score
            scored.Score += CalculateSaveFileScore(path, options) * 0.1;

            // 3. Provider Bonus
            if (candidate.Source == ProviderSource.ETW)
                scored.Score += options.EtwBonusWeight;
        }

        // Post-process directory scores
        foreach (var kvp in directoryScores)
        {
            var dir = kvp.Value.Path;

            // 4. Variant Matching Score
            kvp.Value.Score += CalculateDirectoryVariantMatchScore(dir);

            // 5. Path Structure Score
            kvp.Value.Score += GetPathStructureScore(dir, options);
        }

        return directoryScores.Values
            .Where(v => v.VoteCount >= options.MinVoteCountThreshold)
            .OrderByDescending(v => v.Score)
            .FirstOrDefault(v => v.Score >= options.ConfidenceScoreThreshold)?.Path;
    }

    private bool IsPathExcluded(string path, string appPath, SaveDetectorOptions options)
    {
        if (string.IsNullOrEmpty(path)) return true;
        if (!string.IsNullOrEmpty(appPath) && path.StartsWith(appPath, StringComparison.OrdinalIgnoreCase)) return true;

        // Check path blacklist again for directory parts
        return options.PathBlacklist.Any(b => path.Contains(b, StringComparison.OrdinalIgnoreCase));
    }

    private double CalculateSaveFileScore(string filePath, SaveDetectorOptions options)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var score = 0.0;

            // Size heuristics
            if (fileInfo.Length > 1024 && fileInfo.Length < 10 * 1024 * 1024) score += 30;
            else if (fileInfo.Length > 100 && fileInfo.Length < 100 * 1024 * 1024) score += 20;

            // Time heuristics
            var timeDiff = DateTime.Now - fileInfo.LastWriteTime;
            if (timeDiff.TotalMinutes < 10) score += 40;
            else if (timeDiff.TotalHours < 1) score += 30;
            else if (timeDiff.TotalDays < 1) score += 20;

            // Keyword heuristics
            var nameSpan = Path.GetFileName(filePath).AsSpan();
            foreach (var kw in options.SaveKeywordWhitelist)
            {
                if (nameSpan.IndexOf(kw.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 30;
                    break;
                }
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

        // Exact/Contains Match
        foreach (var variant in _cachedLowerVariants)
        {
            if (string.IsNullOrEmpty(variant)) continue;
            var variantSpan = variant.AsSpan();

            if (dirNameSpan.Equals(variantSpan, StringComparison.OrdinalIgnoreCase))
            {
                totalScore = Math.Max(totalScore, 500); // Perfect Match
            }
            else
            {
                int index = directorySpan.IndexOf(variantSpan, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    // Check boundaries
                    bool startOk = index == 0 || IsSeparator(directory[index - 1]);
                    bool endOk = (index + variant.Length == directory.Length) || IsSeparator(directory[index + variant.Length]);

                    if (startOk && endOk) totalScore = Math.Max(totalScore, 100);
                    else totalScore = Math.Max(totalScore, 20);
                }
            }
        }

        // Fuzzy Match
        if (_fuzzyMatchCoreVariants != null)
        {
            foreach (var core in _fuzzyMatchCoreVariants)
            {
                if (JaroWinkler(dirNameSpan, core) > 0.8)
                {
                    totalScore = Math.Max(totalScore, 400);
                }
            }
        }

        return totalScore;
    }

    private int GetPathStructureScore(string directory, SaveDetectorOptions options)
    {
        var score = 0;
        var dirSpan = directory.AsSpan();

        // Save Directory Suffixes
        foreach (var suffix in options.SaveDirectorySuffixPatterns)
        {
            if (dirSpan.EndsWith(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase)) score += 6;
            else if (dirSpan.IndexOf(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) score += 4;
        }

        // Chinese Suffixes (Higher weight)
        foreach (var suffix in options.ChineseLocalizationSuffixes)
        {
            if (dirSpan.IndexOf(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) score += 12;
        }

        // Common Patterns like "AppData"
        if (dirSpan.IndexOf("appdata".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) score += 8;
        if (dirSpan.IndexOf("saved games".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) score += 10;

        return score;
    }

    private void PrecomputeVariants(Galgame game)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Generate basic variants
        GenerateNameVariants(game.Name?.Value, variants);
        GenerateNameVariants(game.ChineseName, variants);
        GenerateNameVariants(game.OriginalName?.Value, variants);
        GenerateNameVariants(game.Developer?.Value, variants);

        if (game.Categories != null)
        {
            foreach (var cat in game.Categories)
                if (!string.IsNullOrEmpty(cat.Name)) GenerateNameVariants(cat.Name, variants);
        }

        _cachedLowerVariants = variants
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v.ToLowerInvariant())
            .Distinct()
            .ToList();

        // Generate core variants for fuzzy matching
        var coreNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (game.Name?.Value is { } n) coreNames.Add(n);
        if (!string.IsNullOrEmpty(game.ChineseName)) coreNames.Add(game.ChineseName!);
        if (game.OriginalName?.Value is { } o) coreNames.Add(o);

        _fuzzyMatchCoreVariants = coreNames
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v.ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    private void GenerateNameVariants(string? name, HashSet<string> variants)
    {
        if (string.IsNullOrEmpty(name)) return;
        variants.Add(name);

        // Split by separators
        var separators = new[] { ' ', '_', '-', '.' };
        foreach (var sep in separators)
        {
            if (name.Contains(sep))
            {
                var parts = name.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                variants.Add(string.Join("", parts)); // No separator
                variants.Add(string.Join(" ", parts)); // Space
            }
        }
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