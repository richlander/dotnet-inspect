namespace CSharpText.Tests;

using System.Collections.Immutable;

public sealed class IdentifierWordBreakerTests
{
    [Theory]
    [InlineData("jsonContext", "json|Context")]
    [InlineData("JsonContext", "Json|Context")]
    [InlineData("éÉlan", "é|Élan")]
    public void Break_AppliesConventionalCaseBoundaries(
        string text,
        string expected)
    {
        IdentifierWordBreakResult result = Break(text);

        Assert.Equal(expected.Split('|'), result.Spans.Select(static span => span.Text));
        Assert.All(
            result.Spans,
            static span => Assert.Equal(
                IdentifierWordSpanClassification.Word,
                span.Classification));
        AssertCoverage(result);
    }

    [Fact]
    public void Break_AcronymToWordUsesExactOracleAtom()
    {
        IdentifierWordOracle oracle = NewOracle(Atom("XML"));

        IdentifierWordBreakResult result = Break("XMLDocument", oracle);

        Assert.Equal(["XML", "Document"], result.Spans.Select(static span => span.Text));
        Assert.Equal(IdentifierWordRuleKind.ExactOracleAtom, result.Spans[0].Evidence.Kind);
        Assert.Equal(IdentifierWordRuleKind.OrdinaryCaseWord, result.Spans[1].Evidence.Kind);
    }

    [Theory]
    [InlineData("HMACSHA256", "HMAC|SHA256")]
    [InlineData("RSAPKCS1", "RSA|PKCS1")]
    public void Break_UsesCompleteOracleSegmentation(
        string text,
        string expected)
    {
        IdentifierWordBreakResult result = Break(text);

        Assert.Equal(expected.Split('|'), result.Spans.Select(static span => span.Text));
        Assert.All(
            result.Spans,
            static span => Assert.Equal(
                IdentifierWordRuleKind.OracleSegmentation,
                span.Evidence.Kind));
    }

    [Theory]
    [InlineData("IPv6")]
    [InlineData("UInt16")]
    public void Break_ProductCompoundProtectsCompleteUnits(string text)
    {
        IdentifierWordSpan span = Assert.Single(Break(text).Spans);

        Assert.Equal(text, span.Text);
        Assert.Equal(IdentifierWordSpanClassification.Word, span.Classification);
        Assert.Equal(IdentifierWordRuleKind.ExactOracleCompound, span.Evidence.Kind);
    }

    [Fact]
    public void Break_ProductOracleSegmentationPrecedesNumberedFamilyContext()
    {
        IdentifierNumberedFamilyContext context = NewContext(
            "HMACSHA1",
            "HMACSHA256",
            "HMACSHA384",
            "HMACSHA512");

        IdentifierWordBreakResult result = Break("HMACSHA256", context: context);

        Assert.Equal(["HMAC", "SHA256"], result.Spans.Select(static span => span.Text));
        Assert.All(
            result.Spans,
            static span => Assert.Equal(
                IdentifierWordRuleKind.OracleSegmentation,
                span.Evidence.Kind));
    }

    [Fact]
    public void Break_CompleteSegmentationPrecedesUnitAlignedAtomAndOrdinal()
    {
        IdentifierWordOracle oracle = NewOracle(
            Atom("AB"),
            Atom("ABC"),
            Compound("C1"));
        IdentifierNumberedFamilyContext context = NewContext(
            "ABC1",
            "ABC2",
            "ABC3");

        IdentifierWordBreakResult result = Break("ABC1", oracle, context);

        Assert.Equal(["AB", "C1"], result.Spans.Select(static span => span.Text));
        Assert.All(
            result.Spans,
            static span => Assert.Equal(
                IdentifierWordRuleKind.OracleSegmentation,
                span.Evidence.Kind));
    }

