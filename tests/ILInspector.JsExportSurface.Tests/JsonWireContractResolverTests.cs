using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>
/// Verifies <see cref="JsonWireContractResolver"/> and its wiring into
/// <see cref="JsExportSurfaceBuilder.Build"/> against
/// <see cref="ILInspector.JsExportSurface.Fixtures.FixtureExports"/>: proves each
/// <c>[JSExport]</c> export's actual DTO is resolved from its own body's
/// <c>JsonSerializer.Serialize</c> call site, not inferred from the assembly's whole registered
/// shape vocabulary.
/// </summary>
public sealed class JsonWireContractResolverTests
{
    private const string FixtureNamespace =
        "ILInspector.JsExportSurface.Fixtures.";

    [Fact]
    public void WireTypesEqual_DistinguishesCompleteAssemblyIdentity()
    {
        TypeRef first = ScopedType("0011223344556677");
        TypeRef equivalent = ScopedType("0011223344556677");
        TypeRef different = ScopedType("8899aabbccddeeff");

        Assert.True(
            JsonWireContractResolver.WireTypesEqual(
                first,
                equivalent));
        Assert.False(
            JsonWireContractResolver.WireTypesEqual(
                first,
                different));
    }

    [Fact]
    public void SerializerIdentityRequiresSignedSystemTextJsonAssembly()
    {
        TypeRef trusted = ExternalType(
            "System.Text.Json",
            "System.Text.Json",
            "JsonSerializer",
            "cc7b13ffcd2ddd51");
        TypeRef unsigned = ExternalType(
            "System.Text.Json",
            "System.Text.Json",
            "JsonSerializer",
            publicKeyToken: null);
        TypeRef lookalike = ExternalType(
            "Lookalikes",
            "System.Text.Json",
            "JsonSerializer",
            "cc7b13ffcd2ddd51");

        Assert.True(
            JsonWireContractResolver.IsTrustedJsonSerializerType(
                trusted));
        Assert.False(
            JsonWireContractResolver.IsTrustedJsonSerializerType(
                unsigned));
        Assert.False(
            JsonWireContractResolver.IsTrustedJsonSerializerType(
                lookalike));
    }

    static TypeRef ScopedType(string publicKeyToken)
        => ExternalType(
            "Shared",
            "Mine",
            "Result",
            publicKeyToken);

