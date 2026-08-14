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

    // Every public instance option property is backing state a host may want to
    // discover and drive through the catalog. Reflection here is a test-only drift
    // guard (never a product path): if a new knob lands
    // without a catalog value that reaches it, the coverage test below fails and
    // forces the catalog — the single source of truth — to be updated.
    private static IReadOnlyList<PropertyInfo> OptionProperties =>
        typeof(PrinterOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToArray();

    private static IReadOnlyList<StyleOptionValue> AllValues =>
        Options.SelectMany(o => o.Values).ToArray();

    private static ISet<string> ChangedProps(PrinterOptions before, PrinterOptions after) =>
        OptionProperties
            .Where(p => !Equals(p.GetValue(before), p.GetValue(after)))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryBackingProperty_IsReachableThroughSomeCatalogValue()
    {
        // Drift guard: selecting each value (from the shipped default) must, taken
        // together, be able to drive every backing PrinterOptions boolean. A new
        // boolean knob with no catalog value to set it fails here.
        var reached = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in AllValues)
            reached.UnionWith(ChangedProps(PrinterOptions.Default, value.SetSelected(PrinterOptions.Default, true)));

        var expected = OptionProperties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
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
            // the value in effect on the registry-derived product defaults.
            Assert.Contains(o.DefaultValue, tokens);
            Assert.Equal(o.DefaultValue, o.GetValue(StyleOptionCatalog.DefaultOptions));
        });
    }

    [Fact]
    public void Choices_AreExactlyTheNonDefaultCatalogValues()
    {
        var expected = Options
            .SelectMany(option => option.Values
                .Where(value => !string.Equals(
                    value.Token,
                    option.DefaultValue,
                    StringComparison.Ordinal))
                .Select(value => (OptionId: option.Id, ValueToken: value.Token)))
            .ToArray();
        var actual = StyleOptionCatalog.Choices
            .Select(choice => (choice.OptionId, choice.ValueToken))
            .ToArray();

        Assert.Equal(expected, actual);
        Assert.All(
            StyleOptionCatalog.Choices,
            choice => Assert.False(string.IsNullOrWhiteSpace(choice.Id)));
        Assert.Equal(
            StyleOptionCatalog.Choices.Count,
            StyleOptionCatalog.Choices.Select(choice => choice.Id)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(Options, option =>
        {
            Assert.Null(option.Values.Single(
                value => value.Token == option.DefaultValue).ChoiceId);
            Assert.All(
                option.Values.Where(value => value.Token != option.DefaultValue),
                value => Assert.False(string.IsNullOrWhiteSpace(value.ChoiceId)));
        });
    }

    [Fact]
    public void ChoiceIds_PreserveTheExistingBrowserPersistenceContract()
    {
        // Literal ids are the compatibility gate. In particular, a currently
        // two-state option keeps its bare id even if a future value joins that
        // axis; the id is stored on the value rather than recomputed from the
        // axis's current cardinality.
        Assert.Equal(
            new[]
            {
                "slot-local-names",
                "wrap-splittable-expressions",
                "disable-one-liner-wrapping",
                "wrap-expression-body-arrow",
                "qualify-field-access",
                "qualify-property-access",
                "qualify-method-access",
                "qualify-event-access",
                "guarded-boolean-return-style:conditional-expression",
                "guarded-boolean-return-style:branchless",
                "var-spelling-style:var-for-built-in-types",
                "var-spelling-style:var-when-type-apparent",
                "var-spelling-style:var-elsewhere",
                "enum-case-label-order",
                "prefer-long-literal-suffix",
            },
            StyleOptionCatalog.Choices.Select(choice => choice.Id).ToArray());
    }

    [Fact]
    public void Choices_CannotBeMutatedThroughTheirRuntimeCollection()
    {
        var mutable = StyleOptionCatalog.Choices as IList<StyleOptionChoice>;

        Assert.True(mutable is null || mutable.IsReadOnly);
        if (mutable is not null)
            Assert.Throws<NotSupportedException>(mutable.Clear);
    }

    [Fact]
    public void Choices_ProjectProductOwnedPresentationAndConflictSemantics()
    {
        foreach (var choice in StyleOptionCatalog.Choices)
        {
            var option = Options.Single(candidate => candidate.Id == choice.OptionId);
            var value = option.Values.Single(candidate => candidate.Token == choice.ValueToken);
            int selectableCount = option.Values.Count(candidate =>
                candidate.Token != option.DefaultValue);

            Assert.Equal(value.ChoiceId, choice.Id);
            Assert.Equal(
                selectableCount > 1
                    ? $"{option.Title} · {value.Title ?? value.Token}"
                    : option.Title,
                choice.Title);
            Assert.Equal(option.Summary, choice.Summary);
            Assert.Equal(option.Tier, choice.Tier);
            Assert.Equal(option.ByteDivergent, choice.ByteDivergent);
            Assert.Equal(value.OracleEndorsed, choice.OracleEndorsed);
            Assert.Equal(value.CorpusEndorsed, choice.CorpusEndorsed);
            Assert.Equal(selectableCount > 1 ? option.Id : null, choice.ConflictGroup);
        }
    }

    [Fact]
    public void ResolveChoices_StartsFromProductDefaults_AndAppliesSelections()
    {
        Assert.Equal(
            StyleOptionCatalog.DefaultOptions,
            StyleOptionCatalog.ResolveChoices([]));

        var resolved = StyleOptionCatalog.ResolveChoices(
        [
            "slot-local-names",
            "qualify-field-access",
            "guarded-boolean-return-style:branchless",
        ]);

        Assert.False(resolved.ReadableLocalNames);
        Assert.True(resolved.QualifyFieldAccess);
        Assert.True(resolved.PreferBranchlessBoolean);
        Assert.False(resolved.PreferConditionalExpressionReturn);
    }

    [Fact]
    public void EveryChoice_ResolvesItsOwningValue_WithoutMovingOtherOptions()
    {
        foreach (var choice in StyleOptionCatalog.Choices)
        {
            var resolved = StyleOptionCatalog.ResolveChoices([choice.Id]);

            foreach (var option in Options)
            {
                Assert.Equal(
                    option.Id == choice.OptionId
                        ? choice.ValueToken
                        : option.GetValue(StyleOptionCatalog.DefaultOptions),
                    option.GetValue(resolved));
            }
        }
    }

    [Fact]
    public void ResolveChoices_IsOrderIndependentAcrossOptions_AndIgnoresDuplicates()
    {
        var first = StyleOptionCatalog.ResolveChoices(
        [
            "qualify-field-access",
            "prefer-long-literal-suffix",
            "qualify-field-access",
        ]);
        var second = StyleOptionCatalog.ResolveChoices(
        [
            "prefer-long-literal-suffix",
            "qualify-field-access",
        ]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ResolveChoices_RejectsUnknownAndConflictingIds()
    {
        var unknown = Assert.Throws<ArgumentException>(() =>
            StyleOptionCatalog.ResolveChoices(["not-a-style-choice"]));
        Assert.Contains("not a style choice", unknown.Message, StringComparison.Ordinal);

        var conflict = Assert.Throws<ArgumentException>(() =>
            StyleOptionCatalog.ResolveChoices(
            [
                "guarded-boolean-return-style:conditional-expression",
                "guarded-boolean-return-style:branchless",
            ]));
        Assert.Contains("conflict", conflict.Message, StringComparison.Ordinal);
        Assert.Contains("guarded-boolean-return-style", conflict.Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() =>
            StyleOptionCatalog.ResolveChoices(
            [
                "var-spelling-style:var-for-built-in-types",
                "var-spelling-style:var-elsewhere",
            ]));

        Assert.Throws<ArgumentException>(() =>
            StyleOptionCatalog.ResolveChoices([null!]));
    }

    [Fact]
    public void ConfigKeys_WherePresent_AreUnique()
    {
        var keys = AllValues.Select(v => v.ConfigKey)
            .Concat(Options.Select(o => o.ValueConfigKey))
            .Where(k => k is not null)
            .ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void GetValueWithValue_RoundTripsEachValue_FromTheDefault()
    {
        foreach (var o in Options)
        {
            foreach (var value in o.Values)
            {
                var selected = o.WithValue(StyleOptionCatalog.DefaultOptions, value.Token);
                Assert.Equal(value.Token, o.GetValue(selected));

                // Returning to the default token clears the axis again.
                var cleared = o.WithValue(selected, o.DefaultValue);
                Assert.Equal(o.DefaultValue, o.GetValue(cleared));
            }
        }
    }

    [Fact]
    public void ProductDefaults_EnableReadableNames_WithoutChangingLibraryDefaults()
    {
        var slotNames = Options.Single(o => o.Id == "slot-local-names");

        Assert.Equal("false", slotNames.DefaultValue);
        Assert.True(StyleOptionCatalog.DefaultOptions.ReadableLocalNames);
        Assert.False(PrinterOptions.Default.ReadableLocalNames);
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
            var mutated = subject.WithValue(StyleOptionCatalog.DefaultOptions, nonDefault.Token);

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
            Assert.Equal("false", o.GetValue(disabled));
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
        var defaults = StyleOptionCatalog.DefaultOptions;
        var full = StyleOptionCatalog.ApplyFullTaste(defaults);

        foreach (var o in Options)
        {
            var expected = o.EndorsedValue?.Token ?? o.GetValue(defaults);
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

    [Fact]
    public void DisableOneLinerWrapping_IsEndorsedByNeitherFacet()
    {
        // Suppressing the always-on width wrappers is a user compactness preference,
        // not the corpus's practice: the runtime wraps wide constructs (the shipped
        // default keeps wrapping), so keeping a one-liner on one line diverges from
        // the corpus, and no .editorconfig rule declares it. Neither facet endorses it.
        var disable = Options.Single(o => o.Id == "disable-one-liner-wrapping");
        Assert.False(disable.OracleEndorsed);
        Assert.False(disable.CorpusEndorsed);
    }

    // ---- var-spelling family (#3169) ----

    private const string VarStyleId = "var-spelling-style";

    [Fact]
    public void VarSpelling_IsAByteNeutralFourValueSpellingAxis()
    {
        var varStyle = Options.Single(o => o.Id == VarStyleId);

        // A spelling choice (IL-identical), never a byte-divergent lens.
        Assert.Equal(StyleOptionTier.Spelling, varStyle.Tier);
        Assert.False(varStyle.ByteDivergent);

        var tokens = varStyle.Values.Select(v => v.Token).ToArray();
        Assert.Equal(
            new[] { "explicit", "var-for-built-in-types", "var-when-type-apparent", "var-elsewhere" },
            tokens);
        // Explicit is the shipped default (every csharp_style_var_* key off).
        Assert.Equal("explicit", varStyle.DefaultValue);
        Assert.Equal("explicit", varStyle.GetValue(PrinterOptions.Default));
    }

    [Fact]
    public void VarSpelling_ThreeCategories_MapToTheEditorconfigKeys()
    {
        var varStyle = Options.Single(o => o.Id == VarStyleId);

        Assert.Equal(
            "csharp_style_var_for_built_in_types",
            varStyle.Values.Single(v => v.Token == "var-for-built-in-types").ConfigKey);
        Assert.Equal(
            "csharp_style_var_when_type_is_apparent",
            varStyle.Values.Single(v => v.Token == "var-when-type-apparent").ConfigKey);
        Assert.Equal(
            "csharp_style_var_elsewhere",
            varStyle.Values.Single(v => v.Token == "var-elsewhere").ConfigKey);
        // The explicit default is not config-selectable (it is the absence of any key).
        Assert.Null(varStyle.Values.Single(v => v.Token == "explicit").ConfigKey);
    }

    [Fact]
    public void VarSpelling_CategoriesAreIndependent_EachKeySetsOnlyItsOwnBool()
    {
        var varStyle = Options.Single(o => o.Id == VarStyleId);
        var builtIn = varStyle.Values.Single(v => v.Token == "var-for-built-in-types");
        var elsewhere = varStyle.Values.Single(v => v.Token == "var-elsewhere");

        // Enabling two categories independently leaves both selected — they are not
        // mutually exclusive (a site falls into exactly one bucket, so both can be on).
        var both = elsewhere.SetSelected(builtIn.SetSelected(PrinterOptions.Default, true), true);
        Assert.True(both.PreferVarForBuiltInTypes);
        Assert.True(both.PreferVarElsewhere);
        Assert.False(both.PreferVarWhenTypeApparent);

        // Clearing one leaves the other set.
        var onlyElsewhere = builtIn.SetSelected(both, false);
        Assert.False(onlyElsewhere.PreferVarForBuiltInTypes);
        Assert.True(onlyElsewhere.PreferVarElsewhere);
    }

    [Fact]
    public void VarSpelling_IsEndorsedByNeitherFacet_SoItStaysOptInOnly()
    {
        // dotnet/runtime's .editorconfig sets every csharp_style_var_* key false
        // (prefer explicit), so no var value is oracle- or corpus-endorsed and the
        // family never joins the "full taste" aggregate.
        var varStyle = Options.Single(o => o.Id == VarStyleId);
        Assert.False(varStyle.OracleEndorsed);
        Assert.False(varStyle.CorpusEndorsed);
        Assert.DoesNotContain(StyleOptionCatalog.OracleEndorsedOptions, o => o.Id == VarStyleId);
        Assert.DoesNotContain(StyleOptionCatalog.CorpusEndorsedOptions, o => o.Id == VarStyleId);

        // Full taste leaves the axis on its explicit default.
        var full = StyleOptionCatalog.ApplyFullTaste(PrinterOptions.Default);
        Assert.Equal("explicit", varStyle.GetValue(full));
    }

    [Fact]
    public void EnumCaseLabelOrder_IsAByteNeutralTwoValueSpellingAxis()
    {
        var order = Options.Single(o => o.Id == "enum-case-label-order");

        Assert.Equal(StyleOptionTier.Spelling, order.Tier);
        Assert.False(order.ByteDivergent);
        Assert.Equal("dotnet_inspect_style_enum_case_label_order", order.ValueConfigKey);
        Assert.Equal(new[] { "alphabetical", "value" }, order.Values.Select(v => v.Token).ToArray());
        Assert.Equal("alphabetical", order.DefaultValue);
        Assert.Equal("alphabetical", order.GetValue(PrinterOptions.Default));
        Assert.False(order.OracleEndorsed);
        Assert.False(order.CorpusEndorsed);
    }

    [Fact]
    public void Tiers_CoverEveryTierExactlyOnce()
    {
        // The registry drives the grouping a host lays the catalog out with, so it
        // has to be total: a StyleOptionTier value with no descriptor would drop
        // its knobs out of a grouped picker silently. Set equality fails both a
        // missing entry and a stale one.
        var declared = Enum.GetValues<StyleOptionTier>().ToHashSet();
        var registered = StyleOptionCatalog.Tiers.Select(t => t.Id).ToArray();

        Assert.Equal(declared, registered.ToHashSet());
        Assert.Equal(registered.Length, registered.Distinct().Count());
    }

    [Fact]
    public void Tiers_AreListedInAscendingDisplayOrder_WithUniquePositions()
    {
        // Order is the product's presentation fact, so the list is already sorted
        // by it — a host renders Tiers as-is and never sorts. Positions are unique
        // so the layout cannot depend on an unstable tie-break.
        var orders = StyleOptionCatalog.Tiers.Select(t => t.Order).ToArray();

        Assert.Equal(orders.OrderBy(o => o).ToArray(), orders);
        Assert.Equal(orders.Length, orders.Distinct().Count());
    }

    [Fact]
    public void Tiers_HaveNonEmptyTitlesAndSummaries()
    {
        Assert.All(StyleOptionCatalog.Tiers, tier =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tier.Title));
            Assert.False(string.IsNullOrWhiteSpace(tier.Summary));
        });
    }

    [Fact]
    public void ByteDivergence_IsATierProperty()
    {
        // The tier states the contract its knobs honor, so a host can warn about a
        // whole group. Per-knob agreement makes that statement enforced rather than
        // documented: a byte-divergent knob filed under a byte-neutral tier fails
        // here, as does the reverse.
        Assert.All(Options, option =>
            Assert.Equal(StyleOptionCatalog.GetTier(option.Tier).ByteDivergent, option.ByteDivergent));

        // Non-vacuity in the other direction: a tier cannot claim byte-divergence
        // with no knob to witness it, so the registry's flag stays a statement
        // about real knobs. ByteDivergent_IsExactlyTheLensTier pins which tier
        // that is; this pins that the registry agrees with the knobs.
        Assert.Equal(
            StyleOptionCatalog.Tiers.Where(t => t.ByteDivergent).Select(t => t.Id).ToHashSet(),
            Options.Where(o => o.ByteDivergent).Select(o => o.Tier).ToHashSet());
    }

    [Fact]
    public void GetTier_ResolvesEveryTier_AndRejectsAnUnregisteredOne()
    {
        foreach (var tier in Enum.GetValues<StyleOptionTier>())
            Assert.Equal(tier, StyleOptionCatalog.GetTier(tier).Id);

        // A miss is a catalog defect, so it throws instead of yielding an unlabeled
        // group that renders as if it were fine.
        Assert.Throws<ArgumentOutOfRangeException>(() => StyleOptionCatalog.GetTier((StyleOptionTier)(-1)));
    }

    [Fact]
    public void EveryOption_GroupsUnderARegisteredTier()
    {
        // The grouped-picker walk a host performs: every knob lands in exactly one
        // rendered group, so none is invisible to a consumer that trusts Tiers.
        var grouped = StyleOptionCatalog.Tiers
            .SelectMany(tier => Options.Where(o => o.Tier == tier.Id))
            .Select(o => o.Id)
            .ToArray();

        Assert.Equal(Options.Select(o => o.Id).ToHashSet(StringComparer.Ordinal), grouped.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(Options.Count, grouped.Length);
    }
}
