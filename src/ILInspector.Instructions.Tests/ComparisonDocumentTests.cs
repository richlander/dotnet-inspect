using System.Collections.Immutable;

using ILInspector.Findings;

namespace ILInspector.Instructions.Tests;

public class ComparisonDocumentTests
{
    [Fact]
    public void Constructor_PreservesSubjectOrderAndCanonicalizesDescriptions()
    {
        ComparisonChangeDescription rootDescription = Description(
            "root-z",
            ComparisonExceptionalChangeKind.Rename,
            "OldRoot",
            "OldRoot",
            "NewRoot",
            "NewRoot");
        ComparisonChangeDescription subjectDescription = Description(
            "subject-a",
            ComparisonExceptionalChangeKind.Move,
            "A.Type.M",
            "A.Type.M()",
            "B.Type.M",
            "B.Type.M()");
        var first = Subject(
            "B.Type.M",
            "B.Type.M()",
            new ComparisonSubjectChange.Move("subject-a"),
            "first");
        var second = Subject(
            "Stable",
            "Stable",
            new ComparisonSubjectChange.Diff(),
            "second");

        ComparisonDocument<string> document = Document(
            identifier: "NewRoot",
            display: "NewRoot",
            change: new ComparisonSubjectChange.Rename("root-z"),
            subjects: [first, second],
            descriptions: [rootDescription, subjectDescription]);

        Assert.Equal([first, second], document.Subjects);
        Assert.Equal(
            ["root-z", "subject-a"],
            document.ChangeDescriptions.Select(description => description.Id));
    }

    [Fact]
    public void Constructor_AcceptsEveryChangeCase()
    {
        ComparisonDocument<string> document = Document(
            subjects:
            [
                Subject("Diff", "Diff", new ComparisonSubjectChange.Diff(), "diff"),
                Subject("Added", "Added", new ComparisonSubjectChange.Addition(), "addition"),
                Subject("Deleted", "Deleted", new ComparisonSubjectChange.Deletion(), "deletion"),
                Subject(
                    "Renamed",
                    "Renamed",
                    new ComparisonSubjectChange.Rename("rename"),
                    "rename"),
                Subject(
                    "Moved",
                    "Moved",
                    new ComparisonSubjectChange.Move("move"),
                    "move"),
                Subject(
                    "RenamedMoved",
                    "RenamedMoved",
                    new ComparisonSubjectChange.RenameAndMove("rename-move"),
                    "rename-move"),
            ],
            descriptions:
            [
                Description(
                    "rename",
                    ComparisonExceptionalChangeKind.Rename,
                    "OldName",
                    "OldName",
                    "Renamed",
                    "Renamed"),
                Description(
                    "move",
                    ComparisonExceptionalChangeKind.Move,
                    "OldContainer.Moved",
                    "OldContainer.Moved",
                    "Moved",
                    "Moved"),
                Description(
                    "rename-move",
                    ComparisonExceptionalChangeKind.RenameAndMove,
                    "OldContainer.OldName",
                    "OldContainer.OldName",
                    "RenamedMoved",
                    "RenamedMoved"),
            ]);

        Assert.Collection(
            document.Subjects,
            subject => Assert.IsType<ComparisonSubjectChange.Diff>(subject.Change),
            subject => Assert.IsType<ComparisonSubjectChange.Addition>(subject.Change),
            subject => Assert.IsType<ComparisonSubjectChange.Deletion>(subject.Change),
            subject => Assert.IsType<ComparisonSubjectChange.Rename>(subject.Change),
            subject => Assert.IsType<ComparisonSubjectChange.Move>(subject.Change),
            subject => Assert.IsType<ComparisonSubjectChange.RenameAndMove>(subject.Change));
    }

    [Fact]
    public void Constructor_PreservesExplicitRootComparisonPresence()
    {
        var present = Document(
            comparison: new ComparisonRootComparison<string>.Present("root"));
        var absent = Document(
            comparison: new ComparisonRootComparison<string>.NotApplicable());

        Assert.Equal(
            "root",
            Assert.IsType<ComparisonRootComparison<string>.Present>(
                present.Comparison).Comparison);
        Assert.IsType<ComparisonRootComparison<string>.NotApplicable>(
            absent.Comparison);
    }

