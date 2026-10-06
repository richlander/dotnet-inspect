using System.Collections.Immutable;

using TestDocument = Inspector.Graph.GraphDocument<
    Inspector.Graph.Tests.Subject,
    Inspector.Graph.Tests.Relationship,
    Inspector.Graph.Tests.Receipt,
    Inspector.Graph.Tests.Characteristic,
    Inspector.Graph.Tests.Limit,
    Inspector.Graph.Tests.Failure>;

namespace Inspector.Graph.Tests;

public sealed class GraphDocumentTests
{
    [Fact]
    public void EmptyDocumentIsValid()
    {
        TestDocument document = CreateDocument(
            nodes: [],
            groups: [],
            edges: [],
            occurrences: [],
            characteristics: [],
            seeds: [],
            limits: [],
            failures: []);

        Assert.Empty(document.Nodes);
        Assert.Empty(document.Groups);
        Assert.Empty(document.Edges);
        Assert.Empty(document.Occurrences);
        Assert.Empty(document.Characteristics);
        Assert.Empty(document.Seeds);
        Assert.Empty(document.Limits);
        Assert.Empty(document.Failures);
    }

    [Fact]
    public void DisconnectedNodesAndSelfLoopAreValid()
    {
        Relationship relationship = new("depends-on");
        Subject first = new("first");
        Subject second = new("second");
        TestDocument document = CreateDocument(
            nodes:
            [
                new(0, first, GraphNodeRole.Ordinary, []),
                new(1, second, GraphNodeRole.Ordinary, []),
            ],
            occurrences:
            [
                new(
                    0,
                    relationship,
                    first,
                    first,
                    new("self"),
                    []),
            ],
            edges:
            [
                new(0, 0, 0, relationship, [0]),
            ]);

        Assert.Equal(2, document.Nodes.Length);
        Assert.Equal(0, Assert.Single(document.Edges).ToNodeId);
    }

