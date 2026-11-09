using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.Presentation;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeTypeReplay_RetainsExactRc1LibraryWithoutApiInventory(bool legacyUpgrade)
    {
        const string version = "11.0.0-rc.1.26425.128";
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        options.ContextLoad.HttpClient.Timeout = Timeout.InfiniteTimeSpan;
        byte[] archive = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "RealAssets", "NativePlatform",
                "runtime-rc1.nupkg"), TestContext.Current.CancellationToken);
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(new BrowserPackage(
            "Microsoft.NETCore.App.Runtime.linux-x64", version, archive,
            fromCache: false,
            producerKey: NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json")));
        string json = legacyUpgrade
            ? """
              {"f":1,"t":[[":Platform","11.0.0-rc.1.26425.128","net11.0",null]],"g":[[0]],"a":0,"x":0,"v":"overview","y":"System.Text.Json:System.Text.Json.JsonElement","l":["[\"netcore.app\",\"System.Text.Json.dll\"]"]}
              """
            : """
              {"f":6,"t":[[":Platform","11.0.0-rc.1.26425.128","net11.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"type","l":["System.Text.Json","11.0.0.0",null,"cc7b13ffcd2ddd51"],"y":"System.Text.Json.JsonElement"},"u":{"k":"type"},"f":"type.overview"}]}
              """;
        string packet = WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(json, TestContext.Current.CancellationToken));
        if (legacyUpgrade)
        {
            Assert.True(DotnetInspect.Web.Interop.Catalog.CatalogExports
                .CanUpgradeLegacyPlatformWorkspace(packet));
        }
        else
        {
            var sources = DotnetInspect.Web.Interop.Catalog
                .BrowserRetainedWorkspaceActivationService.DescribePackageSources(packet);
            Assert.True(sources.Succeeded, sources.Failure?.Message);
            Assert.Empty(sources.Sources);
        }
        CompleteRestorationRequestBasis input = legacyUpgrade
            ? new CompleteRestorationRequestBasis.LegacyPlatformUpgradeInput(packet)
            : new CompleteRestorationRequestBasis.PacketInput(packet);
        await using var owner = new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await owner.ActivateAsync(new("native", "Native Runtime", "/",
                    input), TestContext.Current.CancellationToken)).Posting;
        BrowserRetainedWorkspacePlatformPresentation platform = Assert.Single(posting.Platforms);
        Assert.Empty(posting.Packages);
        Assert.Null(platform.Surface.TotalMembers);
        Assert.All(platform.Surface.Types, type =>
        {
            Assert.Null(type.Members);
            Assert.Empty(type.Api);
            Assert.Empty(type.Signature);
        });
        Assert.Contains(platform.Surface.Types,
            static type => type.DefinitionId == "System.Text.Json.JsonElement");
        Assert.All(platform.Surface.Types, type =>
            Assert.Contains(platform.Surface.TypeKinds, kind => kind.Id == type.KindFacetId));
        Assert.Contains(platform.Surface.Accessibility,
            bucket => bucket.IsDefault && bucket.Id == "public"
                && bucket.Count == platform.Surface.Types.Length);
        Assert.Equal(version, platform.Surface.Version);
        Assert.Equal(WorkspaceSharePacketCodec.Format6Version,
            WorkspaceSharePacketCodec.Decode(posting.CanonicalPacket!,
                TestContext.Current.CancellationToken).FormatVersion);
        CompleteWorkspaceActivation activation = Assert.IsType<CompleteWorkspaceActivation>(
            platform.NativeActivation);
        Assert.Null(activation.Snapshot.Inventory);
        Assert.Null(activation.SelectedContext!.Group);
        using WorkspaceRealizationOperationLease operation = await EnterAsync(owner, "native");
        Assert.Empty(operation.Scope.Packages);
        CompleteRestorationSubjectResult<InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>> result =
            await CompleteRestorationSelectedSubjectOperation.ExecuteAdmittedAsync(
                operation, activation,
                (subject, lease, token) => ValueTask.FromResult(
                    TypeOverviewDocumentInspectionOperation.Execute(
                        new(subject.Occurrence.Library,
                            TypeOverviewHierarchyPresentation.CreateInspectionPlan(
                                BrowserExactMemberPolicy.ParseTypeIdentity(
                                    "System.Text.Json.JsonElement"),
                                TypeOverviewHierarchyPresentation.CreateCompactPlan(
                                    TypeOverviewHierarchyPresentationFormat.Tree,
                                    includeNonPublic: false),
                                BrowserApiSurfacePolicy.ExtractionBounds)),
                        lease, token)),
                TestContext.Current.CancellationToken);
        var overview = Assert.IsType<TypeOverviewDocumentInspectionOutcome.Available>(
            Assert.IsType<CompleteRestorationSubjectResult<
                InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>>.Completed>(
                    result).Content.Content);
        Assert.Equal("System.Text.Json.JsonElement",
            overview.Document.Subject.Type.ToEscapedFullName());
        Assert.IsType<TypeMemberGroupRowsOutcome.Read>(overview.Document.Members.Rows);

        DotnetInspect.Web.Interop.Metadata.BrowserTypeMetadata metadata =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports.RetainedTypeProjectionAsync(
                owner, posting.RetainedDefinitionId, posting.RealizationId,
                platform.NavigationId, "System.Text.Json.JsonElement",
                TestContext.Current.CancellationToken);
        Assert.Null(metadata.ExactTypeInspection);
        Assert.Null(metadata.TypeDependencyInspection);
        Assert.Equal("struct", metadata.TypeSignaturePresentation!.Kind);
        Assert.Equal("available", metadata.TypeSignatureInspection!.Content
            .GetProperty("outcome").GetString());
        Assert.Equal("struct", metadata.TypeProjectionInspection!.Content.Kind);
        Assert.Contains(metadata.GraphNodes, static node => node.Role == "self");
        string serialized = System.Text.Json.JsonSerializer.Serialize(
            metadata,
            DotnetInspect.Web.Interop.Metadata.BrowserMetadataJsonContext.Default
                .BrowserTypeMetadata);
        Assert.Contains("\"typeSignatureInspection\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"typeProjectionInspection\"", serialized, StringComparison.Ordinal);

        DotnetInspect.Web.Interop.Metadata.BrowserTypeMetadata policy =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports.RetainedTypeProjectionAsync(
                owner, posting.RetainedDefinitionId, posting.RealizationId,
                platform.NavigationId, "System.Text.Json.JsonNamingPolicy",
                TestContext.Current.CancellationToken);
        Assert.Contains("System.Text.Json.JsonCamelCaseNamingPolicy",
            policy.TypeProjectionInspection!.Content.DerivedTypes);

        DotnetInspect.Web.Interop.Metadata.BrowserMemberGroupDocumentInspection member =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports.RetainedMemberGroupDocumentAsync(
                owner, posting.RetainedDefinitionId, posting.RealizationId,
                platform.NavigationId, "System.Text.Json.JsonElement",
                "GetProperty", "public", "all", false,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            DotnetInspect.Web.Interop.Metadata.BrowserMemberGroupDocumentOutcome.Available,
            member.Outcome);
        Assert.NotEmpty(member.Document!.Rows);
        Assert.All(member.Document.Rows, static row =>
            Assert.Contains("GetProperty", row.DisplaySignature, StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DotnetInspect.Web.Interop.Metadata.MetadataExports.RetainedTypeProjectionAsync(
                owner, posting.RetainedDefinitionId, posting.RealizationId + "-retired",
                platform.NavigationId, "System.Text.Json.JsonElement",
                TestContext.Current.CancellationToken));
    }

    // PR-fast: actual format-5 CLI packet, pinned archive and production activation owner.
    [Theory]
    [InlineData("workspace-type-replay.json", StructuralSubjectKind.Type, "type.api")]
    [InlineData("workspace-member-replay.json", StructuralSubjectKind.Member, "member.overview")]
    public async Task SubjectReplay_ReopensRecordedCliPacketWithExactSubject(
        string exampleFile, StructuralSubjectKind kind, string facet)
    {
        string json = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "docs", "examples", exampleFile),
            TestContext.Current.CancellationToken);
        string packet = WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(
            json, TestContext.Current.CancellationToken));
        byte[] archive = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "FrameworkActivation",
            "system.text.json.10.0.0.nupkg"), TestContext.Current.CancellationToken);
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(new BrowserPackage(
            "System.Text.Json", "10.0.0", archive, fromCache: false,
            producerKey: NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json")));
        CompleteRestorationExecutionOptions options =
            (await OptionsAsync()) with { CaptureSelectedSubject = true };
        await using var owner = new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting = await ActivateAsync(owner, "saved-type", packet);
        using WorkspaceRealizationOperationLease operation = await EnterAsync(owner, "saved-type");
        Assert.Equal(packet, posting.CanonicalPacket);
        Assert.Equal(facet, posting.Navigation.Snapshot.LensOutcome.EffectiveLens?.Facet);
        Assert.Equal(kind, posting.Navigation.Snapshot.ActiveSubject.Kind);
        Assert.Contains(kind == StructuralSubjectKind.Type ? "JsonSerializer" : "Serialize",
            posting.Navigation.Snapshot.ActiveSubject.Label, StringComparison.Ordinal);
        Assert.Equal("10.0.0", Assert.Single(operation.Scope.Packages)
            .Occurrence.Package.Coordinate.Version);
        Assert.Equal("net10.0", Assert.Single(operation.Scope.Packages)
            .Occurrence.Package.Coordinate.Framework);
    }
}
