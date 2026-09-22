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
            package.Content.EnumerateEntriesWithLengths(),
            package.Content.EnumerateEntriesWithLengths());
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
        var stats = new BrowserPackageCacheSnapshot(1, 2, 12, 3, 4, 256, 5, 128, 64);
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
            new BrowserPackageCacheStats(1, 2, 12, 3, 4, 256, 5, 128, 64),
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
    public async Task
        QueryMemberDocumentation_UsesSharedPackageDocumentationContract()
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
                    "System.Text.Json.dll",
                    "M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.JsonSerializerOptions)");
        CompiledDocumentationOutcome outcome =
            Assert.IsAssignableFrom<CompiledDocumentationOutcome>(
                JsonSerializer.Deserialize(
                    json,
                    CompiledDocumentationQueryJsonContext.Default
                        .CompiledDocumentationOutcome));

        var available =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                outcome);
        Assert.Equal(
            CompiledDocumentationSourceKind.Package,
            available.Source.Kind);
        Assert.Contains(
            "Converts the JsonDocument",
            available.Documentation.Summary,
            StringComparison.Ordinal);
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
        CompiledDocumentationOutcome outcome =
            Assert.IsAssignableFrom<CompiledDocumentationOutcome>(
                JsonSerializer.Deserialize(
                    json,
                    CompiledDocumentationQueryJsonContext.Default
                        .CompiledDocumentationOutcome));

        var available =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                outcome);
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
        CompiledDocumentationOutcome outcome =
            Assert.IsAssignableFrom<CompiledDocumentationOutcome>(
                JsonSerializer.Deserialize(
                    json,
                    CompiledDocumentationQueryJsonContext.Default
                        .CompiledDocumentationOutcome));

        var absent =
            Assert.IsType<CompiledDocumentationOutcome.Absent>(
                outcome);
        CompiledDocumentationSourceEvidence source =
            Assert.Single(absent.Sources);
        Assert.Equal(
            CompiledDocumentationSourceEvidenceKind.Absent,
            source.Kind);
        Assert.Equal(
            CompiledDocumentationSourceKind.Package,
            source.Source.Kind);
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
        CompiledDocumentationOutcome outcome =
            Assert.IsAssignableFrom<CompiledDocumentationOutcome>(
                JsonSerializer.Deserialize(
                    json,
                    CompiledDocumentationQueryJsonContext.Default
                        .CompiledDocumentationOutcome));

        var available =
            Assert.IsType<CompiledDocumentationOutcome.Available>(
                outcome);
        Assert.Equal(expectedSummary, available.Documentation.Summary);
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
            bool includeDocumentation)
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
        PackageSourceAuthorization authorized =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);
        var sourceAuthorization =
            new FixedPackageSourceAuthorization(authorized);
        using IPackageSourceClient packageClient =
            PackageSourceClientFactory.CreateGallery(
                authorized.Authorities[0].Association,
                new GalleryPackageHandler(
                    "microsoft.netcore.app.ref",
                    version,
                    referencePackage));

        return await BrowserPlatformWorkspace
            .QueryMemberDocumentationAsync(
                "net11.0",
                version,
                assembly,
                "netcore.app",
                "M:InspectWeb.DocumentationFixtures.HiddenDocumentedType.Read",
                workspaceClient,
                packageClient,
                sourceAuthorization,
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
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
                    new CatalogCallGraphDiagnostics(2, 3, 4),
                    hasUnexploredTraversalBoundary: true,
                    hasAnalysisFailureBoundary: true));

        Assert.True(diagnostics.IsIncomplete);
        Assert.Equal(2, diagnostics.IncompleteNodes);
        Assert.Equal(3, diagnostics.IncompleteEdges);
        Assert.Equal(4, diagnostics.BindingIdentityConflicts);
        Assert.True(diagnostics.HasUnexploredTraversalBoundary);
        Assert.True(diagnostics.HasAnalysisFailureBoundary);
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
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Tool.Pointer</id>
                    <version>1.0.0</version>
                    <dependencies>
                      <group targetFramework="net11.0">
                        <dependency id="Tool.Payload" version="[1.0.0]" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """)),
            ("README.md", Encoding.UTF8.GetBytes("# Tool Pointer")),
            ("tools/net11.0/any/Tool.Pointer.dll", [0x01]));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(packageId, "1.0.0", package, fromCache: false));

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
            coordinate.Root.AssetSelection.Status);
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
        Assert.Equal("README.md", Assert.Single(surface.Documents).Path);
        Assert.Empty(surface.InspectionErrors);
        Assert.Null(surface.InspectionError);

        BrowserPackageDependencies dependencies =
            Assert.IsType<BrowserPackageDependencies>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package.PackageExports.QueryPackageDependencies(
                        packageId,
                        "1.0.0",
                        "net11.0",
                        assemblyId: ""),
                    BrowserPackageJsonContext.Default.BrowserPackageDependencies));
        Assert.Null(dependencies.Assembly);
        Assert.Equal(
            BrowserCompileLibraryStatus.NoCompileAssets,
            dependencies.CompileLibrary.Status);
        Assert.Equal(
            dependencies.CompileLibrary.Message,
            Assert.IsType<string>(dependencies.AssemblyReferences.Value));
        BrowserPackageDependency dependency = Assert.Single(
            Assert.Single(dependencies.DependencyGroups).Dependencies);
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

        BrowserPackageDependencies dependencies =
            Assert.IsType<BrowserPackageDependencies>(
                JsonSerializer.Deserialize(
                    await PackageExports.QueryPackageDependencies(
                        packageId,
                        "1.0.0",
                        "net11.0",
                        assemblyId: ""),
                    BrowserPackageJsonContext.Default.BrowserPackageDependencies));

        Assert.Null(dependencies.Assembly);
        Assert.Equal(
            BrowserCompileLibraryStatus.NoMatchingTargetFramework,
            dependencies.CompileLibrary.Status);
        Assert.Equal(
            "net11.0",
            dependencies.CompileLibrary.TargetFramework);
        BrowserPackageDependency dependency = Assert.Single(
            Assert.Single(dependencies.DependencyGroups).Dependencies);
        Assert.Equal("Reference.Dependency", dependency.Id);
    }

    [Fact]
    public async Task QueryPackage_FirstTransportTruncationReturnsTypedNotice()
    {
        const string packageId = "First.Transport.Truncation";
        byte[] image = BuildTransportAmplificationImage(
            packageId,
            typeCount: 10_000,
            namespaceLength: 1_000);
        _ = await Coordinate(
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
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        int implementationTypeCount;
        using (var reader = new PEReader(new MemoryStream(implementation, writable: false)))
        {
            implementationTypeCount = reader.GetMetadataReader().TypeDefinitions.Count;
        }
        byte[] reference = BuildEmptySurfaceImage(
            typeof(BrowserEngineBoundaryTests).Assembly.GetName());
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
            typeof(BrowserEngineBoundaryTests).Assembly.GetName().Name!));

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
        Assert.Empty(performance.Members);
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
    public async Task PackageDependencies_UsesProductQueriesForManifestAndReferences()
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

        string json = await DotnetInspect.Web.Interop.Package.PackageExports.QueryPackageDependencies(
            packageId,
            "1.0.0",
            "net11.0",
            $"{packageId}.dll");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(packageId, root.GetProperty("package").GetString());
        Assert.Equal("net11.0", root.GetProperty("activeFramework").GetString());
        JsonElement group = Assert.Single(
            root.GetProperty("dependencyGroups").EnumerateArray());
        Assert.Equal(0, group.GetProperty("index").GetInt32());
        Assert.True(group.GetProperty("isActive").GetBoolean());
        JsonElement dependency = Assert.Single(
            group.GetProperty("dependencies").EnumerateArray());
        Assert.Equal(
            "Browser.Dependency.Child",
            dependency.GetProperty("id").GetString());
        JsonElement reference = Assert.Single(
            root.GetProperty("assemblyReferences").GetProperty("references").EnumerateArray(),
            reference =>
                reference.GetProperty("name").GetString() == "System.Runtime");
        Assert.Equal("11.0.0.0", reference.GetProperty("version").GetString());
        Assert.True(reference.TryGetProperty("culture", out JsonElement culture));
        Assert.True(
            culture.ValueKind is JsonValueKind.Null or JsonValueKind.String);
        Assert.False(string.IsNullOrWhiteSpace(
            reference.GetProperty("publicKeyToken").GetString()));
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("dependencyGroupError").ValueKind);
        Assert.False(root.TryGetProperty("assemblyReferenceError", out _));
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

        BrowserPackageDependencies dependencies =
            Assert.IsType<BrowserPackageDependencies>(
                JsonSerializer.Deserialize(
                    await PackageExports.QueryPackageDependencies(
                        packageId,
                        "1.0.0",
                        "net8.0",
                        $"{packageId}.dll"),
                    BrowserPackageJsonContext.Default.BrowserPackageDependencies));

        Assert.Equal("net8.0", dependencies.ActiveFramework);
        BrowserPackageDependencyGroup group =
            Assert.Single(dependencies.DependencyGroups);
        Assert.Equal(".NETStandard2.0", group.Framework);
        Assert.True(group.IsActive);
        Assert.Equal(
            "Browser.Dependency.Child",
            Assert.Single(group.Dependencies).Id);
        Assert.Null(dependencies.DependencyGroupError);
        Assert.Equal(
            BrowserCompileLibraryStatus.Selected,
            dependencies.CompileLibrary.Status);
        Assert.Equal(
            "net6.0",
            dependencies.CompileLibrary.TargetFramework);
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

        BrowserPackageDependencies dependencies =
            Assert.IsType<BrowserPackageDependencies>(
                JsonSerializer.Deserialize(
                    await PackageExports.QueryPackageDependencies(
                        packageId,
                        "1.0.0",
                        "net8.0",
                        $"{packageId}.dll"),
                    BrowserPackageJsonContext.Default.BrowserPackageDependencies));

        Assert.Equal("net8.0", dependencies.ActiveFramework);
        BrowserPackageDependencyGroup group =
            Assert.Single(dependencies.DependencyGroups);
        Assert.Equal("any", group.Framework);
        Assert.True(group.IsActive);
        Assert.Equal(
            "Browser.Dependency.Child",
            Assert.Single(group.Dependencies).Id);
        Assert.Null(dependencies.DependencyGroupError);
        Assert.Equal(
            BrowserCompileLibraryStatus.Selected,
            dependencies.CompileLibrary.Status);
        Assert.Equal(
            "net6.0",
            dependencies.CompileLibrary.TargetFramework);
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

        BrowserPackageDependencies dependencies =
            Assert.IsType<BrowserPackageDependencies>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package.PackageExports.QueryPackageDependencies(
                        packageId,
                        "1.0.0",
                        "net11.0",
                        $"{packageId}.dll"),
                    BrowserPackageJsonContext.Default.BrowserPackageDependencies));

        Assert.Equal(2, dependencies.DependencyGroups.Length);
        Assert.Equal("net11.0", dependencies.DependencyGroups[0].Framework);
        Assert.Equal("any", dependencies.DependencyGroups[1].Framework);
        Assert.Equal(
            "Valid.Dependency",
            Assert.Single(dependencies.DependencyGroups[0].Dependencies).Id);
        Assert.Equal(
            BrowserCompileLibraryStatus.Selected,
            dependencies.CompileLibrary.Status);
        Assert.Empty(dependencies.DeclarationFailures);
    }

    [Fact]
    public async Task PackageDependencyConflictsRemainVisibleInDependenciesAndPruning()
    {
        string packageId = $"Browser.Pruning.Conflict.{Guid.NewGuid():N}";
        const string dependencyId = "Browser.Pruning.Conflicting.Child";
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

        BrowserPackageDependencies dependencies =
            Assert.IsType<BrowserPackageDependencies>(
                JsonSerializer.Deserialize(
                    await PackageExports.QueryPackageDependencies(
                        packageId,
                        "1.0.0",
                        "net11.0",
                        $"{packageId}.dll"),
                    BrowserPackageJsonContext.Default
                        .BrowserPackageDependencies));

        BrowserPackageDependencyGroup group =
            Assert.Single(dependencies.DependencyGroups);
        Assert.True(group.IsActive);
        Assert.Empty(group.Dependencies);
        BrowserPackageDependencyDeclarationFailure dependencyFailure =
            Assert.Single(dependencies.DeclarationFailures);
        Assert.Equal(
            BrowserPackageDependencyDeclarationFailureKind
                .ConflictingPackageDeclaration,
            dependencyFailure.Kind);
        Assert.Equal("net11.0", dependencyFailure.Framework);
        Assert.Equal(dependencyId.ToLowerInvariant(), dependencyFailure.Package);
        Assert.Equal(2, dependencyFailure.SourceOccurrenceCount);

        var request = new BrowserPackagePruningRequest(
            1,
            "Microsoft.NETCore.App",
            "net11.0",
            "11.0.0",
            []);
        BrowserPackagePruningResult pruning =
            Assert.IsType<BrowserPackagePruningResult>(
                JsonSerializer.Deserialize(
                    await PackageExports.QueryPackagePruning(
                        packageId,
                        "1.0.0",
                        "net11.0",
                        JsonSerializer.Serialize(
                            request,
                            BrowserPackageJsonContext.Default
                                .BrowserPackagePruningRequest)),
                    BrowserPackageJsonContext.Default
                        .BrowserPackagePruningResult));

        Assert.Equal(
            BrowserPackagePruningCompletion.Failed,
            pruning.Completion);
        Assert.Empty(pruning.Rows);
        Assert.Single(pruning.DeclarationFailures);
        Assert.Equal(1, pruning.Summary.DeclarationFailures);
    }

    [Fact]
    public async Task PackagePruning_PreservesCompatibleSelectionAndSeparatesCandidateFromSupply()
    {
        string packageId = $"Browser.Pruning.Compatible.{Guid.NewGuid():N}";
        const string delegatedPackage = "Browser.Pruning.Delegated";
        const string retainedPackage = "Browser.Pruning.Retained";
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
                     <dependency id="{delegatedPackage}" version="[4.3.1]" />
                     <dependency id="{retainedPackage}" version="[4.3.2]" />
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
        var request = new BrowserPackagePruningRequest(
            1,
            "Microsoft.NETCore.App",
            "net11.0",
            "11.0.0",
            [
                new(
                    "netcore.app",
                    "Microsoft.NETCore.App",
                    delegatedPackage,
                    "4.3.2"),
                new(
                    "netcore.app",
                    "Microsoft.NETCore.App",
                    retainedPackage,
                    "4.3.1"),
            ]);

        BrowserPackagePruningResult result =
            Assert.IsType<BrowserPackagePruningResult>(
                JsonSerializer.Deserialize(
                    await PackageExports.QueryPackagePruning(
                        packageId,
                        "1.0.0",
                        "net11.0",
                        JsonSerializer.Serialize(
                            request,
                            BrowserPackageJsonContext.Default
                                .BrowserPackagePruningRequest)),
                    BrowserPackageJsonContext.Default
                        .BrowserPackagePruningResult));

        Assert.Equal("net11.0", result.TargetFramework);
        Assert.Equal("net8.0", result.SelectedFramework);
        Assert.Equal(BrowserPackagePruningCompletion.Complete, result.Completion);
        Assert.Empty(result.DeclarationFailures);
        Assert.Equal(2, result.Summary.Evaluated);
        Assert.Equal(1, result.Summary.Delegated);
        Assert.Equal(1, result.Summary.Retained);
        BrowserPackagePruningRow delegated =
            Assert.Single(
                result.Rows,
                row => row.Package == delegatedPackage);
        Assert.Equal("4.3.1", delegated.CandidateVersion);
        Assert.Equal("4.3.2", delegated.PlatformSuppliedVersion);
        Assert.Equal(
            BrowserPackagePruningDisposition.PlatformDelegation,
            delegated.Disposition);
        BrowserPackagePruningRow retained =
            Assert.Single(
                result.Rows,
                row => row.Package == retainedPackage);
        Assert.Equal("4.3.2", retained.CandidateVersion);
        Assert.Equal("4.3.1", retained.PlatformSuppliedVersion);
        Assert.Equal(
            BrowserPackagePruningDisposition.PackageRetained,
            retained.Disposition);
    }

    [Fact]
    public async Task PackagePruning_NoSelectedDependencyGroupIsNotApplicable()
    {
        string packageId = $"Browser.Pruning.NoGroup.{Guid.NewGuid():N}";
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
                   <group targetFramework="net12.0">
                     <dependency id="Browser.Pruning.Child" version="[1.0.0]" />
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
        var request = new BrowserPackagePruningRequest(
            1,
            "Microsoft.NETCore.App",
            "net11.0",
            "11.0.0",
            []);

        BrowserPackagePruningResult result =
            Assert.IsType<BrowserPackagePruningResult>(
                JsonSerializer.Deserialize(
                    await PackageExports.QueryPackagePruning(
                        packageId,
                        "1.0.0",
                        "net11.0",
                        JsonSerializer.Serialize(
                            request,
                            BrowserPackageJsonContext.Default
                                .BrowserPackagePruningRequest)),
                    BrowserPackageJsonContext.Default
                        .BrowserPackagePruningResult));

        Assert.Equal(
            BrowserPackagePruningCompletion.NotApplicable,
            result.Completion);
        Assert.Empty(result.Rows);
        Assert.Empty(result.DeclarationFailures);
        Assert.Contains(
            "no dependency group",
            Assert.IsType<string>(result.Message),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PackagePruning_RejectsMismatchedTargetAndSupplyFamily()
    {
        var mismatchedTarget = new BrowserPackagePruningRequest(
            1,
            "Microsoft.NETCore.App",
            "net10.0",
            "10.0.0",
            []);
        var mismatchedSupply = new BrowserPackagePruningRequest(
            1,
            "Microsoft.NETCore.App",
            "net11.0",
            "11.0.0",
            [
                new(
                    "aspnetcore.app",
                    "Microsoft.NETCore.App",
                    "Browser.Pruning.Child",
                    "1.0.0"),
            ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => PackageExports.QueryPackagePruning(
                "Ignored.Package",
                "1.0.0",
                "net11.0",
                JsonSerializer.Serialize(
                    mismatchedTarget,
                    BrowserPackageJsonContext.Default
                        .BrowserPackagePruningRequest)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => PackageExports.QueryPackagePruning(
                "Ignored.Package",
                "1.0.0",
                "net11.0",
                JsonSerializer.Serialize(
                    mismatchedSupply,
                    BrowserPackageJsonContext.Default
                        .BrowserPackagePruningRequest)));
    }

    [Fact]
    public async Task PackagePruning_CandidateIncompletionRemainsVisible()
    {
        string packageId = $"Browser.Pruning.Incomplete.{Guid.NewGuid():N}";
        const string dependencyId = "Browser.Pruning.Child";
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
        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                packageId,
                "1.0.0",
                "net11.0",
                TestContext.Current.CancellationToken);
        PackageDependencyEvidenceRoot root =
            await PackageExports.PackageDependencyEvidenceAsync(coordinate);
        PackageDependencyEvidenceDeclarationResult.Available declarations =
            Assert.IsType<
                PackageDependencyEvidenceDeclarationResult.Available>(
                    root.Declaration);
        PackageDependencyEvidenceGroup selectedGroup =
            Assert.Single(
                declarations.Groups,
                group => group.Identity == root.Selection.SelectedGroup);
        var request = new BrowserPackagePruningRequest(
            1,
            "Microsoft.NETCore.App",
            "net11.0",
            "11.0.0",
            []);
        PlatformPruneInventory inventory =
            PlatformPruneInventory.FromExactFamily(
                new PlatformPruneTarget(
                    request.Family,
                    request.TargetFramework,
                    NuGet.Versioning.NuGetVersion.Parse(
                        request.PlatformVersion)),
                []);
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact(
                request.TargetFramework,
                platformTarget: new PlatformFamilyTarget(
                    PlatformFamily.DotNetRuntime,
                    PlatformTargetFramework.Parse(
                        request.TargetFramework),
                    PlatformVersion.Parse(request.PlatformVersion)));
        using var operation = new NuGetOperationContext(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        BrowserPackagePruningResult result =
            await PackageExports.EvaluatePruningAsync(
                coordinate,
                request,
                root,
                selectedGroup,
                target,
                inventory,
                new IncompletePinnedCandidateSource(),
                operation,
                TestContext.Current.CancellationToken);

        Assert.Equal(BrowserPackagePruningCompletion.Failed, result.Completion);
        BrowserPackagePruningRow row = Assert.Single(result.Rows);
        Assert.Equal(dependencyId, row.Package);
        Assert.Null(row.CandidateVersion);
        Assert.Equal(
            BrowserPackagePruningDisposition.CandidateUnavailable,
            row.Disposition);
        Assert.Equal("PinnedAuthorization", row.Reason);
        Assert.Equal(1, result.Summary.Failed);
        Assert.Equal(0, result.Summary.DeclarationFailures);
    }

    [Fact]
    public async Task PackagePerformance_UsesProductRankedWorkspaceAnalysis()
    {
        const string PackageId = "Browser.Performance.Root";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
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
                == nameof(PerformanceBoxingProbe));
        Assert.Equal(
            typeof(BrowserEngineBoundaryTests).FullName,
            member.GetProperty("typeId").GetString());
        Assert.Equal(
            [
                typeof(BrowserEngineBoundaryTests)
                    .GetMethod(nameof(PerformanceBoxingProbe))!
                    .MetadataToken,
            ],
            member.GetProperty("bodyTokens")
                .EnumerateArray()
                .Select(token => token.GetInt32()));
        Assert.StartsWith(
            $"{nameof(PerformanceBoxingProbe)}~",
            member.GetProperty("stableSelector").GetString());
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
                == nameof(PerformanceBoxingProperty));
        Assert.StartsWith(
            $"{nameof(PerformanceBoxingProperty)}~",
            property.GetProperty("stableSelector").GetString());
        Assert.Equal(
            [
                typeof(BrowserEngineBoundaryTests)
                    .GetProperty(nameof(PerformanceBoxingProperty))!
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
                == nameof(PerformanceNestedProbe.Box));
        Assert.Equal(
            $"{typeof(BrowserEngineBoundaryTests).FullName}+"
                + nameof(PerformanceNestedProbe),
            nested.GetProperty("typeId").GetString());
    }
}
