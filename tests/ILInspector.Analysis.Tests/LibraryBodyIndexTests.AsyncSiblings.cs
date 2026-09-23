using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    [Fact]
    public void OptimizationOpportunities_FindSyncCallsWithAsyncSiblings()
    {
        string path = typeof(OptimizationOpportunityFixtures)
            .Assembly.Location;
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });
        var index = LibraryBodyIndex.Open(path, resolver);
        var opportunities = index.OptimizationOpportunities
            .Where(opportunity =>
                opportunity.Shape == "sync-call-in-async")
            .ToArray();

        var local = Assert.Single(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(OptimizationOpportunityAsyncSiblingFixtures
                    .CallsSyncSiblingFromAsync));
        Assert.Equal(
            nameof(OptimizationOpportunityAsyncSiblingFixtures
                .CallsSyncSiblingFromAsync),
            local.Method.Name);
        Assert.Contains(
            "ReadValuesAsync",
            local.Evidence,
            StringComparison.Ordinal);
        Assert.Equal(
            "analysis.call-site",
            local.SourceFinding);
        Assert.Equal(
            PerformanceTriageProvenance.Exact,
            local.Provenance);
        Assert.Equal("call", local.Operation);
        Assert.NotNull(local.OperandToken);
        Assert.Equal(
            local.Method.MetadataToken,
            local.EvidenceMethodToken);
        AsyncSiblingOpportunityEvidence localEvidence =
            Assert.IsType<AsyncSiblingOpportunityEvidence>(
                local.AsyncSibling);
        Assert.Equal(
            local.Method,
            localEvidence.SynchronousCall.Caller);
        Assert.Equal(
            Assert.IsType<int>(local.EvidenceMethodToken),
            localEvidence.SynchronousCall
                .EvidenceMethod.MetadataToken);
        Assert.Equal(
            Assert.IsType<int>(local.ILOffset),
            localEvidence.SynchronousCall.ILOffset);
        Assert.Equal(
            nameof(OptimizationOpportunityAsyncSiblingFixtures
                .ReadValues),
            localEvidence.SynchronousCall.Callee.Name);
        Assert.Equal(
            nameof(OptimizationOpportunityAsyncSiblingFixtures
                .ReadValuesAsync),
            localEvidence.AsyncCandidate.Name);

        var framework = Assert.Single(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(OptimizationOpportunityFixtures
                    .CallsFileReadLinesFromAsync));
        Assert.Contains(
            "System.IO.File::ReadLinesAsync(string, System.Threading.CancellationToken)",
            framework.Evidence,
            StringComparison.Ordinal);
        AsyncSiblingOpportunityEvidence frameworkEvidence =
            Assert.IsType<AsyncSiblingOpportunityEvidence>(
                framework.AsyncSibling);
        Assert.Equal(
            nameof(File.ReadLines),
            frameworkEvidence.SynchronousCall.Callee.Name);
        Assert.Equal(
            nameof(File.ReadLinesAsync),
            frameworkEvidence.AsyncCandidate.Name);
        Assert.Equal(
            TypeRef.CoreLibrary,
            frameworkEvidence.AsyncCandidate
                .DeclaringType.Assembly);
        Assert.Equal(
            "System.Threading.CancellationToken",
            frameworkEvidence.AsyncCandidate
                .ParameterTypes[^1]
                .ToQualifiedDisplayString());
        Assert.All(
            index.OptimizationOpportunities
                .Where(opportunity => opportunity.Shape
                    != "sync-call-in-async"),
            opportunity => Assert.Null(
                opportunity.AsyncSibling));

        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(OptimizationOpportunityFixtures
                    .CallsSyncSiblingFromNonAsync));
        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(OptimizationOpportunityFixtures
                    .CallsSyncWithoutCompatibleAsyncSibling));
        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(OptimizationOpportunityFixtures
                    .CallsSyncWithMismatchedAsyncReturn));
        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(OptimizationOpportunityFixtures
                    .ExecuteOwnCoreAsync));
        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(OptimizationOpportunityFixtures
                    .CallsBothLifecycleSiblingsAsync));
        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(OptimizationOpportunityFixtures
                    .CallsBothFileReadLinesSiblingsAsync));
        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(GenericSelfSiblingFixture<int>
                    .ReadAsync)
            && opportunity.Method.DeclaringType.Name
                .StartsWith(
                    "GenericSelfSiblingFixture",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(InterfaceSelfSiblingFixture
                    .ReadAsync)
            && opportunity.Method.DeclaringType.Name
                == nameof(InterfaceSelfSiblingFixture));
        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(ConstrainedSiblingFixture
                    .CallsConstrainedSiblingFromAsync));
        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(ConstrainedSiblingFixture
                    .CallsAllowsRefStructFromAsync));
        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(DerivedVirtualSelfSiblingFixture
                    .ReadAsync)
            && opportunity.Method.DeclaringType.Name
                == nameof(
                    DerivedVirtualSelfSiblingFixture));
        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(UnsupportedSignatureSiblingFixture
                    .AnalyzeAsync)
            && opportunity.Method.DeclaringType.Name
                == nameof(
                    UnsupportedSignatureSiblingFixture));

        var genericPrivate = Assert.Single(
            opportunities,
            opportunity => opportunity.Method.Name
                == nameof(GenericPrivateSiblingFixture<int>
                    .CallsPrivateSiblingFromAsync));
        Assert.Contains(
            "ReadAsync",
            genericPrivate.Evidence,
            StringComparison.Ordinal);

        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(RequiredTokenSiblingFixture
                    .CallsRequiredTokenSiblingAsync));

        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                == nameof(UnrelatedVirtualSiblingConsumer
                    .LoadAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        UnrelatedVirtualSiblingConsumer));

        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        NewSlotVirtualSiblingLeaf
                            .LoadAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        NewSlotVirtualSiblingLeaf));

        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        UnrelatedOverrideSiblingConsumer
                            .LoadAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        UnrelatedOverrideSiblingConsumer));
        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        GenericNewSlotSiblingLeaf<int, string>
                            .ReadAsync)
                && opportunity.Method.DeclaringType.Name
                    .StartsWith(
                        "GenericNewSlotSiblingLeaf",
                        StringComparison.Ordinal));
        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        GenericMatchingNewSlotSiblingLeaf<int>
                            .ReadAsync)
                && opportunity.Method.DeclaringType.Name
                    .StartsWith(
                        "GenericMatchingNewSlotSiblingLeaf",
                        StringComparison.Ordinal));

        Assert.Single(
            opportunities,
            opportunity => opportunity.Method.Name
                == nameof(GenericInvocationSiblingFixture
                    .CallsDifferentInstantiationAsync));
        Assert.DoesNotContain(opportunities, opportunity =>
            opportunity.Method.Name
                == nameof(GenericInvocationSiblingFixture
                    .CallsSameInstantiationAsync));
    }

    [Fact]
    public void AsyncSiblingOpportunities_DoNotRequireAllocationAnalysis()
    {
        string path = typeof(OptimizationOpportunityFixtures)
            .Assembly.Location;
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.AsyncSiblingOpportunities,
            resolver);

        Assert.False(index.Features.HasFlag(
            LibraryBodyAnalysisFeatures.Allocations));
        Assert.False(index.Features.HasFlag(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities));
        Assert.Contains(
            index.OptimizationOpportunities,
            opportunity =>
                opportunity.Shape == "sync-call-in-async"
                && opportunity.Method.Name
                    == nameof(
                        OptimizationOpportunityAsyncSiblingFixtures
                            .CallsSyncSiblingFromAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity =>
                opportunity.Shape != "sync-call-in-async");
    }

    [Fact]
    public void OptimizationOpportunities_DistinctCalleesIndexCandidateTypeOnce()
    {
        string path =
            typeof(OptimizationOpportunityAsyncSiblingFixtures)
            .Assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle fixtureType = reader.TypeDefinitions.Single(
            handle => reader.StringComparer.Equals(
                reader.GetTypeDefinition(handle).Name,
                nameof(
                    OptimizationOpportunityAsyncSiblingFixtures)));
        int methodCount = reader.GetTypeDefinition(fixtureType)
            .GetMethods()
            .Count;
        var sourceTokens = reader.GetTypeDefinition(fixtureType)
            .GetMethods()
            .Where(handle =>
            {
                string name = reader.GetString(
                    reader.GetMethodDefinition(handle).Name);
                return name is nameof(
                    OptimizationOpportunityAsyncSiblingFixtures
                            .CallsSyncSiblingFromAsync)
                    or nameof(
                    OptimizationOpportunityAsyncSiblingFixtures
                            .CallsSameSyncSiblingFromAsync)
                    or nameof(
                    OptimizationOpportunityAsyncSiblingFixtures
                            .CallsOtherSyncSiblingFromAsync);
            })
            .Select(handle => MetadataTokens.GetToken(handle))
            .ToHashSet();
        Assert.Equal(3, sourceTokens.Count);
        int scanned = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            asyncSiblingMethodScanned: (definingReader, handle) =>
            {
                if (ReferenceEquals(definingReader, reader)
                    && definingReader.GetMethodDefinition(handle)
                        .GetDeclaringType() == fixtureType)
                {
                    scanned++;
                }
            });

        LibraryBodyAnalysisResult result = builder.Build(
            LibraryBodyAnalysisPlan.Create(
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                sourceTokens,
                typeScope: null));

        Assert.Equal(
            3,
            result.Optimizations.Opportunities.Count(opportunity =>
                opportunity.Shape == "sync-call-in-async"));
        Assert.Equal(methodCount, scanned);
    }

    [Fact]
    public async Task AsyncSiblingMethodIndex_ConcurrentReadsBuildTypeOnce()
    {
        string path = typeof(OptimizationOpportunityFixtures)
            .Assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle fixtureType = reader.TypeDefinitions.Single(
            handle => reader.StringComparer.Equals(
                reader.GetTypeDefinition(handle).Name,
                nameof(OptimizationOpportunityFixtures)));
        int methodCount = reader.GetTypeDefinition(fixtureType)
            .GetMethods()
            .Count;
        int scanned = 0;
        var index = new LibraryBodyAsyncSiblingMethodIndex(
            (definingReader, handle) =>
            {
                if (ReferenceEquals(definingReader, reader)
                    && definingReader.GetMethodDefinition(handle)
                        .GetDeclaringType() == fixtureType)
                {
                    Interlocked.Increment(ref scanned);
                    Thread.Sleep(1);
                }
            });
        using var start = new ManualResetEventSlim();
        Task<IReadOnlyDictionary<
            string,
            ImmutableArray<MethodDefinitionHandle>>>[] reads =
            Enumerable.Range(0, 16)
                .Select(_ => Task.Run(() =>
                {
                    start.Wait(TestContext.Current.CancellationToken);
                    return index.MethodsByName(
                        reader,
                        fixtureType);
                }, TestContext.Current.CancellationToken))
                .ToArray();

        start.Set();
        await Task.WhenAll(reads);

        Assert.Equal(methodCount, scanned);
        Assert.All(
            reads,
            read => Assert.Same(reads[0].Result, read.Result));
    }

    [Fact]
    public void OptimizationOpportunities_InheritedSiblingUsesNearestNameLevel()
    {
        var index = LibraryBodyIndex.Open(
            typeof(OptimizationOpportunityFixtures)
                .Assembly.Location);
        OptimizationOpportunity[] opportunities =
            index.OptimizationOpportunities
                .Where(opportunity => opportunity.Shape
                    == "sync-call-in-async")
                .ToArray();

        OptimizationOpportunity inherited = Assert.Single(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        InheritedAsyncSiblingDerived<int>
                            .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    .StartsWith(
                        nameof(InheritedAsyncSiblingDerived<int>),
                        StringComparison.Ordinal)
                && opportunity.Evidence.Contains(
                    "InheritedAsyncSiblingBase",
                    StringComparison.Ordinal));
        AsyncSiblingOpportunityEvidence inheritedEvidence =
            Assert.IsType<AsyncSiblingOpportunityEvidence>(
                inherited.AsyncSibling);
        Assert.Equal(
            nameof(InheritedAsyncSiblingDerived<int>.Read),
            inheritedEvidence.SynchronousCall.Callee.Name);
        Assert.Equal(
            nameof(InheritedAsyncSiblingBase<int>.ReadAsync),
            inheritedEvidence.AsyncCandidate.Name);
        Assert.Equal(
            TypeRefKind.GenericInstance,
            inheritedEvidence.AsyncCandidate
                .DeclaringType.Kind);
        Assert.Equal(
            TypeRefKind.GenericParameter,
            Assert.Single(
                inheritedEvidence.AsyncCandidate
                    .ParameterTypes).Kind);
        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                    HiddenInheritedAsyncSiblingDerived
                        .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        HiddenInheritedAsyncSiblingDerived));
        Assert.Contains(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        NearestInheritedAsyncSiblingLeaf
                            .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        NearestInheritedAsyncSiblingLeaf)
                && opportunity.Evidence.Contains(
                    "NearestInheritedAsyncSiblingMiddle"
                        + "::ReadAsync",
                    StringComparison.Ordinal));
        Assert.Contains(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                    ProtectedInheritedAsyncSiblingDerived
                        .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        ProtectedInheritedAsyncSiblingDerived));
        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                    ProtectedInheritedAsyncSiblingConsumer
                        .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        ProtectedInheritedAsyncSiblingConsumer));
        Assert.Contains(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                    InternalInheritedAsyncSiblingDerived
                        .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        InternalInheritedAsyncSiblingDerived));
    }

    [Fact]
    public void
        OptimizationOpportunities_InheritedSynchronousReceiverHidingFailsClosed()
    {
        OptimizationOpportunity[] opportunities =
            LibraryBodyIndex.Open(
                    typeof(OptimizationOpportunityFixtures)
                        .Assembly.Location)
                .OptimizationOpportunities
                .Where(opportunity => opportunity.Shape
                    == "sync-call-in-async")
                .ToArray();

        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        HiddenInheritedSynchronousSiblingDerived
                            .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        HiddenInheritedSynchronousSiblingDerived));
        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        InheritedSynchronousSiblingConsumer
                            .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        InheritedSynchronousSiblingConsumer));
        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        InheritedSynchronousDerivedReceiverConsumer
                            .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        InheritedSynchronousDerivedReceiverConsumer));
        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        InheritedSynchronousNameBypassConsumer
                            .ReadAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        InheritedSynchronousNameBypassConsumer));
        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        InterfaceInheritedSynchronousConsumer
                            .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        InterfaceInheritedSynchronousConsumer));
        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        SameTypeInheritedSynchronousBase
                            .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        SameTypeInheritedSynchronousBase));
        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        StaticInheritedSynchronousConsumer
                            .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        StaticInheritedSynchronousConsumer));
        Assert.Contains(
            opportunities,
            opportunity => opportunity.Method.Name
                    == nameof(
                        SealedSynchronousSiblingConsumer
                            .AnalyzeAsync)
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        SealedSynchronousSiblingConsumer));
    }

    [Fact]
    public void AsyncSiblingTypeMatching_RejectsMixedNonCoreOrigins()
    {
        MetadataTypeDefinitionName typeName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Value"]))
            .Name;
        var currentIdentity =
            new AssemblyReferenceIdentity(
                "Collision",
                new Version(1, 0),
                null,
                null);
        TypeRef current = TypeRef.Definition(
            "Collision",
            "Sample",
            "Value",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(
                    currentIdentity),
                typeName));
        TypeRef same = TypeRef.Definition(
            "Collision",
            "Sample",
            "Value",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    currentIdentity),
                typeName));
        TypeRef different = TypeRef.Definition(
            "Collision",
            "Sample",
            "Value",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    new AssemblyReferenceIdentity(
                        "Collision",
                        new Version(2, 0),
                        null,
                        null)),
                typeName));

        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    current,
                    same));
        TypeRef differentCase = TypeRef.Definition(
            "cOLLISION",
            "Sample",
            "Value",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    currentIdentity with
                    {
                        Name = "cOLLISION",
                    }),
                typeName));
        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    current,
                    differentCase));
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    current,
                    different));
    }

    [Fact]
    public void OptimizationOpportunities_PrefetchedImageDoesNotReopenRootPath()
    {
        string sourcePath = typeof(OptimizationOpportunityFixtures)
            .Assembly.Location;
        ImmutableArray<byte> image =
            [.. File.ReadAllBytes(sourcePath)];
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"DeletedAsyncSiblingRoot-{Guid.NewGuid():N}.dll");
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(sourcePath)
            {
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });

        var index = LibraryBodyIndex.OpenFromPrefetchedImage(
            missingPath,
            image,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
            resolver);

        Assert.Contains(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == nameof(OptimizationOpportunityFixtures
                        .CallsFileReadLinesFromAsync));
    }

    [Fact]
    public void AsyncSiblingTypeMatching_DistinguishesExactAssemblyReferenceIdentity()
    {
        MetadataTypeDefinitionName typeName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Value"]))
            .Name;
        static TypeRef CreateType(
            Version version,
            MetadataTypeDefinitionName typeName)
        {
            var identity = new AssemblyReferenceIdentity(
                "Dependency",
                version,
                null,
                null);
            return TypeRef.Definition(
                "Dependency",
                "Sample",
                "Value",
                new ResolvableTypeReference(
                    new TypeReferenceOrigin
                        .AssemblyReference(identity),
                    typeName));
        }

        TypeRef versionOne =
            CreateType(new Version(1, 0), typeName);
        TypeRef versionTwo =
            CreateType(new Version(2, 0), typeName);

        Assert.Equal(versionOne, versionTwo);
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    versionOne,
                    versionTwo));
    }

    [Fact]
    public void AsyncSiblingTypeMatching_HonorsTrustedCoreLibraryFacades()
    {
        MetadataTypeDefinitionName typeName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "System.IO",
                    ["Stream"]))
            .Name;
        static TypeRef CreateType(
            string assemblyName,
            Version version,
            MetadataTypeDefinitionName typeName,
            bool trustedFrameworkAssembly = true)
        {
            var identity = new AssemblyReferenceIdentity(
                assemblyName,
                version,
                null,
                null);
            return TypeRef.Definition(
                assemblyName,
                "System.IO",
                "Stream",
                new ResolvableTypeReference(
                    new TypeReferenceOrigin
                        .AssemblyReference(identity),
                    typeName),
                trustedFrameworkAssembly);
        }

        TypeRef netstandard =
            CreateType(
                "netstandard",
                new Version(2, 0),
                typeName);
        TypeRef systemRuntime =
            CreateType(
                "System.Runtime",
                new Version(11, 0),
                typeName);
        TypeRef untrustedSystemRuntime =
            CreateType(
                "System.Runtime",
                new Version(11, 0),
                typeName,
                trustedFrameworkAssembly: false);
        TypeRef intrinsic = TypeRef.Definition(
            TypeRef.CoreLibrary,
            "System.IO",
            "Stream",
            new ResolvableTypeReference(
                new TypeReferenceOrigin
                    .IntrinsicCoreLibrary(),
                typeName));

        Assert.Equal(netstandard, systemRuntime);
        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    netstandard,
                    systemRuntime));
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    netstandard,
                    untrustedSystemRuntime));
        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    intrinsic,
                    systemRuntime));
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    intrinsic,
                    untrustedSystemRuntime));
    }

    [Theory]
    [InlineData("system.runtime")]
    [InlineData("SYSTEM.THREADING.TASKS")]
    public void AsyncSiblingFrameworkIdentity_IgnoresAssemblyNameCase(
        string assemblyName)
    {
        TypeRef task = TypeRef.Definition(
            assemblyName,
            "System.Threading.Tasks",
            "Task");

        Assert.True(
            FrameworkIdentity.IsKnownFrameworkType(
                task,
                "System.Threading.Tasks",
                "System.Threading.Tasks",
                "Task"));
    }

    [Fact]
    public void AsyncSiblingMethodMatching_PreservesOpenGenericSignature()
    {
        TypeRef declaring = TypeRef.Definition(
            "Sample",
            "Sample",
            "Api");
        TypeRef int32 = TypeRef.CoreLib(
            "System",
            "Int32");
        TypeRef methodParameter =
            TypeRef.MethodGenericParameter(0);
        var generic = new MemberRef(
            declaring,
            "Read",
            [int32],
            int32,
            MemberKind.Method)
        {
            TypeArguments = [int32],
            OpenParameterTypes = [methodParameter],
            OpenReturnType = int32,
            SignatureHeader = 0x10,
            RequiredParameterCount = 1,
            GenericArity = 1,
        };
        var concrete = generic with
        {
            OpenParameterTypes = [int32],
        };

        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingMethodsMatch(
                    generic,
                    generic));
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingMethodsMatch(
                    generic,
                    concrete));
        var source = new MethodIdentity(
            "Sample",
            Guid.Empty,
            declaring,
            "Read",
            [int32],
            int32,
            0x06000001,
            IsStatic: true,
            GenericArity: 1)
        {
            SignatureHeader = 0x10,
            RequiredParameterCount = 1,
        };
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingMethodMatchesSource(
                    generic,
                    source));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AsyncSiblingCancellationTokenDefault_MustBeNull(
        bool validDefault,
        bool duplicateParameter)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("TokenDefault.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("TokenDefault"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    "System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle cancellationToken =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Threading"),
                metadata.GetOrAddString(
                    "CancellationToken"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Api"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        ParameterHandle parameter =
            metadata.AddParameter(
                ParameterAttributes.Optional
                    | ParameterAttributes.HasDefault,
                metadata.GetOrAddString(
                    "cancellationToken"),
                sequenceNumber: 1);
        if (validDefault)
            metadata.AddConstant(parameter, null);
        else
            metadata.AddConstant(parameter, 0);
        if (duplicateParameter)
        {
            ParameterHandle duplicate =
                metadata.AddParameter(
                    ParameterAttributes.Optional
                        | ParameterAttributes.HasDefault,
                    metadata.GetOrAddString(
                        "duplicateCancellationToken"),
                    sequenceNumber: 1);
            metadata.AddConstant(duplicate, null);
        }
        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var il = new BlobBuilder();
        il.WriteByte((byte)ILOpCode.Ret);
        int body = bodyEncoder.AddMethodBody(
            new InstructionEncoder(il),
            maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("ReadAsync"),
            metadata.GetOrAddBlob(
                new byte[]
                {
                    0x00, 0x01, 0x01, 0x11,
                    (byte)CodedIndex
                        .TypeDefOrRefOrSpec(
                            cancellationToken),
                }),
            body,
            parameter);
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        using var peReader = new PEReader(
            new MemoryStream(
                image.ToArray(),
                writable: false));
        MetadataReader reader =
            peReader.GetMetadataReader();

        Assert.Equal(
            validDefault && !duplicateParameter,
            LibraryBodyAsyncSiblingSignatureMatcher
                .TrailingParameterCanBeOmitted(
                    reader,
                    reader.GetMethodDefinition(
                        MetadataTokens
                            .MethodDefinitionHandle(1)),
                    parameterCount: 1));
    }

    [Fact]
    public void OptimizationOpportunities_ClassicAsyncUsesMoveNextEvidenceCoordinate()
    {
        string path = typeof(ClassicAsyncSiblingFixture)
            .Assembly.Location;
        var index = LibraryBodyIndex.Open(path);

        var opportunity = Assert.Single(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == nameof(ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync));
        int evidenceMethodToken =
            Assert.IsType<int>(opportunity.EvidenceMethodToken);
        AsyncSiblingOpportunityEvidence evidence =
            Assert.IsType<AsyncSiblingOpportunityEvidence>(
                opportunity.AsyncSibling);

        Assert.NotEqual(
            opportunity.Method.MetadataToken,
            evidenceMethodToken);
        Assert.NotEqual(
            opportunity.Method,
            evidence.SynchronousCall.Caller);
        Assert.Equal(
            evidenceMethodToken,
            evidence.SynchronousCall.Caller
                .MetadataToken);
        Assert.Equal(
            evidenceMethodToken,
            evidence.SynchronousCall
                .EvidenceMethod.MetadataToken);
        Assert.Equal(
            nameof(ClassicAsyncSiblingFixture.ReadValue),
            evidence.SynchronousCall.Callee.Name);
        Assert.Equal(
            nameof(ClassicAsyncSiblingFixture.ReadValueAsync),
            evidence.AsyncCandidate.Name);
        Assert.Equal(
            "MoveNext",
            Assert.Single(
                index.Methods,
                method => method.MetadataToken
                    == evidenceMethodToken).Name);
        Assert.Equal(
            "analysis.call-site",
            opportunity.SourceFinding);
        Assert.Equal(
            PerformanceTriageProvenance.Exact,
            opportunity.Provenance);
        var memberScoped = LibraryBodyIndex.Open(
            path,
            bodyScope: new HashSet<int>
            {
                opportunity.Method.MetadataToken,
            });
        Assert.Single(
            memberScoped.OptimizationOpportunities,
            candidate => candidate.Shape
                    == "sync-call-in-async"
                && candidate.Method.MetadataToken
                    == opportunity.Method.MetadataToken);
        string sourceTypeName =
            opportunity.Method.DeclaringType
                .ToQualifiedDisplayString();
        Func<TypeRef, bool> sourceTypeScope =
            type => type.ToQualifiedDisplayString()
                == sourceTypeName;
        var typeScoped = LibraryBodyIndex.Open(
            path,
            bodyTypeScope: sourceTypeScope);
        Assert.Single(
            typeScoped.OptimizationOpportunities,
            candidate => candidate.Shape
                    == "sync-call-in-async"
                && candidate.Method.MetadataToken
                    == opportunity.Method.MetadataToken);
        var memberAndTypeScoped =
            LibraryBodyIndex.Open(
                path,
                bodyScope: new HashSet<int>
                {
                    opportunity.Method.MetadataToken,
                },
                bodyTypeScope: sourceTypeScope);
        Assert.Single(
            memberAndTypeScoped
                .OptimizationOpportunities,
            candidate => candidate.Shape
                    == "sync-call-in-async"
                && candidate.Method.MetadataToken
                    == opportunity.Method.MetadataToken);
        var generatedTypeScoped =
            LibraryBodyIndex.Open(
                path,
                bodyTypeScope:
                    type => type.Name.Contains(
                        ">d__",
                        StringComparison.Ordinal));
        Assert.DoesNotContain(
            generatedTypeScoped
                .OptimizationOpportunities,
            candidate => candidate.Shape
                    == "sync-call-in-async"
                && candidate.Method.MetadataToken
                    == opportunity.Method.MetadataToken);

        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        ClassicGenericSelfSiblingFixture<int>)
                && opportunity.Method.Name
                    == nameof(
                        ClassicGenericSelfSiblingFixture<int>
                            .ReadAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        ClassicGenericMethodSelfSiblingFixture)
                && opportunity.Method.Name
                    == nameof(
                        ClassicGenericMethodSelfSiblingFixture
                            .ReadAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == nameof(
                        ClassicGenericInterfaceSelfSiblingFixture
                            .LoadAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name.EndsWith(
                    ".FetchAsync",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == nameof(
                        ClassicGenericVirtualSelfSiblingFixture
                            .LookupAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == nameof(
                        ClassicAsyncSiblingFixture
                            .CallsRefWithOutSiblingAsync));
        Assert.Single(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == nameof(
                        ClassicAsyncSiblingFixture
                            .CallsCompatibleRefSiblingAsync));

        var collision = Assert.Single(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == nameof(
                        ClassicStateMachineCollision<int>
                            .AnalyzeAsync)
                && opportunity.Method.DeclaringType
                    .Resolution?.Type.Segments[0]
                    == "ClassicStateMachineCollision`1");
        Assert.Equal(
            "ClassicStateMachineCollision`1",
            collision.Method.DeclaringType
                .Resolution?.Type.Segments[0]);

        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        ClassicInterfaceCacheFixture)
                && opportunity.Method.Name
                    == nameof(
                        ClassicInterfaceCacheFixture
                            .AaaOtherAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        ClassicInterfaceCacheFixture)
                && opportunity.Method.Name
                    == nameof(
                        ClassicInterfaceCacheFixture
                            .ReadAsync));
        Assert.Contains(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == nameof(
                        ClassicSelfCacheFixture
                            .ZzzAnalyzeAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == nameof(
                        ClassicSelfCacheFixture
                            .AaaAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        ClassicProtectedSiblingDerivedFixture)
                && opportunity.Method.Name
                    == nameof(
                        ClassicProtectedSiblingDerivedFixture
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        ClassicPrivateProtectedSiblingDerivedFixture)
                && opportunity.Method.Name
                    == nameof(
                        ClassicPrivateProtectedSiblingDerivedFixture
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        ClassicCovariantInterfaceSelfSiblingFixture)
                && opportunity.Method.Name
                    == nameof(
                        ClassicCovariantInterfaceSelfSiblingFixture
                            .ReadAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        ClassicProtectedReceiverDerivedFixture)
                && opportunity.Method.Name
                    == nameof(
                        ClassicProtectedReceiverDerivedFixture
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        ClassicProtectedStaticSiblingDerivedFixture)
                && opportunity.Method.Name
                    == nameof(
                        ClassicProtectedStaticSiblingDerivedFixture
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    == "IClassicContravariantDefaultSiblingFixture`1"
                && opportunity.Method.Name
                    == nameof(
                        IClassicContravariantDefaultSiblingFixture<
                            object>.ConsumeAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        ClassicUnrelatedExplicitDefaultSiblingFixture)
                && opportunity.Method.Name.EndsWith(
                    ".ReadAsync",
                    StringComparison.Ordinal));
        Assert.Contains(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    .EndsWith(
                        "+"
                        + nameof(
                            ClassicNestedPrivateSiblingFixture.Consumer),
                        StringComparison.Ordinal)
                && opportunity.Method.Name
                    == nameof(
                        ClassicNestedPrivateSiblingFixture.Consumer
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.DeclaringType.Name
                    == nameof(
                        IClassicHiddenDerivedSiblingFixture)
                && opportunity.Method.Name
                    == nameof(
                        IClassicHiddenDerivedSiblingFixture
                            .ReadAsync));
    }

    [Fact]
    public void
        OptimizationOpportunities_PrivateAccessIsDirectionalAcrossNestedTypes()
    {
        OptimizationOpportunity[] opportunities =
            LibraryBodyIndex.Open(
                    typeof(ClassicAsyncSiblingFixture)
                        .Assembly.Location)
                .OptimizationOpportunities
                .Where(opportunity => opportunity.Shape
                    == "sync-call-in-async")
                .ToArray();

        Assert.Contains(
            opportunities,
            opportunity => opportunity.Method.DeclaringType.Name
                    .EndsWith(
                        "+"
                        + nameof(
                            ClassicNestedPrivateSiblingFixture.Consumer),
                        StringComparison.Ordinal)
                && opportunity.Method.Name
                    == nameof(
                        ClassicNestedPrivateSiblingFixture.Consumer
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.DeclaringType.Name
                    == nameof(
                        ClassicOuterToNestedPrivateSiblingFixture)
                && opportunity.Method.Name
                    == nameof(
                        ClassicOuterToNestedPrivateSiblingFixture
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            opportunities,
            opportunity => opportunity.Method.DeclaringType.Name
                    .Contains(
                        nameof(
                            ClassicSiblingNestedPrivateSiblingFixture),
                        StringComparison.Ordinal)
                && opportunity.Method.DeclaringType.Name
                    .EndsWith(
                        "+"
                        + nameof(
                            ClassicSiblingNestedPrivateSiblingFixture
                                .Consumer),
                        StringComparison.Ordinal)
                && opportunity.Method.Name
                    == nameof(
                        ClassicSiblingNestedPrivateSiblingFixture
                            .Consumer.AnalyzeAsync));
    }

    [Fact]
    public void AsyncSiblingPrivateAccess_CyclicDeclaringTypeFailsClosed()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("CyclicNestedType.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("CyclicNestedType"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle cyclic =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("Cyclic"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(cyclic, cyclic);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        using var peReader =
            new PEReader(
                new MemoryStream(image.ToArray()));

        Assert.False(
            LibraryBodyAsyncSiblingAccessibilityAnalyzer
                .TryTopLevelType(
                peReader.GetMetadataReader(),
                cyclic,
                out _));
    }

    [Fact]
    public void
        OptimizationOpportunities_FriendAccessRequiresProvableReceiver()
    {
        string path =
            FixtureCatalog.AnalysisAsyncSiblingFriend
                .AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                IncludeDepsJsonAssets = true,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });

        var index = LibraryBodyIndex.Open(path, resolver);

        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == "AnalyzeAsync"
                && opportunity.Method.DeclaringType.Name
                    == "FriendProtectedReceiver");
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == "PublicAnalyzeAsync");
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == "InternalAnalyzeAsync");
        Assert.Contains(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == "AnalyzeAsync"
                && opportunity.Method.DeclaringType.Name
                    == "FriendSiblingConsumer"
                && opportunity.Evidence.Contains(
                    "FriendSiblingGrantor::ReadAsync",
                    StringComparison.Ordinal));
        var diagnostic = Assert.Single(index.Diagnostics);
        Assert.Contains(
            "MalformedAsyncSourceFixture::AnalyzeAsync",
            diagnostic.Method,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncSiblingSelection_ExactCandidateWinsRegardlessOfOrder()
    {
        TypeRef declaring = TypeRef.Definition(
            "Sample",
            "Sample",
            "Api");
        TypeRef int32 = TypeRef.CoreLib(
            "System",
            "Int32");
        MemberRef exact = new(
            declaring,
            "ReadAsync",
            [int32],
            int32,
            MemberKind.Method);
        MemberRef optionalOne = exact with
        {
            ParameterTypes =
            [
                int32,
                TypeRef.CoreLib(
                    "System.Threading",
                    "CancellationToken"),
            ],
        };
        MemberRef optionalTwo = optionalOne with
        {
            ReturnType = TypeRef.CoreLib(
                "System.Threading.Tasks",
                "ValueTask"),
        };

        AssertSelection(
            [optionalOne, optionalTwo, exact],
            exact);
        AssertSelection(
            [exact, optionalOne, optionalTwo],
            exact);

        static void AssertSelection(
            MemberRef[] candidates,
            MemberRef expected)
        {
            MemberRef? best = null;
            bool ambiguous = false;
            foreach (MemberRef candidate in candidates)
            {
                LibraryBodyAsyncSiblingCandidateResolver
                    .ConsiderAsyncSibling(
                        candidate,
                        ref best,
                        ref ambiguous);
            }
            Assert.Same(expected, best);
            Assert.False(ambiguous);
        }
    }

    [Fact]
    public void ConstructedInterfaceIdentity_RequiresMatchingArguments()
    {
        TypeRef interfaceDefinition = TypeRef.Definition(
            "Sample",
            "Sample",
            "IReader`1");
        TypeRef int32 = TypeRef.CoreLib(
            "System",
            "Int32");
        TypeRef text = TypeRef.CoreLib(
            "System",
            "String");
        TypeRef intReader = TypeRef.GenericInstance(
            interfaceDefinition,
            [int32]);
        TypeRef stringReader = TypeRef.GenericInstance(
            interfaceDefinition,
            [text]);

        Assert.True(
            LibraryBodyAsyncSiblingDispatchAnalyzer
                .ConstructedTypeArgumentsMatch(
                    intReader,
                    intReader));
        Assert.False(
            LibraryBodyAsyncSiblingDispatchAnalyzer
                .ConstructedTypeArgumentsMatch(
                    intReader,
                    stringReader));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptimizationOpportunities_DuplicateLocalTypeIdentityFailsClosed(
        bool useMemberReference)
    {
        byte[] image = BuildDuplicateLocalTypeAssembly(
            useMemberReference);
        var index = LibraryBodyIndex.OpenFromPrefetchedImage(
            "DuplicateLocalTypes.dll",
            [.. image],
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);

        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                == "sync-call-in-async");
    }

    [Fact]
    public void MethodImplSignature_RequiresByRefDirection()
    {
        TypeRef declaring = TypeRef.Definition(
            "Probe",
            "Sample",
            "Reader");
        TypeRef byRefInt = TypeRef.ByRef(
            TypeRef.CoreLib("System", "Int32"));
        TypeRef task = TypeRef.Definition(
            "System.Runtime",
            "System.Threading.Tasks",
            "Task",
            trustedFrameworkAssembly: true);
        var body = new MemberRef(
            declaring,
            "AnalyzeAsync",
            [byRefInt],
            task,
            MemberKind.Method)
        {
            HasThis = true,
            SignatureHeader = 0x20,
            RequiredParameterCount = 1,
            ParameterDirections =
                [ParameterDirection.Ref],
        };
        var declaration = body with
        {
            Name = "OtherAsync",
            ParameterDirections =
                [ParameterDirection.Out],
        };

        Assert.False(
            LibraryBodyAsyncSiblingDispatchAnalyzer
                .SameMethodImplSignature(
                    body,
                    declaration));
    }

    [Theory]
    [InlineData(
        false,
        ParameterAttributes.None,
        ParameterAttributes.None,
        true)]
    [InlineData(
        true,
        ParameterAttributes.None,
        ParameterAttributes.None,
        true)]
    [InlineData(
        true,
        ParameterAttributes.Out,
        ParameterAttributes.Out,
        true)]
    [InlineData(
        true,
        ParameterAttributes.In,
        ParameterAttributes.In,
        true)]
    [InlineData(
        true,
        ParameterAttributes.None,
        ParameterAttributes.Out,
        false)]
    public void OptimizationOpportunities_ResolvedMemberRefUsesParamDirection(
        bool byRef,
        ParameterAttributes synchronousDirection,
        ParameterAttributes asynchronousDirection,
        bool expected)
    {
        byte[] dependency =
            BuildDirectionProbeDependency(
                byRef,
                synchronousDirection,
                asynchronousDirection);
        byte[] caller =
            BuildDirectionProbeCaller(byRef);

        var index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "DirectionProbeCaller.dll",
                [.. caller],
                LibraryBodyAnalysisFeatures
                    .MethodEvidence
                    | LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                new DirectionProbeResolver(dependency));

        int count = index.OptimizationOpportunities.Count(
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == "AnalyzeAsync");
        Assert.Equal(expected ? 1 : 0, count);
        Assert.Empty(index.Diagnostics);
    }

    [Fact]
    public void OptimizationOpportunities_AmbiguousResolvedMemberRefDirectionFailsClosed()
    {
        byte[] dependency =
            BuildDirectionProbeDependency(
                byRef: true,
                ParameterAttributes.None,
                ParameterAttributes.None,
                duplicateSynchronous: true);
        byte[] caller =
            BuildDirectionProbeCaller(byRef: true);
        var index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "DirectionProbeCaller.dll",
                [.. caller],
                LibraryBodyAnalysisFeatures.Default,
                new DirectionProbeResolver(dependency));

        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == "AnalyzeAsync");
        Assert.Empty(index.Diagnostics);
    }

    [Fact]
    public void OptimizationOpportunities_UnresolvedSynchronousMemberFailsClosed()
    {
        byte[] dependency =
            BuildDirectionProbeDependency(
                byRef: false,
                ParameterAttributes.None,
                ParameterAttributes.None,
                synchronousName: "Gone");
        byte[] caller =
            BuildDirectionProbeCaller(byRef: false);
        var index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "DirectionProbeCaller.dll",
                [.. caller],
                LibraryBodyAnalysisFeatures.Default,
                new DirectionProbeResolver(dependency));

        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == "AnalyzeAsync");
        Assert.Empty(index.Diagnostics);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void OptimizationOpportunities_AsyncSiblingStaticMetadataMustBeConsistent(
        bool asynchronousMethodIsStatic,
        int expected)
    {
        byte[] dependency =
            BuildDirectionProbeDependency(
                byRef: false,
                ParameterAttributes.None,
                ParameterAttributes.None,
                asynchronousMethodIsStatic:
                    asynchronousMethodIsStatic);
        byte[] caller =
            BuildDirectionProbeCaller(byRef: false);
        var index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "DirectionProbeCaller.dll",
                [.. caller],
                LibraryBodyAnalysisFeatures.Default,
                new DirectionProbeResolver(dependency));

        Assert.Equal(
            expected,
            index.OptimizationOpportunities.Count(
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync"));
        Assert.Empty(index.Diagnostics);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void OptimizationOpportunities_AsyncSiblingGenericCountMatchesRows(
        bool addAsynchronousGenericParameter,
        int expected)
    {
        byte[] dependency =
            BuildDirectionProbeDependency(
                byRef: false,
                ParameterAttributes.None,
                ParameterAttributes.None,
                genericSignature: true,
                addAsynchronousGenericParameter:
                    addAsynchronousGenericParameter);
        byte[] caller =
            BuildDirectionProbeCaller(
                byRef: false,
                genericSignature: true);
        var index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "DirectionProbeCaller.dll",
                [.. caller],
                LibraryBodyAnalysisFeatures.Default,
                new DirectionProbeResolver(dependency));

        Assert.Equal(
            expected,
            index.OptimizationOpportunities.Count(
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.Name
                        == "AnalyzeAsync"));
        Assert.Empty(index.Diagnostics);
    }

    [Fact]
    public void OptimizationOpportunities_RuntimeAsyncIgnoresClassicAttribute()
    {
        byte[] dependency =
            BuildDirectionProbeDependency(
                byRef: false,
                ParameterAttributes.None,
                ParameterAttributes.None);
        byte[] caller =
            BuildDirectionProbeCaller(
                byRef: false,
                addStateMachineAttribute: true);
        var index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "DirectionProbeCaller.dll",
                [.. caller],
                LibraryBodyAnalysisFeatures.Default,
                new DirectionProbeResolver(dependency));

        Assert.Single(
            index.OptimizationOpportunities,
            opportunity => opportunity.Shape
                    == "sync-call-in-async"
                && opportunity.Method.Name
                    == "AnalyzeAsync");
        Assert.Empty(index.Diagnostics);
    }

    [Fact]
    public void OptimizationOpportunities_MvidCollisionPreservesRecursiveInterfaceSuppression()
    {
        Guid dependencyMvid = Guid.Parse(
            "11111111-2222-3333-4444-555555555555");
        byte[] dependency =
            BuildMvidCollisionDependency(
                dependencyMvid);
        var resolver =
            new MvidCollisionResolver(dependency);
        var collision =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "CollisionRoot.dll",
                [.. BuildMvidCollisionRoot(
                    dependencyMvid)],
                LibraryBodyAnalysisFeatures.Default,
                resolver);
        var distinct =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "CollisionRoot.dll",
                [.. BuildMvidCollisionRoot(
                    Guid.Parse(
                        "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"))],
                LibraryBodyAnalysisFeatures.Default,
                resolver);

        Assert.Contains(
            collision.DirectCalls,
            call => call.Caller.Name
                    == "ReadAsync"
                && call.Callee.Name == "Read"
                && call.Callee.DeclaringType.Name
                    == "IReader");
        Assert.Empty(collision.Diagnostics);
        Assert.DoesNotContain(
            distinct.OptimizationOpportunities,
            IsRecursiveReadAsyncOpportunity);
        Assert.DoesNotContain(
            collision.OptimizationOpportunities,
            IsRecursiveReadAsyncOpportunity);

        static bool IsRecursiveReadAsyncOpportunity(
            OptimizationOpportunity opportunity)
            => opportunity.Shape == "sync-call-in-async"
                && opportunity.Method.Name
                    == "ReadAsync";
    }
}
