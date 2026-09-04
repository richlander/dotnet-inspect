using DotnetInspector.Packages;

namespace DotnetInspector.Ecosystems.Tests;

public sealed class PackageSetRegistryTests
{
    [Fact]
    public void InitialCatalogIsDiscoverableInDeclaredOrder()
    {
        PackageSetDescriptor[] descriptors = [.. PackageSetCatalog.Discover()];

        Assert.Collection(
            descriptors,
            extensions =>
            {
                Assert.Equal(PackageSetIds.MicrosoftExtensions, extensions.Id);
                Assert.Equal("Microsoft.Extensions", extensions.Title);
                Assert.Equal(
                    "Current Microsoft.Extensions packages that add managed APIs beyond the shared frameworks.",
                    extensions.Summary);
                Assert.Equal(100, extensions.Order);
                Assert.Equal(ExpectedExtensionsPackages, PackageIds(extensions));
            },
            aspNetCore =>
            {
                Assert.Equal(PackageSetIds.AspNetCore, aspNetCore.Id);
                Assert.Equal("ASP.NET Core", aspNetCore.Title);
                Assert.Equal(
                    "Current ASP.NET Core packages that add managed APIs beyond the shared frameworks.",
                    aspNetCore.Summary);
                Assert.Equal(200, aspNetCore.Order);
                Assert.Equal(ExpectedAspNetCorePackages, PackageIds(aspNetCore));
            });
    }

    [Fact]
    public void ExactLookupReturnsEnumeratedDescriptor()
    {
        foreach (PackageSetDescriptor expected in PackageSetCatalog.Discover())
        {
            var known = Assert.IsType<PackageSetLookupResult.Known>(
                PackageSetCatalog.Lookup(expected.Id));
            Assert.Same(expected, known.Descriptor);
        }
    }

    [Fact]
    public void InvalidRegistrationsFailBeforePublication()
    {
        PackageSetId first = PackageSetId.Create("package-set.first");
        PackageSetId second = PackageSetId.Create("package-set.second");

        Assert.Throws<ArgumentException>(() => new PackageSetRegistry([]));
        Assert.Throws<ArgumentException>(() => Registry(
            Registration(first, 100, "Example.Package"),
            Registration(first, 200, "Other.Package")));
        Assert.Throws<ArgumentException>(() => Registry(
            Registration(first, 200, "Example.Package"),
            Registration(second, 100, "Other.Package")));
        Assert.Throws<ArgumentException>(() => Registry(
            Registration(first, 100, "Example.Package"),
            Registration(second, 100, "Other.Package")));
        Assert.Throws<ArgumentException>(() => Registry(
            Registration(first, 100, "invalid/package")));
        Assert.Throws<ArgumentException>(() => Registry(
            Registration(
                first,
                100,
                new PackageCoordinate("Example.Package", Version: "1.0.0"))));
        Assert.Throws<ArgumentException>(() => Registry(
            Registration(
                first,
                100,
                new PackageCoordinate("Example.Package", Framework: "net10.0"))));
        Assert.Throws<ArgumentException>(() => Registry(
            Registration(
                first,
                100,
                new PackageCoordinate("Example.Package", RuntimeIdentifier: "linux-x64"))));
        Assert.Throws<ArgumentException>(() => Registry(
            Registration(
                first,
                100,
                new PackageCoordinate("Example.Package"),
                new PackageCoordinate("example.package"))));
        Assert.Throws<ArgumentException>(() => new PackageSetRegistry(
        [
            new PackageSetRegistration(
                first,
                "",
                "Summary.",
                100,
                [new PackageCoordinate("Example.Package")]),
        ]));
    }

    [Fact]
    public void RegistrationsMayStartAtTheMinimumOrder()
    {
        PackageSetId first = PackageSetId.Create("package-set.first");
        PackageSetId second = PackageSetId.Create("package-set.second");

        PackageSetRegistry registry = new(
        [
            Registration(first, int.MinValue, "Example.First"),
            Registration(second, int.MinValue + 1, "Example.Second"),
        ]);

        Assert.Equal(
            [int.MinValue, int.MinValue + 1],
            registry.Descriptors.Select(descriptor => descriptor.Order));
    }