    [Theory]
    [InlineData("RSAPKCS1SignatureFormatter", "RSA|PKCS1|Signature|Formatter")]
    [InlineData("HMACSHA256Provider", "HMAC|SHA256|Provider")]
    public void Break_OracleSegmentationAllowsOrdinarySuffix(
        string text,
        string expected)
    {
        IdentifierWordBreakResult result = Break(text);

        Assert.Equal(expected.Split('|'), result.Spans.Select(static span => span.Text));
        Assert.All(
            result.Spans,
            static span => Assert.Equal(
                IdentifierWordSpanClassification.Word,
                span.Classification));
    }

    [Fact]
    public void Break_PartialUppercaseRecognitionLeavesCompleteRunUnresolved()
    {
        IdentifierWordOracle oracle = NewOracle(Atom("URL"));

        IdentifierWordSpan span = Assert.Single(Break("CFURL", oracle).Spans);

        Assert.Equal("CFURL", span.Text);
        Assert.Equal(IdentifierWordSpanClassification.Unresolved, span.Classification);
        Assert.Equal(IdentifierWordRuleKind.UnknownUppercaseRun, span.Evidence.Kind);
    }

    [Fact]
    public void Break_OracleEntryCannotFragmentOrdinaryWord()
    {
        IdentifierWordOracle oracle = NewOracle(Atom("Valid"));

        IdentifierWordSpan span = Assert.Single(Break("Validator", oracle).Spans);

        Assert.Equal("Validator", span.Text);
        Assert.Equal(IdentifierWordRuleKind.OrdinaryCaseWord, span.Evidence.Kind);
    }

    [Fact]
    public void Break_OracleMatchingIsExactOrdinal()
    {
        IdentifierWordOracle oracle = NewOracle(Atom("HMAC"));

        IdentifierWordSpan span = Assert.Single(Break("hmac", oracle).Spans);

        Assert.Equal(IdentifierWordRuleKind.OrdinaryCaseWord, span.Evidence.Kind);
        Assert.Null(span.Evidence.OracleEntry);
    }

    [Fact]
    public void Break_OracleSegmentationMinimizesEntriesBeforeLongestLeftmost()
    {
        IdentifierWordOracle oracle = NewOracle(
            Atom("ABCD"),
            Atom("AB"),
            Atom("CDEF"),
            Atom("E"),
            Atom("F"));

        IdentifierWordBreakResult result = Break("ABCDEF", oracle);

        Assert.Equal(["AB", "CDEF"], result.Spans.Select(static span => span.Text));
    }

    [Fact]
    public void Break_OracleSegmentationUsesLongestLeftmostTieBreak()
    {
        IdentifierWordOracle oracle = NewOracle(
            Atom("AB"),
            Atom("A"),
            Atom("BCD"),
            Atom("CD"));

        IdentifierWordBreakResult result = Break("ABCD", oracle);

        Assert.Equal(["AB", "CD"], result.Spans.Select(static span => span.Text));
    }

    [Fact]
    public void Break_UnitAlignedOracleMatchesUseGlobalMinimumEntryRanking()
    {
        IdentifierWordOracle oracle = NewOracle(
            Compound("AB1CD2"),
            Compound("AB1"),
            Compound("CD2EF3GH4"),
            Compound("EF3"),
            Compound("GH4"));

        IdentifierWordBreakResult result = Break("AB1CD2EF3GH4", oracle);

        Assert.Equal(["AB1", "CD2EF3GH4"], result.Spans.Select(static span => span.Text));
    }

    [Fact]
    public void Break_OracleRankingIncludesOpaqueInternalBoundariesAcrossUnits()
    {
        IdentifierWordOracle oracle = NewOracle(
            Atom("AB"),
            Compound("C1D2E3"),
            Compound("ABC1"),
            Compound("D2"),
            Compound("E3"));

        IdentifierWordBreakResult result = Break("ABC1D2E3", oracle);

        Assert.Equal(["AB", "C1D2E3"], result.Spans.Select(static span => span.Text));
        Assert.All(
            result.Spans,
            static span => Assert.Equal(
                IdentifierWordRuleKind.OracleSegmentation,
                span.Evidence.Kind));
    }

