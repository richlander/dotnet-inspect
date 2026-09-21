using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyPairCallUseQueryTests
{
    static readonly AssemblyPairClusterRootPathLimits
        FullClusterRootPathLimits =
            new(
                new(
                    MaximumTypeDefinitions: 1_000,
                    MaximumMethodDefinitions: 10_000,
                    MaximumRoots: 10_000),
                new(
                    MaximumDepth: 20,
                    MaximumNodes: 10_000,
                    MaximumEdges: 100_000,
                    MaximumPaths: 10_000));

    [Fact]
    public async Task ExecuteReturnsExactCallsAcrossBothPairDirections()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());

        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.Second,
                context.First);

        Assert.True(result.IsComplete);
        Assert.Empty(result.Failures);
        AssemblyPairCallUseOccurrence direct = Assert.Single(
            result.Occurrences,
            occurrence =>
                occurrence.Source.Identity.Name
                    == "ILInspector.Analysis.CallerGraphCaller"
                && occurrence.SourceMethod.Name == "Run"
                && occurrence.Target.Identity.Name
                    == "ILInspector.Analysis.CallerGraphTarget"
                && occurrence.TargetMethod.Name == "Ping"
                && occurrence.Call.Kind == Analysis.CallKind.Call);
        Assert.True(direct.Call.ExactTarget);
        AssemblyPairCallUseOccurrence openVirtual = Assert.Single(
            result.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "CallBodiless"
                && occurrence.TargetMethod.Name == "Invoke"
                && occurrence.Call.Kind
                    == Analysis.CallKind.CallVirtual);
        Assert.False(openVirtual.Call.ExactTarget);
        AssemblyPairCallUseOccurrence constructor = Assert.Single(
            result.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "UseBox"
                && occurrence.TargetMethod.Name == ".ctor"
                && occurrence.Call.Kind
                    == Analysis.CallKind.NewObject);
        Assert.True(constructor.Call.ExactTarget);
        Assert.DoesNotContain(
            result.Occurrences,
            occurrence =>
                occurrence.Call.Kind is
                    Analysis.CallKind.LoadFunction
                    or Analysis.CallKind.LoadVirtualFunction
                    or Analysis.CallKind.CallIndirect);
        Assert.All(
            result.Occurrences,
            occurrence =>
                Assert.Equal(
                    occurrence.SourceMethod.MetadataToken,
                    occurrence.Call.Caller.MetadataToken));
    }

    [Fact]
    public async Task ProjectionRetainsEveryOccurrenceInBothSummaryViews()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);

        AssemblyPairCallUseProjection projection =
            AssemblyPairCallUseProjection.Create(result);
        AssemblyPairDirectUseClusterProjection clusters =
            AssemblyPairDirectUseClusterProjection.Create(result);

        Assert.Same(result, projection.Pair);
        Assert.True(projection.IsComplete);
        Assert.Equal(
            Enumerable.Range(0, result.Occurrences.Length),
            projection.ConsumerUseSites
                .SelectMany(site => site.OccurrenceIndexes)
                .Order());
        Assert.Equal(
            Enumerable.Range(0, result.Occurrences.Length),
            projection.ProviderApiTypes
                .SelectMany(type => type.OccurrenceIndexes)
                .Order());
        Assert.Equal(
            Enumerable.Range(0, result.Occurrences.Length),
            clusters.Clusters
                .SelectMany(cluster => cluster.OccurrenceIndexes)
                .Order());
        Assert.All(
            projection.ConsumerUseSites,
            site =>
            {
                AssemblyPairCallUseOccurrence[] occurrences =
                [.. site.OccurrenceIndexes.Select(
                    index => result.Occurrences[index])];
                Assert.All(
                    occurrences,
                    occurrence =>
                    {
                        Assert.Same(
                            site.Source.Registration,
                            occurrence.Source.Registration);
                        Assert.Equal(
                            site.SourceModuleVersionId,
                            occurrence.SourceModuleVersionId);
                        Assert.Equal(
                            site.SourceMethod.MetadataToken,
                            occurrence.SourceMethod.MetadataToken);
                        Assert.Same(
                            site.Target.Registration,
                            occurrence.Target.Registration);
                    });
                Assert.Equal(
                    occurrences
                        .Select(occurrence =>
                            occurrence.TargetMethod.DeclaringType)
                        .Distinct(),
                    site.TargetTypes);
                Assert.Equal(
                    occurrences
                        .Select(occurrence => occurrence.TargetMethod)
                        .Distinct(),
                    site.TargetMethods);
            });
        Assert.All(
            projection.ProviderApiTypes,
            type =>
            {
                AssemblyPairCallUseOccurrence[] occurrences =
                [.. type.OccurrenceIndexes.Select(
                    index => result.Occurrences[index])];
                Assert.All(
                    occurrences,
                    occurrence =>
                    {
                        Assert.Same(
                            type.Source.Registration,
                            occurrence.Source.Registration);
                        Assert.Same(
                            type.Target.Registration,
                            occurrence.Target.Registration);
                        Assert.Equal(
                            type.TargetModuleVersionId,
                            occurrence.TargetModuleVersionId);
                        Assert.Equal(
                            type.TargetType,
                            occurrence.TargetMethod.DeclaringType);
                    });
                Assert.Equal(
                    occurrences
                        .Select(occurrence => occurrence.SourceMethod)
                        .Distinct(),
                    type.SourceMethods);
                Assert.Equal(
                    occurrences
                        .Select(occurrence => occurrence.TargetMethod)
                        .Distinct(),
                    type.TargetMethods);
            });
        Assert.All(
            clusters.Clusters,
            cluster =>
            {
                AssemblyPairCallUseOccurrence[] occurrences =
                [.. cluster.OccurrenceIndexes.Select(
                    index => result.Occurrences[index])];
                Assert.All(
                    occurrences,
                    occurrence =>
                    {
                        Assert.Same(
                            cluster.Identity.Source.Registration,
                            occurrence.Source.Registration);
                        Assert.Equal(
                            cluster.Identity.SourceModuleVersionId,
                            occurrence.SourceModuleVersionId);
                        Assert.Same(
                            cluster.Identity.Target.Registration,
                            occurrence.Target.Registration);
                        Assert.Equal(
                            cluster.Identity.TargetModuleVersionId,
                            occurrence.TargetModuleVersionId);
                    });
                Assert.Equal(
                    occurrences
                        .Select(occurrence => occurrence.SourceMethod)
                        .Distinct(),
                    cluster.SourceMethods);
                Assert.Equal(
                    occurrences
                        .Select(occurrence =>
                            occurrence.TargetMethod.DeclaringType)
                        .Distinct(),
                    cluster.TargetTypes);
                Assert.Equal(
                    occurrences
                        .Select(occurrence => occurrence.TargetMethod)
                        .Distinct(),
                    cluster.TargetMethods);
                Assert.Equal(
                    cluster.SourceMethods.Min(
                        method => method.MetadataToken),
                    cluster.Identity.AnchorSourceMethodToken);
                Assert.Equal(
                    cluster.TargetMethods.Min(
                        method => method.MetadataToken),
                    cluster.Identity.AnchorTargetMethodToken);
            });

        AssemblyPairCallUseConsumerUseSite repeated =
            Assert.Single(
                projection.ConsumerUseSites,
                site => site.SourceMethod.Name == "RunTwice");
        Assert.Equal(2, repeated.CallSiteCount);
        Assert.Single(repeated.TargetTypes);
        Assert.Single(repeated.TargetMethods);
    }

    [Fact]
    public async Task ProjectionBuildsExactDirectUseConnectedComponents()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairCallUseResult pair =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);
        AssemblyPairDirectUseClusterProjection projection =
            AssemblyPairDirectUseClusterProjection.Create(pair);
        Assert.Equal(
            Enumerable.Range(1, projection.Clusters.Length),
            projection.Clusters.Select(cluster => cluster.Ordinal));

        AssemblyPairDirectUseCluster echo = Assert.Single(
            projection.Clusters,
            cluster =>
                cluster.TargetMethods.Length == 1
                && cluster.TargetMethods[0].Name == "Echo");
        Assert.Equal(
            ["RunTwice", "UseEcho"],
            echo.SourceMethods.Select(method => method.Name));
        Assert.Single(echo.TargetTypes);
        Assert.Equal(3, echo.CallSiteCount);
        Assert.Equal(1, echo.ExtensionMethodCount);
        AssemblyPairDirectUseClusterProjection scoped =
            projection.ScopeToObservedCluster(echo.Ordinal)!;
        Assert.Same(
            pair.Occurrences[echo.OccurrenceIndexes[0]],
            scoped.Pair.Occurrences[0]);
        Assert.Equal(
            Enumerable.Range(0, echo.CallSiteCount),
            Assert.Single(scoped.Clusters).OccurrenceIndexes);

        AssemblyPairDirectUseCluster box = Assert.Single(
            projection.Clusters,
            cluster => cluster.SourceMethods.Any(
                method => method.Name == "UseBox"));
        Assert.Equal(
            ["UseBox", "UseBoxList"],
            box.SourceMethods.Select(method => method.Name));
        Assert.Equal(3, box.TargetMethods.Length);
        Assert.Equal(4, box.CallSiteCount);

        AssemblyPairDirectUseCluster runInt = Assert.Single(
            projection.Clusters,
            cluster => cluster.SourceMethods.Any(
                method => method.Name == "RunInt"));
        AssemblyPairDirectUseCluster runString = Assert.Single(
            projection.Clusters,
            cluster => cluster.SourceMethods.Any(
                method => method.Name == "RunString"));
        Assert.NotEqual(runInt.Identity, runString.Identity);
        Assert.Equal(runInt.TargetTypes, runString.TargetTypes);

        AssemblyPairDirectUseClusterProjection incomplete =
            AssemblyPairDirectUseClusterProjection.Create(
                pair with
                {
                    Diagnostics =
                        new AssemblyPairCallUseDiagnostics(1),
                });
        Assert.False(incomplete.IsComplete);
        Assert.Equal(
            projection.Clusters.Select(ClusterFingerprint),
            incomplete.Clusters.Select(ClusterFingerprint));
    }

    [Fact]
    public async Task ClusterRootPaths_ComposePublicRootsAndPrivateUseSites()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairDirectUseClusterProjection selection =
            SelectCluster(
                context,
                targetMethod: "RootPathUse");

        AssemblyPairClusterRootPathResult result =
            AssemblyPairClusterRootPathQuery.Execute(
                context.Group,
                selection,
                FullClusterRootPathLimits,
                TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.NotNull(result.PublicRoots);
        Analysis.LibraryBodyRootPathResult paths =
            Assert.IsType<Analysis.LibraryBodyRootPathResult>(
                result.Paths);
        Assert.Equal(
            ["Create", "CreateAlternative", "CreateOuter"],
            paths.Witnesses
                .Select(witness => witness.Root.Name)
                .ToArray());
        Assert.Equal(
            [2, 2, 3],
            paths.Witnesses
                .Select(witness => witness.Depth)
                .ToArray());
        Assert.All(
            paths.Witnesses,
            witness =>
            {
                Assert.Equal(
                    "AddChange",
                    witness.Destination.Name);
                Assert.All(
                    witness.Steps,
                    step => Assert.NotEmpty(step.CallSites));
            });
    }

    [Fact]
    public async Task ClusterRootPaths_UseExactPublicAccessorRoots()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairDirectUseClusterProjection selection =
            SelectCluster(
                context,
                targetMethod: "AccessorPathUse");

        AssemblyPairClusterRootPathResult result =
            AssemblyPairClusterRootPathQuery.Execute(
                context.Group,
                selection,
                FullClusterRootPathLimits,
                TestContext.Current.CancellationToken);

        Analysis.LibraryBodyRootPathResult paths =
            Assert.IsType<Analysis.LibraryBodyRootPathResult>(
                result.Paths);
        Assert.Contains(
            paths.Witnesses,
            witness =>
                witness.Root.Name == "get_Value"
                && witness.Depth == 1);
        Assert.Contains(
            paths.Witnesses,
            witness =>
                witness.Root.Name == "AssignValue"
                && witness.Depth == 2);
        Assert.DoesNotContain(
            paths.Witnesses,
            witness => witness.Root.Name == "set_Value");
        Assert.Contains(
            paths.Witnesses
                .Single(witness =>
                    witness.Root.Name == "AssignValue")
                .Steps,
            step => step.Callee.Name == "set_Value");
    }

    [Fact]
    public async Task ClusterRootPaths_PublicDirectUseHasZeroDepth()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairDirectUseClusterProjection selection =
            SelectCluster(
                context,
                targetMethod: "PublicPathUse");

        AssemblyPairClusterRootPathResult result =
            AssemblyPairClusterRootPathQuery.Execute(
                context.Group,
                selection,
                FullClusterRootPathLimits,
                TestContext.Current.CancellationToken);

        Analysis.LibraryBodyRootPathWitness witness =
            Assert.Single(result.Paths!.Witnesses);
        Assert.Equal("Use", witness.Root.Name);
        Assert.Equal(witness.Root, witness.Destination);
        Assert.Equal(0, witness.Depth);
        Assert.Empty(witness.Steps);
    }

    [Fact]
    public async Task ClusterRootPaths_CompleteEmptyAbsenceIsDistinctFromBounds()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairDirectUseClusterProjection selection =
            SelectCluster(
                context,
                targetMethod: "UnreachablePathUse");

        AssemblyPairClusterRootPathResult complete =
            AssemblyPairClusterRootPathQuery.Execute(
                context.Group,
                selection,
                FullClusterRootPathLimits,
                TestContext.Current.CancellationToken);

        Assert.True(complete.IsComplete);
        Assert.Empty(complete.Paths!.Witnesses);

        AssemblyPairClusterRootPathResult depthBounded =
            AssemblyPairClusterRootPathQuery.Execute(
                context.Group,
                SelectCluster(
                    context,
                    targetMethod: "RootPathUse"),
                FullClusterRootPathLimits with
                {
                    Paths =
                        FullClusterRootPathLimits.Paths with
                        {
                            MaximumDepth = 2,
                        },
                },
                TestContext.Current.CancellationToken);

        Assert.False(depthBounded.IsComplete);
        Assert.NotEmpty(depthBounded.Paths!.Witnesses);
        Assert.Contains(
            depthBounded.Paths.Boundaries,
            boundary =>
                boundary
                    is Analysis.LibraryBodyRootPathBoundary
                        .DepthLimit);
    }

    [Fact]
    public async Task ClusterRootPaths_RetainPositiveEvidenceAcrossOwners()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairDirectUseClusterProjection selection =
            SelectCluster(
                context,
                targetMethod: "RootPathUse");
        AssemblyPairClusterRootPathResult full =
            AssemblyPairClusterRootPathQuery.Execute(
                context.Group,
                selection,
                FullClusterRootPathLimits,
                TestContext.Current.CancellationToken);
        int createRootIndex =
            full.PublicRoots!.Roots.IndexOf(
                full.PublicRoots.Roots.Single(root =>
                    root.Token
                    == full.Paths!.Witnesses
                        .Single(witness =>
                            witness.Root.Name == "Create")
                        .Root.MetadataToken));

        AssemblyPairClusterRootPathResult bounded =
            AssemblyPairClusterRootPathQuery.Execute(
                context.Group,
                selection with
                {
                    Pair = selection.Pair with
                    {
                        Diagnostics =
                            new AssemblyPairCallUseDiagnostics(1),
                    },
                },
                FullClusterRootPathLimits with
                {
                    PublicRoots =
                        FullClusterRootPathLimits.PublicRoots with
                        {
                            MaximumRoots = createRootIndex + 1,
                        },
                },
                TestContext.Current.CancellationToken);

        Assert.False(bounded.IsComplete);
        Assert.NotNull(bounded.PublicRoots!.Boundary);
        Assert.NotEmpty(bounded.Paths!.Witnesses);
        Assert.False(bounded.Selection.IsComplete);
    }

    [Fact]
    public async Task ClusterRootPaths_RejectStaleOrForeignSelection()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairDirectUseClusterProjection selection =
            SelectCluster(
                context,
                targetMethod: "RootPathUse");
        AssemblyPairDirectUseCluster cluster =
            Assert.Single(selection.Clusters);

        Assert.Throws<AssemblyPairClusterRootPathRequestException>(
            () => AssemblyPairClusterRootPathQuery.Execute(
                context.Group,
                selection with
                {
                    Clusters =
                    [
                        cluster with
                        {
                            Identity = cluster.Identity with
                            {
                                SourceModuleVersionId =
                                    Guid.NewGuid(),
                            },
                        },
                    ],
                },
                FullClusterRootPathLimits,
                TestContext.Current.CancellationToken));

        await using PairContext other = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        Assert.Throws<AssemblyPairClusterRootPathRequestException>(
            () => AssemblyPairClusterRootPathQuery.Execute(
                other.Group,
                selection,
                FullClusterRootPathLimits,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClusterRootPaths_RejectForeignTargetWithSharedSource()
    {
        string sourcePath =
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
        await using PairContext context = PairContext.Create(
            sourcePath,
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairDirectUseClusterProjection selection =
            SelectCluster(
                context,
                targetMethod: "RootPathUse");
        ResolvedAssemblyReference foreignTarget =
            ResolvedAssemblyReference.CreateFromPath(
                FixtureCatalog.AnalysisCallerGraphTargetV2
                    .AssemblyPath(),
                AssemblyResolutionProvenance.Local(
                    "cluster root-path foreign target test"));
        AssemblyContextGroup foreignGroup =
            context.Workspace.CreateAssemblyContextGroup(
                PairContext.Participants(
                    context.First,
                    foreignTarget,
                    sourcePath));

        Assert.Throws<AssemblyPairClusterRootPathRequestException>(
            () => AssemblyPairClusterRootPathQuery.Execute(
                foreignGroup,
                selection,
                FullClusterRootPathLimits,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClusterRootPathInspection_ProjectsOwnerDiagnostics()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairDirectUseClusterProjection selection =
            SelectCluster(
                context,
                targetMethod: "RootPathUse");

        InspectionEnvelope<AssemblyPairClusterRootPathResult> inspection =
            AssemblyPairClusterRootPathInspection.Execute(
                context.Group,
                selection,
                FullClusterRootPathLimits with
                {
                    Paths =
                        FullClusterRootPathLimits.Paths with
                        {
                            MaximumDepth = 2,
                        },
                },
                TestContext.Current.CancellationToken);

        Assert.False(inspection.Content.IsComplete);
        Assert.Contains(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code
                == "cluster-root-paths.analysis-boundary");
        Assert.IsType<InspectionPortableProjection.NonProjectable>(
            inspection.PortableProjection);
    }

    [Fact(Timeout = 10_000)]
    public async Task ProjectionKeepsRepeatedPhysicalSitesLinear()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairCallUseResult pair =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);
        AssemblyPairCallUseOccurrence occurrence = Assert.Single(
            pair.Occurrences,
            candidate =>
                candidate.SourceMethod.Name == "UseEcho"
                && candidate.TargetMethod.Name == "Echo");
        const int PhysicalSiteCount = 50_000;
        AssemblyPairCallUseResult repeated = pair with
        {
            Occurrences =
            [
                .. Enumerable.Range(0, PhysicalSiteCount)
                    .Select(index => occurrence with
                    {
                        Call = occurrence.Call with
                        {
                            ILOffset = index,
                        },
                    }),
            ],
        };

        AssemblyPairDirectUseClusterProjection projection =
            AssemblyPairDirectUseClusterProjection.Create(repeated);

        AssemblyPairDirectUseCluster cluster =
            Assert.Single(projection.Clusters);
        Assert.Equal(PhysicalSiteCount, cluster.CallSiteCount);
        Assert.Single(cluster.SourceMethods);
        Assert.Single(cluster.TargetMethods);
    }

    [Fact]
    public async Task ProjectionOrdinalsSpanBothDirections()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairCallUseResult pair =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);
        AssemblyPairCallUseOccurrence forward = pair.Occurrences[0];
        AssemblyPairCallUseOccurrence reverse = forward with
        {
            Source = forward.Target,
            SourceModuleVersionId = forward.TargetModuleVersionId,
            SourceMethod = forward.TargetMethod,
            Target = forward.Source,
            TargetModuleVersionId = forward.SourceModuleVersionId,
            TargetMethod = forward.SourceMethod,
        };

        AssemblyPairDirectUseClusterProjection projection =
            AssemblyPairDirectUseClusterProjection.Create(
                pair with { Occurrences = [forward, reverse] });

        Assert.Equal(
            [1, 2],
            projection.Clusters.Select(cluster => cluster.Ordinal));
        Assert.NotEqual(
            projection.Clusters[0].Identity.Source,
            projection.Clusters[1].Identity.Source);
    }

    [Fact]
    public async Task ProjectionOrderIsIndependentOfRequestArgumentOrder()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());

        AssemblyPairCallUseProjection forward =
            AssemblyPairCallUseProjection.Create(
                AssemblyPairCallUseQuery.Execute(
                    context.Group,
                    context.First,
                    context.Second));
        AssemblyPairCallUseProjection reverse =
            AssemblyPairCallUseProjection.Create(
                AssemblyPairCallUseQuery.Execute(
                    context.Group,
                    context.Second,
                    context.First));
        AssemblyPairDirectUseClusterProjection forwardClusters =
            AssemblyPairDirectUseClusterProjection.Create(forward.Pair);
        AssemblyPairDirectUseClusterProjection reverseClusters =
            AssemblyPairDirectUseClusterProjection.Create(reverse.Pair);

        Assert.Equal(
            forward.ConsumerUseSites.Select(ConsumerFingerprint),
            reverse.ConsumerUseSites.Select(ConsumerFingerprint));
        Assert.Equal(
            forward.ProviderApiTypes.Select(ProviderFingerprint),
            reverse.ProviderApiTypes.Select(ProviderFingerprint));
        Assert.Equal(
            forwardClusters.Clusters.Select(ClusterFingerprint),
            reverseClusters.Clusters.Select(ClusterFingerprint));
    }

    [Fact]
    public async Task ExecuteRejectsARegistrationOutsideTheGroup()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        ResolvedAssemblyReference outside =
            ResolvedAssemblyReference.CreateFromPath(
                FixtureCatalog.AnalysisCallerGraphTargetV2
                    .AssemblyPath(),
                AssemblyResolutionProvenance.Local(
                    "pairwise query outside participant"));

        Assert.Throws<AssemblyPairCallUseRequestException>(
            () => AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                outside));
    }

    [Fact]
    public async Task ExecuteDoesNotTurnVersionSkewIntoAnAbsenceClaim()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath());

        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);

        Assert.False(result.IsComplete);
        Assert.True(
            result.Diagnostics.UnresolvedCandidateCallCount > 0);
        Assert.Empty(result.Occurrences);
    }

    [Fact]
    public async Task SameNameParticipantsDoNotTurnLocalCallsIntoPairGaps()
    {
        await using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath());

        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);

        Assert.True(result.IsComplete);
        Assert.Equal(
            0,
            result.Diagnostics.UnresolvedCandidateCallCount);
        Assert.Empty(result.Occurrences);
    }

    [Fact]
    public void PairRelevantAssemblyReferenceIgnoresOnlyVersion()
    {
        var target = new AssemblyReferenceIdentity(
            "Dependency",
            new Version(2, 0, 0, 0),
            "neutral",
            "b03f5f7f11d50a3a");
        AssemblyReferenceIdentity versionSkewed = target with
        {
            Version = new Version(1, 0, 0, 0),
            Culture = null,
        };

        Assert.True(
            AssemblyPairCallUseQuery
                .IsPairRelevantAssemblyReference(
                    versionSkewed,
                    target));
        Assert.False(
            AssemblyPairCallUseQuery
                .IsPairRelevantAssemblyReference(
                    versionSkewed with
                    {
                        PublicKeyToken = null,
                    },
                    target));
        Assert.False(
            AssemblyPairCallUseQuery
                .IsPairRelevantAssemblyReference(
                    versionSkewed with
                    {
                        Culture = "fr-FR",
                    },
                    target));
    }

    [Fact]
    public async Task ExecuteRejectsDistinctRegistrationsForTheSamePhysicalImage()
    {
        string path =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        await using PairContext context = PairContext.Create(path, path);

        AssemblyPairCallUseRequestException exception =
            Assert.Throws<AssemblyPairCallUseRequestException>(
                () => AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second));

        Assert.Contains(
            "distinct physical assembly artifacts",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteCarriesRejectedParticipantBesideAvailableEvidence()
    {
        string callerPath =
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
        string targetPath =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        ResolvedAssemblyReference caller =
            ResolvedAssemblyReference.CreateFromPath(
                callerPath,
                AssemblyResolutionProvenance.Local(
                    "pairwise call-use test"));
        ResolvedAssemblyReference targetIdentity =
            ResolvedAssemblyReference.CreateFromPath(
                targetPath,
                AssemblyResolutionProvenance.Local(
                    "pairwise call-use test"));
        ResolvedAssemblyReference malformed =
            ResolvedAssemblyReference.Create(
                targetIdentity.Identity,
                path: null,
                () => new MemoryStream([0x00, 0x01, 0x02]),
                AssemblyResolutionProvenance.Local(
                    "malformed pairwise call-use test"));
        await using PairContext context =
            PairContext.Create(caller, malformed, callerPath);

        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);

        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Subjects.Length);
        Assert.Single(result.Participants);
        Assert.IsType<AssemblyPairCallUseFailure.Rejected>(
            Assert.Single(result.Failures));
        Assert.Empty(result.Occurrences);
    }

    static string ConsumerFingerprint(
        AssemblyPairCallUseConsumerUseSite site) =>
        string.Join(
        "|",
        site.Source.Identity.Name,
        site.SourceModuleVersionId,
        site.SourceMethod.MetadataToken,
        site.Target.Identity.Name,
        site.TargetModuleVersionId,
        string.Join(
            ",",
            site.TargetTypes.Select(
                type => type.ToQualifiedDisplayString())),
        string.Join(
            ",",
            site.TargetMethods.Select(
                method => method.MetadataToken)),
        string.Join(",", site.OccurrenceIndexes));

    static string ProviderFingerprint(
        AssemblyPairCallUseProviderApiType type) =>
        string.Join(
        "|",
        type.Source.Identity.Name,
        type.SourceModuleVersionId,
        type.Target.Identity.Name,
        type.TargetModuleVersionId,
        type.TargetType.ToQualifiedDisplayString(),
        string.Join(
            ",",
            type.SourceMethods.Select(
                method => method.MetadataToken)),
        string.Join(
            ",",
            type.TargetMethods.Select(
                method => method.MetadataToken)),
        string.Join(",", type.OccurrenceIndexes));

    static string ClusterFingerprint(
        AssemblyPairDirectUseCluster cluster) =>
        string.Join(
        "|",
        cluster.Identity.Source.Identity.Name,
        cluster.Identity.SourceModuleVersionId,
        cluster.Identity.AnchorSourceMethodToken,
        cluster.Identity.Target.Identity.Name,
        cluster.Identity.TargetModuleVersionId,
        cluster.Identity.AnchorTargetMethodToken,
        cluster.Derivation,
        cluster.Ordinal,
        string.Join(
            ",",
            cluster.SourceMethods.Select(
                method => method.MetadataToken)),
        string.Join(
            ",",
            cluster.TargetTypes.Select(
                type => type.ToQualifiedDisplayString())),
        string.Join(
            ",",
            cluster.TargetMethods.Select(
                method => method.MetadataToken)),
        string.Join(",", cluster.OccurrenceIndexes));

    [Fact]
    public async Task ExecuteSeparatesFunctionPointerDependenciesInPlanCache()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"pairwise-function-pointer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var dependencyV1 = new AssemblyReferenceIdentity(
                "Collision.Dependency",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: "0011223344556677");
            var dependencyV2 = dependencyV1 with
            {
                Version = new Version(2, 0, 0, 0),
                PublicKeyToken = "8899aabbccddeeff",
            };
            string providerPath = Path.Combine(
                directory,
                "FunctionPointer.Provider.dll");
            string callerPath = Path.Combine(
                directory,
                "FunctionPointer.Caller.dll");
            File.WriteAllBytes(
                providerPath,
                BuildFunctionPointerProvider(
                    dependencyV1,
                    dependencyV2));
            File.WriteAllBytes(
                callerPath,
                BuildFunctionPointerCaller(
                    dependencyV1,
                    dependencyV2));
            await using PairContext context = PairContext.Create(
                callerPath,
                providerPath);

            AssemblyPairCallUseResult result =
                AssemblyPairCallUseQuery.Execute(
                    context.Group,
                    context.First,
                    context.Second);

            Assert.True(
                result.IsComplete,
                $"failures={result.Failures.Length}; "
                + $"unresolved={result.Diagnostics.UnresolvedCandidateCallCount}; "
                + $"occurrences={result.Occurrences.Length}; "
                + $"diagnostics={string.Join(
                    " | ",
                    result.Participants.SelectMany(
                        participant => participant.Diagnostics))}");
            Assert.Equal(
                0,
                result.Diagnostics.UnresolvedCandidateCallCount);
            Assert.Collection(
                result.Occurrences,
                occurrence =>
                {
                    Assert.Equal(
                        "CallFirst",
                        occurrence.SourceMethod.Name);
                    Assert.Equal(
                        "Use",
                        occurrence.TargetMethod.Name);
                },
                occurrence =>
                {
                    Assert.Equal(
                        "CallSecond",
                        occurrence.SourceMethod.Name);
                    Assert.Equal(
                        "Use",
                        occurrence.TargetMethod.Name);
                });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static byte[] BuildFunctionPointerProvider(
        AssemblyReferenceIdentity dependencyV1,
        AssemblyReferenceIdentity dependencyV2)
    {
        MetadataBuilder metadata =
            AssemblyMetadata("FunctionPointer.Provider");
        TypeReferenceHandle typeV1 = AddDependencyType(
            metadata,
            dependencyV1);
        TypeReferenceHandle typeV2 = AddDependencyType(
            metadata,
            dependencyV2);
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("Provider"),
            metadata.GetOrAddString("Api"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var methodBodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(methodBodies);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Use"),
            AddFunctionPointerUseSignature(metadata, typeV1),
            AddRetBody(bodyEncoder),
            parameterList: MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Use"),
            AddFunctionPointerUseSignature(metadata, typeV2),
            AddRetBody(bodyEncoder),
            parameterList: MetadataTokens.ParameterHandle(1));
        return Serialize(metadata, methodBodies);
    }

    static byte[] BuildFunctionPointerCaller(
        AssemblyReferenceIdentity dependencyV1,
        AssemblyReferenceIdentity dependencyV2)
    {
        MetadataBuilder metadata =
            AssemblyMetadata("FunctionPointer.Caller");
        var provider = new AssemblyReferenceIdentity(
            "FunctionPointer.Provider",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        AssemblyReferenceHandle providerReference =
            AddAssemblyReference(metadata, provider);
        TypeReferenceHandle providerType =
            metadata.AddTypeReference(
                providerReference,
                metadata.GetOrAddString("Provider"),
                metadata.GetOrAddString("Api"));
        TypeReferenceHandle typeV1 = AddDependencyType(
            metadata,
            dependencyV1);
        TypeReferenceHandle typeV2 = AddDependencyType(
            metadata,
            dependencyV2);
        MemberReferenceHandle useV1 =
            metadata.AddMemberReference(
                providerType,
                metadata.GetOrAddString("Use"),
                AddFunctionPointerUseSignature(
                    metadata,
                    typeV1));
        MemberReferenceHandle useV2 =
            metadata.AddMemberReference(
                providerType,
                metadata.GetOrAddString("Use"),
                AddFunctionPointerUseSignature(
                    metadata,
                    typeV2));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("Consumer"),
            metadata.GetOrAddString("Calls"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var methodBodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(methodBodies);
        AddCallMethod(
            metadata,
            bodyEncoder,
            "CallFirst",
            useV1);
        AddCallMethod(
            metadata,
            bodyEncoder,
            "CallSecond",
            useV2);
        return Serialize(metadata, methodBodies);
    }

    static void AddCallMethod(
        MetadataBuilder metadata,
        MethodBodyStreamEncoder bodyEncoder,
        string name,
        MemberReferenceHandle target)
    {
        var code = new BlobBuilder();
        var instructions = new InstructionEncoder(
            code,
            new ControlFlowBuilder());
        instructions.OpCode(ILOpCode.Ldnull);
        instructions.Call(target);
        instructions.OpCode(ILOpCode.Ret);
        int bodyOffset = bodyEncoder.AddMethodBody(
            instructions,
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            AddVoidSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
    }

    static int AddRetBody(
        MethodBodyStreamEncoder bodyEncoder)
    {
        var code = new BlobBuilder();
        var instructions = new InstructionEncoder(code);
        instructions.OpCode(ILOpCode.Ret);
        return bodyEncoder.AddMethodBody(
            instructions,
            maxStack: 0);
    }

    static BlobHandle AddFunctionPointerUseSignature(
        MetadataBuilder metadata,
        EntityHandle dependencyType)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters =>
                {
                    MethodSignatureEncoder pointer =
                        parameters
                            .AddParameter()
                            .Type()
                            .FunctionPointer(
                                SignatureCallingConvention.Default,
                                FunctionPointerAttributes.None);
                    pointer.Parameters(
                        1,
                        returnType => returnType.Void(),
                        pointerParameters =>
                            pointerParameters
                                .AddParameter()
                                .Type()
                                .Type(
                                    dependencyType,
                                    isValueType: false));
                });
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddVoidSignature(
        MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        return metadata.GetOrAddBlob(signature);
    }

    static TypeReferenceHandle AddDependencyType(
        MetadataBuilder metadata,
        AssemblyReferenceIdentity dependency)
    {
        AssemblyReferenceHandle reference =
            AddAssemblyReference(metadata, dependency);
        return metadata.AddTypeReference(
            reference,
            metadata.GetOrAddString("Dependency"),
            metadata.GetOrAddString("Value"));
    }

    static AssemblyReferenceHandle AddAssemblyReference(
        MetadataBuilder metadata,
        AssemblyReferenceIdentity identity) =>
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(identity.Name),
            identity.Version ?? new Version(0, 0, 0, 0),
            identity.Culture is null
                ? default
                : metadata.GetOrAddString(identity.Culture),
            identity.PublicKeyToken is null
                ? default
                : metadata.GetOrAddBlob(
                    Convert.FromHexString(
                        identity.PublicKeyToken)),
            flags: default,
            hashValue: default);

    static MetadataBuilder AssemblyMetadata(string name)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString($"{name}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static byte[] Serialize(
        MetadataBuilder metadata,
        BlobBuilder methodBodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static AssemblyPairDirectUseClusterProjection SelectCluster(
        PairContext context,
        string targetMethod)
    {
        AssemblyPairCallUseResult pair =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);
        AssemblyPairDirectUseClusterProjection all =
            AssemblyPairDirectUseClusterProjection.Create(pair);
        AssemblyPairDirectUseCluster cluster =
            Assert.Single(
                all.Clusters,
                candidate =>
                    candidate.TargetMethods.Any(
                        method => method.Name == targetMethod));
        return all.ScopeToObservedCluster(cluster.Ordinal)!;
    }

    sealed class PairContext : IAsyncDisposable
    {
        PairContext(
            InspectionWorkspace workspace,
            AssemblyContextGroup group,
            ResolvedAssemblyReference first,
            ResolvedAssemblyReference second)
        {
            Workspace = workspace;
            Group = group;
            First = first;
            Second = second;
        }

        internal InspectionWorkspace Workspace { get; }
        internal AssemblyContextGroup Group { get; }
        internal ResolvedAssemblyReference First { get; }
        internal ResolvedAssemblyReference Second { get; }

        internal static PairContext Create(
            string firstPath,
            string secondPath)
        {
            ResolvedAssemblyReference first =
                ResolvedAssemblyReference.CreateFromPath(
                    firstPath,
                    AssemblyResolutionProvenance.Local(
                        "pairwise call-use test"));
            ResolvedAssemblyReference second =
                ResolvedAssemblyReference.CreateFromPath(
                    secondPath,
                    AssemblyResolutionProvenance.Local(
                        "pairwise call-use test"));
            return Create(first, second, firstPath);
        }

        internal static PairContext Create(
            ResolvedAssemblyReference first,
            ResolvedAssemblyReference second,
            string resolutionPath)
        {
            var workspace = new InspectionWorkspace();
            AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    Participants(
                        first,
                        second,
                        resolutionPath));
            return new(workspace, group, first, second);
        }

        internal static AssemblyContextParticipant[] Participants(
            ResolvedAssemblyReference first,
            ResolvedAssemblyReference second,
            string resolutionPath)
        {
            var policy =
                new SourceRelativeAssemblyGroupBindingPolicy(
                new[]
                {
                    (
                        first,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(resolutionPath)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                })),
                    (
                        second,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(resolutionPath)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                })),
                });
            return
            [
                new AssemblyContextParticipant(first, policy),
                new AssemblyContextParticipant(second, policy),
            ];
        }

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    }
}
