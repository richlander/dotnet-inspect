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
    public void CompleteProfileRequest_CreatesVersionedMetricPlan()
    {
        LibraryBodyAnalysisRequest request =
            LibraryBodyAnalysisRequest
                .CreateCompleteImplementationProfile();

        Assert.Equal(
            LibraryBodyAnalysisFeatures.None,
            request.Features);
        ImplementationMetricAnalysisPlan plan =
            Assert.IsType<ImplementationMetricAnalysisPlan>(
                request.Plan.ImplementationMetrics);
        Assert.Equal(
            ImplementationMetricAnalysisRequest.CompleteProfileV1,
            plan.RequestedEvidence);
        Assert.Equal(
            plan.RequestedEvidence,
            plan.EffectiveEvidence);
        Assert.False(
            plan.EffectiveEvidence.HasFlag(
                ImplementationMetricEvidenceKind
                    .AllocationOccurrences));
        Assert.Equal(
            ImplementationMetricRequestOrigin
                .CompleteProfileCompatibility,
            plan.Origin);
        Assert.True(plan.Limits.IsLegacyUnbounded);
        Assert.True(
            request.Plan.Includes(
                LibraryBodyAnalysisFeatures
                    .ImplementationProfiles));
        Assert.True(
            request.Plan.Includes(
                LibraryBodyAnalysisFeatures.MethodEvidence));
    }

    [Fact]
    public void CompleteProfileRequest_PreservesLegacyProfileResult()
    {
        string path =
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        LibraryImplementationProfileAnalysisResult legacy =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles))
            .ImplementationProfiles;
        LibraryImplementationProfileAnalysisResult migrated =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateCompleteImplementationProfile())
            .ImplementationProfiles;

        Assert.Equal(legacy.Profiles, migrated.Profiles);
        Assert.Equal(
            legacy.OverloadRelationships,
            migrated.OverloadRelationships);
        Assert.Equal(
            legacy.Coverage.WasRequested,
            migrated.Coverage.WasRequested);
        Assert.Equal(
            legacy.Coverage.HasFullMethodEvidenceScope,
            migrated.Coverage.HasFullMethodEvidenceScope);
        Assert.Equal(
            legacy.Coverage.DeclaredMethods,
            migrated.Coverage.DeclaredMethods);
        Assert.Equal(
            legacy.Coverage.ManagedMethodBodies,
            migrated.Coverage.ManagedMethodBodies);
        Assert.Equal(
            legacy.Coverage.ProfiledEvidenceBodies,
            migrated.Coverage.ProfiledEvidenceBodies);
        Assert.Equal(
            legacy.Coverage.UnavailableBodies,
            migrated.Coverage.UnavailableBodies);
        Assert.Equal(
            legacy.Coverage.Diagnostics,
            migrated.Coverage.Diagnostics);
        Assert.True(
            legacy.GeneratedFrameworkTypes.SetEquals(
                migrated.GeneratedFrameworkTypes));
    }

    [Fact]
    public void LegacyProfileFeature_NormalizesToMetricPlan()
    {
        LibraryBodyAnalysisRequest request =
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures
                    .ImplementationProfiles);

        ImplementationMetricAnalysisPlan plan =
            Assert.IsType<ImplementationMetricAnalysisPlan>(
                request.Plan.ImplementationMetrics);
        Assert.Equal(
            ImplementationMetricAnalysisRequest.CompleteProfileV1,
            plan.EffectiveEvidence);
        Assert.Equal(
            ImplementationMetricRequestOrigin
                .LegacyFeatureCompatibility,
            plan.Origin);
        Assert.True(plan.Limits.IsLegacyUnbounded);
    }

    [Fact]
    public void MetricPlan_ClosesSiblingRelationshipsOverCalls()
    {
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 10,
            maximumEncodedIlBytes: 10_000,
            maximumAttributionProbeBodies: 20,
            maximumAttributionProbeIlBytes: 20_000);
        var request = new ImplementationMetricAnalysisRequest(
            ImplementationMetricEvidenceKind.BodySize
                | ImplementationMetricEvidenceKind
                    .SiblingOverloadRelationships,
            limits,
            ImplementationMetricRequestOrigin.Explicit);

        ImplementationMetricAnalysisPlan plan =
            ImplementationMetricAnalysisPlan.Create(request);

        Assert.Equal(
            request.RequestedEvidence,
            plan.RequestedEvidence);
        Assert.True(
            plan.EffectiveEvidence.HasFlag(
                ImplementationMetricEvidenceKind.DirectCalls));
        Assert.True(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .CanonicalMethodContext));
        Assert.True(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .DirectCallCollection));
        Assert.True(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .SiblingRelationshipProjection));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .AllocationSignalCollection));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .AllocationOccurrenceCollection));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .BodySignalCollection));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage.SafetyCollection));
    }

    [Fact]
    public void MetricPlan_BodySizeAvoidsCanonicalContextWork()
    {
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 10,
            maximumEncodedIlBytes: 10_000,
            maximumAttributionProbeBodies: 20,
            maximumAttributionProbeIlBytes: 20_000);
        var request = new ImplementationMetricAnalysisRequest(
            ImplementationMetricEvidenceKind.BodySize,
            limits,
            ImplementationMetricRequestOrigin.Explicit);

        ImplementationMetricAnalysisPlan plan =
            ImplementationMetricAnalysisPlan.Create(request);

        Assert.Equal(
            ImplementationMetricEvidenceKind.BodySize,
            plan.EffectiveEvidence);
        Assert.True(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .ManagedBodyAcquisition));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .LocalSignatureDecode));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .CanonicalMethodContext));
        Assert.False(
            plan.WorkStages.HasFlag(
                ImplementationMetricWorkStage
                    .DirectCallCollection));
    }

    [Fact]
    public void MetricRequest_RejectsEmptyUnknownAndInvalidLimits()
    {
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 1,
            maximumEncodedIlBytes: 1,
            maximumAttributionProbeBodies: 1,
            maximumAttributionProbeIlBytes: 1);
        Assert.Throws<ArgumentException>(
            () => ImplementationMetricAnalysisPlan.Create(
                new(
                    ImplementationMetricEvidenceKind.None,
                    limits,
                    ImplementationMetricRequestOrigin.Explicit)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImplementationMetricAnalysisPlan.Create(
                new(
                    (ImplementationMetricEvidenceKind)(1 << 20),
                    limits,
                    ImplementationMetricRequestOrigin.Explicit)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ImplementationMetricWorkLimits(
                maximumPhysicalBodies: 0,
                maximumEncodedIlBytes: 1,
                maximumAttributionProbeBodies: 1,
                maximumAttributionProbeIlBytes: 1));
    }

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
                body => body.MethodToken == methodToken);
        Assert.Equal(
            ImplementationProfileUnavailableReason.AnalysisFailed,
            body.Reason);
        Assert.NotNull(body.EvidenceMethod);
        Assert.NotNull(body.Diagnostic);
        Assert.Contains(
            nameof(BadImageFormatException),
            body.Diagnostic.Message);
    }

    [Fact]
    public void ExecuteImage_ProfileCoverageRetainsTokenOnlyIdentityFailures()
    {
        byte[] image = File.ReadAllBytes(
            typeof(ArrayPoolLeakFixtures).Assembly.Location);
        int methodToken =
            ReplaceMethodSignatureWithInvalidValue(image);

        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecuteImage(
                "MalformedProfileSignature.dll",
                ImmutableArray.Create(image),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles));

        ImplementationProfilePopulationCoverageReceipt coverage =
            execution.ImplementationProfiles.Coverage;
        ImplementationProfileUnavailableBody body =
            Assert.Single(
                coverage.UnavailableBodies,
                body => body.MethodToken == methodToken);
        Assert.Equal(
            ImplementationProfileUnavailableReason.AnalysisFailed,
            body.Reason);
        Assert.Null(body.EvidenceMethod);
        Assert.NotNull(body.Diagnostic);
        Assert.Equal(
            coverage.ManagedMethodBodies.Length + 1,
            coverage.ManagedMethodBodyCount);
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

    static int ReplaceMethodSignatureWithInvalidValue(
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
        int methodToken = MetadataTokens.GetToken(methodHandle);
        Assert.NotEqual(0, method.RelativeVirtualAddress);
        int stringIndexSize = MetadataHeapIndexSize(
            reader,
            HeapIndex.String);
        int blobIndexSize = MetadataHeapIndexSize(
            reader,
            HeapIndex.Blob);
        int rowOffset =
            reader.GetTableMetadataOffset(TableIndex.MethodDef)
            + (MetadataTokens.GetRowNumber(methodHandle) - 1)
                * reader.GetTableRowSize(TableIndex.MethodDef);
        int signatureOffset =
            peReader.PEHeaders.MetadataStartOffset
            + rowOffset
            + sizeof(uint)
            + sizeof(ushort)
            + sizeof(ushort)
            + stringIndexSize;
        if (blobIndexSize == sizeof(uint))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                image.AsSpan(signatureOffset, blobIndexSize),
                0x7FFFFFFF);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                image.AsSpan(signatureOffset, blobIndexSize),
                0xFFFF);
        }
        return methodToken;
    }

    static int MetadataHeapIndexSize(
        MetadataReader reader,
        HeapIndex heap) =>
        reader.GetHeapSize(heap) < 0x10000
            ? sizeof(ushort)
            : sizeof(uint);

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
