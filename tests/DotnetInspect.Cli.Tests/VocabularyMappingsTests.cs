using DotnetInspector.Sections;
using DotnetInspector.Vocabulary;

namespace DotnetInspect.Cli.Tests;

public sealed class VocabularyMappingsTests
{
    [Fact]
    public void ProductSnapshotAuthenticatesStyleChoiceTierMap()
    {
        VocabularySnapshot snapshot = VocabularyCatalog.Snapshot;
        VocabularyDefinition choices = snapshot.GetVocabulary(
            new(snapshot.Catalog, "csharp.style-choices"));
        VocabularyMapDefinition tier = choices.GetMap("tier");

        Assert.Equal(1, snapshot.FormatVersion);
        Assert.Equal(
            "sha256:79f5a1cddcbabc41e23e85a96ecefbe582416382ec00fb79794f444e794c308e",
            snapshot.Identity.Value);
        Assert.Equal(
            VocabularyMapCardinality.ExactlyOne,
            tier.Cardinality);
        Assert.Equal(VocabularyMapCoverage.Complete, tier.Coverage);
        var target = Assert.IsType<VocabularyMapTarget.Terms>(tier.Target);
        var local = Assert.IsType<VocabularyTermSetReference.Local>(
            target.Reference);
        Assert.Equal("csharp.style-tiers", local.Vocabulary.Value);

        foreach (VocabularyTerm choice in choices.Terms)
        {
            VocabularyMapValue.Term value = Assert.IsType<
                VocabularyMapValue.Term>(
                Assert.Single(choice.GetRequiredValues(tier.Identity)));
            Assert.Same(
                snapshot.GetTerm(value.Identity),
                snapshot.GetVocabulary(local.Vocabulary)
                    .GetTerm(value.Identity.Value));
        }
    }

