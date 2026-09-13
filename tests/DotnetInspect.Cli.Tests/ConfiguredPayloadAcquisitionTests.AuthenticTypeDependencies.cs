using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string AuthenticDependencyVersion = "8.0.4";
    private const string NpgsqlPackage = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string RelationalPackage = "Microsoft.EntityFrameworkCore.Relational";
    private const string NpgsqlOptions =
        "Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.NpgsqlOptionsExtension";
    private const string RelationalOptions =
        "Microsoft.EntityFrameworkCore.Infrastructure.RelationalOptionsExtension";
    private const string OptionsInterface =
        "Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsExtension";

    private static readonly AuthenticDependencyPackage[] AuthenticDependencyPackages =
    [
        new(NpgsqlPackage,
            "4f9cae32541cfde509b538c7ca046ab443b8c0a73d610495603294ed94bc5885"),
        new(RelationalPackage,
            "4d13ff228afbd3c1108c3d37186c4aa26d9e2cec5c51837532816ace4de5629f"),
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Depends_AuthenticPackages_ExpandOnlySelectedPopulation(
        bool includeRelational)
    {
        ConfigureAuthenticDependencyFeed();
        var result = await RunCommandAsync(
            AuthenticDependencyArguments(includeRelational));

        Assert.True(result.Exit == 0, result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement[] edges = [.. document.RootElement.GetProperty("edges").EnumerateArray()];
        Assert.Equal(
            AuthenticRelationships(includeRelational),
            edges.Select(edge => (
                Assert.IsType<string>(edge.GetProperty("source_identity").GetProperty("type").GetString()),
                Assert.IsType<string>(edge.GetProperty("target_identity").GetProperty("type").GetString()),
                Assert.IsType<string>(edge.GetProperty("relationship").GetString()))));
        Assert.All(edges, edge =>
            Assert.Equal("declared", edge.GetProperty("resolution").GetString()));
        JsonElement root = Assert.Single(
            document.RootElement.GetProperty("nodes").EnumerateArray(),
            node => node.GetProperty("root_occurrences").GetArrayLength() > 0);
        Assert.Equal(NpgsqlOptions, root.GetProperty("identity").GetProperty("type").GetString());
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task Depends_AuthenticPackages_SelectSecondLogicalRelationship()
    {
        ConfigureAuthenticDependencyFeed();
        var result = await RunCommandAsync(
            [.. AuthenticDependencyArguments(includeRelational: true), "--rows", "2..2"]);

        Assert.True(result.Exit == 0, result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement edge = Assert.Single(
            document.RootElement.GetProperty("edges").EnumerateArray());
        Assert.Equal(RelationalOptions,
            edge.GetProperty("source_identity").GetProperty("type").GetString());
        Assert.Equal(OptionsInterface,
            edge.GetProperty("target_identity").GetProperty("type").GetString());
        Assert.Equal("interface", edge.GetProperty("relationship").GetString());
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypeDependency_AuthenticPackages_RequireTheOwningParticipant(
        bool includeRelational)
    {
        using var client = new HttpClient(new AuthenticDependencyFeedHandler());
        using var workspace = new InspectionWorkspace();
        WorkspaceMemberCoordinate[] members = includeRelational
            ?
            [
                WorkspaceMemberCoordinate.Package(RelationalPackage, AuthenticDependencyVersion),
                WorkspaceMemberCoordinate.Package(NpgsqlPackage, AuthenticDependencyVersion),
            ]
            : [WorkspaceMemberCoordinate.Package(NpgsqlPackage, AuthenticDependencyVersion)];
        WorkspaceContextLoadOutcome outcome = await WorkspaceContextLoader.LoadAsync(
            workspace,
            new WorkspaceContextInput { Framework = "net8.0", Members = members },
            new WorkspaceContextLoadOptions
            {
                HttpClient = client,
                SourceAuthorization = new UniformPackageSourceAuthorization(
                    [new PackageSource("authentic-type-dependencies", FirstFeed)]),
                PackageStore = new InMemoryPackageStore(),
                UseVersionCache = false,
            },
            TestContext.Current.CancellationToken);
        var loaded = Assert.IsType<WorkspaceContextLoadOutcome.Loaded>(outcome);
        AssemblyContextParticipant root = Assert.Single(
            loaded.Group.Participants,
            participant => participant.Assembly.Identity.Name == NpgsqlPackage);
        var provenance = Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(
            root.Assembly.Provenance);
        Assert.Equal(NpgsqlPackage, provenance.PackageId, ignoreCase: true);
        Assert.Equal(AuthenticDependencyVersion, provenance.PackageVersion);
        Assert.Equal("net8.0", loaded.Framework);

        TypeDependencySectionPlan plan = TypeDependencySectionPlan.All(NpgsqlOptions);
        TypeDependencySectionResult result =
            TypeDependencySectionExecutor.ExecuteParticipant(loaded.Group, root, plan);
        Assert.Equal(NpgsqlOptions, result.QueryResult.Dependency.MatchedType);
        Assert.True(result.RowSelection.IsSuccess);
        Assert.Equal(
            AuthenticRelationships(includeRelational),
            result.RowSelection.Relationships.Select(relationship => (
                relationship.SourceTypeName,
                relationship.TargetTypeName,
                relationship.Kind == TypeDependencyRelationshipKind.BaseType
                    ? "base-type"
                    : "interface")));
        Assert.All(result.QueryResult.Participants, participant =>
            Assert.IsType<AssemblyContextTypeDependencyEntry.Completed>(participant));

        if (includeRelational)
        {
            AssemblyContextParticipant other = Assert.Single(
                loaded.Group.Participants,
                participant => participant.Assembly.Identity.Name == RelationalPackage);
            TypeDependencySectionResult wrongRoot =
                TypeDependencySectionExecutor.ExecuteParticipant(loaded.Group, other, plan);
            Assert.False(wrongRoot.QueryResult.Dependency.Found);
            Assert.Empty(wrongRoot.RowSelection.Relationships);
        }
    }

    private static (string Source, string Target, string Kind)[] AuthenticRelationships(
        bool includeRelational) =>
        includeRelational
            ?
            [
                (NpgsqlOptions, RelationalOptions, "base-type"),
                (RelationalOptions, OptionsInterface, "interface"),
            ]
            : [(NpgsqlOptions, RelationalOptions, "base-type")];

    private static string[] AuthenticDependencyArguments(bool includeRelational) =>
    [
        "depends", NpgsqlOptions,
        "--package", $"{NpgsqlPackage}@{AuthenticDependencyVersion}",
        .. includeRelational
            ? new[] { "--package", $"{RelationalPackage}@{AuthenticDependencyVersion}" }
            : [],
        "--tfm", "net8.0",
        "--source", FirstFeed,
        "--json",
        "--tips", "q",
    ];

    private static void ConfigureAuthenticDependencyFeed()
    {
        CoreHttpClientFactory.SetAuthenticationDecorator(_ => new AuthenticDependencyFeedHandler());
        CoreHttpClientFactory.ResetSharedForTesting();
    }

    private sealed record AuthenticDependencyPackage(string Id, string Sha256)
    {
        internal byte[] ReadArchive()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "RealAssets", "TypeDependencies",
                $"{Id.ToLowerInvariant()}.{AuthenticDependencyVersion}.nupkg");
            byte[] archive = File.ReadAllBytes(path);
            Assert.Equal(Sha256, Convert.ToHexStringLower(SHA256.HashData(archive)));
            return archive;
        }
    }

    private sealed class AuthenticDependencyFeedHandler : HttpMessageHandler
    {
        private static readonly string Flat = new Uri(new Uri(FirstFeed), "flat2/").AbsoluteUri;
        private readonly Dictionary<string, byte[]> _archives = AuthenticDependencyPackages
            .ToDictionary(
                package => $"{Flat}{package.Id.ToLowerInvariant()}/{AuthenticDependencyVersion}/"
                    + $"{package.Id.ToLowerInvariant()}.{AuthenticDependencyVersion}.nupkg",
                package => package.ReadArchive(),
                StringComparer.Ordinal);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            HttpContent content = url == FirstFeed
                ? new StringContent($$"""
                    {"version":"3.0.0","resources":[
                      {"@id":"{{Flat}}","@type":"PackageBaseAddress/3.0.0"}
                    ]}
                    """)
                : _archives.TryGetValue(url, out byte[]? archive)
                    ? new ByteArrayContent(archive)
                    : throw new InvalidOperationException($"Unexpected authentic package request: {url}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
                RequestMessage = request,
            });
        }
    }
}