    [Fact]
    public void DescriptorAndMembershipAreImmutableSnapshots()
    {
        PackageSetId id = PackageSetId.Create("package-set.mutable-source");
        PackageCoordinate[] source =
        [
            new("Example.First"),
            new("Example.Second"),
        ];
        PackageSetRegistry registry = new(
        [
            new PackageSetRegistration(
                id,
                "Example",
                "Example package set.",
                100,
                source),
        ]);

        source[0] = new PackageCoordinate("Changed.Package");
        var known = Assert.IsType<PackageSetLookupResult.Known>(
            registry.Lookup(id));

        Assert.Equal(
            ["Example.First", "Example.Second"],
            PackageIds(known.Descriptor));
        IList<PackageCoordinate> exposedMembers = known.Descriptor.Members;
        Assert.Throws<NotSupportedException>(
            () => exposedMembers[0] = new PackageCoordinate("Changed.Again"));
        Assert.Equal(
            ["Example.First", "Example.Second"],
            PackageIds(known.Descriptor));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("package-set.")]
    [InlineData("package-set.1example")]
    [InlineData("package-set.Example")]
    [InlineData("package-set.example_name")]
    [InlineData("package-set.example--name")]
    [InlineData("package-set.example-")]
    [InlineData(" package-set.example")]
    [InlineData("package-set.example ")]
    [InlineData("--extensions")]
    [InlineData("Microsoft.Extensions")]
    public void InvalidTextDoesNotConstructAnIdentity(string? value)
    {
        Assert.False(PackageSetId.TryCreate(value, out PackageSetId? id));
        Assert.Null(id);
    }

    [Fact]
    public void WellKnownIdsResolveToInitialDescriptors()
    {
        Assert.Equal(
            "package-set.microsoft-extensions",
            PackageSetIds.MicrosoftExtensions.Value);
        Assert.Equal("package-set.aspnetcore", PackageSetIds.AspNetCore.Value);
        Assert.IsType<PackageSetLookupResult.Known>(
            PackageSetCatalog.Lookup(PackageSetIds.MicrosoftExtensions));
        Assert.IsType<PackageSetLookupResult.Known>(
            PackageSetCatalog.Lookup(PackageSetIds.AspNetCore));
    }

    [Fact]
    public void UnknownIdentityDoesNotAliasOrSelectADefault()
    {
        Assert.True(PackageSetId.TryCreate(
            "package-set.microsoft-extension",
            out PackageSetId? unknownId));

        var unknown = Assert.IsType<PackageSetLookupResult.Unknown>(
            PackageSetCatalog.Lookup(unknownId));

        Assert.Equal(unknownId, unknown.Id);
    }

