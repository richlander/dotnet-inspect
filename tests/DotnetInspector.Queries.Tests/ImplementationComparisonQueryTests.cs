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
        Assert.IsType<AssemblyResolutionProvenance.LocalAsset>(
            document.Before.Provenance);
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
