using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;
using Catalog = DotnetInspect.Web.Interop.Catalog;
using PackageAdmission = DotnetInspect.Web.BrowserRetainedWorkspaceAdmissionResult<DotnetInspect.Web.BrowserRetainedWorkspacePackagePresentation>;
using PlatformAdmission = DotnetInspect.Web.BrowserRetainedWorkspaceAdmissionResult<DotnetInspect.Web.BrowserRetainedWorkspacePlatformPresentation>;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task DetachedPackagePresentation_PreservesInventoryAfterClose(
        int format)
    {
        CompleteRestorationExecutionOptions options =
            await DetachedInventoryOptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting installation =
            await ActivateAsync(owner, "package", Packet(schemaVersion: format));
        await owner.DisposeAsync();

        BrowserRetainedWorkspacePackagePresentation package =
            Assert.Single(installation.Packages);
        Assert.Equal(0, package.ContextIndex);
        Assert.Empty(installation.Platforms);
        Assert.Equal("t0", Assert.Single(
            installation.Definition.Navigation!.Tabs).Id);
        Assert.Equal("g0", Assert.Single(
            installation.Definition.Workspace!.Contexts).Name);
        AssertPackagePresentation(package.Surface, "net9.0");
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedPresentation_PreservesInactivePlatformAndExactContexts(
        bool reverseContexts)
    {
        CompleteRestorationExecutionOptions options =
            await DetachedInventoryOptionsAsync(includePlatform: true);
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting installation =
            await ActivateAsync(owner, "mixed", MixedInventoryPacket(reverseContexts));
        int mixedIndex = reverseContexts ? 1 : 0;
        Assert.Equal("t1", installation.Definition.Navigation!.Focus);
        Assert.Equal($"g{mixedIndex}", installation.Definition.Scenario.Context);
        Assert.Equal(3, installation.Definition.Navigation.Tabs.Count);
        Assert.Equal(2, installation.Definition.Workspace!.Contexts.Count);
        Assert.Collection(
            installation.Packages,
            package =>
            {
                Assert.Equal("t1", package.NavigationId);
                Assert.Equal(mixedIndex, package.ContextIndex);
                AssertPackagePresentation(package.Surface, "net10.0");
            },
            package =>
            {
                Assert.Equal("t2", package.NavigationId);
                Assert.Equal(1 - mixedIndex, package.ContextIndex);
                AssertPackagePresentation(package.Surface, "net9.0");
            });
        BrowserRetainedWorkspacePlatformPresentation platform =
            Assert.Single(installation.Platforms);
        Assert.Equal("t0", platform.NavigationId);
        Assert.Equal(mixedIndex, platform.ContextIndex);
        Assert.Equal("linux-x64", platform.RuntimeIdentifier);
        Assert.IsType<PackageAdmission.Unavailable>(
            await owner.AdmitPackageAsync(
                "mixed", installation.RealizationId, platform.NavigationId,
                TestContext.Current.CancellationToken));
        var platformAdmission = Assert.IsType<PlatformAdmission.Admitted>(
            await owner.AdmitPlatformAsync(
                "mixed", installation.RealizationId, platform.NavigationId,
                TestContext.Current.CancellationToken));
        Assert.Same(platform, platformAdmission.Presentation);
        Assert.IsType<PlatformAdmission.Unavailable>(
            await owner.AdmitPlatformAsync(
                "mixed", installation.RealizationId, "t1",
                TestContext.Current.CancellationToken));
        Assert.Same(installation, owner.Active);
        Assert.Equal("t1", owner.Active?.Definition.Navigation?.Focus);
        await owner.DisposeAsync();
        AssertPlatformPresentation(platform);
    }

    [Fact]
    public async Task PlatformOnlyPresentation_PreservesBoundedInventory()
    {
        CompleteRestorationExecutionOptions options =
            await DetachedInventoryOptionsAsync(includePlatform: true);
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting installation =
            await ActivateAsync(owner, "platform", EncodeInventoryPacket(
                """
                {"f":3,"t":[[":Platform","10.0.10","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}
                """));
        Assert.Empty(installation.Packages);
        Assert.Null(installation.Definition.Navigation!.Focus);
        Assert.Equal("g0", installation.Definition.Scenario.Context);
        BrowserRetainedWorkspacePlatformPresentation platform =
            Assert.Single(installation.Platforms);
        Assert.Null(platform.RuntimeIdentifier);
        await owner.DisposeAsync();
        AssertPlatformPresentation(platform);
    }

    [Fact]
    public async Task RegistrationOnlyPresentation_PreservesDefinitionWithoutCoordinates()
    {
        CompleteRestorationExecutionOptions options =
            await DetachedInventoryOptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting installation =
            await ActivateAsync(owner, "registrations", EncodeInventoryPacket(
                """
                {"f":3,"t":[],"g":[],"r":[["p","Microsoft.Extensions."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}
                """));
        Assert.Empty(installation.Packages);
        Assert.Empty(installation.Platforms);
        Assert.Empty(installation.Definition.Navigation!.Tabs);
        Assert.Empty(installation.Definition.Workspace!.Contexts);
        Assert.Single(installation.Definition.Workspace.Registrations);
        Assert.Null(installation.Definition.Navigation.Focus);
        Assert.Null(installation.Definition.Scenario.Context);
    }

    [Fact]
    public async Task PackageAdmission_RequiresExactDefinitionRealizationAndRow()
    {
        CompleteRestorationExecutionOptions options =
            await DetachedInventoryOptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        string packet = Packet();
        BrowserRetainedWorkspacePosting first =
            await ActivateAsync(owner, "a", packet);
        var admitted =
            Assert.IsType<PackageAdmission.Admitted>(
                await owner.AdmitPackageAsync(
                    "a", first.RealizationId, "t0",
                    TestContext.Current.CancellationToken));
        Assert.Same(Assert.Single(first.Packages), admitted.Presentation);
        Assert.IsType<PackageAdmission.Unavailable>(
            await owner.AdmitPackageAsync(
                "a", first.RealizationId, "System.Text.Json",
                TestContext.Current.CancellationToken));
        Assert.IsType<PackageAdmission.Superseded>(
            await owner.AdmitPackageAsync(
                "other", first.RealizationId, "t0",
                TestContext.Current.CancellationToken));
        _ = await ActivateAsync(owner, "b", packet);
        BrowserRetainedWorkspacePosting replacement =
            await ActivateAsync(owner, "a", packet);
        Assert.NotEqual(first.RealizationId, replacement.RealizationId);
        Assert.IsType<PackageAdmission.Superseded>(
            await owner.AdmitPackageAsync(
                "a", first.RealizationId, "t0",
                TestContext.Current.CancellationToken));
        Assert.IsType<PackageAdmission.Admitted>(
            await owner.AdmitPackageAsync(
                "a", replacement.RealizationId, "t0",
                TestContext.Current.CancellationToken));
        await owner.DisposeAsync();
        Assert.IsType<PackageAdmission.Superseded>(
            await owner.AdmitPackageAsync(
                "a", replacement.RealizationId, "t0",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public Task CatalogFacade_PreservesPackageDefinitionAndAdmission() =>
        AssertCatalogFacadeAsync(includePlatform: false);

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task CatalogFacade_PreservesRegistrationOnlyDefinition(int format)
    {
        string packet = EncodeInventoryPacket(
            $$$"""
            {"f":{{{format}}},"t":[],"g":[],"r":[
                ["p","Microsoft.Extensions."],
                ["l",["p","system.text.json","9.0.4",["System.Text.Json","9.0.0.0",null,"cc7b13ffcd2ddd51"]]],
                ["l",["t","DotNetRuntime",["System.Runtime","10.0.0.0",null,"b03f5f7f11d50a3a"]]],
                ["e","ecosystem.json",["System.Text.Json"],["system.text.json"],[
                    ["l",["p","system.text.json","9.0.4",["System.Text.Json","9.0.0.0",null,"cc7b13ffcd2ddd51"]]],
                    ["t","AspNetCore"],
                    ["p","Microsoft.Extensions."]
                ]]
            ],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}
            """);
        await Catalog.BrowserRetainedWorkspaceActivationService.ResetForTestsAsync();
        try
        {
            string json = await Catalog.CatalogExports.ActivateRetainedWorkspaceDefinition(
                "registrations", "Registration-only", "/workspace", packet);
            var result = Assert.IsType<Catalog.BrowserRetainedWorkspaceActivationResult>(
                JsonSerializer.Deserialize(
                    json,
                    Catalog.BrowserCatalogJsonContext.Default
                        .BrowserRetainedWorkspaceActivationResult));
            Assert.Equal("activated", result.Status);
            var installation = Assert.IsType<Catalog.BrowserRetainedWorkspacePosting>(
                result.Posting);
            Assert.Empty(installation.Packages);
            Assert.Empty(installation.Platforms);
            Assert.Empty(installation.Definition.Tabs);
            Assert.Empty(installation.Definition.Contexts);
            Assert.Null(installation.Definition.ActiveTabId);
            Assert.Null(installation.Definition.SelectedContextId);

            Catalog.BrowserRetainedWorkspaceRegistration[] registrations =
                installation.Definition.Registrations;
            Assert.Equal(
                ["packagePrefix", "exactLibrary", "exactLibrary", "ecosystem"],
                registrations.Select(registration => registration.Kind));
            Assert.Equal("Microsoft.Extensions.", registrations[0].PackagePrefix);
            var package = Assert.IsType<Catalog.BrowserRetainedWorkspaceExactLibrary>(
                registrations[1].ExactLibrary);
            Assert.Equal("package", package.Kind);
            Assert.Equal("system.text.json", package.PackageId);
            Assert.Equal("9.0.4", package.PackageVersion);
            Assert.Null(package.PlatformFamily);
            Assert.Equal(
                new Catalog.BrowserRetainedWorkspaceLibraryIdentity(
                    "System.Text.Json", "9.0.0.0", null, "cc7b13ffcd2ddd51"),
                package.Library);
            var platform = Assert.IsType<Catalog.BrowserRetainedWorkspaceExactLibrary>(
                registrations[2].ExactLibrary);
            Assert.Equal("platform", platform.Kind);
            Assert.Equal("DotNetRuntime", platform.PlatformFamily);
            Assert.Null(platform.PackageId);
            Assert.Null(platform.PackageVersion);
            Assert.Equal(
                new Catalog.BrowserRetainedWorkspaceLibraryIdentity(
                    "System.Runtime", "10.0.0.0", null, "b03f5f7f11d50a3a"),
                platform.Library);
            var ecosystem = Assert.IsType<Catalog.BrowserRetainedWorkspaceEcosystem>(
                registrations[3].Ecosystem);
            Assert.Equal("ecosystem.json", ecosystem.Id);
            Assert.Equal(["System.Text.Json"], ecosystem.NamespaceRoots);
            Assert.Equal(["system.text.json"], ecosystem.CorePackages);
            Assert.Equal(
                ["exactLibrary", "platform", "packagePrefix"],
                ecosystem.Populations.Select(population => population.Kind));
            Assert.Equal(package, ecosystem.Populations[0].ExactLibrary);
            Assert.Equal("AspNetCore", ecosystem.Populations[1].PlatformFamily);
            Assert.Equal("Microsoft.Extensions.", ecosystem.Populations[2].PackagePrefix);
        }
        finally
        {
            await Catalog.BrowserRetainedWorkspaceActivationService.ResetForTestsAsync();
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public Task CatalogFacade_PreservesMixedDefinitionAndAdmission() =>
        AssertCatalogFacadeAsync(includePlatform: true);

    [Fact]
    public async Task CatalogFacade_PreservesConsumerAcceptedReceiptTransaction()
    {
        _ = await OptionsAsync();
        await Catalog.BrowserRetainedWorkspaceActivationService
            .ResetForTestsAsync();
        try
        {
            string packet = Packet();
            string preparationJson =
                await Catalog.CatalogExports.PrepareRetainedWorkspaceDefinition(
                    "facade-receipt",
                    "Receipt",
                    "/workspace",
                    packet);
            Catalog.BrowserRetainedWorkspacePreparationResult preparation =
                Assert.IsType<
                    Catalog.BrowserRetainedWorkspacePreparationResult>(
                        JsonSerializer.Deserialize(
                            preparationJson,
                            Catalog.BrowserCatalogJsonContext.Default
                                .BrowserRetainedWorkspacePreparationResult));
            Assert.Equal("prepared", preparation.Status);
            Assert.NotNull(preparation.Preparation);
            string receipt = Assert.IsType<string>(preparation.Receipt);
            Assert.Null(
                Catalog.BrowserRetainedWorkspaceActivationService.Owner.Active);

            string wrongCancelJson =
                await Catalog.CatalogExports.CancelRetainedWorkspaceActivation(
                    "workspace-activation-wrong");
            Catalog.BrowserRetainedWorkspaceActivationResult wrongCancel =
                Assert.IsType<Catalog.BrowserRetainedWorkspaceActivationResult>(
                    JsonSerializer.Deserialize(
                        wrongCancelJson,
                        Catalog.BrowserCatalogJsonContext.Default
                            .BrowserRetainedWorkspaceActivationResult));
            Assert.Equal("failed", wrongCancel.Status);
            Assert.Equal("InvalidRequest", wrongCancel.Failure?.Kind);

            string wrongCommitJson =
                await Catalog.CatalogExports.CommitRetainedWorkspaceActivation(
                    "workspace-activation-wrong");
            Catalog.BrowserRetainedWorkspaceActivationResult wrongCommit =
                Assert.IsType<Catalog.BrowserRetainedWorkspaceActivationResult>(
                    JsonSerializer.Deserialize(
                        wrongCommitJson,
                        Catalog.BrowserCatalogJsonContext.Default
                            .BrowserRetainedWorkspaceActivationResult));
            Assert.Equal("failed", wrongCommit.Status);

            string activationJson =
                await Catalog.CatalogExports.CommitRetainedWorkspaceActivation(
                    receipt);
            Catalog.BrowserRetainedWorkspaceActivationResult activation =
                Assert.IsType<Catalog.BrowserRetainedWorkspaceActivationResult>(
                    JsonSerializer.Deserialize(
                        activationJson,
                        Catalog.BrowserCatalogJsonContext.Default
                            .BrowserRetainedWorkspaceActivationResult));
            Assert.Equal("activated", activation.Status);
            Assert.Equal(
                "facade-receipt",
                Catalog.BrowserRetainedWorkspaceActivationService.Owner.Active!
                    .RetainedDefinitionId);

            string wrongCompletionJson =
                Catalog.CatalogExports.CompleteRetainedWorkspaceActivation(
                    "workspace-activation-wrong",
                    succeeded: true,
                    failure: null);
            Catalog.BrowserRetainedWorkspaceConsumerCompletionResult
                wrongCompletion = Assert.IsType<
                    Catalog.BrowserRetainedWorkspaceConsumerCompletionResult>(
                        JsonSerializer.Deserialize(
                            wrongCompletionJson,
                            Catalog.BrowserCatalogJsonContext.Default
                                .BrowserRetainedWorkspaceConsumerCompletionResult));
            Assert.Equal("unavailable", wrongCompletion.Status);

            string completionJson =
                Catalog.CatalogExports.CompleteRetainedWorkspaceActivation(
                    receipt,
                    succeeded: true,
                    failure: null);
            Catalog.BrowserRetainedWorkspaceConsumerCompletionResult
                completion = Assert.IsType<
                    Catalog.BrowserRetainedWorkspaceConsumerCompletionResult>(
                        JsonSerializer.Deserialize(
                            completionJson,
                            Catalog.BrowserCatalogJsonContext.Default
                                .BrowserRetainedWorkspaceConsumerCompletionResult));
            Assert.Equal("completed", completion.Status);
            Assert.True(completion.Succeeded);

            string duplicateJson =
                Catalog.CatalogExports.CompleteRetainedWorkspaceActivation(
                    receipt,
                    succeeded: true,
                    failure: null);
            Catalog.BrowserRetainedWorkspaceConsumerCompletionResult duplicate =
                Assert.IsType<
                    Catalog.BrowserRetainedWorkspaceConsumerCompletionResult>(
                        JsonSerializer.Deserialize(
                            duplicateJson,
                            Catalog.BrowserCatalogJsonContext.Default
                                .BrowserRetainedWorkspaceConsumerCompletionResult));
            Assert.Equal("unavailable", duplicate.Status);
        }
        finally
        {
            await Catalog.BrowserRetainedWorkspaceActivationService
                .ResetForTestsAsync();
        }
    }

    static async Task AssertCatalogFacadeAsync(
        bool includePlatform)
    {
        _ = await OptionsAsync();
        if (includePlatform)
        {
            foreach (string id in new[]
            {
                "Microsoft.NETCore.App.Ref",
                "Microsoft.NETCore.App.Runtime.linux-x64",
            })
            {
                byte[] archive = await File.ReadAllBytesAsync(
                    Path.Combine(AppContext.BaseDirectory, "RealAssets",
                        "FrameworkActivation", $"{id.ToLowerInvariant()}.10.0.10.nupkg"),
                    TestContext.Current.CancellationToken);
                await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
                    new BrowserPackage(
                        id, "10.0.10", archive, fromCache: false,
                        producerKey: NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json")));
            }
        }
        await Catalog.BrowserRetainedWorkspaceActivationService.ResetForTestsAsync();
        try
        {
            string packet = includePlatform ? MixedInventoryPacket(false) : Packet();
            string activeTab = includePlatform ? "t1" : "t0";
            string json = await Catalog.CatalogExports.ActivateRetainedWorkspaceDefinition(
                "facade", "Package", "/workspace", packet);
            Catalog.BrowserRetainedWorkspaceActivationResult result =
                Assert.IsType<Catalog.BrowserRetainedWorkspaceActivationResult>(
                    JsonSerializer.Deserialize(
                        json,
                        Catalog.BrowserCatalogJsonContext.Default
                            .BrowserRetainedWorkspaceActivationResult));
            Assert.Equal("activated", result.Status);
            Catalog.BrowserRetainedWorkspacePosting installation =
                Assert.IsType<Catalog.BrowserRetainedWorkspacePosting>(
                    result.Posting);
            Assert.Equal(packet, installation.CanonicalPacket);
            Assert.Equal(activeTab, installation.Definition.ActiveTabId);
            Assert.Equal("g0", installation.Definition.SelectedContextId);
            Assert.Empty(installation.Definition.Registrations);
            if (includePlatform)
            {
                Assert.Equal(["t0", "t1", "t2"],
                    installation.Definition.Tabs.Select(tab => tab.Id));
                Assert.Equal(["t0", "t1"], installation.Definition.Contexts[0].TabIds);
                Assert.Equal(["t2"], installation.Definition.Contexts[1].TabIds);
                Assert.Equal(2, installation.Packages.Length);
                Catalog.BrowserRetainedWorkspacePlatformInventory platform =
                    Assert.Single(installation.Platforms);
                Assert.Equal("t0", platform.NavigationId);
                Assert.Equal(0, platform.ContextIndex);
                Assert.Equal("runtime", platform.Family);
                Assert.Equal("linux-x64", platform.RuntimeIdentifier);
                Assert.True(platform.Summary.LibraryCount > 0);
                Assert.True(platform.Summary.TypeCount > 0);
                string platformJson = await Catalog.CatalogExports.AdmitRetainedWorkspacePlatform(
                    "facade", installation.RealizationId, platform.NavigationId);
                var platformDetail = Assert.IsType<Catalog.BrowserRetainedWorkspacePlatformAdmissionResult>(
                    JsonSerializer.Deserialize(
                        platformJson,
                        Catalog.BrowserCatalogJsonContext.Default
                            .BrowserRetainedWorkspacePlatformAdmissionResult));
                Assert.True(platformDetail.Status == "admitted", platformDetail.Message);
                Assert.NotNull(platformDetail.Platform);
                Assert.Equal(platform.Summary.LibraryCount,
                    platformDetail.Platform.Surface.Assemblies.Length);
                Assert.Equal(platform.Summary.TypeCount,
                    platformDetail.Platform.TypePage.TotalTypes);
                Assert.Equal(platform.ContextIndex, platformDetail.Platform.ContextIndex);
                Assert.Equal(platform.RuntimeIdentifier, platformDetail.Platform.RuntimeIdentifier);
                var expectedTypes = Catalog.BrowserRetainedWorkspaceActivationService.Owner.Active!
                    .Platforms.Single().Surface.Types;
                var actualTypes = new List<Catalog.BrowserTypeSurface>();
                while (true)
                {
                    AssertTransportAdmitted(platformJson);
                    Assert.Equal(actualTypes.Count, platformDetail.Platform.TypePage.Offset);
                    actualTypes.AddRange(platformDetail.Platform.Surface.Types);
                    if (platformDetail.Platform.TypePage.NextOffset is not { } nextOffset)
                        break;
                    platformJson = await Catalog.CatalogExports.AdmitRetainedWorkspacePlatform(
                        "facade", installation.RealizationId, platform.NavigationId, nextOffset);
                    platformDetail = Assert.IsType<Catalog.BrowserRetainedWorkspacePlatformAdmissionResult>(
                        JsonSerializer.Deserialize(
                            platformJson,
                            Catalog.BrowserCatalogJsonContext.Default
                                .BrowserRetainedWorkspacePlatformAdmissionResult));
                    Assert.True(platformDetail.Status == "admitted", platformDetail.Message);
                    Assert.NotNull(platformDetail.Platform);
                }
                Assert.Equal(expectedTypes.Select(type => type.QueryId),
                    actualTypes.Select(type => type.QueryId));
            }
            else
            {
                Assert.Equal("t0", Assert.Single(installation.Definition.Tabs).Id);
                Assert.Equal(["t0"], Assert.Single(installation.Definition.Contexts).TabIds);
                Assert.Empty(installation.Platforms);
            }
            Catalog.BrowserRetainedWorkspacePackageInventory package =
                Assert.Single(installation.Packages,
                    package => package.NavigationId == activeTab);
            Assert.Equal(0, package.ContextIndex);
            Assert.True(package.Summary.TypeCount > 0);
            Assert.True(package.Summary.DocumentCount > 0);
            Assert.Equal("net9.0", package.Summary.SelectedCompileFramework);
            using (JsonDocument compact = JsonDocument.Parse(json))
            {
                Assert.All(compact.RootElement.GetProperty("posting")
                    .GetProperty("packages").EnumerateArray(),
                    row => Assert.False(row.TryGetProperty("surface", out _)));
            }
            json = await Catalog.CatalogExports.AdmitRetainedWorkspacePackage(
                "facade", installation.RealizationId, package.NavigationId);
            var admitted = Assert.IsType<Catalog.BrowserRetainedWorkspacePackageAdmissionResult>(
                JsonSerializer.Deserialize(
                    json,
                    Catalog.BrowserCatalogJsonContext.Default
                        .BrowserRetainedWorkspacePackageAdmissionResult));
            Assert.Equal("admitted", admitted.Status);
            Assert.Equal(package.ConsumerPackageSubjectId, admitted.Package?.ConsumerPackageSubjectId);
            Assert.NotNull(admitted.Package);
            Assert.Equal(package.Summary.TypeCount, admitted.Package.TypePage.TotalTypes);
            Assert.Equal(0, admitted.Package.TypePage.Offset);
            Assert.InRange(admitted.Package.Surface.Types.Length, 1, 100);
            Assert.Equal(package.Summary.DocumentCount, admitted.Package.Surface.Documents.Length);
            Assert.Equal(package.Summary.MemberCount, admitted.Package.Surface.TotalMembers);
            json = await Catalog.CatalogExports.AdmitRetainedWorkspacePackage(
                "facade", "old-realization", package.NavigationId);
            var superseded = Assert.IsType<Catalog.BrowserRetainedWorkspacePackageAdmissionResult>(
                JsonSerializer.Deserialize(
                    json,
                    Catalog.BrowserCatalogJsonContext.Default
                        .BrowserRetainedWorkspacePackageAdmissionResult));
            Assert.Equal("superseded", superseded.Status);
            Assert.Null(superseded.Package);
            if (includePlatform)
            {
                string wrongKindJson = await Catalog.CatalogExports.AdmitRetainedWorkspacePlatform(
                    "facade", installation.RealizationId, package.NavigationId, 0);
                var wrongKind = JsonSerializer.Deserialize(wrongKindJson,
                    Catalog.BrowserCatalogJsonContext.Default.BrowserRetainedWorkspacePlatformAdmissionResult);
                Assert.Equal("unavailable", wrongKind?.Status);
                string invalidOffsetJson = await Catalog.CatalogExports.AdmitRetainedWorkspacePlatform(
                    "facade", installation.RealizationId, "t0", -1);
                var invalidOffset = JsonSerializer.Deserialize(invalidOffsetJson,
                    Catalog.BrowserCatalogJsonContext.Default.BrowserRetainedWorkspacePlatformAdmissionResult);
                Assert.Equal("unavailable", invalidOffset?.Status);
                Assert.Contains("offset", invalidOffset?.Message);
                var replacement = ReadActivation(
                    await Catalog.CatalogExports.ActivateRetainedWorkspaceDefinition(
                        "replacement", "Replacement", "/workspace", Packet()));
                Assert.Equal("activated", replacement.Status);
                string stalePageJson = await Catalog.CatalogExports.AdmitRetainedWorkspacePlatform(
                    "facade", installation.RealizationId, "t0", 100);
                var stalePage = JsonSerializer.Deserialize(stalePageJson,
                    Catalog.BrowserCatalogJsonContext.Default.BrowserRetainedWorkspacePlatformAdmissionResult);
                Assert.Equal("superseded", stalePage?.Status);
                Assert.Null(stalePage?.Platform);
            }
        }
        finally
        {
            await Catalog.BrowserRetainedWorkspaceActivationService.ResetForTestsAsync();
        }
    }

    static void AssertPackagePresentation(
        BrowserPackageSurfaceInfo surface,
        string requestedFramework)
    {
        Assert.Equal("System.Text.Json", surface.Package, ignoreCase: true);
        Assert.Equal("9.0.4", surface.Version);
        Assert.Equal(requestedFramework, surface.ActiveFramework);
        Assert.Equal("net9.0", surface.CompileLibrary.TargetFramework);
        Assert.Contains("net8.0", surface.Frameworks);
        Assert.Contains("net9.0", surface.Frameworks);
        Assert.NotNull(surface.Icon);
        BrowserPackageDocumentEntry document =
            Assert.Single(surface.Documents, document => document.Kind == "package");
        Assert.Equal("PACKAGE.md", document.Path);
        Assert.Equal(8582, document.Size);
        Assert.Contains(
            surface.Types,
            type => type.QueryId == "System.Text.Json.JsonSerializer");
        Assert.True(surface.TotalMembers > 0);
        Assert.Null(surface.InspectionError);
    }

    static void AssertPlatformPresentation(
        BrowserRetainedWorkspacePlatformPresentation platform)
    {
        Assert.Equal("runtime", platform.Family);
        Assert.Equal("Microsoft.NETCore.App", platform.Surface.Package);
        Assert.Equal("10.0.10", platform.Surface.Version);
        Assert.Equal("net10.0", platform.Surface.ActiveFramework);
        Assert.Single(platform.Surface.Assemblies);
        Assert.NotEmpty(platform.Surface.Types);
        Assert.NotNull(platform.Surface.InspectionError);
        Assert.All(
            platform.Surface.Types,
            type => Assert.Equal("netcore.app", type.PlatformPack));
    }

    static string MixedInventoryPacket(bool reverseContexts)
    {
        string contexts = reverseContexts ? "[[2],[0,1]]" : "[[0,1],[2]]";
        int mixedIndex = reverseContexts ? 1 : 0;
        return EncodeInventoryPacket(
            $$$"""
            {"f":3,"t":[[":Platform","10.0.10","net10.0","linux-x64"],["System.Text.Json","9.0.4","net10.0","linux-x64"],["System.Text.Json","9.0.4","net9.0",null]],"g":{{{contexts}}},"r":[],"a":1,"x":{{{mixedIndex}}},"v":[{"t":null,"u":{"k":"workspace"}},{"t":0},{"t":1,"r":{"k":"package"},"u":{"k":"package"}},{"t":2,"r":{"k":"package"},"u":{"k":"package"}}]}
            """);
    }

    static string EncodeInventoryPacket(string json) =>
        WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(
            json, TestContext.Current.CancellationToken));

    static async Task<CompleteRestorationExecutionOptions> DetachedInventoryOptionsAsync(
        bool includePlatform = false)
    {
        var store = new InMemoryPackageStore();
        await StorePackageAsync(
            "System.Text.Json",
            "9.0.4",
            Path.Combine(FindRepositoryRoot(), "fixtures", "services",
                "signatures", "system.text.json.9.0.4.nupkg"));
        if (includePlatform)
        {
            string assets = Path.Combine(AppContext.BaseDirectory,
                "RealAssets", "FrameworkActivation");
            await StorePackageAsync("Microsoft.NETCore.App.Ref", "10.0.10",
                Path.Combine(assets, "microsoft.netcore.app.ref.10.0.10.nupkg"));
            await StorePackageAsync("Microsoft.NETCore.App.Runtime.linux-x64", "10.0.10",
                Path.Combine(assets, "microsoft.netcore.app.runtime.linux-x64.10.0.10.nupkg"));
        }
        CompleteRestorationExecutionOptions options =
            BrowserCompleteRestorationOptions.Create();
        return options with
        {
            ContextLoad = options.ContextLoad with
            {
                HttpClient = new HttpClient(new RejectingHandler()),
                PackageStore = store,
            },
            PlatformSurfaceLimits = new(
                maxParticipants: 1,
                maxTypes: 5_000,
                maxMembers: 100_000,
                maxInspectionFailures: 100,
                maxTypeForwarders: 1_000,
                maxMetadataRows: 1_000_000,
                maxRetainedTextCharacters: 5_000_000),
        };

        async Task StorePackageAsync(string id, string version, string path)
        {
            await using FileStream archive = File.OpenRead(path);
            await store.CommitAsync(
                id, version, NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json"),
                archive, TestContext.Current.CancellationToken);
        }
    }
}
