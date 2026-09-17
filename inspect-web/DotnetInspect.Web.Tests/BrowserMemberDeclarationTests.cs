using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspect.Web.Interop.Metadata;
using ILInspector.Metadata;
using NuGetFetch;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Web.Tests;

[CollectionDefinition(
    "Browser member declaration operations",
    DisableParallelization = true)]
public sealed class BrowserMemberDeclarationOperationCollection;

[Collection("Browser member declaration operations")]
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
    const string ReadonlyPropertyType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetyReadonlyPropertyFixture";
    const string ReadonlySetterPropertyType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetyReadonlySetterPropertyFixture";
    const string ExplicitLayoutType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetyExplicitLayoutFixture";
    const string AccessorType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.IMemorySafetyAccessorContract";
    const string ImplicitPropertyType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetyImplicitPropertyFixture";
    const string EnumType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetyExtensionEnum";

    [Fact]
    public async Task SelectedDeclarationsUseTypedMemorySafetyFactsWithoutChangingInventory()
    {
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath());
        using (var peReader = new PEReader(
            new MemoryStream(image, writable: false)))
        {
            ApiSurface extractedSurface = ApiSurfaceExtractor.Extract(peReader);
            ApiType extractedType = Assert.Single(
                extractedSurface.Types,
                candidate => candidate.FullName == SpellingType);
            foreach (string propertyName in
                new[]
                {
                    "Type",
                    "RequiredValue",
                    "NativeInt",
                    "InternalSet",
                    "PrivateGet",
                    "InitOnly",
                })
            {
                ApiMember property = Assert.Single(
                    extractedType.Members,
                    candidate => candidate.Name == propertyName);
                Assert.All(
                    property.SignatureModel!.Accessors,
                    accessor =>
                    {
                        Assert.True(accessor.AccessibilityIsRepresentable);
                        Assert.True(
                            accessor.DeclarationModifiersMatchProperty);
                        Assert.True(
                            accessor.DeclarationModifiersAreRepresentable);
                        Assert.False(
                            accessor.IsExplicitInterfaceImplementation);
                        Assert.True(accessor.SignatureMatchesProperty);
                    });
            }
            ApiMember restrictedProperty = Assert.Single(
                extractedType.Members,
                candidate => candidate.Name == "InternalSet");
            Assert.Equal(
                "internal",
                restrictedProperty.SignatureModel!.Accessors
                    .Single(accessor => accessor.Kind == "set")
                    .Accessibility);
            ApiMember nativeIntProperty = Assert.Single(
                extractedType.Members,
                candidate => candidate.Name == "NativeInt");
            Assert.Equal(
                ApiPrimitiveType.IntPtr,
                nativeIntProperty.SignatureModel!.ReturnTypeShape!.Primitive);
            ApiMember restrictedGetterProperty = Assert.Single(
                extractedType.Members,
                candidate => candidate.Name == "PrivateGet");
            Assert.Equal(
                "private",
                restrictedGetterProperty.SignatureModel!.Accessors
                    .Single(accessor => accessor.Kind == "get")
                    .Accessibility);
            ApiType extractedReadonlyType = Assert.Single(
                extractedSurface.Types,
                candidate => candidate.FullName == ReadonlyPropertyType);
            ApiMember readonlyProperty = Assert.Single(
                extractedReadonlyType.Members,
                candidate => candidate.Name == "Value");
            Assert.True(
                readonlyProperty.SignatureModel!.Accessors.Single().IsReadOnly);
            Assert.True(
                readonlyProperty.SignatureModel.Accessors.Single()
                    .SignatureMatchesProperty);
            ApiType extractedReadonlySetterType = Assert.Single(
                extractedSurface.Types,
                candidate => candidate.FullName == ReadonlySetterPropertyType);
            Assert.True(extractedReadonlySetterType.IsReadOnly);
            ApiMember readonlySetterProperty = Assert.Single(
                extractedReadonlySetterType.Members,
                candidate => candidate.Name == "Value");
            Assert.Contains(
                readonlySetterProperty.SignatureModel!.Accessors,
                accessor => accessor.Kind == "set");
            Assert.All(
                readonlySetterProperty.SignatureModel.Accessors,
                accessor => Assert.False(accessor.IsReadOnly));
            ApiType extractedImplicitType = Assert.Single(
                extractedSurface.Types,
                candidate => candidate.FullName == ImplicitPropertyType);
            ApiAccessor implicitAccessor = Assert.Single(
                Assert.Single(
                    extractedImplicitType.Members,
                    candidate => candidate.Name == "Value")
                .SignatureModel!.Accessors);
            Assert.True(implicitAccessor.DeclarationModifiersMatchProperty);
            Assert.False(implicitAccessor.DeclarationModifiersAreRepresentable);
        }
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                Version,
                PackagePair(image),
                fromCache: false));

        string loadJson =
            await DotnetInspect.Web.Interop.Package.PackageExports.QueryPackage(
                PackageId,
                Version,
                Framework);
        using JsonDocument loadDocument = JsonDocument.Parse(loadJson);
        string surfaceJson = loadDocument.RootElement
            .GetProperty("surface")
            .GetRawText();
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

        BrowserMemberDeclaration propertyDeclaration = await Declaration(
            spellingType,
            Member(spellingType, "Type"));
        Assert.Equal(
            "public string Type { get; set; }",
            Assert.IsType<string>(propertyDeclaration.Text));
        Assert.Null(propertyDeclaration.Unavailable);
        Assert.False(propertyDeclaration.Compatibility);

        BrowserMemberDeclaration requiredDeclaration = await Declaration(
            spellingType,
            Member(spellingType, "RequiredValue"));
        Assert.True(
            requiredDeclaration.Text is not null,
            requiredDeclaration.Unavailable);
        Assert.Equal(
            "public required string RequiredValue { get; set; }",
            Assert.IsType<string>(requiredDeclaration.Text));
        Assert.Null(requiredDeclaration.Unavailable);
        Assert.False(requiredDeclaration.Compatibility);

        BrowserMemberDeclaration nativeIntDeclaration = await Declaration(
            spellingType,
            Member(spellingType, "NativeInt"));
        Assert.Equal(
            "public nint NativeInt { get; set; }",
            Assert.IsType<string>(nativeIntDeclaration.Text));
        Assert.Null(nativeIntDeclaration.Unavailable);
        Assert.False(nativeIntDeclaration.Compatibility);

        BrowserMemberDeclaration internalSetDeclaration = await Declaration(
            spellingType,
            Member(spellingType, "InternalSet"));
        Assert.Equal(
            "public int InternalSet { get; internal set; }",
            Assert.IsType<string>(internalSetDeclaration.Text));
        Assert.Null(internalSetDeclaration.Unavailable);
        Assert.False(internalSetDeclaration.Compatibility);

        BrowserMemberDeclaration privateGetDeclaration = await Declaration(
            spellingType,
            Member(spellingType, "PrivateGet"));
        Assert.Equal(
            "public string PrivateGet { private get; set; }",
            Assert.IsType<string>(privateGetDeclaration.Text));
        Assert.Null(privateGetDeclaration.Unavailable);
        Assert.False(privateGetDeclaration.Compatibility);

        BrowserMemberDeclaration initOnlyDeclaration = await Declaration(
            spellingType,
            Member(spellingType, "InitOnly"));
        Assert.Null(initOnlyDeclaration.Text);
        Assert.Contains(
            "accessor return shape",
            Assert.IsType<string>(initOnlyDeclaration.Unavailable),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(initOnlyDeclaration.Compatibility);

        JsonElement readonlyPropertyType =
            Type(surfaceDocument.RootElement, ReadonlyPropertyType);
        BrowserMemberDeclaration readonlyPropertyDeclaration =
            await Declaration(
                readonlyPropertyType,
                Member(readonlyPropertyType, "Value"));
        Assert.Null(readonlyPropertyDeclaration.Text);
        Assert.Contains(
            "readonly accessor",
            Assert.IsType<string>(readonlyPropertyDeclaration.Unavailable),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(readonlyPropertyDeclaration.Compatibility);

        JsonElement readonlySetterPropertyType =
            Type(surfaceDocument.RootElement, ReadonlySetterPropertyType);
        BrowserMemberDeclaration readonlySetterPropertyDeclaration =
            await Declaration(
                readonlySetterPropertyType,
                Member(readonlySetterPropertyType, "Value"));
        Assert.Null(readonlySetterPropertyDeclaration.Text);
        Assert.Contains(
            "readonly struct",
            Assert.IsType<string>(
                readonlySetterPropertyDeclaration.Unavailable),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(readonlySetterPropertyDeclaration.Compatibility);

        BrowserMemberDeclaration readonlyStaticPropertyDeclaration =
            await Declaration(
                readonlySetterPropertyType,
                Member(readonlySetterPropertyType, "StaticValue"));
        Assert.Equal(
            "public static int StaticValue { get; set; }",
            Assert.IsType<string>(readonlyStaticPropertyDeclaration.Text));
        Assert.Null(readonlyStaticPropertyDeclaration.Unavailable);
        Assert.False(readonlyStaticPropertyDeclaration.Compatibility);

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
        BrowserMemberDeclaration accessorContractDeclaration =
            await Declaration(accessorType, Member(accessorType, "Value"));
        Assert.Null(accessorContractDeclaration.Text);
        Assert.Contains(
            "contract",
            Assert.IsType<string>(accessorContractDeclaration.Unavailable),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(accessorContractDeclaration.Compatibility);

        JsonElement implicitPropertyType =
            Type(surfaceDocument.RootElement, ImplicitPropertyType);
        BrowserMemberDeclaration implicitPropertyDeclaration =
            await Declaration(
                implicitPropertyType,
                Member(implicitPropertyType, "Value"));
        Assert.Null(implicitPropertyDeclaration.Text);
        Assert.Contains(
            "accessor declaration modifier",
            Assert.IsType<string>(implicitPropertyDeclaration.Unavailable),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(implicitPropertyDeclaration.Compatibility);

        JsonElement enumType = Type(surfaceDocument.RootElement, EnumType);
        Assert.DoesNotContain(
            enumType.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString() == "value__");
        BrowserMemberDeclaration enumValueDeclaration =
            await Declaration(enumType, Member(enumType, "Value"));
        Assert.Equal(
            "Value = 0",
            Assert.IsType<string>(enumValueDeclaration.Text));
        Assert.Null(enumValueDeclaration.Unavailable);
        Assert.False(enumValueDeclaration.Compatibility);
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