    [Fact]
    public void NumberedFamilyContext_RecognizesDistinctCanonicalSuffixes()
    {
        IdentifierNumberedFamilyContext context = NewContext(
            "DelegateInvoker1",
            "DelegateInvoker2",
            "DelegateInvoker10",
            "LookupType3",
            "LookupType4",
            "LookupType5",
            "LookupType8",
            "Adler32");

        Assert.Equal(
            ["DelegateInvoker", "LookupType"],
            context.Families.Select(static family => family.Prefix));
        Assert.Equal(8, context.Receipt.IdentifierCount);
        Assert.Equal(
            IdentifierNumberedFamilyContext.ComputePopulationDigest(
                [
                    "DelegateInvoker1",
                    "DelegateInvoker2",
                    "DelegateInvoker10",
                    "LookupType3",
                    "LookupType4",
                    "LookupType5",
                    "LookupType8",
                    "Adler32",
                ]),
            context.Receipt.Digest);
    }

    [Theory]
    [InlineData("DelegateInvoker1")]
    [InlineData("DelegateInvoker10")]
    public void Break_NumberedFamilyProducesTrailingOrdinal(string text)
    {
        IdentifierNumberedFamilyContext context = NewContext(
            "DelegateInvoker1",
            "DelegateInvoker2",
            "DelegateInvoker10");

        IdentifierWordBreakResult result = Break(text, context: context);

        Assert.Equal(
            ["Delegate", "Invoker", text["DelegateInvoker".Length..]],
            result.Spans.Select(static span => span.Text));
        Assert.Equal(
            IdentifierWordSpanClassification.Ordinal,
            result.Spans[^1].Classification);
        Assert.Equal(
            IdentifierWordRuleKind.NumberedFamilyOrdinal,
            result.Spans[^1].Evidence.Kind);
        Assert.Equal(context.Receipt, result.NumberedFamilyReceipt);
    }

    [Fact]
    public void Break_NumberedFamilyContextChangesOnlyQualifiedTrailingRun()
    {
        IdentifierNumberedFamilyContext context = NewContext(
            "LookupType3",
            "LookupType4",
            "LookupType5",
            "LookupType8");

        IdentifierWordBreakResult withoutContext = Break("LookupType3");
        IdentifierWordBreakResult withContext = Break("LookupType3", context: context);

        Assert.Equal(
            ["Lookup", "Type3"],
            withoutContext.Spans.Select(static span => span.Text));
        Assert.Equal(
            IdentifierWordRuleKind.DigitCompoundFallback,
            withoutContext.Spans[^1].Evidence.Kind);
        Assert.Equal(
            ["Lookup", "Type", "3"],
            withContext.Spans.Select(static span => span.Text));
        Assert.Equal(
            IdentifierWordSpanClassification.Ordinal,
            withContext.Spans[^1].Classification);
    }

    [Fact]
    public void Break_UnresolvedPrefixRetainsRecognizedOrdinal()
    {
        IdentifierNumberedFamilyContext context = NewContext(
            "QQURL1",
            "QQURL2",
            "QQURL3");
        IdentifierWordOracle oracle = NewOracle(Atom("URL"));

        IdentifierWordBreakResult result = Break("QQURL1", oracle, context);

        Assert.Equal(["QQURL", "1"], result.Spans.Select(static span => span.Text));
        Assert.Equal(
            IdentifierWordSpanClassification.Unresolved,
            result.Spans[0].Classification);
        Assert.Equal(
            IdentifierWordSpanClassification.Ordinal,
            result.Spans[1].Classification);
    }

    [Theory]
    [InlineData("Log4Net")]
    [InlineData("Secp256r")]
    public void Break_UnsupportedPrefixRetainsRecognizedOrdinal(string prefix)
    {
        IdentifierNumberedFamilyContext context = NewContext(
            $"{prefix}1",
            $"{prefix}2",
            $"{prefix}3");

        IdentifierWordBreakResult result = Break($"{prefix}3", context: context);

        Assert.Equal([prefix, "3"], result.Spans.Select(static span => span.Text));
        Assert.Equal(
            IdentifierWordRuleKind.UnsupportedLetterDigitShape,
            result.Spans[0].Evidence.Kind);
        Assert.Equal(
            IdentifierWordSpanClassification.Ordinal,
            result.Spans[1].Classification);
    }