    static TypeRef ExternalType(
        string assemblyName,
        string @namespace,
        string nameValue,
        string? publicKeyToken)
    {
        var name = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                ImmutableArray.Create(nameValue)))
            .Name;
        var assembly = new AssemblyReferenceIdentity(
            assemblyName,
            new Version(1, 0, 0, 0),
            null,
            publicKeyToken);
        return TypeRef.Definition(
            assemblyName,
            @namespace,
            nameValue,
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    assembly),
                name));
    }

    private static ILInspector.JsExportSurface.JsExportSurface BuildFixtureSurfaceWithWireContracts()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);
        var bodyIndex = LibraryBodyIndex.Open(path);
        return JsExportSurfaceBuilder.Build(apiSurface, bodyIndex);
    }

    [Fact]
    public void Build_ResolvesReturnWireTypeForSyncExport()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction getWidget = Assert.Single(
            surface.Functions,
            f => f.Name == "GetWidget");
        Assert.Equal(
            FixtureNamespace + "WidgetDto",
            getWidget.ReturnWireType);
        Assert.Equal(
            [
                new ApiTypeReferenceIdentity(
                    surface.AssemblyIdentity!,
                    FixtureNamespace + "WidgetDto",
                    Assert.Single(
                        surface.Records,
                        type => type.Name == "WidgetDto")
                        .DefinitionName),
            ],
            getWidget.ReturnWireTypeReferences);
        Assert.Empty(getWidget.ParameterWireTypes);
    }

    [Fact]
    public void Build_ResolvesReturnWireTypeForAsyncExport()
    {
        // GetWidgetAsync's JsonSerializer.Serialize call is physically emitted in the compiler
        // generated state machine's MoveNext body, not GetWidgetAsync's own body. This only
        // resolves correctly because DirectCall.Caller is already attributed to the declared
        // async method (see PR #4461 / issue #4459) rather than to MoveNext.
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction getWidgetAsync = Assert.Single(
            surface.Functions,
            f => f.Name == "GetWidgetAsync");
        Assert.Equal(
            FixtureNamespace + "WidgetDto",
            getWidgetAsync.ReturnWireType);
        Assert.Empty(getWidgetAsync.ParameterWireTypes);
    }

    [Fact]
    public void Build_ResolvesParameterWireTypeForDeserializeCall()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction renameWidget = Assert.Single(
            surface.Functions,
            f => f.Name == "RenameWidget");
        Assert.Equal(
            FixtureNamespace + "WidgetDto",
            renameWidget.ReturnWireType);
        Assert.Equal(
            [FixtureNamespace + "WidgetDto"],
            renameWidget.ParameterWireTypes);
    }

    [Fact]
    public void Build_LeavesReturnWireTypeUnsetWhenBodySerializesMoreThanOneDistinctDto()
    {
        // GetWidgetOrOwner Serialize<T>'s WidgetOwner on one branch and WidgetDto on the other.
        // DirectCall carries no branch/reachability evidence to decide which one actually reaches
        // the caller, so the ambiguity must be left unresolved rather than guessed.
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction fn = Assert.Single(
            surface.Functions,
            f => f.Name == "GetWidgetOrOwner");
        Assert.Null(fn.ReturnWireType);
    }

    [Fact]
    public void Build_ResolvesContainerShapedReturnWireType()
    {
        // The Serialize<T> type argument is WidgetDto[], not WidgetDto. TypeRef.Name is empty for
        // non-Definition kinds, so this only resolves correctly via
        // ToQualifiedDisplayString().
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction fn = Assert.Single(
            surface.Functions,
            f => f.Name == "GetWidgetArray");
        Assert.Equal(
            FixtureNamespace + "WidgetDto[]",
            fn.ReturnWireType);
    }

    [Fact]
    public void Build_DoesNotTreatStreamSerializationAsReturnEnvelope()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction fn = Assert.Single(
            surface.Functions,
            function =>
                function.Name == "SerializeWidgetSideEffect");

        Assert.Null(fn.ReturnWireType);
    }

    [Fact]
    public void Build_DoesNotTreatDiscardedSerializationAsReturnEnvelope()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithWireContracts();

        JsExportFunction fn = Assert.Single(
            surface.Functions,
            function =>
                function.Name == "IgnoreSerializedWidget");

        Assert.Null(fn.ReturnWireType);
    }

    [Fact]
    public void Build_LeavesWireContractUnsetForNonEnvelopeExport()
    {
        // Ping has no JSON envelope at all (returns a non-generic Task), so no
        // JsonSerializer.Serialize/Deserialize call site exists in its body.
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurfaceWithWireContracts();

        JsExportFunction ping = Assert.Single(surface.Functions, f => f.Name == "Ping");
        Assert.Null(ping.ReturnWireType);
        Assert.Empty(ping.ParameterWireTypes);
    }

    [Fact]
    public void Build_WithoutBodyIndex_LeavesWireContractFieldsUnset()
    {
        // The overload without a LibraryBodyIndex must not attempt call-site resolution.
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);

        ILInspector.JsExportSurface.JsExportSurface surface = JsExportSurfaceBuilder.Build(apiSurface);

        Assert.All(surface.Functions, f => Assert.Null(f.ReturnWireType));
        Assert.All(surface.Functions, f => Assert.Empty(f.ParameterWireTypes));
    }

    [Fact]
    public void Build_RejectsRealAsyncStateMachineAnalysisFailure()
    {
        string sourcePath = typeof(FixtureExports).Assembly.Location;
        byte[] image = File.ReadAllBytes(sourcePath);
        int exportToken;
        int moveNextToken;
        int moveNextRva;
        using (var stream = new MemoryStream(image, writable: false))
        using (var peReader = new PEReader(stream))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            TypeDefinition fixtureType = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(type => reader.GetString(type.Name)
                    == nameof(FixtureExports));
            MethodDefinitionHandle exportHandle =
                fixtureType.GetMethods().Single(handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                        == "GetWidgetAsync");
            exportToken = MetadataTokens.GetToken(exportHandle);

            TypeDefinition stateMachine = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(type => reader.GetString(type.Name)
                    .StartsWith(
                        "<GetWidgetAsync>d__",
                        StringComparison.Ordinal));
            MethodDefinitionHandle moveNextHandle =
                stateMachine.GetMethods().Single(handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                        == "MoveNext");
            MethodDefinition moveNext =
                reader.GetMethodDefinition(moveNextHandle);
            moveNextToken = MetadataTokens.GetToken(moveNextHandle);
            moveNextRva = moveNext.RelativeVirtualAddress;

            int bodyOffset = RvaToFileOffset(
                peReader.PEHeaders,
                moveNextRva);
            image[bodyOffset] = 0x01;
        }

        string corruptedPath = Path.Combine(
            Path.GetTempPath(),
            $"tsbindgen-async-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(corruptedPath, image);
            LibraryBodyIndex bodyIndex =
                LibraryBodyIndex.Open(corruptedPath);
            AnalysisDiagnostic diagnostic = Assert.Single(
                bodyIndex.Diagnostics,
                candidate => candidate.MethodToken == moveNextToken);
            Assert.Equal(exportToken, diagnostic.SourceMethodToken);

            using FileStream source = File.OpenRead(sourcePath);
            using var sourceReader = new PEReader(source);
            ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
                sourceReader,
                includeAll: false);

            Assert.Throws<UnsupportedJsExportSurfaceException>(
                () => JsExportSurfaceBuilder.Build(
                    apiSurface,
                    bodyIndex));
        }
        finally
        {
            File.Delete(corruptedPath);
        }
    }

    static int RvaToFileOffset(PEHeaders headers, int rva)
    {
        foreach (SectionHeader section in headers.SectionHeaders)
        {
            int size = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (rva >= section.VirtualAddress
                && rva < section.VirtualAddress + size)
            {
                return section.PointerToRawData
                    + rva
                    - section.VirtualAddress;
            }
        }

        throw new InvalidOperationException(
            $"RVA 0x{rva:X8} is not in a PE section.");
    }
}