    [Fact]
    public void DefaultImmutableCollectionIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new GraphNode<Subject>(
                0,
                new("node"),
                GraphNodeRole.Ordinary,
                default(ImmutableArray<int>)));
    }

    [Fact]
    public void NestedCollectionsAreSnapshotted()
    {
        var groupIds = new List<int> { 0 };
        var occurrenceIds = new List<int> { 0 };
        var derivationSources = new List<GraphTarget>
        {
            GraphTarget.Occurrence(0),
        };
        var derivedOccurrenceIds = new List<int>();
        var relationship = new Relationship("relationship");
        var source = new Subject("source");
        var target = new Subject("target");
        var node = new GraphNode<Subject>(
            0,
            source,
            GraphNodeRole.Ordinary,
            groupIds);
        var occurrence =
            new GraphOccurrence<
                Subject,
                Relationship,
                Receipt>(
                0,
                relationship,
                source,
                target,
                new("receipt"),
                derivedOccurrenceIds);
        var edge = new GraphEdge<Relationship>(
            0,
            0,
            0,
            relationship,
            occurrenceIds);
        var derivation = new GraphCharacteristicDerivation(
            GraphCharacteristicDerivationKind.Derived,
            derivationSources);

        groupIds.Clear();
        occurrenceIds.Clear();
        derivationSources.Clear();
        derivedOccurrenceIds.Add(0);

        Assert.Equal([0], node.GroupIds);
        Assert.Empty(occurrence.DerivedFromOccurrenceIds);
        Assert.Equal([0], edge.OccurrenceIds);
        Assert.Equal(
            [GraphTarget.Occurrence(0)],
            derivation.Sources);
    }

    [Fact]
    public void NullDocumentCollectionIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TestDocument(
                GraphDocumentScope.SessionBound,
                nodes: null!,
                groups: [],
                edges: [],
                occurrences: [],
                characteristics: [],
                seeds: [],
                limits: [],
                failures: []));
    }

    [Fact]
    public void NodeSubjectsMustBeUnique()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                nodes:
                [
                    new(0, new("same"), GraphNodeRole.Ordinary, []),
                    new(1, new("same"), GraphNodeRole.External, []),
                ]));
    }

    [Fact]
    public void GraphOwnedEnumsMustBeDefined()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateDocument(
                nodes:
                [
                    new(
                        0,
                        new("node"),
                        (GraphNodeRole)int.MaxValue,
                        []),
                ]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TestDocument(
                (GraphDocumentScope)int.MaxValue,
                nodes: [],
                groups: [],
                edges: [],
                occurrences: [],
                characteristics: [],
                seeds: [],
                limits: [],
                failures: []));
    }

    [Fact]
    public void CallerSuppliedSubjectEqualityIsUsed()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                nodes:
                [
                    new(0, new("same"), GraphNodeRole.Ordinary, []),
                    new(1, new("SAME"), GraphNodeRole.External, []),
                ],
                subjectComparer: SubjectNameComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryLocalIdentityMustBeDenseAndOrdered()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                nodes:
                [
                    new(1, new("node"), GraphNodeRole.Ordinary, []),
                ]));
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                groups:
                [
                    new(1, new("group"), null),
                ]));
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                edges:
                [
                    new(1, 0, 0, new("relationship"), []),
                ]));
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                occurrences:
                [
                    new(
                        1,
                        new("relationship"),
                        new("source"),
                        new("target"),
                        new("receipt"),
                        []),
                ]));
    }

    [Fact]
    public void EveryLocalReferenceMustExist()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                nodes:
                [
                    new(0, new("node"), GraphNodeRole.Ordinary, [0]),
                ]));
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                groups:
                [
                    new(0, new("group"), 1),
                ]));
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                edges:
                [
                    new(0, 0, 1, new("relationship"), []),
                ]));
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                edges:
                [
                    new(0, 0, 0, new("relationship"), [0]),
                ]));
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                occurrences:
                [
                    new(
                        0,
                        new("relationship"),
                        new("source"),
                        new("target"),
                        new("receipt"),
                        [1]),
                ],
                edges:
                [
                    new(0, 0, 0, new("relationship"), [0]),
                ]));
    }

    [Fact]
    public void GroupsMustNotContainParentCycles()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                groups:
                [
                    new(0, new("first"), 1),
                    new(1, new("second"), 0),
                ]));
    }

    [Fact]
    public void DeepGroupParentsDoNotUseCallStack()
    {
        const int count = 20_000;
        GraphGroup<Subject>[] groups =
        [
            .. Enumerable.Range(0, count).Select(id =>
                new GraphGroup<Subject>(
                    id,
                    new($"group-{id}"),
                    id == 0 ? null : id - 1)),
        ];

        TestDocument document = CreateDocument(groups: groups);

        Assert.Equal(count, document.Groups.Length);
    }

    [Fact]
    public void OccurrencesMustBeBoundAndAcyclic()
    {
        Relationship relationship = new("relationship");
        Subject source = new("source");
        Subject target = new("target");
        GraphOccurrence<Subject, Relationship, Receipt>[] occurrences =
        [
            new(
                0,
                relationship,
                source,
                target,
                new("first"),
                [1]),
            new(
                1,
                relationship,
                source,
                target,
                new("second"),
                [0]),
        ];

        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                occurrences: occurrences,
                edges:
                [
                    new(0, 0, 0, relationship, [0, 1]),
                ]));
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                occurrences:
                [
                    new(
                        0,
                        relationship,
                        source,
                        target,
                        new("unbound"),
                        []),
                ]));
    }

    [Fact]
    public void EdgeAndOccurrenceRelationshipsMustAgree()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                occurrences:
                [
                    new(
                        0,
                        new("calls"),
                        new("source"),
                        new("target"),
                        new("receipt"),
                        []),
                ],
                edges:
                [
                    new(0, 0, 0, new("depends-on"), [0]),
                ]));
    }

    [Fact]
    public void LogicalEdgesMustBeUniqueUnderRelationshipEquality()
    {
        Relationship first = new("calls");
        Relationship second = new("CALLS");

        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                edges:
                [
                    new(0, 0, 0, first, []),
                    new(1, 0, 0, second, []),
                ],
                relationshipComparer:
                    RelationshipNameComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryGraphTargetMustBeInitializedAndInRange()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                characteristics:
                [
                    new(
                        default,
                        new("characteristic"),
                        DirectDerivation()),
                ]));
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                characteristics:
                [
                    new(
                        GraphTarget.Node(1),
                        new("characteristic"),
                        DirectDerivation()),
                ]));
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                limits:
                [
                    new(new("limit"), GraphTarget.Edge(0)),
                ]));
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                failures:
                [
                    new(new("failure"), GraphTarget.Occurrence(0)),
                ]));
    }

    [Fact]
    public void CharacteristicDerivationSourcesMustBeValid()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                characteristics:
                [
                    new(
                        GraphTarget.Node(0),
                        new("characteristic"),
                        new(
                            GraphCharacteristicDerivationKind.Derived,
                            [GraphTarget.Node(1)])),
                ]));
    }

    [Fact]
    public void SeedMustTargetItsOwnNodeOrGroup()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                seeds:
                [
                    new(
                        new("different"),
                        GraphTarget.Node(0),
                        GraphSeedRole.Primary),
                ]));
        Assert.Throws<ArgumentException>(() =>
            CreateDocument(
                seeds:
                [
                    new(
                        new("node"),
                        GraphTarget.Edge(0),
                        GraphSeedRole.Primary),
                ]));
    }

    [Fact]
    public void SuppliedOrderIsPreserved()
    {
        TestDocument document = CreateDocument(
            nodes:
            [
                new(0, new("z"), GraphNodeRole.External, []),
                new(1, new("a"), GraphNodeRole.Ordinary, []),
            ],
            groups:
            [
                new(0, new("second"), null),
                new(1, new("first"), null),
            ]);

        Assert.Equal(
            ["z", "a"],
            document.Nodes.Select(node => node.Subject.Name));
        Assert.Equal(
            ["second", "first"],
            document.Groups.Select(group => group.Subject.Name));
    }

    private static TestDocument CreateDocument(
        IEnumerable<GraphNode<Subject>>? nodes = null,
        IEnumerable<GraphGroup<Subject>>? groups = null,
        IEnumerable<GraphEdge<Relationship>>? edges = null,
        IEnumerable<
            GraphOccurrence<
                Subject,
                Relationship,
                Receipt>>? occurrences = null,
        IEnumerable<GraphCharacteristic<Characteristic>>?
            characteristics = null,
        IEnumerable<GraphSeed<Subject>>? seeds = null,
        IEnumerable<GraphLimit<Limit>>? limits = null,
        IEnumerable<GraphFailure<Failure>>? failures = null,
        IEqualityComparer<Subject>? subjectComparer = null,
        IEqualityComparer<Relationship>? relationshipComparer = null)
    {
        GraphNode<Subject>[] defaultNodes =
        [
            new(0, new("node"), GraphNodeRole.Ordinary, []),
        ];
        return new(
            GraphDocumentScope.SessionBound,
            nodes ?? defaultNodes,
            groups ?? [],
            edges ?? [],
            occurrences ?? [],
            characteristics ?? [],
            seeds ?? [],
            limits ?? [],
            failures ?? [],
            subjectComparer,
            relationshipComparer);
    }

    private static GraphCharacteristicDerivation DirectDerivation() =>
        new(GraphCharacteristicDerivationKind.Direct, []);
}