    [Fact]
    public void Break_ProtectedCompoundAndUnsupportedPrefixRetainRecognizedOrdinal()
    {
        IdentifierNumberedFamilyContext context = NewContext(
            "IPv6Log4Net1",
            "IPv6Log4Net2",
            "IPv6Log4Net3");

        IdentifierWordBreakResult result = Break(
            "IPv6Log4Net3",
            context: context);

        Assert.Equal(
            ["IPv6", "Log4Net", "3"],
            result.Spans.Select(static span => span.Text));
        Assert.Equal(
            IdentifierWordRuleKind.ExactOracleCompound,
            result.Spans[0].Evidence.Kind);
        Assert.Equal(
            IdentifierWordRuleKind.UnsupportedLetterDigitShape,
            result.Spans[1].Evidence.Kind);
        Assert.Equal(
            IdentifierWordSpanClassification.Ordinal,
            result.Spans[2].Classification);
    }

    [Theory]
    [InlineData("Adler32", "Adler32", IdentifierWordRuleKind.DigitCompoundFallback)]
    [InlineData("Secp256r1", "Secp256r1", IdentifierWordRuleKind.UnsupportedLetterDigitShape)]
    [InlineData("P320t1", "P320t1", IdentifierWordRuleKind.UnsupportedLetterDigitShape)]
    [InlineData("Log4Net", "Log4Net", IdentifierWordRuleKind.UnsupportedLetterDigitShape)]
    public void Break_ClassifiesDigitControls(
        string text,
        string expectedText,
        IdentifierWordRuleKind expectedRule)
    {
        IdentifierWordSpan span = Assert.Single(Break(text).Spans);

        Assert.Equal(expectedText, span.Text);
        Assert.Equal(expectedRule, span.Evidence.Kind);
    }

    [Theory]
    [InlineData("IPv6Log4Net", "IPv6", "Log4Net")]
    [InlineData("Log4NetIPv6", "Log4Net", "IPv6")]
    public void Break_UnsupportedDigitShapeDoesNotEraseProtectedCompound(
        string text,
        string first,
        string second)
    {
        IdentifierWordBreakResult result = Break(text);

        Assert.Equal([first, second], result.Spans.Select(static span => span.Text));
        IdentifierWordSpan protectedSpan = Assert.Single(
            result.Spans,
            static span => span.Text == "IPv6");
        Assert.Equal(
            IdentifierWordRuleKind.ExactOracleCompound,
            protectedSpan.Evidence.Kind);
        IdentifierWordSpan unresolvedSpan = Assert.Single(
            result.Spans,
            static span => span.Text == "Log4Net");
        Assert.Equal(
            IdentifierWordRuleKind.UnsupportedLetterDigitShape,
            unresolvedSpan.Evidence.Kind);
    }

    [Fact]
    public void Break_DigitAcrossSeparatorDoesNotAttach()
    {
        IdentifierWordBreakResult result = Break("Adler_32");

        Assert.Equal(["Adler", "_", "32"], result.Spans.Select(static span => span.Text));
        Assert.Equal(
            IdentifierWordSpanClassification.Unresolved,
            result.Spans[^1].Classification);
    }

    [Theory]
    [InlineData("Adler٠")]
    [InlineData("Family0")]
    [InlineData("Family01")]
    [InlineData("Family-1")]
    public void Break_NonCanonicalOrdinalsAreNeverIssued(string text)
    {
        IdentifierNumberedFamilyContext context = NewContext(
            "Family1",
            "Family2",
            "Family3");

        IdentifierWordBreakResult result = Break(text, context: context);

        Assert.DoesNotContain(
            result.Spans,
            static span => span.Classification == IdentifierWordSpanClassification.Ordinal);
        AssertCoverage(result);
    }

