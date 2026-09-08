using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed class StringLiteralUsePatternTests
{
    static string FixturePath =>
        FixtureCatalog.AnalysisStringLiterals.AssemblyPath();

    [Fact]
    public void Producer_identity_is_stable()
    {
        Assert.Equal(
            "analysis.ldstr.ordinal-substring.v1",
            StringLiteralUsePatternAnalysis.ProducerId);
    }

    [Fact]
    public void Operand_preserves_exact_utf16_and_contains_display_text()
    {
        const string value = "A\0e\u0301\U0001F680";

        StringLiteralUseOperand operand = StringLiteralUseOperand.Create(value);

        Assert.Equal(value, operand.RawValue);
        Assert.Equal(value.Length, operand.CharacterCount);
        Assert.DoesNotContain('\0', operand.DisplayText.ToString());
    }

    [Fact]
    public void Operand_rejects_null_empty_and_over_limit_values()
    {
        string maximum =
            new('x', StringLiteralUseOperand.MaximumLength);

        Assert.Equal(
            maximum.Length,
            StringLiteralUseOperand.Create(maximum).CharacterCount);
        Assert.Throws<ArgumentNullException>(() =>
            StringLiteralUseOperand.Create(null!));
        Assert.Throws<ArgumentException>(() =>
            StringLiteralUseOperand.Create(""));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StringLiteralUseOperand.Create(
                new string('x', StringLiteralUseOperand.MaximumLength + 1)));
    }

    [Fact]
    public void Budget_rejects_non_positive_values()
    {
        StringLiteralUsePatternBudget valid =
            StringLiteralUsePatternBudget.Default;

        Assert.Throws<ArgumentOutOfRangeException>(() => new StringLiteralUsePatternBudget(
            0,
            valid.MaximumMethodBodyBytes,
            valid.MaximumMethodBodyBytesVisited,
            valid.MaximumInstructions,
            valid.MaximumDecodedUserStringCharacters,
            valid.MaximumOccurrences));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StringLiteralUsePatternBudget(
            valid.MaximumMethods,
            0,
            valid.MaximumMethodBodyBytesVisited,
            valid.MaximumInstructions,
            valid.MaximumDecodedUserStringCharacters,
            valid.MaximumOccurrences));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StringLiteralUsePatternBudget(
            valid.MaximumMethods,
            valid.MaximumMethodBodyBytes,
            0,
            valid.MaximumInstructions,
            valid.MaximumDecodedUserStringCharacters,
            valid.MaximumOccurrences));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StringLiteralUsePatternBudget(
            valid.MaximumMethods,
            valid.MaximumMethodBodyBytes,
            valid.MaximumMethodBodyBytesVisited,
            0,
            valid.MaximumDecodedUserStringCharacters,
            valid.MaximumOccurrences));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StringLiteralUsePatternBudget(
            valid.MaximumMethods,
            valid.MaximumMethodBodyBytes,
            valid.MaximumMethodBodyBytesVisited,
            valid.MaximumInstructions,
            0,
            valid.MaximumOccurrences));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StringLiteralUsePatternBudget(
            valid.MaximumMethods,
            valid.MaximumMethodBodyBytes,
            valid.MaximumMethodBodyBytesVisited,
            valid.MaximumInstructions,
            valid.MaximumDecodedUserStringCharacters,
            0));
    }

    [Fact]
    public void Inspect_returns_every_physical_use_with_resource_free_identity()
    {
        const string marker = "shared-literal-use-marker";
        Guid expectedMvid = ReadFixtureShape().ModuleVersionId;

        StringLiteralUsePatternResult.Match match = Match(marker);

        Assert.Equal(2, match.Occurrences.Length);
        Assert.Single(match.Occurrences.Select(occurrence => occurrence.UserStringToken).Distinct());
        Assert.Equal(
            2,
            match.Occurrences.Select(occurrence => occurrence.Address).Distinct().Count());
        Assert.All(match.Occurrences, occurrence =>
        {
            Assert.Equal(expectedMvid, occurrence.Address.ModuleVersionId);
            Assert.Equal(
                0x06000000,
                occurrence.Address.MethodDefinitionToken
                    & unchecked((int)0xFF000000));
            Assert.True(occurrence.Address.ILOffset >= 0);
            Assert.Equal(marker.Length, occurrence.LiteralCharacterCount);
            Assert.Equal(marker, occurrence.LiteralText.ToString());
        });
        Assert.Equal(2, match.Receipt.OccurrencesRetained);
    }

    [Theory]
    [InlineData("literal-marker-present-only-as-a-constant")]
    [InlineData("literal-marker-present-only-in-an-attribute")]
    [InlineData("boundary-leftboundary-right")]
    [InlineData("ordinal-case-marker")]
    public void Inspect_does_not_infer_non_ldstr_or_non_ordinal_matches(
        string operand)
    {
        Assert.IsType<StringLiteralUsePatternResult.NoMatch>(
            Inspect(operand));
    }

    [Fact]
    public void Inspect_preserves_unicode_normalization_and_case_distinctions()
    {
        StringLiteralUsePatternResult.Match precomposed =
            Match("caf\u00E9-literal-marker");
        StringLiteralUsePatternResult.Match decomposed =
            Match("cafe\u0301-literal-marker");
        StringLiteralUsePatternResult.Match ordinal =
            Match("Ordinal-Case-Marker");

        Assert.Single(precomposed.Occurrences);
        Assert.Single(decomposed.Occurrences);
        Assert.Single(ordinal.Occurrences);
        Assert.Equal(
            "caf\u00E9-literal-marker",
            precomposed.Occurrences[0].LiteralText.ToString());
        Assert.Equal(
            "cafe\u0301-literal-marker",
            decomposed.Occurrences[0].LiteralText.ToString());
    }

    [Theory]
    [InlineData("\u96EA-literal-marker")]
    [InlineData("rocket-\U0001F680-literal-marker")]
    [InlineData("embedded\0nul-literal-marker")]
    public void Inspect_matches_exact_unicode_and_embedded_nul(string operand)
    {
        StringLiteralUsePatternResult.Match match = Match(operand);

        StringLiteralUseOccurrence occurrence =
            Assert.Single(match.Occurrences);
        Assert.Equal(operand.Length, occurrence.LiteralCharacterCount);
        if (operand.Contains('\0'))
            Assert.DoesNotContain('\0', occurrence.LiteralText.ToString());
    }

    [Fact]
    public void Inspect_completes_at_exact_global_limits_and_reports_exhaustion_below_them()
    {
        const string absent = "literal-pattern-that-is-not-in-the-fixture";
        var completed =
            Assert.IsType<StringLiteralUsePatternResult.NoMatch>(Inspect(absent));
        StringLiteralUsePatternReceipt receipt = completed.Receipt;

        Assert.True(receipt.MethodsVisited > 1);
        Assert.True(receipt.MethodBodyBytesVisited > 1);
        Assert.True(receipt.InstructionsVisited > 1);
        Assert.True(receipt.UserStringCharactersDecoded > 1);

        Assert.IsType<StringLiteralUsePatternResult.NoMatch>(
            Inspect(absent, Budget(maximumMethods: receipt.MethodsVisited)));
        StringLiteralUsePatternResult.WorkLimitExceeded methodLimit =
            AssertLimit(
                Inspect(
                    absent,
                    Budget(maximumMethods: receipt.MethodsVisited - 1)),
                StringLiteralUseLimitKind.Methods);
        Assert.Equal(0, methodLimit.Receipt.MethodsVisited);

        Assert.IsType<StringLiteralUsePatternResult.NoMatch>(
            Inspect(
                absent,
                Budget(
                    maximumMethodBodyBytesVisited:
                        receipt.MethodBodyBytesVisited)));
        StringLiteralUsePatternResult.WorkLimitExceeded bodyBytesLimit =
            AssertLimit(
                Inspect(
                    absent,
                    Budget(
                        maximumMethodBodyBytesVisited:
                            receipt.MethodBodyBytesVisited - 1)),
                StringLiteralUseLimitKind.TotalMethodBodyBytes);
        Assert.Equal(0, bodyBytesLimit.Receipt.OccurrencesRetained);

        Assert.IsType<StringLiteralUsePatternResult.NoMatch>(
            Inspect(
                absent,
                Budget(maximumInstructions: receipt.InstructionsVisited)));
        StringLiteralUsePatternResult.WorkLimitExceeded instructionLimit =
            AssertLimit(
                Inspect(
                    absent,
                    Budget(
                        maximumInstructions:
                            receipt.InstructionsVisited - 1)),
                StringLiteralUseLimitKind.Instructions);
        Assert.Equal(0, instructionLimit.Receipt.OccurrencesRetained);
        Assert.True(instructionLimit.Receipt.UserStringsDecoded > 0);

        Assert.IsType<StringLiteralUsePatternResult.NoMatch>(
            Inspect(
                absent,
                Budget(
                    maximumDecodedUserStringCharacters:
                        receipt.UserStringCharactersDecoded)));
        AssertLimit(
            Inspect(
                absent,
                Budget(
                    maximumDecodedUserStringCharacters:
                        receipt.UserStringCharactersDecoded - 1)),
            StringLiteralUseLimitKind.DecodedUserStringCharacters);
    }

    [Fact]
    public void Inspect_enforces_the_per_body_copy_limit()
    {
        int maximumBodyBytes = ReadFixtureShape().MaximumMethodBodyBytes;
        const string absent = "literal-pattern-that-is-not-in-the-fixture";

        Assert.IsType<StringLiteralUsePatternResult.NoMatch>(
            Inspect(
                absent,
                Budget(maximumMethodBodyBytes: maximumBodyBytes)));
        AssertLimit(
            Inspect(
                absent,
                Budget(maximumMethodBodyBytes: maximumBodyBytes - 1)),
            StringLiteralUseLimitKind.MethodBodyBytes);
    }

    [Fact]
    public void Inspect_discards_prior_matches_when_occurrence_limit_is_exhausted()
    {
        StringLiteralUsePatternResult.WorkLimitExceeded limited =
            AssertLimit(
                Inspect(
                    "shared-literal-use-marker",
                    Budget(maximumOccurrences: 1)),
                StringLiteralUseLimitKind.Occurrences);

        Assert.Equal(1, limited.Receipt.OccurrencesRetained);
    }

    [Fact]
    public void Inspect_counts_bodyless_method_rows()
    {
        FixtureShape fixture = ReadFixtureShape();

        var completed = Assert.IsType<StringLiteralUsePatternResult.NoMatch>(
            Inspect("literal-pattern-that-is-not-in-the-fixture"));

        Assert.Equal(fixture.Methods, completed.Receipt.MethodsVisited);
        Assert.True(
            completed.Receipt.MethodsVisited
                > completed.Receipt.MethodBodiesVisited);
    }

    [Fact]
    public void Inspect_propagates_cancellation()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
        {
            using var session = AssemblyInspectionSession.Open(FixturePath);
            StringLiteralUsePatternAnalysis.Inspect(
                session,
                StringLiteralUseOperand.Create("shared-literal-use-marker"),
                StringLiteralUsePatternBudget.Default,
                cancellation.Token);
        });
    }

    [Fact]
    public void Occurrences_remain_usable_after_session_disposal()
    {
        StringLiteralUsePatternResult result;
        var session = AssemblyInspectionSession.Open(FixturePath);
        try
        {
            result = StringLiteralUsePatternAnalysis.Inspect(
                session,
                StringLiteralUseOperand.Create("shared-literal-use-marker"),
                StringLiteralUsePatternBudget.Default,
                TestContext.Current.CancellationToken);
        }
        finally
        {
            session.Dispose();
        }

        StringLiteralUsePatternResult.Match match =
            Assert.IsType<StringLiteralUsePatternResult.Match>(result);
        Assert.Equal(
            ["shared-literal-use-marker", "shared-literal-use-marker"],
            match.Occurrences.Select(occurrence => occurrence.LiteralText.ToString()));
        Assert.All(
            match.Occurrences,
            occurrence => Assert.NotEqual(Guid.Empty, occurrence.Address.ModuleVersionId));
    }

    static StringLiteralUsePatternResult.Match Match(string operand) =>
        Assert.IsType<StringLiteralUsePatternResult.Match>(Inspect(operand));

    static StringLiteralUsePatternResult.WorkLimitExceeded AssertLimit(
        StringLiteralUsePatternResult result,
        StringLiteralUseLimitKind expected)
    {
        var limited =
            Assert.IsType<StringLiteralUsePatternResult.WorkLimitExceeded>(result);
        Assert.Equal(expected, limited.Limit);
        return limited;
    }

    static StringLiteralUsePatternResult Inspect(
        string operand,
        StringLiteralUsePatternBudget? budget = null)
    {
        using var session = AssemblyInspectionSession.Open(FixturePath);
        return StringLiteralUsePatternAnalysis.Inspect(
            session,
            StringLiteralUseOperand.Create(operand),
            budget ?? StringLiteralUsePatternBudget.Default,
            TestContext.Current.CancellationToken);
    }

    static StringLiteralUsePatternBudget Budget(
        int? maximumMethods = null,
        int? maximumMethodBodyBytes = null,
        long? maximumMethodBodyBytesVisited = null,
        long? maximumInstructions = null,
        long? maximumDecodedUserStringCharacters = null,
        int? maximumOccurrences = null)
    {
        StringLiteralUsePatternBudget defaults =
            StringLiteralUsePatternBudget.Default;
        return new StringLiteralUsePatternBudget(
            maximumMethods ?? defaults.MaximumMethods,
            maximumMethodBodyBytes ?? defaults.MaximumMethodBodyBytes,
            maximumMethodBodyBytesVisited
                ?? defaults.MaximumMethodBodyBytesVisited,
            maximumInstructions ?? defaults.MaximumInstructions,
            maximumDecodedUserStringCharacters
                ?? defaults.MaximumDecodedUserStringCharacters,
            maximumOccurrences ?? defaults.MaximumOccurrences);
    }

    static FixtureShape ReadFixtureShape()
    {
        using FileStream stream = File.OpenRead(FixturePath);
        using var image = new PEReader(stream);
        MetadataReader reader = image.GetMetadataReader();
        int maximumMethodBodyBytes = 0;
        foreach (MethodDefinitionHandle methodHandle in reader.MethodDefinitions)
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            int ilBytes = image.GetMethodBody(method.RelativeVirtualAddress)
                .GetILBytes()?
                .Length ?? 0;
            maximumMethodBodyBytes = Math.Max(maximumMethodBodyBytes, ilBytes);
        }

        return new FixtureShape(
            reader.GetTableRowCount(TableIndex.MethodDef),
            maximumMethodBodyBytes,
            reader.GetGuid(reader.GetModuleDefinition().Mvid));
    }

    readonly record struct FixtureShape(
        int Methods,
        int MaximumMethodBodyBytes,
        Guid ModuleVersionId);
}