    [Fact]
    public void ProductInspectionReturnsTheSharedStaticSnapshot()
    {
        InspectionEnvelope<VocabularySnapshot> inspection =
            ProductVocabularyInspection.Execute();

        Assert.Same(VocabularyCatalog.Snapshot, inspection.Content);
        var share = Assert.IsType<InspectionShare.NonProjectable>(
            inspection.Share);
        Assert.Equal("vocabulary/share", share.Path);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public void IdentityCoversEveryObservableSnapshotChange()
    {
        VocabularySnapshot baseline = CreateReferenceSnapshot();
        Assert.Equal(
            baseline.Identity,
            CreateReferenceSnapshot().Identity);

        Assert.NotEqual(
            baseline.Identity,
            CreateReferenceSnapshot(sourceLabel: "Renamed").Identity);
        Assert.NotEqual(
            baseline.Identity,
            CreateReferenceSnapshot(reverseTargetOrder: true).Identity);
        Assert.NotEqual(
            baseline.Identity,
            CreateReferenceSnapshot(
                cardinality: VocabularyMapCardinality.OneOrMore).Identity);
        Assert.NotEqual(
            baseline.Identity,
            CreateReferenceSnapshot(useAlternateTarget: true).Identity);
    }

    [Fact]
    public void VocabularySummaryMayBeAbsent()
    {
        VocabularyCatalogIdentity catalog = new("test");
        VocabularyIdentity vocabulary = new(catalog, "values");
        VocabularySnapshot snapshot = VocabularySnapshot.Create(
            1,
            catalog,
            [
                new(
                    vocabulary,
                    "Values",
                    summary: null,
                    maps: [],
                    [new(new(vocabulary, "value"), "Value", null)]),
            ]);

        Assert.Null(snapshot.GetVocabulary(vocabulary).Summary);
        Assert.Equal(snapshot.Identity, VocabularySnapshot.Create(
            snapshot.FormatVersion,
            snapshot.Catalog,
            snapshot.Vocabularies,
            expectedIdentity: snapshot.Identity).Identity);
    }

    [Fact]
    public void ConstructionRejectsIllFormedUtf16BeforeIdentity()
    {
        string unpairedSurrogate = "\U0001F680"[..1];

        Assert.Throws<ArgumentException>(
            () => VocabularyScalarValue.FromText(unpairedSurrogate));
        Assert.Throws<ArgumentException>(
            () => new VocabularyCatalogIdentity(unpairedSurrogate));

        VocabularyCatalogIdentity catalog = new("test");
        VocabularyIdentity vocabulary = new(catalog, "values");
        VocabularyMapDefinition textMap = ScalarMap(
            vocabulary,
            "text",
            VocabularyScalarKind.Text);
        Assert.Throws<ArgumentException>(() => new VocabularyDefinition(
            vocabulary,
            unpairedSurrogate,
            summary: null,
            maps: [],
            terms: []));
        Assert.Throws<ArgumentException>(() => new VocabularyDefinition(
            vocabulary,
            "Values",
            unpairedSurrogate,
            maps: [],
            terms: []));

        VocabularySnapshot replacementCharacter = VocabularySnapshot.Create(
            1,
            catalog,
            [
                new(
                    vocabulary,
                    "Values",
                    summary: null,
                    maps: [textMap],
                    [
                        new(
                            new(vocabulary, "value"),
                            "Value",
                            summary: null,
                            [
                                new(
                                    textMap.Identity,
                                    [Text("\uFFFD")]),
                            ]),
                    ]),
            ]);
        Assert.NotNull(replacementCharacter.Identity.Value);
    }

    [Fact]
    public void CompleteOptionalMapDistinguishesEmptyFromOmitted()
    {
        VocabularyCatalogIdentity catalog = new("test");
        VocabularyIdentity source = new(catalog, "source");
        VocabularyMapDefinition optional = ScalarMap(
            source,
            "optional",
            VocabularyScalarKind.Text,
            VocabularyMapCardinality.OptionalOne,
            VocabularyMapCoverage.Complete);

        VocabularySnapshot snapshot = VocabularySnapshot.Create(
            1,
            catalog,
            [
                new(
                    source,
                    "Source",
                    "Source terms.",
                    [optional],
                    [
                        new(
                            new(source, "empty"),
                            "Empty",
                            summary: null,
                            [new(optional.Identity, [])]),
                    ]),
            ]);
        Assert.Empty(
            snapshot.GetVocabulary(source)
                .GetTerm("empty")
                .GetRequiredValues(optional.Identity));

        InvalidOperationException failure = Assert.Throws<
            InvalidOperationException>(() => VocabularySnapshot.Create(
                1,
                catalog,
                [
                    new(
                        source,
                        "Source",
                        "Source terms.",
                        [optional],
                        [new(new(source, "omitted"), "Omitted", null)]),
                ]));
        Assert.Contains("Complete map", failure.Message);
    }

    [Fact]
    public void ConstructionRejectsInvalidTypedMappings()
    {
        VocabularyCatalogIdentity catalog = new("test");
        VocabularyIdentity source = new(catalog, "source");
        VocabularyIdentity target = new(catalog, "target");
        VocabularyMapDefinition scalar = ScalarMap(
            source,
            "scalar",
            VocabularyScalarKind.Boolean);
        VocabularyMapDefinition relation = new(
            new(source, "relation"),
            "Relation",
            "Target relation.",
            new VocabularyMapTarget.Terms(
                new VocabularyTermSetReference.Local(target)),
            VocabularyMapCardinality.OneOrMore,
            VocabularyMapCoverage.Complete);

        AssertFailure(
            "wrong kind",
            catalog,
            scalar,
            [Text("not-a-boolean")],
            target);
        AssertFailure(
            "wrong kind",
            catalog,
            relation,
            [Text("known")],
            target);
        AssertFailure(
            "repeats a value",
            catalog,
            relation,
            [
                Term(target, "known"),
                Term(target, "known"),
            ],
            target);
        AssertFailure(
            "dangling local target",
            catalog,
            relation,
            [Term(target, "missing")],
            target);
    }

    [Fact]
    public void OrderedMultiTargetMapRetainsEveryTarget()
    {
        VocabularyCatalogIdentity catalog = new("test");
        VocabularyIdentity source = new(catalog, "source");
        VocabularyIdentity target = new(catalog, "target");
        VocabularyMapDefinition relation = new(
            new(source, "relation"),
            "Relation",
            "Ordered targets.",
            new VocabularyMapTarget.Terms(
                new VocabularyTermSetReference.Local(target)),
            VocabularyMapCardinality.OneOrMore,
            VocabularyMapCoverage.Complete);
        VocabularySnapshot snapshot = VocabularySnapshot.Create(
            1,
            catalog,
            [
                new(
                    source,
                    "Source",
                    "Source terms.",
                    [relation],
                    [
                        new(
                            new(source, "value"),
                            "Value",
                            null,
                            [
                                new(
                                    relation.Identity,
                                    [
                                        Term(target, "second"),
                                        Term(target, "first"),
                                    ]),
                            ]),
                    ]),
                new(
                    target,
                    "Target",
                    "Target terms.",
                    maps: [],
                    [
                        new(new(target, "first"), "First", null),
                        new(new(target, "second"), "Second", null),
                    ]),
            ]);

        Assert.Equal(
            ["second", "first"],
            snapshot.GetVocabulary(source)
                .GetTerm("value")
                .GetRequiredValues(relation.Identity)
                .Cast<VocabularyMapValue.Term>()
                .Select(value => value.Identity.Value));
    }

    [Fact]
    public void ConstructionRejectsMismatchedIdentityAndExternalSnapshot()
    {
        VocabularySnapshot snapshot = CreateReferenceSnapshot();
        var wrongIdentity = new VocabularySnapshotIdentity(
            $"sha256:{new string('0', 64)}");
        InvalidOperationException identityFailure = Assert.Throws<
            InvalidOperationException>(() => VocabularySnapshot.Create(
                snapshot.FormatVersion,
                snapshot.Catalog,
                snapshot.Vocabularies,
                expectedIdentity: wrongIdentity));
        Assert.Contains("does not match", identityFailure.Message);

        VocabularyCatalogIdentity catalog = new("test");
        VocabularyIdentity source = new(catalog, "source");
        VocabularyIdentity externalVocabulary =
            new(new VocabularyCatalogIdentity("external"), "target");
        var externalMap = new VocabularyMapDefinition(
            new(source, "external"),
            "External",
            "External target.",
            new VocabularyMapTarget.Terms(
                new VocabularyTermSetReference.External(
                    wrongIdentity,
                    externalVocabulary)),
            VocabularyMapCardinality.OptionalOne,
            VocabularyMapCoverage.Complete);
        InvalidOperationException externalFailure = Assert.Throws<
            InvalidOperationException>(() => VocabularySnapshot.Create(
                1,
                catalog,
                [
                    new(
                        source,
                        "Source",
                        "Source terms.",
                        [externalMap],
                        [
                            new(
                                new(source, "value"),
                                "Value",
                                null,
                                [new(externalMap.Identity, [])]),
                        ]),
                ]));
        Assert.Contains("unavailable external snapshot", externalFailure.Message);
    }

    [Fact]
    public void EqualLabelsDoNotCollapseTermIdentity()
    {
        VocabularyCatalogIdentity catalog = new("test");
        VocabularyIdentity vocabulary = new(catalog, "values");
        VocabularySnapshot snapshot = VocabularySnapshot.Create(
            1,
            catalog,
            [
                new(
                    vocabulary,
                    "Values",
                    "Equal labels remain distinct.",
                    maps: [],
                    [
                        new(new(vocabulary, "first"), "Same", null),
                        new(new(vocabulary, "second"), "Same", null),
                    ]),
            ]);

        Assert.Equal(2, snapshot.GetVocabulary(vocabulary).Terms.Length);
        Assert.NotEqual(
            snapshot.GetTerm(new(vocabulary, "first")).Identity,
            snapshot.GetTerm(new(vocabulary, "second")).Identity);
    }

    private static VocabularySnapshot CreateReferenceSnapshot(
        string sourceLabel = "Source",
        bool reverseTargetOrder = false,
        VocabularyMapCardinality cardinality =
            VocabularyMapCardinality.ExactlyOne,
        bool useAlternateTarget = false)
    {
        VocabularyCatalogIdentity catalog = new("test");
        VocabularyIdentity source = new(catalog, "source");
        VocabularyIdentity target = new(catalog, "target");
        VocabularyIdentity alternate = new(catalog, "alternate");
        VocabularyIdentity selectedTarget =
            useAlternateTarget ? alternate : target;
        VocabularyMapDefinition relation = new(
            new(source, "relation"),
            "Relation",
            "Target relation.",
            new VocabularyMapTarget.Terms(
                new VocabularyTermSetReference.Local(selectedTarget)),
            cardinality,
            VocabularyMapCoverage.Complete);
        VocabularyTerm[] targetTerms =
        [
            new(new(target, "one"), "One", null),
            new(new(target, "two"), "Two", null),
        ];
        if (reverseTargetOrder)
            Array.Reverse(targetTerms);

        return VocabularySnapshot.Create(
            1,
            catalog,
            [
                new(
                    source,
                    sourceLabel,
                    "Source terms.",
                    [relation],
                    [
                        new(
                            new(source, "value"),
                            "Value",
                            null,
                            [
                                new(
                                    relation.Identity,
                                    [Term(selectedTarget, "one")]),
                            ]),
                    ]),
                new(
                    target,
                    "Target",
                    "Target terms.",
                    maps: [],
                    targetTerms),
                new(
                    alternate,
                    "Alternate",
                    "Alternate target terms.",
                    maps: [],
                    [new(new(alternate, "one"), "One", null)]),
            ]);
    }

    private static void AssertFailure(
        string expectedMessage,
        VocabularyCatalogIdentity catalog,
        VocabularyMapDefinition map,
        VocabularyMapValue[] values,
        VocabularyIdentity target)
    {
        VocabularyIdentity source = map.Identity.SourceVocabulary;
        InvalidOperationException failure = Assert.Throws<
            InvalidOperationException>(() => VocabularySnapshot.Create(
                1,
                catalog,
                [
                    new(
                        source,
                        "Source",
                        "Source terms.",
                        [map],
                        [
                            new(
                                new(source, "value"),
                                "Value",
                                null,
                                [new(map.Identity, values)]),
                        ]),
                    new(
                        target,
                        "Target",
                        "Target terms.",
                        maps: [],
                        [new(new(target, "known"), "Known", null)]),
                ]));
        Assert.Contains(expectedMessage, failure.Message);
    }

    private static VocabularyMapDefinition ScalarMap(
        VocabularyIdentity source,
        string identity,
        VocabularyScalarKind kind,
        VocabularyMapCardinality cardinality =
            VocabularyMapCardinality.ExactlyOne,
        VocabularyMapCoverage coverage =
            VocabularyMapCoverage.Complete) =>
        new(
            new(source, identity),
            identity,
            $"{identity} values.",
            new VocabularyMapTarget.Scalar(kind),
            cardinality,
            coverage);

    private static VocabularyMapValue Text(string value) =>
        new VocabularyMapValue.Scalar(
            VocabularyScalarValue.FromText(value));

    private static VocabularyMapValue Term(
        VocabularyIdentity vocabulary,
        string identity) =>
        new VocabularyMapValue.Term(new(vocabulary, identity));
}
