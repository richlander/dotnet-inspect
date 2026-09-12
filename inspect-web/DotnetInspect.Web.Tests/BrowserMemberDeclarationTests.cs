using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspect.Web.Interop.Metadata;
using ILInspector.Metadata;
using NuGetFetch;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserMemberDeclarationTests
{
    const string PackageId = "Browser.Member.Declarations";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    const string AssemblyFileName =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.dll";
    const string SpellingType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetySpellingFixture";
    const string ExplicitLayoutType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetyExplicitLayoutFixture";
    const string AccessorType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.IMemorySafetyAccessorContract";

    [Fact]
    public async Task SelectedDeclarationsUseTypedMemorySafetyFactsWithoutChangingInventory()
    {
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath());
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                Version,
                PackagePair(image),
                fromCache: false));

        string surfaceJson =
            await DotnetInspect.Web.Interop.Package.PackageExports.QueryPackage(
                PackageId,
                Version,
                Framework);
        using JsonDocument surfaceDocument = JsonDocument.Parse(surfaceJson);

        JsonElement spellingType = Type(surfaceDocument.RootElement, SpellingType);
        JsonElement pointerFree = Member(spellingType, "PointerFreeUnsafeMethod");
        JsonElement pointerNone = Member(spellingType, "PointerNoneMethod");

        BrowserMemberDeclaration pointerFreeDeclaration =
            await Declaration(spellingType, pointerFree);
        Assert.Contains(
            "unsafe",
            Assert.IsType<string>(pointerFreeDeclaration.Text),
            StringComparison.Ordinal);
        Assert.Null(pointerFreeDeclaration.Unavailable);
        Assert.False(pointerFreeDeclaration.Compatibility);
        Assert.DoesNotContain(
            "unsafe",
            pointerFree.GetProperty("signature").GetString()!,
            StringComparison.Ordinal);

        BrowserMemberDeclaration pointerNoneDeclaration =
            await Declaration(spellingType, pointerNone);
        Assert.DoesNotContain(
            "unsafe",
            Assert.IsType<string>(pointerNoneDeclaration.Text),
            StringComparison.Ordinal);
        Assert.Null(pointerNoneDeclaration.Unavailable);
        Assert.False(pointerNoneDeclaration.Compatibility);

        JsonElement explicitLayoutType =
            Type(surfaceDocument.RootElement, ExplicitLayoutType);
        BrowserMemberDeclaration safeFieldDeclaration = await Declaration(
            explicitLayoutType,
            Member(explicitLayoutType, "SafeInstanceField"));
        string safeFieldText =
            Assert.IsType<string>(safeFieldDeclaration.Text);
        Assert.Contains("safe", safeFieldText, StringComparison.Ordinal);
        Assert.Contains(
            "FieldOffsetAttribute(0)",
            safeFieldText,
            StringComparison.Ordinal);
        Assert.Null(safeFieldDeclaration.Unavailable);
        Assert.False(safeFieldDeclaration.Compatibility);

        JsonElement accessorType = Type(surfaceDocument.RootElement, AccessorType);
        BrowserMemberDeclaration propertyDeclaration =
            await Declaration(accessorType, Member(accessorType, "Value"));
        Assert.Null(propertyDeclaration.Text);
        Assert.Contains(
            "not supported",
            Assert.IsType<string>(propertyDeclaration.Unavailable),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(propertyDeclaration.Compatibility);
    }

    [Fact]
    public async Task SelectedPlatformDeclarationUsesPlatformWorkspace()
    {
        const string framework = "net11.0";
        const string version = "11.0.973";
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath());
        using var archiveBytes = new MemoryStream();
        using (var archive = new ZipArchive(
            archiveBytes,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry(
                $"runtimes/linux-x64/lib/net11.0/{AssemblyFileName}").Open();
            entry.Write(image);
        }

        using var handler = new PlatformHandler(
            version,
            archiveBytes.ToArray());
        using var client = new HttpClient(handler);
        BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                framework,
                version,
                AssemblyFileName,
                "netcore.app",
                client,
                new UniformPackageSourceAuthorization(
                    [PackageSource.NuGetOrg]),
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        try
        {
            ApiSurface surface = resolution.Scope.UseParticipant(
                resolution.Participant,
                BrowserMemberResolution.ImplementationSurface);
            ApiType type = Assert.Single(
                surface.Types,
                candidate => candidate.FullName == SpellingType);
            ApiMember member = Assert.Single(
                type.Members,
                candidate => candidate.Name == "PointerFreeUnsafeMethod");
            int requests = handler.Requests;

            string json =
                await MetadataExports.QueryPlatformMemberDeclaration(
                    framework,
                    version,
                    AssemblyFileName,
                    "netcore.app",
                    type.DefinitionName!.ToEscapedFullName(),
                    member.Name,
                    Analysis.CallGraphMemberResolver
                        .CreateSelector(type, member).Key,
                    member.DeclarationMetadataToken
                        ?? member.MetadataToken
                        ?? 0);
            BrowserMemberDeclaration declaration =
                JsonSerializer.Deserialize(
                    json,
                    BrowserMetadataJsonContext.Default
                        .BrowserMemberDeclaration)
                ?? throw new InvalidOperationException(
                    "The platform declaration export returned null.");

            Assert.Contains(
                "unsafe",
                Assert.IsType<string>(declaration.Text),
                StringComparison.Ordinal);
            Assert.Null(declaration.Unavailable);
            Assert.False(declaration.Compatibility);
            Assert.Equal(requests, handler.Requests);
        }
        finally
        {
            await resolution.DisposeAsync();
            await BrowserPackageWorkspace.RemoveScopeAsync(resolution.Scope);
        }
    }

    static JsonElement Type(JsonElement root, string definitionId) =>
        Assert.Single(
            root.GetProperty("types").EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                == definitionId);

    static JsonElement Member(JsonElement type, string name) =>
        Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate => candidate.GetProperty("name").GetString() == name);

    static async Task<BrowserMemberDeclaration> Declaration(
        JsonElement type,
        JsonElement member)
    {
        JsonElement declarationToken =
            member.GetProperty("declarationMetadataToken");
        JsonElement bodyToken = member.GetProperty("metadataToken");
        string json =
            await MetadataExports.QueryMemberDeclaration(
                PackageId,
                Version,
                Framework,
                type.GetProperty("assembly").GetString()!,
                type.GetProperty("definitionId").GetString()!,
                member.GetProperty("name").GetString()!,
                member.GetProperty("graphSelectorKey").GetString()!,
                declarationToken.ValueKind == JsonValueKind.Number
                    ? declarationToken.GetInt32()
                    : bodyToken.ValueKind == JsonValueKind.Number
                        ? bodyToken.GetInt32()
                        : 0,
                implementationMember: false);
        return JsonSerializer.Deserialize(
                json,
                BrowserMetadataJsonContext.Default.BrowserMemberDeclaration)
            ?? throw new InvalidOperationException(
                "The browser declaration export returned null.");
    }

    static byte[] PackagePair(byte[] image)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(
            content,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (string path in
                new[]
                {
                    $"ref/{Framework}/{AssemblyFileName}",
                    $"lib/{Framework}/{AssemblyFileName}",
                })
            {
                using Stream entry = archive
                    .CreateEntry(path, CompressionLevel.NoCompression)
                    .Open();
                entry.Write(image);
            }
        }

        return content.ToArray();
    }

    sealed class PlatformHandler(
        string version,
        byte[] archive) : HttpMessageHandler
    {
        internal int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            string url = request.RequestUri!.AbsoluteUri;
            const string package =
                "microsoft.netcore.app.runtime.linux-x64";
            HttpContent? content = url switch
            {
                $"https://api.nuget.org/v3-flatcontainer/{package}/index.json" =>
                    new StringContent(
                        $$"""{"versions":["{{version}}"]}"""),
                $"https://api.nuget.org/v3/registration5-gz-semver2/{package}/index.json" =>
                    new StringContent(
                        $$$"""{"items":[{"items":[{"catalogEntry":{"version":"{{{version}}}","listed":true}}]}]}"""),
                _ when url ==
                    $"https://api.nuget.org/v3-flatcontainer/{package}/{version}/{package}.{version}.nupkg" =>
                    new ByteArrayContent(archive),
                _ => null,
            };
            return Task.FromResult(new HttpResponseMessage(
                content is null
                    ? System.Net.HttpStatusCode.NotFound
                    : System.Net.HttpStatusCode.OK)
            {
                Content = content,
            });
        }
    }
}
