using System.Collections.Immutable;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;

namespace DotnetInspect.Cli.Tests;

public sealed class TypeHierarchyRelationsInspectionExecutorTests
{
    [Fact]
    public void CountOnlyRequestsNoRows()
    {
        SubjectRelationPopulationRequest request =
            TypeHierarchyRelationsInspectionExecutor.CreatePopulationRequest(
                new TypeOptions { Count = true },
                SubjectRelationForm.Interface);

        Assert.NotNull(request.Count);
        Assert.Null(request.Rows);
    }

    [Fact]
    public void BoundedRowsRequestNoCountAndPushesProducerPrefix()
    {
        SubjectRelationPopulationRequest request =
            TypeHierarchyRelationsInspectionExecutor.CreatePopulationRequest(
                new TypeOptions
                {
                    Rows = RowWindow.Range(3, 7),
                },
                SubjectRelationForm.BaseType);

        Assert.Null(request.Count);
        Assert.Equal(7, request.Rows?.MaximumRows);
    }

    [Fact]
    public void CountWithRowsRequestsBothTerminals()
    {
        SubjectRelationPopulationRequest request =
            TypeHierarchyRelationsInspectionExecutor.CreatePopulationRequest(
                new TypeOptions
                {
                    Count = true,
                    Rows = RowWindow.Head(5),
                },
                SubjectRelationForm.Interface);

        Assert.NotNull(request.Count);
        Assert.Equal(5, request.Rows?.MaximumRows);
    }

    [Fact]
    public void ExistsRequestRequestsOnlyExists()
    {
        SubjectRelationPopulationRequest request =
            TypeHierarchyRelationsInspectionExecutor.CreateExistsRequest(
                SubjectRelationForm.BaseType);

        Assert.Null(request.Count);
        Assert.Null(request.Rows);
        Assert.NotNull(request.Exists);
        Assert.Equal(SubjectRelationForm.BaseType, request.Selection.Form);
    }

    [Fact]
    public void ExistsApplicabilityRemovesOnlyADecidedEmptyCompletePopulation()
    {
        var complete = new TypeHierarchyRelationsExistsProbe(
            implementers: true,
            derivedTypes: true)
        {
            Implementers = new SubjectRelationPopulationExistsOutcome.Decided(true),
            DerivedTypes = new SubjectRelationPopulationExistsOutcome.Decided(false),
        };

        TypeHierarchyRelationsApplicability decided =
            TypeHierarchyRelationsApplicability.Resolve(
                complete,
                out ImmutableArray<string> noReasons);

        Assert.True(decided.Implementers);
        Assert.False(decided.DerivedTypes);
        Assert.Empty(noReasons);

        // An empty answer over a population missing a skipped source is
        // undecided with the skipped source named; a non-empty answer stands.
        var incompletePopulation = new TypeHierarchyRelationsExistsProbe(
            implementers: true,
            derivedTypes: true)
        {
            Implementers = new SubjectRelationPopulationExistsOutcome.Decided(true),
            DerivedTypes = new SubjectRelationPopulationExistsOutcome.Decided(false),
            PopulationComplete = false,
            PopulationDiagnostics = ["aspnetcore: pack unavailable, skipping."],
        };

        TypeHierarchyRelationsApplicability undecided =
            TypeHierarchyRelationsApplicability.Resolve(
                incompletePopulation,
                out ImmutableArray<string> reasons);

        Assert.True(undecided.Implementers);
        Assert.Null(undecided.DerivedTypes);
        string reason = Assert.Single(reasons);
        Assert.Contains(SectionNames.DerivedTypes, reason);
        Assert.Contains("aspnetcore: pack unavailable", reason);

        // Every non-decided outcome is undecided and names its reason.
        var unread = new TypeHierarchyRelationsExistsProbe(
            implementers: true,
            derivedTypes: true)
        {
            Implementers = new SubjectRelationPopulationExistsOutcome.Incomplete(),
            DerivedTypes = new SubjectRelationPopulationExistsOutcome.Unavailable(),
        };

        TypeHierarchyRelationsApplicability unreadApplicability =
            TypeHierarchyRelationsApplicability.Resolve(
                unread,
                out ImmutableArray<string> unreadReasons);

        Assert.Null(unreadApplicability.Implementers);
        Assert.Null(unreadApplicability.DerivedTypes);
        Assert.Equal(2, unreadReasons.Length);
        Assert.Contains(unreadReasons, r => r.Contains(SectionNames.Implementers));
        Assert.Contains(unreadReasons, r => r.Contains(SectionNames.DerivedTypes));

        // A form the probe did not request contributes neither a decision
        // nor a reason.
        var oneForm = new TypeHierarchyRelationsExistsProbe(
            implementers: false,
            derivedTypes: true)
        {
            DerivedTypes = new SubjectRelationPopulationExistsOutcome.Failed(),
        };
        TypeHierarchyRelationsApplicability oneFormApplicability =
            TypeHierarchyRelationsApplicability.Resolve(
                oneForm,
                out ImmutableArray<string> oneReason);
        Assert.Null(oneFormApplicability.Implementers);
        Assert.Null(oneFormApplicability.DerivedTypes);
        Assert.Contains(SectionNames.DerivedTypes, Assert.Single(oneReason));
    }

