using System.IO.Compression;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text;
using System.Xml;
using System.Text.Json;
using DotnetInspector.Ecosystems;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using ILInspector.Research;
using InertText;
using Inspector.Findings;
using ILInspector.Metadata;
using NuGetFetch;

using DotnetInspect.Web.Interop.Package;
using BrowserMetadataJsonContext = DotnetInspect.Web.Interop.Metadata.BrowserMetadataJsonContext;
using BrowserAnalysisJsonContext = DotnetInspect.Web.Interop.Analysis.BrowserAnalysisJsonContext;
using BrowserSourceJsonContext = DotnetInspect.Web.Interop.Source.BrowserSourceJsonContext;
using BrowserCallGraphJsonContext = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphJsonContext;
using BrowserCatalogJsonContext = DotnetInspect.Web.Interop.Catalog.BrowserCatalogJsonContext;
using BrowserPackageMetadata = DotnetInspect.Web.Interop.Metadata.BrowserPackageMetadata;
using BrowserMetadataCompileLibraryStatus = DotnetInspect.Web.Interop.Metadata.BrowserCompileLibraryStatus;
using BrowserAnalysisCompileLibraryStatus = DotnetInspect.Web.Interop.Analysis.BrowserCompileLibraryStatus;
using BrowserPackageIntegrations = DotnetInspect.Web.Interop.Analysis.BrowserPackageIntegrations;
using BrowserPackageOpportunities = DotnetInspect.Web.Interop.Analysis.BrowserPackageOpportunities;
using BrowserPackagePerformance = DotnetInspect.Web.Interop.Analysis.BrowserPackagePerformance;
using BrowserPerformanceMember = DotnetInspect.Web.Interop.Analysis.BrowserPerformanceMember;
using BrowserOpportunityItem = DotnetInspect.Web.Interop.Analysis.BrowserOpportunityItem;
using BrowserSource = DotnetInspect.Web.Interop.Source.BrowserSource;
using BrowserCallGraph = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraph;
using BrowserCallGraphTarget = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphTarget;
using BrowserCallGraphWireProjection = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphWireProjection;
using BrowserCallGraphDiagnostics = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphDiagnostics;
using BrowserHomeDemoRunResult = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunResult;
using BrowserHomeDemoRunActivation = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunActivation;
using BrowserHomeDemoRunPlan = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunPlan;
using BrowserProductHomeDemos = DotnetInspect.Web.Interop.Catalog.BrowserProductHomeDemos;
using BrowserHomeDemoRunMember = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunMember;
using BrowserHomeDemoRunRequest = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunRequest;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{

    [Fact]
    public async Task PackagePerformance_ExcludesOtherLibraries()
    {
        const string PackageId = "Browser.Performance.Exact";
        const string SelectedLibrary = "Selected.Empty";
        byte[] otherLibrary = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackageEntries(
                    ($"lib/net11.0/{SelectedLibrary}.dll",
                        BuildEmptySurfaceImage(
                            new AssemblyName(SelectedLibrary))),
                    ("lib/net11.0/DotnetInspect.Web.Tests.dll",
                        otherLibrary)),
                fromCache: false));

        BrowserPackagePerformance performance =
            Assert.IsType<BrowserPackagePerformance>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackagePerformance(
                        PackageId,
                        "1.0.0",
                        "net11.0",
                        "selected.empty.dll"),
                    BrowserAnalysisJsonContext.Default
                        .BrowserPackagePerformance));

        Assert.Equal(0, performance.TotalOpportunities);
        Assert.Equal(0, performance.NonPublicOpportunities);
        Assert.Empty(performance.Members);
    }

    [Fact]
    public async Task PackagePerformance_UnrelatedFailureDoesNotLeakIntoSelectedLibrary()
    {
        await AssertPerformanceParticipantIsolation(
            "Browser.Performance.FailedNeighbor",
            [0x01, 0x02, 0x03]);
    }

    [Fact]
    public async Task PackagePerformance_UnrelatedSurfaceCannotConsumeSelectedBudget()
    {
        await AssertPerformanceParticipantIsolation(
            "Browser.Performance.BoundedNeighbor",
            BuildTransportAmplificationImage(
                "A.Other",
                typeCount: 10_000,
                namespaceLength: 1_000));
    }

    [Fact]
    public async Task MemberFacts_DistinguishesSurfaceAndBodyTokenResolution()
    {
        const string PackageId = "Browser.Member.Facts";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await QueryPackageSurfaceJson(
            PackageId,
            "1.0.0",
            "net11.0");
        using JsonDocument surfaceDocument =
            JsonDocument.Parse(surfaceJson);
        JsonElement type = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                == typeof(BrowserEngineBoundaryTests).FullName);
        JsonElement member = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(PerformanceBoxingProbe));
        JsonElement property = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(PerformanceBoxingProperty));
        JsonElement getter = Assert.Single(
            property.GetProperty("bodySelectors").EnumerateArray());

        string graphMemberJson =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryGraphMemberSurface(
                PackageId,
                "1.0.0",
                "net11.0",
                type.GetProperty("assembly").GetString()!,
                type.GetProperty("definitionId").GetString()!,
                getter.GetProperty("memberName").GetString()!,
                "stale-selector",
                getter.GetProperty("token").GetInt32());
        using JsonDocument graphMemberDocument =
            JsonDocument.Parse(graphMemberJson);
        JsonElement graphMember = graphMemberDocument.RootElement;
        JsonElement graphMemberType = graphMember.GetProperty("type");
        Assert.Equal(
            type.GetProperty("definitionId").GetString(),
            graphMemberType.GetProperty("definitionId").GetString());
        Assert.Equal(
            type.GetProperty("assemblyId").GetString(),
            graphMemberType.GetProperty("assemblyId").GetString());
        JsonElement graphMemberApi =
            Assert.Single(graphMemberType.GetProperty("api").EnumerateArray());
        Assert.Equal(
            JsonValueKind.Null,
            graphMemberApi
                .GetProperty("metadataToken").ValueKind);
        Assert.Equal(
            getter.GetProperty("token").GetInt32(),
            graphMember.GetProperty("selectedBody")
                .GetProperty("token").GetInt32());
        Assert.Equal(
            getter.GetProperty("memberName").GetString(),
            graphMember.GetProperty("selectedBody")
                .GetProperty("memberName").GetString());
        Assert.Equal(
            getter.GetProperty("selectorKey").GetString(),
            graphMember.GetProperty("selectedBody")
                .GetProperty("selectorKey").GetString());

        string accessorFactsJson =
            await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryMemberFacts(
                PackageId,
                "1.0.0",
                "net11.0",
                type.GetProperty("assembly").GetString()!,
                type.GetProperty("definitionId").GetString()!,
                graphMember.GetProperty("selectedBody")
                    .GetProperty("memberName").GetString()!,
                property.GetProperty("signature").GetString()!,
                graphMember.GetProperty("selectedBody")
                    .GetProperty("selectorKey").GetString()!,
                graphMember.GetProperty("selectedBody")
                    .GetProperty("token").GetInt32(),
                implementationBodySelected: true);
        using JsonDocument accessorFactsDocument =
            JsonDocument.Parse(accessorFactsJson);
        Assert.Equal(
            getter.GetProperty("token").GetInt32(),
            accessorFactsDocument.RootElement
                .GetProperty("metadataToken").GetInt32());

        string json = await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryMemberFacts(
            PackageId,
            "1.0.0",
            "net11.0",
            type.GetProperty("assembly").GetString()!,
            type.GetProperty("definitionId").GetString()!,
            member.GetProperty("name").GetString()!,
            member.GetProperty("signature").GetString()!,
            member.GetProperty("graphSelectorKey").GetString()!,
            typeof(BrowserEngineBoundaryTests)
                .GetMethod(nameof(PerformanceNoAllocationProbe))!
                .MetadataToken,
            implementationBodySelected: false);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(
            typeof(BrowserEngineBoundaryTests)
                .GetMethod(nameof(PerformanceBoxingProbe))!
                .MetadataToken,
            root.GetProperty("metadataToken").GetInt32());
        Assert.NotEqual(
            typeof(BrowserEngineBoundaryTests)
                .GetMethod(nameof(PerformanceNoAllocationProbe))!
                .MetadataToken,
            root.GetProperty("metadataToken").GetInt32());
        Assert.True(
            root.GetProperty("signals")
                .GetProperty("allocations")
                .GetInt32() > 0);
        Assert.Contains(
            root.GetProperty("allocations").EnumerateArray(),
            allocation =>
                allocation.GetProperty("kind").GetString()
                    == nameof(AllocationKind.Box)
                && allocation.GetProperty("countedAsHeap").GetBoolean());
        Assert.Contains(
            root.GetProperty("performanceOpportunities")
                .EnumerateArray(),
            opportunity =>
                opportunity.GetProperty("shape").GetString()
                == "box-value-type");
        Assert.Empty(
            root.GetProperty("diagnostics").EnumerateArray());

        JsonElement genericCallMember = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(PerformanceGenericCallProbe));
        string genericCallJson = await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryMemberFacts(
            PackageId,
            "1.0.0",
            "net11.0",
            type.GetProperty("assembly").GetString()!,
            type.GetProperty("definitionId").GetString()!,
            genericCallMember.GetProperty("name").GetString()!,
            genericCallMember.GetProperty("signature").GetString()!,
            genericCallMember.GetProperty("graphSelectorKey").GetString()!,
            metadataToken: 0,
            implementationBodySelected: false);
        using JsonDocument genericCallDocument =
            JsonDocument.Parse(genericCallJson);
        string[] genericCallees =
        [
            .. genericCallDocument.RootElement
                .GetProperty("calls")
                .EnumerateArray()
                .Select(call =>
                    call.GetProperty("callee").GetString()!)
                .Where(callee =>
                    callee.Contains(
                        nameof(PerformanceGenericCallTarget),
                        StringComparison.Ordinal))
                .Distinct(),
        ];
        Assert.Equal(4, genericCallees.Length);
        Assert.All(
            genericCallees,
            callee => Assert.Contains("<", callee));
        Assert.Contains(
            genericCallees,
            callee => callee.Contains(
                "<System.Threading.Timer>",
                StringComparison.Ordinal));
        Assert.Contains(
            genericCallees,
            callee => callee.Contains(
                "<System.Timers.Timer>",
                StringComparison.Ordinal));

        int implementationToken = typeof(BrowserEngineBoundaryTests)
            .GetMethod(nameof(PerformanceBoxingProbe))!
            .MetadataToken;
        string implementationBodyJson =
            await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryMemberFacts(
                PackageId,
                "1.0.0",
                "net11.0",
                type.GetProperty("assembly").GetString()!,
                type.GetProperty("definitionId").GetString()!,
                member.GetProperty("name").GetString()!,
                member.GetProperty("signature").GetString()!,
                "missing-structural-selector",
                implementationToken,
                implementationBodySelected: true);
        using JsonDocument implementationBodyDocument =
            JsonDocument.Parse(implementationBodyJson);
        Assert.Equal(
            implementationToken,
            implementationBodyDocument.RootElement
                .GetProperty("metadataToken")
                .GetInt32());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryMemberFacts(
                PackageId,
                "1.0.0",
                "net11.0",
                type.GetProperty("assembly").GetString()!,
                type.GetProperty("definitionId").GetString()!,
                member.GetProperty("name").GetString()!,
                member.GetProperty("signature").GetString()!,
                "missing-structural-selector",
                implementationToken,
                implementationBodySelected: false));

        JsonElement valueTypeMember = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(PerformanceValueTypeConstructionProbe));
        string valueTypeJson = await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryMemberFacts(
            PackageId,
            "1.0.0",
            "net11.0",
            type.GetProperty("assembly").GetString()!,
            type.GetProperty("definitionId").GetString()!,
            valueTypeMember.GetProperty("name").GetString()!,
            valueTypeMember.GetProperty("signature").GetString()!,
            valueTypeMember.GetProperty("graphSelectorKey").GetString()!,
            metadataToken: 0,
            implementationBodySelected: false);
        using JsonDocument valueTypeDocument =
            JsonDocument.Parse(valueTypeJson);
        Assert.Contains(
            valueTypeDocument.RootElement
                .GetProperty("allocations")
                .EnumerateArray(),
            allocation =>
                !allocation.GetProperty("countedAsHeap").GetBoolean());

        JsonElement stackAllocMember = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(PerformanceStackAllocProbe));
        string stackAllocJson = await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryMemberFacts(
            PackageId,
            "1.0.0",
            "net11.0",
            type.GetProperty("assembly").GetString()!,
            type.GetProperty("definitionId").GetString()!,
            stackAllocMember.GetProperty("name").GetString()!,
            stackAllocMember.GetProperty("signature").GetString()!,
            stackAllocMember.GetProperty("graphSelectorKey").GetString()!,
            metadataToken: 0,
            implementationBodySelected: false);
        using JsonDocument stackAllocDocument =
            JsonDocument.Parse(stackAllocJson);
        JsonElement[] safety =
            [.. stackAllocDocument.RootElement
                .GetProperty("safety")
                .EnumerateArray()];
        Assert.Contains(
            safety,
            fact => fact.GetProperty("kind").GetString() == "stackalloc");
        Assert.DoesNotContain(
            safety
                .Where(fact => fact.GetProperty("offset").ValueKind
                    == JsonValueKind.String)
                .GroupBy(fact => fact.GetProperty("offset").GetString()),
            group => group.Count() > 1);
    }

    [Fact]
    public async Task AnnotatedSourceDestinations_RetainLoadedAssemblyIdentity()
    {
        const string PackageId = "Browser.Annotated.Destinations";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await QueryPackageSurfaceJson(
            PackageId,
            "1.0.0",
            "net11.0");
        using JsonDocument surfaceDocument =
            JsonDocument.Parse(surfaceJson);
        JsonElement type = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                == typeof(BrowserEngineBoundaryTests).FullName);
        JsonElement member = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(InvocationDestinationProbe));

        string annotatedJson =
            await DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberAnnotatedSource(
                PackageId,
                "1.0.0",
                "net11.0",
                type.GetProperty("assembly").GetString()!,
                type.GetProperty("definitionId").GetString()!,
                type.GetProperty("queryId").GetString()!,
                member.GetProperty("name").GetString()!,
                member.GetProperty("signature").GetString()!,
                member.GetProperty("graphSelectorKey").GetString()!,
                member.GetProperty("metadataToken").GetInt32(),
                "[]");
        using JsonDocument annotatedDocument =
            JsonDocument.Parse(annotatedJson);
        Assert.False(
            annotatedDocument.RootElement
                .GetProperty("viewerCatalog")
                .GetProperty("findingEvidence")
                .GetProperty("available")
                .GetBoolean());
        Assert.Equal(
            "NotProjected",
            annotatedDocument.RootElement
                .GetProperty("viewerCatalog")
                .GetProperty("findingEvidence")
                .GetProperty("unavailableReason")
                .GetString());
        Assert.Empty(
            annotatedDocument.RootElement
                .GetProperty("findingEvidence")
                .EnumerateArray());
        JsonElement destination = Assert.Single(
            annotatedDocument.RootElement
                .GetProperty("viewerCatalog")
                .GetProperty("invocationDestinations")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("target")
                    .GetProperty("memberName").GetString()
                == nameof(InvocationDestinationTarget));
        JsonElement target = destination.GetProperty("target");

        Assert.Equal(
            typeof(BrowserEngineBoundaryTests).Assembly.GetName().Version?.ToString(),
            target.GetProperty("assemblyVersion").GetString());
        Assert.Equal(
            type.GetProperty("assemblyId").GetString(),
            target.GetProperty("surfaceAssemblyId").GetString());
    }

    [Fact]
    public async Task MemberFindingCensus_TransportsOneReceiptAcrossBothProjections()
    {
        const string PackageId = "Browser.Member.FindingCensus";
        const string CallerCanonicalIdentity =
            "M:DotnetInspect.Web.Tests.BrowserEngineBoundaryTests."
            + "PerformanceBoxingProbe(System.Int32)";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await QueryPackageSurfaceJson(
            PackageId,
            "1.0.0",
            "net11.0");
        using JsonDocument surfaceDocument = JsonDocument.Parse(surfaceJson);
        JsonElement type = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                == typeof(BrowserEngineBoundaryTests).FullName);
        JsonElement member = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(PerformanceBoxingProbe));

        string censusJson = await DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberFindingCensus(
            PackageId,
            "1.0.0",
            "net11.0",
            type.GetProperty("assembly").GetString()!,
            type.GetProperty("definitionId").GetString()!,
            type.GetProperty("queryId").GetString()!,
            member.GetProperty("name").GetString()!,
            CallerCanonicalIdentity,
            member.GetProperty("graphSelectorKey").GetString()!,
            member.GetProperty("metadataToken").GetInt32(),
            "[]");
        using JsonDocument censusDocument = JsonDocument.Parse(censusJson);
        JsonElement root = censusDocument.RootElement;
        Assert.True(
            Guid.TryParse(
                root.GetProperty("factCensusReceipt").GetString(),
                out Guid receipt));
        Assert.NotEqual(Guid.Empty, receipt);

        int[] factKeys =
        [
            .. root.GetProperty("facts")
                .EnumerateArray()
                .Where(fact =>
                    fact.GetProperty("instanceKey").ValueKind
                        == JsonValueKind.Number)
                .Select(fact =>
                    fact.GetProperty("instanceKey").GetInt32())
                .Order(),
        ];
        int[] sourceKeys =
        [
            .. root.GetProperty("sourceFactInstances")
                .EnumerateArray()
                .Select(identity =>
                    identity.GetProperty("instanceKey").GetInt32())
                .Order(),
        ];
        Assert.NotEmpty(factKeys);
        Assert.Equal(factKeys.Length, factKeys.Distinct().Count());
        Assert.Equal(factKeys, sourceKeys);

        JsonElement annotatedSource = root.GetProperty("annotatedSource");
        string declarationJson =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .QueryMemberDeclaration(
                    PackageId,
                    "1.0.0",
                    "net11.0",
                    type.GetProperty("assembly").GetString()!,
                    type.GetProperty("definitionId").GetString()!,
                    member.GetProperty("name").GetString()!,
                    member.GetProperty("graphSelectorKey").GetString()!,
                    member.GetProperty("metadataToken").GetInt32(),
                    implementationMember: true);
        using JsonDocument declarationDocument =
            JsonDocument.Parse(declarationJson);
        string declaration = declarationDocument.RootElement
            .GetProperty("text")
            .GetString()!;
        Assert.Equal(
            declaration,
            annotatedSource.GetProperty("signature").GetString());
        Assert.Contains(
            "public static object PerformanceBoxingProbe(int value)",
            declaration,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            CallerCanonicalIdentity,
            annotatedSource.GetProperty("signature").GetString()!,
            StringComparison.Ordinal);
        HashSet<int> documentFactIds =
        [
            .. annotatedSource
                .GetProperty("document")
                .GetProperty("facts")
                .EnumerateArray()
                .Select(fact => fact.GetProperty("id").GetInt32()),
        ];
        Assert.All(
            root.GetProperty("sourceFactInstances").EnumerateArray(),
            identity => Assert.Contains(
                identity.GetProperty("factId").GetInt32(),
                documentFactIds));
    }

    [Fact]
    public async Task MemberFindingCensus_ProjectsExactCalleeEvidenceSource()
    {
        const string PackageId = "Browser.Member.CalleeEvidence";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await QueryPackageSurfaceJson(
            PackageId,
            "1.0.0",
            "net11.0");
        using JsonDocument surfaceDocument = JsonDocument.Parse(surfaceJson);
        JsonElement type = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                == typeof(BrowserEngineBoundaryTests).FullName);
        JsonElement member = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(CalleeEvidenceProbe));

        string censusJson = await DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberFindingCensus(
            PackageId,
            "1.0.0",
            "net11.0",
            type.GetProperty("assembly").GetString()!,
            type.GetProperty("definitionId").GetString()!,
            type.GetProperty("queryId").GetString()!,
            member.GetProperty("name").GetString()!,
            member.GetProperty("signature").GetString()!,
            member.GetProperty("graphSelectorKey").GetString()!,
            member.GetProperty("metadataToken").GetInt32(),
            "[]");
        using JsonDocument censusDocument = JsonDocument.Parse(censusJson);
        JsonElement root = censusDocument.RootElement;
        JsonElement annotatedSource = root.GetProperty("annotatedSource");
        Assert.True(
            annotatedSource
                .GetProperty("viewerCatalog")
                .GetProperty("findingEvidence")
                .GetProperty("available")
                .GetBoolean());

        JsonElement evidence = Assert.Single(
            annotatedSource.GetProperty("findingEvidence").EnumerateArray());
        int factId = evidence.GetProperty("factId").GetInt32();
        int instanceKey = evidence.GetProperty("instanceKey").GetInt32();
        JsonElement relationshipFact = Assert.Single(
            root.GetProperty("facts").EnumerateArray(),
            candidate =>
                candidate.GetProperty("id").GetString()
                    == ResearchFactRegistry.CallRelationshipDescriptorId);
        int relationshipInstanceKey =
            relationshipFact.GetProperty("instanceKey").GetInt32();
        Assert.Contains(
            nameof(PerformanceStackAllocProbe),
            relationshipFact.GetProperty("detail").GetString(),
            StringComparison.Ordinal);
        JsonElement relationshipIdentity = Assert.Single(
            root.GetProperty("sourceFactInstances").EnumerateArray(),
            identity =>
                identity.GetProperty("instanceKey").GetInt32()
                    == relationshipInstanceKey);
        int relationshipFactId =
            relationshipIdentity.GetProperty("factId").GetInt32();
        JsonElement relationshipDocumentFact = Assert.Single(
            annotatedSource
                .GetProperty("document")
                .GetProperty("facts")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("id").GetInt32()
                    == relationshipFactId);
        Assert.Equal(
            ResearchFactRegistry.CallRelationshipDescriptorId,
            relationshipDocumentFact.GetProperty("descriptor").GetString());
        Assert.DoesNotContain(
            relationshipFactId,
            annotatedSource
                .GetProperty("viewerCatalog")
                .GetProperty("defaultFindingIds")
                .EnumerateArray()
                .Select(value => value.GetInt32()));
        Assert.True(
            annotatedSource
                .GetProperty("viewerCatalog")
                .GetProperty("callRelationships")
                .GetProperty("available")
                .GetBoolean());
        JsonElement relationship = Assert.Single(
            annotatedSource
                .GetProperty("callRelationships")
                .EnumerateArray());
        Assert.Equal(
            relationshipFactId,
            relationship.GetProperty("factId").GetInt32());
        Assert.True(relationship.GetProperty("edgeRow").GetInt32() > 0);
        Assert.Equal(
            nameof(PerformanceStackAllocProbe),
            relationship.GetProperty("target")
                .GetProperty("memberName")
                .GetString());
        Assert.Equal(
            member.GetProperty("metadataToken").GetInt32(),
            relationship.GetProperty("callerToken").GetInt32());
        Assert.Equal(
            relationshipDocumentFact.GetProperty("source_offset").GetInt32(),
            relationship.GetProperty("ilOffset").GetInt32());
        JsonElement sourceIdentity = Assert.Single(
            root.GetProperty("sourceFactInstances").EnumerateArray(),
            identity => identity.GetProperty("factId").GetInt32() == factId);
        Assert.Equal(
            instanceKey,
            sourceIdentity.GetProperty("instanceKey").GetInt32());
        JsonElement fact = Assert.Single(
            root.GetProperty("facts").EnumerateArray(),
            candidate =>
                candidate.GetProperty("instanceKey").ValueKind
                    == JsonValueKind.Number
                && candidate.GetProperty("instanceKey").GetInt32()
                    == instanceKey);
        Assert.Equal("safety.callee", fact.GetProperty("id").GetString());

        Assert.Equal(
            nameof(PerformanceStackAllocProbe),
            evidence.GetProperty("target")
                .GetProperty("memberName")
                .GetString());
        Assert.Equal(
            typeof(BrowserEngineBoundaryTests)
                .GetMethod(nameof(PerformanceStackAllocProbe))!
                .MetadataToken,
            evidence.GetProperty("target")
                .GetProperty("metadataToken")
                .GetInt32());
        Assert.Equal(
            type.GetProperty("assemblyId").GetString(),
            evidence.GetProperty("target")
                .GetProperty("surfaceAssemblyId")
                .GetString());
        JsonElement coordinate = Assert.Single(
            evidence.GetProperty("coordinates").EnumerateArray());
        Assert.Equal("Localloc", coordinate.GetProperty("kind").GetString());
        Assert.True(coordinate.GetProperty("ilOffset").GetInt32() >= 0);
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("unavailableReason").ValueKind);

        int documentId = evidence.GetProperty("documentId").GetInt32();
        JsonElement calleeDocument = Assert.Single(
            annotatedSource
                .GetProperty("findingEvidenceDocuments")
                .EnumerateArray(),
            candidate => candidate.GetProperty("id").GetInt32() == documentId)
            .GetProperty("document");
        int nodeId = Assert.Single(
            evidence.GetProperty("nodeIds").EnumerateArray()).GetInt32();
        JsonElement node = calleeDocument
            .GetProperty("nodes")
            .EnumerateArray()
            .ElementAt(nodeId);
        Assert.Equal(
            "StackAllocationExpression",
            node.GetProperty("kind").GetString());
        string text = calleeDocument.GetProperty("text").GetString()!;
        Assert.Contains(
            node.GetProperty("spans").EnumerateArray(),
            span => text.Substring(
                span.GetProperty("start").GetInt32(),
                span.GetProperty("length").GetInt32())
                .Contains("stackalloc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MemberFindingCensus_ProjectsExactMutualCycleWitness()
    {
        const string PackageId = "Browser.Member.CallCycles";
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphTarget
                .AssemblyPath());
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await QueryPackageSurfaceJson(
            PackageId,
            "1.0.0",
            "net11.0");
        using JsonDocument surfaceDocument =
            JsonDocument.Parse(surfaceJson);
        JsonElement type = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                    == "Target.InstanceRecursionApi");
        JsonElement member = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                    == "IsEven");

        string censusJson =
            await DotnetInspect.Web.Interop.Source.SourceExports
                .QueryMemberFindingCensus(
                    PackageId,
                    "1.0.0",
                    "net11.0",
                    type.GetProperty("assembly").GetString()!,
                    type.GetProperty("definitionId").GetString()!,
                    type.GetProperty("queryId").GetString()!,
                    member.GetProperty("name").GetString()!,
                    member.GetProperty("signature").GetString()!,
                    member.GetProperty("graphSelectorKey")
                        .GetString()!,
                    member.GetProperty("metadataToken").GetInt32(),
                    "[]");

        using JsonDocument censusDocument =
            JsonDocument.Parse(censusJson);
        JsonElement annotatedSource =
            censusDocument.RootElement
                .GetProperty("annotatedSource");
        JsonElement cycles = annotatedSource
            .GetProperty("viewerCatalog")
            .GetProperty("callCycles");
        Assert.True(
            cycles.GetProperty("available").GetBoolean());
        JsonElement cycle = Assert.Single(
            cycles.GetProperty("findings").EnumerateArray());
        Assert.Equal(
            2,
            cycle.GetProperty("edgeRows").GetArrayLength());
        int factId = Assert.Single(
            cycle.GetProperty("factIds").EnumerateArray())
            .GetInt32();
        Assert.Equal(
            ["IsOdd", "IsEven"],
            cycle.GetProperty("targets")
                .EnumerateArray()
                .Select(target =>
                    target.GetProperty("memberName").GetString()));

        JsonElement relationship = Assert.Single(
            annotatedSource.GetProperty("callRelationships")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("factId").GetInt32()
                    == factId);
        Assert.Equal(
            cycle.GetProperty("edgeRows")[0].GetInt32(),
            relationship.GetProperty("edgeRow").GetInt32());
        Assert.DoesNotContain(
            factId,
            annotatedSource
                .GetProperty("viewerCatalog")
                .GetProperty("defaultFindingIds")
                .EnumerateArray()
                .Select(value => value.GetInt32()));
    }

    [Fact]
    public async Task MemberFindingCensus_ProjectsSynchronousTaskCompletionOperation()
    {
        const string PackageId = "Browser.Member.SynchronousCompletion";
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphTarget
                .AssemblyPath());
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await QueryPackageSurfaceJson(
            PackageId,
            "1.0.0",
            "net11.0");
        using JsonDocument surfaceDocument =
            JsonDocument.Parse(surfaceJson);
        JsonElement type = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                    == "Target.SynchronousCompletionApi");
        JsonElement member = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                    == "AwaiterResult");

        string censusJson =
            await DotnetInspect.Web.Interop.Source.SourceExports
                .QueryMemberFindingCensus(
                    PackageId,
                    "1.0.0",
                    "net11.0",
                    type.GetProperty("assembly").GetString()!,
                    type.GetProperty("definitionId").GetString()!,
                    type.GetProperty("queryId").GetString()!,
                    member.GetProperty("name").GetString()!,
                    member.GetProperty("signature").GetString()!,
                    member.GetProperty("graphSelectorKey")
                        .GetString()!,
                    member.GetProperty("metadataToken").GetInt32(),
                    "[]");

        using JsonDocument censusDocument =
            JsonDocument.Parse(censusJson);
        JsonElement annotatedSource =
            censusDocument.RootElement
                .GetProperty("annotatedSource");
        JsonElement completions = annotatedSource
            .GetProperty("viewerCatalog")
            .GetProperty("synchronousCompletions");
        Assert.True(
            completions.GetProperty("available").GetBoolean());
        JsonElement observation = Assert.Single(
            completions.GetProperty("observations").EnumerateArray());
        Assert.Equal(
            "TaskAwaiterGetResult",
            observation.GetProperty("kind").GetString());
        int factId = observation.GetProperty("factId").GetInt32();
        JsonElement relationship = Assert.Single(
            annotatedSource.GetProperty("callRelationships")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("factId").GetInt32()
                    == factId);
        Assert.Equal(
            "GetResult",
            relationship.GetProperty("target")
                .GetProperty("memberName").GetString());
        Assert.DoesNotContain(
            factId,
            annotatedSource
                .GetProperty("viewerCatalog")
                .GetProperty("defaultFindingIds")
                .EnumerateArray()
                .Select(value => value.GetInt32()));
    }

    [Fact]
    public async Task MemberFindingCensus_ProjectsClassicAwaitCompletionPaths()
    {
        const string PackageId = "Browser.Member.AwaitCompletionPaths";
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphTarget
                .AssemblyPath());
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await QueryPackageSurfaceJson(
            PackageId,
            "1.0.0",
            "net11.0");
        using JsonDocument surfaceDocument =
            JsonDocument.Parse(surfaceJson);
        JsonElement type = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                    == "Target.AwaitCompletionPathApi");
        JsonElement member = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                    == "One");

        string censusJson =
            await DotnetInspect.Web.Interop.Source.SourceExports
                .QueryMemberFindingCensus(
                    PackageId,
                    "1.0.0",
                    "net11.0",
                    type.GetProperty("assembly").GetString()!,
                    type.GetProperty("definitionId").GetString()!,
                    type.GetProperty("queryId").GetString()!,
                    member.GetProperty("name").GetString()!,
                    member.GetProperty("signature").GetString()!,
                    member.GetProperty("graphSelectorKey")
                        .GetString()!,
                    member.GetProperty("metadataToken").GetInt32(),
                    "[]");

        using JsonDocument censusDocument =
            JsonDocument.Parse(censusJson);
        JsonElement annotatedSource =
            censusDocument.RootElement
                .GetProperty("annotatedSource");
        JsonElement completionPaths = annotatedSource
            .GetProperty("viewerCatalog")
            .GetProperty("awaitCompletionPaths");
        Assert.True(
            completionPaths.GetProperty("available").GetBoolean());
        JsonElement[] observations =
            completionPaths.GetProperty("observations")
                .EnumerateArray()
                .ToArray();
        Assert.Single(observations);
        int[] nodeIds =
        [
            .. observations.Select(observation =>
                observation.GetProperty("nodeId").GetInt32()),
        ];
        Assert.Single(nodeIds.Distinct());
        JsonElement[] nodes = annotatedSource
            .GetProperty("document")
            .GetProperty("nodes")
            .EnumerateArray()
            .ToArray();
        Assert.All(nodeIds, nodeId =>
        {
            Assert.Equal(
                "AwaitExpression",
                nodes[nodeId].GetProperty("kind").GetString());
            Assert.Equal(
                "CSharp",
                nodes[nodeId].GetProperty("medium").GetString());
        });
    }

    [Fact]
    public async Task MemberFindingCensus_ProjectsAllocationExceptionPath()
    {
        const string PackageId =
            "Browser.Member.AllocationExceptionPath";
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphTarget
                .AssemblyPath());
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await QueryPackageSurfaceJson(
            PackageId,
            "1.0.0",
            "net11.0");
        using JsonDocument surfaceDocument =
            JsonDocument.Parse(surfaceJson);
        JsonElement type = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                    == "Target.AllocationExceptionPathApi");
        JsonElement member = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                    == "ExceptionHandler");

        string censusJson =
            await DotnetInspect.Web.Interop.Source.SourceExports
                .QueryMemberFindingCensus(
                    PackageId,
                    "1.0.0",
                    "net11.0",
                    type.GetProperty("assembly").GetString()!,
                    type.GetProperty("definitionId").GetString()!,
                    type.GetProperty("queryId").GetString()!,
                    member.GetProperty("name").GetString()!,
                    member.GetProperty("signature").GetString()!,
                    member.GetProperty("graphSelectorKey")
                        .GetString()!,
                    member.GetProperty("metadataToken").GetInt32(),
                    "[]");

        using JsonDocument censusDocument =
            JsonDocument.Parse(censusJson);
        JsonElement annotatedSource =
            censusDocument.RootElement
                .GetProperty("annotatedSource");
        JsonElement exceptionPaths = annotatedSource
            .GetProperty("viewerCatalog")
            .GetProperty("allocationExceptionPaths");
        Assert.True(
            exceptionPaths.GetProperty("available").GetBoolean());
        JsonElement observation = Assert.Single(
            exceptionPaths.GetProperty("observations")
                .EnumerateArray());
        Assert.Equal(
            "ExceptionHandler",
            observation.GetProperty("kind").GetString());
        int factId = observation.GetProperty("factId").GetInt32();
        JsonElement fact = annotatedSource
            .GetProperty("document")
            .GetProperty("facts")[factId];
        Assert.Equal(
            "alloc.new",
            fact.GetProperty("descriptor").GetString());
        Assert.Contains(
            annotatedSource
                .GetProperty("document")
                .GetProperty("targets")
                .EnumerateArray(),
            target =>
                target.GetProperty("fact_id").GetInt32()
                    == factId);
    }

    [Fact]
    public async Task MemberFindingCensus_ProjectsBoundedLocalThrowPath()
    {
        const string PackageId = "Browser.Member.LocalThrowPath";
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerGraphTarget
                .AssemblyPath());
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await QueryPackageSurfaceJson(
            PackageId,
            "1.0.0",
            "net11.0");
        using JsonDocument surfaceDocument =
            JsonDocument.Parse(surfaceJson);
        JsonElement type = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                    == "Target.LocalThrowPathApi");
        JsonElement member = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                    == "Entry");

        string censusJson =
            await DotnetInspect.Web.Interop.Source.SourceExports
                .QueryMemberFindingCensus(
                    PackageId,
                    "1.0.0",
                    "net11.0",
                    type.GetProperty("assembly").GetString()!,
                    type.GetProperty("definitionId").GetString()!,
                    type.GetProperty("queryId").GetString()!,
                    member.GetProperty("name").GetString()!,
                    member.GetProperty("signature").GetString()!,
                    member.GetProperty("graphSelectorKey")
                        .GetString()!,
                    member.GetProperty("metadataToken").GetInt32(),
                    "[]");

        using JsonDocument censusDocument =
            JsonDocument.Parse(censusJson);
        JsonElement annotatedSource =
            censusDocument.RootElement
                .GetProperty("annotatedSource");
        JsonElement inspection = annotatedSource
            .GetProperty("viewerCatalog")
            .GetProperty("localThrowPaths");
        Assert.True(inspection.GetProperty("available").GetBoolean());
        JsonElement[] paths = inspection.GetProperty("paths")
            .EnumerateArray()
            .ToArray();
        Assert.True(paths.Length == 1, inspection.GetRawText());
        JsonElement path = paths[0];
        int[] factIds =
        [
            .. path.GetProperty("factIds")
                .EnumerateArray()
                .Select(value => value.GetInt32()),
        ];
        Assert.Equal(2, factIds.Length);
        JsonElement[] relationships = annotatedSource
            .GetProperty("callRelationships")
            .EnumerateArray()
            .Where(relationship =>
                relationship.GetProperty("target")
                    .GetProperty("memberName").GetString()
                    == "Forward")
            .ToArray();
        Assert.Equal(3, relationships.Length);
        Assert.Contains(
            relationships,
            relationship =>
                relationship.GetProperty("kind").GetString()
                    == "LoadFunction");
        Assert.All(
            relationships.Where(relationship =>
                factIds.Contains(
                    relationship.GetProperty("factId").GetInt32())),
            relationship => Assert.Equal(
                "Call",
                relationship.GetProperty("kind").GetString()));
        Assert.Single(
            relationships
                .Select(relationship =>
                    relationship.GetProperty("edgeRow").GetInt32())
                .Distinct());
        Assert.Equal(
            "Throw",
            path.GetProperty("targets")
                .EnumerateArray()
                .Last()
                .GetProperty("memberName").GetString());
        JsonElement terminal = Assert.Single(
            path.GetProperty("terminalThrows").EnumerateArray());
        Assert.EndsWith(
            ".LocalThrowPathException",
            terminal.GetProperty("exceptionType").GetString(),
            StringComparison.Ordinal);
        Assert.True(
            terminal.GetProperty("constructionOffset").GetInt32()
                < terminal.GetProperty("throwOffset").GetInt32());
        Assert.Equal(
            0x02000000,
            terminal.GetProperty("definitionToken").GetInt32()
                & 0xFF000000);
        Assert.All(factIds, factId =>
            Assert.Contains(
                relationships,
                relationship =>
                    relationship.GetProperty("factId").GetInt32()
                        == factId));
        Assert.DoesNotContain(
            factIds,
            factId => annotatedSource
                .GetProperty("viewerCatalog")
                .GetProperty("defaultFindingIds")
                .EnumerateArray()
                .Any(value => value.GetInt32() == factId));
    }

    [Fact]
    public async Task MemberFindingCensus_ProjectsMethodLevelCostEvidence()
    {
        const string PackageId = "Browser.Member.CostCalleeEvidence";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await QueryPackageSurfaceJson(
            PackageId,
            "1.0.0",
            "net11.0");
        using JsonDocument surfaceDocument = JsonDocument.Parse(surfaceJson);
        JsonElement type = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                == typeof(BrowserEngineBoundaryTests).FullName);
        JsonElement member = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(CostCalleeEvidenceProbe));

        string censusJson =
            await DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberFindingCensus(
                PackageId,
                "1.0.0",
                "net11.0",
                type.GetProperty("assembly").GetString()!,
                type.GetProperty("definitionId").GetString()!,
                type.GetProperty("queryId").GetString()!,
                member.GetProperty("name").GetString()!,
                member.GetProperty("signature").GetString()!,
                member.GetProperty("graphSelectorKey").GetString()!,
                member.GetProperty("metadataToken").GetInt32(),
                "[]");
        using JsonDocument censusDocument = JsonDocument.Parse(censusJson);
        JsonElement root = censusDocument.RootElement;
        JsonElement annotatedSource = root.GetProperty("annotatedSource");
        JsonElement evidence = Assert.Single(
            annotatedSource
                .GetProperty("findingEvidence")
                .EnumerateArray());
        int instanceKey = evidence.GetProperty("instanceKey").GetInt32();
        JsonElement fact = Assert.Single(
            root.GetProperty("facts").EnumerateArray(),
            candidate =>
                candidate.GetProperty("instanceKey").ValueKind
                    == JsonValueKind.Number
                && candidate.GetProperty("instanceKey").GetInt32()
                    == instanceKey);

        Assert.Equal("cost.callee", fact.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Number, fact.GetProperty("ilOffset").ValueKind);
        Assert.Equal("Method", evidence.GetProperty("state").GetString());
        Assert.Equal(
            nameof(PerformanceAllocationInLoopProbe),
            evidence.GetProperty("target")
                .GetProperty("memberName")
                .GetString());
        JsonElement input = Assert.Single(
            evidence.GetProperty("aggregateInputs").EnumerateArray());
        Assert.Equal(
            "AllocationInLoop",
            input.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, input.GetProperty("value").ValueKind);
        Assert.Empty(evidence.GetProperty("coordinates").EnumerateArray());
        Assert.Equal(
            JsonValueKind.Null,
            evidence.GetProperty("documentId").ValueKind);
        Assert.Empty(evidence.GetProperty("nodeIds").EnumerateArray());
        Assert.Equal(
            JsonValueKind.Null,
            evidence.GetProperty("unavailableReason").ValueKind);
        Assert.Empty(
            annotatedSource
                .GetProperty("findingEvidenceDocuments")
                .EnumerateArray());
    }

    [Fact]
    public async Task GraphMemberSurface_UsesSurfaceAssetForImplementationOnlyType()
    {
        const string PackageId = "Browser.Graph.Internal.Pair";
        const string AssemblyName = "DotnetInspect.Web.Tests";
        byte[] implementation = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] surface = BuildEmptySurfaceImage(
            typeof(BrowserEngineBoundaryTests).Assembly.GetName());
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    surface,
                    implementation,
                    $"{AssemblyName}.dll"),
                fromCache: false));
        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                PackageId,
                "1.0.0",
                "net11.0",
                TestContext.Current.CancellationToken);
        PackageCompileAsset surfaceAsset =
            Assert.IsType<PackageCompileAsset>(coordinate.DefaultAsset);
        MethodInfo method = typeof(BrowserEngineBoundaryTests).GetMethod(
            nameof(InvocationDestinationTarget),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Missing {nameof(InvocationDestinationTarget)}.");

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string json = await DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryGraphMemberSurface(
                PackageId,
                "1.0.0",
                "net11.0",
                surfaceAsset.Id,
                typeof(BrowserEngineBoundaryTests).FullName!,
                method.Name,
                "stale-selector",
                method.MetadataToken);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement type = document.RootElement.GetProperty("type");

            Assert.Equal(
                surfaceAsset.Id,
                type.GetProperty("assemblyId").GetString());
            Assert.StartsWith(
                "compile:ref/net11.0/",
                surfaceAsset.Id,
                StringComparison.Ordinal);
            Assert.Equal(
                typeof(BrowserEngineBoundaryTests).FullName,
                type.GetProperty("definitionId").GetString());
            Assert.Equal(
                $"{surfaceAsset.AssemblyName}:{typeof(BrowserEngineBoundaryTests).FullName}",
                type.GetProperty("id").GetString());
            Assert.Single(type.GetProperty("api").EnumerateArray());

            string declarationJson =
                await DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryMemberDeclaration(
                    PackageId,
                    "1.0.0",
                    "net11.0",
                    surfaceAsset.Id,
                    typeof(BrowserEngineBoundaryTests).FullName!,
                    method.Name,
                    "stale-selector",
                    method.MetadataToken,
                    implementationMember: true);
            using JsonDocument declarationDocument =
                JsonDocument.Parse(declarationJson);
            Assert.Equal(
                JsonValueKind.String,
                declarationDocument.RootElement
                    .GetProperty("text").ValueKind);
            Assert.Equal(
                JsonValueKind.Null,
                declarationDocument.RootElement
                    .GetProperty("unavailable").ValueKind);
        }
    }

    [Fact]
    public async Task PackagePerformance_ExcludesMembersWithoutANavigableSurface()
    {
        const string PackageId = "Browser.Performance.Reference";
        byte[] extraImplementation = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] pairedImplementation = File.ReadAllBytes(
            typeof(BrowserPackage).Assembly.Location);
        byte[] surface = BuildEmptySurfaceImage(
            typeof(BrowserPackage).Assembly.GetName());
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePairWithExtraImplementation(
                    surface,
                    pairedImplementation,
                    "DotnetInspect.Web.dll",
                    extraImplementation,
                    "DotnetInspect.Web.Tests.dll"),
                fromCache: false));

        string json = await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackagePerformance(
            PackageId,
            "1.0.0",
            "net11.0",
            "DotnetInspect.Web.Tests.dll");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.True(
            root.GetProperty("totalOpportunities").GetInt32() > 0);
        Assert.Empty(
            root.GetProperty("members").EnumerateArray());
    }

    [Fact]
    public async Task PackagePerformance_ReportsSurfaceTruncation()
    {
        const string PackageId = "Browser.Performance.Truncated";
        byte[] image = BuildTransportAmplificationImage(
            PackageId,
            typeCount: 10_000,
            namespaceLength: 1_000);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                Package(
                    image,
                    $"lib/net11.0/{PackageId}.dll"),
                fromCache: false));

        string json = await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackagePerformance(
            PackageId,
            "1.0.0",
            "net11.0",
            $"{PackageId}.dll");

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Contains(
            "truncated",
            document.RootElement
                .GetProperty("inspectionError")
                .GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceMemberLimit_ReportsOnlyActualTruncation()
    {
        static BrowserPerformanceMember Member(int index) =>
            new(
                "Example.dll",
                $"Example.Type{index}",
                "Run",
                $"Run~{index}",
                [0x06000001 + index],
                1,
                0,
                ["box-value-type"],
                "high");

        var exactFailures = new List<string>();
        BrowserPerformanceMember[] exact =
            DotnetInspect.Web.Interop.Analysis.AnalysisExports.ApplyPerformanceMemberLimit(
                Enumerable.Range(0, 200).Select(Member),
                exactFailures);
        var truncatedFailures = new List<string>();
        BrowserPerformanceMember[] truncated =
            DotnetInspect.Web.Interop.Analysis.AnalysisExports.ApplyPerformanceMemberLimit(
                Enumerable.Range(0, 201).Select(Member),
                truncatedFailures);

        Assert.Equal(200, exact.Length);
        Assert.Empty(exactFailures);
        Assert.Equal(200, truncated.Length);
        Assert.Single(truncatedFailures);
        Assert.Contains(
            "truncated",
            truncatedFailures[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public void MermaidLabel_ContainsGrammarSignificantArtifactText()
    {
        string encoded = BrowserCallGraphProjection.MermaidLabel(
            "A\"B\n<x>&\\\u2028\u202E\u200D\uD800X\uDC00\U000E0001-Caf\u00E9\U0001F600");

        Assert.Equal(
            "A&quot;B&#92;u000A&lt;x&gt;&amp;&#92;&#92;u2028"
                + "&#92;u202E&#92;u200D&#92;uD800X&#92;uDC00"
                + "&#92;uDB40&#92;uDC01-Caf\u00E9\U0001F600",
            encoded);
        Assert.DoesNotContain('"', encoded);
        Assert.DoesNotContain('\n', encoded);
        Assert.DoesNotContain('<', encoded);
        Assert.DoesNotContain('>', encoded);
        Assert.DoesNotContain('\\', encoded);
        Assert.DoesNotContain('\u2028', encoded);
        Assert.DoesNotContain('\u202E', encoded);
        Assert.DoesNotContain('\u200D', encoded);
        Assert.DoesNotContain('\uD800', encoded);
        Assert.DoesNotContain('\uDC00', encoded);
        Assert.EndsWith("-Caf\u00E9\U0001F600", encoded, StringComparison.Ordinal);
    }
}
