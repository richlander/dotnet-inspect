using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ExactTypeWorkspaceRouteTests
{
    const string PackageId = "ilinspector.metadata.test";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    const string SourceUrl = "https://example.test/v3/index.json";

    static readonly PackageSource Source =
        new("test", SourceUrl);

    [Fact]
    public async Task EligiblePinnedPackageRouteUsesInjectedWorkspaceCapabilities()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = client,
                        SourceAuthorization =
                            new UniformPackageSourceAuthorization([Source]),
                        PackageStore = store,
                    }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains(
            "ILInspector.Metadata.ApiType",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "string? Accessibility { get; set; }",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RicherViewsRemainOnCompatibilityPath()
    {
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
        };

        Assert.True(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options,
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    Verbosity = Verbosity.Normal,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    IncludeSections = ["Summary"],
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    JsonOutput = true,
                    FormatExplicitlySet = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with { IncludeAll = true },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with { DocsExplicitlySet = true },
                out _));
        Assert.DoesNotContain(
            typeof(ExactTypeInspectionRequest).GetProperties(),
            property => property.Name == "IncludeAll");
    }

    [Fact]
    public async Task EligibleRoutePreservesDiagnosticsAndIncompleteExit()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/PartiallyMalformed.dll",
                BuildPartiallyMalformedTypeAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = "Exact.Type.Good",
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = client,
                        SourceAuthorization =
                            new UniformPackageSourceAuthorization([Source]),
                        PackageStore = store,
                    }));

        Assert.Equal(1, exitCode);
        Assert.Contains("Exact.Type.Good", output, StringComparison.Ordinal);
        Assert.Contains("Warning:", error, StringComparison.Ordinal);
        Assert.Contains(
            "MalformedMetadata",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EligibleRoutePreservesDiagnosticsForNotFoundOutcome()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/PartiallyMalformed.dll",
                BuildPartiallyMalformedTypeAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = "Exact.Type.Malformed",
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = client,
                        SourceAuthorization =
                            new UniformPackageSourceAuthorization([Source]),
                        PackageStore = store,
                    }));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("Warning:", error, StringComparison.Ordinal);
        Assert.Contains(
            "MalformedMetadata",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            "Type 'Exact.Type.Malformed' was not found.",
            error,
            StringComparison.Ordinal);
    }

    static async Task<IPackageStore> CachedStoreAsync(
        params (string EntryPath, byte[] Content)[] entries)
    {
        var store = new InMemoryPackageStore();
        if (entries.Length == 0)
        {
            entries =
            [
                ($"lib/{Framework}/ILInspector.Metadata.dll",
                    await File.ReadAllBytesAsync(
                        typeof(ApiType).Assembly.Location,
                        TestContext.Current.CancellationToken)),
            ];
        }
        byte[] package = Archive(entries);
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(SourceUrl),
            new MemoryStream(package),
            TestContext.Current.CancellationToken);
        return store;
    }

    static byte[] BuildPartiallyMalformedTypeAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString("PartiallyMalformed.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("PartiallyMalformed"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        TypeSpecificationHandle malformedBase =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(new byte[] { 0x15 }));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Exact.Type"),
            metadata.GetOrAddString("Good"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Exact.Type"),
            metadata.GetOrAddString("Malformed"),
            baseType: malformedBase,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static byte[] Archive(
        params (string EntryPath, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string entryPath, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(entryPath).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"The eligible route bypassed injected Workspace capabilities: "
                + request.RequestUri);
    }
}
