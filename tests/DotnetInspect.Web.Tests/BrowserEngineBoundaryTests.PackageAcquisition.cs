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
    public async Task PackageAcquisition_StallBecomesVisibleOperationTimeout()
    {
        var handler = new StallingPackageHandler();
        using IPackageSourceClient source = Gallery(handler);
        string packageId =
            $"timeout.package.{Guid.NewGuid():N}";

        Task<BrowserPackage> acquisition = BrowserPackageWorkspace.AcquireAsync(
            packageId,
            "1.0.0",
            source,
            PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromMilliseconds(200),
            TestContext.Current.CancellationToken,
            epochWork: null);
        await handler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        TimeoutException failure =
            await Assert.ThrowsAsync<TimeoutException>(() => acquisition);

        Assert.Contains(
            "Browser package operation",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task PackageAcquisition_ExactPinUsesGalleryCdnWithoutServiceIndex()
    {
        string packageId = $"gallery.exact.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] archive = PackageDocuments(1);
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackage package = await BrowserPackageWorkspace.AcquireAsync(
            packageId,
            version,
            source,
            PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken,
            epochWork: null);

        Assert.Equal(version, package.Version);
        Assert.Equal(archive, package.RetainedBytes);
        Assert.False(package.Content.FromCache);
        Assert.IsType<PackageArchiveValidation.Valid>(
            package.Content.ValidateArchive(
                BrowserPackageWorkspace.PackageLimits,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            NuGetCache.GetSourceKey(PackageSourceIdentity.NuGetOrg.Value),
            package.Content.ProducerKey);
        Assert.Equal(
            [$"https://globalcdn.nuget.org/packages/{packageId}.{version}.nupkg"],
            handler.Requested);
        Assert.DoesNotContain(
            handler.Requested,
            request => request.Contains(
                "api.nuget.org",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageDocument_ReadmeAndSkillPullThroughHouseAcquisition()
    {
        string packageId = $"gallery.documents.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        string readmeText =
            "\uFEFF"
            + new string('r', 64 * 1024)
            + " Browser/Wasm \U0001F310";
        const string skillText =
            "# Inspect package\n\nRead progressively.";
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            PackageWithDocuments(
                packageId,
                version,
                readmeText,
                skillText));
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackageDocumentPayload readme =
            await BrowserPackageWorkspace.ReadDocumentAsync(
                packageId,
                version,
                "README.md",
                source,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        BrowserPackageDocumentPayload skill =
            await BrowserPackageWorkspace.ReadDocumentAsync(
                packageId,
                version,
                "skills/demo/SKILL.md",
                source,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        InvalidOperationException unavailable =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.ReadDocumentAsync(
                    packageId,
                    version,
                    "content/notes.txt",
                    source,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            new BrowserPackageDocumentPayload(
                "readme",
                "README.md",
                "README.md",
                readmeText),
            readme);
        Assert.Equal(
            new BrowserPackageDocumentPayload(
                "skill",
                "demo",
                "skills/demo/SKILL.md",
                skillText),
            skill);
        Assert.Contains(
            "is not a browsable document",
            unavailable.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            [$"https://globalcdn.nuget.org/packages/{packageId}.{version}.nupkg"],
            handler.Requested);
    }

    [Fact]
    public async Task PackageQueryContent_AcquiresThroughBrowserPackagePolicy()
    {
        string packageId = $"gallery.query.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] archive = PackageWithSkill(packageId, version);
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient source = Gallery(handler);
        PackageManifestFacts manifest = Assert.IsType<
            PackageManifestFactsResult.Available>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        Nuspec(packageId, version)),
                    PackageSourceCoordinate.Create(packageId, version))).Value;
        var package = new PackageQueryPackage(
            packageId,
            version,
            [],
            TotalDownloads: 0,
            Verified: false,
            source.Source,
            manifest);
        using var deadline =
            new BrowserPackageWorkspace.BrowserPackageOperationDeadline(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        PackageQueryContentResult result =
            await BrowserPackageWorkspace.AcquirePackageQueryContentAsync(
                package,
                source,
                PackageSourceIdentity.NuGetOrg,
                deadline);

        IPackageContent content = Assert.IsType<
            PackageQueryContentResult.Available>(result).Content;
        Assert.Contains(
            "skills/SKILL.md",
            content.EnumerateEntries(),
            StringComparer.Ordinal);
        Assert.Equal(
            [$"https://globalcdn.nuget.org/packages/{packageId}.{version}.nupkg"],
            handler.Requested);
    }

    [Fact]
    public async Task PackageQueryContent_PolicyRejectionRemainsVisible()
    {
        string packageId = $"gallery.query.no-length.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            PackageWithSkill(packageId, version),
            omitContentLength: true);
        using IPackageSourceClient source = Gallery(handler);
        PackageManifestFacts manifest = Assert.IsType<
            PackageManifestFactsResult.Available>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        Nuspec(packageId, version)),
                    PackageSourceCoordinate.Create(packageId, version))).Value;
        var package = new PackageQueryPackage(
            packageId,
            version,
            [],
            TotalDownloads: 0,
            Verified: false,
            source.Source,
            manifest);
        using var deadline =
            new BrowserPackageWorkspace.BrowserPackageOperationDeadline(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        PackageQueryContentResult result =
            await BrowserPackageWorkspace.AcquirePackageQueryContentAsync(
                package,
                source,
                PackageSourceIdentity.NuGetOrg,
                deadline);

        string message = Assert.IsType<
            PackageQueryContentResult.Unavailable>(result).Message;
        Assert.Contains(
            "did not declare its byte length",
            message,
            StringComparison.Ordinal);
        Assert.True(handler.PayloadDisposed);
    }

    [Fact]
    public async Task BrowserPackageRealization_ReceivesAcquisitionIssuedCoordinate()
    {
        string packageId = $"Gallery.Binding.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] archive = Package(
            [0x01],
            $"lib/net11.0/{packageId}.dll");
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                packageId,
                version,
                "net11.0",
                source,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));

        PackageRootBinding binding = Assert.IsType<PackageRootBinding>(
            coordinate.Binding);
        Assert.Same(binding.Root, coordinate.Root);
        Assert.Equal(packageId, binding.Root.PackageId);
        Assert.Equal(packageId.ToLowerInvariant(), binding.Coordinate.PackageId);
        Assert.Equal(version, binding.Coordinate.Version);
        Assert.Equal("net11.0", binding.Coordinate.Framework);
        Assert.Null(binding.Coordinate.RuntimeIdentifier);
        Assert.Equal(
            PackageProducerIdentity.NuGetOrg.PortableKey,
            binding.Coordinate.Producer);
        Assert.Equal(
            NuGetCache.GetSourceKey(PackageSourceIdentity.NuGetOrg.Value),
            binding.Root.ProducerKey);
        Assert.True(binding.Root.ReferencesContent(coordinate.Package.Content));
    }

    [Fact]
    public async Task WorkspaceOccurrences_PreserveOrderAndSupersedeOldActions()
    {
        string packageId = $"Gallery.Workspace.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] archive = Package(
            [0x01],
            $"lib/net11.0/{packageId}.dll");
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                packageId,
                version,
                "net11.0",
                source,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));

        BrowserWorkspacePackageOccurrenceView view =
            await BrowserWorkspaceOccurrenceOperations.ReplaceCurrent(
                [coordinate, coordinate]);

        Assert.Equal(2, view.Occurrences.Length);
        Assert.All(
            view.Occurrences,
            occurrence =>
            {
                Assert.Equal(packageId, occurrence.Package);
                Assert.Equal(version, occurrence.Version);
                Assert.Equal("net11.0", occurrence.Framework);
                Assert.DoesNotContain(
                    packageId,
                    occurrence.Action,
                    StringComparison.OrdinalIgnoreCase);
            });
        Assert.NotEqual(
            view.Occurrences[0].Action,
            view.Occurrences[1].Action);
        BrowserWorkspaceOccurrenceSelection selection = Assert.IsType<
            BrowserWorkspaceOccurrenceSelection>(
                BrowserWorkspaceOccurrenceOperations.Activate(
                    view.Occurrences[1].Action));
        Assert.Same(coordinate, selection.Coordinate);

        await BrowserWorkspaceOccurrenceOperations.ReplaceCurrent([]);

        Assert.Null(
            BrowserWorkspaceOccurrenceOperations.Activate(
                view.Occurrences[0].Action));

        BrowserWorkspacePackageOccurrenceView replacement =
            await BrowserWorkspaceOccurrenceOperations.ReplaceCurrent(
                [coordinate]);
        await BrowserWorkspaceOccurrenceOperations.ClearCurrent();

        Assert.Null(
            BrowserWorkspaceOccurrenceOperations.Activate(
                replacement.Occurrences[0].Action));
    }

    [Fact]
    public async Task WorkspaceOccurrences_ReplacedInflightQueryCannotRetireReplacement()
    {
        string packageId = $"Gallery.Workspace.Race.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            Package(
                [0x01],
                $"lib/net11.0/{packageId}.dll"));
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                packageId,
                version,
                "net11.0",
                source,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));
        var resolutionStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var continueResolution =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        Task<BrowserWorkspacePackageOccurrenceView> query =
            BrowserWorkspaceOccurrenceOperations.QueryAsync(
                [
                    new BrowserPackageRequest(
                        packageId,
                        version,
                        "net11.0"),
                ],
                async (_, _) =>
                {
                    resolutionStarted.SetResult();
                    await continueResolution.Task;
                    return coordinate;
                });
        await resolutionStarted.Task;
        BrowserWorkspacePackageOccurrenceView replacement =
            await BrowserWorkspaceOccurrenceOperations.ReplaceCurrent(
                [coordinate]);
        continueResolution.SetResult();

        BrowserWorkspacePackageOccurrenceView view = await query;

        Assert.True(view.Superseded);
        Assert.Empty(view.Occurrences);
        Assert.NotNull(
            BrowserWorkspaceOccurrenceOperations.Activate(
                replacement.Occurrences[0].Action));
    }

    [Fact]
    public async Task WorkspaceOccurrences_RevocationReleasesLeasesBeforeAStalledResolutionCompletes()
    {
        string packageId = $"Gallery.Workspace.LeaseRace.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            Package(
                [0x01],
                $"lib/net11.0/{packageId}.dll"));
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                packageId,
                version,
                "net11.0",
                source,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));
        var secondResolutionStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var continueResolution =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        int resolution = 0;

        Task<BrowserWorkspacePackageOccurrenceView> query =
            BrowserWorkspaceOccurrenceOperations.QueryAsync(
                [
                    new BrowserPackageRequest(
                        packageId,
                        version,
                        "net11.0"),
                    new BrowserPackageRequest(
                        packageId,
                        version,
                        "net11.0"),
                ],
                async (_, cancellationToken) =>
                {
                    if (Interlocked.Increment(ref resolution) == 1)
                        return coordinate;

                    secondResolutionStarted.SetResult();
                    await continueResolution.Task;
                    return coordinate;
                });
        await secondResolutionStarted.Task;

        await BrowserWorkspaceOccurrenceOperations.ClearCurrent();
        using (
            await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"workspace.lease.pressure.{Guid.NewGuid():N}@1.0.0",
                128L * MiB))
        {
            Assert.Equal(
                0,
                BrowserPackageWorkspace.Stats().Resident);
        }
        continueResolution.SetResult();

        BrowserWorkspacePackageOccurrenceView view = await query;
        Assert.True(view.Superseded);
        Assert.Empty(view.Occurrences);
    }

    [Fact]
    public async Task BrowserPackageRealization_WithoutFrameworkKeepsHostProjectionSemantics()
    {
        string selectedId = $"gallery.binding.selected.{Guid.NewGuid():N}";
        var selectedHandler = new GalleryPackageHandler(
            selectedId,
            "1.0.0",
            Package(
                [0x01],
                $"lib/net11.0/{selectedId}.dll"));
        using IPackageSourceClient selectedSource = Gallery(selectedHandler);
        BrowserPackageCoordinate selected =
            await BrowserPackageWorkspace.ResolveAsync(
                selectedId,
                "1.0.0",
                targetFramework: null,
                selectedSource,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));

        Assert.Null(selected.RealizedCoordinate.Framework);
        Assert.Equal("net11.0", selected.Framework);
        Assert.True(selected.Selection.IsSelected);

        string rootOnlyId = $"gallery.binding.root.{Guid.NewGuid():N}";
        var rootOnlyHandler = new GalleryPackageHandler(
            rootOnlyId,
            "1.0.0",
            PackageDocuments(1));
        using IPackageSourceClient rootOnlySource = Gallery(rootOnlyHandler);
        BrowserPackageCoordinate rootOnly =
            await BrowserPackageWorkspace.ResolveAsync(
                rootOnlyId,
                "1.0.0",
                targetFramework: null,
                rootOnlySource,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));

        Assert.Null(rootOnly.RealizedCoordinate.Framework);
        Assert.Equal("", rootOnly.Framework);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            rootOnly.Selection.Status);
    }

    [Fact]
    public async Task PackageAcquisition_GalleryFailureRemainsVisible()
    {
        string packageId = $"gallery.failure.{Guid.NewGuid():N}";
        var handler = new GalleryPackageHandler(
            packageId,
            "1.0.0",
            PackageDocuments(1),
            packageStatus: System.Net.HttpStatusCode.BadGateway);
        using IPackageSourceClient source = Gallery(handler);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.AcquireAsync(
                    packageId,
                    "1.0.0",
                    source,
                    PackageSourceIdentity.NuGetOrg,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken,
                    epochWork: null));

        Assert.Contains(
            "transport failed",
            failure.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "globalcdn.nuget.org",
            failure.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PackageAcquisition_RejectedReservationDisposesGalleryPayload()
    {
        string packageId = $"gallery.no-length.{Guid.NewGuid():N}";
        var handler = new GalleryPackageHandler(
            packageId,
            "1.0.0",
            PackageDocuments(1),
            omitContentLength: true);
        using IPackageSourceClient source = Gallery(handler);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.AcquireAsync(
                    packageId,
                    "1.0.0",
                    source,
                    PackageSourceIdentity.NuGetOrg,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken,
                    epochWork: null));

        Assert.Contains(
            "did not declare its byte length",
            failure.Message,
            StringComparison.Ordinal);
        Assert.True(handler.PayloadDisposed);
    }

    [Fact]
    public async Task PackageResolution_StallBecomesVisibleOperationTimeout()
    {
        var handler = new StallingPackageHandler();
        using IPackageSourceClient source = Gallery(handler);
        string packageId =
            $"resolution.timeout.package.{Guid.NewGuid():N}";

        Task<BrowserPackage> acquisition = BrowserPackageWorkspace.AcquireAsync(
            packageId,
            version: null,
            source,
            PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken,
            epochWork: null);
        await handler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        TimeoutException failure =
            await Assert.ThrowsAsync<TimeoutException>(() => acquisition);

        Assert.Contains(
            "Browser package operation",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task PackageAcquisition_SharedStallIsAVisibleTimeoutForEveryCaller()
    {
        var handler = new StallingPackageHandler();
        using IPackageSourceClient source = Gallery(handler);
        string packageId =
            $"shared.timeout.package.{Guid.NewGuid():N}";

        Task<BrowserPackage> first = BrowserPackageWorkspace.AcquireAsync(
            packageId,
            "1.0.0",
            source,
            PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromMilliseconds(500),
            TestContext.Current.CancellationToken,
            epochWork: null);
        await handler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        Task<BrowserPackage> second = BrowserPackageWorkspace.AcquireAsync(
            packageId,
            "1.0.0",
            source,
            PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken,
            epochWork: null);

        TimeoutException secondFailure =
            await Assert.ThrowsAsync<TimeoutException>(() => second);
        Assert.Contains(
            "0.1-second deadline",
            secondFailure.Message,
            StringComparison.Ordinal);
        Assert.False(first.IsCompleted);
        await Assert.ThrowsAsync<TimeoutException>(() => first);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public void PendingAcquisitionAssociation_UsesCoordinateAndExactClientReference()
    {
        using IPackageSourceClient gallery =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        using IPackageSourceClient v3 =
            PackageSourceClientFactory.Create(
                PackageSourceDescriptor.NuGetV3(
                    "nuget-v3",
                    "NuGet.org v3",
                    new Uri("https://api.nuget.org/v3/index.json")),
                PackageSourceAssociation.Create());
        const string coordinate = "example@1.0.0";

        Assert.Equal(gallery.Source.Producer, v3.Source.Producer);
        Assert.NotEqual(
            gallery.Source.TransportKind,
            v3.Source.TransportKind);

        var galleryKey =
            new BrowserPackageWorkspace.PendingAcquisitionKey(
                coordinate,
                gallery);
        var equivalentGalleryKey =
            new BrowserPackageWorkspace.PendingAcquisitionKey(
                coordinate,
                gallery);
        var v3Key =
            new BrowserPackageWorkspace.PendingAcquisitionKey(
                coordinate,
                v3);

        Assert.Equal(galleryKey, equivalentGalleryKey);
        Assert.Equal(
            galleryKey.GetHashCode(),
            equivalentGalleryKey.GetHashCode());
        Assert.NotEqual(galleryKey, v3Key);

        FieldInfo[] fields = typeof(
                BrowserPackageWorkspace.PendingAcquisitionKey)
            .GetFields(
                BindingFlags.Instance
                | BindingFlags.NonPublic);
        Assert.Equal(2, fields.Length);
        Assert.Contains(fields, field => field.FieldType == typeof(string));
        Assert.Contains(
            fields,
            field => field.FieldType == typeof(IPackageSourceClient));
    }

    [Fact]
    public async Task PackageAcquisition_DistinctSameProducerClientsDoNotSharePendingTransfer()
    {
        string packageId =
            $"distinct.pending.package.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        var stalledHandler = new StallingPackageHandler();
        var servingHandler = new GalleryPackageHandler(
            packageId,
            version,
            PackageDocuments(1));
        using IPackageSourceClient stalledSource =
            Gallery(stalledHandler);
        using IPackageSourceClient servingSource =
            Gallery(servingHandler);

        Assert.Equal(
            stalledSource.Source.Producer,
            servingSource.Source.Producer);
        Assert.NotSame(stalledSource, servingSource);

        Task<BrowserPackage> stalled =
            BrowserPackageWorkspace.AcquireAsync(
                packageId,
                version,
                stalledSource,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromMilliseconds(500),
                TestContext.Current.CancellationToken,
                epochWork: null);
        await stalledHandler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        BrowserPackage served =
            await BrowserPackageWorkspace.AcquireAsync(
                packageId,
                version,
                servingSource,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken,
                epochWork: null);

        Assert.Equal(packageId, served.PackageId);
        Assert.Equal(version, served.Version);
        await Assert.ThrowsAsync<TimeoutException>(() => stalled);
        Assert.Equal(1, stalledHandler.Requests);
        Assert.Single(servingHandler.Requested);
    }

    [Fact]
    public async Task PackageAcquisition_SameClientReusesCompletedPayload()
    {
        string packageId = $"same.completed.package.{Guid.NewGuid():N}";
        byte[] archive = PackageDocuments(1);
        var handler = new GalleryPackageHandler(packageId, "1.0.0", archive);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackage first = await Acquire(source);
        BrowserPackage repeated = await Acquire(source);

        Assert.False(first.Content.FromCache);
        Assert.True(repeated.Content.FromCache);
        Assert.Same(first.RetainedBytes, repeated.RetainedBytes);
        Assert.Same(first.Content.GenerationIdentity, repeated.Content.GenerationIdentity);
        Assert.Equal(archive, repeated.RetainedBytes);
        Assert.Single(handler.Requested);

        Task<BrowserPackage> Acquire(IPackageSourceClient client) =>
            BrowserPackageWorkspace.AcquireAsync(
                packageId,
                "1.0.0",
                client,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken,
                epochWork: null);
    }

    [Fact]
    public async Task PackageAcquisition_DistinctSameProducerClientsRetainTheirOwnPayloads()
    {
        string packageId = $"distinct.completed.package.{Guid.NewGuid():N}";
        byte[] firstArchive = PackageDocuments(1);
        byte[] secondArchive = PackageDocuments(2);
        var firstHandler = new GalleryPackageHandler(packageId, "1.0.0", firstArchive);
        var secondHandler = new GalleryPackageHandler(packageId, "1.0.0", secondArchive);
        using IPackageSourceClient firstSource = Gallery(firstHandler);
        using IPackageSourceClient secondSource = Gallery(secondHandler);
        Assert.Equal(firstSource.Source.Producer, secondSource.Source.Producer);
        int downloaded = BrowserPackageWorkspace.Stats().Packages;

        BrowserPackage first = await Acquire(firstSource);
        BrowserPackage second = await Acquire(secondSource);
        BrowserPackage firstAgain = await Acquire(firstSource);
        BrowserPackage secondAgain = await Acquire(secondSource);

        Assert.False(first.Content.FromCache);
        Assert.False(second.Content.FromCache);
        Assert.True(firstAgain.Content.FromCache);
        Assert.True(secondAgain.Content.FromCache);
        Assert.Equal(firstArchive, first.RetainedBytes);
        Assert.Equal(secondArchive, second.RetainedBytes);
        Assert.Same(first.RetainedBytes, firstAgain.RetainedBytes);
        Assert.Same(second.RetainedBytes, secondAgain.RetainedBytes);
        Assert.NotSame(first.Content.GenerationIdentity, second.Content.GenerationIdentity);
        Assert.Same(first.Content.GenerationIdentity, firstAgain.Content.GenerationIdentity);
        Assert.Same(second.Content.GenerationIdentity, secondAgain.Content.GenerationIdentity);
        Assert.Single(firstAgain.Documents());
        Assert.Equal(2, secondAgain.Documents().Count);
        Assert.Single(firstHandler.Requested);
        Assert.Single(secondHandler.Requested);
        Assert.Equal(downloaded + 2, BrowserPackageWorkspace.Stats().Packages);

        Task<BrowserPackage> Acquire(IPackageSourceClient client) =>
            BrowserPackageWorkspace.AcquireAsync(
                packageId,
                "1.0.0",
                client,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken,
                epochWork: null);
    }

    [Fact]
    public async Task PackageQueryContent_UsesTheSelectedClientsCompletedCache()
    {
        string packageId = $"distinct.query.package.{Guid.NewGuid():N}";
        var firstHandler = new GalleryPackageHandler(
            packageId, "1.0.0", PackageDocuments(1));
        var secondHandler = new GalleryPackageHandler(
            packageId, "1.0.0", PackageDocuments(2));
        using IPackageSourceClient firstSource = Gallery(firstHandler);
        using IPackageSourceClient secondSource = Gallery(secondHandler);
        Assert.Equal(firstSource.Source.Producer, secondSource.Source.Producer);

        BrowserPackage first = await BrowserPackageWorkspace.AcquireAsync(
            packageId, "1.0.0", firstSource,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken,
            epochWork: null);
        IPackageContent second = await QueryContent(secondSource);
        IPackageContent firstAgain = await QueryContent(firstSource);
        BrowserPackage secondAgain = await BrowserPackageWorkspace.AcquireAsync(
            packageId, "1.0.0", secondSource,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken,
            epochWork: null);

        Assert.False(second.FromCache);
        Assert.True(firstAgain.FromCache);
        Assert.True(secondAgain.Content.FromCache);
        Assert.Same(first.Content.GenerationIdentity, firstAgain.GenerationIdentity);
        Assert.NotSame(firstAgain.GenerationIdentity, second.GenerationIdentity);
        Assert.Same(second.GenerationIdentity, secondAgain.Content.GenerationIdentity);
        Assert.Single(firstAgain.EnumerateEntries());
        Assert.Equal(2, second.EnumerateEntries().Count());
        Assert.Single(firstHandler.Requested);
        Assert.Single(secondHandler.Requested);

        async Task<IPackageContent> QueryContent(IPackageSourceClient source)
        {
            PackageManifestFacts manifest = Assert.IsType<
                PackageManifestFactsResult.Available>(
                    PackageManifestFactsQuery.Execute(
                        Encoding.UTF8.GetBytes(Nuspec(packageId, "1.0.0")),
                        PackageSourceCoordinate.Create(packageId, "1.0.0"))).Value;
            var package = new PackageQueryPackage(
                packageId, "1.0.0", [], TotalDownloads: 0, Verified: false,
                source.Source, manifest);
            using var deadline =
                new BrowserPackageWorkspace.BrowserPackageOperationDeadline(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken);
            return Assert.IsType<PackageQueryContentResult.Available>(
                await BrowserPackageWorkspace.AcquirePackageQueryContentAsync(
                    package, source, deadline)).Content;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageAcquisition_DistinctClientReservationsShareGlobalBudget(bool byteLimit)
    {
        using (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            $"reservation.drain.{Guid.NewGuid():N}", 128L * MiB))
        {
        }

        string packageId = $"distinct.reserved.package.{Guid.NewGuid():N}";
        byte[] firstArchive = PackageDocuments(1);
        byte[] secondArchive = PackageDocuments(2);
        var firstRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstHandler = new GalleryPackageHandler(
            packageId, "1.0.0", firstArchive, payloadRelease: firstRelease.Task);
        var secondHandler = new GalleryPackageHandler(
            packageId, "1.0.0", secondArchive, payloadRelease: secondRelease.Task);
        var rejectedHandler = new GalleryPackageHandler(
            packageId, "1.0.0", PackageDocuments(1));
        using IPackageSourceClient firstSource = Gallery(firstHandler);
        using IPackageSourceClient secondSource = Gallery(secondHandler);
        using IPackageSourceClient rejectedSource = Gallery(rejectedHandler);
        Assert.Equal(firstSource.Source.Producer, secondSource.Source.Producer);
        var held = new List<BrowserPackageWorkspace.PackageDownloadReservation>();
        Task<BrowserPackage>? first = null;
        Task<BrowserPackage>? second = null;
        try
        {
            int count = byteLimit
                ? 1
                : BrowserPackageWorkspace.Stats().MaxPackageEntries - 2;
            for (int index = 0; index < count; index++)
            {
                held.Add(await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                    $"reservation.holder.{index}.{Guid.NewGuid():N}",
                    byteLimit ? 128L * MiB - firstArchive.Length - secondArchive.Length : 0));
            }

            first = Acquire(firstSource);
            await firstHandler.PayloadReadStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            second = Acquire(secondSource);
            await secondHandler.PayloadReadStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(
                byteLimit ? 128L * MiB : firstArchive.Length + secondArchive.Length,
                BrowserPackageWorkspace.Stats().ResidentBytes);

            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(() => Acquire(rejectedSource));
            Assert.Contains("package-cache limit", failure.Message, StringComparison.Ordinal);

            firstRelease.SetResult();
            BrowserPackage firstPackage = await first;
            Assert.False(second.IsCompleted);
            secondRelease.SetResult();
            BrowserPackage secondPackage = await second;
            Assert.Equal(firstArchive, firstPackage.RetainedBytes);
            Assert.Equal(secondArchive, secondPackage.RetainedBytes);
            Assert.NotSame(
                firstPackage.Content.GenerationIdentity,
                secondPackage.Content.GenerationIdentity);
            Assert.Equal(2, BrowserPackageWorkspace.Stats().Resident);
            Assert.Single(firstHandler.Requested);
            Assert.Single(secondHandler.Requested);
        }
        finally
        {
            try
            {
                firstRelease.TrySetResult();
                if (first is not null)
                    await first;
            }
            finally
            {
                try
                {
                    secondRelease.TrySetResult();
                    if (second is not null)
                        await second;
                }
                finally
                {
                    foreach (BrowserPackageWorkspace.PackageDownloadReservation reservation in held)
                        reservation.Dispose();
                }
            }
        }

        Task<BrowserPackage> Acquire(IPackageSourceClient source) =>
            BrowserPackageWorkspace.AcquireAsync(
                packageId, "1.0.0", source,
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken,
                epochWork: null);
    }

    [Fact]
    public async Task PackageCacheAccountingIsAtomicAcrossConcurrentReservations()
    {
        await Task.WhenAll(
            Enumerable.Range(0, 8).Select(worker => Task.Run(
                async () =>
                {
                    for (int iteration = 0; iteration < 200; iteration++)
                    {
                        using BrowserPackageWorkspace.PackageDownloadReservation reservation =
                            await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                                $"reservation.concurrent.{worker}.{iteration}.{Guid.NewGuid():N}",
                                declaredLength: 0);
                        _ = BrowserPackageWorkspace.Stats();
                    }
                })));

        BrowserPackageCacheSnapshot settled = BrowserPackageWorkspace.Stats();
        Assert.InRange(settled.Resident, 0, settled.MaxPackageEntries);
        Assert.InRange(settled.ResidentBytes, 0, settled.MaxResidentBytes);
    }

    [Fact]
    public async Task WorkspaceAdmissionPublishesConstructionLeaseBeforeReleasingCacheLedger()
    {
        using (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            $"reservation.construction-lease.drain.{Guid.NewGuid():N}", 128L * MiB))
        {
        }

        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            $"construction.lease.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Construction.Lease.dll"));
        var validated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseValidation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reservationAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var hooks = new BrowserPackageWorkspaceTestHooks
        {
            CoordinatesValidatedBeforeConstructionLease = () =>
            {
                validated.TrySetResult();
                releaseValidation.Task.GetAwaiter().GetResult();
            },
        };

        Task<BrowserScopeLease<BrowserInspectionScope>> opening = Task.Run(
            () => BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                hooks,
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        await validated.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Task reservation = Task.Run(
            async () =>
            {
                reservationAttempted.TrySetResult();
                using BrowserPackageWorkspace.PackageDownloadReservation ignored =
                    await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                        $"reservation.construction-lease.{Guid.NewGuid():N}",
                        128L * MiB);
            },
            TestContext.Current.CancellationToken);
        await reservationAttempted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        bool reservationWaitedForRetention = !reservation.IsCompleted;
        releaseValidation.TrySetResult();

        Assert.True(reservationWaitedForRetention);
        await using BrowserScopeLease<BrowserInspectionScope> scope =
            await opening.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(() => reservation);
        Assert.Contains("package-cache limit", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowserWorkspace_DistinctClientsKeepScopesAndArchiveLeasesSeparate()
    {
        using (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            $"scope.drain.{Guid.NewGuid():N}", 128L * MiB))
        {
        }

        string packageId = $"distinct.scope.package.{Guid.NewGuid():N}";
        byte[] image = File.ReadAllBytes(
            typeof(InspectWeb.MethodBodyFixtures.Left).Assembly.Location);
        byte[] firstArchive = Package(image, "lib/net11.0/First.dll");
        byte[] secondArchive = Package(image, "lib/net11.0/Second.dll");
        var firstHandler = new GalleryPackageHandler(packageId, "1.0.0", firstArchive);
        var secondHandler = new GalleryPackageHandler(packageId, "1.0.0", secondArchive);
        using IPackageSourceClient firstSource = Gallery(firstHandler);
        using IPackageSourceClient secondSource = Gallery(secondHandler);
        Assert.Equal(firstSource.Source.Producer, secondSource.Source.Producer);

        BrowserPackageCoordinate first = await Resolve(firstSource);
        BrowserPackageCoordinate second = await Resolve(secondSource);
        Assert.NotEqual(first.Key, second.Key);
        Assert.NotEqual(
            BrowserPackageWorkspace.PackageScopeKey([first]),
            BrowserPackageWorkspace.PackageScopeKey([second]));
        await using BrowserScopeLease<BrowserInspectionScope> firstLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                first.Package, first.Framework, TestContext.Current.CancellationToken);
        await using BrowserScopeLease<BrowserInspectionScope> secondLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                second.Package, second.Framework, TestContext.Current.CancellationToken);
        Assert.Equal("lib/net11.0/First.dll", Assert.Single(firstLease.Scope.SurfaceParticipants).Asset.Path);
        Assert.Equal("lib/net11.0/Second.dll", Assert.Single(secondLease.Scope.SurfaceParticipants).Asset.Path);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(firstLease.Scope));
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(secondLease.Scope));
        Assert.InRange(BrowserPackageWorkspace.Stats().Workspaces, 2, 4);
        Assert.Throws<InvalidOperationException>(() =>
            BrowserPackageWorkspace.LeaseRetainedPackageScope(
                packageId, "1.0.0", first.Framework));
        BrowserPackage firstCached = await BrowserPackageWorkspace.AcquireAsync(
            packageId, "1.0.0", firstSource,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken,
            epochWork: null);
        Assert.True(firstCached.Content.FromCache);
        Assert.Same(first.Package.Content.GenerationIdentity, firstCached.Content.GenerationIdentity);
        Assert.Same(first.Package.RetainedBytes, firstCached.RetainedBytes);
        await using (BrowserScopeLease<BrowserInspectionScope> repeated =
            await BrowserPackageWorkspace.OpenScopeAsync(
                firstCached, first.Framework, TestContext.Current.CancellationToken))
        {
            Assert.Same(firstLease.Scope, repeated.Scope);
            Assert.NotSame(firstLease.Scope, secondLease.Scope);
        }

        await firstLease.DisposeAsync();
        await BrowserPackageWorkspace.RemoveScopeAsync(firstLease.Scope);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(secondLease.Scope));
        using (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            $"scope.pressure.{Guid.NewGuid():N}", 128L * MiB - secondArchive.Length))
        {
            Assert.DoesNotContain(first.Package.CacheKey, BrowserPackageWorkspace.ResidentPackageKeys());
            Assert.Contains(second.Package.CacheKey, BrowserPackageWorkspace.ResidentPackageKeys());
            Assert.Equal(128L * MiB, BrowserPackageWorkspace.Stats().ResidentBytes);
        }

        BrowserPackageCoordinate firstAgain = await Resolve(firstSource);
        BrowserPackageCoordinate secondAgain = await Resolve(secondSource);
        Assert.False(firstAgain.Package.Content.FromCache);
        Assert.True(secondAgain.Package.Content.FromCache);
        Assert.Same(second.Package.Content.GenerationIdentity, secondAgain.Package.Content.GenerationIdentity);
        Assert.NotSame(first.Package.Content.GenerationIdentity, firstAgain.Package.Content.GenerationIdentity);
        Assert.Equal(2, firstHandler.Requested.Count);
        Assert.Single(secondHandler.Requested);
        Assert.NotEmpty(secondLease.Scope.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group)).Assemblies.Assemblies);

        Task<BrowserPackageCoordinate> Resolve(IPackageSourceClient source) =>
            BrowserPackageWorkspace.ResolveAsync(
                packageId, "1.0.0", "net11.0", source,
                TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void PackageAcquisition_ExpiredDeadlineCannotPublishReservedContent()
    {
        using var deadline =
            new BrowserPackageWorkspace.BrowserPackageOperationDeadline(
                TimeSpan.FromMilliseconds(10));
        var inner = new RecordingTransferPolicy();
        var policy =
            new BrowserPackageWorkspace.BrowserPackageOperationTransferPolicy(
                inner,
                deadline);
        using IPackagePayloadReservation reservation =
            policy.ApplyDeadline(inner.Reservation);
        while (!deadline.HasExpired)
            Thread.SpinWait(100);

        Assert.Throws<TimeoutException>(() => reservation.Complete());
        Assert.False(inner.Reservation.Completed);
    }

    [Fact]
    public async Task PackageOperation_LateFailureBecomesVisibleTimeout()
    {
        TimeoutException failure =
            await Assert.ThrowsAsync<TimeoutException>(
                () => BrowserPackageWorkspace.RunPackageOperationAsync<int>(
                    deadline =>
                    {
                        while (!deadline.HasExpired)
                            Thread.SpinWait(100);
                        return Task.FromException<int>(
                            new InvalidOperationException(
                                "Synchronous work failed after the deadline."));
                    },
                    TimeSpan.FromMilliseconds(10),
                    TestContext.Current.CancellationToken));

        Assert.IsType<InvalidOperationException>(failure.InnerException);
    }

    [Fact]
    public async Task PackageOperation_LateSuccessDisposesOwnedResult()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Late.Success",
            Package(image, "lib/net11.0/Late.Success.dll"));
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([coordinate], TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;

        await Assert.ThrowsAsync<TimeoutException>(
            () => BrowserPackageWorkspace.RunPackageOperationAsync<
                BrowserScopeLease<BrowserInspectionScope>>(
                deadline =>
                {
                    BrowserScopeLease<BrowserInspectionScope> lease =
                        BrowserPackageWorkspace.LeaseScope(scope);
                    while (!deadline.HasExpired)
                        Thread.SpinWait(100);
                    return Task.FromResult(lease);
                },
                TimeSpan.FromMilliseconds(10),
                TestContext.Current.CancellationToken));

        // Removal waits for the caller's own protected use, and then retires: the abandoned
        // late result released the lease it took rather than stranding it here.
        await BrowserPackageWorkspace.RemoveScopeAsync(scope);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(scope));
        await scopeLease.DisposeAsync();
        Assert.False(BrowserPackageWorkspace.IsScopeRetained(scope));
    }

    [Fact]
    public async Task PackageOperation_LateCancellationPreservesCleanupFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var owned = new FailingScope();

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(
            () => BrowserPackageWorkspace.RunPackageOperationAsync(
                _ =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(owned);
                },
                TimeSpan.FromSeconds(5),
                cancellation.Token));

        Assert.Collection(
            failure.InnerExceptions,
            primary => Assert.IsAssignableFrom<OperationCanceledException>(primary),
            cleanup => Assert.IsType<InvalidOperationException>(cleanup));
        Assert.Equal(1, owned.DisposalCount);
    }

    [Fact]
    public async Task PackageOperation_LateCallerCancellationRemainsCancellation()
    {
        using var callerCancellation = new CancellationTokenSource();
        Task<int> operation =
            BrowserPackageWorkspace.RunPackageOperationAsync<int>(
                async _ =>
                {
                    callerCancellation.Cancel();
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(50),
                        TestContext.Current.CancellationToken);
                    throw new OperationCanceledException(
                        callerCancellation.Token);
                },
                TimeSpan.FromMilliseconds(10),
                callerCancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation);
    }

    [Fact]
    public async Task PackageVersionIndex_ValidatesTheIdBeforeRequestingIt()
    {
        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.GetVersionsAsync("evil/../other"));

        Assert.Contains("package coordinate", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExactDependencyNavigation_DoesNotRequireAListedVersion()
    {
        string resolved =
            await BrowserPackageWorkspace.ResolveDependencyVersionAsync(
                "Example.Package",
                "[999999.0]");

        Assert.Equal("999999.0.0", resolved);
    }

    [Fact]
    public async Task DependencyRangeUsesAuthoritativeGalleryListingState()
    {
        var handler = new GalleryVersionHandler();
        using IPackageSourceClient source = Gallery(handler);

        string resolved =
            await BrowserPackageWorkspace.ResolveDependencyVersionAsync(
                "Contoso",
                "[1.0.0,2.0.0)",
                source,
                TimeSpan.FromSeconds(5));

        Assert.Equal("1.1.0", resolved);
        Assert.Equal(
            [
                "https://globalcdn.nuget.org/v3-flatcontainer/contoso/index.json",
                "https://globalcdn.nuget.org/v3/registration5-gz-semver2/contoso/index.json",
            ],
            handler.Requested);
    }

    [Fact]
    public async Task DependencyRangePreservesGalleryRegistrationTimeout()
    {
        var handler = new StallingGalleryRegistrationHandler();
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(100),
                    OperationTimeout = TimeSpan.FromSeconds(5),
                });

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.ResolveDependencyVersionAsync(
                    "contoso",
                    "[1.0.0,2.0.0)",
                    source,
                    TimeSpan.FromSeconds(10)));

        Assert.Equal(
            "The package source operation exceeded its configured deadline.",
            failure.Message);
        Assert.Equal(1, handler.FlatContainerRequests);
        Assert.True(handler.RegistrationRequests >= 1);
    }

    [Fact]
    public void BrowserGalleryDeadlineLeavesTimeForSourceTimeout()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(5),
            BrowserPackageWorkspace.PackageOperationTimeout
            - BrowserPackageWorkspace.GalleryOperationTimeout);
        NuGetGalleryPackageSourceClient gallery =
            Assert.IsType<NuGetGalleryPackageSourceClient>(
                BrowserPackageWorkspace.Gallery);
        Assert.Equal(
            BrowserPackageWorkspace.GalleryOperationTimeout,
            gallery.RequestTimeout);
        Assert.Equal(
            BrowserPackageWorkspace.GalleryOperationTimeout,
            gallery.OperationTimeout);
    }

    [Fact]
    public void PackageChangesUsesFullCatalogSourceWithinReportDeadline()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(120),
            BrowserPackageWorkspace.PackageChangesOperationTimeout);
        Assert.Equal(
            TimeSpan.FromSeconds(5),
            BrowserPackageWorkspace.PackageChangesOperationTimeout
            - BrowserPackageWorkspace.PackageChangesSourceOperationTimeout);
        Assert.True(
            BrowserPackageWorkspace.PackageChangesRequestTimeout
            < BrowserPackageWorkspace.PackageChangesSourceOperationTimeout);
        Assert.Equal(
            BrowserPackageWorkspace.PackageChangesRequestTimeout,
            BrowserPackageWorkspace.PackageChangesAdvisoryClient.Timeout);
        Assert.Equal(
            PackageSourceKind.NuGetV3,
            BrowserPackageWorkspace.Catalog.Source.TransportKind);
        Assert.Equal(
            PackageProducerIdentity.NuGetOrg,
            BrowserPackageWorkspace.Catalog.Source.Producer);
        Assert.True(
            BrowserPackageWorkspace.Catalog.Capabilities.HasFlag(
                PackageSourceCapabilities.Catalog));
    }

    [Fact]
    public async Task VersionPickerMapsHouseOperationTimeoutToBrowserDeadline()
    {
        var handler = new StallingGalleryRegistrationHandler();
        using IPackageSourceClient source =
            BrowserPackageWorkspace.CreateGallerySource(
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(100),
                    OperationTimeout = TimeSpan.FromSeconds(5),
                });

        TimeoutException failure =
            await Assert.ThrowsAsync<TimeoutException>(
                () => BrowserPackageWorkspace.GetVersionInventoryAsync(
                    "contoso",
                    "2.0.0",
                    source,
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "Browser package operation exceeded its 10-second deadline",
            failure.Message);
        Assert.Contains(
            "authorized package source was not settled",
            failure.InnerException?.Message);
        Assert.Equal(1, handler.FlatContainerRequests);
        Assert.True(handler.RegistrationRequests >= 1);
    }

    // The default package load runs under explicit bounds and says so when it stops early. Both
    // halves matter: an ordinary projection must be untouched, and the bound must be reachable.
    [Fact]
    public async Task ApiSurfaceProjection_IsBoundedAndReportsTruncation()
    {
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
            [await Coordinate("Bounded.Surface", Package(image, "lib/net11.0/Bounded.Surface.dll"))],
            TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;

        AssemblyContextApiSurfaceResult complete = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                BrowserApiSurfacePolicy.Limits));

        Assert.Null(complete.Truncation);
        Assert.True(complete.IsComplete);
        Assert.Null(BrowserApiSurfacePolicy.TruncationNotice(complete.Truncation));
        int projectedTypes = complete.Assemblies.Assemblies
            .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
            .Sum(entry => entry.Value.Surface.Types.Count);
        Assert.True(projectedTypes > 0);
        Assert.True(projectedTypes < BrowserApiSurfacePolicy.MaxTypes);

        AssemblyContextApiSurfaceResult truncated = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                new ApiSurfaceProjectionLimits(1, 1, 1, 1, 1, int.MaxValue)));

        Assert.NotNull(truncated.Truncation);
        Assert.False(truncated.IsComplete);
        string notice = Assert.IsType<string>(
            BrowserApiSurfacePolicy.TruncationNotice(truncated.Truncation));
        Assert.Contains("API surface truncated", notice, StringComparison.Ordinal);

        // A truncation is carried beside participant failures, never instead of them.
        Assert.Equal(
            notice,
            BrowserSurfaceProjection.Notice(truncated.Assemblies.Assemblies, notice));

        AssemblyContextApiSurfaceResult textTruncated = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                new ApiSurfaceProjectionLimits(
                    1,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    1)));
        Assert.Equal(
            ApiSurfaceProjectionLimit.RetainedTextCharacters,
            textTruncated.Truncation!.Limit);
        Assert.Contains(
            "retained text character",
            BrowserApiSurfacePolicy.TruncationNotice(textTruncated.Truncation),
            StringComparison.Ordinal);
    }
}