    [Fact]
    public void Break_WhitespacePrecedesControlAndCoalesces()
    {
        IdentifierWordSpan span = Assert.Single(Break("\t\r\n").Spans);

        Assert.Equal(IdentifierWordSpanClassification.Separator, span.Classification);
        Assert.Equal(IdentifierWordRuleKind.WhitespaceSeparator, span.Evidence.Kind);
        Assert.Equal(3, span.Length);
    }

    [Fact]
    public void Break_PreservesControlFormatAndMalformedUtf16()
    {
        string text = "\0\u001B\u200D\uD800X\uDC00";

        IdentifierWordBreakResult result = Break(text);

        Assert.Equal(
            [
                IdentifierWordRuleKind.ControlText,
                IdentifierWordRuleKind.FormatText,
                IdentifierWordRuleKind.MalformedUtf16,
                IdentifierWordRuleKind.SingleUppercaseWord,
                IdentifierWordRuleKind.MalformedUtf16,
            ],
            result.Spans.Select(static span => span.Evidence.Kind));
        Assert.Equal(2, result.Spans[0].Length);
        AssertCoverage(result);
    }

    [Theory]
    [InlineData("\u0301")]
    [InlineData("\u20DD")]
    public void Break_CombiningMarkFollowsWordButLeadingMarkIsUnresolved(string mark)
    {
        IdentifierWordBreakResult result = Break($"{mark}Cafe{mark}");

        Assert.Equal([mark, $"Cafe{mark}"], result.Spans.Select(static span => span.Text));
        Assert.Equal(
            IdentifierWordRuleKind.LeadingCombiningMark,
            result.Spans[0].Evidence.Kind);
        Assert.Equal(
            IdentifierWordSpanClassification.Word,
            result.Spans[1].Classification);
    }

    [Fact]
    public void EnclosingMark_IsValidInOracleEntryAndNumberedFamilyPrefix()
    {
        const string Prefix = "A\u20DD";
        IdentifierWordOracle oracle = NewOracle(Atom(Prefix));
        IdentifierNumberedFamilyContext context = NewContext(
            $"{Prefix}1",
            $"{Prefix}2",
            $"{Prefix}3");

        IdentifierWordBreakResult word = Break(Prefix, oracle);
        IdentifierWordBreakResult ordinal = Break($"{Prefix}3", oracle, context);

        Assert.Equal(
            IdentifierWordRuleKind.ExactOracleAtom,
            Assert.Single(word.Spans).Evidence.Kind);
        Assert.Equal([Prefix, "3"], ordinal.Spans.Select(static span => span.Text));
        Assert.Equal(
            IdentifierWordSpanClassification.Ordinal,
            ordinal.Spans[^1].Classification);
    }

    [Fact]
    public void Break_UnsupportedUnicodeLetterAndSupplementarySymbolRemainGapFree()
    {
        IdentifierWordBreakResult result = Break("名😀Value");

        Assert.Equal(["名", "😀", "Value"], result.Spans.Select(static span => span.Text));
        Assert.Equal(
            IdentifierWordRuleKind.UnsupportedUnicodeCategory,
            result.Spans[0].Evidence.Kind);
        Assert.Equal(
            IdentifierWordSpanClassification.Separator,
            result.Spans[1].Classification);
        AssertCoverage(result);
    }

    [Fact]
    public void Break_SeparatorsCoalesceByRuleKindAndRetainCoverage()
    {
        IdentifierWordBreakResult result = Break("Json-_+Context");

        Assert.Equal(
            [
                "Json",
                "-_",
                "+",
                "Context",
            ],
            result.Spans.Select(static span => span.Text));
        Assert.Equal(
            [
                IdentifierWordSpanClassification.Word,
                IdentifierWordSpanClassification.Separator,
                IdentifierWordSpanClassification.Separator,
                IdentifierWordSpanClassification.Word,
            ],
            result.Spans.Select(static span => span.Classification));
        Assert.Null(result.Spans[1].Evidence.UnicodeCategory);
        AssertCoverage(result);
    }

