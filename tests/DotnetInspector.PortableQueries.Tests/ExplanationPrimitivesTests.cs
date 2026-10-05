using System.Numerics;
using QuerySpace.Explanation;

namespace DotnetInspector.PortableQueries.Tests;

public sealed class ExplanationPrimitivesTests
{
    private static readonly ExplanationOwnerIdentity Owner =
        new("test.owner");
    private static readonly ExplanationSchemaIdentity SchemaIdentity =
        new(Owner, "test");
    private static readonly ExplanationSchemaVersion Version = new(1);
    private static readonly ExplanationDataShapeIdentity TextShape =
        new(SchemaIdentity, "text");
    private static readonly ExplanationDataShapeIdentity IntegerShape =
        new(SchemaIdentity, "integer");
    private static readonly ExplanationDataShapeIdentity NodeShape =
        new(SchemaIdentity, "node");
    private static readonly ExplanationDataShapeIdentity NodeReferenceShape =
        new(SchemaIdentity, "node-reference");
    private static readonly ExplanationFieldIdentity NodeValueField =
        new(NodeShape, "value");
    private static readonly ExplanationFieldIdentity NodeChildrenField =
        new(NodeShape, "children");
    private static readonly ExplanationResourceTypeIdentity ResourceType =
        new(SchemaIdentity, "node");
    private static readonly ExplanationFactIdentity NameFact =
        new(ResourceType, "name");
    private static readonly ExplanationRelationshipIdentity Children =
        new(ResourceType, "children");
    private static readonly ExplanationPublicAddressKindIdentity AddressKind =
        new(SchemaIdentity, "path");

    [Fact]
    public void RecursiveNamedShape_ValidatesWithinFiniteBudget()
    {
        ExplanationSchema schema = CreateSchema(
            new(maximumCanonicalByteCount: 4096, maximumDepth: 8, maximumNodeCount: 32));
        ExplanationValue value = Node(
            1,
            Node(2),
            Node(3, Node(4)));

        ExplanationValueMeasurement measurement =
            ExplanationConformance.ValidateValue(
                [schema],
                NodeShape,
                value);

        Assert.Equal(4, CountRecords(value));
        Assert.Equal(8, measurement.NodeCount);
        Assert.Equal(4, measurement.Depth);
        Assert.True(measurement.CanonicalByteCount > 0);
    }

    [Fact]
    public void RecursiveNamedShape_RejectsDepthBeyondBudget()
    {
        ExplanationSchema schema = CreateSchema(
            new(maximumCanonicalByteCount: 4096, maximumDepth: 2, maximumNodeCount: 32));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ExplanationConformance.ValidateValue(
                [schema],
                NodeShape,
                Node(1, Node(2))));