    [Fact]
    public void InitialManifestMatchesAuditedSnapshot()
    {
        PackageSetDescriptor[] descriptors = [.. PackageSetCatalog.Discover()];

        Assert.Equal(44, ExpectedExtensionsPackages.Length);
        Assert.Equal(53, ExpectedAspNetCorePackages.Length);
        Assert.Equal(
            ExpectedExtensionsPackages,
            PackageIds(descriptors[0]));
        Assert.Equal(
            ExpectedAspNetCorePackages,
            PackageIds(descriptors[1]));
        Assert.Equal(
            ExpectedExtensionsPackages.Order(StringComparer.Ordinal),
            ExpectedExtensionsPackages);
        Assert.Equal(
            ExpectedAspNetCorePackages.Order(StringComparer.Ordinal),
            ExpectedAspNetCorePackages);
        Assert.All(
            descriptors.SelectMany(descriptor => descriptor.Members),
            member =>
            {
                Assert.Null(member.Version);
                Assert.Null(member.Framework);
                Assert.Null(member.RuntimeIdentifier);
            });
        Assert.All(
            ExpectedExtensionsPackages,
            packageId => Assert.StartsWith(
                "Microsoft.Extensions.",
                packageId,
                StringComparison.Ordinal));
        Assert.All(
            ExpectedAspNetCorePackages,
            packageId => Assert.StartsWith(
                "Microsoft.AspNetCore.",
                packageId,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            "Microsoft.Extensions.ApiDescription.Client",
            ExpectedExtensionsPackages);
        Assert.DoesNotContain(
            "Microsoft.Extensions.AI.Evaluation.Console",
            ExpectedExtensionsPackages);
        Assert.DoesNotContain(
            "Microsoft.Extensions.DependencyInjection",
            ExpectedExtensionsPackages);
        Assert.DoesNotContain(
            "Microsoft.Extensions.Logging.Log4Net.AspNetCore",
            ExpectedExtensionsPackages);
        Assert.DoesNotContain(
            "Microsoft.AspNetCore.Authentication",
            ExpectedAspNetCorePackages);
        Assert.DoesNotContain(
            "Microsoft.AspNetCore.Authentication.Abstractions",
            ExpectedAspNetCorePackages);
        Assert.DoesNotContain(
            "Microsoft.AspNetCore.Mvc.Core",
            ExpectedAspNetCorePackages);
        Assert.DoesNotContain(
            "Microsoft.AspNetCore.Mvc.Formatters.Xml.Extensions",
            ExpectedAspNetCorePackages);
        Assert.DoesNotContain(
            "Microsoft.AspNetCore.SignalR",
            ExpectedAspNetCorePackages);
    }

    private static PackageSetRegistry Registry(
        params PackageSetRegistration[] registrations) =>
        new(registrations);

    private static PackageSetRegistration Registration(
        PackageSetId id,
        int order,
        params string[] packageIds) =>
        Registration(
            id,
            order,
            [.. packageIds.Select(packageId => new PackageCoordinate(packageId))]);

    private static PackageSetRegistration Registration(
        PackageSetId id,
        int order,
        params PackageCoordinate[] members) =>
        new(id, "Title", "Summary.", order, members);

    private static string[] PackageIds(PackageSetDescriptor descriptor) =>
        [.. descriptor.Members.Select(member => member.PackageId)];

    internal static string[] ExpectedExtensionsPackages { get; } =
    [
        "Microsoft.Extensions.AI",
        "Microsoft.Extensions.AI.Abstractions",
        "Microsoft.Extensions.AI.Evaluation",
        "Microsoft.Extensions.AI.Evaluation.Quality",
        "Microsoft.Extensions.AI.Evaluation.Reporting",
        "Microsoft.Extensions.AI.Evaluation.Reporting.Azure",
        "Microsoft.Extensions.AI.OpenAI",
        "Microsoft.Extensions.AmbientMetadata.Application",
        "Microsoft.Extensions.AmbientMetadata.Build",
        "Microsoft.Extensions.AsyncState",
        "Microsoft.Extensions.AuditReports",
        "Microsoft.Extensions.Caching.Hybrid",
        "Microsoft.Extensions.Caching.SqlServer",
        "Microsoft.Extensions.Caching.StackExchangeRedis",
        "Microsoft.Extensions.Compliance.Abstractions",
        "Microsoft.Extensions.Compliance.Redaction",
        "Microsoft.Extensions.Compliance.Testing",
        "Microsoft.Extensions.DependencyInjection.AutoActivation",
        "Microsoft.Extensions.DependencyInjection.Specification.Tests",
        "Microsoft.Extensions.DependencyModel",
        "Microsoft.Extensions.Diagnostics.ExceptionSummarization",
        "Microsoft.Extensions.Diagnostics.HealthChecks.Common",
        "Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore",
        "Microsoft.Extensions.Diagnostics.HealthChecks.ResourceUtilization",
        "Microsoft.Extensions.Diagnostics.ResourceMonitoring",
        "Microsoft.Extensions.Diagnostics.ResourceMonitoring.Kubernetes",
        "Microsoft.Extensions.Diagnostics.Testing",
        "Microsoft.Extensions.Hosting.Systemd",
        "Microsoft.Extensions.Hosting.WindowsServices",
        "Microsoft.Extensions.Http.Diagnostics",
        "Microsoft.Extensions.Http.Polly",
        "Microsoft.Extensions.Http.Resilience",
        "Microsoft.Extensions.Logging.AzureAppServices",
        "Microsoft.Extensions.ObjectPool.DependencyInjection",
        "Microsoft.Extensions.Resilience",
        "Microsoft.Extensions.ServiceDiscovery",
        "Microsoft.Extensions.ServiceDiscovery.Abstractions",
        "Microsoft.Extensions.ServiceDiscovery.Dns",
        "Microsoft.Extensions.ServiceDiscovery.Yarp",
        "Microsoft.Extensions.Telemetry",
        "Microsoft.Extensions.Telemetry.Abstractions",
        "Microsoft.Extensions.TimeProvider.Testing",
        "Microsoft.Extensions.VectorData.Abstractions",
        "Microsoft.Extensions.VectorData.ConformanceTests",
    ];

    internal static string[] ExpectedAspNetCorePackages { get; } =
    [
        "Microsoft.AspNetCore.AsyncState",
        "Microsoft.AspNetCore.Authentication.Certificate",
        "Microsoft.AspNetCore.Authentication.Facebook",
        "Microsoft.AspNetCore.Authentication.Google",
        "Microsoft.AspNetCore.Authentication.JwtBearer",
        "Microsoft.AspNetCore.Authentication.MicrosoftAccount",
        "Microsoft.AspNetCore.Authentication.Negotiate",
        "Microsoft.AspNetCore.Authentication.OpenIdConnect",
        "Microsoft.AspNetCore.Authentication.Twitter",
        "Microsoft.AspNetCore.Authentication.WsFederation",
        "Microsoft.AspNetCore.AzureAppServices.HostingStartup",
        "Microsoft.AspNetCore.AzureAppServicesIntegration",
        "Microsoft.AspNetCore.Components.CustomElements",
        "Microsoft.AspNetCore.Components.QuickGrid",
        "Microsoft.AspNetCore.Components.QuickGrid.EntityFrameworkAdapter",
        "Microsoft.AspNetCore.Components.WebAssembly",
        "Microsoft.AspNetCore.Components.WebAssembly.Authentication",
        "Microsoft.AspNetCore.Components.WebAssembly.Server",
        "Microsoft.AspNetCore.Components.WebView",
        "Microsoft.AspNetCore.Components.WebView.Maui",
        "Microsoft.AspNetCore.Components.WebView.WindowsForms",
        "Microsoft.AspNetCore.Components.WebView.Wpf",
        "Microsoft.AspNetCore.ConcurrencyLimiter",
        "Microsoft.AspNetCore.DataProtection.EntityFrameworkCore",
        "Microsoft.AspNetCore.DataProtection.StackExchangeRedis",
        "Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore",
        "Microsoft.AspNetCore.Diagnostics.Middleware",
        "Microsoft.AspNetCore.Grpc.JsonTranscoding",
        "Microsoft.AspNetCore.HeaderParsing",
        "Microsoft.AspNetCore.HeaderPropagation",
        "Microsoft.AspNetCore.Hosting.WindowsServices",
        "Microsoft.AspNetCore.Http.Connections.Client",
        "Microsoft.AspNetCore.Identity.EntityFrameworkCore",
        "Microsoft.AspNetCore.Identity.UI",
        "Microsoft.AspNetCore.JsonPatch",
        "Microsoft.AspNetCore.JsonPatch.SystemTextJson",
        "Microsoft.AspNetCore.MiddlewareAnalysis",
        "Microsoft.AspNetCore.Mvc.NewtonsoftJson",
        "Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation",
        "Microsoft.AspNetCore.Mvc.Testing",
        "Microsoft.AspNetCore.OpenApi",
        "Microsoft.AspNetCore.OutputCaching.StackExchangeRedis",
        "Microsoft.AspNetCore.Owin",
        "Microsoft.AspNetCore.SignalR.Client",
        "Microsoft.AspNetCore.SignalR.Client.Core",
        "Microsoft.AspNetCore.SignalR.Protocols.MessagePack",
        "Microsoft.AspNetCore.SignalR.Protocols.NewtonsoftJson",
        "Microsoft.AspNetCore.SignalR.Specification.Tests",
        "Microsoft.AspNetCore.SignalR.StackExchangeRedis",
        "Microsoft.AspNetCore.SpaProxy",
        "Microsoft.AspNetCore.SpaServices.Extensions",
        "Microsoft.AspNetCore.TestHost",
        "Microsoft.AspNetCore.Testing",
    ];
}
