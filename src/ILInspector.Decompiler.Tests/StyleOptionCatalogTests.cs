using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The library-owned <see cref="StyleOptionCatalog"/> is the single source of
/// truth for the opt-in <see cref="PrinterOptions"/> knobs — their identity,
/// tier/contract, value domain, oracle endorsement, config keys, and
/// NativeAOT-safe accessors. These tests pin that contract so a host (the CLI
/// resolver, a Wasm UI, the "full taste" aggregate) can rely on it, and so the
/// catalog cannot silently drift from the <see cref="PrinterOptions"/> surface.
/// Most knobs are two-state (boolean) toggles; the guarded-boolean-return knob is
/// a single multi-value axis whose value domain the descriptor carries directly.
/// </summary>
public class StyleOptionCatalogTests
{
    private static IReadOnlyList<StyleOptionDescriptor> Options => StyleOptionCatalog.Options;

    private const string GuardedReturnId = "guarded-boolean-return-style";

    // Every public instance boolean property on PrinterOptions is backing state a
    // host may want to discover and drive through the catalog. Reflection here is a
    // test-only drift guard (never a product path): if a new boolean knob lands
    // without a catalog value that reaches it, the coverage test below fails and
    // forces the catalog — the single source of truth — to be updated.
    private static IReadOnlyList<PropertyInfo> BooleanKnobProperties =>
        typeof(PrinterOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool))
            .ToArray();

    private static IReadOnlyList<StyleOptionValue> AllValues =>
        Options.SelectMany(o => o.Values).ToArray();

    private static ISet<string> ChangedBoolProps(PrinterOptions before, PrinterOptions after) =>
        BooleanKnobProperties
            .Where(p => (bool)p.GetValue(before)! != (bool)p.GetValue(after)!)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryBackingBooleanProperty_IsReachableThroughSomeCatalogValue()
    {
        // Drift guard: selecting each value (from the shipped default) must, taken
        // together, be able to drive every backing PrinterOptions boolean. A new
        // boolean knob with no catalog value to set it fails here.
        var reached = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in AllValues)
            reached.UnionWith(ChangedBoolProps(PrinterOptions.Default, value.SetSelected(PrinterOptions.Default, true)));

        var expected = BooleanKnobProperties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expected, reached);
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
    public void Values_AreNonEmpty_WithUniqueTokens_AndAValidDefault()
    {
        Assert.All(Options, o =>
        {
            Assert.NotEmpty(o.Values);
            var tokens = o.Values.Select(v => v.Token).ToArray();
            Assert.All(tokens, t => Assert.False(string.IsNullOrWhiteSpace(t)));
            Assert.Equal(tokens.Length, tokens.Distinct(StringComparer.Ordinal).Count());
            // The declared default must be a real token on the axis, and it must be
            // the value in effect on the shipped default options.
            Assert.Contains(o.DefaultValue, tokens);
            Assert.Equal(o.DefaultValue, o.GetValue(PrinterOptions.Default));
        });
    }

    [Fact]
    public void ConfigKeys_WherePresent_AreUnique()
    {
        var keys = AllValues.Select(v => v.ConfigKey).Where(k => k is not null).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void GetValueWithValue_RoundTripsEachValue_FromTheDefault()
    {
        foreach (var o in Options)
        {
            Assert.Equal(o.DefaultValue, o.GetValue(PrinterOptions.Default));

            foreach (var value in o.Values)
            {
                var selected = o.WithValue(PrinterOptions.Default, value.Token);
                Assert.Equal(value.Token, o.GetValue(selected));

                // Returning to the default token clears the axis again.
                var cleared = o.WithValue(selected, o.DefaultValue);
                Assert.Equal(o.DefaultValue, o.GetValue(cleared));
            }
        }
    }

    [Fact]
    public void WithValue_SetsOneAxis_LeavingEveryOtherAxisAtItsDefault()
    {
        // Single-selecting a non-default value on one descriptor must not move any
        // other descriptor off its default — proof the delegates target disjoint
        // backing state.
        foreach (var subject in Options)
        {
            var nonDefault = subject.Values.First(v => !string.Equals(v.Token, subject.DefaultValue, StringComparison.Ordinal));
            var mutated = subject.WithValue(PrinterOptions.Default, nonDefault.Token);

            foreach (var other in Options)
            {
                if (ReferenceEquals(other, subject))
                    continue;

                Assert.Equal(other.DefaultValue, other.GetValue(mutated));
            }
        }
    }

    [Fact]
    public void ConfigKeyFalse_TogglesAnAxisOffWithoutTouchingSiblings()
    {
        // A boolean knob's key = false is the per-value SetSelected(false) path: it
        // clears its own backing state and nothing else. Proven on the four
        // qualification knobs (each is a two-state axis with a config key).
        foreach (var o in Options.Where(o => o.Values.Count == 2 && o.ConfigKey is not null))
        {
            var onValue = o.Values.Single(v => v.ConfigKey is not null);
            var enabled = onValue.SetSelected(PrinterOptions.Default, true);
            Assert.Equal(onValue.Token, o.GetValue(enabled));

            var disabled = onValue.SetSelected(enabled, false);
            Assert.Equal(o.DefaultValue, o.GetValue(disabled));
        }
    }

    [Fact]
    public void ByteDivergent_IsExactlyTheLensTier()
    {
        Assert.All(Options, o => Assert.Equal(o.Tier == StyleOptionTier.Lens, o.ByteDivergent));
    }

    [Fact]
    public void GuardedBooleanReturn_IsOneTriStateLensAxis()
    {
        var guarded = Options.Single(o => o.Id == GuardedReturnId);
        Assert.Equal(StyleOptionTier.Lens, guarded.Tier);
        Assert.True(guarded.ByteDivergent);

        var tokens = guarded.Values.Select(v => v.Token).ToArray();
        Assert.Equal(new[] { "flat", "conditional-expression", "branchless" }, tokens);
        // The byte-faithful flat spelling is the default (no lens applied).
        Assert.Equal("flat", guarded.DefaultValue);
    }

    [Fact]
    public void GuardedBooleanReturn_EndorsesTheTernary_NotTheBranchless()
    {
        var guarded = Options.Single(o => o.Id == GuardedReturnId);

        var endorsed = guarded.EndorsedValue;
        Assert.NotNull(endorsed);
        Assert.Equal("conditional-expression", endorsed!.Token);
        Assert.StartsWith("dotnet_style_", endorsed.ConfigKey);

        var branchless = guarded.Values.Single(v => v.Token == "branchless");
        Assert.False(branchless.OracleEndorsed);
        // The branchless "bool hack" uses a tool-owned key, never a dotnet_style_* one.
        Assert.StartsWith("dotnet_inspect_style_", branchless.ConfigKey);
    }

    [Fact]
    public void EndorsedValuesWithAConfigKey_UseTheEditorconfigVocabulary()
    {
        foreach (var o in Options)
            if (o.EndorsedValue is { ConfigKey: not null } endorsed)
                Assert.StartsWith("dotnet_style_", endorsed.ConfigKey);
    }

    [Fact]
    public void AtMostOneValuePerAxis_IsOracleEndorsed()
    {
        Assert.All(Options, o => Assert.True(o.Values.Count(v => v.OracleEndorsed) <= 1));
    }

    [Fact]
    public void WrapExpressionBodyArrow_IsAnApiOnlyFormattingKnob()
    {
        // The former ExpressionBodyArrowPlacement enum is now a two-state toggle: a
        // whitespace-only formatting choice with no config key and no oracle
        // endorsement (the shipped default keeps the arrow on the same line;
        // wrapping it is a user preference, not the oracle's).
        var arrow = Options.Single(o => o.Id == "wrap-expression-body-arrow");
        Assert.Equal(StyleOptionTier.Formatting, arrow.Tier);
        Assert.False(arrow.ByteDivergent);
        Assert.False(arrow.OracleEndorsed);
        Assert.Null(arrow.ConfigKey);
        Assert.Equal("false", arrow.GetValue(PrinterOptions.Default));
        Assert.Equal("true", arrow.GetValue(arrow.WithValue(PrinterOptions.Default, "true")));
    }

    [Fact]
    public void OracleEndorsedOptions_IsExactlyTheOracleEndorsedDescriptors()
    {
        // The "full taste" member list is precisely the oracle-endorsed filter over
        // Options — same instances, same order — and nothing else.
        Assert.Equal(
            Options.Where(o => o.OracleEndorsed).ToArray(),
            StyleOptionCatalog.OracleEndorsedOptions.ToArray());
        Assert.All(StyleOptionCatalog.OracleEndorsedOptions, o => Assert.True(o.OracleEndorsed));
    }

    [Fact]
    public void OracleEndorsedOptions_AreExactlyTheFourQualificationsAndTheGuardedReturn()
    {
        // Pin the intended "full taste" subset to literal ids, independent of the
        // per-value OracleEndorsed flag the production filter reads. Without this,
        // mismarking a knob would silently widen the aggregate while every
        // flag-derived test still passed.
        var expected = new[]
        {
            "guarded-boolean-return-style",
            "qualify-event-access",
            "qualify-field-access",
            "qualify-method-access",
            "qualify-property-access",
        };

        var actual = StyleOptionCatalog.OracleEndorsedOptions
            .Select(o => o.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ApplyFullTaste_SelectsExactlyTheEndorsedValueOnEachAxis()
    {
        var full = StyleOptionCatalog.ApplyFullTaste(PrinterOptions.Default);

        foreach (var o in Options)
        {
            var expected = o.EndorsedValue?.Token ?? o.DefaultValue;
            Assert.Equal(expected, o.GetValue(full));
        }
    }

    [Fact]
    public void ApplyFullTaste_ResolvesGuardedBooleanReturn_ToTheTernary()
    {
        // The aggregate picks the oracle-endorsed ternary and never the branchless
        // "bool hack": the axis resolves deterministically to conditional-expression.
        var full = StyleOptionCatalog.ApplyFullTaste(PrinterOptions.Default);

        var guarded = Options.Single(o => o.Id == GuardedReturnId);
        Assert.Equal("conditional-expression", guarded.GetValue(full));
        Assert.True(full.PreferConditionalExpressionReturn);
        Assert.False(full.PreferBranchlessBoolean);
    }

    [Fact]
    public void ApplyFullTaste_False_DeselectsExactlyTheEndorsedValues()
    {
        // Turn full taste on, then apply it with enabled: false — every endorsed
        // value is deselected and the aggregate leaves the render byte-faithful
        // again (no endorsed value selected on any axis).
        var full = StyleOptionCatalog.ApplyFullTaste(PrinterOptions.Default);
        var off = StyleOptionCatalog.ApplyFullTaste(full, enabled: false);

        foreach (var o in Options)
            if (o.EndorsedValue is { } endorsed)
                Assert.False(endorsed.IsSelected(off), $"{o.Id}: endorsed value should be deselected");
    }

    // ---- corpus (revealed-preference) endorsement axis (#3179) ----

    [Fact]
    public void CorpusEndorsedOptions_IsExactlyTheCorpusEndorsedDescriptors()
    {
        Assert.Equal(
            Options.Where(o => o.CorpusEndorsed).ToArray(),
            StyleOptionCatalog.CorpusEndorsedOptions.ToArray());
        Assert.All(StyleOptionCatalog.CorpusEndorsedOptions, o => Assert.True(o.CorpusEndorsed));
    }

    [Fact]
    public void CorpusEndorsedOptions_AreExactlyTheWrapSplittableKnob()
    {
        // Pin the first revealed-preference classification to literal ids,
        // independent of the CorpusEndorsed flag the production filter reads. The
        // runtime corpus wraps long boolean chains (matching its 120-column
        // practice); the other formatting/synthesis knobs are deliberately left
        // un-endorsed pending evidence, so mismarking one fails here.
        var actual = StyleOptionCatalog.CorpusEndorsedOptions
            .Select(o => o.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "wrap-splittable-expressions" }, actual);
    }

    [Fact]
    public void TheTwoEndorsementFacets_AreIndependentFlags()
    {
        // The two axes are orthogonal by contract. In today's catalog no knob is
        // endorsed by both facets (the subsets are disjoint), but this asserts the
        // flags are read independently, not that one implies the other.
        var declaredButNotRevealed = Options.Where(o => o.OracleEndorsed && !o.CorpusEndorsed).ToArray();
        var revealedButNotDeclared = Options.Where(o => o.CorpusEndorsed && !o.OracleEndorsed).ToArray();

        Assert.NotEmpty(declaredButNotRevealed); // e.g. the this.-qualifications
        Assert.NotEmpty(revealedButNotDeclared); // e.g. wrap-splittable-expressions
    }

    [Fact]
    public void BranchlessValue_IsEndorsedByNeitherFacet()
    {
        // The idiosyncratic "bool hack" is the canonical neither-facet value on the
        // guarded-boolean-return axis: no .editorconfig rule and no revealed corpus
        // practice.
        var guarded = Options.Single(o => o.Id == GuardedReturnId);
        var branchless = guarded.Values.Single(v => v.Token == "branchless");
        Assert.False(branchless.OracleEndorsed);
        Assert.False(branchless.CorpusEndorsed);
    }

    [Fact]
    public void WrapExpressionBodyArrow_IsEndorsedByNeitherFacet()
    {
        // Wrapping the expression-body arrow is a user preference, not the corpus's
        // practice: the runtime keeps => on the same line, so the shipped default
        // (arrow.Get == false) already matches the corpus and enabling the knob
        // diverges from it. So it is neither declared- nor revealed-endorsed.
        var arrow = Options.Single(o => o.Id == "wrap-expression-body-arrow");
        Assert.False(arrow.OracleEndorsed);
        Assert.False(arrow.CorpusEndorsed);
    }
}