public sealed record Subject(string Name);

public sealed record Relationship(string Name);

public sealed record Receipt(string Value);

public sealed record Characteristic(string Value);

public sealed record Limit(string Value);

public sealed record Failure(string Value);

public sealed class SubjectNameComparer :
    IEqualityComparer<Subject>
{
    private readonly StringComparer _names;

    private SubjectNameComparer(StringComparer names)
    {
        _names = names;
    }

    public static SubjectNameComparer OrdinalIgnoreCase { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool Equals(Subject? x, Subject? y) =>
        ReferenceEquals(x, y)
        || x is not null
            && y is not null
            && _names.Equals(x.Name, y.Name);

    public int GetHashCode(Subject value) =>
        _names.GetHashCode(value.Name);
}

public sealed class RelationshipNameComparer :
    IEqualityComparer<Relationship>
{
    private readonly StringComparer _names;

    private RelationshipNameComparer(StringComparer names)
    {
        _names = names;
    }

    public static RelationshipNameComparer OrdinalIgnoreCase { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool Equals(Relationship? x, Relationship? y) =>
        ReferenceEquals(x, y)
        || x is not null
            && y is not null
            && _names.Equals(x.Name, y.Name);

    public int GetHashCode(Relationship value) =>
        _names.GetHashCode(value.Name);
}
