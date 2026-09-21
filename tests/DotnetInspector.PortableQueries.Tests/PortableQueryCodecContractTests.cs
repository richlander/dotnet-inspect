using QuerySpace.Rows;

namespace DotnetInspector.PortableQueries.Tests;

/// <summary>
/// The parts of the contract the vectors cannot witness, and the typed path a host
/// uses when it never serializes an intent at all.
/// </summary>
public sealed class PortableQueryCodecContractTests
{
    /// <summary>
    /// The declared maxima are pinned constants, identical across builds and
    /// vocabularies.
    /// </summary>
    /// <remarks>
    /// No vector can witness this: a vector shows one payload admitted or refused,
    /// never that the boundary is the same one everywhere. The structural half —
    /// that no vocabulary can relax a limit — is that the codec has no vocabulary
    /// parameter to vary; the numeric half is asserted here so a build cannot move
    /// a boundary that already-shared links depend on.
    /// </remarks>
    [Fact]
    public void DeclaredLimits_ArePinned()
    {
        Assert.Equal(3 * 1024, PortableQueryPayloadCodec.MaxPayloadBytes);
        Assert.Equal(4, PortableQueryPayloadCodec.MaxDepth);
        Assert.Equal(24, PortableQueryPayloadCodec.MaxTerms);
        Assert.Equal(8, PortableQueryPayloadCodec.MaxBounds);
        Assert.Equal(8, PortableQueryPayloadCodec.MaxStages);
        Assert.Equal(8, PortableQueryPayloadCodec.MaxOrderOperations);
        Assert.Equal(8, PortableQueryPayloadCodec.MaxOrderFieldTerms);
        Assert.Equal(64, PortableQueryPayloadCodec.MaxIdentityBytes);
        Assert.Equal(256, PortableQueryPayloadCodec.MaxValueBytes);
        Assert.Equal(2147483647, PortableQueryPayloadCodec.MaxCount);
    }

    /// <summary>
    /// Cancellation is observed before any parsing or emission, on every entry
    /// point and whatever the input would otherwise do.
    /// </summary>
    [Fact]
    public void Cancellation_IsObservedBeforeAnyWork()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        CancellationToken cancelled = source.Token;