    [Fact]
    public void Break_UnresolvedTextCoalescesByReason()
    {
        IdentifierWordBreakResult result = Break("名١");
        IdentifierWordSpan span = Assert.Single(result.Spans);

        Assert.Equal("名١", span.Text);
        Assert.Equal(
            IdentifierWordRuleKind.UnsupportedUnicodeCategory,
            span.Evidence.Kind);
        Assert.Null(span.Evidence.UnicodeCategory);
        AssertCoverage(result);
    }

    [Fact]
    public void Break_EmptyTextSucceedsWithExactReceipts()
    {
        IdentifierWordBreakResult result = Break("");

        Assert.Empty(result.Spans);
        Assert.Same(
            IdentifierWordProductOracle.Instance.Receipt,
            result.OracleReceipt);
        Assert.Null(result.NumberedFamilyReceipt);
    }

    [Fact]
    public void Break_RejectsMismatchedContextGrammar()
    {
        var receipt = new IdentifierPopulationReceipt("other-grammar", "digest", 3);
        var context = Assert.IsType<IdentifierNumberedFamilyContextConstruction.Created>(
            IdentifierNumberedFamilyContext.Create(
                receipt,
                [new("Family", ["1", "2", "3"])]));

        var outcome = Assert.IsType<IdentifierWordBreakOutcome.Rejected>(
            IdentifierWordBreaker.Break(
                "Family1",
                IdentifierWordProductOracle.Instance,
                context.Context));

        Assert.Equal(
            IdentifierWordBreakRejectionReason.NumberedFamilyGrammarVersionMismatch,
            outcome.Reason);
    }

    [Fact]
    public void Break_RejectsUnsupportedOracleGrammar()
    {
        IdentifierWordOracleEntry[] entries = [Atom("URL")];
        IdentifierWordOracle oracle =
            Assert.IsType<IdentifierWordOracleConstruction.Created>(
                IdentifierWordOracle.Create(
                    "other-grammar",
                    "test-v1",
                    IdentifierWordOracle.ComputeDigest(entries),
                    "test",
                    "test",
                    entries)).Oracle;

        var outcome = Assert.IsType<IdentifierWordBreakOutcome.Rejected>(
            IdentifierWordBreaker.Break("URL", oracle));

        Assert.Equal(
            IdentifierWordBreakRejectionReason.OracleGrammarVersionMismatch,
            outcome.Reason);
    }

    [Fact]
    public void OracleConstruction_RejectsEveryInvalidShape()
    {
        AssertOracleRejected(
            IdentifierWordOracleRejectionReason.EmptyGrammarVersion,
            grammarVersion: "");
        AssertOracleRejected(
            IdentifierWordOracleRejectionReason.EmptyVocabularyVersion,
            vocabularyVersion: "");
        AssertOracleRejected(
            IdentifierWordOracleRejectionReason.EmptySourceCoordinate,
            sourceCoordinate: "");
        AssertOracleRejected(
            IdentifierWordOracleRejectionReason.EmptyReviewSetVersion,
            reviewSetVersion: "");
        AssertOracleRejected(
            IdentifierWordOracleRejectionReason.EmptyEntry,
            entries: [Atom("")]);
        AssertOracleRejected(
            IdentifierWordOracleRejectionReason.InvalidEntry,
            entries: [Atom("bad-name")]);
        AssertOracleRejected(
            IdentifierWordOracleRejectionReason.InvalidEntry,
            entries: [Atom("\uD800")]);
        AssertOracleRejected(
            IdentifierWordOracleRejectionReason.InvalidEntry,
            entries: [Atom("\u20DDA")]);
        AssertOracleRejected(
            IdentifierWordOracleRejectionReason.InvalidEntry,
            entries: [Atom("A1\u0301")]);
        AssertOracleRejected(
            IdentifierWordOracleRejectionReason.InvalidEntry,
            entries: [Atom("SHA256")]);
        AssertOracleRejected(
            IdentifierWordOracleRejectionReason.DuplicateEntry,
            entries: [Atom("URL"), Compound("URL")]);
        AssertOracleRejected(
            IdentifierWordOracleRejectionReason.DigestMismatch,
            expectedDigest: "not-the-digest");
    }

