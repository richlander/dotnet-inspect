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
                            BodyIndex = LibraryBodyIndex.Open(newPath),
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
            oldInput.BodyIndex.ModuleIdentity.ModuleVersionId,
            document.Before.ModuleVersionId);
        Assert.Equal(
            ImplementationDiffEndpointProvenanceKind.Local,
            document.Before.Provenance.Kind);
        Assert.Equal(
            newInput.Assembly.Identity,
            document.After.AssemblyIdentity);
        Assert.Equal(
            newInput.BodyIndex.ModuleIdentity.ModuleVersionId,
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
            LibraryBodyIndex.Open(path));
    }
}
