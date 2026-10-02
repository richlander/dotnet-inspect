using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Metadata;
using InertText;
using Inspector.Findings;

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

    [Fact]
    public void Findings_preserve_repeated_physical_uses_with_equal_literal_keys()
    {
        var subject = new FindingSubject("fixture", "fixture");

        FindingInspection<StringLiteralUseOccurrence> inspection =
            StringLiteralUseFindings.Inspect(
                Match("shared-literal-use-marker"),
                subject);

        var complete = Assert.IsType<
            FindingInspection<StringLiteralUseOccurrence>.Complete>(
                inspection.Value);
        Assert.Equal(2, complete.Findings.Length);
        Assert.Equal([0, 1], complete.Findings.Select(finding => finding.Ordinal));
        Assert.Single(
            complete.Findings.Select(finding => finding.Key).Distinct());
        Assert.Equal(
            2,
            complete.Findings
                .Select(finding => finding.Payload.Address)
                .Distinct()
                .Count());
        Assert.All(complete.Findings, finding =>
        {
            Assert.Same(StringLiteralUseFindings.Descriptor, finding.Descriptor);
            Assert.Same(subject, finding.Subject);
            Assert.Equal(
                "shared-literal-use-marker",
                finding.Payload.LiteralText.ToString());
        });
    }

    [Fact]
    public void Findings_keep_one_complete_literal_for_prefix_and_interior_matches()
    {
        const string literal =
            "https://first.example and https://second.example";
        var subject = new FindingSubject("fixture", "fixture");

        Finding<StringLiteralUseOccurrence> prefix = Assert.Single(
            Assert.IsType<
                FindingInspection<StringLiteralUseOccurrence>.Complete>(
                    StringLiteralUseFindings.Inspect(
                        Match("https://"),
                        subject).Value)
                .Findings);
        Finding<StringLiteralUseOccurrence> interior = Assert.Single(
            Assert.IsType<
                FindingInspection<StringLiteralUseOccurrence>.Complete>(
                    StringLiteralUseFindings.Inspect(
                        Match("second.example"),
                        subject).Value)
                .Findings);

        Assert.Equal(literal, prefix.Payload.LiteralText.ToString());
        Assert.Equal(literal, interior.Payload.LiteralText.ToString());
        Assert.Equal(prefix.Key, interior.Key);
    }

    [Fact]
    public void Finding_identity_preserves_raw_utf16_code_units()
    {
        const string literal = "unpaired-\uD800-literal-marker";
        StringLiteralUseOccurrence occurrence =
            Assert.Single(Match("\uD800").Occurrences);

        Assert.Equal(
            StringLiteralUseFindings.CreateIdentityKey(literal),
            occurrence.LiteralIdentityKey);
        Assert.NotEqual(
            StringLiteralUseFindings.CreateIdentityKey(
                "unpaired-\uFFFD-literal-marker"),
            occurrence.LiteralIdentityKey);
        Assert.StartsWith("utf16-v1:", occurrence.LiteralIdentityKey);
        Assert.All(
            occurrence.LiteralIdentityKey["utf16-v1:".Length..],
            value => Assert.True(char.IsAsciiHexDigit(value)));
    }

    [Fact]
    public void Finding_projection_keeps_no_match_and_failures_distinct()
    {
        var subject = new FindingSubject("fixture", "fixture");

        var noMatch = Assert.IsType<
            FindingInspection<StringLiteralUseOccurrence>.Complete>(
                StringLiteralUseFindings.Inspect(
                    Inspect("not-present-in-fixture"),
                    subject).Value);
        Assert.Empty(noMatch.Findings);

        var limited = Assert.IsType<
            FindingInspection<StringLiteralUseOccurrence>.Failed>(
                StringLiteralUseFindings.Inspect(
                    Inspect(
                        "shared-literal-use-marker",
                        Budget(maximumOccurrences: 1)),
                    subject).Value);
        Assert.Contains("Occurrences work limit", limited.Error.Reason);
        Assert.Contains("retained 1 occurrences", limited.Error.Reason);

        var rejectedResult = new StringLiteralUsePatternResult.Rejected(
            new StringLiteralUseRejection(
                StringLiteralUseRejectionKind.BoundedDecode,
                StringLiteralUseFailureStage.UserString,
                new StringLiteralUseFailureSite(
                    Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
                    0x06000002,
                    4)),
            Receipt());
        var rejected = Assert.IsType<
            FindingInspection<StringLiteralUseOccurrence>.Failed>(
                StringLiteralUseFindings.Inspect(
                    rejectedResult,
                    subject).Value);
        Assert.Contains(
            "BoundedDecode/UserString",
            rejected.Error.Reason);
        Assert.Contains("0x06000002/IL_0004", rejected.Error.Reason);
    }

    [Fact]
    public void Finding_comparison_uses_literal_content_not_physical_coordinates()
    {
        var subject = new FindingSubject("fixture", "fixture");
        StringLiteralUsePatternResult.Match before = Result(
            Occurrence(
                Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
                0x06000001,
                1,
                "https://example"));
        StringLiteralUsePatternResult.Match after = Result(
            Occurrence(
                Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100"),
                0x06000004,
                20,
                "https://example"));

        var complete = Assert.IsType<
            FindingComparison<StringLiteralUseOccurrence>.Complete>(
                StringLiteralUseFindings.Compare(
                    before,
                    after,
                    subject).Value);
        PairFinding<StringLiteralUseOccurrence> present =
            Assert.Single(complete.Pairs);
        Assert.IsType<PairFinding<StringLiteralUseOccurrence>.Present>(
            present.Value);

        var changed = Assert.IsType<
            FindingComparison<StringLiteralUseOccurrence>.Complete>(
                StringLiteralUseFindings.Compare(
                    before,
                    Result(Occurrence(
                        Guid.NewGuid(),
                        0x06000004,
                        20,
                        "https://changed")),
                    subject).Value);
        Assert.Contains(
            changed.Pairs,
            pair => pair is PairFinding<StringLiteralUseOccurrence>.Removed);
        Assert.Contains(
            changed.Pairs,
            pair => pair is PairFinding<StringLiteralUseOccurrence>.Added);
    }

    static StringLiteralUsePatternResult.Match Match(string operand) =>
        Assert.IsType<StringLiteralUsePatternResult.Match>(Inspect(operand));

    static StringLiteralUsePatternResult.Match Result(
        params StringLiteralUseOccurrence[] occurrences) =>
        new([.. occurrences], Receipt(occurrences.Length));

    static StringLiteralUseOccurrence Occurrence(
        Guid mvid,
        int methodToken,
        int ilOffset,
        string literal) =>
        new(
            new StringLiteralInstructionAddress(
                mvid,
                methodToken,
                ilOffset),
            0x70000001,
            literal.Length,
            new InertString(TextPolicy.Field, literal),
            StringLiteralUseFindings.CreateIdentityKey(literal));

    static StringLiteralUsePatternReceipt Receipt(
        int occurrences = 0) =>
        new(
            methodsVisited: 1,
            methodBodiesVisited: 1,
            methodBodyBytesVisited: 1,
            instructionsVisited: 1,
            userStringsDecoded: 1,
            userStringCharactersDecoded: 1,
            occurrencesRetained: occurrences);

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
