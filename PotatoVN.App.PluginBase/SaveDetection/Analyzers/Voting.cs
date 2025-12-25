using GalgameManager.Models;
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

    public Action<string, LogLevel>? Logger { get; set; }

    public bool IsValidPath(string path, SaveDetectorOptions options, Galgame? game = null, IoOperation op = IoOperation.Unknown)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.Contains(':')) return false;

        // 1. Global Path Blacklist (Strict Filter)
        if (IsBlacklisted(path, options.PathBlacklist))
        {
            Logger?.Invoke($"[Voting] Path rejected by global path blacklist: {path}", LogLevel.Debug);
            return false;
        }

        // 2. Extension Filter (Nuanced)
        var ext = Path.GetExtension(path);
        bool isBlacklistedExt = options.ExtensionBlacklist.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));

        if (isBlacklistedExt)
        {
            // The extension is in the blacklist (e.g. .dat, .bin, .png)
            // But we must check if it's actually a save file masquerading as an asset (common in .dat/.bin)

            // A. Is it explicitly whitelisted? (Conflict resolution: Whitelist wins)
            var extWithoutDot = ext.TrimStart('.');
            if (options.SaveExtensionWhitelist.Any(w => w.Equals(extWithoutDot, StringComparison.OrdinalIgnoreCase)))
            {
                // It's a .dat or .bin, which is ambiguous. We accept it for now and let the scorer decide.
                Logger?.Invoke($"[Voting] Path accepted (Ambiguous Extension in Whitelist): {path}", LogLevel.Debug);
                return true;
            }

            // B. Context Heuristic: Is it in a "Save" directory?
            // If we have "Assets/data.bin" -> Reject.
            // If we have "SaveData/data.bin" -> Accept.
            var directoryName = Path.GetFileName(Path.GetDirectoryName(path));
            if (!string.IsNullOrEmpty(directoryName) &&
                options.SaveDirectorySuffixPatterns.Any(p => directoryName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                Logger?.Invoke($"[Voting] Path accepted (Blacklisted extension but in Save Dir): {path}", LogLevel.Debug);
                return true;
            }

            // C. Otherwise, it's just a blacklisted file (e.g. .png in Assets)
            Logger?.Invoke($"[Voting] Path rejected by extension blacklist: {path}", LogLevel.Debug);
            return false;
        }

        // 3. Default Accept
        Logger?.Invoke($"[Voting] Path accepted (Passed Basic Filters): {path}", LogLevel.Debug);
        return true;
    }

    private bool IsBlacklisted(string path, string[] blacklist)
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
            // Find next occurrence
            var remaining = text.Slice(index);
            int found = remaining.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;

            int actualIndex = index + found;
            int endIndex = actualIndex + word.Length;

            // Check boundaries
            bool startBoundary = actualIndex == 0 || !char.IsLetterOrDigit(text[actualIndex - 1]);
            bool endBoundary = endIndex == text.Length || !char.IsLetterOrDigit(text[endIndex]);

            if (startBoundary && endBoundary) return true;

            // Move past this occurrence
            index = actualIndex + 1;
        }
    }
    public string? FindBestSaveDirectory(List<PathCandidate> candidates, SaveDetectorOptions options, Galgame? game = null)
    {
        if (candidates == null || candidates.Count == 0) return null;

        Logger?.Invoke($"[Voting] Starting analysis with {candidates.Count} candidates", LogLevel.Debug);

        // Update variants if game context is provided
        if (game != null && game != _cachedGame)
        {
            PrecomputeVariants(game);
            _cachedGame = game;
        }

                var directoryScores = new Dictionary<string, ScoredPath>(StringComparer.OrdinalIgnoreCase);
                var currentAppPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                var gameRoot = game?.LocalPath;
        
                foreach (var candidate in candidates)
                {
                    var path = candidate.Path;
                    var directory = Path.GetDirectoryName(path);
                    if (Directory.Exists(path) && !File.Exists(path)) directory = path;
        
                    if (string.IsNullOrEmpty(directory)) continue;
                    
                    var normalizedDir = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        
                    // Skip generic system roots (Centralized check)
                    if (options.GenericRoots.Contains(normalizedDir)) continue;
        
                    if (IsPathExcluded(directory, currentAppPath, options, gameRoot)) continue;
        
                    if (!directoryScores.TryGetValue(normalizedDir, out var scored))
                    {
                        scored = new ScoredPath { Path = directory };
                        directoryScores[normalizedDir] = scored;
                    }
            // 1. Base Score based on Operation
            // Increased weights to prioritize active changes
            double baseScore = candidate.Op switch
            {
                IoOperation.Write => 50,
                IoOperation.Rename => 70,
                IoOperation.Create => 5, // Slightly higher base
                _ => 2
            };

            var ext = Path.GetExtension(path).TrimStart('.');
            bool isWhitelistedExt = options.SaveExtensionWhitelist.Any(w => w.Equals(ext, StringComparison.OrdinalIgnoreCase));
            bool hasSaveKeyword = options.SaveKeywordWhitelist.Any(w => Path.GetFileName(path).Contains(w, StringComparison.OrdinalIgnoreCase));
            bool isLikelySaveDirectory = false;

            var dirName = Path.GetFileName(directory);
            if (!string.IsNullOrEmpty(dirName))
            {
                isLikelySaveDirectory = options.SaveDirectorySuffixPatterns.Any(p => dirName.Contains(p, StringComparison.OrdinalIgnoreCase));
            }

            // 2. Extension Bonus (Crucial for identifying known save formats)
            if (isWhitelistedExt)
            {
                baseScore += 200; // Major boost for known save extensions
            }

            // 3. Game Root Heuristic / Penalty
            // If it's a Create in game directory without strong evidence, it's likely an asset read.
            if (candidate.Op == IoOperation.Create &&
                game?.LocalPath != null && path.StartsWith(game.LocalPath, StringComparison.OrdinalIgnoreCase))
            {
                // We skip penalty if:
                // a) It has a known save extension
                // b) It has a save keyword in filename
                // c) It is in a directory that looks like a save folder (e.g. "UserData")
                bool isStrongCandidate = isWhitelistedExt || hasSaveKeyword || isLikelySaveDirectory;

                if (!isStrongCandidate)
                {
                    baseScore = 0.1; // Almost ignore
                }
            }

            scored.Score += baseScore;
            scored.VoteCount++;

            // 4. File Quality & Size Score (Full weight now)
            scored.Score += CalculateSaveFileScore(path, options);

            // 5. Provider Bonus
            if (candidate.Source == ProviderSource.ETW)
                scored.Score += options.EtwBonusWeight;
        }

        // --- Filename Similarity Mechanism (Super Bonus) ---
        var similarityWinner = GetSimilarityMechanismWinner(candidates, options);
        if (similarityWinner != null)
        {
            var normalizedSimilarityDir = similarityWinner.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (directoryScores.TryGetValue(normalizedSimilarityDir, out var scored))
            {
                scored.Score += 1000; // Provide a massive bonus to ensure priority
                Logger?.Invoke($"[Voting] Filename similarity winner '{similarityWinner}' received a Super Bonus (+1000)", LogLevel.Debug);
            }
        }

        // Post-process directory scores
        foreach (var kvp in directoryScores)
        {
            var dir = kvp.Value.Path;

            // 4. Variant Matching Score
            kvp.Value.Score += CalculateDirectoryVariantMatchScore(dir);

            // 5. Path Structure Score
            kvp.Value.Score += GetPathStructureScore(dir, options);

            Logger?.Invoke($"[Voting] Candidate: {dir} | Score: {kvp.Value.Score} | Votes: {kvp.Value.VoteCount}", LogLevel.Debug);
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

        // Take top 2 similarity pairs
        var topTwo = similarities.OrderByDescending(s => s.Sim).Take(2).ToList();

        var dirCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in topTwo)
        {
            dirCounts[pair.Dir1] = dirCounts.GetValueOrDefault(pair.Dir1) + 1;
            dirCounts[pair.Dir2] = dirCounts.GetValueOrDefault(pair.Dir2) + 1;
        }

        int totalInvolvedPaths = topTwo.Count * 2;
        // Winner must appear in more than half of the involved paths
        var winner = dirCounts.OrderByDescending(x => x.Value).FirstOrDefault(x => x.Value > totalInvolvedPaths / 2);

        return winner.Key;
    }

    private bool IsPathExcluded(string path, string appPath, SaveDetectorOptions options, string? gameRoot = null)
    {
        if (string.IsNullOrEmpty(path)) return true;
        if (!string.IsNullOrEmpty(appPath) && path.StartsWith(appPath, StringComparison.OrdinalIgnoreCase)) return true;

        // If the path is inside the game root, we don't apply the blacklist
        if (gameRoot != null && path.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        // Check path blacklist again for directory parts
        return options.PathBlacklist.Any(b => path.Contains(b, StringComparison.OrdinalIgnoreCase));
    }

    private double CalculateSaveFileScore(string filePath, SaveDetectorOptions options)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);

            // Hard limit: Save files are almost never > 100MB. 
            // Most game assets (pac/arc) are huge.
            if (fileInfo.Length > 100 * 1024 * 1024) return -500;

            var score = 0.0;

            // Size heuristics
            if (fileInfo.Length > 1024 && fileInfo.Length < 10 * 1024 * 1024) score += 30;
            else if (fileInfo.Length > 100 && fileInfo.Length < 50 * 1024 * 1024) score += 20;

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