    [Fact]
    public void NumberedContextConstruction_RejectsInvalidFamilies()
    {
        var receipt = new IdentifierPopulationReceipt(
            IdentifierWordBreaker.GrammarVersion,
            "digest",
            3);

        AssertContextRejected(
            IdentifierNumberedFamilyContextRejectionReason.EmptyGrammarVersion,
            receipt with { GrammarVersion = "" },
            []);
        AssertContextRejected(
            IdentifierNumberedFamilyContextRejectionReason.InvalidPopulationReceipt,
            receipt with { Digest = "" },
            []);
        AssertContextRejected(
            IdentifierNumberedFamilyContextRejectionReason.EmptyPrefix,
            receipt,
            [new("", ["1", "2", "3"])]);
        AssertContextRejected(
            IdentifierNumberedFamilyContextRejectionReason.InvalidPrefix,
            receipt,
            [new("Family_", ["1", "2", "3"])]);
        AssertContextRejected(
            IdentifierNumberedFamilyContextRejectionReason.InvalidPrefix,
            receipt,
            [new("Foo_Bar", ["1", "2", "3"])]);
        AssertContextRejected(
            IdentifierNumberedFamilyContextRejectionReason.TooFewDistinctOrdinals,
            receipt,
            [new("Family", ["1", "2"])]);
        AssertContextRejected(
            IdentifierNumberedFamilyContextRejectionReason.InvalidOrdinal,
            receipt,
            [new("Family", ["0", "1", "2"])]);
        AssertContextRejected(
            IdentifierNumberedFamilyContextRejectionReason.InvalidOrdinal,
            receipt,
            [new("Family", ["01", "2", "3"])]);
        AssertContextRejected(
            IdentifierNumberedFamilyContextRejectionReason.DuplicateOrdinal,
            receipt,
            [new("Family", ["1", "1", "2"])]);
        AssertContextRejected(
            IdentifierNumberedFamilyContextRejectionReason.DuplicatePrefix,
            receipt,
            [
                new("Family", ["1", "2", "3"]),
                new("Family", ["4", "5", "6"]),
            ]);
    }

    [Fact]
    public void NumberedContext_AllowsPrefixEndingInCombiningMark()
    {
        IdentifierNumberedFamilyContext context = NewContext(
            "Cafe\u03011",
            "Cafe\u03012",
            "Cafe\u03013");

        IdentifierWordBreakResult result = Break("Cafe\u03013", context: context);

        Assert.Equal(
            IdentifierWordSpanClassification.Ordinal,
            result.Spans[^1].Classification);
    }

    [Fact]
    public void NumberedContext_CanonicalizesSuppliedFamilyAndOrdinalOrder()
    {
        var receipt = new IdentifierPopulationReceipt(
            IdentifierWordBreaker.GrammarVersion,
            "digest",
            6);
        IdentifierNumberedFamilyContext context =
            Assert.IsType<IdentifierNumberedFamilyContextConstruction.Created>(
                IdentifierNumberedFamilyContext.Create(
                    receipt,
                    [
                        new("Zulu", ["3", "1", "2"]),
                        new("Alpha", ["6", "4", "5"]),
                    ])).Context;

        Assert.Equal(["Alpha", "Zulu"], context.Families.Select(static family => family.Prefix));
        Assert.Equal(["4", "5", "6"], context.Families[0].CanonicalOrdinals);
        Assert.Equal(["1", "2", "3"], context.Families[1].CanonicalOrdinals);
    }

    [Fact]
    public void PopulationDigest_PreservesMalformedUtf16CodeUnits()
    {
        Assert.NotEqual(
            IdentifierNumberedFamilyContext.ComputePopulationDigest(["\uD800"]),
            IdentifierNumberedFamilyContext.ComputePopulationDigest(["\uDC00"]));
    }

