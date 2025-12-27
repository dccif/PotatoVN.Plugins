using GalgameManager.Models;
using PotatoVN.App.PluginBase.SaveDetection.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PotatoVN.App.PluginBase.SaveDetection.Helpers;

public class GameVariantHelper
{
    private List<string>? _cachedVariants;
    private string? _lastGameIdentifier;

    public List<string> GetVariants(Galgame game)
    {
        if (game == null) return new List<string>();

        var gameIdentifier =
            $"{game.Name?.Value}_{game.ChineseName}_{game.OriginalName?.Value}_{game.Developer?.Value}";

        if (_cachedVariants != null && _lastGameIdentifier == gameIdentifier) return _cachedVariants;

        var allVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Name Variants
        GenerateNameVariants(game.Name?.Value, allVariants);
        GenerateNameVariants(game.ChineseName, allVariants);
        GenerateNameVariants(game.OriginalName?.Value, allVariants);

        // 2. Developer Variants
        GenerateDeveloperVariants(game.Developer?.Value, allVariants);

        // 3. Category Variants
        if (game.Categories != null)
            foreach (var category in game.Categories)
                if (!string.IsNullOrEmpty(category.Name))
                    GenerateNameVariants(category.Name, allVariants);

        _cachedVariants = allVariants.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        _lastGameIdentifier = gameIdentifier;

        return _cachedVariants;
    }

    private void GenerateNameVariants(string? name, HashSet<string> variants)
    {
        if (string.IsNullOrEmpty(name)) return;

        variants.Add(name);
        variants.Add(name.ToLowerInvariant());

        GenerateSeparatorVariants(name, variants);
        GenerateAbbreviationVariants(name, variants);
        GenerateSpecialCharacterVariants(name, variants);
    }

    private void GenerateSeparatorVariants(string name, HashSet<string> variants)
    {
        foreach (var currentSep in Constants.CurrentSeparators)
        {
            if (!name.Contains(currentSep)) continue;

            var parts = name.Split(currentSep, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            foreach (var newSep in Constants.Separators)
            {
                if (newSep == currentSep) continue;
                if (newSep == '\0') continue;

                var variant = string.Join(newSep.ToString(), parts);
                variants.Add(variant);
            }

            var noSepVariant = string.Join("", parts);
            variants.Add(noSepVariant);
        }
    }

    private void GenerateAbbreviationVariants(string name, HashSet<string> variants)
    {
        var words = name.Split(Constants.CurrentSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1)
        {
            var abbreviation = new string(words.Select(word =>
                string.IsNullOrEmpty(word) ? ' ' : char.ToUpperInvariant(word[0])).ToArray());
            variants.Add(abbreviation);
        }

        ApplyWordSimplifications(name, variants);
    }

    private void GenerateSpecialCharacterVariants(string name, HashSet<string> variants)
    {
        ApplyJapaneseConversions(name, variants);
        var numberVariants = ApplyNumberConversions(name);
        foreach (var variant in numberVariants) variants.Add(variant);
    }

    private void GenerateDeveloperVariants(string? developer, HashSet<string> variants)
    {
        if (string.IsNullOrEmpty(developer)) return;

        GenerateNameVariants(developer, variants);
        var devVariants = GetDeveloperVariants(developer);
        foreach (var variant in devVariants) variants.Add(variant);
    }

    // Moved from SaveDetectionConstants
    private List<string> GetDeveloperVariants(string developer)
    {
        if (string.IsNullOrEmpty(developer)) return new List<string>();

        var lowerDev = developer.ToLowerInvariant();

        foreach (var kvp in Constants.DeveloperVariants)
            if (lowerDev.Contains(kvp.Key))
                return kvp.Value;

        return new List<string>();
    }

    private void ApplyWordSimplifications(string name, HashSet<string> variants)
    {
        var lowerName = name.ToLowerInvariant();

        foreach (var simplification in Constants.WordSimplifications)
            if (lowerName.Contains(simplification.Key))
                foreach (var replacement in simplification.Value)
                {
                    var simplified = lowerName.Replace(simplification.Key, replacement);
                    variants.Add(simplified);

                    var noSepVersion = simplified.Replace("_", "").Replace("-", "").Replace(" ", "");
                    if (noSepVersion != simplified) variants.Add(noSepVersion);
                }
    }

    private void ApplyJapaneseConversions(string name, HashSet<string> variants)
    {
        foreach (var mapping in Constants.JapaneseMappings)
            if (name.Contains(mapping.Key))
                foreach (var variant in mapping.Value)
                {
                    var converted = name.ToLowerInvariant().Replace(mapping.Key, variant);
                    variants.Add(converted);
                }
    }

    private List<string> ApplyNumberConversions(string name)
    {
        var variants = new List<string>();
        var result = name;

        foreach (var mapping in Constants.NumberMappings)
        {
            result = result.Replace(mapping.Key, mapping.Value);
            variants.Add(result);
        }

        return variants;
    }
}