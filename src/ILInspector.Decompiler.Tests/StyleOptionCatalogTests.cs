using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The library-owned <see cref="StyleOptionCatalog"/> is the single source of
/// truth for the opt-in boolean <see cref="PrinterOptions"/> knobs — their
/// identity, tier/contract, oracle endorsement, config key, mutual-exclusivity,
/// and NativeAOT-safe accessors. These tests pin that contract so a host (the CLI
/// resolver, a Wasm UI, the future "full taste" aggregate) can rely on it, and so
/// the catalog cannot silently drift from the <see cref="PrinterOptions"/> surface.
/// </summary>
public class StyleOptionCatalogTests
{
    private static IReadOnlyList<StyleOptionDescriptor> Options => StyleOptionCatalog.Options;

    // Every public instance boolean property on PrinterOptions is a knob a host may
    // want to discover and toggle. Reflection here is a test-only drift guard (never
    // a product path): if a new boolean knob lands without a catalog entry, this
    // fails and forces the catalog — the single source of truth — to be updated.
    private static IReadOnlyList<PropertyInfo> BooleanKnobProperties =>
        typeof(PrinterOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool))
            .ToArray();

    [Fact]
    public void EveryBooleanPrinterOption_HasExactlyOneCatalogEntry()
    {
        Assert.Equal(BooleanKnobProperties.Count, Options.Count);
    }

    [Fact]
    public void Ids_AreNonEmpty_AndUnique()
    {
        Assert.All(Options, o => Assert.False(string.IsNullOrWhiteSpace(o.Id)));
        Assert.Equal(Options.Count, Options.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TitlesAndSummaries_AreNonEmpty()
    {
        Assert.All(Options, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Title));
            Assert.False(string.IsNullOrWhiteSpace(o.Summary));
        });
    }

    [Fact]
    public void ConfigKeys_WherePresent_AreUnique()
    {
        var keys = Options.Select(o => o.ConfigKey).Where(k => k is not null).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void GetWith_RoundTripsEachKnob_OffByDefault()
    {
        foreach (var o in Options)
        {
            Assert.False(o.Get(PrinterOptions.Default), $"{o.Id} should be off in the shipped default");

            var enabled = o.With(PrinterOptions.Default, true);
            Assert.True(o.Get(enabled), $"{o.Id} should read back true after With(true)");

            var disabled = o.With(enabled, false);
            Assert.False(o.Get(disabled), $"{o.Id} should read back false after With(false)");
        }
    }

    [Fact]
    public void With_TogglesExactlyOneKnob_LeavingOthersUntouched()
    {
        // Enabling one knob must not flip any other knob's Get — proof that the
        // delegates are isolated and target distinct PrinterOptions properties.
        foreach (var subject in Options)
        {
            var enabled = subject.With(PrinterOptions.Default, true);
            foreach (var other in Options)
            {
                var expected = ReferenceEquals(other, subject);
                Assert.Equal(expected, other.Get(enabled));
            }
        }
    }

    [Fact]
    public void ByteDivergent_IsExactlyTheLensTier()
    {
        Assert.All(Options, o => Assert.Equal(o.Tier == StyleOptionTier.Lens, o.ByteDivergent));
    }

    [Fact]
    public void GuardedBooleanReturnGroup_IsExactlyTheTwoLenses()
    {
        var grouped = Options
            .Where(o => o.ConflictGroup == StyleOptionCatalog.GuardedBooleanReturnGroup)
            .Select(o => o.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "prefer-branchless-boolean", "prefer-conditional-expression-return" }, grouped);
        // Both members of a conflict group are byte-divergent lenses.
        Assert.All(
            Options.Where(o => o.ConflictGroup == StyleOptionCatalog.GuardedBooleanReturnGroup),
            o => Assert.Equal(StyleOptionTier.Lens, o.Tier));
    }

    [Fact]
    public void OracleEndorsedSet_ExcludesTheBranchlessLens()
    {
        var endorsed = Options.Where(o => o.OracleEndorsed).Select(o => o.Id).ToArray();
        Assert.Contains("prefer-conditional-expression-return", endorsed);
        Assert.DoesNotContain("prefer-branchless-boolean", endorsed);
        // The branchless lens uses a tool-owned key, never a dotnet_style_* one.
        var branchless = Options.Single(o => o.Id == "prefer-branchless-boolean");
        Assert.StartsWith("dotnet_inspect_style_", branchless.ConfigKey);
    }

    [Fact]
    public void OracleEndorsedKnobsWithAConfigKey_UseTheEditorconfigVocabulary()
    {
        foreach (var o in Options.Where(o => o.OracleEndorsed && o.ConfigKey is not null))
            Assert.StartsWith("dotnet_style_", o.ConfigKey);
    }
}
