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
using CSharpText;
using CSharpText.MemberSlicing;
using DotnetInspector.Ecosystems;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using InertText;
using Inspector.Findings;
using ILInspector.Metadata;
using InspectWeb.MethodBodyFixtures;
using NuGetFetch;

using BrowserMemberDocument =
    DotnetInspect.Web.Interop.Metadata.BrowserMemberDocument;
using BrowserMemberDocumentInspection =
    DotnetInspect.Web.Interop.Metadata.BrowserMemberDocumentInspection;
using BrowserMemberGroupDocument =
    DotnetInspect.Web.Interop.Metadata.BrowserMemberGroupDocument;
using BrowserMemberGroupDocumentInspection =
    DotnetInspect.Web.Interop.Metadata.BrowserMemberGroupDocumentInspection;
using BrowserMemberGroupDocumentRow =
    DotnetInspect.Web.Interop.Metadata.BrowserMemberGroupDocumentRow;
using BrowserTypeMemberPopulation =
    DotnetInspect.Web.Interop.Metadata.BrowserTypeMemberPopulation;
using BrowserTypeMemberPopulationInspection =
    DotnetInspect.Web.Interop.Metadata.BrowserTypeMemberPopulationInspection;
using BrowserTypeMemberPopulationOutcome =
    DotnetInspect.Web.Interop.Metadata.BrowserTypeMemberPopulationOutcome;
using BrowserTypeOverviewInspection =
    DotnetInspect.Web.Interop.Metadata.BrowserTypeOverviewInspection;
using BrowserTypeOverviewOutcome =
    DotnetInspect.Web.Interop.Metadata.BrowserTypeOverviewOutcome;
using AttachedDocumentationQueryOutcome =
    DotnetInspect.Web.Interop.Metadata.Wire.DocumentationQueryOutcome;
using MetadataExports =
    DotnetInspect.Web.Interop.Metadata.MetadataExports;

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
using BrowserLibraryMetrics = DotnetInspect.Web.Interop.Analysis.BrowserLibraryMetrics;
using BrowserLibraryMetricsPopulation =
    DotnetInspect.Web.Interop.Analysis.BrowserLibraryMetricsPopulation;
using BrowserLibraryDependencyStructure =
    DotnetInspect.Web.Interop.Analysis.BrowserLibraryDependencyStructure;
using BrowserLibraryDependencyTypeEdge =
    DotnetInspect.Web.Interop.Analysis.BrowserLibraryDependencyTypeEdge;