    [Fact]
    public void RootRelative_TwoSidedRootAcceptsEverySubjectKind()
    {
        ComparisonDocument<string> document = Document(
            basis: SubjectCoordinateBasis.RootRelative,
            subjects:
            [
                Subject("Diff", "Diff", new ComparisonSubjectChange.Diff()),
                Subject("Added", "Added", new ComparisonSubjectChange.Addition()),
                Subject("Deleted", "Deleted", new ComparisonSubjectChange.Deletion()),
                Subject(
                    "Renamed",
                    "Renamed",
                    new ComparisonSubjectChange.Rename("rename")),
                Subject(
                    "Moved",
                    "Moved",
                    new ComparisonSubjectChange.Move("move")),
                Subject(
                    "RenamedMoved",
                    "RenamedMoved",
                    new ComparisonSubjectChange.RenameAndMove("rename-move")),
            ],
            descriptions:
            [
                Description(
                    "rename",
                    ComparisonExceptionalChangeKind.Rename,
                    "OldName",
                    "OldName",
                    "Renamed",
                    "Renamed"),
                Description(
                    "move",
                    ComparisonExceptionalChangeKind.Move,
                    "OldContainer.Moved",
                    "OldContainer.Moved",
                    "Moved",
                    "Moved"),
                Description(
                    "rename-move",
                    ComparisonExceptionalChangeKind.RenameAndMove,
                    "OldContainer.OldName",
                    "OldContainer.OldName",
                    "RenamedMoved",
                    "RenamedMoved"),
            ]);

        Assert.Equal(6, document.Subjects.Length);
    }

    [Fact]
    public void RootRelative_OneSidedRootsAcceptOnlySameSideSubjects()
    {
        ComparisonDocument<string> addition = Document(
            basis: SubjectCoordinateBasis.RootRelative,
            change: new ComparisonSubjectChange.Addition(),
            subjects:
            [
                Subject(
                    "Added",
                    "Added",
                    new ComparisonSubjectChange.Addition()),
            ]);
        ComparisonDocument<string> deletion = Document(
            basis: SubjectCoordinateBasis.RootRelative,
            change: new ComparisonSubjectChange.Deletion(),
            subjects:
            [
                Subject(
                    "Deleted",
                    "Deleted",
                    new ComparisonSubjectChange.Deletion()),
            ]);

        Assert.Single(addition.Subjects);
        Assert.Single(deletion.Subjects);
        Assert.Empty(
            Document(
                basis: SubjectCoordinateBasis.RootRelative,
                change: new ComparisonSubjectChange.Addition()).Subjects);
        Assert.Empty(
            Document(
                basis: SubjectCoordinateBasis.RootRelative,
                change: new ComparisonSubjectChange.Deletion()).Subjects);
    }

    [Fact]
    public void RootRelative_EveryRootKindAcceptsEmptySubjectPopulation()
    {
        ComparisonSubjectChange[] changes =
        [
            new ComparisonSubjectChange.Diff(),
            new ComparisonSubjectChange.Addition(),
            new ComparisonSubjectChange.Deletion(),
            new ComparisonSubjectChange.Rename("rename"),
            new ComparisonSubjectChange.Move("move"),
            new ComparisonSubjectChange.RenameAndMove("rename-move"),
        ];

        foreach (ComparisonSubjectChange change in changes)
        {
            ImmutableArray<ComparisonChangeDescription> descriptions =
                ExceptionalDescription(change, "Root", "Root");
            ComparisonDocument<string> document = Document(
                basis: SubjectCoordinateBasis.RootRelative,
                change: change,
                descriptions: descriptions);

            Assert.Empty(document.Subjects);
        }
    }

    [Fact]
    public void RootRelative_RejectsEverySubjectRequiringAbsentRootSide()
    {
        ComparisonSubjectChange[] additionRootInvalidChildren =
        [
            new ComparisonSubjectChange.Diff(),
            new ComparisonSubjectChange.Deletion(),
            new ComparisonSubjectChange.Rename("rename"),
            new ComparisonSubjectChange.Move("move"),
            new ComparisonSubjectChange.RenameAndMove("rename-move"),
        ];
        ComparisonSubjectChange[] deletionRootInvalidChildren =
        [
            new ComparisonSubjectChange.Diff(),
            new ComparisonSubjectChange.Addition(),
            new ComparisonSubjectChange.Rename("rename"),
            new ComparisonSubjectChange.Move("move"),
            new ComparisonSubjectChange.RenameAndMove("rename-move"),
        ];

        foreach (ComparisonSubjectChange childChange in additionRootInvalidChildren)
        {
            Assert.Throws<ArgumentException>(
                () => Document(
                    basis: SubjectCoordinateBasis.RootRelative,
                    change: new ComparisonSubjectChange.Addition(),
                    subjects:
                    [
                        Subject("Child", "Child", childChange),
                    ]));
        }
        foreach (ComparisonSubjectChange childChange in deletionRootInvalidChildren)
        {
            Assert.Throws<ArgumentException>(
                () => Document(
                    basis: SubjectCoordinateBasis.RootRelative,
                    change: new ComparisonSubjectChange.Deletion(),
                    subjects:
                    [
                        Subject("Child", "Child", childChange),
                    ]));
        }
    }