        Assert.Throws<OperationCanceledException>(
            () => PortableQueryPayloadCodec.Encode(PortableQueryIntent.Empty, cancelled));
        Assert.Throws<OperationCanceledException>(
            () => PortableQueryPayloadCodec.ParseJson("{ this is not json", cancelled));
        Assert.Throws<OperationCanceledException>(
            () => PortableQueryPayloadCodec.Decode("{ this is not json", cancelled));
    }

    /// <summary>An intent with no parts has a payload, and it is the empty object.</summary>
    [Fact]
    public void EmptyIntent_IsTheEmptyObject()
    {
        Assert.Equal("{}", CodecUnderTest.Encode(PortableQueryIntent.Empty));
        Assert.Empty(CodecUnderTest.Decode("{}").Terms);
    }

    /// <summary>
    /// An intent a host builds in process — never serialized, never parsed — emits
    /// the same canonical bytes the worked example in the design shows.
    /// </summary>
    [Fact]
    public void TypedIntent_EmitsTheWorkedExample()
    {
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [
                new PortableQueryTerm("prerelease", PortableQueryOperator.Equal, "include"),
                new PortableQueryTerm("depends", PortableQueryOperator.Equal, "Serilog"),
                new PortableQueryTerm("prefix", PortableQueryOperator.Equal, "Microsoft.Extensions.")
            ],
            [new PortableQueryBound("candidates", 200)],
            [],
            []);

        Assert.Equal(
            """
            {"t":[["depends","eq","Serilog"],["prefix","eq","Microsoft.Extensions."],["prerelease","eq","include"]],"b":[["candidates",200]]}
            """,
            CodecUnderTest.Encode(intent));
    }

    /// <summary>
    /// Terms order by key, then operator, then value — the operator by its identity
    /// text, so all eight sort by their canonical texts rather than by any enum's
    /// member order.
    /// </summary>
    [Fact]
    public void TermOrder_FollowsIdentityTextNotEnumOrder()
    {
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [
                new PortableQueryTerm("k", PortableQueryOperator.NotEqual, "v"),
                new PortableQueryTerm("k", PortableQueryOperator.NotStartsWith, "v"),
                new PortableQueryTerm("k", PortableQueryOperator.NotContains, "v"),
                new PortableQueryTerm("k", PortableQueryOperator.StartsWith, "v"),
                new PortableQueryTerm("k", PortableQueryOperator.Contains, "v"),
                new PortableQueryTerm("k", PortableQueryOperator.AtMost, "v"),
                new PortableQueryTerm("k", PortableQueryOperator.AtLeast, "v"),
                new PortableQueryTerm("k", PortableQueryOperator.Equal, "v")
            ],
            [],
            [],
            []);

        Assert.Equal(
            """{"t":[["k","contains","v"],["k","eq","v"],["k","gte","v"],["k","lte","v"],["k","ne","v"],["k","not-contains","v"],["k","not-starts-with","v"],["k","starts-with","v"]]}""",
            CodecUnderTest.Encode(intent));
    }

    /// <summary>
    /// The comparator is Unicode scalar order, which is UTF-8 byte order, not the
    /// UTF-16 code-unit order .NET and JavaScript compare strings with by default.
    /// </summary>
    [Fact]
    public void ScalarOrder_DisagreesWithUtf16AboveTheBasicPlane()
    {
        const string SupplementaryPlane = "\U00010000";
        const string PrivateUse = "";

        Assert.True(PortableQueryModel.ScalarOrder.Compare(PrivateUse, SupplementaryPlane) < 0);
        Assert.True(string.CompareOrdinal(PrivateUse, SupplementaryPlane) > 0);
    }

    /// <summary>
    /// A limit reached only by a directly-constructed intent is still charged: the
    /// typed path is not a way around the payload's boundaries.
    /// </summary>
    [Fact]
    public void TypedIntent_IsChargedTheDeclaredLimits()
    {
        var terms = new List<PortableQueryTerm>();
        for (int index = 0; index <= PortableQueryPayloadCodec.MaxTerms; index++)
            terms.Add(new PortableQueryTerm($"k{index:D2}", PortableQueryOperator.Equal, "v"));

        PortableQueryPayloadException failure = Assert.Throws<PortableQueryPayloadException>(
            () => CodecUnderTest.Encode(
                PortableQueryIntent.Create(terms, [], [], [])));

        Assert.Equal(PortableQueryPayloadFailureKind.LimitExceeded, failure.Kind);
    }

    /// <summary>
    /// Exact duplicate terms collapse, because conjunction is idempotent — but the
    /// limit is charged on what was supplied, so collapse cannot buy room.
    /// </summary>
    [Fact]
    public void DuplicateTerms_CollapseButAreChargedAsSupplied()
    {
        var one = new PortableQueryTerm("k", PortableQueryOperator.Equal, "v");

        Assert.Equal(
            """{"t":[["k","eq","v"]]}""",
            CodecUnderTest.Encode(
                PortableQueryIntent.Create([one, one, one], [], [], [])));

        var many = new List<PortableQueryTerm>();
        for (int index = 0; index <= PortableQueryPayloadCodec.MaxTerms; index++) many.Add(one);

        Assert.Equal(
            PortableQueryPayloadFailureKind.LimitExceeded,
            Assert.Throws<PortableQueryPayloadException>(
                () => CodecUnderTest.Encode(
                    PortableQueryIntent.Create(many, [], [], []))).Kind);
    }

    /// <summary>
    /// The model's aggregate invariants hold at construction, so an intent a host
    /// never serializes still cannot be a contradiction.
    /// </summary>
    /// <remarks>
    /// These are the model's, not the payload's: a dimension bounded twice and a
    /// role claimed twice are contradictions rather than narrower requests, and a
    /// ranking that names no ranking stage is not a request at all. Deferring them
    /// to the codec would let an in-process intent — which need never touch one —
    /// hold a state the contract forbids.
    /// </remarks>
    [Fact]
    public void TypedIntent_RefusesContradictionsAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => PortableQueryIntent.Create(
            [],
            [new PortableQueryBound("candidates", 1), new PortableQueryBound("candidates", 2)],
            [],
            []));

        Assert.Throws<ArgumentException>(() => PortableQueryIntent.Create(
            [],
            [new PortableQueryBound("candidates", 1), new PortableQueryBound("candidates", 1)],
            [],
            []));

        Assert.Throws<ArgumentException>(() => PortableQueryIntent.Create(
            [],
            [],
            [PortableQueryStage.Head(5)],
            [Ranking(0, "relevance")]));

        Assert.Throws<ArgumentException>(() => PortableQueryIntent.Create(
            [],
            [],
            [],
            [Ranking(3, "relevance")]));

        Assert.Throws<ArgumentException>(() => PortableQueryIntent.Create(
            [],
            [],
            [PortableQueryStage.Top(5)],
            [Ranking(0, "a"), Ranking(0, "b")]));

        Assert.Throws<ArgumentException>(() => PortableQueryIntent.Create(
            [],
            [],
            [],
            [
                PortableQueryOrderOperation.Named(
                    PortableQueryOrderRole.Baseline, "a", PortableQueryDirection.Ascending),
                PortableQueryOrderOperation.Named(
                    PortableQueryOrderRole.Baseline, "b", PortableQueryDirection.Ascending)
            ]));

        static PortableQueryOrderOperation Ranking(int stage, string reference) =>
            PortableQueryOrderOperation.Named(
                PortableQueryOrderRole.ForStage(stage),
                reference,
                PortableQueryDirection.Descending);
    }

    /// <summary>
    /// The payload's byte ceiling belongs to the payload, and each entry point
    /// charges it where that artifact exists.
    /// </summary>
    /// <remarks>
    /// <c>Decode</c> charges it before parsing, because its input is the payload
    /// and the untrusted path should never parse more than the declared maximum.
    /// <c>Encode</c> charges it against the canonical form it just produced.
    /// <c>ParseJson</c> charges neither, because it returns an intent rather than
    /// a payload, and its input may be spelled with whitespace a payload cannot
    /// carry — <c>payload-at-exact-byte-maximum</c> and three other admissible
    /// vectors are witnessed exactly that way.
    /// </remarks>
    [Fact]
    public void ThePayloadCeiling_IsChargedWhereThePayloadExists()
    {
        string oversized = @"{""t"":[[""k"",""eq"","""
            + new string('v', PortableQueryPayloadCodec.MaxPayloadBytes)
            + @"""]]}";

        Assert.Equal(
            PortableQueryPayloadFailureKind.LimitExceeded,
            Assert.Throws<PortableQueryPayloadException>(
                () => CodecUnderTest.Decode(oversized)).Kind);

        // The same request, spelled with padding no payload may carry, is still a
        // valid intent; the ceiling catches it when the payload is written.
        string padded = "{ \"t\" : [ [ \"k\" , \"eq\" , \"v\" ] ] }"
            + new string(' ', PortableQueryPayloadCodec.MaxPayloadBytes);
        PortableQueryIntent intent = CodecUnderTest.ParseJson(padded);
        Assert.Equal(@"{""t"":[[""k"",""eq"",""v""]]}", CodecUnderTest.Encode(intent));

        Assert.Equal(
            PortableQueryPayloadFailureKind.LimitExceeded,
            Assert.Throws<PortableQueryPayloadException>(
                () => CodecUnderTest.Encode(CodecUnderTest.ParseJson(oversized))).Kind);
    }

    /// <summary>
    /// An identity is only ever a vocabulary with a payload this codec vouched
    /// for: emitted here, or confirmed canonical here.
    /// </summary>
    [Fact]
    public void Identity_CannotBeBuiltFromUnvouchedBytes()
    {
        Assert.Equal(
            PortableQueryPayloadFailureKind.EmptyPart,
            Assert.Throws<PortableQueryPayloadException>(
                () => CodecUnderTest.IdentityOf("v", @"{""t"":[]}")).Kind);

        Assert.Equal("{}", CodecUnderTest.IdentityOf("v", "{}").Payload);
    }

    /// <summary>
    /// Stage sequence is the question, so it survives emission exactly while the
    /// order operations' outer sequence — which carries nothing, because each names
    /// its own role — is replaced by the model's.
    /// </summary>
    [Fact]
    public void MeaningfulSequenceSurvivesAndMeaninglessSequenceDoesNot()
    {
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [],
            [],
            [PortableQueryStage.Top(5), PortableQueryStage.Head(2)],
            [
                PortableQueryOrderOperation.Named(
                    PortableQueryOrderRole.ForStage(0),
                    "triage",
                    PortableQueryDirection.Descending),
                PortableQueryOrderOperation.Named(
                    PortableQueryOrderRole.Baseline,
                    "name",
                    PortableQueryDirection.Ascending)
            ]);

        Assert.Equal(
            """{"s":[["top",5],["head",2]],"o":[["base","named","name","asc"],[0,"named","triage","desc"]]}""",
            CodecUnderTest.Encode(intent));
    }

    /// <summary>
    /// The window's construction precondition holds on the typed path: a host
    /// cannot build a stage the codec would have to refuse.
    /// </summary>
    [Fact]
    public void Window_CannotBeConstructedOutOfOrder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PortableQueryStage.Window(9, 1));
        Assert.Equal(7, PortableQueryStage.Window(7, 7).Start);
    }

    /// <summary>A stage answers only for its own kind.</summary>
    [Fact]
    public void Stage_RefusesWrongKindAccessors()
    {
        PortableQueryStage window = PortableQueryStage.Window(1, 2);
        PortableQueryStage head = PortableQueryStage.Head(1);

        Assert.Equal(RowSelectionStageKind.Window, window.Kind);
        Assert.Throws<InvalidOperationException>(() => window.Count);
        Assert.Throws<InvalidOperationException>(() => head.Start);
    }

    /// <summary>
    /// The same bytes under two vocabularies are two queries; the same vocabulary
    /// and bytes are one.
    /// </summary>
    [Fact]
    public void Identity_PairsTheVocabularyWithTheBytes()
    {
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [new PortableQueryTerm("k", PortableQueryOperator.Equal, "v")],
            [],
            [],
            []);

        PortableQueryIdentity package = CodecUnderTest.Identity("package.query", intent);
        PortableQueryIdentity row = CodecUnderTest.Identity("row.query", intent);

        Assert.Equal(package.Payload, row.Payload);
        Assert.NotEqual(package, row);
        Assert.Equal(package, CodecUnderTest.Identity("package.query", intent));
    }

    /// <summary>
    /// The baseline is what a role defaults to, so a host that never names one
    /// cannot accidentally claim a ranking stage.
    /// </summary>
    [Fact]
    public void OrderRole_DefaultsToTheBaseline()
    {
        Assert.True(default(PortableQueryOrderRole).IsBaseline);
        Assert.True(PortableQueryOrderRole.Baseline.IsBaseline);
        Assert.Equal(0, PortableQueryOrderRole.ForStage(0).StageIndex);
        Assert.Throws<InvalidOperationException>(
            () => PortableQueryOrderRole.Baseline.StageIndex);
    }
}