using BrowserLibraryStructuralSalience = DotnetInspect.Web.Interop.Analysis.BrowserLibraryStructuralSalience;
using BrowserLibraryTypeLeverageShard = DotnetInspect.Web.Interop.Analysis.BrowserLibraryTypeLeverageShard;
using LibraryDependencyStructure =
    ILInspector.Research.LibraryDependencyStructure;
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
    public void PackageDocumentDiscovery_UsesOneCachedEntryManifestAtTheLimit()
    {
        const int maxEntries = 4_096;
        var package = new BrowserPackage(
            "Document.Limit",
            "1.0.0",
            PackageDocuments(maxEntries),
            fromCache: false);

        IReadOnlyList<BrowserPackageDocumentEntry> documents = package.Documents();

        Assert.Equal(maxEntries, documents.Count);
        Assert.Same(
            Assert.IsType<InMemoryPackageContent>(package.Content).EnumerateEntriesWithLengths(),
            Assert.IsType<InMemoryPackageContent>(package.Content).EnumerateEntriesWithLengths());
    }

    [Fact]
    public void PackageIcon_ProjectsOnlyTheBoundedEmbeddedAsset()
    {
        const string packageId = "Icon.Package";
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var package = new BrowserPackage(
            packageId,
            "1.0.0",
            PackageEntries(
                ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                    $"""
                    <package>
                      <metadata>
                        <id>{packageId}</id>
                        <version>1.0.0</version>
                        <authors>Example</authors>
                        <description>Example</description>
                        <icon>images\icon.png</icon>
                        <iconUrl>https://example.test/legacy.png</iconUrl>
                      </metadata>
                    </package>
                    """)),
                ("images/icon.png", png)),
            fromCache: false);

        Assert.NotNull(package.Icon);
        BrowserPackageIconPayload icon = package.Icon;

        Assert.Equal("image/png", icon.MediaType);
        Assert.Equal(png, Convert.FromBase64String(icon.Base64));
        Assert.DoesNotContain("example.test", icon.Base64, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageIcon_UsesNoRemoteManifestFallback()
    {
        const string packageId = "Legacy.Icon.Package";
        var package = new BrowserPackage(
            packageId,
            "1.0.0",
            PackageEntries(
                ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                    $"""
                    <package>
                      <metadata>
                        <id>{packageId}</id>
                        <version>1.0.0</version>
                        <authors>Example</authors>
                        <description>Example</description>
                        <iconUrl>https://example.test/legacy.png</iconUrl>
                      </metadata>
                    </package>
                    """))),
            fromCache: false);

        Assert.Null(package.Icon);
    }

    [Fact]
    public void PackageWireProjection_PreservesCoreValues()
    {
        var stats = new BrowserPackageCacheSnapshot(
            1,
            2,
            12,
            3,
            4,
            256,
            5,
            128,
            64,
            "persistent",
            7,
            8,
            null);
        var entry = new BrowserPackageDocumentEntry(
            "skill",
            "Inspect",
            "skills/inspect/SKILL.md",
            5);
        var payload = new BrowserPackageDocumentPayload(
            entry.Kind,
            entry.Name,
            entry.Path,
            "# Inspect");
        var icon = new BrowserPackageIconPayload(
            "image/png",
            "cG5n");

        Assert.Equal(
            new BrowserPackageCacheStats(
                1,
                2,
                12,
                3,
                4,
                256,
                5,
                128,
                64,
                "persistent",
                7,
                8,
                null),
            BrowserPackageWireProjection.Project(stats));
        Assert.Equal(
            [
                new BrowserPackageDocument(
                    entry.Kind,
                    entry.Name,
                    entry.Path,
                    entry.Size),
            ],
            BrowserPackageWireProjection.Project([entry]));
        Assert.Equal(
            new BrowserPackageDocumentContent(
                payload.Kind,
                payload.Name,
                payload.Path,
                payload.Text),
            BrowserPackageWireProjection.Project(payload));
        Assert.Equal(
            new BrowserPackageIcon(
                icon.MediaType,
                icon.Base64),
            BrowserPackageWireProjection.Project(icon));
        Assert.Null(BrowserPackageWireProjection.Project(
            (BrowserPackageIconPayload?)null));
    }

    [Fact]
    public void PackageRuntimePopulationWireProjectionPreservesChoiceState()
    {
        PackageRuntimePopulationChoiceResult selectedChoice = Choice(
            "runtimes/win-x64/lib/net10.0/Windows.dll",
            "runtimes/linux-x64/lib/net10.0/Linux.dll");
        BrowserPackageRuntimePopulationChoice selected =
            Assert.IsType<BrowserPackageRuntimePopulationChoice>(
                BrowserPackageWireProjection.Project(selectedChoice));

        Assert.Equal(
            BrowserPackageRuntimePopulationChoiceStatus.Selected,
            selected.Status);
        Assert.Equal("linux-x64", selected.SelectedRuntimeIdentifier);
        Assert.Equal(
            ["linux-x64", "win-x64"],
            selected.AvailableRuntimeIdentifiers);

        PackageRuntimePopulationChoiceResult ambiguousChoice = Choice(
            "runtimes/win-x64/lib/net10.0/Windows.dll",
            "runtimes/osx-arm64/lib/net10.0/Mac.dll");
        BrowserPackageRuntimePopulationChoice ambiguous =
            Assert.IsType<BrowserPackageRuntimePopulationChoice>(
                BrowserPackageWireProjection.Project(ambiguousChoice));

        Assert.Equal(
            BrowserPackageRuntimePopulationChoiceStatus.Ambiguous,
            ambiguous.Status);
        Assert.Null(ambiguous.SelectedRuntimeIdentifier);
        Assert.Equal(
            ["osx-arm64", "win-x64"],
            ambiguous.AvailableRuntimeIdentifiers);
        Assert.Null(BrowserPackageWireProjection.Project(
            (PackageRuntimePopulationChoiceResult?)null));

        static PackageRuntimePopulationChoiceResult Choice(
            params string[] entries) =>
            PackageRuntimePopulationChoice.Evaluate(
                new PackageRuntimePopulationChoiceRequest(
                    PackageRuntimePopulationChoiceOperationAssociation
                        .Create(),
                    PackageSourceCoordinate.Create(
                        "runtime.projection",
                        "1.0.0"),
                    PackageAssetSelector.EvaluateSingleRuntime(
                        new InMemoryPackageContent(
                            PackageEntries(
                                entries.Select(entry =>
                                    (entry, Array.Empty<byte>()))
                                .ToArray()),
                            fromCache: true,
                            "test-source")),
                    new PackageRuntimePopulationAuthority.None()));
    }

    [Fact]
    public void PackageDocumentLeaves_PreserveIdentityPolicyAndInertBrand()
    {
        const string leavesJson =
            """
            [
              { "identity": 2, "policy": "Prose", "text": "line\u202Eone" },
              { "identity": 1, "policy": "Field", "text": "field\nvalue" }
            ]
            """;

        string json = PackageExports.ContainPackageDocumentLeaves(
            "document-11",
            leavesJson);
        BrowserDocumentLeafContainmentOutcome outcome =
            JsonSerializer.Deserialize(
                json,
                BrowserPackageJsonContext.Default
                    .BrowserDocumentLeafContainmentOutcome)
            ?? throw new InvalidOperationException(
                "The contained leaf outcome was absent.");
        BrowserDocumentLeafContainmentContained contained =
            Assert.IsType<BrowserDocumentLeafContainmentContained>(outcome);

        Assert.Equal("document-11", contained.Generation);
        Assert.Collection(
            contained.Leaves,
            leaf =>
            {
                Assert.Equal(2, leaf.Identity);
                Assert.Equal(BrowserDocumentTextPolicy.Prose, leaf.Policy);
                Assert.Equal("line\\u202Eone", leaf.Text.ToString());
            },
            leaf =>
            {
                Assert.Equal(1, leaf.Identity);
                Assert.Equal(BrowserDocumentTextPolicy.Field, leaf.Policy);
                Assert.Equal("field\\^Jvalue", leaf.Text.ToString());
            });
    }

    [Fact]
    public void PackageDocumentLeaves_PreserveTypedRejection()
    {
        const string generation = "document-12";
        const string leavesJson =
            """[{"identity":9,"policy":99,"text":"value"}]""";

        string json = PackageExports.ContainPackageDocumentLeaves(
            generation,
            leavesJson);
        BrowserDocumentLeafContainmentRejected rejected =
            Assert.IsType<BrowserDocumentLeafContainmentRejected>(
                JsonSerializer.Deserialize(
                    json,
                    BrowserPackageJsonContext.Default
                        .BrowserDocumentLeafContainmentOutcome));

        Assert.Equal(generation, rejected.Generation);
        Assert.Equal(
            BrowserDocumentLeafContainmentFailureKind.UnsupportedTextPolicy,
            rejected.Failure.Kind);
        Assert.Equal(9, rejected.Failure.Identity);
    }

    [Fact]
    public void PackageDocumentLeaves_AcceptMaximumIdentity()
    {
        const string leavesJson =
            """[{"identity":2147483647,"policy":"Field","text":"maximum"}]""";

        string json = PackageExports.ContainPackageDocumentLeaves(
            "document-13",
            leavesJson);
        BrowserDocumentLeafContainmentContained contained =
            Assert.IsType<BrowserDocumentLeafContainmentContained>(
                JsonSerializer.Deserialize(
                    json,
                    BrowserPackageJsonContext.Default
                        .BrowserDocumentLeafContainmentOutcome));

        BrowserContainedDocumentPresentationLeaf leaf =
            Assert.Single(contained.Leaves);
        Assert.Equal(int.MaxValue, leaf.Identity);
        Assert.Equal("maximum", leaf.Text.ToString());
    }

    [Fact]
    public async Task
        QueryMemberDocumentation_DottedAssemblyNameUsesSharedContract()
    {
        const string packageId = "System.Text.Json";
        const string version = "10.0.0";
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
            new BrowserPackage(
                packageId,
                version,
                File.ReadAllBytes(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "RealAssets",
                        "FrameworkActivation",
                        "system.text.json.10.0.0.nupkg")),
                fromCache: false,
                producerKey:
                    BrowserPackageWorkspace.Gallery.Source.Producer.Key));

        string json =
            await DotnetInspect.Web.Interop.Package.PackageExports
                .QueryMemberDocumentation(
                    packageId,
                    version,
                    "net10.0",
                    "System.Text.Json",
                    "M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.JsonSerializerOptions)");
        DocumentationQueryOutcome outcome =
            Assert.IsAssignableFrom<DocumentationQueryOutcome>(
                JsonSerializer.Deserialize(
                    json,
                    DocumentationQueryJsonContext.Default
                        .DocumentationQueryOutcome));

        var completed =
            Assert.IsType<DocumentationQueryOutcome.Completed>(
                outcome);
        var available =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                completed.CompiledXml);
        Assert.Equal(
            CompiledDocumentationSourceKind.Package,
            available.Source.Kind);
        Assert.Contains(
            "Converts the JsonDocument",
            available.Documentation.Summary,
            StringComparison.Ordinal);
        Assert.IsType<AuthoredDocumentationOutcome.Unavailable>(
            completed.AuthoredSource);
        Assert.Equal(
            DocumentationQueryFieldEvidenceKind.Selected,
            completed.Fields.Summary.Kind);
    }

    [Fact]
    public async Task
        QueryPlatformMemberDocumentation_UsesSharedPlatformContract()
    {
        CompiledDocumentationOutcome outcome =
            await QueryPlatformMemberDocumentationAsync(
                "11.0.7146",
                includeDocumentation: true);

        var available =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                outcome);
        Assert.Equal(
            CompiledDocumentationSourceKind.Platform,
            available.Source.Kind);
        Assert.Equal(
            "Reads documentation from a non-public type.",
            available.Documentation.Summary);
        string json = JsonSerializer.Serialize(
            outcome,
            CompiledDocumentationQueryJsonContext.Default
                .CompiledDocumentationOutcome);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            ["documentation", "kind", "source", "subject"],
            document.RootElement.EnumerateObject()
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.True(
            json.Length <= 4_096,
            $"Platform documentation JSON was {json.Length} UTF-16 code units.");
    }

    [Fact]
    public async Task
        QueryPlatformMemberDocumentation_MissingCompanionIsAuthoritativeAbsence()
    {
        CompiledDocumentationOutcome outcome =
            await QueryPlatformMemberDocumentationAsync(
                "11.0.7147",
                includeDocumentation: false);

        var absent =
            Assert.IsType<CompiledDocumentationOutcome.Absent>(
                outcome);
        CompiledDocumentationSourceEvidence source =
            Assert.Single(absent.Sources);
        Assert.Equal(
            CompiledDocumentationSourceKind.Platform,
            source.Source.Kind);
        Assert.Equal(
            CompiledDocumentationSourceEvidenceKind.Absent,
            source.Kind);
        string json = JsonSerializer.Serialize(
            outcome,
            CompiledDocumentationQueryJsonContext.Default
                .CompiledDocumentationOutcome);
        Assert.True(
            json.Length <= 1_024,
            $"Platform absence JSON was {json.Length} UTF-16 code units.");
    }

    [Fact]
    public async Task
        QueryUnifiedPlatformMemberDocumentation_UsesSharedSettlement()
    {
        DocumentationQueryOutcome outcome =
            await QueryUnifiedPlatformMemberDocumentationAsync(
                "11.0.7148",
                includeDocumentation: true);

        var completed =
            Assert.IsType<DocumentationQueryOutcome.Completed>(outcome);
        Assert.Contains(
            completed.Fields.Summary.Contributions,
            static contribution =>
                contribution.Value
                    == "Reads documentation from a non-public type.");
    }

    [Fact]
    public async Task
        QueryMemberDocumentation_BrowserAdmittedLargeSurfaceReturnsAvailable()
    {
        const string packageId =
            "Microsoft.FluentUI.AspNetCore.Components.Icons";
        const string version = "4.1.0";
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
            new BrowserPackage(
                packageId,
                version,
                File.ReadAllBytes(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "RealAssets",
                        "Documentation",
                        "microsoft.fluentui.aspnetcore.components.icons.4.1.0.nupkg")),
                fromCache: false,
                producerKey:
                    BrowserPackageWorkspace.Gallery.Source.Producer.Key));

        string json =
            await DotnetInspect.Web.Interop.Package.PackageExports
                .QueryMemberDocumentation(
                    packageId,
                    version,
                    "net8.0",
                    "Microsoft.FluentUI.AspNetCore.Components.Icons.dll",
                    "M:Microsoft.FluentUI.AspNetCore.Components.Icons.GetInstance(Microsoft.FluentUI.AspNetCore.Components.IconInfo)");
        DocumentationQueryOutcome outcome =
            Assert.IsAssignableFrom<DocumentationQueryOutcome>(
                JsonSerializer.Deserialize(
                    json,
                    DocumentationQueryJsonContext.Default
                        .DocumentationQueryOutcome));

        var completed =
            Assert.IsType<DocumentationQueryOutcome.Completed>(
                outcome);
        var available =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                completed.CompiledXml);
        Assert.Equal(
            "Returns a new instance of the icon.",
            available.Documentation.Summary);
    }

    [Fact]
    public async Task
        QueryMemberDocumentation_MissingCompanionIsAuthoritativeAbsence()
    {
        string packageId =
            $"Browser.Documentation.Absent.{Guid.NewGuid():N}";
        const string assemblyName =
            "DotnetInspect.Web.Interop.Package.dll";
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                PackageEntries(
                    ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                        $"""
                         <package>
                           <metadata>
                             <id>{packageId}</id>
                             <version>1.0.0</version>
                             <authors>Tests</authors>
                             <description>Package without compiled XML documentation.</description>
                           </metadata>
                         </package>
                         """)),
                    ($"lib/net10.0/{assemblyName}",
                        File.ReadAllBytes(
                            typeof(DotnetInspect.Web.Interop.Package
                                .PackageExports).Assembly.Location))),
                fromCache: false,
                producerKey:
                    BrowserPackageWorkspace.Gallery.Source.Producer.Key));

        string json =
            await DotnetInspect.Web.Interop.Package.PackageExports
                .QueryMemberDocumentation(
                    packageId,
                    "1.0.0",
                    "net10.0",
                    assemblyName,
                    "M:DotnetInspect.Web.Interop.Package.PackageExports.SearchTypes(System.String,System.String)");
        DocumentationQueryOutcome outcome =
            Assert.IsAssignableFrom<DocumentationQueryOutcome>(
                JsonSerializer.Deserialize(
                    json,
                    DocumentationQueryJsonContext.Default
                        .DocumentationQueryOutcome));

        var completed =
            Assert.IsType<DocumentationQueryOutcome.Completed>(
                outcome);
        var absent =
            Assert.IsType<CompiledDocumentationOutcome.Absent>(
                completed.CompiledXml);
        CompiledDocumentationSourceEvidence source =
            Assert.Single(absent.Sources);
        Assert.Equal(
            CompiledDocumentationSourceEvidenceKind.Absent,
            source.Kind);
        Assert.Equal(
            CompiledDocumentationSourceKind.Package,
            source.Source.Kind);
        Assert.IsType<AuthoredDocumentationOutcome.Unavailable>(
            completed.AuthoredSource);
    }

    [Fact]
    public async Task
        QueryMemberDocumentation_ReferenceOnlyLibraryRetainsCompiledDocumentation()
    {
        string packageId =
            $"Browser.Documentation.ReferenceOnly.{Guid.NewGuid():N}";
        const string assemblyName =
            "DotnetInspect.Web.Interop.Package.dll";
        const string summary =
            "Searches package types from a reference-only Library.";
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                PackageEntries(
                    ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                        $"""
                         <package>
                           <metadata>
                             <id>{packageId}</id>
                             <version>1.0.0</version>
                             <authors>Tests</authors>
                             <description>Reference-only documentation package.</description>
                           </metadata>
                         </package>
                         """)),
                    ($"ref/net11.0/{assemblyName}",
                        File.ReadAllBytes(
                            typeof(DotnetInspect.Web.Interop.Package
                                .PackageExports).Assembly.Location)),
                    ($"ref/net11.0/{Path.ChangeExtension(
                        assemblyName,
                        ".xml")}",
                        Encoding.UTF8.GetBytes(
                            $"""
                             <?xml version="1.0"?>
                             <doc>
                               <assembly>
                                 <name>DotnetInspect.Web.Interop.Package</name>
                               </assembly>
                               <members>
                                 <member name="M:DotnetInspect.Web.Interop.Package.PackageExports.SearchTypes(System.String,System.String)">
                                   <summary>{summary}</summary>
                                 </member>
                               </members>
                             </doc>
                             """))),
                fromCache: false,
                producerKey:
                    BrowserPackageWorkspace.Gallery.Source.Producer.Key));

        string json =
            await DotnetInspect.Web.Interop.Package.PackageExports
                .QueryMemberDocumentation(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    assemblyName,
                    "M:DotnetInspect.Web.Interop.Package.PackageExports.SearchTypes(System.String,System.String)");
        DocumentationQueryOutcome outcome =
            Assert.IsAssignableFrom<DocumentationQueryOutcome>(
                JsonSerializer.Deserialize(
                    json,
                    DocumentationQueryJsonContext.Default
                        .DocumentationQueryOutcome));

        var completed =
            Assert.IsType<DocumentationQueryOutcome.Completed>(
                outcome);
        var available =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                completed.CompiledXml);
        Assert.Equal(summary, available.Documentation.Summary);
        Assert.IsType<AuthoredDocumentationOutcome.Unavailable>(
            completed.AuthoredSource);
    }

    [Theory]
    [InlineData(
        "M:InspectWeb.DocumentationFixtures.HiddenDocumentedType.Read",
        "Reads documentation from a non-public type.",
        1)]
    [InlineData(
        "M:InspectWeb.DocumentationFixtures.WidgetExtensions.Measure(InspectWeb.DocumentationFixtures.Widget,System.Int32)",
        "Measures a widget through its declaring extension member.",
        2)]
    public async Task
        QueryMemberDocumentation_SelectableDeclarationShapesReturnAvailable(
            string documentationId,
            string expectedSummary,
            int expectedSurfaceOccurrences)
    {
        string packageId =
            $"Browser.Documentation.Shapes.{Guid.NewGuid():N}";
        byte[] packageBytes = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                $"""
                 <package>
                   <metadata>
                     <id>{packageId}</id>
                     <version>1.0.0</version>
                     <authors>Tests</authors>
                     <description>Inspect Web documentation declaration shapes.</description>
                   </metadata>
                 </package>
                 """)),
            ("lib/net11.0/InspectWeb.DocumentationFixtures.dll",
                File.ReadAllBytes(
                    FixtureCatalog.InspectWebDocumentation.AssemblyPath())),
            ("lib/net11.0/InspectWeb.DocumentationFixtures.xml",
                File.ReadAllBytes(
                    FixtureCatalog.InspectWebDocumentation.AssetPath(
                        "documentation"))));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                packageBytes,
                fromCache: false));

        BrowserPackageSurface surface = await QueryPackageSurface(
            packageId,
            "1.0.0",
            "net11.0");
        Assert.Equal(
            expectedSurfaceOccurrences,
            surface.Types
                .SelectMany(type => type.Api)
                .Count(member => member.DocumentationId == documentationId));

        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                packageBytes,
                fromCache: false,
                producerKey:
                    BrowserPackageWorkspace.Gallery.Source.Producer.Key));
        string json =
            await DotnetInspect.Web.Interop.Package.PackageExports
                .QueryMemberDocumentation(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    "InspectWeb.DocumentationFixtures.dll",
                    documentationId);
        DocumentationQueryOutcome outcome =
            Assert.IsAssignableFrom<DocumentationQueryOutcome>(
                JsonSerializer.Deserialize(
                    json,
                    DocumentationQueryJsonContext.Default
                        .DocumentationQueryOutcome));

        var completed =
            Assert.IsType<DocumentationQueryOutcome.Completed>(
                outcome);
        var available =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                completed.CompiledXml);
        Assert.Equal(expectedSummary, available.Documentation.Summary);
    }

    [Fact]
    public async Task
        QueryMemberDocument_AttachesExactDocumentationOutcome()
    {
        string packageId =
            $"Browser.MemberDocument.Documentation.{Guid.NewGuid():N}";
        byte[] packageBytes = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                $"""
                 <package>
                   <metadata>
                     <id>{packageId}</id>
                     <version>1.0.0</version>
                     <authors>Tests</authors>
                     <description>Exact Member documentation attachment.</description>
                   </metadata>
                 </package>
                 """)),
            ("lib/net11.0/InspectWeb.DocumentationFixtures.dll",
                File.ReadAllBytes(
                    FixtureCatalog.InspectWebDocumentation.AssemblyPath())),
            ("lib/net11.0/InspectWeb.DocumentationFixtures.xml",
                File.ReadAllBytes(
                    FixtureCatalog.InspectWebDocumentation.AssetPath(
                        "documentation"))));
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                packageBytes,
                fromCache: false,
                producerKey:
                    BrowserPackageWorkspace.Gallery.Source.Producer.Key));

        BrowserMemberGroupDocumentInspection group =
            JsonSerializer.Deserialize(
                await MetadataExports.QueryMemberGroupDocument(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    "InspectWeb.DocumentationFixtures.dll",
                    "InspectWeb.DocumentationFixtures.WidgetExtensions",
                    "Measure"),
                BrowserMetadataJsonContext.Default
                    .BrowserMemberGroupDocumentInspection)!;
        BrowserMemberGroupDocumentRow row =
            Assert.Single(
                Assert.IsType<BrowserMemberGroupDocument>(group.Document)
                    .Rows);

        BrowserMemberDocumentInspection inspection =
            JsonSerializer.Deserialize(
                await MetadataExports.QueryMemberDocument(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    "InspectWeb.DocumentationFixtures.dll",
                    "InspectWeb.DocumentationFixtures.WidgetExtensions",
                    "Measure",
                    row.BaselineOrdinal,
                    ""),
                BrowserMetadataJsonContext.Default
                    .BrowserMemberDocumentInspection)!;

        BrowserMemberDocument document =
            Assert.IsType<BrowserMemberDocument>(inspection.Document);
        var completed =
            Assert.IsType<AttachedDocumentationQueryOutcome.Completed>(
                document.Documentation);
        Assert.Contains(
            completed.Fields.Summary.Contributions,
            static contribution =>
                contribution.Value
                    == "Measures a widget through its declaring extension member.");
    }

    [Fact]
    public async Task
        QueryMemberDocumentation_PublicPackagePathPublishesAuthoredDocumentation()
    {
        string packageId =
            $"Browser.Documentation.Authored.{Guid.NewGuid():N}";
        string assemblyPath =
            typeof(MemberTextSlicer).Assembly.Location;
        byte[] packageBytes = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                $"""
                 <package>
                   <metadata>
                     <id>{packageId}</id>
                     <version>1.0.0</version>
                     <authors>Tests</authors>
                     <description>PDB-mapped browser documentation.</description>
                   </metadata>
                 </package>
                 """)),
            ("lib/net11.0/CSharpText.MemberSlicing.dll",
                File.ReadAllBytes(assemblyPath)),
            ("lib/net11.0/CSharpText.MemberSlicing.pdb",
                File.ReadAllBytes(
                    Path.ChangeExtension(assemblyPath, ".pdb"))));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                packageBytes,
                fromCache: false));
        BrowserPackageSurface surface = await QueryPackageSurface(
            packageId,
            "1.0.0",
            "net11.0");
        BrowserTypeSurface type = Assert.Single(
            surface.Types,
            candidate =>
                candidate.DefinitionId
                    == "CSharpText.MemberSlicing.MemberTextSlicer");
        string documentationId = Assert.Single(
                type.Api,
                member =>
                    member.Name == nameof(
                        MemberTextSlicer.ExtractMemberText))
            .DocumentationId!;
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                packageBytes,
                fromCache: false,
                producerKey:
                    BrowserPackageWorkspace.Gallery.Source.Producer.Key));
        string json =
            await PackageExports
                .QueryMemberDocumentation(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    "CSharpText.MemberSlicing.dll",
                    documentationId);
        DocumentationQueryOutcome outcome =
            Assert.IsAssignableFrom<DocumentationQueryOutcome>(
                JsonSerializer.Deserialize(
                    json,
                    DocumentationQueryJsonContext.Default
                        .DocumentationQueryOutcome));

        var completed =
            Assert.IsType<DocumentationQueryOutcome.Completed>(
                outcome);
        var authored =
            Assert.IsType<AuthoredDocumentationOutcome.Available>(
                completed.AuthoredSource);
        Assert.Contains(
            "Locates the declaration",
            authored.Documentation.Summary,
            StringComparison.Ordinal);
        Assert.Equal(
            DocumentationQueryFieldEvidenceKind.Selected,
            completed.Fields.Summary.Kind);
        DocumentationQueryTextFieldContribution contribution =
            Assert.Single(completed.Fields.Summary.Contributions);
        Assert.Equal(
            DocumentationQueryChannel.AuthoredSource,
            contribution.Channel);
    }

    [Fact]
    public async Task
        QueryMemberDocumentation_UsesRangeAndWarmEntryCache()
    {
        string packageId =
            $"Browser.Documentation.Ranged.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        const string assemblyName =
            "InspectWeb.DocumentationFixtures.dll";
        const string documentationId =
            "M:InspectWeb.DocumentationFixtures.WidgetExtensions.Measure"
            + "(InspectWeb.DocumentationFixtures.Widget,System.Int32)";
        string assemblyPath =
            FixtureCatalog.InspectWebDocumentation.AssemblyPath();
        byte[] packageBytes = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                $"""
                 <package>
                   <metadata>
                     <id>{packageId}</id>
                     <version>{version}</version>
                     <authors>Tests</authors>
                     <description>Ranged browser documentation.</description>
                   </metadata>
                 </package>
                 """)),
            ($"ref/net11.0/{assemblyName}",
                File.ReadAllBytes(assemblyPath)),
            ("ref/net11.0/InspectWeb.DocumentationFixtures.xml",
                File.ReadAllBytes(
                    FixtureCatalog.InspectWebDocumentation.AssetPath(
                        "documentation"))),
            ($"lib/net11.0/{assemblyName}",
                File.ReadAllBytes(assemblyPath)),
            ("lib/net11.0/InspectWeb.DocumentationFixtures.pdb",
                File.ReadAllBytes(
                    FixtureCatalog.InspectWebDocumentation.AssetPath(
                        "pdb"))),
            ("lib/net11.0/Unrelated.dll", new byte[2 * MiB]),
            ("content/padding.bin", new byte[2 * MiB]));
        Assert.True(packageBytes.Length > 4 * MiB);
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            packageBytes);
        using IPackageSourceClient source = Gallery(handler);
        var persistence = new MemoryPackageEntryPersistence();
        var store = new BrowserPackageWorkspace.BrowserSessionPackageStore(
            source,
            persistence);

        Task<DocumentationQueryOutcome> Read(
            BrowserPackageWorkspace.BrowserSessionPackageStore packageStore) =>
            BrowserPackageWorkspace.QueryMemberDocumentationAsync(
                packageId,
                version,
                "net11.0",
                assemblyName,
                documentationId,
                source,
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken,
                packageStore);

        DocumentationQueryOutcome first = await Read(store);
        int requests = handler.Requested.Count;
        Assert.True(
            persistence.ContainsEntry(
                "lib/net11.0/InspectWeb.DocumentationFixtures.pdb"));
        Assert.False(
            persistence.ContainsEntry(
                "lib/net11.0/Unrelated.dll"));
        var recreatedStore =
            new BrowserPackageWorkspace.BrowserSessionPackageStore(
                source,
                persistence);
        DocumentationQueryOutcome second = await Read(recreatedStore);

        foreach (DocumentationQueryOutcome outcome in new[] { first, second })
        {
            var completed =
                Assert.IsType<DocumentationQueryOutcome.Completed>(
                    outcome);
            var compiled =
                Assert.IsType<CompiledDocumentationOutcome.Available>(
                    completed.CompiledXml);
            Assert.Contains(
                "Measures a widget",
                compiled.Documentation.Summary,
                StringComparison.Ordinal);
        }
        Assert.Equal(requests, handler.Requested.Count);
        Assert.Equal(0, handler.OrdinaryPackageResponses);
        Assert.True(handler.RangedPackageResponses > 0);
        Assert.True(
            handler.PackageBytesServed < 512 * 1024,
            $"served {handler.PackageBytesServed} of "
                + $"{packageBytes.Length} package bytes");
    }

    [Fact]
    public void UnconstrainedDependencyNavigation_SelectsLatestStableVersion()
    {
        Assert.Equal(
            "3.0.0",
            BrowserPackageWorkspace.SelectDependencyVersion(
                ["1.0.0", "3.1.0-preview.1", "3.0.0"],
                declaredRange: ""));
        Assert.Equal(
            "2.1.0",
            BrowserPackageWorkspace.SelectDependencyVersion(
                ["1.0.0", "2.0.0", "2.1.0", "3.0.0"],
                declaredRange: "2.*"));
    }

    private static async Task<CompiledDocumentationOutcome>
        QueryPlatformMemberDocumentationAsync(
            string version,
            bool includeDocumentation) =>
        await QueryPlatformDocumentationAsync(
            version,
            includeDocumentation,
            (workspaceClient, packageClient, sourceAuthorization) =>
                BrowserPlatformWorkspace.QueryMemberDocumentationAsync(
                    "net11.0",
                    version,
                    "InspectWeb.DocumentationFixtures.dll",
                    "netcore.app",
                    "M:InspectWeb.DocumentationFixtures.HiddenDocumentedType.Read",
                    workspaceClient,
                    packageClient,
                    sourceAuthorization,
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken));

    private static async Task<DocumentationQueryOutcome>
        QueryUnifiedPlatformMemberDocumentationAsync(
            string version,
            bool includeDocumentation) =>
        await QueryPlatformDocumentationAsync(
            version,
            includeDocumentation,
            (workspaceClient, packageClient, sourceAuthorization) =>
                BrowserPlatformWorkspace
                    .QueryUnifiedMemberDocumentationAsync(
                        "net11.0",
                        version,
                        "InspectWeb.DocumentationFixtures.dll",
                        "netcore.app",
                        "M:InspectWeb.DocumentationFixtures.HiddenDocumentedType.Read",
                        workspaceClient,
                        packageClient,
                        sourceAuthorization,
                        TimeSpan.FromSeconds(10),
                        TestContext.Current.CancellationToken));

    private static async Task<T> QueryPlatformDocumentationAsync<T>(
        string version,
        bool includeDocumentation,
        Func<
            HttpClient,
            IPackageSourceClient,
            IPackageSourceAuthorization,
            Task<T>> query)
    {
        const string assembly =
            "InspectWeb.DocumentationFixtures.dll";
        byte[] assemblyBytes = File.ReadAllBytes(
            FixtureCatalog.InspectWebDocumentation.AssemblyPath());
        var referenceEntries =
            new List<(string Path, byte[] Content)>
            {
                ($"ref/net11.0/{assembly}", assemblyBytes),
            };
        if (includeDocumentation)
        {
            referenceEntries.Add(
                (
                    "ref/net11.0/InspectWeb.DocumentationFixtures.xml",
                    File.ReadAllBytes(
                        FixtureCatalog.InspectWebDocumentation.AssetPath(
                            "documentation"))));
        }
        byte[] referencePackage =
            PackageEntries([.. referenceEntries]);
        var packages =
            new Dictionary<string, byte[]>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["microsoft.netcore.app.runtime.linux-x64"] =
                    PlatformPackage((assembly, assemblyBytes)),
                ["microsoft.netcore.app.ref"] =
                    referencePackage,
            };
        var workspaceHandler =
            new MultiplePlatformVersionHandler(version, packages);
        using var workspaceClient = new HttpClient(workspaceHandler);
        byte[] runtimePackage =
            PlatformPackage((assembly, assemblyBytes));
        var galleryPackages =
            new Dictionary<string, HttpMessageHandler>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["microsoft.netcore.app.runtime.linux-x64"] =
                    new GalleryPackageHandler(
                        "microsoft.netcore.app.runtime.linux-x64",
                        version,
                        runtimePackage),
                ["microsoft.netcore.app.ref"] =
                    new GalleryPackageHandler(
                        "microsoft.netcore.app.ref",
                        version,
                        referencePackage),
            };
        using IPackageSourceClient packageClient =
            BrowserPackageWorkspace.CreateGallerySource(
                new MultipleGalleryPackageHandler(version, galleryPackages),
                new NuGetFetchOptions());
        IPackageSourceAuthorization sourceAuthorization =
            BrowserPackageWorkspace.SourceAuthorizationFor(packageClient);

        return await query(
            workspaceClient,
            packageClient,
            sourceAuthorization);
    }

    private sealed class FixedPackageSourceAuthorization(
        PackageSourceAuthorization authorization) :
        IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId) =>
            authorization;
    }

    [Fact]
    public void DependencyCoordinateMatch_PreservesProductOwnedProvenanceAndCardinality()
    {
        var platform = new BrowserDependencyCoordinateCandidate(
            "platform",
            BrowserDependencyCoordinateProvenance.PlatformRuntime,
            "Microsoft.NETCore.App",
            "10.0.10",
            "net10.0");
        var package = new BrowserDependencyCoordinateCandidate(
            "package",
            BrowserDependencyCoordinateProvenance.NuGetPackage,
            "Microsoft.NETCore.App",
            "2.2.8",
            "netcoreapp1.0");

        BrowserDependencyCoordinateMatch noMatch = MatchDependencyCoordinate(
            [platform],
            "Microsoft.NETCore.App",
            "1.0.5");
        BrowserDependencyCoordinateMatch unique = MatchDependencyCoordinate(
            [platform, package],
            "Microsoft.NETCore.App",
            "1.0.5");
        BrowserDependencyCoordinateMatch ambiguous = MatchDependencyCoordinate(
            [
                platform,
                package,
                package with { Key = "package-other-framework", TargetFramework = "net8.0" },
            ],
            "Microsoft.NETCore.App",
            "1.0.5");

        Assert.Equal(BrowserDependencyCoordinateMatchOutcome.NoMatch, noMatch.Outcome);
        Assert.Null(noMatch.CandidateKey);
        Assert.Equal(BrowserDependencyCoordinateMatchOutcome.Unique, unique.Outcome);
        Assert.Equal("package", unique.CandidateKey);
        Assert.Equal(BrowserDependencyCoordinateMatchOutcome.Ambiguous, ambiguous.Outcome);
        Assert.Null(ambiguous.CandidateKey);
    }

    [Fact]
    public void BuildIdentity_ReadsHostAssemblyAttributes()
    {
        Assembly assembly = typeof(InspectionEngine).Assembly;
        AssemblyInformationalVersionAttribute? informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        BrowserBuildIdentity identity =
            BrowserBuildIdentityReader.Read(assembly);

        Assert.NotNull(informationalVersion);
        Assert.Equal(
            informationalVersion.InformationalVersion.Split('+', 2)[0],
            identity.Version);
    }

    [Fact]
    public void BuildIdentity_UsesFileVersionWithoutInformationalVersion()
    {
        const string fileVersion = "2.3.4.5";
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("BrowserBuildIdentityFallback"),
            AssemblyBuilderAccess.Run);
        ConstructorInfo constructor =
            typeof(AssemblyFileVersionAttribute).GetConstructor([typeof(string)])!;
        assembly.SetCustomAttribute(
            new CustomAttributeBuilder(constructor, [fileVersion]));

        BrowserBuildIdentity identity = BrowserBuildIdentityReader.Read(assembly);

        Assert.Equal(fileVersion, identity.Version);
    }

    [Fact]
    public void BuildIdentity_UsesVersionedRepositoryProvenance()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";

        BrowserBuildIdentity identity = BrowserBuildIdentityReader.Create(
            "0.18.0",
            commit,
            "https://github.com/richlander/dotnet-inspect",
            "2026-08-14T23:30:22Z");

        Assert.Equal("0.18.0", identity.Version);
        Assert.Equal(commit, identity.Commit);
        Assert.Equal("2026-08-14T23:30:22.0000000+00:00", identity.BuiltAtUtc);
        Assert.Equal(
            $"https://github.com/richlander/dotnet-inspect/commit/{commit}",
            identity.CommitUrl);
    }

    [Fact]
    public void BuildIdentity_DropsInvalidOptionalProvenance()
    {
        BrowserBuildIdentity identity = BrowserBuildIdentityReader.Create(
            "0.18.0",
            "not-a-commit",
            "javascript:alert(1)",
            "not-a-time");

        Assert.Null(identity.Commit);
        Assert.Null(identity.BuiltAtUtc);
        Assert.Null(identity.CommitUrl);
    }

    [Fact]
    public async Task WorkspaceBinding_RejectsPackageParticipantsForPlatformScope()
    {
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Platform.Confusable",
            Package(image, "lib/net11.0/Platform.Confusable.dll"));
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([coordinate], TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserWorkspaceParticipant participant =
            Assert.Single(scope.SurfaceParticipants);
        AssemblyBindingSelection any =
            participant.Participant.BindingPolicy.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(
                        participant.Assembly.Identity),
                    AssemblyBindingOrigin.FromAssembly(
                        participant.Assembly),
                    AssemblyResolutionScope.Any)).Selection;
        AssemblyBindingSelection platform =
            participant.Participant.BindingPolicy.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(
                        participant.Assembly.Identity),
                    AssemblyBindingOrigin.FromAssembly(
                        participant.Assembly),
                    AssemblyResolutionScope.Platform)).Selection;

        Assert.Same(
            participant.Assembly,
            Assert.IsType<AssemblyBindingSelection.Selected>(any).Assembly);
        Assert.IsType<AssemblyBindingSelection.Missing>(platform);
    }

    [Fact]
    public async Task WorkspaceBinding_RejectsEquivalentAssemblyIdentities()
    {
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate first = await Coordinate(
            "Identity.Collision.A",
            Package(image, "lib/net11.0/Identity.Collision.A.dll"));
        BrowserPackageCoordinate second = await Coordinate(
            "Identity.Collision.B",
            Package(image, "lib/net11.0/Identity.Collision.B.dll"));

        PackageAssemblyRoleCorrespondenceException failure =
            await Assert.ThrowsAsync<PackageAssemblyRoleCorrespondenceException>(
                async () => await BrowserInspectionScope.CreateAsync(
                    [first, second],
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "same assembly identity",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImplementationPairing_RequiresEquivalentAssemblyIdentity()
    {
        byte[] surfaceImage =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] differentImage =
            File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        BrowserPackageCoordinate mismatched = await Coordinate(
            "Identity.Mismatch",
            PackagePair(surfaceImage, differentImage, "Identity.Pair.dll"));

        PackageAssemblyRoleCorrespondenceException failure =
            await Assert.ThrowsAsync<PackageAssemblyRoleCorrespondenceException>(
                async () => await BrowserPackageWorkspace.OpenScopeAsync(
                    [mismatched],
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "different assembly identities",
            failure.Message,
            StringComparison.Ordinal);

        BrowserPackageCoordinate equivalent = await Coordinate(
            "Identity.Equivalent",
            PackagePair(surfaceImage, surfaceImage, "Identity.Pair.dll"));
        await using BrowserScopeLease<BrowserInspectionScope> equivalentScopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([equivalent], TestContext.Current.CancellationToken);
        BrowserInspectionScope equivalentScope = equivalentScopeLease.Scope;
        BrowserWorkspaceParticipant equivalentSurface =
            Assert.Single(equivalentScope.SurfaceParticipants);

        Assert.NotNull(
            equivalentScope.ImplementationParticipant(equivalentSurface));
    }

    [Fact]
    public async Task WorkspaceDisposal_ClosesWorkspaceAfterRoleFailure()
    {
        byte[] image =
            File.ReadAllBytes(
                typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Dispose.Roles",
            PackagePair(image, image, "Dispose.Roles.dll"));
        var scope = await BrowserInspectionScope.CreateAsync([coordinate], TestContext.Current.CancellationToken);
        AssemblyContextGroup implementation =
            scope.UseImplementation(group => group);
        MethodInfo registerOwnedResource =
            typeof(AssemblyContextGroup).GetMethod(
                "RegisterOwnedResource",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "AssemblyContextGroup.RegisterOwnedResource was not found.");
        registerOwnedResource.Invoke(
            implementation,
            [new ThrowingResource("browser role disposal failed")]);

        AggregateException failure =
            await Assert.ThrowsAsync<AggregateException>(
                async () => await scope.DisposeAsync());

        Assert.Contains(
            failure.Flatten().InnerExceptions,
            ex => ex.Message == "browser role disposal failed");
        FieldInfo field =
            typeof(BrowserInspectionScope).GetField(
                "_workspace",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "BrowserInspectionScope._workspace was not found.");
        var workspace =
            Assert.IsType<InspectionWorkspace>(field.GetValue(scope));
        Assert.Throws<ObjectDisposedException>(
            () => workspace.CreateAssemblyContextGroup(
                [scope.SurfaceParticipants[0].Participant]));
    }

    [Fact]
    public void CallGraphDiagnostics_PreserveIncompleteProductEvidence()
    {
        BrowserCallGraphDiagnostics diagnostics =
            BrowserCallGraphWireProjection.Project(
                BrowserCallGraphProjection.Diagnostics(
                    new AssemblyContextMemberCallGraphDiagnostics(2, 3, 4),
                    hasUnexploredTraversalBoundary: true,
                    hasAnalysisFailureBoundary: true));

        Assert.True(diagnostics.IsIncomplete);
        Assert.Equal(2, diagnostics.IncompleteNodes);
        Assert.Equal(3, diagnostics.IncompleteEdges);
        Assert.Equal(4, diagnostics.BindingIdentityConflicts);
        Assert.True(diagnostics.HasUnexploredTraversalBoundary);
        Assert.True(diagnostics.HasAnalysisFailureBoundary);
        Assert.True(diagnostics.HasIncompleteCorrespondence);
        Assert.Equal(0, diagnostics.UnclassifiedBoundaryEdges);
        Assert.Equal(0, diagnostics.UnclassifiedBoundaryNamedEdges);
        Assert.Empty(diagnostics.UnclassifiedBoundaryAssemblies);
        Assert.Equal(0, diagnostics.PhysicalOccurrenceUnavailableEdges);
    }

    [Fact]
    public void SurfaceProjection_UsesExactMetadataTypeIdentityForBrowserKeys()
    {
        MetadataTypeDefinitionName nestedName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Outer", "Inner"]))
            .Name;
        var type = new ApiType
        {
            Namespace = "Sample",
            Name = "Outer.Inner",
            MetadataName = "Outer+Inner",
            DefinitionName = nestedName,
            Kind = "class",
        };

        BrowserTypeSurfaceInfo projected = BrowserSurfaceProjection.Type(
            type,
            "Physical.dll",
            "asset:physical",
            "Sample");

        Assert.Equal("Sample.Outer+Inner", projected.Id);
        Assert.Equal("Physical.dll", projected.Assembly);
        Assert.Equal("asset:physical", projected.AssemblyId);
        Assert.Equal("Sample", projected.AssemblyName);
        Assert.Equal(projected.Id, projected.DefinitionId);
        Assert.Equal("Sample.Outer.Inner", projected.QueryId);
        Assert.Equal(projected.Id, projected.MetadataId);

        var literalPlus = new ApiType
        {
            Namespace = "Sample",
            Name = "Outer+Inner",
            MetadataName = "Outer+Inner",
            DefinitionName = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Outer+Inner"]))
                .Name,
            Kind = "class",
        };
        BrowserTypeSurfaceInfo projectedLiteral =
            BrowserSurfaceProjection.Type(
                literalPlus,
                "Physical.dll",
                "asset:physical",
                "Sample");

        Assert.Equal(@"Sample.Outer\+Inner", projectedLiteral.Id);
        Assert.Equal(projectedLiteral.Id, projectedLiteral.DefinitionId);
        Assert.NotEqual(projected.Id, projectedLiteral.Id);
        Assert.Equal("Sample.Outer+Inner", projectedLiteral.QueryId);
        Assert.Equal(projected.MetadataId, projectedLiteral.MetadataId);

        BrowserTypeSurfaceInfo qualified = projected with { Id = $"Sample.dll:{projected.Id}" };
        Assert.NotEqual(qualified.Id, qualified.DefinitionId);
        Assert.Equal(projected.DefinitionId, qualified.DefinitionId);
    }

    [Fact]
    public void SurfaceProjection_LongDeclaringTypeStopsIncrementally()
    {
        var type = new ApiType
        {
            Namespace = new string('N', 4_000),
            Name = "Amplifier",
            Kind = "class",
            Members =
            [
                .. Enumerable.Range(0, 10_000).Select(index => new ApiMember
                {
                    Name = $"M{index}",
                    Kind = "method",
                    Signature = $"void M{index}()",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "void",
                        MemberName = $"M{index}",
                    },
                }),
            ],
        };
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(8_000_000);
        budget.BeginParticipant();
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Type(
                type,
                "Amplifier.dll",
                "asset:amplifier",
                "Amplifier",
                budget));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(
            allocated < 64L * MiB,
            $"bounded Browser projection allocated {allocated:N0} bytes");
    }

    [Fact]
    public void SurfaceProjection_OneHugeTypeStopsBeforeDerivedIdentities()
    {
        var type = new ApiType
        {
            Namespace = new string('N', 4_000_000),
            Name = "Amplifier",
            MetadataName = "Amplifier",
            Kind = "class",
        };
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(32_000_000);
        budget.BeginParticipant();
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Type(
                type,
                "Amplifier.dll",
                "asset:amplifier",
                "Amplifier",
                budget));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(
            allocated < 4L * MiB,
            $"Browser projection preflight allocated {allocated:N0} bytes");
    }

    [Fact]
    public void SurfaceProjection_OneHugeMemberStopsBeforeDerivedIdentities()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Amplifier",
            MetadataName = "Amplifier",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "M",
                    Kind = "method",
                    Signature = new string('S', 4_000_000),
                },
            ],
        };
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(32_000_000);
        budget.BeginParticipant();
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Type(
                type,
                "Amplifier.dll",
                "asset:amplifier",
                "Amplifier",
                budget));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(
            allocated < 4L * MiB,
            $"Browser projection preflight allocated {allocated:N0} bytes");
    }

    [Fact]
    public void SurfaceProjection_OneHugeExactMemberStopsBeforeDerivedIdentities()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Amplifier",
            MetadataName = "Amplifier",
            Kind = "class",
        };
        var member = new ApiMember
        {
            Name = "M",
            Kind = "method",
            Signature = new string('S', 4_000_000),
        };
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(32_000_000);
        budget.BeginParticipant();
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Member(type, member, budget));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(
            allocated < 4L * MiB,
            $"Browser exact-member projection preflight allocated {allocated:N0} bytes");
    }

    [Fact]
    public void SurfaceProjection_PreflightUsesTheRemainingSharedBudget()
    {
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(1_000_000);
        budget.BeginParticipant();
        _ = BrowserSurfaceProjection.Type(
            new ApiType
            {
                Namespace = new string('C', 10_000),
                Name = "Committed",
                MetadataName = "Committed",
                Kind = "class",
            },
            "Committed.dll",
            "asset:committed",
            "Committed",
            budget);
        budget.CommitParticipant();
        Assert.True(budget.CommittedCharacters > 40_000);

        budget.BeginParticipant();
        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Type(
                new ApiType
                {
                    Namespace = new string('P', 80_000),
                    Name = "Pending",
                    MetadataName = "Pending",
                    Kind = "class",
                },
                "Pending.dll",
                "asset:pending",
                "Pending",
                budget));
    }

    [Fact]
    public async Task QueryPackage_ToolsPointerRetainsRootAndManifestDependencies()
    {
        const string packageId = "Tool.Pointer";
        const string ridPackageId = "Tool.Pointer.linux-x64";
        const string ridAsset =
            "tools/net11.0/any/DotnetInspect.Web.Interop.Package.dll";
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Tool.Pointer</id>
                    <version>1.0.0</version>
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                    <dependencies>
                      <group targetFramework="net11.0">
                        <dependency id="Tool.Payload" version="[1.0.0]" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """)),
            ("README.md", Encoding.UTF8.GetBytes("# Tool Pointer")),
            ("skills/tool-pointer/SKILL.md",
                Encoding.UTF8.GetBytes("# Tool Pointer Skill")),
            ("tools/DotnetToolSettings.xml", Encoding.UTF8.GetBytes(
                """
                <DotNetCliTool Version="2">
                  <Commands>
                    <Command Name="tool-pointer" />
                  </Commands>
                  <RuntimeIdentifierPackages>
                    <RuntimeIdentifierPackage RuntimeIdentifier="linux-x64" Id="Tool.Pointer.linux-x64" />
                    <RuntimeIdentifierPackage RuntimeIdentifier="win-x64" Id="Tool.Pointer.win-x64" />
                  </RuntimeIdentifierPackages>
                </DotNetCliTool>
                """)));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(packageId, "1.0.0", package, fromCache: false));
        byte[] ridPackage = PackageEntries(
            ($"{ridPackageId}.nuspec", Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Tool.Pointer.linux-x64</id>
                    <version>1.0.0</version>
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                  </metadata>
                </package>
                """)),
            ("tools/net11.0/any/DotnetToolSettings.xml",
                Encoding.UTF8.GetBytes(
                    """
                    <DotNetCliTool Version="2">
                      <Commands>
                        <Command Name="tool-pointer" EntryPoint="DotnetInspect.Web.Interop.Package.dll" Runner="dotnet" />
                      </Commands>
                    </DotNetCliTool>
                    """)),
            (ridAsset,
                File.ReadAllBytes(
                    typeof(DotnetInspect.Web.Interop.Package
                        .PackageExports).Assembly.Location)));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                ridPackageId,
                "1.0.0",
                ridPackage,
                fromCache: false));

        await using BrowserScopeLease<BrowserInspectionScope> rootScopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                "1.0.0",
                "net11.0",
                TestContext.Current.CancellationToken);
        BrowserInspectionScope rootScope = rootScopeLease.Scope;
        BrowserPackageCoordinate coordinate = Assert.Single(rootScope.Coordinates);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            Assert.IsType<PackageCompileAssetSelection>(coordinate.Root.AssetSelection).Status);
        Assert.Equal(
            coordinate.Package.Content.FromCache,
            coordinate.Root.FromCache);
        Assert.Equal(
            coordinate.Package.Content.ProducerKey,
            coordinate.Root.ProducerKey);
        Assert.True(
            coordinate.Root.ReferencesContent(coordinate.Package.Content));
        Assert.Empty(rootScope.SurfaceParticipants);
        Assert.Empty(rootScope.ImplementationParticipants);

        BrowserPackageSurface surface = await QueryPackageSurface(
            packageId,
            "1.0.0",
            "net11.0");

        Assert.Equal(
            BrowserCompileLibraryStatus.NoCompileAssets,
            surface.CompileLibrary.Status);
        Assert.Null(surface.CompileLibrary.TargetFramework);
        Assert.Null(surface.DefaultAssemblyId);
        Assert.Empty(surface.Assemblies);
        Assert.Empty(surface.Types);
        Assert.Empty(surface.Accessibility);
        Assert.Collection(
            surface.Documents,
            document => Assert.Equal("README.md", document.Path),
            document => Assert.Equal(
                "skills/tool-pointer/SKILL.md",
                document.Path),
            document => Assert.Equal("metadata", document.Kind));
        Assert.Empty(surface.InspectionErrors);
        Assert.Null(surface.InspectionError);

        BrowserPackageLoadResult load =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryPackageSummary(
                            packageId,
                            "1.0.0",
                            "net11.0"),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageLoadResult));
        BrowserPackageChildren children =
            Assert.IsType<BrowserPackageChildrenInspection>(
                load.PackageChildren).Content;
        Assert.Collection(
            load.Documents,
            document => Assert.Equal("README.md", document.Path),
            document => Assert.Equal(
                "skills/tool-pointer/SKILL.md",
                document.Path),
            document => Assert.Equal("metadata", document.Kind));
        Assert.Equal("RuntimeIdentifierPackages", children.Kind);
        Assert.Equal("Available", children.Status);
        Assert.Collection(
            children.RuntimeIdentifierPackages,
            child =>
            {
                Assert.Equal("linux-x64", child.RuntimeIdentifier);
                Assert.Equal(ridPackageId, child.PackageId);
            },
            child =>
            {
                Assert.Equal("win-x64", child.RuntimeIdentifier);
                Assert.Equal(
                    "Tool.Pointer.win-x64",
                    child.PackageId);
            });
        Assert.Empty(children.Libraries);

        BrowserPackageLoadResult ridLoad =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryPackage(
                            ridPackageId,
                            "1.0.0",
                            targetFramework: ""),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageLoadResult));
        BrowserPackageChildren ridChildren =
            Assert.IsType<BrowserPackageChildrenInspection>(
                ridLoad.PackageChildren).Content;
        BrowserPackageLibraryChild ridLibrary =
            Assert.Single(ridChildren.Libraries);
        Assert.Equal("net11.0", ridChildren.TargetFramework);
        Assert.Equal(ridAsset, ridLibrary.AssetId);

        BrowserExactLibraryApiInspection ridApi =
            Assert.IsType<BrowserExactLibraryApiInspection>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryLibraryApi(
                            ridPackageId,
                            "1.0.0",
                            targetFramework: "",
                            ridLibrary.AssetId),
                    BrowserPackageJsonContext.Default
                        .BrowserExactLibraryApiInspection));
        Assert.True(ridApi.Content.IsAvailable);
        Assert.Equal("", ridApi.Content.RequestedTargetFramework);
        Assert.Equal("", ridApi.Content.Source?.Framework);
        Assert.Equal("net11.0", ridApi.Content.Asset?.TargetFramework);
        Assert.Equal(ridAsset, ridApi.Content.Asset?.Id);
        Assert.Equal(
            BrowserExactLibraryApiAssetKind.Tool,
            ridApi.Content.Asset?.Kind);
        Assert.Equal(
            BrowserInspectionShareKind.Available,
            ridApi.Share.Kind);

        BrowserPackageDependencyInspection inspection =
            await InspectPackageDependencies(
                packageId,
                "1.0.0",
                "net11.0");
        PackageDependencyInspectionOutcome.Available available =
            AssertAvailableDependencies(inspection);
        DeclaredPackageDependency dependency = Assert.Single(
            Assert.Single(available.Dependencies.Groups).Dependencies);
        Assert.Equal("Tool.Payload", dependency.Id);
        InvalidOperationException metadataTableFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageMetadataTable(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    assemblyFileName: "",
                    metadataRoot: "cli",
                    tableIndex: 0,
                    startRowId: 1,
                    maxRows: 1));
        Assert.Contains(
            nameof(PackageCompileAssetSelectionStatus.NoCompileAssets),
            metadataTableFailure.Message);
        await AssertRootOnlyAggregateStatus(
            packageId,
            "net11.0",
            BrowserCompileLibraryStatus.NoCompileAssets);
    }

    [Theory]
    [InlineData("1", "DotnetInspect.Web.Interop.Package.dll")]
    [InlineData("2", "DOTNETINSPECT.WEB.INTEROP.PACKAGE.DLL")]
    public async Task QueryPackageSummary_ToolPayloadPublishesExactManagedLibraries(
        string settingsVersion,
        string entryPoint)
    {
        const string packageId = "Tool.Payload";
        const string asset =
            "tools/net11.0/any/DotnetInspect.Web.Interop.Package.dll";
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Tool.Payload</id>
                    <version>1.0.0</version>
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                  </metadata>
                </package>
                """)),
            ("tools/net11.0/any/DotnetToolSettings.xml",
                Encoding.UTF8.GetBytes(
                    $"""
                    <DotNetCliTool Version="{settingsVersion}">
                      <Commands>
                        <Command Name="tool-payload" EntryPoint="{entryPoint}" Runner="dotnet" />
                      </Commands>
                    </DotNetCliTool>
                    """)),
            (asset,
                File.ReadAllBytes(
                    typeof(DotnetInspect.Web.Interop.Package
                        .PackageExports).Assembly.Location)));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                package,
                fromCache: false));

        BrowserPackageLoadResult load =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryPackageSummary(
                            packageId,
                            "1.0.0",
                            "net11.0"),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageLoadResult));

        BrowserPackageChildren children =
            Assert.IsType<BrowserPackageChildrenInspection>(
                load.PackageChildren).Content;
        Assert.Equal("Libraries", children.Kind);
        Assert.Equal("Available", children.Status);
        BrowserPackageLibraryChild library =
            Assert.Single(children.Libraries);
        Assert.Equal(asset, library.AssetId);
        Assert.Equal(asset, library.AssetPath);
        Assert.Equal(
            "DotnetInspect.Web.Interop.Package",
            library.AssemblyName);
        Assert.Equal("ToolEntryPoint", library.Role);
        Assert.Equal(asset, load.DefaultLibraryId);
        Assert.Empty(children.RuntimeIdentifierPackages);
        Assert.Null(load.Surface);

        BrowserExactLibraryApiInspection api =
            Assert.IsType<BrowserExactLibraryApiInspection>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryLibraryApi(
                            packageId,
                            "1.0.0",
                            "net11.0",
                            library.AssetId),
                    BrowserPackageJsonContext.Default
                        .BrowserExactLibraryApiInspection));
        Assert.True(api.Content.IsAvailable);
        Assert.True(api.Content.Inventory?.PublicTypeCount > 0);
        Assert.Equal(library.AssetId, api.Content.Asset?.Id);
        Assert.Equal(
            BrowserExactLibraryApiAssetKind.Tool,
            api.Content.Asset?.Kind);
    }

    [Fact]
    public async Task QueryPackage_SummaryDoesNotOpenCompileLibraries()
    {
        const string packageId = "Package.Invalid.Library";
        const string asset = "lib/net11.0/Invalid.Library.dll";
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Package.Invalid.Library</id>
                    <version>1.0.0</version>
                  </metadata>
                </package>
                """)),
            (asset, [0x00, 0x01, 0x02, 0x03]));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                package,
                fromCache: false));

        BrowserPackageLoadResult load =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryPackageSummary(
                            packageId,
                            "1.0.0",
                            "net11.0"),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageLoadResult));

        BrowserPackageLibraryChild library = Assert.Single(
            Assert.IsType<BrowserPackageChildrenInspection>(
                load.PackageChildren).Content.Libraries);
        Assert.Equal($"compile:{asset}", library.AssetId);
        Assert.Equal(asset, library.AssetPath);
        Assert.Equal("Invalid.Library.dll", library.AssemblyName);
        Assert.Equal($"compile:{asset}", load.DefaultLibraryId);
        Assert.Null(load.Surface);
    }

    [Fact]
    public async Task QueryPackage_PreservesAggregateLibrarySurface()
    {
        const string packageId = "Package.First.Surface";
        const string firstAsset = "lib/net11.0/A.First.dll";
        const string siblingAsset = "lib/net11.0/Z.Sibling.dll";
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                $"""
                 <?xml version="1.0" encoding="utf-8"?>
                 <package>
                   <metadata>
                     <id>{packageId}</id>
                     <version>1.0.0</version>
                   </metadata>
                 </package>
                 """)),
            (firstAsset,
                File.ReadAllBytes(
                    typeof(DotnetInspect.Web.Interop.Package
                        .PackageExports).Assembly.Location)),
            (siblingAsset,
                File.ReadAllBytes(
                    typeof(BrowserPackage).Assembly.Location)));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                package,
                fromCache: false));

        BrowserPackageLoadResult load =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryPackage(
                            packageId,
                            "1.0.0",
                            "net11.0"),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageLoadResult));

        string firstLibraryId = $"compile:{firstAsset}";
        Assert.Equal(firstLibraryId, load.DefaultLibraryId);
        BrowserPackageChildrenInspection children =
            Assert.IsType<BrowserPackageChildrenInspection>(
                load.PackageChildren);
        Assert.Equal(2, children.Content.Libraries.Length);
        BrowserPackageSurface surface =
            Assert.IsType<BrowserPackageSurface>(load.Surface);
        Assert.Equal(2, surface.Assemblies.Length);
        Assert.Contains(
            surface.Assemblies,
            candidate => candidate.Id == firstLibraryId);
        Assert.Contains(
            surface.Assemblies,
            candidate => candidate.Id == $"compile:{siblingAsset}");
    }

    [Fact]
    public async Task QueryLibrarySurface_FirstLibrarySurfaceExcludesSibling()
    {
        const string packageId = "Package.First.Surface.Exact";
        const string firstAsset = "lib/net11.0/A.First.dll";
        const string siblingAsset = "lib/net11.0/Z.Sibling.dll";
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                $"""
                 <?xml version="1.0" encoding="utf-8"?>
                 <package>
                   <metadata>
                     <id>{packageId}</id>
                     <version>1.0.0</version>
                   </metadata>
                 </package>
                 """)),
            (firstAsset,
                File.ReadAllBytes(
                    typeof(DotnetInspect.Web.Interop.Package
                        .PackageExports).Assembly.Location)),
            (siblingAsset,
                File.ReadAllBytes(
                    typeof(BrowserPackage).Assembly.Location)));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                package,
                fromCache: false));

        string firstLibraryId = $"compile:{firstAsset}";
        BrowserPackageLoadResult load =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryLibrarySurface(
                            packageId,
                            "1.0.0",
                            "net11.0",
                            firstLibraryId),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageLoadResult));

        Assert.Equal(firstLibraryId, load.DefaultLibraryId);
        BrowserPackageChildrenInspection children =
            Assert.IsType<BrowserPackageChildrenInspection>(
                load.PackageChildren);
        Assert.Equal(2, children.Content.Libraries.Length);
        BrowserPackageSurface surface =
            Assert.IsType<BrowserPackageSurface>(load.Surface);
        BrowserAssemblySurface assembly =
            Assert.Single(surface.Assemblies);
        Assert.Equal(firstLibraryId, assembly.Id);
        Assert.Equal(firstLibraryId, surface.DefaultAssemblyId);
        Assert.DoesNotContain(
            surface.Assemblies,
            candidate => candidate.Id == $"compile:{siblingAsset}");
    }

    [Fact]
    public async Task QueryLibrarySurface_FirstLibraryFailureDoesNotSelectSiblingSurface()
    {
        const string packageId = "Package.First.Library";
        const string firstAsset =
            "lib/net11.0/Package.First.Library.dll";
        const string siblingAsset = "lib/net11.0/Sibling.dll";
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Package.First.Library</id>
                    <version>1.0.0</version>
                  </metadata>
                </package>
                """)),
            (firstAsset, [0x00, 0x01, 0x02, 0x03]),
            (siblingAsset,
                File.ReadAllBytes(
                    typeof(DotnetInspect.Web.Interop.Package
                        .PackageExports).Assembly.Location)));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                package,
                fromCache: false));

        BrowserPackageLoadResult summary =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryPackageSummary(
                            packageId,
                            "1.0.0",
                            "net11.0"),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageLoadResult));

        string firstLibraryId = $"compile:{firstAsset}";
        Assert.Equal(firstLibraryId, summary.DefaultLibraryId);
        Assert.Null(summary.Surface);
        BrowserPackageChildrenInspection children =
            Assert.IsType<BrowserPackageChildrenInspection>(
                summary.PackageChildren);
        Assert.Equal(2, children.Content.Libraries.Length);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DotnetInspect.Web.Interop.Package
                    .PackageExports.QueryLibrarySurface(
                        packageId,
                        "1.0.0",
                        "net11.0",
                        firstLibraryId));
        Assert.Contains(
            "Assembly unavailable: InvalidImage.",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryPackage_DeclaredToolWithoutSettingsRemainsUnavailable()
    {
        const string packageId = "Tool.Missing.Settings";
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Tool.Missing.Settings</id>
                    <version>1.0.0</version>
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                  </metadata>
                </package>
                """)),
            ("tools/net11.0/any/DotnetInspect.Web.Interop.Package.dll",
                File.ReadAllBytes(
                    typeof(DotnetInspect.Web.Interop.Package
                        .PackageExports).Assembly.Location)));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                package,
                fromCache: false));

        BrowserPackageLoadResult load =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryPackage(
                            packageId,
                            "1.0.0",
                            "net11.0"),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageLoadResult));

        BrowserPackageChildren children =
            Assert.IsType<BrowserPackageChildrenInspection>(
                load.PackageChildren).Content;
        Assert.Equal("Libraries", children.Kind);
        Assert.Equal("Unavailable", children.Status);
        Assert.False(children.IsComplete);
        Assert.Contains(
            "no DotnetToolSettings.xml manifest",
            children.Detail);
        Assert.Empty(children.Libraries);
    }

    [Fact]
    public async Task QueryPackage_CompatibleToolNavigationPreservesSelectedFramework()
    {
        const string packageId = "Tool.Compatible.Payload";
        const string requestedFramework = "net10.0";
        const string selectedFramework = "net8.0";
        const string asset =
            "tools/net8.0/any/DotnetInspect.Web.Interop.Package.dll";
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Tool.Compatible.Payload</id>
                    <version>1.0.0</version>
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                  </metadata>
                </package>
                """)),
            ("tools/net8.0/any/DotnetToolSettings.xml",
                Encoding.UTF8.GetBytes(
                    """
                    <DotNetCliTool Version="2">
                      <Commands>
                        <Command Name="tool-compatible" EntryPoint="DotnetInspect.Web.Interop.Package.dll" Runner="dotnet" />
                      </Commands>
                    </DotNetCliTool>
                    """)),
            (asset,
                File.ReadAllBytes(
                    typeof(DotnetInspect.Web.Interop.Package
                        .PackageExports).Assembly.Location)));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                package,
                fromCache: false));

        BrowserPackageLoadResult load =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryPackage(
                            packageId,
                            "1.0.0",
                            requestedFramework),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageLoadResult));
        BrowserPackageChildren children =
            Assert.IsType<BrowserPackageChildrenInspection>(
                load.PackageChildren).Content;
        BrowserPackageLibraryChild library =
            Assert.Single(children.Libraries);
        Assert.Equal(selectedFramework, children.TargetFramework);

        BrowserExactLibraryApiInspection api =
            Assert.IsType<BrowserExactLibraryApiInspection>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryLibraryApi(
                            packageId,
                            "1.0.0",
                            requestedFramework,
                            library.AssetId),
                    BrowserPackageJsonContext.Default
                        .BrowserExactLibraryApiInspection));
        Assert.True(api.Content.IsAvailable);
        Assert.Equal(
            requestedFramework,
            api.Content.RequestedTargetFramework);
        Assert.Equal(requestedFramework, api.Content.Source?.Framework);
        Assert.Equal(selectedFramework, api.Content.Asset?.TargetFramework);
        Assert.Equal(
            BrowserExactLibraryApiAssetKind.Tool,
            api.Content.Asset?.Kind);
    }

    [Fact]
    public async Task QueryPackage_NativeToolPayloadStatesNoManagedLibraries()
    {
        const string packageId = "Tool.Native.Payload";
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Tool.Native.Payload</id>
                    <version>1.0.0</version>
                    <packageTypes>
                      <packageType name="DotnetToolRidPackage" />
                    </packageTypes>
                  </metadata>
                </package>
                """)),
            ("tools/net11.0/linux-x64/DotnetToolSettings.xml",
                Encoding.UTF8.GetBytes(
                    """
                    <DotNetCliTool Version="2">
                      <Commands>
                        <Command Name="native-tool" EntryPoint="native-tool" Runner="direct" />
                      </Commands>
                    </DotNetCliTool>
                    """)),
            ("tools/net11.0/linux-x64/native-tool", [0x7f, 0x45, 0x4c, 0x46]));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                package,
                fromCache: false));

        BrowserPackageLoadResult load =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryPackage(
                            packageId,
                            "1.0.0",
                            "net11.0"),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageLoadResult));

        BrowserPackageChildren children =
            Assert.IsType<BrowserPackageChildrenInspection>(
                load.PackageChildren).Content;
        Assert.Equal("NoManagedLibraries", children.Kind);
        Assert.Equal("Available", children.Status);
        Assert.Equal("net11.0", children.TargetFramework);
        Assert.Contains("no managed Libraries", children.Detail);
        Assert.Empty(children.Libraries);
        Assert.Empty(children.RuntimeIdentifierPackages);
    }

    [Fact]
    public async Task QueryPackage_DisagreeingToolSettingsRemainUnavailable()
    {
        const string packageId = "Tool.Ambiguous";
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Tool.Ambiguous</id>
                    <version>1.0.0</version>
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                  </metadata>
                </package>
                """)),
            ("tools/DotnetToolSettings.xml", Encoding.UTF8.GetBytes(
                """
                <DotNetCliTool Version="1">
                  <Commands><Command Name="first" /></Commands>
                </DotNetCliTool>
                """)),
            ("tools/net11.0/any/DotnetToolSettings.xml",
                Encoding.UTF8.GetBytes(
                    """
                    <DotNetCliTool Version="1">
                      <Commands><Command Name="second" /></Commands>
                    </DotNetCliTool>
                    """)));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                package,
                fromCache: false));

        BrowserPackageLoadResult load =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryPackage(
                            packageId,
                            "1.0.0",
                            "net11.0"),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageLoadResult));

        BrowserPackageChildren children =
            Assert.IsType<BrowserPackageChildrenInspection>(
                load.PackageChildren).Content;
        Assert.Equal("Libraries", children.Kind);
        Assert.Equal("Unavailable", children.Status);
        Assert.False(children.IsComplete);
        Assert.Contains("disagreeing", children.Detail);
        Assert.Empty(children.Libraries);
    }

    [Fact]
    public async Task QueryPackage_EmptyRidPackageIdRemainsUnavailable()
    {
        const string packageId = "Tool.Invalid.Pointer";
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Tool.Invalid.Pointer</id>
                    <version>1.0.0</version>
                    <packageTypes>
                      <packageType name="DotnetTool" />
                    </packageTypes>
                  </metadata>
                </package>
                """)),
            ("tools/DotnetToolSettings.xml", Encoding.UTF8.GetBytes(
                """
                <DotNetCliTool Version="2">
                  <Commands><Command Name="invalid-tool" /></Commands>
                  <RuntimeIdentifierPackages>
                    <RuntimeIdentifierPackage RuntimeIdentifier="linux-x64" Id="" />
                  </RuntimeIdentifierPackages>
                </DotNetCliTool>
                """)));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                package,
                fromCache: false));

        BrowserPackageLoadResult load =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package
                        .PackageExports.QueryPackage(
                            packageId,
                            "1.0.0",
                            "net11.0"),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageLoadResult));

        BrowserPackageChildren children =
            Assert.IsType<BrowserPackageChildrenInspection>(
                load.PackageChildren).Content;
        Assert.Equal("Libraries", children.Kind);
        Assert.Equal("Unavailable", children.Status);
        Assert.False(children.IsComplete);
        Assert.Contains("unsupported shape", children.Detail);
        Assert.Empty(children.Libraries);
        Assert.Empty(children.RuntimeIdentifierPackages);
    }

    [Fact]
    public async Task QueryPackage_ExplicitEmptyCompileGroupRetainsTypedAbsence()
    {
        const string packageId = "Empty.Compile.Group";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                PackageEntries(
                    ("ref/net11.0/_._", []),
                    ("lib/net11.0/Empty.Compile.Group.dll", [0x01])),
                fromCache: false));

        BrowserPackageSurface surface = await QueryPackageSurface(
            packageId,
            "1.0.0",
            "net11.0");

        Assert.Equal(
            BrowserCompileLibraryStatus.EmptyCompileGroup,
            surface.CompileLibrary.Status);
        Assert.Equal("net11.0", surface.CompileLibrary.TargetFramework);
        Assert.Null(surface.DefaultAssemblyId);
        Assert.Empty(surface.Assemblies);
        Assert.Empty(surface.Types);
        await AssertRootOnlyAggregateStatus(
            packageId,
            "net11.0",
            BrowserCompileLibraryStatus.EmptyCompileGroup);
    }

    [Fact]
    public async Task QueryPackage_CompatibleEmptyCompileGroupSuppressesLibraryFallback()
    {
        const string packageId = "Compatible.Empty.Compile.Group";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                PackageEntries(
                    ($"ref/net6.0/{packageId}.dll",
                        File.ReadAllBytes(
                            typeof(BrowserEngineBoundaryTests).Assembly.Location)),
                    ("ref/net8.0/_._", []),
                    ($"lib/net6.0/{packageId}.dll",
                        File.ReadAllBytes(
                            typeof(BrowserEngineBoundaryTests).Assembly.Location))),
                fromCache: false));

        BrowserPackageSurface surface = await QueryPackageSurface(
            packageId,
            "1.0.0",
            "net9.0");

        Assert.Equal("net9.0", surface.ActiveFramework);
        Assert.Equal(
            BrowserCompileLibraryStatus.EmptyCompileGroup,
            surface.CompileLibrary.Status);
        Assert.Equal("net8.0", surface.CompileLibrary.TargetFramework);
        Assert.Null(surface.DefaultAssemblyId);
        Assert.Empty(surface.Assemblies);
        Assert.Empty(surface.Types);
    }

    [Fact]
    public async Task QueryPackage_NoMatchingFrameworkRetainsRequestedRoot()
    {
        const string packageId = "Future.Library";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                PackageEntries(
                    ("lib/net11.0/Future.Library.dll",
                        File.ReadAllBytes(
                            typeof(BrowserEngineBoundaryTests).Assembly.Location))),
                fromCache: false));

        BrowserPackageSurface surface = await QueryPackageSurface(
            packageId,
            "1.0.0",
            "net10.0");

        Assert.Equal(
            BrowserCompileLibraryStatus.NoMatchingTargetFramework,
            surface.CompileLibrary.Status);
        Assert.Equal("net10.0", surface.ActiveFramework);
        Assert.Equal("net10.0", surface.CompileLibrary.TargetFramework);
        Assert.Null(surface.DefaultAssemblyId);
        Assert.Empty(surface.Assemblies);
        await AssertRootOnlyAggregateStatus(
            packageId,
            "net10.0",
            BrowserCompileLibraryStatus.NoMatchingTargetFramework);
    }

    [Fact]
    public async Task QueryPackage_HouseSelectsCompatibleReferenceAssetsAndRetainsDependencies()
    {
        const string packageId = "Reference.Only";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                PackageEntries(
                    ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                        $"""
                         <?xml version="1.0" encoding="utf-8"?>
                         <package>
                           <metadata>
                             <id>{packageId}</id>
                             <version>1.0.0</version>
                             <dependencies>
                               <group targetFramework="net10.0">
                                 <dependency id="Reference.Dependency" version="[1.0.0]" />
                               </group>
                             </dependencies>
                           </metadata>
                         </package>
                         """)),
                    ($"ref/net10.0/{packageId}.dll",
                        File.ReadAllBytes(
                            typeof(BrowserEngineBoundaryTests).Assembly.Location))),
                fromCache: false));

        BrowserPackageSurface surface = await QueryPackageSurface(
            packageId,
            "1.0.0",
            "net11.0");

        Assert.Equal("net11.0", surface.ActiveFramework);
        Assert.Equal(
            BrowserCompileLibraryStatus.Selected,
            surface.CompileLibrary.Status);
        Assert.Equal("net10.0", surface.CompileLibrary.TargetFramework);
        Assert.NotNull(surface.DefaultAssemblyId);
        Assert.Single(surface.Assemblies);

        PackageDependencyInspectionOutcome.Available dependencies =
            AssertAvailableDependencies(
                await InspectPackageDependencies(
                    packageId,
                    "1.0.0",
                    "net11.0"));
        Assert.Equal(
            PackageDependencyGroupSelectionStatus.Selected,
            dependencies.Dependencies.SelectionStatus);
        Assert.Equal(
            "net10.0",
            dependencies.Dependencies.SelectedTargetFramework);
        DeclaredPackageDependency dependency = Assert.Single(
            Assert.Single(dependencies.Dependencies.Groups).Dependencies);
        Assert.Equal("Reference.Dependency", dependency.Id);
    }

    [Fact]
    public async Task ReferencePackageSuppliers_WithoutRouteEvidenceRemainPlain()
    {
        const string packageId = "Reference.Icon.Without.Route";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                PackageEntries(
                    ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                        $"<package><metadata><id>{packageId}</id>"
                        + "<version>1.0.0</version></metadata></package>")),
                    ($"lib/net11.0/{packageId}.dll",
                        File.ReadAllBytes(
                            typeof(BrowserEngineBoundaryTests).Assembly.Location))),
                fromCache: false));

        BrowserReferencePackageSupplier[] suppliers =
            Assert.IsType<BrowserReferencePackageSupplier[]>(
                JsonSerializer.Deserialize(
                    await PackageExports.QueryReferencePackageSuppliers(
                        packageId,
                        "1.0.0",
                        "net11.0",
                        $"compile:lib/net11.0/{packageId}.dll"),
                    BrowserPackageJsonContext.Default
                        .BrowserReferencePackageSupplierArray));

        Assert.NotEmpty(suppliers);
        Assert.All(suppliers, supplier =>
        {
            Assert.Null(supplier.PackageId);
            Assert.Null(supplier.PackageVersion);
        });
    }

    [Fact]
    public async Task QueryPackage_FirstTransportTruncationReturnsTypedNotice()
    {
        const string packageId = "First.Transport.Truncation";
        byte[] image = BuildTransportAmplificationImage(
            packageId,
            typeCount: 10_000,
            namespaceLength: 1_000);
        BrowserPackageCoordinate coordinate = await Coordinate(
            packageId,
            Package(image, $"lib/net11.0/{packageId}.dll"));

        string json = await QueryPackageSurfaceJson(
            packageId,
            "1.0.0",
            "net11.0");
        BrowserPackageSurface surface = Assert.IsType<BrowserPackageSurface>(
            JsonSerializer.Deserialize(
                json,
                BrowserPackageJsonContext.Default.BrowserPackageSurface));

        Assert.Empty(surface.Assemblies);
        Assert.NotEmpty(surface.Accessibility);
        string inspectionError = Assert.Single(surface.InspectionErrors);
        Assert.Contains(
            "truncated",
            inspectionError,
            StringComparison.Ordinal);
        Assert.Equal(surface.InspectionError, inspectionError);
    }

    [Fact]
    public async Task QueryLibraries_UsesTransportAdmittedSurfacePopulation()
    {
        const string packageId = "Library.Query.Transport.Truncation";
        byte[] oversized = BuildTransportAmplificationImage(
            "A.Oversized",
            typeCount: 10_000,
            namespaceLength: 1_000);
        byte[] matching = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        _ = await Coordinate(
            packageId,
            PackageEntries(
                ("lib/net11.0/A.Oversized.dll", oversized),
                ("lib/net11.0/Z.Match.dll", matching)));

        BrowserPackageSurface surface = await QueryPackageSurface(
            packageId,
            "1.0.0",
            "net11.0");
        string json = await PackageExports.QueryLibraries(
            packageId,
            "1.0.0",
            "net11.0",
            JsonSerializer.Serialize(
                surface.Assemblies.Select(assembly => assembly.Id).ToArray(),
                BrowserPackageJsonContext.Default.StringArray),
            """["System.Runtime"]""");
        BrowserLibraryQueryInspection query =
            Assert.IsType<BrowserLibraryQueryInspection>(
                JsonSerializer.Deserialize(
                    json,
                    BrowserPackageJsonContext.Default
                        .BrowserLibraryQueryInspection));

        Assert.Empty(surface.Assemblies);
        Assert.Contains(
            "truncated",
            surface.InspectionError,
            StringComparison.Ordinal);
        Assert.Empty(query.Content.Results);
        Assert.Equal(0, query.Content.Summary.PopulationCandidates);
        Assert.True(query.Content.Summary.IsComplete);
    }

    [Fact]
    public void SurfaceProjection_QualifiedCollisionIdIsAccountedBeforeCommit()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Value",
            MetadataName = "Value",
            Kind = "class",
        };
        const string assembly = "Collision.Assembly.dll";
        var unqualifiedBudget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(10_000);
        unqualifiedBudget.BeginParticipant();
        _ = BrowserSurfaceProjection.Type(
            type,
            assembly,
            "asset:collision",
            "Collision.Assembly",
            unqualifiedBudget);
        unqualifiedBudget.CommitParticipant();

        var qualifiedBudget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(10_000);
        qualifiedBudget.BeginParticipant();
        BrowserTypeSurfaceInfo qualified = BrowserSurfaceProjection.Type(
            type,
            assembly,
            "asset:collision",
            "Collision.Assembly",
            qualifiedBudget,
            qualifyId: true);
        qualifiedBudget.CommitParticipant();

        Assert.Equal($"{assembly}:{qualified.DefinitionId}", qualified.Id);
        Assert.Equal(
            unqualifiedBudget.CommittedCharacters + assembly.Length + 1,
            qualifiedBudget.CommittedCharacters);
    }

    [Fact]
    public void ApiSurfacePolicy_AcceptsCoreLibraryAtEveryBrowserScope()
    {
        using var stream = File.OpenRead(typeof(object).Assembly.Location);
        using var reader = new PEReader(stream);

        foreach (ApiSurfaceExtractionScope scope in
            new[]
            {
                ApiSurfaceExtractionScope.PublicWithNonPublicTypes,
                ApiSurfaceExtractionScope.IncludeAll,
            })
        {
            stream.Position = 0;
            var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
                ApiSurfaceExtractor.ExtractBounded(
                    reader,
                    scope,
                    new ApiSurfaceExtractionBounds(
                        BrowserApiSurfacePolicy.MaxTypes,
                        BrowserApiSurfacePolicy.MaxMembers,
                        BrowserApiSurfacePolicy.MaxInspectionFailures,
                        BrowserApiSurfacePolicy.MaxTypeForwarders,
                        BrowserApiSurfacePolicy.MaxMetadataRows,
                        BrowserApiSurfacePolicy.MaxRetainedTextCharacters)));
            if (scope == ApiSurfaceExtractionScope.PublicWithNonPublicTypes)
            {
                var transportBudget =
                    new BrowserSurfaceProjection.BrowserSurfaceTextBudget(
                        BrowserApiSurfacePolicy.MaxRetainedTextCharacters);
                transportBudget.BeginParticipant();
                foreach (ApiType type in extracted.Surface.Types)
                {
                    BrowserSurfaceProjection.Type(
                        type,
                        "System.Private.CoreLib.dll",
                        "runtime:corelib",
                        "System.Private.CoreLib",
                        transportBudget);
                }
                transportBudget.CommitParticipant();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryExports_SelectOneExactPackageLibrary(bool useAssetId)
    {
        string PackageId = $"Browser.Library.Exact.{useAssetId}";
        const string SelectedLibrary = "Selected.Library";
        const string OtherLibrary = "Other.Library";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackageEntries(
                    ($"lib/net11.0/{SelectedLibrary}.dll",
                        BuildIntegrationImage(
                            SelectedLibrary,
                            "Microsoft.Extensions.DependencyInjection"
                            + ".IServiceCollection")),
                    ($"lib/net11.0/{OtherLibrary}.dll",
                        BuildIntegrationImage(
                            OtherLibrary,
                            "Amazon.S3.AmazonS3Client",
                            "Microsoft.Extensions.Logging.CustomLogger"))),
                fromCache: false));

        BrowserPackageMetadata metadata =
            Assert.IsType<BrowserPackageMetadata>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageMetadata(
                        PackageId,
                        "1.0.0",
                        "net11.0",
                        useAssetId
                            ? $"compile:lib/net11.0/{SelectedLibrary}.dll"
                            : "selected.library"),
                    BrowserMetadataJsonContext.Default
                        .BrowserPackageMetadata));
        Assert.Equal(
            $"{SelectedLibrary}.dll",
            Assert.Single(metadata.Assemblies).Assembly);

        BrowserLibraryMetrics metrics =
            Assert.IsType<BrowserLibraryMetrics>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports
                        .QueryPackageLibraryMetrics(
                            PackageId,
                            "1.0.0",
                            "net11.0",
                            useAssetId
                                ? $"compile:lib/net11.0/{SelectedLibrary}.dll"
                                : "selected.library"),
                    BrowserAnalysisJsonContext.Default
                        .BrowserLibraryMetrics));
        Assert.Equal("available", metrics.Outcome);
        Assert.True(
            Assert.IsType<BrowserLibraryMetricsPopulation>(
                metrics.Population)
                .PhysicalEvidenceBodyCount > 0);

        BrowserPackageIntegrations integrations =
            Assert.IsType<BrowserPackageIntegrations>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackageIntegrations(
                        PackageId,
                        "1.0.0",
                        "net11.0",
                        useAssetId
                            ? $"compile:lib/net11.0/{SelectedLibrary}.dll"
                            : $"{SelectedLibrary}.DLL"),
                    BrowserAnalysisJsonContext.Default
                        .BrowserPackageIntegrations));
        Assert.Contains(
            integrations.Categories,
            category =>
                category.Integration
                == EcosystemIntegrationNames.DependencyInjection);
        Assert.DoesNotContain(
            integrations.Categories,
            category =>
                category.Integration
            == EcosystemIntegrationNames.Logging);

        BrowserPackageOpportunities opportunities =
            Assert.IsType<BrowserPackageOpportunities>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackageOpportunities(
                        PackageId,
                        "1.0.0",
                        "net11.0",
                        useAssetId
                            ? $"compile:lib/net11.0/{OtherLibrary}.dll"
                            : OtherLibrary),
                    BrowserAnalysisJsonContext.Default
                        .BrowserPackageOpportunities));
        Assert.Contains(
            opportunities.Categories,
            category =>
                category.Integration
                == EcosystemIntegrationNames.Aspire);
        Assert.All(
            opportunities.Categories.SelectMany(
                category => category.Items),
            opportunity =>
                Assert.Equal(
                    OtherLibrary,
                    opportunity.SourceAssembly));
    }

    [Fact]
    public async Task LibraryParticipant_ExactSurfaceIdUsesProductImplementationCorrespondence()
    {
        const string packageId = "Browser.Library.Correspondence";
        const string fileName = "Renamed.Library.dll";
        byte[] implementation = File.ReadAllBytes(
            FixtureCatalog.InspectWebMethodBodies.AssemblyPath());
        int implementationTypeCount;
        using (var reader = new PEReader(new MemoryStream(implementation, writable: false)))
        {
            implementationTypeCount = reader.GetMetadataReader().TypeDefinitions.Count;
        }
        byte[] reference = BuildEmptySurfaceImage(
            typeof(PerformanceProbes).Assembly.GetName());
        int referenceTypeCount;
        using (var reader = new PEReader(new MemoryStream(reference, writable: false)))
        {
            referenceTypeCount = reader.GetMetadataReader().TypeDefinitions.Count;
        }
        Assert.NotEqual(referenceTypeCount, implementationTypeCount);
        _ = await Coordinate(
            packageId,
            PackagePair(reference, implementation, fileName));
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId, "1.0.0", "net11.0", TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserWorkspaceParticipant surface = Assert.Single(scope.SurfaceParticipants);
        BrowserWorkspaceParticipant selected = scope.LibraryParticipant(
            coordinate,
            surface.Asset.Id);

        Assert.Same(scope.ImplementationParticipant(surface), selected);
        Assert.NotSame(surface.Participant, selected.Participant);
        Assert.Same(surface, scope.TryGetSurfaceParticipant(selected));
        Assert.Same(selected, scope.LibraryParticipant(coordinate, "renamed.library"));
        Assert.Same(selected, scope.LibraryParticipant(
            coordinate,
            typeof(PerformanceProbes).Assembly.GetName().Name!));

        using JsonDocument metadata = JsonDocument.Parse(
            await DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageMetadata(
                packageId, "1.0.0", "net11.0", surface.Asset.Id));
        JsonElement metadataAssembly = Assert.Single(
            metadata.RootElement.GetProperty("assemblies").EnumerateArray());
        Assert.Equal(
            fileName,
            metadataAssembly.GetProperty("assembly").GetString());
        Assert.Equal(
            nameof(MetadataRootKind.Cli),
            Assert.Single(
                metadataAssembly
                    .GetProperty("metadataRoots")
                    .EnumerateArray())
                .GetProperty("requestedRoot")
                .GetString());
        Assert.Equal(
            JsonValueKind.Null,
            metadataAssembly.GetProperty("readyToRun").ValueKind);
        using JsonDocument table = JsonDocument.Parse(
            await DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageMetadataTable(
                packageId, "1.0.0", "net11.0", surface.Asset.Id,
                "cli",
                (int)TableIndex.TypeDef, 1, 10));
        Assert.Equal(
            implementationTypeCount,
            table.RootElement.GetProperty("rowCount").GetInt32());

        BrowserPackagePerformance performance = Assert.IsType<BrowserPackagePerformance>(
            JsonSerializer.Deserialize(
                await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackagePerformance(
                    packageId, "1.0.0", "net11.0", surface.Asset.Id),
                BrowserAnalysisJsonContext.Default.BrowserPackagePerformance));
        Assert.True(performance.TotalOpportunities > 0);
        Assert.Contains(performance.Members, member =>
            member.MemberName
                == nameof(PerformanceProbes.PerformanceBoxingProbe));
        Assert.Null(performance.InspectionError);
    }

    [Fact]
    public async Task LibraryParticipant_RejectsAmbiguousNamesAndMissingIds()
    {
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Browser.Library.Ambiguous",
            PackageEntries(
                ("lib/net11.0/First.dll",
                    BuildEmptySurfaceImage(new AssemblyName("First") { Version = new(1, 0, 0, 0) })),
                ("lib/net11.0/Second.dll",
                    BuildEmptySurfaceImage(new AssemblyName("First") { Version = new(2, 0, 0, 0) }))));
        await using BrowserInspectionScope scope =
            await BrowserInspectionScope.CreateAsync(
                [coordinate], TestContext.Current.CancellationToken);

        Assert.Contains("ambiguous", Assert.Throws<InvalidOperationException>(
            () => scope.LibraryParticipant(coordinate, "First")).Message);
        Assert.Contains("not part", Assert.Throws<InvalidOperationException>(
            () => scope.LibraryParticipant(
                coordinate, "compile:ref/net11.0/First.dll")).Message);
        Assert.Throws<ArgumentException>(() => scope.LibraryParticipant(coordinate, ""));
        foreach (BrowserWorkspaceParticipant participant in scope.SurfaceParticipants)
        {
            Assert.Same(participant, scope.LibraryParticipant(
                coordinate, participant.Asset.Id));
        }
    }

    [Fact]
    public async Task LibraryExports_ExactIdPreservesReferenceOnlySelection()
    {
        const string packageId = "Browser.Library.ReferenceOnly";
        const string assemblyName = "Reference.Library";
        _ = await Coordinate(
            packageId,
            Package(
                BuildIntegrationImage(
                    assemblyName,
                    "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                    "Amazon.S3.AmazonS3Client"),
                $"ref/net11.0/{assemblyName}.dll"));
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId, "1.0.0", "net11.0", TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserWorkspaceParticipant surface = Assert.Single(scope.SurfaceParticipants);
        Assert.Same(surface, scope.LibraryParticipant(coordinate, surface.Asset.Id));
        Assert.Empty(scope.ImplementationParticipants);

        BrowserLibraryMetrics metrics = Assert.IsType<BrowserLibraryMetrics>(
            JsonSerializer.Deserialize(
                await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackageLibraryMetrics(
                    packageId, "1.0.0", "net11.0", surface.Asset.Id),
                BrowserAnalysisJsonContext.Default.BrowserLibraryMetrics));
        Assert.Equal("unavailable", metrics.Outcome);
        Assert.Contains("no managed implementation assembly", metrics.Failure);
        Assert.Equal(
            BrowserAnalysisCompileLibraryStatus.Selected,
            metrics.CompileLibrary.Status);
        BrowserLibraryDependencyStructure dependency =
            Assert.IsType<BrowserLibraryDependencyStructure>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports
                        .QueryPackageLibraryDependencyStructure(
                            packageId,
                            "1.0.0",
                            "net11.0",
                            surface.Asset.Id),
                    BrowserAnalysisJsonContext.Default
                        .BrowserLibraryDependencyStructure));
        Assert.Equal(
            "unavailable",
            dependency.Outcome);
        Assert.Contains(
            "no managed implementation assembly",
            dependency.Failure);

        BrowserLibraryStructuralSalience salience =
            Assert.IsType<BrowserLibraryStructuralSalience>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports
                        .QueryPackageLibraryStructuralSalience(
                            packageId,
                            "1.0.0",
                            "net11.0",
                            surface.Asset.Id),
                    BrowserAnalysisJsonContext.Default
                        .BrowserLibraryStructuralSalience));
        Assert.Equal("available", salience.Surface.Outcome);
        Assert.Equal(
            "structural-salience.v3",
            salience.Surface.MethodologyVersion);
        Assert.True(
            salience.Surface.NamespaceIndex?.Coverage.Considered > 0);
        Assert.NotEmpty(salience.Surface.NamespaceIndex!.Namespaces);
        Assert.Equal(
            salience.Surface.NamespaceIndex.Namespaces.Length,
            salience.Surface.TypeLeverageShards.Length);
        Assert.Null(salience.Surface.Failure);
        Assert.Equal("unavailable", salience.Implementation.Outcome);
        Assert.Equal(
            "NoImplementationAssembly",
            salience.Implementation.FailureKind);
        Assert.Equal(
            BrowserAnalysisCompileLibraryStatus.Selected,
            salience.CompileLibrary.Status);

        BrowserPackageIntegrations integrations = Assert.IsType<BrowserPackageIntegrations>(
            JsonSerializer.Deserialize(
                await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackageIntegrations(
                    packageId, "1.0.0", "net11.0", surface.Asset.Id),
                BrowserAnalysisJsonContext.Default.BrowserPackageIntegrations));
        Assert.True(integrations.IsComplete);
        Assert.Contains(integrations.Categories, category =>
            category.Integration == EcosystemIntegrationNames.DependencyInjection);
        BrowserPackageOpportunities opportunities = Assert.IsType<BrowserPackageOpportunities>(
            JsonSerializer.Deserialize(
                await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackageOpportunities(
                    packageId, "1.0.0", "net11.0", surface.Asset.Id),
                BrowserAnalysisJsonContext.Default.BrowserPackageOpportunities));
        Assert.True(opportunities.IsComplete);
        Assert.Contains(opportunities.Categories, category =>
            category.Integration == EcosystemIntegrationNames.Aspire);
        BrowserPackagePerformance performance = Assert.IsType<BrowserPackagePerformance>(
            JsonSerializer.Deserialize(
                await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackagePerformance(
                    packageId, "1.0.0", "net11.0", surface.Asset.Id),
                BrowserAnalysisJsonContext.Default.BrowserPackagePerformance));
        Assert.Equal(0, performance.TotalOpportunities);
        Assert.Empty(performance.Members);
        Assert.Null(performance.InspectionError);
    }

    [Fact]
    public async Task LibraryDependencyStructure_ProjectsBoundedResult()
    {
        const string packageId =
            "Browser.Library.DependencyStructure";
        string assemblyPath =
            FixtureCatalog.ResearchDependencyStructure.AssemblyPath();
        string assemblyName = Path.GetFileName(assemblyPath);
        BrowserPackageCoordinate coordinate = await Coordinate(
            packageId,
            Package(
                File.ReadAllBytes(assemblyPath),
                $"lib/net11.0/{assemblyName}"));
        string assemblyId = Assert.Single(
            coordinate.Selection.Assets).Id;

        BrowserLibraryDependencyStructure dependency =
            Assert.IsType<BrowserLibraryDependencyStructure>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis
                        .AnalysisExports.QueryPackageLibraryDependencyStructure(
                            packageId,
                            "1.0.0",
                            "net11.0",
                            assemblyId),
                    BrowserAnalysisJsonContext.Default
                        .BrowserLibraryDependencyStructure));

        Assert.Equal("available", dependency.Outcome);
        Assert.Equal(
            LibraryDependencyStructure.CurrentMethodologyVersion,
            dependency.MethodologyVersion);
        Assert.NotNull(dependency.Population);
        Assert.NotEmpty(dependency.Namespaces);
        Assert.NotEmpty(dependency.NamespaceEdges);
        Assert.InRange(dependency.NamespaceEdges.Length, 1, 64);
        Assert.True(
            dependency.TotalNamespaceEdgeCount
                >= dependency.NamespaceEdges.Length);
        Assert.All(
            dependency.NamespaceEdges,
            edge => Assert.True(edge.Counts.Total > 0));
        BrowserLibraryDependencyTypeEdge explanation =
            Assert.Single(
                dependency.NamespaceEdges
                    .SelectMany(
                        edge => edge.ExplainingTypeEdges)
                    .Take(1));
        Assert.NotEmpty(explanation.SourceTypeKey);
        Assert.NotEmpty(explanation.TargetTypeKey);
        Assert.NotEmpty(explanation.SourceTypeDisplay);
        Assert.NotEmpty(explanation.TargetTypeDisplay);
    }

    [Fact]
    public async Task
        NamespaceTypeLeverage_PreservesExactGenericArityAcrossJson()
    {
        const string packageId = "Browser.Library.SurfaceLeverageIdentity";
        string assemblyPath =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        string assemblyName =
            Assert.IsType<string>(
                AssemblyName.GetAssemblyName(assemblyPath).Name);
        _ = await Coordinate(
            packageId,
            Package(
                File.ReadAllBytes(assemblyPath),
                $"lib/net11.0/{assemblyName}.dll"));
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                "1.0.0",
                "net11.0",
                TestContext.Current.CancellationToken);
        BrowserWorkspaceParticipant surface =
            Assert.Single(scopeLease.Scope.SurfaceParticipants);

        BrowserLibraryStructuralSalience salience =
            Assert.IsType<BrowserLibraryStructuralSalience>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports
                        .QueryPackageLibraryStructuralSalience(
                            packageId,
                            "1.0.0",
                            "net11.0",
                            surface.Asset.Id),
                    BrowserAnalysisJsonContext.Default
                        .BrowserLibraryStructuralSalience));

        Assert.Equal("available", salience.Surface.Outcome);
        Assert.Equal("available", salience.Implementation.Outcome);
        BrowserLibraryTypeLeverageShard leverage =
            Assert.Single(
                salience.Surface.TypeLeverageShards,
                shard => shard.Namespace == "Target");
        Assert.Contains(
            leverage.Types,
            type => type.TypeDefinitionId == "Target.Box`1");
        Assert.Contains(
            leverage.Types,
            type => type.TypeDefinitionId == "Target.Box`2");
        Assert.All(
            leverage.SeaLevelOrder,
            id => Assert.Contains(
                leverage.Types,
                type => type.TypeDefinitionId == id));
        Assert.Contains(
            salience.Implementation.TypeLeverageShards,
            shard => shard.Namespace == "Target");
        Assert.All(
            leverage.MountainPeakOrder,
            id => Assert.Contains(
                leverage.Types,
                type => type.TypeDefinitionId == id));
    }

    [Fact]
    public void LibraryMetricsTypeKey_PreservesGenericArityHiddenByDisplay()
    {
        ILInspector.Analysis.TypeRef oneArgument =
            ILInspector.Analysis.TypeRef.Definition(
            "Target", "Target", "Box`1");
        ILInspector.Analysis.TypeRef twoArguments =
            ILInspector.Analysis.TypeRef.Definition(
            "Target", "Target", "Box`2");

        Assert.Equal(
            oneArgument.ToQualifiedDisplayString(),
            twoArguments.ToQualifiedDisplayString());
        Assert.NotEqual(
            DotnetInspect.Web.Interop.Analysis.AnalysisExports
                .LibraryMetricsTypeKey(oneArgument),
            DotnetInspect.Web.Interop.Analysis.AnalysisExports
                .LibraryMetricsTypeKey(twoArguments));
    }

    [Fact]
    public async Task PackageDependencies_SerializesSharedEnvelopeWithoutLibraryState()
    {
        const string packageId = "Browser.Dependency.Root";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net11.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
                 <dependencies>
                   <group targetFramework=".NETCoreApp,Version=v11.0">
                     <dependency id="Browser.Dependency.Child" version="[2.0.0]" />
                   </group>
                 </dependencies>
               </metadata>
             </package>
             """);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        string json = await PackageExports.InspectPackageDependencies(
            packageId,
            "1.0.0",
            "net11.0",
            runtimeIdentifier: null);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement inspection = root.GetProperty("inspection");
        JsonElement content = inspection.GetProperty("content");
        Assert.Equal("available", content.GetProperty("kind").GetString());
        Assert.Equal(
            packageId.ToLowerInvariant(),
            content.GetProperty("manifest")
                .GetProperty("coordinate")
                .GetProperty("packageId")
                .GetString());
        JsonElement dependencyGroups =
            content.GetProperty("dependencies");
        Assert.Equal(
            "net11.0",
            dependencyGroups.GetProperty("requestedTargetFramework")
                .GetString());
        JsonElement group = Assert.Single(
            dependencyGroups.GetProperty("groups").EnumerateArray());
        JsonElement dependency = Assert.Single(
            group.GetProperty("dependencies").EnumerateArray());
        Assert.Equal(
            "Browser.Dependency.Child",
            dependency.GetProperty("id").GetString());
        Assert.Equal(
            "nonProjectable",
            inspection.GetProperty("share").GetProperty("kind").GetString());
        Assert.Empty(inspection.GetProperty("diagnostics").EnumerateArray());
        Assert.Empty(root.GetProperty("platformReferences").EnumerateArray());
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("defaultPlatformReference").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("platformReferenceError").ValueKind);
        Assert.False(root.TryGetProperty("package", out _));
        Assert.False(root.TryGetProperty("assembly", out _));
        Assert.False(root.TryGetProperty("compileLibrary", out _));
        Assert.False(root.TryGetProperty("assemblyReferences", out _));
        Assert.False(root.TryGetProperty("declarationFailures", out _));
    }

    [Fact]
    public async Task LibraryReferences_ReturnsSharedAvailableEnvelope()
    {
        const string packageId = "Browser.Reference.Root";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net11.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
               </metadata>
             </package>
             """);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        string json = await PackageExports.QueryLibraryReferences(
            packageId,
            "1.0.0",
            "net11.0",
            $"{packageId}.dll");

        InspectionEnvelope<LibraryReferenceInspectionOutcome> envelope =
            Assert.IsType<InspectionEnvelope<LibraryReferenceInspectionOutcome>>(
                JsonSerializer.Deserialize(
                    json,
                    BrowserPackageJsonContext.Default
                        .InspectionEnvelopeLibraryReferenceInspectionOutcome));
        LibraryReferenceInspectionOutcome.Available available =
            Assert.IsType<LibraryReferenceInspectionOutcome.Available>(
                envelope.Content);
        Assert.NotEmpty(available.References);
        Assert.Equal(
            available.References
                .OrderBy(
                    static reference => reference.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static reference => reference.Name,
                    StringComparer.Ordinal)
                .ThenBy(
                    static reference => reference.Version,
                    StringComparer.Ordinal)
                .ThenBy(
                    static reference => reference.Culture,
                    StringComparer.Ordinal)
                .ThenBy(
                    static reference => reference.PublicKeyToken,
                    StringComparer.Ordinal),
            available.References);
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public async Task PackageDependencies_RuntimeIdentifierDoesNotSelectLibraryState()
    {
        string packageId = $"Browser.Dependency.Runtime.{Guid.NewGuid():N}";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageEntries(
            (
                $"runtimes/linux-x64/lib/net11.0/{packageId}.dll",
                [0x01]),
            (
                $"runtimes/win-x64/lib/net11.0/{packageId}.dll",
                image),
            (
                $"{packageId}.nuspec",
                Encoding.UTF8.GetBytes(
                    $"""
                     <package>
                       <metadata>
                         <id>{packageId}</id>
                         <version>1.0.0</version>
                       </metadata>
                     </package>
                     """)));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        PackageDependencyInspectionOutcome.Available windows =
            AssertAvailableDependencies(
                await InspectPackageDependencies(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    "win-x64"));
        PackageDependencyInspectionOutcome.Available linux =
            AssertAvailableDependencies(
                await InspectPackageDependencies(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    "linux-x64"));

        Assert.Equal(windows.Manifest.Coordinate, linux.Manifest.Coordinate);
        Assert.Equal(
            windows.Dependencies.SelectionStatus,
            linux.Dependencies.SelectionStatus);
        Assert.Equal(
            windows.Dependencies.Groups,
            linux.Dependencies.Groups);
    }

    [Fact]
    public async Task PackageDependencies_EmptyModernGroupDefaultsToRuntime()
    {
        string packageId = $"Browser.Dependency.Runtime.{Guid.NewGuid():N}";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net10.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
                 <dependencies>
                   <group targetFramework="net10.0" />
                 </dependencies>
               </metadata>
             </package>
             """);
        var handler = new GalleryPackageHandler(
            packageId,
            "1.0.0",
            nupkg);
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageRealization realization = Assert.IsType<
            BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                packageId,
                "1.0.0",
                "net10.0",
                source,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken)).Realization;

        BrowserPackagePlatformReferenceProjection projection =
            BrowserPackagePlatformReferences.Project(realization.Coordinate);
        BrowserPackagePlatformReferenceProjection.Row platformReference =
            Assert.IsType<BrowserPackagePlatformReferenceProjection.Row>(
                projection.DefaultReference);
        Assert.Equal(
            BrowserPlatformWorkspace.RuntimeFamily,
            platformReference.Family);
        Assert.Equal(
            ".NET Runtime",
            platformReference.Label);
        Assert.Empty(projection.References);
        Assert.Null(projection.Error);
    }

    [Fact]
    public async Task PackageDependencies_UnmatchedGroupDoesNotDefaultToRuntime()
    {
        string packageId = $"Browser.Dependency.Unmatched.{Guid.NewGuid():N}";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net10.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
                 <dependencies>
                   <group targetFramework="net11.0">
                     <dependency id="Browser.Dependency.Child" version="[2.0.0]" />
                   </group>
                 </dependencies>
               </metadata>
             </package>
             """);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        BrowserPackageDependencyInspection inspection =
            await InspectPackageDependencies(
                packageId,
                "1.0.0",
                "net10.0");
        PackageDependencyInspectionOutcome.Available available =
            AssertAvailableDependencies(inspection);
        Assert.Equal(
            PackageDependencyGroupSelectionStatus.NoMatchingTargetFramework,
            available.Dependencies.SelectionStatus);
        Assert.Null(available.Dependencies.SelectedGroup);
        Assert.Empty(inspection.PlatformReferences);
        Assert.Null(inspection.DefaultPlatformReference);
        Assert.Null(inspection.PlatformReferenceError);
    }

    [Fact]
    public async Task PackageDependencies_NonemptySelectionSuppressesEmptyAlternativeDefault()
    {
        string packageId = $"Browser.Dependency.Alternative.{Guid.NewGuid():N}";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net10.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
                 <dependencies>
                   <group targetFramework="net10.0">
                     <dependency id="Browser.Dependency.Child" version="[2.0.0]" />
                   </group>
                   <group targetFramework="net9.0" />
                 </dependencies>
               </metadata>
             </package>
             """);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        BrowserPackageDependencyInspection inspection =
            await InspectPackageDependencies(
                packageId,
                "1.0.0",
                "net10.0");
        PackageDependencyInspectionOutcome.Available available =
            AssertAvailableDependencies(inspection);
        Assert.Equal(2, available.Dependencies.Groups.Length);
        Assert.NotEmpty(available.Dependencies.Groups[0].Dependencies);
        Assert.Empty(available.Dependencies.Groups[1].Dependencies);
        Assert.Null(inspection.DefaultPlatformReference);
        Assert.Empty(inspection.PlatformReferences);
    }

    [Fact]
    public async Task PackageDependencies_ProjectsExplicitAspNetCoreReference()
    {
        string packageId =
            $"Browser.Dependency.AspNetCore.{Guid.NewGuid():N}";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net8.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
                 <dependencies>
                   <group targetFramework="net8.0">
                     <dependency id="Browser.Dependency.Child" version="[2.0.0]" />
                   </group>
                 </dependencies>
                 <frameworkReferences>
                   <group targetFramework="net8.0">
                     <frameworkReference name="Microsoft.AspNetCore.App" />
                   </group>
                 </frameworkReferences>
               </metadata>
             </package>
             """);
        var handler = new GalleryPackageHandler(
            packageId,
            "1.0.0",
            nupkg);
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageRealization realization = Assert.IsType<
            BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                packageId,
                "1.0.0",
                "net8.0",
                source,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken)).Realization;

        BrowserPackagePlatformReferenceProjection projection =
            BrowserPackagePlatformReferences.Project(realization.Coordinate);
        BrowserPackagePlatformReferenceProjection.Row platformReference =
            Assert.Single(projection.References);
        Assert.Equal(
            BrowserPlatformWorkspace.AspNetCoreFamily,
            platformReference.Family);
        Assert.Equal(
            "ASP.NET Core",
            platformReference.Label);
        Assert.Null(projection.DefaultReference);
        Assert.Null(projection.Error);
    }

    [Fact]
    public async Task LibraryReferences_ReturnsSharedRejectedEnvelope()
    {
        const string packageId = "Browser.Reference.Invalid";
        byte[] nupkg = PackageWithManifest(
            [0x00, 0x01, 0x02, 0x03],
            $"lib/net11.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
               </metadata>
             </package>
             """);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        string json = await PackageExports.QueryLibraryReferences(
            packageId,
            "1.0.0",
            "net11.0",
            $"{packageId}.dll");

        InspectionEnvelope<LibraryReferenceInspectionOutcome> envelope =
            Assert.IsType<InspectionEnvelope<LibraryReferenceInspectionOutcome>>(
                JsonSerializer.Deserialize(
                    json,
                    BrowserPackageJsonContext.Default
                        .InspectionEnvelopeLibraryReferenceInspectionOutcome));
        LibraryReferenceInspectionOutcome.Rejected rejected =
            Assert.IsType<LibraryReferenceInspectionOutcome.Rejected>(
                envelope.Content);
        Assert.Equal(
            AssemblyReferenceRowsRejectionKind.InvalidImage,
            rejected.Reason.Kind);
        Assert.NotEmpty(rejected.Reason.Detail.ToString());
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            nameof(AssemblyReferenceRowsRejectionKind.InvalidImage),
            document.RootElement
                .GetProperty("content")
                .GetProperty("reason")
                .GetProperty("kind")
                .GetString());
    }

    [Fact]
    public async Task PackageDependencies_UsesCompatibleAssetsWithoutChangingRequestedFramework()
    {
        const string packageId = "Browser.Dependency.Compatible";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net6.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
                 <dependencies>
                   <group targetFramework=".NETStandard2.0">
                     <dependency id="Browser.Dependency.Child" version="[2.0.0]" />
                   </group>
                 </dependencies>
               </metadata>
             </package>
             """);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        BrowserPackageSurface surface = await QueryPackageSurface(
            packageId,
            "1.0.0",
            "net8.0");

        Assert.Equal("net8.0", surface.ActiveFramework);
        Assert.Contains("net8.0", surface.Frameworks);
        Assert.Contains("net6.0", surface.Frameworks);
        Assert.Equal(
            BrowserCompileLibraryStatus.Selected,
            surface.CompileLibrary.Status);
        Assert.Equal("net6.0", surface.CompileLibrary.TargetFramework);

        PackageDependencyInspectionOutcome.Available dependencies =
            AssertAvailableDependencies(
                await InspectPackageDependencies(
                    packageId,
                    "1.0.0",
                    "net8.0"));
        Assert.Equal(
            "net8.0",
            dependencies.Dependencies.RequestedTargetFramework);
        DeclaredPackageDependencyGroup group =
            Assert.Single(dependencies.Dependencies.Groups);
        Assert.Equal(".NETStandard2.0", group.TargetFramework);
        Assert.Equal(0, dependencies.Dependencies.SelectedGroupIndex);
        Assert.Equal(
            group.TargetFramework,
            dependencies.Dependencies.SelectedGroup?.TargetFramework);
        Assert.Equal(
            group.Dependencies.ToArray(),
            dependencies.Dependencies.SelectedGroup?.Dependencies.ToArray());
        Assert.Equal(
            "Browser.Dependency.Child",
            Assert.Single(group.Dependencies).Id);
        Assert.Equal(
            PackageDependencyGroupSelectionStatus.Selected,
            dependencies.Dependencies.SelectionStatus);
    }

    [Fact]
    public async Task PackageDependencies_SelectsUngroupedDependenciesWithCompatibleAssets()
    {
        const string packageId = "Browser.Dependency.Ungrouped";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net6.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
                 <dependencies>
                   <dependency id="Browser.Dependency.Child" version="[2.0.0]" />
                 </dependencies>
               </metadata>
             </package>
             """);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        PackageDependencyInspectionOutcome.Available dependencies =
            AssertAvailableDependencies(
                await InspectPackageDependencies(
                    packageId,
                    "1.0.0",
                    "net8.0"));
        Assert.Equal(
            "net8.0",
            dependencies.Dependencies.RequestedTargetFramework);
        DeclaredPackageDependencyGroup group =
            Assert.Single(dependencies.Dependencies.Groups);
        Assert.Equal("any", group.TargetFramework);
        Assert.True(group.IsImplicitManifestGroup);
        Assert.Equal(0, dependencies.Dependencies.SelectedGroupIndex);
        Assert.Equal(
            group.TargetFramework,
            dependencies.Dependencies.SelectedGroup?.TargetFramework);
        Assert.Equal(
            group.Dependencies.ToArray(),
            dependencies.Dependencies.SelectedGroup?.Dependencies.ToArray());
        Assert.Equal(
            "Browser.Dependency.Child",
            Assert.Single(group.Dependencies).Id);
        Assert.Equal(
            PackageDependencyGroupSelectionStatus.Selected,
            dependencies.Dependencies.SelectionStatus);
    }

    [Fact]
    public async Task PackageDependencies_BlankDeclaredFrameworkDoesNotAbortProjection()
    {
        string packageId = $"Blank.Dependency.Framework.{Guid.NewGuid():N}";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net11.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
                 <dependencies>
                   <group targetFramework="net11.0">
                     <dependency id="Valid.Dependency" version="[1.0.0]" />
                   </group>
                   <group targetFramework="" />
                 </dependencies>
               </metadata>
             </package>
             """);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        PackageDependencyInspectionOutcome.Available dependencies =
            AssertAvailableDependencies(
                await InspectPackageDependencies(
                    packageId,
                    "1.0.0",
                    "net11.0"));

        Assert.Equal(2, dependencies.Dependencies.Groups.Length);
        Assert.Equal(
            "net11.0",
            dependencies.Dependencies.Groups[0].TargetFramework);
        Assert.Equal(
            "",
            dependencies.Dependencies.Groups[1].TargetFramework);
        Assert.Equal(
            "Valid.Dependency",
            Assert.Single(
                dependencies.Dependencies.Groups[0].Dependencies).Id);
        Assert.Equal(
            PackageDependencyGroupSelectionStatus.Selected,
            dependencies.Dependencies.SelectionStatus);
    }

    [Fact]
    public async Task PackageDependencyConflictsRemainVisibleInDependencies()
    {
        string packageId = $"Browser.Dependency.Conflict.{Guid.NewGuid():N}";
        const string dependencyId = "Browser.Dependency.Conflicting.Child";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net11.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
                 <dependencies>
                   <group targetFramework="net11.0">
                     <dependency id="{dependencyId}" version="[1.0.0]" />
                     <dependency id="{dependencyId.ToLowerInvariant()}" version="[2.0.0]" />
                   </group>
                 </dependencies>
               </metadata>
             </package>
             """);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        BrowserPackageDependencyInspection inspection =
            await InspectPackageDependencies(
                packageId,
                "1.0.0",
                "net11.0");
        PackageDependencyInspectionOutcome.Available dependencies =
            AssertAvailableDependencies(inspection);
        DeclaredPackageDependencyGroup group =
            Assert.Single(dependencies.Dependencies.Groups);
        Assert.Equal(0, dependencies.Dependencies.SelectedGroupIndex);
        Assert.Equal(
            group.TargetFramework,
            dependencies.Dependencies.SelectedGroup?.TargetFramework);
        Assert.Equal(
            group.Dependencies.ToArray(),
            dependencies.Dependencies.SelectedGroup?.Dependencies.ToArray());
        Assert.Collection(
            group.Dependencies,
            first =>
            {
                Assert.Equal(dependencyId, first.Id);
                Assert.Equal("[1.0.0]", first.VersionRange);
            },
            second =>
            {
                Assert.Equal(dependencyId.ToLowerInvariant(), second.Id);
                Assert.Equal("[2.0.0]", second.VersionRange);
            });
        Assert.Null(inspection.DefaultPlatformReference);
    }

    [Fact]
    public async Task PackageVulnerabilities_ProjectsExactReviewedAdvisoryEvidence()
    {
        const string packageId = "Example.Vulnerable";
        const string version = "1.2.3";
        const string advisory =
            """
            [
              {
                "ghsa_id": "GHSA-aaaa-bbbb-cccc",
                "cve_id": "CVE-2026-1234",
                "type": "reviewed",
                "severity": "high",
                "published_at": "2026-09-09T16:04:11Z",
                "updated_at": "2026-09-09T18:00:00Z",
                "withdrawn_at": null,
                "vulnerabilities": [
                  {
                    "package": {
                      "ecosystem": "nuget",
                      "name": "Example.Vulnerable"
                    },
                    "vulnerable_version_range": "< 2.0.0",
                    "first_patched_version": "2.0.0"
                  }
                ]
              }
            ]
            """;
        using var handler = new JsonResponseHandler(advisory);
        using var client = new HttpClient(handler);
        var service = new GitHubNuGetAdvisoryService(client);

        BrowserPackageVulnerabilityResult result =
            await PackageExports.ExecutePackageVulnerabilitiesAsync(
                packageId,
                version,
                service,
                TestContext.Current.CancellationToken);

        Assert.Equal(packageId.ToLowerInvariant(), result.Package);
        Assert.Equal(version, result.Version);
        Assert.Equal(
            BrowserPackageVulnerabilityAvailability.Complete,
            result.Availability);
        Assert.Empty(result.Failures);
        BrowserPackageVulnerabilityAdvisory projected =
            Assert.Single(result.Advisories);
        Assert.Equal("GHSA-AAAA-BBBB-CCCC", projected.GhsaId);
        Assert.Equal("CVE-2026-1234", projected.CveId);
        Assert.Equal(
            BrowserPackageVulnerabilitySeverity.High,
            projected.Severity);
        Assert.Equal(
            "https://github.com/advisories/GHSA-AAAA-BBBB-CCCC",
            projected.AdvisoryUrl);
    }

    [Fact]
    public async Task PackagePerformance_UsesProductRankedWorkspaceAnalysis()
    {
        const string PackageId = "Browser.Performance.Root";
        byte[] surface = File.ReadAllBytes(
            FixtureCatalog.InspectWebMethodBodies.AssetPath("reference"));
        byte[] implementation = File.ReadAllBytes(
            FixtureCatalog.InspectWebMethodBodies.AssemblyPath());
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    surface,
                    implementation,
                    $"{PackageId}.DLL",
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await QueryPackageSurfaceJson(
            PackageId,
            "1.0.0",
            "net11.0");
        string json = await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackagePerformance(
            PackageId,
            "1.0.0",
            "net11.0",
            $"{PackageId}.dll");

        using JsonDocument surfaceDocument =
            JsonDocument.Parse(surfaceJson);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.True(
            root.GetProperty("totalOpportunities").GetInt32() > 0);
        JsonElement member = Assert.Single(
            root.GetProperty("members").EnumerateArray(),
            candidate =>
                candidate.GetProperty("memberName").GetString()
                == nameof(PerformanceProbes.PerformanceBoxingProbe));
        Assert.Equal(
            typeof(PerformanceProbes).FullName,
            member.GetProperty("typeId").GetString());
        Assert.Equal(
            [
                typeof(PerformanceProbes)
                    .GetMethod(
                        nameof(PerformanceProbes.PerformanceBoxingProbe))!
                    .MetadataToken,
            ],
            member.GetProperty("bodyTokens")
                .EnumerateArray()
                .Select(token => token.GetInt32()));
        Assert.StartsWith(
            $"{nameof(PerformanceProbes.PerformanceBoxingProbe)}~",
            member.GetProperty("stableSelector").GetString());
        JsonElement privateMember = Assert.Single(
            root.GetProperty("members").EnumerateArray(),
            candidate =>
                candidate.GetProperty("memberName").GetString()
                == "PerformancePrivateBoxingProbe");
        Assert.Equal(
            [
                typeof(PerformanceProbes)
                    .GetMethod(
                        "PerformancePrivateBoxingProbe",
                        BindingFlags.Static | BindingFlags.NonPublic)!
                    .MetadataToken,
            ],
            privateMember.GetProperty("bodyTokens")
                .EnumerateArray()
                .Select(token => token.GetInt32()));
        Assert.StartsWith(
            "PerformancePrivateBoxingProbe~",
            privateMember.GetProperty("stableSelector").GetString());
        JsonElement privateBody = Assert.Single(
            privateMember.GetProperty("bodyTargets").EnumerateArray());
        Assert.Equal(
            "PerformancePrivateBoxingProbe",
            privateBody.GetProperty("memberName").GetString());
        Assert.True(
            root.GetProperty("nonPublicOpportunities").GetInt32() > 0);
        JsonElement surfaceType = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                == member.GetProperty("typeId").GetString()
                && candidate.GetProperty("assembly").GetString()
                == member.GetProperty("assembly").GetString());
        Assert.Contains(
            surfaceType.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("stableSelector").GetString()
                == member.GetProperty("stableSelector").GetString());
        Assert.DoesNotContain(
            surfaceType.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("stableSelector").GetString()
                == privateMember.GetProperty("stableSelector").GetString());
        Assert.Contains(
            member.GetProperty("shapes").EnumerateArray(),
            shape => shape.GetString() == "box-value-type");
        Assert.True(
            member.GetProperty("opportunityCount").GetInt32()
            > 0);
        Assert.True(
            !root.TryGetProperty(
                "inspectionError",
                out JsonElement inspectionError)
            || inspectionError.ValueKind == JsonValueKind.Null);

        JsonElement property = Assert.Single(
            root.GetProperty("members").EnumerateArray(),
            candidate =>
                candidate.GetProperty("memberName").GetString()
                == nameof(PerformanceProbes.PerformanceBoxingProperty));
        Assert.StartsWith(
            $"{nameof(PerformanceProbes.PerformanceBoxingProperty)}~",
            property.GetProperty("stableSelector").GetString());
        Assert.Equal(
            [
                typeof(PerformanceProbes)
                    .GetProperty(
                        nameof(PerformanceProbes.PerformanceBoxingProperty))!
                    .GetMethod!
                    .MetadataToken,
            ],
            property.GetProperty("bodyTokens")
                .EnumerateArray()
                .Select(token => token.GetInt32()));

        JsonElement nested = Assert.Single(
            root.GetProperty("members").EnumerateArray(),
            candidate =>
                candidate.GetProperty("memberName").GetString()
                == nameof(PerformanceProbes.PerformanceNestedProbe.Box));
        Assert.Equal(
            typeof(PerformanceProbes.PerformanceNestedProbe).FullName,
            nested.GetProperty("typeId").GetString());
    }

    [Fact]
    public async Task
        PackagePerformance_PrivateHiddenMemberPopulationUsesImplementation()
    {
        const string PackageId = "Browser.Performance.Private.Pair";
        const string AssemblyFileName = "DotnetInspect.Web.Tests.dll";
        byte[] implementation = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] surface = BuildSurfaceImageWithType(
            typeof(BrowserEngineBoundaryTests).Assembly.GetName(),
            "ReferenceOnly");
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(surface, implementation, AssemblyFileName),
                fromCache: false));

        BrowserPackagePerformance performance =
            Assert.IsType<BrowserPackagePerformance>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports
                        .QueryPackagePerformance(
                            PackageId,
                            "1.0.0",
                            "net11.0",
                            AssemblyFileName),
                    BrowserAnalysisJsonContext.Default
                        .BrowserPackagePerformance));
        BrowserPerformanceMember ranked = Assert.Single(
            performance.Members,
            member =>
                member.MemberName
                == nameof(PerformancePrivateBoxingProbe));
        Assert.Equal(
            typeof(BrowserEngineBoundaryTests).FullName,
            ranked.DeclaringType.DefinitionId);

        BrowserTypeMemberPopulationInspection ordinary =
            Assert.IsType<BrowserTypeMemberPopulationInspection>(
                JsonSerializer.Deserialize(
                    await MetadataExports.QueryTypeMemberPopulation(
                        PackageId,
                        "1.0.0",
                        "net11.0",
                        AssemblyFileName,
                        typeof(BrowserEngineBoundaryTests).FullName!,
                        "csharp",
                        "all"),
                    BrowserMetadataJsonContext.Default
                        .BrowserTypeMemberPopulationInspection));
        Assert.Equal(
            BrowserTypeMemberPopulationOutcome.Failed,
            ordinary.Outcome);
        Assert.Equal("The exact Type was not found.", ordinary.Detail);
        Assert.Null(ordinary.Population);

        BrowserTypeMemberPopulationInspection performancePopulation =
            Assert.IsType<BrowserTypeMemberPopulationInspection>(
                JsonSerializer.Deserialize(
                    await MetadataExports.QueryPerformanceTypeMemberPopulation(
                        PackageId,
                        "1.0.0",
                        "net11.0",
                        AssemblyFileName,
                        typeof(BrowserEngineBoundaryTests).FullName!,
                        "csharp",
                        "all"),
                    BrowserMetadataJsonContext.Default
                        .BrowserTypeMemberPopulationInspection));
        Assert.Equal(
            BrowserTypeMemberPopulationOutcome.Available,
            performancePopulation.Outcome);
        Assert.Contains(
            Assert.IsType<BrowserTypeMemberPopulation>(
                    performancePopulation.Population)
                .Groups
                .SelectMany(group => group.Members),
            member =>
                member.Name == nameof(PerformancePrivateBoxingProbe)
                && member.StableSelector == ranked.StableSelector);
    }

    [Fact]
    public async Task TypeAndMemberInspection_RetainsExactRuntimeIdentifier()
    {
        const string PackageId = "Browser.Type.Runtime";
        const string AssemblyFileName = "DotnetInspect.Web.Tests.dll";
        const string RuntimeIdentifier = "win-x64";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackageEntries(
                    (
                        $"runtimes/linux-x64/lib/net11.0/{AssemblyFileName}",
                        [0x01]),
                    (
                        $"runtimes/{RuntimeIdentifier}/lib/net11.0/{AssemblyFileName}",
                        image)),
                fromCache: false));

        BrowserTypeOverviewInspection overview =
            Assert.IsType<BrowserTypeOverviewInspection>(
                JsonSerializer.Deserialize(
                    await MetadataExports.QueryTypeOverviewDocument(
                        PackageId,
                        "1.0.0",
                        "net11.0",
                        AssemblyFileName,
                        typeof(BrowserEngineBoundaryTests).FullName!,
                        "csharp",
                        "public",
                        RuntimeIdentifier),
                    BrowserMetadataJsonContext.Default
                        .BrowserTypeOverviewInspection));
        Assert.Equal(BrowserTypeOverviewOutcome.Available, overview.Outcome);

        BrowserTypeMemberPopulationInspection ordinary =
            Assert.IsType<BrowserTypeMemberPopulationInspection>(
                JsonSerializer.Deserialize(
                    await MetadataExports.QueryTypeMemberPopulation(
                        PackageId,
                        "1.0.0",
                        "net11.0",
                        AssemblyFileName,
                        typeof(BrowserEngineBoundaryTests).FullName!,
                        "csharp",
                        "public",
                        RuntimeIdentifier),
                    BrowserMetadataJsonContext.Default
                        .BrowserTypeMemberPopulationInspection));
        Assert.Equal(
            BrowserTypeMemberPopulationOutcome.Available,
            ordinary.Outcome);

        BrowserTypeMemberPopulationInspection performance =
            Assert.IsType<BrowserTypeMemberPopulationInspection>(
                JsonSerializer.Deserialize(
                    await MetadataExports
                        .QueryPerformanceTypeMemberPopulation(
                            PackageId,
                            "1.0.0",
                            "net11.0",
                            AssemblyFileName,
                            typeof(BrowserEngineBoundaryTests).FullName!,
                            "csharp",
                            "all",
                            RuntimeIdentifier),
                    BrowserMetadataJsonContext.Default
                        .BrowserTypeMemberPopulationInspection));
        Assert.Equal(
            BrowserTypeMemberPopulationOutcome.Available,
            performance.Outcome);
        Assert.Contains(
            Assert.IsType<BrowserTypeMemberPopulation>(
                    performance.Population)
                .Groups
                .SelectMany(group => group.Members),
            member =>
                member.Name == nameof(PerformancePrivateBoxingProbe));
    }

    static async Task<BrowserPackageDependencyInspection>
        InspectPackageDependencies(
            string packageId,
            string version,
            string targetFramework,
            string? runtimeIdentifier = null) =>
        Assert.IsType<BrowserPackageDependencyInspection>(
            JsonSerializer.Deserialize(
                await PackageExports.InspectPackageDependencies(
                    packageId,
                    version,
                    targetFramework,
                    runtimeIdentifier),
                BrowserPackageJsonContext.Default
                    .BrowserPackageDependencyInspection));

    static PackageDependencyInspectionOutcome.Available
        AssertAvailableDependencies(
            BrowserPackageDependencyInspection inspection) =>
        Assert.IsType<PackageDependencyInspectionOutcome.Available>(
            inspection.Inspection.Content);
}