    [Fact]
    public void RootRelative_ExceptionalRootKeepsStableChildDiff()
    {
        foreach (ComparisonSubjectChange rootChange in new ComparisonSubjectChange[]
        {
            new ComparisonSubjectChange.Rename("rename"),
            new ComparisonSubjectChange.Move("move"),
        })
        {
            ComparisonDocument<string> document = Document(
                basis: SubjectCoordinateBasis.RootRelative,
                change: rootChange,
                subjects:
                [
                    Subject(
                        "MemberAnchor",
                        "Member()",
                        new ComparisonSubjectChange.Diff()),
                ],
                descriptions: ExceptionalDescription(rootChange, "Root", "Root"));

            Assert.IsType<ComparisonSubjectChange.Diff>(
                Assert.Single(document.Subjects).Change);
        }
    }

    [Fact]
    public void OuterContext_AllowsIndependentRootAndSubjectLifecycles()
    {
        ComparisonDocument<string> document = Document(
            basis: SubjectCoordinateBasis.OuterContext,
            change: new ComparisonSubjectChange.Addition(),
            subjects:
            [
                Subject(
                    "Deleted",
                    "Deleted",
                    new ComparisonSubjectChange.Deletion()),
            ]);

        Assert.Single(document.Subjects);
    }

    [Fact]
    public void OuterContext_AcceptsEveryIndependentOneSidedRootCombination()
    {
        ComparisonSubjectChange[] additionRootChildren =
        [
            new ComparisonSubjectChange.Diff(),
            new ComparisonSubjectChange.Deletion(),
            new ComparisonSubjectChange.Rename("rename"),
            new ComparisonSubjectChange.Move("move"),
            new ComparisonSubjectChange.RenameAndMove("rename-move"),
        ];
        ComparisonSubjectChange[] deletionRootChildren =
        [
            new ComparisonSubjectChange.Diff(),
            new ComparisonSubjectChange.Addition(),
            new ComparisonSubjectChange.Rename("rename"),
            new ComparisonSubjectChange.Move("move"),
            new ComparisonSubjectChange.RenameAndMove("rename-move"),
        ];

        foreach (ComparisonSubjectChange childChange in additionRootChildren)
        {
            Assert.Single(
                Document(
                    change: new ComparisonSubjectChange.Addition(),
                    subjects: [Subject("Child", "Child", childChange)],
                    descriptions: ExceptionalDescription(
                        childChange,
                        "Child",
                        "Child")).Subjects);
        }
        foreach (ComparisonSubjectChange childChange in deletionRootChildren)
        {
            Assert.Single(
                Document(
                    change: new ComparisonSubjectChange.Deletion(),
                    subjects: [Subject("Child", "Child", childChange)],
                    descriptions: ExceptionalDescription(
                        childChange,
                        "Child",
                        "Child")).Subjects);
        }
    }

    [Fact]
    public void OuterContext_RequiresASeparateExceptionalChildWhenItsCoordinateChanges()
    {
        ComparisonChangeDescription rootDescription = Description(
            "root",
            ComparisonExceptionalChangeKind.Move,
            "AssemblyA.Type",
            "AssemblyA.Type",
            "AssemblyB.Type",
            "AssemblyB.Type");
        ComparisonChangeDescription childDescription = Description(
            "child",
            ComparisonExceptionalChangeKind.Move,
            "AssemblyA.Type.Member",
            "AssemblyA.Type.Member()",
            "AssemblyB.Type.Member",
            "AssemblyB.Type.Member()");
        ComparisonDocument<string> document = Document(
            basis: SubjectCoordinateBasis.OuterContext,
            identifier: "AssemblyB.Type",
            display: "AssemblyB.Type",
            change: new ComparisonSubjectChange.Move("root"),
            subjects:
            [
                Subject(
                    "AssemblyB.Type.Member",
                    "AssemblyB.Type.Member()",
                    new ComparisonSubjectChange.Move("child")),
            ],
            descriptions: [rootDescription, childDescription]);

        Assert.Equal("root", Assert.IsType<ComparisonSubjectChange.Move>(
            document.Change).ChangeId);
        Assert.Equal("child", Assert.IsType<ComparisonSubjectChange.Move>(
            Assert.Single(document.Subjects).Change).ChangeId);
    }

