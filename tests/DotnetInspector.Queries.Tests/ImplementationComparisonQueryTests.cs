using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using Inspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

public sealed class ImplementationComparisonQueryTests
{
    [Fact]
    public void Execute_UsesSuppliedAssemblyContentForCSharpAndIlEvidence()
    {
        string oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        string newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        ImplementationDiffResult result =
            ImplementationComparisonQuery.Execute(
                new ImplementationComparisonInput(
                    [StreamBackedInput(oldPath, "old.dll")],
                    [StreamBackedInput(newPath, "new.dll")],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "DiffSample",
                    }));

        ImplementationDiffMember member = Assert.Single(
            result.Members,
            member => member.Subject.Display.Contains(
                "ConstantValue",
                StringComparison.Ordinal));
        Assert.Contains(
            member.Changes,
            change => change.Mechanism
                == ResearchChangeMechanism.CSharp);
        Assert.Contains(
            member.Changes,
            change => change.Mechanism
                == ResearchChangeMechanism.IlBody);
        Assert.False(Assert.Single(
            result.Research.RetainedComparisons
                .Get<CSharpCanonicalLine>(
                    CSharpFindings.LineDescriptor),
            comparison => comparison.Subject.MemberName
                == "ConstantValue").IsExact);
        Assert.False(Assert.Single(
            result.Research.RetainedComparisons
                .Get<CanonicalIlOperation>(
                    IlFindings.OperationDescriptor),
            comparison => comparison.Subject.MemberName
                == "ConstantValue").IsExact);
    }

    [Fact]
    public void Execute_RejectsBodyIndexFromDifferentAssemblyImage()
    {
        string oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        string newPath = FixtureCatalog.DiffPair.NewAssemblyPath();
        ImplementationAssemblyInput oldContent =
            StreamBackedInput(oldPath, "old.dll");

        var error = Assert.Throws<ArgumentException>(() =>
            ImplementationComparisonQuery.Execute(
                new ImplementationComparisonInput(
                    [
                        oldContent with
                        {
                            CallGraph = LibraryBodyIndex.Open(newPath).CallGraphAnalysis,
                        },
                    ],
                    [StreamBackedInput(newPath, "new.dll")])));

        Assert.Contains(
            "does not match assembly content",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Definition_IsUnbounded()
        => Assert.Equal(
            InspectionCost.Unbounded,
            ImplementationComparisonQuery.Definition.Cost);

    [Fact]
    public void DocumentQuery_DetachesExactEndpointsAndStructuredEvidence()
    {
        string oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        string newPath = FixtureCatalog.DiffPair.NewAssemblyPath();
        ImplementationAssemblyInput oldInput =
            StreamBackedInput(oldPath, "old.dll");
        ImplementationAssemblyInput newInput =
            StreamBackedInput(newPath, "new.dll");

        ImplementationDiffDocument document =
            ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [oldInput],
                    [newInput],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "DiffSample",
                    }));

        Assert.Equal(
            ImplementationDiffDocumentScope.ExactLibraryPair,
            document.Request.Scope);
        Assert.Equal(["DiffSample"], document.Request.TypeFilters);
        Assert.Equal(
            oldInput.Assembly.Identity,
            document.Before.AssemblyIdentity);
        Assert.Equal(
            oldInput.CallGraph.ModuleIdentity.ModuleVersionId,
            document.Before.ModuleVersionId);
        Assert.Equal(
            ImplementationDiffEndpointProvenanceKind.Local,
            document.Before.Provenance.Kind);
        Assert.Equal(
            newInput.Assembly.Identity,
            document.After.AssemblyIdentity);
        Assert.Equal(
            newInput.CallGraph.ModuleIdentity.ModuleVersionId,
            document.After.ModuleVersionId);

        ImplementationDiffDocumentMember member = Assert.Single(
            document.Members,
            member => member.Subject.MemberName == "ConstantValue");
        Assert.Contains(
            member.Evidence,
            evidence => evidence.CSharpRow is not null
                && evidence.CSharpRow.OldValue == "1"
                && evidence.CSharpRow.NewValue == "2");
        Assert.Contains(
            member.Evidence,
            evidence => evidence.IlRows.Count > 0
                && evidence.IlBodyOutcome
                    == IlBodyDiffOutcome.OperandDiff);
        Assert.Contains(
            document.Coverage.Mechanisms,
            coverage => coverage.Mechanism
                    == ImplementationDiffDocumentMechanism.CSharp
                && coverage.EvaluatedSubjectCount > 0
                && coverage.ChangedSubjectCount > 0);
    }

    [Fact]
    public void DocumentQuery_ProjectsPathFreeEndpointProvenance()
    {
        string oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        string newPath = FixtureCatalog.DiffPair.NewAssemblyPath();
        ImplementationAssemblyInput oldInput =
            StreamBackedInput(oldPath, "old.dll");
        ImplementationAssemblyInput newInput =
            StreamBackedInput(newPath, "new.dll");
        oldInput = oldInput with
        {
            Assembly = ResolvedAssemblyReference.Create(
                oldInput.Assembly.Identity,
                oldInput.Assembly.Path,
                oldInput.Assembly.OpenRead,
                AssemblyResolutionProvenance.Local(oldPath)),
        };
        newInput = newInput with
        {
            Assembly = ResolvedAssemblyReference.Create(
                newInput.Assembly.Identity,
                newInput.Assembly.Path,
                newInput.Assembly.OpenRead,
                AssemblyResolutionProvenance.Local(newPath)),
        };

        ImplementationDiffDocument document =
            ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [oldInput],
                    [newInput]));

        Assert.Equal(
            new ImplementationDiffEndpointProvenance(
                ImplementationDiffEndpointProvenanceKind.Local),
            document.Before.Provenance);
        Assert.Equal(
            new ImplementationDiffEndpointProvenance(
                ImplementationDiffEndpointProvenanceKind.Local),
            document.After.Provenance);
        string json = JsonSerializer.Serialize(document);
        Assert.DoesNotContain(oldPath, json, StringComparison.Ordinal);
        Assert.DoesNotContain(newPath, json, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentQuery_PreservesOnlyOwningIlHunkEvidence()
    {
        string oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        string newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        ImplementationDiffDocument document =
            ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [StreamBackedInput(oldPath, "old.dll")],
                    [StreamBackedInput(newPath, "new.dll")],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "DiffSample",
                    }));

        ImplementationDiffDocumentMember member = Assert.Single(
            document.Members,
            member => member.Subject.MemberName == "MultipleHunks");
        ImplementationDiffEvidence[] hunkEvidence = member.Evidence
            .Where(evidence =>
                evidence.Mechanism == ResearchChangeMechanism.IlBody
                && evidence.DescriptorId == "il.hunk.changed")
            .OrderBy(evidence => evidence.IlRows[0].HunkId)
            .ToArray();

        Assert.Equal(2, hunkEvidence.Length);
        Assert.Equal([0, 1], hunkEvidence
            .Select(evidence => Assert.Single(
                evidence.IlRows
                    .Select(row => row.HunkId)
                    .Distinct()))
            .ToArray());
        Assert.All(
            hunkEvidence,
            evidence =>
            {
                Assert.Equal(2, evidence.IlRows.Count);
                Assert.Empty(evidence.IlFailureRows);
            });
        Assert.Equal(
            4,
            hunkEvidence
                .SelectMany(evidence => evidence.IlRows)
                .Distinct()
                .Count());
    }

    [Fact]
    public void DocumentQuery_PreservesUnavailableIlBodyEvidenceAndCoverage()
    {
        string oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        string newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        ImplementationDiffDocument document =
            ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [StreamBackedInput(oldPath, "old.dll")],
                    [StreamBackedInput(newPath, "new.dll")],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "BodyStateSample",
                    }));

        ImplementationDiffDocumentMember member = Assert.Single(
            document.Members,
            member => member.Subject.MemberName == "BodyState");
        ImplementationDiffEvidence evidence = Assert.Single(
            member.Evidence,
            evidence =>
                evidence.Mechanism == ResearchChangeMechanism.IlBody);
        IlDiffFailureRow failure = Assert.Single(evidence.IlFailureRows);
        Assert.Equal(IlDiffFailureKind.OldBodyMissing, failure.Kind);
        Assert.Equal(IlBodyDiffOutcome.Unavailable, evidence.IlBodyOutcome);
        Assert.Empty(evidence.IlRows);

        ImplementationDiffMechanismCoverage coverage = Assert.Single(
            document.Coverage.Mechanisms,
            coverage => coverage.Mechanism
                == ImplementationDiffDocumentMechanism.IlBody);
        Assert.True(coverage.IsAvailable);
        Assert.Equal(0, coverage.ChangedSubjectCount);
        Assert.Equal(1, coverage.UnavailableSubjectCount);
        Assert.Equal(0, coverage.FailedSubjectCount);
        Assert.False(document.Coverage.IsComplete);
    }

    [Theory]
    [InlineData(
        false,
        PairKind.Removed,
        FindingInspectionState.Complete,
        FindingInspectionState.SubjectAbsent)]
    [InlineData(
        true,
        PairKind.Added,
        FindingInspectionState.SubjectAbsent,
        FindingInspectionState.Complete)]
    public void DocumentQuery_PreservesOneSidedIlFindingEvidence(
        bool reverse,
        PairKind expectedKind,
        FindingInspectionState expectedOldState,
        FindingInspectionState expectedNewState)
    {
        string oldPath = reverse
            ? FixtureCatalog.DiffPair.NewAssemblyPath()
            : FixtureCatalog.DiffPair.OldAssemblyPath();
        string newPath = reverse
            ? FixtureCatalog.DiffPair.OldAssemblyPath()
            : FixtureCatalog.DiffPair.NewAssemblyPath();

        ImplementationDiffDocument document =
            ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [StreamBackedInput(oldPath, "old.dll")],
                    [StreamBackedInput(newPath, "new.dll")],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "MethodRemovalSample",
                    }));

        ImplementationDiffDocumentMember[] members = [
            .. document.Members.Where(member =>
                member.Subject.MemberName == "Removed"),
        ];
        Assert.Equal(2, members.Length);
        Assert.All(members, member =>
        {
            ImplementationDiffIlFindingComparison comparison =
                Assert.IsType<ImplementationDiffIlFindingComparison>(
                    member.IlFindingComparison);
            Assert.Equal(
                expectedOldState,
                comparison.InspectionTransition.Old);
            Assert.Equal(
                expectedNewState,
                comparison.InspectionTransition.New);
            Assert.NotEmpty(comparison.Operations);
            Assert.All(comparison.Operations, operation =>
            {
                Assert.Equal(expectedKind, operation.Kind);
                if (reverse)
                {
                    Assert.Null(operation.Old);
                    Assert.NotNull(operation.New);
                }
                else
                {
                    Assert.NotNull(operation.Old);
                    Assert.Null(operation.New);
                }
            });
        });

        ImplementationDiffMechanismCoverage ilCoverage = Assert.Single(
            document.Coverage.Mechanisms,
            coverage => coverage.Mechanism
                == ImplementationDiffDocumentMechanism.IlBody);
        Assert.Equal(2, ilCoverage.ChangedSubjectCount);
        Assert.Equal(0, ilCoverage.UnavailableSubjectCount);
    }

    [Fact]
    public void ComplexityCoverage_CountsDistinctSubjectsAcrossPhysicalEvidence()
    {
        var subject = new ResearchSubjectKey(
            ResearchSubjectKind.Member,
            "M:DiffFixtureSample.DiffSample.MultipleHunks",
            "DiffFixtureSample.DiffSample.MultipleHunks(int)",
            "DiffFixtureSample.DiffSample",
            "MultipleHunks");
        ImplementationComplexityChange Change(
            ImplementationComplexityChangeKind kind,
            bool oldIsComplete = true,
            bool newIsComplete = true)
            => new(
                subject,
                kind,
                OldValue: 1,
                NewValue: 2,
                Delta: 1,
                OldIsComplete: oldIsComplete,
                NewIsComplete: newIsComplete);

        ImplementationDiffMechanismCoverage coverage =
            ImplementationDiff.CreateComplexityCoverage(
                new ImplementationComplexityDiff(
                    true,
                    null,
                    [
                        Change(ImplementationComplexityChangeKind.Unchanged),
                        Change(ImplementationComplexityChangeKind.Unchanged),
                        Change(ImplementationComplexityChangeKind.Changed),
                        Change(ImplementationComplexityChangeKind.Changed),
                        Change(
                            ImplementationComplexityChangeKind.Incomplete,
                            oldIsComplete: false),
                        Change(
                            ImplementationComplexityChangeKind.Incomplete,
                            newIsComplete: false),
                    ]));

        Assert.Equal(1, coverage.EvaluatedSubjectCount);
        Assert.Equal(1, coverage.ExactSubjectCount);
        Assert.Equal(1, coverage.ChangedSubjectCount);
        Assert.Equal(0, coverage.UnavailableSubjectCount);
        Assert.Equal(1, coverage.IncompleteSubjectCount);
        Assert.Equal(0, coverage.FailedSubjectCount);
        Assert.False(new ImplementationDiffCoverage([coverage]).IsComplete);
    }

    [Fact]
    public void ComplexityCoverage_DoesNotTreatExpectedEndpointAbsenceAsIncomplete()
    {
        var removed = new ImplementationComplexityChange(
            new ResearchSubjectKey(
                ResearchSubjectKind.Member,
                "M:DiffFixtureSample.MethodRemovalSample.Removed",
                "DiffFixtureSample.MethodRemovalSample.Removed()",
                "DiffFixtureSample.MethodRemovalSample",
                "Removed"),
            ImplementationComplexityChangeKind.Removed,
            OldValue: 1,
            NewValue: null,
            Delta: null,
            OldIsComplete: true,
            NewIsComplete: false);

        ImplementationDiffMechanismCoverage coverage =
            ImplementationDiff.CreateComplexityCoverage(
                new ImplementationComplexityDiff(true, null, [removed]));

        Assert.Equal(1, coverage.ChangedSubjectCount);
        Assert.Equal(0, coverage.IncompleteSubjectCount);
        Assert.True(new ImplementationDiffCoverage([coverage]).IsComplete);
    }

    [Theory]
    [InlineData(ImplementationComplexityChangeKind.Added)]
    [InlineData(ImplementationComplexityChangeKind.Removed)]
    public void ComplexityCoverage_RetainsIncompletePresentEndpoint(
        ImplementationComplexityChangeKind kind)
    {
        bool isAdded = kind == ImplementationComplexityChangeKind.Added;
        var change = new ImplementationComplexityChange(
            new ResearchSubjectKey(
                ResearchSubjectKind.Member,
                "M:DiffFixtureSample.MethodRemovalSample.Removed",
                "DiffFixtureSample.MethodRemovalSample.Removed()",
                "DiffFixtureSample.MethodRemovalSample",
                "Removed"),
            kind,
            OldValue: isAdded ? null : 1,
            NewValue: isAdded ? 1 : null,
            Delta: null,
            OldIsComplete: false,
            NewIsComplete: false);

        ImplementationDiffMechanismCoverage coverage =
            ImplementationDiff.CreateComplexityCoverage(
                new ImplementationComplexityDiff(true, null, [change]));

        Assert.Equal(1, coverage.ChangedSubjectCount);
        Assert.Equal(1, coverage.IncompleteSubjectCount);
        Assert.False(new ImplementationDiffCoverage([coverage]).IsComplete);
    }

    [Fact]
    public void DocumentQuery_DistinguishesExactEmptyFromUnavailableComplexity()
    {
        string path = FixtureCatalog.DiffPair.OldAssemblyPath();
        ImplementationAssemblyInput input =
            StreamBackedInput(path, "same.dll");

        ImplementationDiffDocument document =
            ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [input],
                    [input],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "DiffSample",
                    }));

        Assert.Empty(document.Members);
        ImplementationDiffMechanismCoverage csharp = Assert.Single(
            document.Coverage.Mechanisms,
            coverage => coverage.Mechanism
                == ImplementationDiffDocumentMechanism.CSharp);
        Assert.True(csharp.ExactSubjectCount > 0);
        Assert.Equal(0, csharp.ChangedSubjectCount);
        Assert.Equal(0, csharp.UnavailableSubjectCount);
        ImplementationDiffMechanismCoverage complexity = Assert.Single(
            document.Coverage.Mechanisms,
            coverage => coverage.Mechanism
                == ImplementationDiffDocumentMechanism.Complexity);
        Assert.False(complexity.IsAvailable);
        Assert.False(document.Complexity.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(
            document.Complexity.UnavailableReason));
        Assert.False(document.Coverage.IsComplete);
    }

    [Fact]
    public void DocumentQuery_RejectsAmbiguousAssemblyPopulations()
    {
        string oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        string newPath = FixtureCatalog.DiffPair.NewAssemblyPath();
        ImplementationAssemblyInput oldInput =
            StreamBackedInput(oldPath, "old.dll");

        var error = Assert.Throws<ArgumentException>(() =>
            ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [oldInput, oldInput],
                    [StreamBackedInput(newPath, "new.dll")])));

        Assert.Contains(
            "exactly one assembly",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentQuery_DistinguishesPairedReturnTypeOnlyOverloads()
    {
        ImplementationDiffDocument document = CompareReturnTypeOverloads(
            methodName: "Changed",
            oldIntValue: 1,
            oldStringValue: "old",
            newIntValue: 2,
            newStringValue: "new");

        ImplementationDiffDocumentMember[] members =
        [
            .. document.Members.Where(member =>
                member.Subject.MemberName == "Changed"),
        ];
        Assert.Equal(2, members.Length);
        Assert.Equal(
            2,
            members.Select(member => member.Subject.Id)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            members,
            member =>
            {
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.CSharp);
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.IlBody);
            });
        Assert.Equal(
            2,
            Assert.Single(
                document.Coverage.Mechanisms,
                coverage => coverage.Mechanism
                    == ImplementationDiffDocumentMechanism.CSharp)
                .ChangedSubjectCount);
        Assert.Equal(
            2,
            Assert.Single(
                document.Coverage.Mechanisms,
                coverage => coverage.Mechanism
                    == ImplementationDiffDocumentMechanism.IlBody)
                .ChangedSubjectCount);
    }

    [Fact]
    public void DocumentQuery_DistinguishesOneSidedReturnTypeOnlyOverloads()
    {
        ImplementationDiffDocument document = CompareReturnTypeOverloads(
            methodName: "Removed",
            oldIntValue: 1,
            oldStringValue: "old",
            newIntValue: 0,
            newStringValue: "",
            newIncludesInt: false,
            newIncludesString: false);

        ImplementationDiffDocumentMember[] members =
        [
            .. document.Members.Where(member =>
                member.Subject.MemberName == "Removed"),
        ];
        Assert.Equal(2, members.Length);
        Assert.Equal(
            2,
            members.Select(member => member.Subject.Id)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            members,
            member => Assert.NotNull(member.IlFindingComparison));
        Assert.Equal(
            2,
            Assert.Single(
                document.Coverage.Mechanisms,
                coverage => coverage.Mechanism
                    == ImplementationDiffDocumentMechanism.IlBody)
                .ChangedSubjectCount);
    }

    [Fact]
    public void DocumentQuery_UsesCrossEndpointReturnTypeCollisionPopulation()
    {
        ImplementationDiffDocument document = CompareReturnTypeOverloads(
            methodName: "Mixed",
            oldIntValue: 1,
            oldStringValue: "removed",
            newIntValue: 2,
            newStringValue: "",
            newIncludesString: false);

        ImplementationDiffDocumentMember[] members =
        [
            .. document.Members.Where(member =>
                member.Subject.MemberName == "Mixed"),
        ];
        Assert.Equal(2, members.Length);
        Assert.Equal(
            2,
            members.Select(member => member.Subject.Id)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Single(
            members,
            member => member.IlFindingComparison is not null);
        ImplementationDiffDocumentMember paired = Assert.Single(
            members,
            member => member.IlFindingComparison is null);
        Assert.Contains(
            paired.Evidence,
            evidence => evidence.Mechanism
                == ResearchChangeMechanism.CSharp);
        Assert.Contains(
            paired.Evidence,
            evidence => evidence.Mechanism
                == ResearchChangeMechanism.IlBody);
    }

    [Fact]
    public void DocumentQuery_TargetsOneReturnTypeOnlyOverload()
    {
        ImplementationDiffDocument unfiltered = CompareReturnTypeOverloads(
            methodName: "Selected",
            oldIntValue: 1,
            oldStringValue: "old",
            newIntValue: 2,
            newStringValue: "new");
        string selectedId = unfiltered.Members
            .Where(member => member.Subject.MemberName == "Selected")
            .Select(member => member.Subject.Id)
            .Order(StringComparer.Ordinal)
            .First();
        int returnSeparator = selectedId.LastIndexOf('~');
        Assert.True(returnSeparator > 0);
        string apiId = selectedId[..returnSeparator];

        ImplementationDiffDocument filtered = CompareReturnTypeOverloads(
            methodName: "Selected",
            oldIntValue: 1,
            oldStringValue: "old",
            newIntValue: 2,
            newStringValue: "new",
            memberTargetIdentities: new HashSet<string>(
                [apiId, selectedId],
                StringComparer.Ordinal));

        ImplementationDiffDocumentMember member = Assert.Single(
            filtered.Members,
            member => member.Subject.MemberName == "Selected");
        Assert.Equal(selectedId, member.Subject.Id);
    }

    [Fact]
    public void DocumentQuery_CorrelatesGenericReturnTypeCollision()
    {
        ImplementationDiffDocument unfiltered =
            CompareGenericReturnTypeOverloads();
        ImplementationDiffDocumentMember[] members =
        [
            .. unfiltered.Members.Where(member =>
                member.Subject.MemberName == "Changed"),
        ];

        Assert.Equal(2, members.Length);
        Assert.All(
            members,
            member =>
            {
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.CSharp);
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.IlBody);
            });
        ImplementationDiffDocumentMember genericMember = Assert.Single(
            members,
            member => member.Evidence.Any(evidence =>
                evidence.CSharpRow?.BodyAnchor?.CanonicalSignature
                    .EndsWith("~!!0", StringComparison.Ordinal)
                    == true));

        string selectedId = genericMember.Subject.Id;
        int returnSeparator = selectedId.LastIndexOf('~');
        Assert.True(returnSeparator > 0);
        ImplementationDiffDocument filtered =
            CompareGenericReturnTypeOverloads(
                new HashSet<string>(
                    [selectedId[..returnSeparator], selectedId],
                    StringComparer.Ordinal));

        ImplementationDiffDocumentMember selected = Assert.Single(
            filtered.Members,
            member => member.Subject.MemberName == "Changed");
        Assert.Equal(selectedId, selected.Subject.Id);
        Assert.Contains(
            selected.Evidence,
            evidence => evidence.Mechanism
                == ResearchChangeMechanism.CSharp);
        Assert.Contains(
            selected.Evidence,
            evidence => evidence.Mechanism
                == ResearchChangeMechanism.IlBody);
    }

    [Fact]
    public void DocumentQuery_CorrelatesConstructedGenericReturnTypeCollision()
    {
        ImplementationDiffDocument unfiltered =
            CompareConstructedGenericReturnTypeOverloads();
        ImplementationDiffDocumentMember[] members =
        [
            .. unfiltered.Members.Where(member =>
                member.Subject.MemberName == "Changed"),
        ];

        Assert.Equal(2, members.Length);
        Assert.All(
            members,
            member =>
            {
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.CSharp);
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.IlBody);
            });

        string selectedId = members
            .Select(member => member.Subject.Id)
            .Order(StringComparer.Ordinal)
            .First();
        int returnSeparator = selectedId.LastIndexOf('~');
        Assert.True(returnSeparator > 0);
        ImplementationDiffDocument filtered =
            CompareConstructedGenericReturnTypeOverloads(
                new HashSet<string>(
                    [selectedId[..returnSeparator], selectedId],
                    StringComparer.Ordinal));

        ImplementationDiffDocumentMember selected = Assert.Single(
            filtered.Members,
            member => member.Subject.MemberName == "Changed");
        Assert.Equal(selectedId, selected.Subject.Id);
    }

    [Fact]
    public void DocumentQuery_CorrelatesNestedReturnTypeCollision()
    {
        ImplementationDiffDocument unfiltered =
            CompareNestedReturnTypeOverloads();
        ImplementationDiffDocumentMember[] members =
        [
            .. unfiltered.Members.Where(member =>
                member.Subject.MemberName == "Changed"),
        ];

        Assert.Equal(2, members.Length);
        Assert.All(
            members,
            member =>
            {
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.CSharp);
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.IlBody);
            });
        ImplementationDiffDocumentMember leftMember = Assert.Single(
            members,
            member => member.Evidence.Any(evidence =>
                evidence.CSharpRow?.BodyAnchor?.CanonicalSignature
                    .EndsWith(
                        "~NestedReturnTypes+Left",
                        StringComparison.Ordinal)
                    == true));

        string selectedId = leftMember.Subject.Id;
        int returnSeparator = selectedId.LastIndexOf('~');
        Assert.True(returnSeparator > 0);
        ImplementationDiffDocument filtered =
            CompareNestedReturnTypeOverloads(
                new HashSet<string>(
                    [selectedId[..returnSeparator], selectedId],
                    StringComparer.Ordinal));

        ImplementationDiffDocumentMember selected = Assert.Single(
            filtered.Members,
            member => member.Subject.MemberName == "Changed");
        Assert.Equal(selectedId, selected.Subject.Id);
        Assert.Contains(
            selected.Evidence,
            evidence => evidence.Mechanism
                == ResearchChangeMechanism.CSharp);
        Assert.Contains(
            selected.Evidence,
            evidence => evidence.Mechanism
                == ResearchChangeMechanism.IlBody);
    }

    [Fact]
    public void DocumentQuery_CorrelatesFunctionPointerReturnTypeCollision()
    {
        ImplementationDiffDocument unfiltered =
            CompareFunctionPointerReturnTypeOverloads();
        ImplementationDiffDocumentMember[] members =
        [
            .. unfiltered.Members.Where(member =>
                member.Subject.MemberName == "Changed"),
        ];

        Assert.Equal(2, members.Length);
        Assert.All(
            members,
            member =>
            {
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.CSharp);
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.IlBody);
            });
        Assert.Contains(
            members,
            member => member.Evidence.Any(evidence =>
                evidence.CSharpRow?.BodyAnchor?.CanonicalSignature
                    .EndsWith(
                        "~delegate*<System.Int32>",
                        StringComparison.Ordinal)
                    == true));
        Assert.Contains(
            members,
            member => member.Evidence.Any(evidence =>
                evidence.CSharpRow?.BodyAnchor?.CanonicalSignature
                    .EndsWith(
                        "~delegate*<System.String>",
                        StringComparison.Ordinal)
                    == true));

        string selectedId = members
            .Select(member => member.Subject.Id)
            .Order(StringComparer.Ordinal)
            .First();
        int returnSeparator = selectedId.LastIndexOf('~');
        Assert.True(returnSeparator > 0);
        ImplementationDiffDocument filtered =
            CompareFunctionPointerReturnTypeOverloads(
                new HashSet<string>(
                    [selectedId[..returnSeparator], selectedId],
                    StringComparer.Ordinal));

        ImplementationDiffDocumentMember selected = Assert.Single(
            filtered.Members,
            member => member.Subject.MemberName == "Changed");
        Assert.Equal(selectedId, selected.Subject.Id);
    }

    [Fact]
    public void
        DocumentQuery_CorrelatesFunctionPointerConventionModifierCollision()
    {
        ImplementationDiffDocument unfiltered =
            CompareFunctionPointerConventionReturnTypeOverloads();
        ImplementationDiffDocumentMember[] members =
        [
            .. unfiltered.Members.Where(member =>
                member.Subject.MemberName == "Changed"),
        ];

        Assert.Equal(2, members.Length);
        Assert.All(
            members,
            member =>
            {
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.CSharp);
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.IlBody);
            });
        Assert.False(unfiltered.Complexity.IsAvailable);
        Assert.Empty(unfiltered.Complexity.Changes);
        Assert.False(string.IsNullOrWhiteSpace(
            unfiltered.Complexity.UnavailableReason));
        Assert.Contains(
            members,
            member => member.Evidence.Any(evidence =>
                evidence.CSharpRow?.BodyAnchor?.CanonicalSignature
                    .EndsWith(
                        "~delegate* unmanaged[Cdecl]<System.Int32>",
                        StringComparison.Ordinal)
                    == true));
        Assert.Contains(
            members,
            member => member.Evidence.Any(evidence =>
                evidence.CSharpRow?.BodyAnchor?.CanonicalSignature
                    .EndsWith(
                        "~delegate* unmanaged[SuppressGCTransition, Cdecl]"
                            + "<System.Int32>",
                        StringComparison.Ordinal)
                    == true));

        string selectedId = members
            .Select(member => member.Subject.Id)
            .Order(StringComparer.Ordinal)
            .First();
        int returnSeparator = selectedId.LastIndexOf('~');
        Assert.True(returnSeparator > 0);
        ImplementationDiffDocument filtered =
            CompareFunctionPointerConventionReturnTypeOverloads(
                new HashSet<string>(
                    [selectedId[..returnSeparator], selectedId],
                    StringComparer.Ordinal));

        ImplementationDiffDocumentMember selected = Assert.Single(
            filtered.Members,
            member => member.Subject.MemberName == "Changed");
        Assert.Equal(selectedId, selected.Subject.Id);
    }

    [Theory]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .SignatureHeader,
        "{flags=0x20}")]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .UnsupportedModifier,
        "modopt(Probe.Marker)")]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .RequiredModifier,
        "modreq(Probe.Marker)")]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .DuplicateConventionModifier,
        "modopt(System.Runtime.CompilerServices.CallConvCdecl)")]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .CoreLibraryLookalikeModifier,
        "modopt(System.Runtime.CompilerServices.CallConvCdecl)")]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .CoreLibrarySuppressGcTransitionLookalikeModifier,
        "modopt(System.Runtime.CompilerServices"
            + ".CallConvSuppressGCTransition)")]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .MixedSuppressGcTransitionModifier,
        "modopt(Probe.Marker)")]
    public void
        DocumentQuery_CorrelatesFunctionPointerStructuralReturnCollisions(
            FunctionPointerConventionReturnOverloadFixture.IdentityCase
                identityCase,
            string distinguishingIdentity)
    {
        ImplementationDiffDocument document =
            CompareFunctionPointerConventionReturnTypeOverloads(
                identityCase: identityCase);
        ImplementationDiffDocumentMember[] members =
        [
            .. document.Members.Where(member =>
                member.Subject.MemberName == "Changed"),
        ];

        Assert.True(
            members.Length == 2,
            string.Join(
                Environment.NewLine,
                members.Select(member =>
                    $"{member.Subject.Id}: "
                    + string.Join(
                        ", ",
                        member.Evidence.Select(evidence =>
                            evidence.CSharpRow?.BodyAnchor
                                ?.CanonicalSignature
                            ?? evidence.Mechanism.ToString())))));
        Assert.Equal(
            2,
            members.Select(member => member.Subject.Id)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            members,
            member =>
            {
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.CSharp);
                Assert.Contains(
                    member.Evidence,
                    evidence => evidence.Mechanism
                        == ResearchChangeMechanism.IlBody);
            });
        Assert.Contains(
            members,
            member => member.Evidence.Any(evidence =>
                evidence.CSharpRow?.BodyAnchor?.CanonicalSignature
                    .Contains(
                        distinguishingIdentity,
                        StringComparison.Ordinal)
                    == true));

        string selectedId = members
            .Select(member => member.Subject.Id)
            .Order(StringComparer.Ordinal)
            .First();
        int returnSeparator = selectedId.LastIndexOf('~');
        Assert.True(returnSeparator > 0);
        ImplementationDiffDocument filtered =
            CompareFunctionPointerConventionReturnTypeOverloads(
                new HashSet<string>(
                    [selectedId[..returnSeparator], selectedId],
                    StringComparer.Ordinal),
                identityCase);

        ImplementationDiffDocumentMember selected = Assert.Single(
            filtered.Members,
            member => member.Subject.MemberName == "Changed");
        Assert.Equal(selectedId, selected.Subject.Id);
        Assert.Contains(
            selected.Evidence,
            evidence => evidence.Mechanism
                == ResearchChangeMechanism.CSharp);
        Assert.Contains(
            selected.Evidence,
            evidence => evidence.Mechanism
                == ResearchChangeMechanism.IlBody);
    }

    static ImplementationDiffDocument
        CompareConstructedGenericReturnTypeOverloads(
            IReadOnlySet<string>? memberTargetIdentities = null)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-constructed-return-overloads-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string oldPath = Path.Combine(directory, "before.dll");
            string newPath = Path.Combine(directory, "after.dll");
            EmitConstructedGenericReturnTypeOverloads(
                oldPath,
                constructReturnValue: false);
            EmitConstructedGenericReturnTypeOverloads(
                newPath,
                constructReturnValue: true);
            return ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [StreamBackedInput(oldPath, "before.dll")],
                    [StreamBackedInput(newPath, "after.dll")],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "ConstructedGenericReturnSample",
                    },
                    MemberTargetIdentities: memberTargetIdentities));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static void EmitConstructedGenericReturnTypeOverloads(
        string path,
        bool constructReturnValue)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("ConstructedGenericReturnOverloadFixture"),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(
            "ConstructedGenericReturnOverloadFixture");
        TypeBuilder host = module.DefineType(
            "ConstructedGenericReturnSample",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        EmitConstructedGenericReturnMethod(
            host,
            typeof(List<int>),
            constructReturnValue);
        EmitConstructedGenericReturnMethod(
            host,
            typeof(List<string>),
            constructReturnValue);

        host.CreateType();
        assembly.Save(path);
    }

    static void EmitConstructedGenericReturnMethod(
        TypeBuilder host,
        Type returnType,
        bool constructReturnValue)
    {
        MethodBuilder method = host.DefineMethod(
            "Changed",
            MethodAttributes.Public | MethodAttributes.Static,
            returnType,
            Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        if (constructReturnValue)
        {
            ConstructorInfo constructor = Assert.Single(
                returnType.GetConstructors(),
                candidate => candidate.GetParameters().Length == 0);
            il.Emit(OpCodes.Newobj, constructor);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Ret);
    }

    static ImplementationDiffDocument
        CompareFunctionPointerReturnTypeOverloads(
            IReadOnlySet<string>? memberTargetIdentities = null)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-function-pointer-return-overloads-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string oldPath = Path.Combine(directory, "before.dll");
            string newPath = Path.Combine(directory, "after.dll");
            EmitFunctionPointerReturnTypeOverloads(
                oldPath,
                useSecondTarget: false);
            EmitFunctionPointerReturnTypeOverloads(
                newPath,
                useSecondTarget: true);
            return ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [StreamBackedInput(oldPath, "before.dll")],
                    [StreamBackedInput(newPath, "after.dll")],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "FunctionPointerReturnSample",
                    },
                    MemberTargetIdentities: memberTargetIdentities));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static unsafe void EmitFunctionPointerReturnTypeOverloads(
        string path,
        bool useSecondTarget)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("FunctionPointerReturnOverloadFixture"),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(
            "FunctionPointerReturnOverloadFixture");
        TypeBuilder host = module.DefineType(
            "FunctionPointerReturnSample",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);

        MethodBuilder firstInt = EmitFunctionPointerTarget<int>(
            host,
            "FirstInt");
        MethodBuilder secondInt = EmitFunctionPointerTarget<int>(
            host,
            "SecondInt");
        MethodBuilder firstString = EmitFunctionPointerTarget<string>(
            host,
            "FirstString");
        MethodBuilder secondString = EmitFunctionPointerTarget<string>(
            host,
            "SecondString");
        EmitFunctionPointerReturnMethod(
            host,
            typeof(delegate*<int>),
            useSecondTarget ? secondInt : firstInt);
        EmitFunctionPointerReturnMethod(
            host,
            typeof(delegate*<string>),
            useSecondTarget ? secondString : firstString);

        host.CreateType();
        assembly.Save(path);
    }

    static MethodBuilder EmitFunctionPointerTarget<TReturn>(
        TypeBuilder host,
        string name)
    {
        MethodBuilder method = host.DefineMethod(
            name,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(TReturn),
            Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        if (typeof(TReturn).IsValueType)
        {
            LocalBuilder local = il.DeclareLocal(typeof(TReturn));
            il.Emit(OpCodes.Ldloca_S, local);
            il.Emit(OpCodes.Initobj, typeof(TReturn));
            il.Emit(OpCodes.Ldloc_0);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Ret);
        return method;
    }

    static void EmitFunctionPointerReturnMethod(
        TypeBuilder host,
        Type returnType,
        MethodBuilder target)
    {
        MethodBuilder method = host.DefineMethod(
            "Changed",
            MethodAttributes.Public | MethodAttributes.Static,
            returnType,
            Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldftn, target);
        il.Emit(OpCodes.Ret);
    }

    static ImplementationDiffDocument
        CompareFunctionPointerConventionReturnTypeOverloads(
            IReadOnlySet<string>? memberTargetIdentities = null,
            FunctionPointerConventionReturnOverloadFixture.IdentityCase
                identityCase =
                    FunctionPointerConventionReturnOverloadFixture
                        .IdentityCase.ConventionModifiers)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-function-pointer-convention-overloads-"
                + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string oldPath = Path.Combine(directory, "before.dll");
            string newPath = Path.Combine(directory, "after.dll");
            File.WriteAllBytes(
                oldPath,
                FunctionPointerConventionReturnOverloadFixture.Build(
                    returnOne: false,
                    identityCase: identityCase));
            File.WriteAllBytes(
                newPath,
                FunctionPointerConventionReturnOverloadFixture.Build(
                    returnOne: true,
                    identityCase: identityCase));
            return ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [StreamBackedInput(oldPath, "before.dll")],
                    [StreamBackedInput(newPath, "after.dll")],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        FunctionPointerConventionReturnOverloadFixture
                            .GetTypeName(identityCase),
                    },
                    MemberTargetIdentities: memberTargetIdentities));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static ImplementationDiffDocument CompareNestedReturnTypeOverloads(
        IReadOnlySet<string>? memberTargetIdentities = null)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-nested-return-overloads-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string oldPath = Path.Combine(directory, "before.dll");
            string newPath = Path.Combine(directory, "after.dll");
            EmitNestedReturnTypeOverloads(
                oldPath,
                constructReturnValue: false);
            EmitNestedReturnTypeOverloads(
                newPath,
                constructReturnValue: true);
            return ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [StreamBackedInput(oldPath, "before.dll")],
                    [StreamBackedInput(newPath, "after.dll")],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "NestedReturnSample",
                    },
                    MemberTargetIdentities: memberTargetIdentities));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static void EmitNestedReturnTypeOverloads(
        string path,
        bool constructReturnValue)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("NestedReturnOverloadFixture"),
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule("NestedReturnOverloadFixture");
        TypeBuilder returnTypes = module.DefineType(
            "NestedReturnTypes",
            TypeAttributes.Public | TypeAttributes.Class);
        TypeBuilder left = returnTypes.DefineNestedType(
            "Left",
            TypeAttributes.NestedPublic
                | TypeAttributes.Class
                | TypeAttributes.Sealed);
        ConstructorBuilder leftConstructor =
            left.DefineDefaultConstructor(MethodAttributes.Public);
        TypeBuilder right = returnTypes.DefineNestedType(
            "Right",
            TypeAttributes.NestedPublic
                | TypeAttributes.Class
                | TypeAttributes.Sealed);
        ConstructorBuilder rightConstructor =
            right.DefineDefaultConstructor(MethodAttributes.Public);

        TypeBuilder host = module.DefineType(
            "NestedReturnSample",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        EmitNestedReturnMethod(
            host,
            left,
            leftConstructor,
            constructReturnValue);
        EmitNestedReturnMethod(
            host,
            right,
            rightConstructor,
            constructReturnValue);

        left.CreateType();
        right.CreateType();
        returnTypes.CreateType();
        host.CreateType();
        assembly.Save(path);
    }

    static void EmitNestedReturnMethod(
        TypeBuilder host,
        TypeBuilder returnType,
        ConstructorBuilder constructor,
        bool constructReturnValue)
    {
        MethodBuilder method = host.DefineMethod(
            "Changed",
            MethodAttributes.Public | MethodAttributes.Static,
            returnType,
            Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        if (constructReturnValue)
            il.Emit(OpCodes.Newobj, constructor);
        else
            il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    static ImplementationDiffDocument CompareGenericReturnTypeOverloads(
        IReadOnlySet<string>? memberTargetIdentities = null)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-generic-return-overloads-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string oldPath = Path.Combine(directory, "before.dll");
            string newPath = Path.Combine(directory, "after.dll");
            EmitGenericReturnTypeOverloads(
                oldPath,
                intValue: 1,
                returnArgument: false);
            EmitGenericReturnTypeOverloads(
                newPath,
                intValue: 2,
                returnArgument: true);
            return ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [StreamBackedInput(oldPath, "before.dll")],
                    [StreamBackedInput(newPath, "after.dll")],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "GenericReturnSample",
                    },
                    MemberTargetIdentities: memberTargetIdentities));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static void EmitGenericReturnTypeOverloads(
        string path,
        int intValue,
        bool returnArgument)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("GenericReturnOverloadFixture"),
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule("GenericReturnOverloadFixture");
        TypeBuilder type = module.DefineType(
            "GenericReturnSample",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);

        MethodBuilder genericMethod = type.DefineMethod(
            "Changed",
            MethodAttributes.Public | MethodAttributes.Static);
        GenericTypeParameterBuilder genericReturn =
            Assert.Single(genericMethod.DefineGenericParameters("T"));
        genericMethod.SetReturnType(genericReturn);
        genericMethod.SetParameters(genericReturn);
        ILGenerator genericIl = genericMethod.GetILGenerator();
        if (returnArgument)
        {
            genericIl.Emit(OpCodes.Ldarg_0);
        }
        else
        {
            LocalBuilder local = genericIl.DeclareLocal(genericReturn);
            genericIl.Emit(OpCodes.Ldloca_S, local);
            genericIl.Emit(OpCodes.Initobj, genericReturn);
            genericIl.Emit(OpCodes.Ldloc_0);
        }
        genericIl.Emit(OpCodes.Ret);

        MethodBuilder intMethod = type.DefineMethod(
            "Changed",
            MethodAttributes.Public | MethodAttributes.Static);
        GenericTypeParameterBuilder intParameter =
            Assert.Single(intMethod.DefineGenericParameters("T"));
        intMethod.SetReturnType(typeof(int));
        intMethod.SetParameters(intParameter);
        ILGenerator intIl = intMethod.GetILGenerator();
        intIl.Emit(OpCodes.Ldc_I4, intValue);
        intIl.Emit(OpCodes.Ret);

        type.CreateType();
        assembly.Save(path);
    }

    static ImplementationDiffDocument CompareReturnTypeOverloads(
        string methodName,
        int oldIntValue,
        string oldStringValue,
        int newIntValue,
        string newStringValue,
        bool newIncludesInt = true,
        bool newIncludesString = true,
        IReadOnlySet<string>? memberTargetIdentities = null)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-return-overloads-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string oldPath = Path.Combine(directory, "before.dll");
            string newPath = Path.Combine(directory, "after.dll");
            EmitReturnTypeOverloads(
                oldPath,
                methodName,
                oldIntValue,
                oldStringValue);
            EmitReturnTypeOverloads(
                newPath,
                methodName,
                newIntValue,
                newStringValue,
                newIncludesInt,
                newIncludesString);
            return ImplementationDiffDocumentQuery.Execute(
                new ImplementationComparisonInput(
                    [StreamBackedInput(oldPath, "before.dll")],
                    [StreamBackedInput(newPath, "after.dll")],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "ReturnTypeOverloadSample",
                    },
                    MemberTargetIdentities: memberTargetIdentities));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static void EmitReturnTypeOverloads(
        string path,
        string methodName,
        int intValue,
        string stringValue,
        bool includeInt = true,
        bool includeString = true)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("ReturnTypeOverloadFixture"),
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule("ReturnTypeOverloadFixture");
        TypeBuilder type = module.DefineType(
            "ReturnTypeOverloadSample",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        if (includeInt)
        {
            MethodBuilder intMethod = type.DefineMethod(
                methodName,
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int),
                Type.EmptyTypes);
            ILGenerator intIl = intMethod.GetILGenerator();
            intIl.Emit(OpCodes.Ldc_I4, intValue);
            intIl.Emit(OpCodes.Ret);
        }
        if (includeString)
        {
            MethodBuilder stringMethod = type.DefineMethod(
                methodName,
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(string),
                Type.EmptyTypes);
            ILGenerator stringIl = stringMethod.GetILGenerator();
            stringIl.Emit(OpCodes.Ldstr, stringValue);
            stringIl.Emit(OpCodes.Ret);
        }
        type.CreateType();
        assembly.Save(path);
    }

    static ImplementationAssemblyInput StreamBackedInput(
        string path,
        string displayName)
    {
        byte[] content = File.ReadAllBytes(path);
        ResolvedAssemblyReference pathReference =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "implementation query test identity"));
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}",
            displayName);
        ResolvedAssemblyReference contentReference =
            ResolvedAssemblyReference.Create(
                pathReference.Identity,
                missingPath,
                () => new MemoryStream(content, writable: false),
                AssemblyResolutionProvenance.Local(
                    "implementation query test content"));
        return new ImplementationAssemblyInput(
            contentReference,
            MetadataSource.DefaultAssemblyReferenceResolver(path),
            LibraryBodyIndex.Open(path).CallGraphAnalysis);
    }
}