    [Fact]
    public void ProductOracle_HasCheckedInReceipt()
    {
        IdentifierWordOracle oracle = IdentifierWordProductOracle.Instance;

        Assert.Equal(IdentifierWordBreaker.GrammarVersion, oracle.Receipt.GrammarVersion);
        Assert.Equal(IdentifierWordProductOracle.VocabularyVersion, oracle.Receipt.VocabularyVersion);
        Assert.Equal(IdentifierWordProductOracle.SourceCoordinate, oracle.Receipt.SourceCoordinate);
        Assert.Equal(IdentifierWordProductOracle.ReviewSetVersion, oracle.Receipt.ReviewSetVersion);
        Assert.Equal(IdentifierWordProductOracle.Digest, oracle.Receipt.Digest);
        Assert.Equal(
            IdentifierWordOracle.ComputeDigest(IdentifierWordProductOracle.Entries),
            oracle.Receipt.Digest);
        Assert.Equal(IdentifierWordProductOracle.Entries.Length, oracle.Receipt.EntryCount);
    }

    static IdentifierWordBreakResult Break(
        string text,
        IdentifierWordOracle? oracle = null,
        IdentifierNumberedFamilyContext? context = null)
        => Assert.IsType<IdentifierWordBreakOutcome.Succeeded>(
            IdentifierWordBreaker.Break(
                text,
                oracle ?? IdentifierWordProductOracle.Instance,
                context)).Result;

    static IdentifierWordOracle NewOracle(params IdentifierWordOracleEntry[] entries)
    {
        string digest = IdentifierWordOracle.ComputeDigest(entries);
        return Assert.IsType<IdentifierWordOracleConstruction.Created>(
            IdentifierWordOracle.Create(
                IdentifierWordBreaker.GrammarVersion,
                "test-v1",
                digest,
                "test",
                "test",
                entries)).Oracle;
    }

    static IdentifierNumberedFamilyContext NewContext(params string[] identifiers)
        => Assert.IsType<IdentifierNumberedFamilyContextConstruction.Created>(
            IdentifierNumberedFamilyContext.Build(identifiers)).Context;

    static IdentifierWordOracleEntry Atom(string text)
        => new(text, IdentifierWordOracleEntryKind.Atom);

    static IdentifierWordOracleEntry Compound(string text)
        => new(text, IdentifierWordOracleEntryKind.Compound);

    static void AssertCoverage(IdentifierWordBreakResult result)
    {
        int offset = 0;
        foreach (IdentifierWordSpan span in result.Spans)
        {
            Assert.Equal(offset, span.Start);
            Assert.Equal(result.Text.Substring(span.Start, span.Length), span.Text);
            offset += span.Length;
        }
        Assert.Equal(result.Text.Length, offset);
    }

    static void AssertOracleRejected(
        IdentifierWordOracleRejectionReason expected,
        string grammarVersion = IdentifierWordBreaker.GrammarVersion,
        string vocabularyVersion = "test-v1",
        string? expectedDigest = null,
        string sourceCoordinate = "test",
        string reviewSetVersion = "test",
        IdentifierWordOracleEntry[]? entries = null)
    {
        entries ??= [Atom("URL")];
        expectedDigest ??= IdentifierWordOracle.ComputeDigest(entries);
        var rejected = Assert.IsType<IdentifierWordOracleConstruction.Rejected>(
            IdentifierWordOracle.Create(
                grammarVersion,
                vocabularyVersion,
                expectedDigest,
                sourceCoordinate,
                reviewSetVersion,
                entries));
        Assert.Equal(expected, rejected.Reason);
    }

    static void AssertContextRejected(
        IdentifierNumberedFamilyContextRejectionReason expected,
        IdentifierPopulationReceipt receipt,
        IdentifierNumberedFamilyEvidence[] families)
    {
        var rejected =
            Assert.IsType<IdentifierNumberedFamilyContextConstruction.Rejected>(
                IdentifierNumberedFamilyContext.Create(receipt, families));
        Assert.Equal(expected, rejected.Reason);
    }
}
