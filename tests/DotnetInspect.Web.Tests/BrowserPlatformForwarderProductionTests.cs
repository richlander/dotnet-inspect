using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspect.Web.Core;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    const string ForwarderPlatformVersion = "11.0.0-rc.1.26425.128";

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task PlatformForwarders_ProductionSourceOpensEachXmlLibraryAndRenewsActions()
    {
        var handler = new PlatformCatalogHandler(ForwarderPlatformVersion)
        {
            Packages = new()
            {
                [CatalogPackages[1]] = await File.ReadAllBytesAsync(
                    Path.Combine(AppContext.BaseDirectory, "RealAssets", "PlatformForwarderActivation", "runtime.nupkg"),
                    TestContext.Current.CancellationToken),
            },
        };
        PackageSourceAuthorization authority =
            PackageSourceAuthorization.Authorize([PackageSource.NuGetOrg]);
        using IPackageSourceClient packageClient = PackageSourceClientFactory.CreateGallery(
            authority.Authorities[0].Association, handler);
        using var networkClient = new HttpClient(handler, disposeHandler: false);
        using var navigation = new BrowserPlatformForwarderNavigation(
            networkClient,
            packageClient,
            new FixedPackageSourceAuthorization(authority),
            TimeSpan.FromSeconds(30));
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        var initial = RequireForwarderView(
            await navigation.OpenAsync(
                "net11.0", ForwarderPlatformVersion, "System.Xml.dll", "netcore.app", cancellationToken));
        Assert.Equal("System.Xml", initial.View.Coordinate.Assembly);
        using (var image = new PEReader(File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "RealAssets", "PlatformForwarderActivation", "System.Xml.dll"))))
        {
            Assert.Equal(image.GetMetadataReader().ExportedTypes.Count, initial.View.Forwarders.Length);
        }
        Assert.All(initial.View.Forwarders, row =>
        {
            Assert.Equal(LibraryTypeDeclarationKind.Forwarder, row.Declaration.DeclarationKind);
            Assert.Null(row.Declaration.DefinitionKind);
            Assert.Null(row.Declaration.MemberCount);
        });
        BrowserPlatformForwarderRowInfo first = XmlReaderForwarder(initial.View);
        Assert.Equal("System.Xml.ReaderWriter", first.Declaration.Forwarding!.TargetAssembly.Name.ToString());
        Assert.Equal(
            "canceled",
            Assert.IsType<BrowserPlatformForwarderNavigationResult.Blocked>(
                await navigation.ActivateAsync(first.Action, new CancellationToken(canceled: true))).Status);
        var intermediate = RequireForwarderView(
            await navigation.ActivateAsync(first.Action, cancellationToken));
        Assert.Equal("System.Xml.ReaderWriter", intermediate.View.Coordinate.Assembly);
        Assert.Equal("System.Xml.ReaderWriter:System.Xml.XmlReader", intermediate.View.SelectedTypeId);
        Assert.Equal(2, intermediate.Resolution!.Hops.Length);
        Assert.Equal(
            "stale",
            Assert.IsType<BrowserPlatformForwarderNavigationResult.Blocked>(
                await navigation.ActivateAsync(first.Action, cancellationToken)).Status);
        Assert.False(navigation.Close(initial.View.Id));

        BrowserPlatformForwarderRowInfo second = XmlReaderForwarder(intermediate.View);
        Assert.NotEqual(first.Action, second.Action);
        Assert.Equal("System.Private.Xml", second.Declaration.Forwarding!.TargetAssembly.Name.ToString());
        var terminal = RequireForwarderView(
            await navigation.ActivateAsync(second.Action, cancellationToken));
        Assert.Equal("System.Private.Xml", terminal.View.Coordinate.Assembly);
        Assert.Equal("System.Private.Xml:System.Xml.XmlReader", terminal.View.SelectedTypeId);
        BrowserTypeSurfaceInfo type = Assert.Single(
            terminal.View.Surface.Types,
            item => item.Id == terminal.View.SelectedTypeId);
        Assert.NotEmpty(type.Api);
        Assert.DoesNotContain(terminal.View.Forwarders,
            row => row.Declaration.Identity.ToEscapedFullName() == "System.Xml.XmlReader");
        Assert.Equal(ForwarderPlatformVersion, terminal.View.Coordinate.Version);

        using JsonDocument wire = JsonDocument.Parse(
            Interop.Package.PackageExports.SerializeForwarderResult(intermediate));
        Assert.Equal("opened", wire.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, wire.RootElement.GetProperty("hops").GetArrayLength());
        Assert.Equal(
            intermediate.View.SelectedTypeId,
            wire.RootElement.GetProperty("view").GetProperty("selectedTypeId").GetString());
        Assert.True(navigation.Close(terminal.View.Id));
        Assert.Equal(
            "stale",
            Assert.IsType<BrowserPlatformForwarderNavigationResult.Blocked>(
                await navigation.ActivateAsync(second.Action, cancellationToken)).Status);
        var returning = RequireForwarderView(
            await navigation.OpenAsync(
                "net11.0", ForwarderPlatformVersion, "System.Xml.dll", "netcore.app", cancellationToken));
        Assert.NotEqual(initial.View.Id, returning.View.Id);
        Assert.NotEqual(first.Action, XmlReaderForwarder(returning.View).Action);
        Assert.Equal(
            "stale",
            Assert.IsType<BrowserPlatformForwarderNavigationResult.Blocked>(
                await navigation.ActivateAsync(first.Action, cancellationToken)).Status);
        Assert.True(navigation.Close(returning.View.Id));
    }

    [Fact]
    public async Task PlatformForwarders_SourceFailurePreservesContribution()
    {
        var handler = new PlatformCatalogHandler("11.0.999");
        PackageSourceAuthorization authority =
            PackageSourceAuthorization.Authorize([PackageSource.NuGetOrg]);
        using IPackageSourceClient client = PackageSourceClientFactory.CreateGallery(
            authority.Authorities[0].Association, handler);
        var source = new BrowserPlatformForwarderSource(
            client, new FixedPackageSourceAuthorization(authority), TimeSpan.FromSeconds(10));
        var name = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("System.Xml", ImmutableArray.Create("XmlReader"))).Name;
        var failure = await Assert.ThrowsAsync<BrowserPlatformForwarderOperationException>(
            async () => await source.ResolveAsync(
                new(
                    new(PlatformFamily.DotNetRuntime,
                        PlatformTargetFramework.Parse("net11.0"), PlatformVersion.Parse("11.0.999")),
                    "linux-x64",
                    source.Sources,
                    new AssemblyReferenceIdentity("System.Xml", new Version(11, 0, 0, 0), null, "b77a5c561934e089"),
                    Guid.NewGuid(),
                    name,
                    [],
                    new AssemblyReferenceIdentity("System.Xml.ReaderWriter", new Version(11, 0, 0, 0), null, "b03f5f7f11d50a3a")),
                TestContext.Current.CancellationToken));
        Assert.Equal("unavailable", failure.Status);
        Assert.NotNull(failure.Contribution);
        Assert.Equal(PlatformSourceContributionKind.Unavailable, failure.Contribution.Kind);
        Assert.NotEmpty(failure.Message);
    }

    [Theory]
    [InlineData(false, "failed")]
    [InlineData(true, "canceled")]
    public async Task PlatformForwarders_OpenFailureReturnsTypedNonSuccess(bool cancel, string expectedStatus)
    {
        var handler = new PlatformCatalogHandler("11.0.999");
        PackageSourceAuthorization authority =
            PackageSourceAuthorization.Authorize([PackageSource.NuGetOrg]);
        using IPackageSourceClient packageClient = PackageSourceClientFactory.CreateGallery(
            authority.Authorities[0].Association, handler);
        using var networkClient = new HttpClient(handler, disposeHandler: false);
        using var navigation = new BrowserPlatformForwarderNavigation(
            networkClient, packageClient, new FixedPackageSourceAuthorization(authority), TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = cancel
            ? new CancellationToken(canceled: true)
            : TestContext.Current.CancellationToken;

        var blocked = Assert.IsType<BrowserPlatformForwarderNavigationResult.Blocked>(
            await navigation.OpenAsync(
                "net11.0", "11.0.999", "System.Xml.dll", "netcore.app", cancellationToken));
        Assert.Equal(expectedStatus, blocked.Status);
        Assert.NotEmpty(blocked.Message);
        Assert.Null(blocked.Resolution);
        if (!cancel)
            Assert.Contains("PlatformPackUnavailable", blocked.Message, StringComparison.Ordinal);

        using JsonDocument wire = JsonDocument.Parse(
            Interop.Package.PackageExports.SerializeForwarderResult(blocked));
        Assert.Equal(expectedStatus, wire.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, wire.RootElement.GetProperty("view").ValueKind);
        Assert.Equal(0, wire.RootElement.GetProperty("hops").GetArrayLength());
    }

    static BrowserPlatformForwarderRowInfo XmlReaderForwarder(BrowserPlatformForwarderViewInfo view) =>
        Assert.Single(view.Forwarders,
            row => row.Declaration.Identity.ToEscapedFullName() == "System.Xml.XmlReader");

    static BrowserPlatformForwarderNavigationResult.Opened RequireForwarderView(
        BrowserPlatformForwarderNavigationResult result)
    {
        if (result is BrowserPlatformForwarderNavigationResult.Blocked blocked)
            Assert.Fail($"{blocked.Status}: {blocked.Message}");
        return Assert.IsType<BrowserPlatformForwarderNavigationResult.Opened>(result);
    }
}
