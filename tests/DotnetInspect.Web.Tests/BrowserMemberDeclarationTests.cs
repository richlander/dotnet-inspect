using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Web.Interop.Metadata;
using DotnetInspect.Web.Interop.Source;
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
    const string ExtensionType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetyReceiverExtensions";
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
        BrowserMemberGroupDocumentInspection singletonGroup =
            MemberGroupDocument(
                await MetadataExports.QueryMemberGroupDocument(
                    PackageId,
                    Version,
                    Framework,
                    AssemblyFileName,
                    SpellingType,
                    "PointerFreeUnsafeMethod"));
        Assert.Equal(
            BrowserMemberGroupDocumentOutcome.Available,
            singletonGroup.Outcome);
        BrowserMemberGroupDocument singletonDocument =
            Assert.IsType<BrowserMemberGroupDocument>(
                singletonGroup.Document);
        Assert.Equal(SpellingType, singletonDocument.TypeIdentity);
        Assert.Equal("PointerFreeUnsafeMethod", singletonDocument.MemberName);
        Assert.Equal(1, singletonDocument.Count);
        BrowserMemberGroupDocumentRow singletonRow =
            Assert.Single(singletonDocument.Rows);
        Assert.Equal(1, singletonRow.BaselineOrdinal);
        Assert.Contains(
            "PointerFreeUnsafeMethod",
            singletonRow.DisplaySignature,
            StringComparison.Ordinal);

        BrowserMemberDocumentInspection ordinalMember =
            MemberDocument(
                await MetadataExports.QueryMemberDocument(
                    PackageId,
                    Version,
                    Framework,
                    AssemblyFileName,
                    SpellingType,
                    "PointerFreeUnsafeMethod",
                    singletonRow.BaselineOrdinal,
                    ""));
        BrowserMemberDocumentInspection fingerprintMember =
            MemberDocument(
                await MetadataExports.QueryMemberDocument(
                    PackageId,
                    Version,
                    Framework,
                    AssemblyFileName,
                    SpellingType,
                    "PointerFreeUnsafeMethod",
                    0,
                    singletonRow.Fingerprint));
        Assert.Equal(
            BrowserMemberDocumentOutcome.Available,
            ordinalMember.Outcome);
        BrowserMemberDocument exactDocument =
            Assert.IsType<BrowserMemberDocument>(
                ordinalMember.Document);
        Assert.Equal(SpellingType, exactDocument.TypeIdentity);
        Assert.Equal(
            "PointerFreeUnsafeMethod",
            exactDocument.MemberName);
        Assert.Equal(
            singletonRow.MetadataToken,
            exactDocument.MetadataToken);
        Assert.Equal(
            singletonRow.BaselineOrdinal,
            exactDocument.BaselineOrdinal);
        Assert.Equal(
            singletonRow.CanonicalSignature,
            exactDocument.CanonicalSignature);
        Assert.Equal(
            exactDocument,
            Assert.IsType<BrowserMemberDocument>(
                fingerprintMember.Document));

        BrowserMemberDocumentInspection missingMember =
            MemberDocument(
                await MetadataExports.QueryMemberDocument(
                    PackageId,
                    Version,
                    Framework,
                    AssemblyFileName,
                    SpellingType,
                    "PointerFreeUnsafeMethod",
                    2,
                    ""));
        Assert.Equal(
            BrowserMemberDocumentOutcome.Rejected,
            missingMember.Outcome);
        Assert.Null(missingMember.Document);
        Assert.NotNull(missingMember.Detail);

        BrowserMemberGroupDocumentInspection uploadedGroup =
            MemberGroupDocument(
                await MetadataExports.QueryUploadedLibraryMemberGroupDocument(
                    AssemblyFileName,
                    image,
                    ExtensionType,
                    "Examine"));
        Assert.Equal(
            BrowserMemberGroupDocumentOutcome.Available,
            uploadedGroup.Outcome);
        BrowserMemberGroupDocument uploadedDocument =
            Assert.IsType<BrowserMemberGroupDocument>(
                uploadedGroup.Document);
        Assert.Equal(ExtensionType, uploadedDocument.TypeIdentity);
        Assert.Equal("Examine", uploadedDocument.MemberName);
        Assert.Equal(5, uploadedDocument.Count);
        Assert.All(
            uploadedDocument.Rows,
            static row => Assert.Equal("Extension", row.Receiver));

        BrowserMemberGroupDocumentInspection missingGroup =
            MemberGroupDocument(
                await MetadataExports.QueryMemberGroupDocument(
                    PackageId,
                    Version,
                    Framework,
                    AssemblyFileName,
                    SpellingType,
                    "MissingMethod"));
        Assert.Equal(
            BrowserMemberGroupDocumentOutcome.Rejected,
            missingGroup.Outcome);
        Assert.Null(missingGroup.Document);
        Assert.NotNull(missingGroup.Detail);

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

            BrowserMemberGroupDocumentInspection group =
                MemberGroupDocument(
                    await MetadataExports.QueryPlatformMemberGroupDocument(
                        framework,
                        version,
                        AssemblyFileName,
                        "netcore.app",
                        ExtensionType,
                        "Examine"));
            Assert.Equal(
                BrowserMemberGroupDocumentOutcome.Available,
                group.Outcome);
            BrowserMemberGroupDocument document =
                Assert.IsType<BrowserMemberGroupDocument>(group.Document);
            Assert.Equal(ExtensionType, document.TypeIdentity);
            Assert.Equal("Examine", document.MemberName);
            Assert.Equal(5, document.Count);
            Assert.Equal(5, document.Rows.Length);
            Assert.Equal(
                [1, 2, 3, 4, 5],
                document.Rows.Select(static row => row.BaselineOrdinal));
            Assert.Equal(
                5,
                document.Rows.Select(static row => row.MetadataToken)
                    .Distinct()
                    .Count());
            Assert.All(
                document.Rows,
                static row => Assert.Equal("Extension", row.Receiver));
            Assert.Equal(requests, handler.Requests);
        }
        finally
        {
            await resolution.DisposeAsync();
            await BrowserPackageWorkspace.RemoveScopeAsync(resolution.Scope);
        }
    }

    [Fact]
    public async Task SelectedPlatformMemberSourceUsesPlatformWorkspace()
    {
        const string framework = "net11.0";
        const string version = "11.0.974";
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
        string assemblyName =
            Path.GetFileNameWithoutExtension(AssemblyFileName);
        var plan = new WorkspacePlan(
            [],
            [
                new WorkspaceContextInput
                {
                    Framework = framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            assemblyName,
                            version,
                            framework),
                    ],
                },
            ]);
        BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenContextAsync(
                plan,
                plan.Contexts[0],
                "runtime",
                assemblyName,
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
                await SourceExports.QueryPlatformMemberSource(
                    framework,
                    version,
                    AssemblyFileName,
                    "netcore.app",
                    type.DefinitionName!.ToEscapedFullName(),
                    member.Name,
                    Analysis.CallGraphMemberResolver
                        .CreateSelector(type, member).Key,
                    member.MetadataToken ?? 0,
                    "[]",
                    Assert.IsType<string>(resolution.ContextId));
            BrowserMemberSource source =
                JsonSerializer.Deserialize(
                    json,
                    BrowserSourceJsonContext.Default.BrowserMemberSource)
                ?? throw new InvalidOperationException(
                    "The platform member Source export returned null.");

            Assert.Equal("decompiled", source.Source.Provider);
            Assert.Contains(
                "PointerFreeUnsafeMethod",
                source.Source.Text,
                StringComparison.Ordinal);
            Assert.Empty(source.Parts);
            Assert.Equal(requests, handler.Requests);
        }
        finally
        {
            await resolution.DisposeAsync();
            await BrowserPackageWorkspace.RemoveScopeAsync(resolution.Scope);
        }
    }

    [Fact]
    public async Task PlatformTypeSourceDecompilesSystemTextJsonJsonArray()
    {
        const string framework = "net11.0";
        const string version = "11.0.976";
        const string assemblyFileName = "System.Text.Json.dll";
        byte[] image = File.ReadAllBytes(
            typeof(System.Text.Json.Nodes.JsonArray).Assembly.Location);
        using var archiveBytes = new MemoryStream();
        using (var archive = new ZipArchive(
            archiveBytes,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry(
                $"runtimes/linux-x64/lib/net11.0/{assemblyFileName}").Open();
            entry.Write(image);
        }
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                "microsoft.netcore.app.runtime.linux-x64",
                version,
                archiveBytes.ToArray(),
                fromCache: false));

        BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                framework,
                version,
                assemblyFileName,
                "netcore.app",
                TestContext.Current.CancellationToken);
        try
        {
            ApiSurface surface = resolution.Scope.UseParticipant(
                resolution.Participant,
                BrowserMemberResolution.ImplementationSurface);
            ApiType type = Assert.Single(
                surface.Types,
                candidate =>
                    candidate.FullName
                    == "System.Text.Json.Nodes.JsonArray");

            BrowserTypeSourceResult result =
                JsonSerializer.Deserialize(
                    await SourceExports.QueryPlatformTypeSource(
                        Guid.NewGuid().ToString(),
                        framework,
                        version,
                        assemblyFileName,
                        "netcore.app",
                        type.DefinitionName!.ToEscapedFullName(),
                        "[]",
                        "decompiler-source"),
                    BrowserSourceJsonContext.Default
                        .BrowserTypeSourceResult)
                ?? throw new InvalidOperationException(
                    "The System.Text.Json platform Type Source export "
                    + "returned null.");

            Assert.Equal(
                BrowserTypeSourceResultKind.Succeeded,
                result.Kind);
            var source =
                Assert.IsType<BrowserTypeCodeView.Source>(result.Value);
            Assert.Equal("decompiled", source.Value.Provider);
            Assert.Contains(
                "class JsonArray",
                source.Value.Text,
                StringComparison.Ordinal);
            Assert.Contains(
                $"runtime {version} System.Text.Json",
                source.Value.Provenance.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            await resolution.DisposeAsync();
            await BrowserPackageWorkspace.RemoveScopeAsync(resolution.Scope);
        }
    }

    [Fact]
    public async Task SelectedPlatformTypeSourceUsesRetainedPlatformWorkspace()
    {
        const string framework = "net11.0";
        const string version = "11.0.975";
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
        string assemblyName =
            Path.GetFileNameWithoutExtension(AssemblyFileName);
        var plan = new WorkspacePlan(
            [],
            [
                new WorkspaceContextInput
                {
                    Framework = framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            assemblyName,
                            version,
                            framework),
                    ],
                },
            ]);
        BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenContextAsync(
                plan,
                plan.Contexts[0],
                "runtime",
                assemblyName,
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
            int requests = handler.Requests;
            string contextId = Assert.IsType<string>(resolution.ContextId);

            BrowserTypeSourceResult source =
                JsonSerializer.Deserialize(
                    await SourceExports.QueryPlatformTypeSource(
                        Guid.NewGuid().ToString(),
                        framework,
                        version,
                        AssemblyFileName,
                        "netcore.app",
                        type.DefinitionName!.ToEscapedFullName(),
                        "[]",
                        "decompiler-source",
                        contextId),
                    BrowserSourceJsonContext.Default
                        .BrowserTypeSourceResult)
                ?? throw new InvalidOperationException(
                    "The platform Type Source export returned null.");

            Assert.Equal(
                BrowserTypeSourceResultKind.Succeeded,
                source.Kind);
            var sourceView =
                Assert.IsType<BrowserTypeCodeView.Source>(source.Value);
            Assert.Equal("decompiled", sourceView.Value.Provider);
            Assert.Contains(
                "MemorySafetySpellingFixture",
                sourceView.Value.Text,
                StringComparison.Ordinal);
            Assert.Contains(
                $"runtime {version} {assemblyName}",
                sourceView.Value.Provenance.ToString(),
                StringComparison.Ordinal);
            Assert.Null(sourceView.Value.PdbSourceLimitation);

            BrowserTypeSourceResult declarations =
                JsonSerializer.Deserialize(
                    await SourceExports.QueryPlatformTypeSource(
                        Guid.NewGuid().ToString(),
                        framework,
                        version,
                        AssemblyFileName,
                        "netcore.app",
                        type.DefinitionName.ToEscapedFullName(),
                        "[]",
                        "api-declarations",
                        contextId),
                    BrowserSourceJsonContext.Default
                        .BrowserTypeSourceResult)
                ?? throw new InvalidOperationException(
                    "The platform Type declarations export returned null.");
            var declarationView =
                Assert.IsType<BrowserTypeCodeView.ApiDeclarations>(
                    declarations.Value);
            Assert.Equal(
                TypeApiDeclarationOutcome.Available,
                declarationView.Inspection.Content.Outcome);
            Assert.Equal(
                TypeApiDeclarationScope.ApiVisible,
                declarationView.Inspection.Content.Scope);
            Assert.Contains(
                "MemorySafetySpellingFixture",
                Assert.IsType<string>(
                    declarationView.Inspection.Content.Text),
                StringComparison.Ordinal);
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

    static BrowserMemberGroupDocumentInspection MemberGroupDocument(
        string json) =>
        JsonSerializer.Deserialize(
            json,
            BrowserMetadataJsonContext.Default
                .BrowserMemberGroupDocumentInspection)
        ?? throw new InvalidOperationException(
            "The browser member-group export returned null.");

    static BrowserMemberDocumentInspection MemberDocument(
        string json) =>
        JsonSerializer.Deserialize(
            json,
            BrowserMetadataJsonContext.Default
                .BrowserMemberDocumentInspection)
        ?? throw new InvalidOperationException(
            "The browser Member export returned null.");

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
