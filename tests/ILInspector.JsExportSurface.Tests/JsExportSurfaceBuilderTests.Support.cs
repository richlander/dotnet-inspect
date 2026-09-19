using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.JsExportSurface.NamingFixtures;
using ILInspector.JsExportSurface.OperatorFixtures;
using ILInspector.JsExportSurface.PublishabilityFixtures;
using ILInspector.JsExportSurface.ScalarFixtures;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

public sealed partial class JsExportSurfaceBuilderTests
{
    static void FourArgumentCallback(
        Action<int, int, int, int> callback)
    {
    }

    private static ILInspector.JsExportSurface.JsExportSurface BuildFixtureSurface(bool includeAll = false)
    {
        using FileStream stream = File.OpenRead(typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: includeAll);
        return JsExportSurfaceBuilder.Build(apiSurface);
    }

    private static ILInspector.JsExportSurface.JsExportSurface
        BuildFixtureSurfaceWithBodies()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        return JsExportSurfaceBuilder.Build(
            apiSurface,
            OpenWireContractBodyIndex(path));
    }

    static ApiSurface ExportSurface(int token) =>
        new()
        {
            Types =
            [
                new ApiType
                {
                    Name = "Exports",
                    Members =
                    [
                        new ApiMember
                        {
                            Name = "Run",
                            Kind = "method",
                            MetadataToken = token,
                            IsStatic = true,
                            SignatureModel = new ApiSignature
                            {
                                ReturnType = "void",
                            },
                            HasRuntimeJsExport = true,
                        },
                    ],
                },
            ],
        };

    static void SelectOnlyRuntimeJsExport(
        ApiSurface surface,
        ApiMember selected)
    {
        foreach (ApiType type in surface.Types)
        {
            type.Members =
            [
                .. type.Members.Where(member =>
                    member.HasRuntimeJsExport != true
                    || ReferenceEquals(member, selected)),
            ];
        }
    }

    static int ReplaceAscii(
        byte[] image,
        string oldValue,
        string newValue)
    {
        byte[] oldBytes = Encoding.ASCII.GetBytes(oldValue);
        byte[] newBytes = Encoding.ASCII.GetBytes(newValue);
        Assert.Equal(oldBytes.Length, newBytes.Length);

        int replacements = 0;
        for (int i = 0;
            i <= image.Length - oldBytes.Length;
            i++)
        {
            if (!image
                .AsSpan(i, oldBytes.Length)
                .SequenceEqual(oldBytes))
            {
                continue;
            }

            newBytes.CopyTo(image, i);
            replacements++;
        }

        return replacements;
    }

    static byte[] BuildFakeJsExportImage(
        bool trustedAssembly = false,
        string constructorName = ".ctor",
        bool addNamedArgument = false,
        bool addDuplicateValid = false,
        bool addMalformedSibling = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Fake.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Fake"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle fakeAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    "System.Runtime.InteropServices.JavaScript"),
                new Version(11, 0, 0, 0),
                default,
                trustedAssembly
                    ? metadata.GetOrAddBlob(
                        new byte[]
                        {
                            0xcc, 0x7b, 0x13, 0xff,
                            0xcd, 0x2d, 0xdd, 0x51,
                        })
                    : default,
                default,
                default);
        TypeReferenceHandle fakeAttribute =
            metadata.AddTypeReference(
                fakeAssembly,
                metadata.GetOrAddString(
                    "System.Runtime.InteropServices.JavaScript"),
                metadata.GetOrAddString("JSExportAttribute"));
        var attributeConstructorSignature = new BlobBuilder();
        new BlobEncoder(attributeConstructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        MemberReferenceHandle attributeConstructor =
            metadata.AddMemberReference(
                fakeAttribute,
                metadata.GetOrAddString(constructorName),
                metadata.GetOrAddBlob(
                    attributeConstructorSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString(
                "ILInspector.JsExportSurface.Tests"),
            metadata.GetOrAddString("FakeJsExportFixture"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var methodSignature = new BlobBuilder();
        new BlobEncoder(methodSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: false).Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        MethodDefinitionHandle method = metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("NotAnExport"),
            metadata.GetOrAddBlob(methodSignature),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));
        var attributeValue = new BlobBuilder();
        attributeValue.WriteUInt16(1);
        if (addNamedArgument)
        {
            attributeValue.WriteUInt16(1);
            attributeValue.WriteByte(0x54);
            attributeValue.WriteByte(0x0e);
            attributeValue.WriteSerializedString("Bogus");
            attributeValue.WriteSerializedString("value");
        }
        else
        {
            attributeValue.WriteUInt16(0);
        }
        metadata.AddCustomAttribute(
            method,
            attributeConstructor,
            metadata.GetOrAddBlob(attributeValue));
        if (addDuplicateValid)
        {
            metadata.AddCustomAttribute(
                method,
                attributeConstructor,
                metadata.GetOrAddBlob(attributeValue));
        }
        if (addMalformedSibling)
        {
            var malformedValue = new BlobBuilder();
            malformedValue.WriteUInt16(1);
            malformedValue.WriteUInt16(1);
            malformedValue.WriteByte(0x54);
            malformedValue.WriteByte(0x0e);
            malformedValue.WriteSerializedString("Bogus");
            malformedValue.WriteSerializedString("value");
            metadata.AddCustomAttribute(
                method,
                attributeConstructor,
                metadata.GetOrAddBlob(malformedValue));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    private static ApiType CreateSerializerContext(
        string name,
        string recordName,
        JsonWireNamingPolicy namingPolicy) =>
        new()
        {
            Name = name,
            BaseType = "System.Text.Json.Serialization.JsonSerializerContext",
            JsonPropertyNamingPolicy = namingPolicy,
            Members =
            [
                new ApiMember
                {
                    Name = recordName,
                    Kind = "property",
                    ReturnType =
                        $"System.Text.Json.Serialization.Metadata.JsonTypeInfo<{recordName}>",
                },
            ],
        };

    static ApiSurface ExtractFixtureApiSurface()
    {
        using FileStream stream = File.OpenRead(
            typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    static JsExportSurface BuildMarshaledLongSurface(
        string typeName,
        string exportName)
    {
        string path =
            typeof(BigIntMarshalFixture).Assembly.Location;
        ApiSurface extracted = ExtractApiSurface(path);
        ApiType fixture = Assert.Single(
            extracted.Types,
            type => type.Name == typeName);
        Assert.Contains(
            fixture.Members,
            member => member.Name == exportName);
        extracted.FilteredRuntimeJsExportFacts = [];
        extracted.Types = [fixture];
        return JsExportSurfaceBuilder.Build(
            extracted,
            OpenWireContractBodyIndex(path));
    }

    static JsExportSurface BuildWith(
        string path,
        LibraryBodyIndex bodyIndex,
        FieldStoreFact extraStore)
        => JsExportSurfaceBuilder.Build(
            ExtractApiSurface(path),
            LibraryBodyIndex.FromEvidence(
                bodyIndex.Methods,
                [],
                diagnostics: bodyIndex.Diagnostics,
                directCalls: bodyIndex.DirectCalls,
                resultSinks: bodyIndex.ResultSinks,
                fieldStores: [.. bodyIndex.FieldStores, extraStore],
                fieldLoads: bodyIndex.FieldLoads,
                returnFlows: bodyIndex.ReturnFlows));

    static ApiSurface ExtractApiSurface(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    static (
        ApiSurface Surface,
        ApiType Context,
        ApiMember RootProperty,
        LibraryBodyIndex BodyIndex)
        ExtractSupportedScalarVectorSurface()
    {
        string path =
            typeof(ScalarContextOptionsFixtureExports).Assembly.Location;
        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(ScalarContextOptionsFixtureExports));
        ApiMember vectorSerializer = Assert.Single(
            exports.Members,
            member => member.Name
                == nameof(
                    ScalarContextOptionsFixtureExports.SerializeVector));
        foreach (ApiMember export in exports.Members.Where(
            member => member.HasRuntimeJsExport
                && member != vectorSerializer))
        {
            export.HasRuntimeJsExport = false;
            export.RuntimeJsExportAttributeCount = 0;
            export.HasMalformedRuntimeJsExportAttribute = false;
        }

        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(SupportedScalarContextOptions));
        ApiMember rootProperty = Assert.Single(
            context.Members,
            member => member.Name == "Int32Array");
        return (
            apiSurface,
            context,
            rootProperty,
            OpenWireContractBodyIndex(path));
    }

    static ImmutableArray<TypeRef>
        ReplaceRegistrationCoreParameter(
            ImmutableArray<TypeRef> parameters,
            int parameterIndex)
    {
        TypeRef[] replacement = [.. parameters];
        replacement[parameterIndex] = parameterIndex switch
        {
            0 => TypeRef.Definition(
                "System.Runtime",
                "System",
                "String",
                trustedFrameworkAssembly: false),
            1 => TypeRef.Definition(
                "System.Runtime",
                "System",
                "Int32",
                trustedFrameworkAssembly: false),
            2 => TypeRef.GenericInstance(
                TypeRef.Definition(
                    "System.Runtime",
                    "System",
                    "ReadOnlySpan`1",
                    trustedFrameworkAssembly: false),
                parameters[2].TypeArguments),
            _ => throw new ArgumentOutOfRangeException(
                nameof(parameterIndex)),
        };
        return [.. replacement];
    }

    static LibraryBodyIndex OpenWireContractBodyIndex(string path) =>
        LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);

    static string[] ReadMethodNames(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        return
        [
            .. reader.MethodDefinitions.Select(handle =>
                reader.GetString(reader.GetMethodDefinition(handle).Name)),
        ];
    }

    static MetadataTypeDefinitionName TopLevelDefinitionName(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [name])).Name;
}