        Assert.Contains("exceeds", exception.Message);
    }

    [Fact]
    public void ScalarCarriers_AreFiniteAndStructurallyEqual()
    {
        Assert.Equal(
            ExplanationScalarValue.FromInteger(
                BigInteger.Parse("123456789012345678901234567890")),
            ExplanationScalarValue.FromInteger(
                BigInteger.Parse("123456789012345678901234567890")));
        Assert.Equal(
            ExplanationScalarValue.FromOctets([1, 2, 3]),
            ExplanationScalarValue.FromOctets([1, 2, 3]));
        Assert.True(Key(1) == Key(1));
        Assert.True(Key(1) != Key(2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExplanationScalarValue.FromBinaryFloatingPoint(
                double.PositiveInfinity));
        Assert.Throws<ArgumentException>(
            () => ExplanationScalarValue.FromText("\ud800"));
    }

    [Fact]
    public void Snapshot_RequiresEveryDeclaredObservationInOrder()
    {
        ExplanationSchema schema = CreateSchema(
            new(maximumCanonicalByteCount: 4096, maximumDepth: 8, maximumNodeCount: 32));
        ExplanationResourceKey key = Key(1);
        var address = new ExplanationPublicAddress(
            AddressKind,
            Text("nodes/1"));
        var name = new ExplanationFactObservation(
            NameFact,
            ExplanationObservationState.Available,
            [Text("one")]);
        var children = new ExplanationRelationshipObservation(
            Children,
            ExplanationObservationState.Available,
            [
                new ExplanationRelationshipTarget(Key(2)),
            ]);

        ExplanationResourceSnapshot snapshot =
            ExplanationConformance.CreateSnapshot(
                [schema],
                key,
                Version,
                ExplanationSnapshotScope.Installed,
                [address],
                [name],
                [children]);

        Assert.Equal(key, snapshot.Key);
        Assert.Single(snapshot.Addresses);
        Assert.Single(snapshot.Facts);
        Assert.Single(snapshot.Relationships);

        Assert.Throws<InvalidOperationException>(
            () => ExplanationConformance.CreateSnapshot(
                [schema],
                key,
                Version,
                ExplanationSnapshotScope.Installed,
                [address],
                [],
                [children]));
    }

    [Fact]
    public void ObservationDeclaration_AbsentRequiresOptionalOne()
    {
        Assert.Throws<ArgumentException>(() =>
            new ExplanationFactDeclaration(
                NameFact,
                "Name",
                "The node name.",
                TextShape,
                ExplanationCardinality.RequiredOne,
                ExplanationObservationStates.Available
                    | ExplanationObservationStates.Absent));
        Assert.Throws<ArgumentException>(() =>
            new ExplanationRelationshipDeclaration(
                Children,
                "Children",
                "Child nodes.",
                ResourceType,
                ExplanationCardinality.OrderedMany,
                ExplanationObservationStates.Available
                    | ExplanationObservationStates.Absent));
    }

    [Fact]
    public void Snapshot_RejectsMultipleAddressesOfOneKind()
    {
        ExplanationSchema schema = CreateSchema(
            new(
                maximumCanonicalByteCount: 4096,
                maximumDepth: 8,
                maximumNodeCount: 32));

        Assert.Throws<ArgumentException>(() =>
            ExplanationConformance.CreateSnapshot(
                [schema],
                Key(1),
                Version,
                ExplanationSnapshotScope.Installed,
                [
                    new(AddressKind, Text("nodes/1")),
                    new(AddressKind, Text("aliases/1")),
                ],
                [
                    new(
                        NameFact,
                        ExplanationObservationState.Available,
                        [Text("one")]),
                ],
                [
                    new(
                        Children,
                        ExplanationObservationState.Available,
                        []),
                ]));
    }

    [Fact]
    public void OptionalObservation_UsesAbsentInsteadOfAvailableEmpty()
    {
        ExplanationSchema schema = CreateSchema(
            new(
                maximumCanonicalByteCount: 4096,
                maximumDepth: 8,
                maximumNodeCount: 32),
            nameCardinality: ExplanationCardinality.OptionalOne,
            nameStates: ExplanationObservationStates.Available
                | ExplanationObservationStates.Absent,
            childrenCardinality: ExplanationCardinality.OptionalOne,
            childrenStates: ExplanationObservationStates.Available
                | ExplanationObservationStates.Absent);
        ExplanationResourceKey key = Key(1);
        var address = new ExplanationPublicAddress(
            AddressKind,
            Text("nodes/1"));
        var children = new ExplanationRelationshipObservation(
            Children,
            ExplanationObservationState.Available,
            []);

        Assert.Throws<InvalidOperationException>(() =>
            ExplanationConformance.CreateSnapshot(
                [schema],
                key,
                Version,
                ExplanationSnapshotScope.Installed,
                [address],
                [
                    new(
                        NameFact,
                        ExplanationObservationState.Available,
                        []),
                ],
                [children]));

        Assert.Throws<InvalidOperationException>(() =>
            ExplanationConformance.CreateSnapshot(
                [schema],
                key,
                Version,
                ExplanationSnapshotScope.Installed,
                [address],
                [
                    new(
                        NameFact,
                        ExplanationObservationState.Absent),
                ],
                [children]));

        ExplanationResourceSnapshot snapshot =
            ExplanationConformance.CreateSnapshot(
                [schema],
                key,
                Version,
                ExplanationSnapshotScope.Installed,
                [address],
                [
                    new(
                        NameFact,
                        ExplanationObservationState.Absent),
                ],
                [
                    new(
                        Children,
                        ExplanationObservationState.Absent),
                ]);

        Assert.Equal(
            ExplanationObservationState.Absent,
            Assert.Single(snapshot.Facts).State);
        Assert.Equal(
            ExplanationObservationState.Absent,
            Assert.Single(snapshot.Relationships).State);
    }

    [Fact]
    public void UnavailableRelationship_CarriesOutcomeDataAndNoTargets()
    {
        ExplanationDataShapeIdentity outcomeShape =
            new(SchemaIdentity, "reason");
        ExplanationRelationshipIdentity relationship =
            new(ResourceType, "optional-children");
        ExplanationSchema schema = CreateSchema(
            new(maximumCanonicalByteCount: 4096, maximumDepth: 8, maximumNodeCount: 32),
            outcomeShape,
            relationship);
        var observation = new ExplanationRelationshipObservation(
            relationship,
            ExplanationObservationState.Unavailable,
            outcomeData: Text("not acquired"));

        ExplanationResourceSnapshot snapshot =
            ExplanationConformance.CreateSnapshot(
                [schema],
                Key(1),
                Version,
                ExplanationSnapshotScope.Detached,
                [new ExplanationPublicAddress(AddressKind, Text("nodes/1"))],
                [
                    new(
                        NameFact,
                        ExplanationObservationState.Available,
                        [Text("one")]),
                ],
                [
                    new(
                        Children,
                        ExplanationObservationState.Available,
                        []),
                    observation,
                ]);

        Assert.Empty(snapshot.Relationships[1].Targets);
        Assert.NotNull(snapshot.Relationships[1].OutcomeData);
    }

    private static ExplanationSchema CreateSchema(
        ExplanationValueBudget nodeBudget,
        ExplanationDataShapeIdentity? outcomeShape = null,
        ExplanationRelationshipIdentity? optionalRelationship = null,
        ExplanationCardinality nameCardinality =
            ExplanationCardinality.RequiredOne,
        ExplanationObservationStates nameStates =
            ExplanationObservationStates.Available,
        ExplanationCardinality childrenCardinality =
            ExplanationCardinality.OrderedMany,
        ExplanationObservationStates childrenStates =
            ExplanationObservationStates.Available)
    {
        var shapes = new List<ExplanationDataShapeDeclaration>
        {
            new ExplanationDataShapeDeclaration.Scalar(
                TextShape,
                "Text",
                "Unicode text.",
                new(4096, 1, 1),
                ExplanationScalarKind.Text),
            new ExplanationDataShapeDeclaration.Scalar(
                IntegerShape,
                "Integer",
                "An arbitrary-precision integer.",
                new(256, 1, 1),
                ExplanationScalarKind.Integer),
            new ExplanationDataShapeDeclaration.Reference(
                NodeReferenceShape,
                "Node reference",
                "A recursive node reference.",
                nodeBudget,
                NodeShape),
            new ExplanationDataShapeDeclaration.Record(
                NodeShape,
                "Node",
                "One recursive node.",
                nodeBudget,
                [
                    new(
                        NodeValueField,
                        "Value",
                        "The node value.",
                        IntegerShape,
                        ExplanationCardinality.RequiredOne),
                    new(
                        NodeChildrenField,
                        "Children",
                        "Ordered child nodes.",
                        NodeReferenceShape,
                        ExplanationCardinality.OrderedMany,
                        maximumValueCount: 4),
                ]),
        };
        if (outcomeShape is { } outcome)
        {
            shapes.Add(
                new ExplanationDataShapeDeclaration.Reference(
                    outcome,
                    "Reason",
                    "A textual reason.",
                    new(4096, 1, 1),
                    TextShape));
        }

        var relationships =
            new List<ExplanationRelationshipDeclaration>
            {
                new(
                    Children,
                    "Children",
                    "Child nodes.",
                    ResourceType,
                    childrenCardinality,
                    childrenStates),
            };
        if (optionalRelationship is { } optional)
        {
            relationships.Add(
                new(
                    optional,
                    "Optional children",
                    "Children that may be unavailable.",
                    ResourceType,
                    ExplanationCardinality.OrderedMany,
                    ExplanationObservationStates.Available
                        | ExplanationObservationStates.Unavailable,
                    unavailableDataShape: outcomeShape));
        }

        var addressDeclaration =
            new ExplanationPublicAddressKindDeclaration(
                AddressKind,
                "Path",
                "A public test path.",
                TextShape);
        return new(
            SchemaIdentity,
            Version,
            shapes,
            [
                new ExplanationResourceTypeDeclaration(
                    ResourceType,
                    "Node",
                    "A test node.",
                    IntegerShape,
                    [
                        new ExplanationFactDeclaration(
                            NameFact,
                            "Name",
                            "The node name.",
                            TextShape,
                            nameCardinality,
                            nameStates),
                    ],
                    relationships,
                    [AddressKind]),
            ],
            [addressDeclaration]);
    }

    private static ExplanationValue Node(
        int value,
        params ExplanationValue[] children) =>
        new ExplanationValue.Record(
        [
            new(
                NodeValueField,
                [
                    new ExplanationValue.Scalar(
                        ExplanationScalarValue.FromInteger(value)),
                ]),
            new(NodeChildrenField, children),
        ]);

    private static ExplanationValue Text(string value) =>
        new ExplanationValue.Scalar(
            ExplanationScalarValue.FromText(value));

    private static ExplanationResourceKey Key(int value) =>
        new(
            Owner,
            ResourceType,
            new ExplanationValue.Scalar(
                ExplanationScalarValue.FromInteger(value)));

    private static int CountRecords(ExplanationValue value) =>
        value is ExplanationValue.Record record
            ? 1 + record.Fields.Sum(field =>
                field.Values.Sum(CountRecords))
            : 0;
}
