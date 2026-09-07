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
            },
            aspire =>
            {
                Assert.Equal(PackageSetIds.Aspire, aspire.Id);
                Assert.Equal("Aspire", aspire.Title);
                Assert.Equal(
                    "Current aspire-co-owned Aspire packages with public managed APIs.",
                    aspire.Summary);
                Assert.Equal(300, aspire.Order);
                Assert.Equal(ExpectedAspirePackages, PackageIds(aspire));
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
        Assert.Equal("package-set.aspire", PackageSetIds.Aspire.Value);
        Assert.IsType<PackageSetLookupResult.Known>(
            PackageSetCatalog.Lookup(PackageSetIds.MicrosoftExtensions));
        Assert.IsType<PackageSetLookupResult.Known>(
            PackageSetCatalog.Lookup(PackageSetIds.AspNetCore));
        Assert.IsType<PackageSetLookupResult.Known>(
            PackageSetCatalog.Lookup(PackageSetIds.Aspire));
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
        Assert.Equal(82, ExpectedAspirePackages.Length);
        Assert.Equal(
            ExpectedExtensionsPackages,
            PackageIds(descriptors[0]));
        Assert.Equal(
            ExpectedAspNetCorePackages,
            PackageIds(descriptors[1]));
        Assert.Equal(
            ExpectedAspirePackages,
            PackageIds(descriptors[2]));
        Assert.Equal(
            ExpectedExtensionsPackages.Order(StringComparer.Ordinal),
            ExpectedExtensionsPackages);
        Assert.Equal(
            ExpectedAspNetCorePackages.Order(StringComparer.Ordinal),
            ExpectedAspNetCorePackages);
        Assert.Equal(
            ExpectedAspirePackages.Order(StringComparer.Ordinal),
            ExpectedAspirePackages);
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
        Assert.All(
            ExpectedAspirePackages,
            packageId => Assert.StartsWith(
                "Aspire.",
                packageId,
                StringComparison.Ordinal));
        Assert.Contains("Aspire.Hosting", ExpectedAspirePackages);
        Assert.Contains("Aspire.Hosting.Testing", ExpectedAspirePackages);
        Assert.Contains("Aspire.TypeSystem", ExpectedAspirePackages);
        Assert.Contains("Aspire.Hosting.AWS", ExpectedAspirePackages);
        Assert.Contains("Aspire.Hosting.Python", ExpectedAspirePackages);
        Assert.Contains("Aspire.MongoDB.Driver.v2", ExpectedAspirePackages);
        Assert.Contains("Aspire.RabbitMQ.Client.v6", ExpectedAspirePackages);
        Assert.DoesNotContain("Aspire.Hosting.AppHost", ExpectedAspirePackages);
        Assert.DoesNotContain(
            "Aspire.Hosting.CodeGeneration.TypeScript",
            ExpectedAspirePackages);
        Assert.DoesNotContain(
            "Aspire.Dashboard.Sdk.linux-x64",
            ExpectedAspirePackages);
        Assert.DoesNotContain(
            "Aspire.Hosting.Orchestration.linux-x64",
            ExpectedAspirePackages);
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

    internal static string[] ExpectedAspirePackages { get; } =
    [
        "Aspire.Azure.Data.Tables",
        "Aspire.Azure.Messaging.EventHubs",
        "Aspire.Azure.Messaging.ServiceBus",
        "Aspire.Azure.Messaging.WebPubSub",
        "Aspire.Azure.Npgsql",
        "Aspire.Azure.Npgsql.EntityFrameworkCore.PostgreSQL",
        "Aspire.Azure.Search.Documents",
        "Aspire.Azure.Security.KeyVault",
        "Aspire.Azure.Storage.Blobs",
        "Aspire.Azure.Storage.Queues",
        "Aspire.Confluent.Kafka",
        "Aspire.Elastic.Clients.Elasticsearch",
        "Aspire.Hosting",
        "Aspire.Hosting.AWS",
        "Aspire.Hosting.Azure",
        "Aspire.Hosting.Azure.AppConfiguration",
        "Aspire.Hosting.Azure.AppContainers",
        "Aspire.Hosting.Azure.AppService",
        "Aspire.Hosting.Azure.ApplicationInsights",
        "Aspire.Hosting.Azure.CognitiveServices",
        "Aspire.Hosting.Azure.ContainerRegistry",
        "Aspire.Hosting.Azure.CosmosDB",
        "Aspire.Hosting.Azure.EventHubs",
        "Aspire.Hosting.Azure.Functions",
        "Aspire.Hosting.Azure.KeyVault",
        "Aspire.Hosting.Azure.Network",
        "Aspire.Hosting.Azure.OperationalInsights",
        "Aspire.Hosting.Azure.PostgreSQL",
        "Aspire.Hosting.Azure.Redis",
        "Aspire.Hosting.Azure.Search",
        "Aspire.Hosting.Azure.ServiceBus",
        "Aspire.Hosting.Azure.SignalR",
        "Aspire.Hosting.Azure.Sql",
        "Aspire.Hosting.Azure.Storage",
        "Aspire.Hosting.Azure.WebPubSub",
        "Aspire.Hosting.DevTunnels",
        "Aspire.Hosting.Docker",
        "Aspire.Hosting.Elasticsearch",
        "Aspire.Hosting.Garnet",
        "Aspire.Hosting.GitHub.Models",
        "Aspire.Hosting.JavaScript",
        "Aspire.Hosting.Kafka",
        "Aspire.Hosting.Milvus",
        "Aspire.Hosting.MongoDB",
        "Aspire.Hosting.MySql",
        "Aspire.Hosting.Nats",
        "Aspire.Hosting.OpenAI",
        "Aspire.Hosting.Oracle",
        "Aspire.Hosting.Orleans",
        "Aspire.Hosting.PostgreSQL",
        "Aspire.Hosting.Python",
        "Aspire.Hosting.Qdrant",
        "Aspire.Hosting.RabbitMQ",
        "Aspire.Hosting.Redis",
        "Aspire.Hosting.Seq",
        "Aspire.Hosting.SqlServer",
        "Aspire.Hosting.Testing",
        "Aspire.Hosting.Valkey",
        "Aspire.Hosting.Yarp",
        "Aspire.Microsoft.Azure.Cosmos",
        "Aspire.Microsoft.Azure.StackExchangeRedis",
        "Aspire.Microsoft.Data.SqlClient",
        "Aspire.Microsoft.EntityFrameworkCore.Cosmos",
        "Aspire.Microsoft.EntityFrameworkCore.SqlServer",
        "Aspire.Microsoft.Extensions.Configuration.AzureAppConfiguration",
        "Aspire.MongoDB.Driver",
        "Aspire.MongoDB.Driver.v2",
        "Aspire.MongoDB.EntityFrameworkCore",
        "Aspire.MySqlConnector",
        "Aspire.NATS.Net",
        "Aspire.Npgsql",
        "Aspire.Npgsql.EntityFrameworkCore.PostgreSQL",
        "Aspire.Oracle.EntityFrameworkCore",
        "Aspire.Pomelo.EntityFrameworkCore.MySql",
        "Aspire.Qdrant.Client",
        "Aspire.RabbitMQ.Client",
        "Aspire.RabbitMQ.Client.v6",
        "Aspire.Seq",
        "Aspire.StackExchange.Redis",
        "Aspire.StackExchange.Redis.DistributedCaching",
        "Aspire.StackExchange.Redis.OutputCaching",
        "Aspire.TypeSystem",
    ];
}