    [Fact]
    public void DefaultPlatformHierarchyUsesEveryPlatformFramework()
    {
        IReadOnlyList<string> frameworks =
            TypeHierarchyRelationsInspectionExecutor
                .PlatformHierarchyFamilies(new TypeOptions());

        Assert.Equal(
            ["runtime", "aspnetcore", "netstandard"],
            frameworks);
    }

    [Fact]
    public void ExplicitPlatformHierarchyFrameworkNarrowsThePopulation()
    {
        IReadOnlyList<string> frameworks =
            TypeHierarchyRelationsInspectionExecutor
                .PlatformHierarchyFamilies(
                    new TypeOptions
                    {
                        PlatformFramework = "runtime",
                    });

        Assert.Equal(["runtime"], frameworks);
    }

    [Fact]
    public async Task AssemblySetPartialFailureRetainsRowsAndIncompleteEvidence()
    {
        (TypeHierarchyRelationsInspection relations, string resolverDiagnostic) =
            await ExecuteWithAssemblySetPartialFailureAsync(count: false);

        TypeHierarchyRelationSectionInspection implementers =
            Assert.IsType<TypeHierarchyRelationSectionInspection>(
                relations.Implementers);
        Assert.Contains(
            implementers.Candidates,
            candidate => candidate.Type
                == typeof(WorkspaceImplementation).FullName);
        InspectionDiagnostic diagnostic =
            Assert.Single(relations.EffectiveDiagnostics);
        Assert.Equal(resolverDiagnostic, diagnostic.Summary.ToString());
        Assert.Equal(
            InspectionDiagnosticSeverity.Warning,
            diagnostic.Severity);
        Assert.False(relations.IsComplete);
    }

    [Fact]
    public async Task AssemblySetPartialFailureRejectsExactCount()
    {
        (TypeHierarchyRelationsInspection relations, _) =
            await ExecuteWithAssemblySetPartialFailureAsync(count: true);

        Assert.False(
            ApiCommand.TryGetHierarchyCount(
                SectionNames.Implementers,
                relations.Implementers,
                relations.AdditionalEvidenceComplete,
                out int? count,
                out string? failure));
        Assert.Null(count);
        Assert.Equal(
            "The 'Implementers' count is incomplete or unavailable.",
            failure);
    }

    private static async Task<(
        TypeHierarchyRelationsInspection Relations,
        string ResolverDiagnostic)> ExecuteWithAssemblySetPartialFailureAsync(
            bool count)
    {
        string path =
            typeof(IWorkspaceImplementationMarker).Assembly.Location;
        var options = new TypeOptions
        {
            AssemblyPath = path,
            IncludeAll = true,
            IncludeSections =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    SectionNames.Implementers,
                },
            Count = count,
        };
        string typeName =
            typeof(IWorkspaceImplementationMarker).FullName!;
        var source = new ApiSourceResult(
            SearchPath: path,
            RuntimeAssemblyPath: null,
            PackageName: null,
            PackageVersion: null,
            ResolvedPackagePath: null,
            PackageExtractPath: null,
            ApiSource: SourceKind.Library,
            ApiVersion: null,
            PlatformFramework: null,
            SelectedTfm: null,
            ProjectAssetsPath: null,
            TempDir: null,
            TypeName: typeName,
            PackageReplaySourceUrls: null,
            PackageReplayUsesOriginalSources: false,
            Context: new CommandContext(verbose: false));
        string missingProject = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.csproj");
        AssemblySet assemblySet =
            await AssemblySetResolver.CollectAsync(
                source.Context.HttpClient,
                new AssemblySetRequest
                {
                    Assemblies = [path],
                    Projects = [missingProject],
                    CancellationToken =
                        TestContext.Current.CancellationToken,
                });
        Assert.Single(assemblySet.Assemblies);
        string resolverDiagnostic =
            Assert.Single(assemblySet.Diagnostics).Message;
        TypeHierarchyRelationsInspection? relations = null;

        (int? exitCode, string? error) =
            await TypeHierarchyRelationsInspectionExecutor.ExecuteAsync(
                options,
                source,
                execution =>
                {
                    relations = execution.Relations;
                    return Task.FromResult(0);
                },
                TestContext.Current.CancellationToken,
                assemblySet);

        Assert.Equal(0, exitCode);
        Assert.Null(error);
        return (
            Assert.IsType<TypeHierarchyRelationsInspection>(relations),
            resolverDiagnostic);
    }
}
