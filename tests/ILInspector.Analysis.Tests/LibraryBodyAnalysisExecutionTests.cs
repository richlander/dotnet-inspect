using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;

namespace ILInspector.Analysis.Tests;

public sealed class LibraryBodyAnalysisExecutionTests
{
    [Fact]
    public void ExecutePath_PublishesFocusedResultsWithOneReceipt()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles));

        Assert.Same(
            execution.Receipt,
            execution.Safety.Receipt);
        Assert.Same(
            execution.Receipt,
            execution.Allocations.Receipt);
        Assert.Same(
            execution.Receipt,
            execution.ImplementationProfiles.Receipt);
        Assert.Same(
            execution.Receipt,
            execution.Optimization.Receipt);
        Assert.Same(
            execution.Receipt,
            execution.CallGraph.Receipt);
        Assert.Same(
            execution.Receipt,
            execution.Leverage.Receipt);
        Assert.False(
            execution.Safety.Evidence.IsDefault);
        Assert.True(
            execution.Safety.WasRequested);
        Assert.False(
            execution.Allocations.WasRequested);
        Assert.Empty(
            execution.Allocations.Occurrences);
        Assert.True(
            execution.ImplementationProfiles.WasRequested);
        Assert.True(
            execution.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.MethodEvidence));
        Assert.False(
            execution.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.Allocations));
        Assert.False(
            execution.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities));
        Assert.False(
            execution.Optimization.WasRequested);
        Assert.True(
            execution.CallGraph
                .HasProjectedPhysicalDirectCalls);
        Assert.True(
            execution.CallGraph
                .HasProjectedMethodSignals);
        Assert.Empty(
            execution.Optimization.Opportunities);
        Assert.Empty(
            execution.Optimization
                .AllocationFanoutOpportunities);
        Assert.True(
            execution.Optimization
                .HasProjectedPhysicalDirectCalls);
        Assert.NotEmpty(
            execution.ImplementationProfiles.Profiles);
        Assert.NotEmpty(
            execution.ImplementationProfiles
                .OverloadRelationships);
        Assert.Same(
            execution.ImplementationProfiles
                .GeneratedFrameworkTypes,
            execution.Leverage.GeneratedFrameworkTypes);
        Assert.Same(
            execution.ImplementationProfiles
                .GeneratedFrameworkTypes,
            execution.Optimization.GeneratedFrameworkTypes);
        Assert.Same(
            execution.CallGraph.DeclaredMethodMap,
            execution.Optimization.DeclaredMethodMap);
    }

    [Fact]
    public void ExecutePath_DoesNotProduceUnrequestedSafetyEvidence()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.None));

        Assert.False(
            execution.Safety.WasRequested);
        Assert.Empty(
            execution.Safety.Evidence);
        Assert.Empty(
            execution.Safety.Occurrences);
        Assert.False(
            execution.Allocations.WasRequested);
        Assert.Empty(
            execution.Allocations.Occurrences);
    }

    [Fact]
    public void ExecutePath_DoesNotProduceUnrequestedProfiles()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));

        Assert.False(
            execution.ImplementationProfiles.WasRequested);
        Assert.Empty(
            execution.ImplementationProfiles.Profiles);
        Assert.Empty(
            execution.ImplementationProfiles
                .OverloadRelationships);
        Assert.Empty(
            execution.ImplementationProfiles
                .GeneratedFrameworkTypes);
        Assert.False(
            execution.ImplementationProfiles.Coverage.WasRequested);
        Assert.Equal(
            execution.Receipt.HasFullMethodEvidenceScope,
            execution.ImplementationProfiles
                .Coverage
                .HasFullMethodEvidenceScope);
        Assert.NotEmpty(
            execution.ImplementationProfiles
                .Coverage
                .ManagedMethodBodies);
        Assert.Empty(
            execution.ImplementationProfiles
                .Coverage
                .ProfiledEvidenceBodies);
        Assert.Empty(
            execution.ImplementationProfiles
                .Coverage
                .UnavailableBodies);
    }

    [Fact]
    public void ExecutePath_PublishesImplementationProfileCoverage()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles));

        ImplementationProfilePopulationCoverageReceipt coverage =
            execution.ImplementationProfiles.Coverage;

        Assert.True(coverage.WasRequested);
        Assert.True(coverage.HasFullMethodEvidenceScope);
        Assert.Equal(
            execution.Receipt.Diagnostics,
            coverage.Diagnostics);
        Assert.Equal(
            execution.CallGraph.DeclaredMethods,
            coverage.DeclaredMethods);
        Assert.Equal(
            execution.CallGraph.Methods,
            coverage.ManagedMethodBodies);
        HashSet<int> profiledEvidenceTokens =
        [
            .. execution.ImplementationProfiles
                .Profiles
                .Select(static profile => profile.EvidenceMethod.MetadataToken),
        ];
        Assert.Equal(
            profiledEvidenceTokens.Count,
            coverage.ProfiledEvidenceBodyCount);
        Assert.All(
            coverage.ProfiledEvidenceBodies,
            method => Assert.Contains(
                method.MetadataToken,
                profiledEvidenceTokens));
        Assert.Equal(
            coverage.ManagedMethodBodyCount,
            coverage.ProfiledEvidenceBodyCount);
        Assert.Empty(coverage.UnavailableBodies);
    }

    [Fact]
    public void ExecutePath_ProfileCoverageRecordsScopedUnavailableBodies()
    {
        LibraryBodyAnalysisExecution full =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles));
        int selectedBodyToken =
            full.ImplementationProfiles
                .Profiles[0]
                .EvidenceMethod
                .MetadataToken;

        LibraryBodyAnalysisExecution scoped =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles,
                    bodyScope:
                    new HashSet<int> { selectedBodyToken }));

        ImplementationProfilePopulationCoverageReceipt coverage =
            scoped.ImplementationProfiles.Coverage;

        Assert.True(coverage.WasRequested);
        Assert.False(coverage.HasFullMethodEvidenceScope);
        Assert.Contains(
            coverage.ProfiledEvidenceBodies,
            method => method.MetadataToken == selectedBodyToken);
        Assert.True(
            coverage.ManagedMethodBodyCount
                > coverage.ProfiledEvidenceBodyCount);
        Assert.All(
            coverage.UnavailableBodies,
            body => Assert.Equal(
                ImplementationProfileUnavailableReason.ScopeExcluded,
                body.Reason));
    }

    [Fact]
    public void ExecuteImage_ProfileCoverageReportsScopedDiagnosticsAsAnalysisFailed()
    {
        byte[] image = File.ReadAllBytes(
            typeof(ArrayPoolLeakFixtures).Assembly.Location);
        int methodToken =
            ReplaceMethodCodeSizeWithInvalidValue(image);

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecuteImage(
                "MalformedProfileBody.dll",
                ImmutableArray.Create(image),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles,
                    bodyScope: new HashSet<int> { methodToken }));

        ImplementationProfileUnavailableBody body =
            Assert.Single(
                execution.ImplementationProfiles
                    .Coverage
                    .UnavailableBodies,
                body => body.EvidenceMethod.MetadataToken == methodToken);
        Assert.Equal(
            ImplementationProfileUnavailableReason.AnalysisFailed,
            body.Reason);
        Assert.NotNull(body.Diagnostic);
        Assert.Contains(
            nameof(BadImageFormatException),
            body.Diagnostic.Message);
    }

    [Fact]
    public void CompatibilityIndex_PreservesFocusedProfileResults()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles));

        LibraryBodyIndex index =
            execution.CompatibilityIndex();

        Assert.Equal(
            execution.ImplementationProfiles.Profiles,
            index.ImplementationProfiles());
        Assert.Equal(
            execution.ImplementationProfiles
                .OverloadRelationships,
            index.OverloadRelationships());
        Assert.True(
            execution.ImplementationProfiles
                .GeneratedFrameworkTypes.SetEquals(
                    index.GeneratedFrameworkTypes));
    }

    static int ReplaceMethodCodeSizeWithInvalidValue(
        byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle methodHandle =
            reader.MethodDefinitions.Single(handle =>
                reader.StringComparer.Equals(
                    reader.GetMethodDefinition(handle).Name,
                    nameof(ArrayPoolLeakFixtures
                        .ExternalReadBeforeReturn)));
        MethodDefinition method =
            reader.GetMethodDefinition(methodHandle);
        int methodOffset = RvaToFileOffset(
            peReader.PEHeaders,
            method.RelativeVirtualAddress);
        Assert.Equal(3, image[methodOffset] & 3);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(methodOffset + 4, 4),
            0x7F000000);
        return MetadataTokens.GetToken(methodHandle);
    }

    static int RvaToFileOffset(
        PEHeaders headers,
        int rva)
    {
        SectionHeader section =
            headers.SectionHeaders.Single(header =>
                rva >= header.VirtualAddress
                && rva < header.VirtualAddress
                    + Math.Max(
                        header.VirtualSize,
                        header.SizeOfRawData));
        return checked(rva - section.VirtualAddress + section.PointerToRawData);
    }

    [Fact]
    public void CompatibilityIndex_DelegatesCallGraphAndLeverageResults()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));
        LibraryBodyIndex index =
            execution.CompatibilityIndex();
        int rootToken =
            execution.CallGraph.Methods[0].MetadataToken;

        Assert.Same(
            execution.CallGraph,
            index.CallGraphAnalysis);
        Assert.Same(
            execution.Leverage,
            index.LeverageAnalysis);
        Assert.Equal(
            execution.CallGraph.BuildCallTree(rootToken),
            index.BuildCallTree(rootToken));
        Assert.Equal(
            execution.CallGraph.BuildCallerTree(rootToken),
            index.BuildCallerTree(rootToken));
        Assert.Equal(
            execution.Leverage.Top(int.MaxValue),
            index.TopLeverage(int.MaxValue));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CompatibilityIndex_PreservesFocusedOptimizationResults()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                typeof(LibraryBodyAnalysisExecutionTests)
                    .Assembly.Location,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities));

        LibraryBodyIndex index =
            execution.CompatibilityIndex();

        Assert.True(execution.Optimization.WasRequested);
        Assert.False(
            execution.Optimization
                .HasProjectedPhysicalDirectCalls);
        Assert.NotEmpty(
            execution.Optimization.Opportunities);
        Assert.True(
            execution.Optimization
                .HasProjectedPhysicalDirectCalls);
        Assert.Equal(
            execution.Optimization.Opportunities,
            index.OptimizationOpportunities);
        Assert.Equal(
            execution.Optimization
                .AllocationFanoutOpportunities,
            index.AllocationFanoutOpportunities);
        Assert.True(
            execution.Optimization
                .GeneratedFrameworkTypes.SetEquals(
                    index.GeneratedFrameworkTypes));
    }
}