    [Fact]
    public void Constructor_ComposesPortableCoordinatesAcrossRequiredScopes()
    {
        ComparisonDocument<ComparisonDocumentTestPayload>[] documents =
        [
            CoordinateDocument(
                "Assembly.Type",
                ["Assembly.Type.Member"],
                "within-type"),
            CoordinateDocument(
                "Assembly",
                ["TypeB.Member"],
                "within-assembly"),
            CoordinateDocument(
                "PackageComparison",
                ["AssemblyA.Type.Member", "AssemblyB.Type.Member"],
                "cross-assembly"),
        ];

        Assert.Equal(
            ["within-type", "within-assembly", "cross-assembly"],
            documents.Select(
                document => document.Subjects[0].Comparison.Orientation));
        Assert.Equal(2, documents[2].Subjects.Length);
    }

    [Fact]
    public void Constructor_RejectsUninitializedNullAndUnknownValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ComparisonDocument<string>(
                2,
                SubjectCoordinateBasis.OuterContext,
                "Root",
                "Root",
                new ComparisonSubjectChange.Diff(),
                new ComparisonRootComparison<string>.NotApplicable(),
                [],
                []));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Document(basis: (SubjectCoordinateBasis)int.MaxValue));
        Assert.Throws<ArgumentException>(
            () => Document(identifier: ""));
        Assert.Throws<ArgumentException>(
            () => Document(display: " "));
        Assert.Throws<ArgumentNullException>(
            () => new ComparisonDocument<string>(
                ComparisonDocument<string>.CurrentSchemaVersion,
                SubjectCoordinateBasis.OuterContext,
                "Root",
                "Root",
                null!,
                new ComparisonRootComparison<string>.NotApplicable(),
                [],
                []));
        Assert.Throws<ArgumentNullException>(
            () => new ComparisonDocument<string>(
                ComparisonDocument<string>.CurrentSchemaVersion,
                SubjectCoordinateBasis.OuterContext,
                "Root",
                "Root",
                new ComparisonSubjectChange.Diff(),
                null!,
                [],
                []));
        Assert.Throws<ArgumentException>(
            () => new ComparisonDocument<string>(
                ComparisonDocument<string>.CurrentSchemaVersion,
                SubjectCoordinateBasis.OuterContext,
                "Root",
                "Root",
                new ComparisonSubjectChange.Diff(),
                new ComparisonRootComparison<string>.NotApplicable(),
                default,
                []));
        Assert.Throws<ArgumentException>(
            () => new ComparisonDocument<string>(
                ComparisonDocument<string>.CurrentSchemaVersion,
                SubjectCoordinateBasis.OuterContext,
                "Root",
                "Root",
                new ComparisonSubjectChange.Diff(),
                new ComparisonRootComparison<string>.NotApplicable(),
                [],
                default));
        Assert.Throws<ArgumentException>(
            () => Document(subjects: [null!]));
        Assert.Throws<ArgumentException>(
            () => Document(descriptions: [null!]));
        Assert.Throws<ArgumentNullException>(
            () => new ComparisonSubject<string>(
                "S",
                "S",
                new ComparisonSubjectChange.Diff(),
                null!));
        Assert.Throws<ArgumentNullException>(
            () => new ComparisonRootComparison<string>.Present(null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ComparisonChangeDescription(
                "id",
                (ComparisonExceptionalChangeKind)int.MaxValue,
                new ComparisonSubjectEndpoint("before", "before"),
                new ComparisonSubjectEndpoint("after", "after"),
                []));
    }

    [Fact]
    public void ChangeDescription_RejectsInvalidEndpointsAndTransformations()
    {
        Assert.Throws<ArgumentException>(
            () => Description(
                "id",
                ComparisonExceptionalChangeKind.Rename,
                "same",
                "before",
                "same",
                "after"));
        Assert.Throws<ArgumentException>(
            () => new ComparisonChangeDescription(
                "id",
                ComparisonExceptionalChangeKind.Move,
                new ComparisonSubjectEndpoint("before", "before"),
                new ComparisonSubjectEndpoint("after", "after"),
                default));
        Assert.Throws<ArgumentException>(
            () => new ComparisonChangeDescription(
                "id",
                ComparisonExceptionalChangeKind.Move,
                new ComparisonSubjectEndpoint("before", "before"),
                new ComparisonSubjectEndpoint("after", "after"),
                [null!]));
        Assert.Throws<ArgumentException>(
            () => new ComparisonChangeDescription(
                "id",
                ComparisonExceptionalChangeKind.Move,
                new ComparisonSubjectEndpoint("before", "before"),
                new ComparisonSubjectEndpoint("after", "after"),
                [
                    new ComparisonTransformationDescriptor("kind", "first"),
                    new ComparisonTransformationDescriptor("kind", "second"),
                ]));
    }

    [Fact]
    public void Constructor_RequiresDescriptionsToMatchExactlyOneReferrer()
    {
        ComparisonChangeDescription description = Description(
            "change",
            ComparisonExceptionalChangeKind.Rename,
            "Before",
            "Before",
            "After",
            "After");

        Assert.Throws<ArgumentException>(
            () => Document(
                identifier: "After",
                display: "After",
                change: new ComparisonSubjectChange.Rename("missing")));
        Assert.Throws<ArgumentException>(
            () => Document(descriptions: [description]));
        Assert.Throws<ArgumentException>(
            () => Document(
                identifier: "After",
                display: "After",
                change: new ComparisonSubjectChange.Rename("change"),
                subjects:
                [
                    Subject(
                        "After",
                        "After",
                        new ComparisonSubjectChange.Rename("change")),
                ],
                descriptions: [description]));
        Assert.Throws<ArgumentException>(
            () => Document(
                identifier: "After",
                display: "After",
                change: new ComparisonSubjectChange.Move("change"),
                descriptions: [description]));
        Assert.Throws<ArgumentException>(
            () => Document(
                identifier: "Wrong",
                display: "After",
                change: new ComparisonSubjectChange.Rename("change"),
                descriptions: [description]));
        Assert.Throws<ArgumentException>(
            () => Document(
                identifier: "After",
                display: "Wrong",
                change: new ComparisonSubjectChange.Rename("change"),
                descriptions: [description]));
        Assert.Throws<ArgumentException>(
            () => Document(
                descriptions: [description, description]));
    }

    [Fact]
    public void Constructor_RejectsDuplicateEndpointCoordinates()
    {
        Assert.Throws<ArgumentException>(
            () => Document(
                subjects:
                [
                    Subject("Same", "First", new ComparisonSubjectChange.Diff()),
                    Subject("Same", "Second", new ComparisonSubjectChange.Diff()),
                ]));
        Assert.Throws<ArgumentException>(
            () => Document(
                subjects:
                [
                    Subject("Same", "Diff", new ComparisonSubjectChange.Diff()),
                    Subject(
                        "Same",
                        "Deleted",
                        new ComparisonSubjectChange.Deletion()),
                ]));

        ComparisonChangeDescription move = Description(
            "move",
            ComparisonExceptionalChangeKind.Move,
            "Before",
            "Before",
            "After",
            "After");
        Assert.Throws<ArgumentException>(
            () => Document(
                subjects:
                [
                    Subject(
                        "Before",
                        "Deleted",
                        new ComparisonSubjectChange.Deletion()),
                    Subject(
                        "After",
                        "After",
                        new ComparisonSubjectChange.Move("move")),
                ],
                descriptions: [move]));

        ComparisonChangeDescription firstMove = Description(
            "first",
            ComparisonExceptionalChangeKind.Move,
            "SharedBefore",
            "SharedBefore",
            "FirstAfter",
            "FirstAfter");
        ComparisonChangeDescription secondMove = Description(
            "second",
            ComparisonExceptionalChangeKind.Move,
            "SharedBefore",
            "SharedBefore",
            "SecondAfter",
            "SecondAfter");
        Assert.Throws<ArgumentException>(
            () => Document(
                subjects:
                [
                    Subject(
                        "FirstAfter",
                        "FirstAfter",
                        new ComparisonSubjectChange.Move("first")),
                    Subject(
                        "SecondAfter",
                        "SecondAfter",
                        new ComparisonSubjectChange.Move("second")),
                ],
                descriptions: [firstMove, secondMove]));

        ComparisonChangeDescription diffCollision = Description(
            "diff-collision",
            ComparisonExceptionalChangeKind.Rename,
            "Stable",
            "Stable",
            "Changed",
            "Changed");
        Assert.Throws<ArgumentException>(
            () => Document(
                subjects:
                [
                    Subject(
                        "Stable",
                        "Stable",
                        new ComparisonSubjectChange.Diff()),
                    Subject(
                        "Changed",
                        "Changed",
                        new ComparisonSubjectChange.Rename("diff-collision")),
                ],
                descriptions: [diffCollision]));
    }

    [Fact]
    public void Constructor_AllowsSameSpellingInSeparateEndpointSpaces()
    {
        ComparisonDocument<string> document = Document(
            subjects:
            [
                Subject(
                    "Same",
                    "Deleted",
                    new ComparisonSubjectChange.Deletion()),
                Subject(
                    "Same",
                    "Added",
                    new ComparisonSubjectChange.Addition()),
            ]);

        Assert.Equal(2, document.Subjects.Length);
    }

    [Fact]
    public void ValueEquality_IncludesBasisOrderPayloadAndDescriptions()
    {
        ComparisonDocument<string> first = Document(
            subjects:
            [
                Subject("A", "A", new ComparisonSubjectChange.Diff(), "one"),
                Subject("B", "B", new ComparisonSubjectChange.Diff(), "two"),
            ]);
        ComparisonDocument<string> equal = Document(
            subjects:
            [
                Subject("A", "A", new ComparisonSubjectChange.Diff(), "one"),
                Subject("B", "B", new ComparisonSubjectChange.Diff(), "two"),
            ]);
        ComparisonDocument<string> reordered = Document(
            subjects:
            [
                Subject("B", "B", new ComparisonSubjectChange.Diff(), "two"),
                Subject("A", "A", new ComparisonSubjectChange.Diff(), "one"),
            ]);
        ComparisonDocument<string> differentBasis = Document(
            basis: SubjectCoordinateBasis.RootRelative,
            subjects:
            [
                Subject("A", "A", new ComparisonSubjectChange.Diff(), "one"),
                Subject("B", "B", new ComparisonSubjectChange.Diff(), "two"),
            ]);

        Assert.Equal(first, equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(first, reordered);
        Assert.NotEqual(first, differentBasis);
    }

    [Fact]
    public void ValueEquality_DistinguishesContractDefiningTopologyAndPresence()
    {
        ComparisonDocument<string> emptyDiff = Document();
        ComparisonDocument<string> emptyAddition = Document(
            change: new ComparisonSubjectChange.Addition());
        ComparisonDocument<string> rootPresent = Document(
            comparison: new ComparisonRootComparison<string>.Present("root"));
        ComparisonDocument<string> subjectDiff = Document(
            subjects:
            [
                Subject("Subject", "Subject", new ComparisonSubjectChange.Diff()),
            ]);
        ComparisonDocument<string> subjectAddition = Document(
            subjects:
            [
                Subject("Subject", "Subject", new ComparisonSubjectChange.Addition()),
            ]);

        Assert.NotEqual(emptyDiff, emptyAddition);
        Assert.NotEqual(emptyDiff, rootPresent);
        Assert.NotEqual(subjectDiff, subjectAddition);
    }

    [Fact]
    public void PayloadEquality_IsIndependentOfSubjectIdentity()
    {
        var payload = new ComparisonDocumentTestPayload("same", "same");
        var first = new ComparisonSubject<ComparisonDocumentTestPayload>(
            "First",
            "First",
            new ComparisonSubjectChange.Diff(),
            payload);
        var second = new ComparisonSubject<ComparisonDocumentTestPayload>(
            "Second",
            "Second",
            new ComparisonSubjectChange.Diff(),
            new ComparisonDocumentTestPayload("same", "same"));

        Assert.NotEqual(first, second);
        Assert.Equal(first.Comparison, second.Comparison);
    }

    [Fact]
    public void SubjectTopology_RemainsIndependentOfOpaquePayloadDisposition()
    {
        var movedPayload = new ComparisonDocumentTestPayload(
            "contains moved item relations",
            "moved");
        var unchangedPayload = new ComparisonDocumentTestPayload(
            "contains unchanged item relations",
            "unchanged");
        ComparisonChangeDescription moveDescription = Description(
            "move",
            ComparisonExceptionalChangeKind.Move,
            "Before",
            "Before",
            "After",
            "After");
        var document = new ComparisonDocument<ComparisonDocumentTestPayload>(
            ComparisonDocument<ComparisonDocumentTestPayload>.CurrentSchemaVersion,
            SubjectCoordinateBasis.OuterContext,
            "Root",
            "Root",
            new ComparisonSubjectChange.Diff(),
            new ComparisonRootComparison<ComparisonDocumentTestPayload>.NotApplicable(),
            [
                new(
                    "Stable",
                    "Stable",
                    new ComparisonSubjectChange.Diff(),
                    movedPayload),
                new(
                    "After",
                    "After",
                    new ComparisonSubjectChange.Move("move"),
                    unchangedPayload),
            ],
            [moveDescription]);

        Assert.IsType<ComparisonSubjectChange.Diff>(document.Subjects[0].Change);
        Assert.IsType<ComparisonSubjectChange.Move>(document.Subjects[1].Change);
    }

    [Fact]
    public void GenericPayload_PreservesPathologicalThreeRegionComposition()
    {
        AnalysisDiff<string> rootDiff = new(
            ["Process:A", "Process:B", "Process:C"],
            ["NewPartA:A", "NewPartB:B", "NewPartC:C"],
            [
                new AnalysisDiffRelation.Correspondence(
                    [0],
                    [0],
                    AnalysisDiffContentKind.Unchanged,
                    AnalysisDiffPlacementKind.Moved),
                new AnalysisDiffRelation.Correspondence(
                    [1],
                    [1],
                    AnalysisDiffContentKind.Unchanged,
                    AnalysisDiffPlacementKind.Moved),
                new AnalysisDiffRelation.Correspondence(
                    [2],
                    [2],
                    AnalysisDiffContentKind.Unchanged,
                    AnalysisDiffPlacementKind.Moved),
            ]);
        AnalysisDiff<string> processDiff = new(
            ["alpha", "beta", "gamma"],
            ["alpha", "beta-prime", "gamma"],
            [
                new AnalysisDiffRelation.Correspondence(
                    [0],
                    [0],
                    AnalysisDiffContentKind.Unchanged,
                    AnalysisDiffPlacementKind.Stable),
                new AnalysisDiffRelation.Correspondence(
                    [1],
                    [1],
                    AnalysisDiffContentKind.Changed,
                    AnalysisDiffPlacementKind.Stable),
                new AnalysisDiffRelation.Correspondence(
                    [2],
                    [2],
                    AnalysisDiffContentKind.Unchanged,
                    AnalysisDiffPlacementKind.Stable),
            ]);
        AnalysisDiff<string> newPart = new(
            [],
            ["body"],
            [new AnalysisDiffRelation.Addition([0])]);

        var document = new ComparisonDocument<AnalysisDiff<string>>(
            ComparisonDocument<AnalysisDiff<string>>.CurrentSchemaVersion,
            SubjectCoordinateBasis.RootRelative,
            "Assembly.Type",
            "Assembly.Type",
            new ComparisonSubjectChange.Diff(),
            new ComparisonRootComparison<AnalysisDiff<string>>.Present(rootDiff),
            [
                new(
                    "Process",
                    "Process()",
                    new ComparisonSubjectChange.Diff(),
                    processDiff),
                new(
                    "NewPartA",
                    "NewPartA()",
                    new ComparisonSubjectChange.Addition(),
                    newPart),
                new(
                    "NewPartB",
                    "NewPartB()",
                    new ComparisonSubjectChange.Addition(),
                    newPart),
                new(
                    "NewPartC",
                    "NewPartC()",
                    new ComparisonSubjectChange.Addition(),
                    newPart),
            ],
            []);

        Assert.Equal(rootDiff, Assert.IsType<
            ComparisonRootComparison<AnalysisDiff<string>>.Present>(
                document.Comparison).Comparison);
        Assert.Equal(processDiff, document.Subjects[0].Comparison);
        Assert.All(
            rootDiff.Relations,
            relation => Assert.Equal(
                AnalysisDiffPlacementKind.Moved,
                Assert.IsType<AnalysisDiffRelation.Correspondence>(
                    relation).Placement));
        Assert.All(
            document.Subjects[1..],
            subject => Assert.IsType<ComparisonSubjectChange.Addition>(subject.Change));
    }

    [Fact]
    public void ChangeDescription_PreservesExtensionTransformations()
    {
        var toExtension = new ComparisonTransformationDescriptor(
            "dotnet.member.to-extension",
            "Converted to extension method");
        var fromExtension = new ComparisonTransformationDescriptor(
            "dotnet.member.from-extension",
            "Converted from extension method");
        ComparisonChangeDescription description = new(
            "move",
            ComparisonExceptionalChangeKind.Move,
            new ComparisonSubjectEndpoint("Type.Member", "Type.Member()"),
            new ComparisonSubjectEndpoint(
                "Extensions.Member",
                "Extensions.Member(Type)"),
            [toExtension, fromExtension]);
        ComparisonDocument<string> document = Document(
            identifier: "Extensions.Member",
            display: "Extensions.Member(Type)",
            change: new ComparisonSubjectChange.Move("move"),
            descriptions: [description]);

        Assert.Equal(
            [toExtension, fromExtension],
            Assert.Single(document.ChangeDescriptions).Transformations);
    }

    static ComparisonDocument<ComparisonDocumentTestPayload> CoordinateDocument(
        string rootIdentifier,
        ImmutableArray<string> subjectIdentifiers,
        string orientation)
        => new(
            ComparisonDocument<ComparisonDocumentTestPayload>.CurrentSchemaVersion,
            SubjectCoordinateBasis.OuterContext,
            rootIdentifier,
            rootIdentifier,
            new ComparisonSubjectChange.Diff(),
            new ComparisonRootComparison<ComparisonDocumentTestPayload>.Present(
                new("root item space", orientation)),
            [..
                subjectIdentifiers.Select(subjectIdentifier => new ComparisonSubject<
                    ComparisonDocumentTestPayload>(
                    subjectIdentifier,
                    subjectIdentifier,
                    new ComparisonSubjectChange.Diff(),
                    new("subject item space", orientation)))],
            []);

    static ComparisonDocument<string> Document(
        SubjectCoordinateBasis basis = SubjectCoordinateBasis.OuterContext,
        string identifier = "Root",
        string display = "Root",
        ComparisonSubjectChange? change = null,
        ComparisonRootComparison<string>? comparison = null,
        ImmutableArray<ComparisonSubject<string>> subjects = default,
        ImmutableArray<ComparisonChangeDescription> descriptions = default)
        => new(
            ComparisonDocument<string>.CurrentSchemaVersion,
            basis,
            identifier,
            display,
            change ?? new ComparisonSubjectChange.Diff(),
            comparison ?? new ComparisonRootComparison<string>.NotApplicable(),
            subjects.IsDefault ? [] : subjects,
            descriptions.IsDefault ? [] : descriptions);

    static ComparisonSubject<string> Subject(
        string identifier,
        string display,
        ComparisonSubjectChange change,
        string comparison = "payload")
        => new(identifier, display, change, comparison);

    static ComparisonChangeDescription Description(
        string id,
        ComparisonExceptionalChangeKind kind,
        string beforeIdentifier,
        string beforeDisplay,
        string afterIdentifier,
        string afterDisplay)
        => new(
            id,
            kind,
            new ComparisonSubjectEndpoint(beforeIdentifier, beforeDisplay),
            new ComparisonSubjectEndpoint(afterIdentifier, afterDisplay),
            []);

    static ImmutableArray<ComparisonChangeDescription> ExceptionalDescription(
        ComparisonSubjectChange change,
        string afterIdentifier,
        string afterDisplay)
        => change switch
        {
            ComparisonSubjectChange.Rename rename =>
            [
                Description(
                    rename.ChangeId,
                    ComparisonExceptionalChangeKind.Rename,
                    $"Before.{afterIdentifier}",
                    $"Before {afterDisplay}",
                    afterIdentifier,
                    afterDisplay),
            ],
            ComparisonSubjectChange.Move move =>
            [
                Description(
                    move.ChangeId,
                    ComparisonExceptionalChangeKind.Move,
                    $"Before.{afterIdentifier}",
                    $"Before {afterDisplay}",
                    afterIdentifier,
                    afterDisplay),
            ],
            ComparisonSubjectChange.RenameAndMove renameAndMove =>
            [
                Description(
                    renameAndMove.ChangeId,
                    ComparisonExceptionalChangeKind.RenameAndMove,
                    $"Before.{afterIdentifier}",
                    $"Before {afterDisplay}",
                    afterIdentifier,
                    afterDisplay),
            ],
            _ => [],
        };
}
