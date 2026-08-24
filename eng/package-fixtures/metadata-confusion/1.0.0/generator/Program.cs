using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

const string FixtureVersion = "1.0.0";
const string AssemblyPath =
    "lib/net11.0/DotnetInspect.TestAssets.MetadataConfusion.dll";
const string ManifestPath = "content/metadata-fixture.json";

return args switch
{
    ["generate", string assemblyPath, string manifestPath] =>
        Generate(assemblyPath, manifestPath),
    ["verify-package", string packagePath] =>
        VerifyPackage(packagePath),
    _ => Usage(),
};

static int Generate(string assemblyPath, string manifestPath)
{
    Fixture fixture = BuildDeterministically();
    Directory.CreateDirectory(
        Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!);
    Directory.CreateDirectory(
        Path.GetDirectoryName(Path.GetFullPath(manifestPath))!);
    File.WriteAllBytes(assemblyPath, fixture.Image);
    File.WriteAllText(
        manifestPath,
        ManifestJson(fixture),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    FixtureVerifier.VerifyImage(fixture.Image);
    Console.WriteLine($"Generated {assemblyPath}");
    Console.WriteLine($"Generated {manifestPath}");
    return 0;
}

static int VerifyPackage(string packagePath)
{
    Fixture expected = BuildDeterministically();
    using ZipArchive package = ZipFile.OpenRead(packagePath);
    ZipArchiveEntry assembly = package.GetEntry(AssemblyPath)
        ?? throw new InvalidDataException(
            $"{AssemblyPath} is missing from {packagePath}.");
    ZipArchiveEntry manifest = package.GetEntry(ManifestPath)
        ?? throw new InvalidDataException(
            $"{ManifestPath} is missing from {packagePath}.");
    ZipArchiveEntry nuspec = package.Entries.Single(
        entry => entry.FullName.EndsWith(
            ".nuspec",
            StringComparison.OrdinalIgnoreCase));

    using var image = new MemoryStream();
    using (Stream source = assembly.Open())
    {
        source.CopyTo(image);
    }
    byte[] packagedImage = image.ToArray();
    if (!packagedImage.AsSpan().SequenceEqual(expected.Image))
    {
        throw new InvalidDataException(
            "The packaged metadata fixture differs from deterministic generator output.");
    }
    FixtureVerifier.VerifyImage(packagedImage);

    using var manifestBytes = new MemoryStream();
    using (Stream source = manifest.Open())
    {
        source.CopyTo(manifestBytes);
    }
    byte[] expectedManifest = Encoding.UTF8.GetBytes(ManifestJson(expected));
    if (!manifestBytes.ToArray().AsSpan().SequenceEqual(expectedManifest))
    {
        throw new InvalidDataException(
            "The packaged metadata fixture manifest differs from generator output.");
    }

    using Stream nuspecStream = nuspec.Open();
    XDocument nuspecDocument = XDocument.Load(nuspecStream);
    string? packageVersion = nuspecDocument
        .Descendants()
        .SingleOrDefault(element => element.Name.LocalName == "version")
        ?.Value;
    if (!string.Equals(packageVersion, FixtureVersion, StringComparison.Ordinal))
    {
        throw new InvalidDataException(
            "The package version does not match the version-owned fixture generator.");
    }

    Console.WriteLine(
        $"Verified {packagePath}: {expected.Specimens.Count} metadata confusion specimens.");
    return 0;
}

static Fixture BuildDeterministically()
{
    Fixture first = FixtureBuilder.Build();
    Fixture second = FixtureBuilder.Build();
    if (!first.Image.AsSpan().SequenceEqual(second.Image))
    {
        throw new InvalidDataException(
            "The metadata fixture generator produced different bytes for identical inputs.");
    }
    return first;
}

static string ManifestJson(Fixture fixture) =>
    JsonSerializer.Serialize(
        new FixtureManifest(
            1,
            FixtureVersion,
            AssemblyPath,
            fixture.Specimens),
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            NewLine = "\n",
        }) + "\n";

static int Usage()
{
    Console.Error.WriteLine(
        "Usage: MetadataConfusionGenerator generate <assembly-path> <manifest-path>");
    Console.Error.WriteLine(
        "   or: MetadataConfusionGenerator verify-package <nupkg-path>");
    return 2;
}

sealed record Fixture(byte[] Image, IReadOnlyList<Specimen> Specimens);

sealed record FixtureManifest(
    int SchemaVersion,
    string PackageVersion,
    string AssemblyPath,
    IReadOnlyList<Specimen> Specimens);

sealed record Specimen(
    string Id,
    string Location,
    int Token,
    string Raw,
    IReadOnlyList<string> Concerns);

static class FixtureBuilder
{
    internal const int ExpectedSpecimenCount = 18;

    internal const string AssemblyName =
        "DotnetInspect.Metadata\u202Eeman\u202C";
    internal const string ModuleName =
        "DotnetInspect.Metadata\u202Eeludom\u202C.dll";
    internal const string NamespaceName = "Dotnet\u200BInspect.Metadata";
    internal const string TypeName = "Route\u202EepyT\u202C";
    internal const string MethodName = "Inspect\u2028Injected";
    internal const string ParameterName = "input\rforged";
    internal const string FieldName = "Marker\u001B[2J";
    internal const string UserString =
        "literal\u001B]52;c;RG90bmV0LWluc3BlY3QgbWV0YWRhdGEgZml4dHVyZS4K\u0007";
    internal const string ModuleReferenceName =
        "native/../\u202Eld.daolyap\u202C";
    internal const string ImportName = "open\rforged";
    internal const string ResourceName = "assets/../\u202Egpj.wen\u202C";
    internal const string HomoglyphAssemblyReference = "\u0405ystem.Runtime";
    internal const string LiteralNestedName = "Outer+Inner";
    internal const string GenericArityName = "Arity`2";
    internal const string GenericParameterName = "T\u2060Value";
    internal const string Description =
        "Metadata line one\nMetadata line two\u001B]0;DotnetInspect\u0007";
    internal const string RepositoryUrl =
        "https://api.\u202Etentod\u202C.com/v3/index.json";
    internal const string InformationalVersion = "1.0.0+build\rverified";

    internal static Fixture Build()
    {
        var metadata = new MetadataBuilder();
        var specimens = new List<Specimen>(ExpectedSpecimenCount);
        StringHandle S(string value) => metadata.GetOrAddString(value);

        ModuleDefinitionHandle module = metadata.AddModule(
            0,
            S(ModuleName),
            metadata.GetOrAddGuid(
                Guid.Parse("444f544e-4554-494e-5350-4543544d4554")),
            default,
            default);
        Add(
            specimens,
            "module-bidi",
            "Module.Name",
            module,
            ModuleName,
            "format/bidi");

        AssemblyDefinitionHandle assembly = metadata.AddAssembly(
            name: S(AssemblyName),
            version: new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: AssemblyHashAlgorithm.None);
        Add(
            specimens,
            "assembly-name-bidi",
            "Assembly.Name",
            assembly,
            AssemblyName,
            "format/bidi",
            "identity");

        AssemblyReferenceHandle systemRuntime = metadata.AddAssemblyReference(
            S("System.Runtime"),
            new Version(11, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        AssemblyReferenceHandle homoglyphReference =
            metadata.AddAssemblyReference(
                S(HomoglyphAssemblyReference),
                new Version(11, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        Add(
            specimens,
            "assembly-reference-homoglyph",
            "AssemblyRef.Name",
            homoglyphReference,
            HomoglyphAssemblyReference,
            "identifier/homoglyph");

        TypeReferenceHandle objectType = metadata.AddTypeReference(
            systemRuntime,
            S("System"),
            S("Object"));
        TypeReferenceHandle descriptionAttribute = metadata.AddTypeReference(
            systemRuntime,
            S("System.Reflection"),
            S("AssemblyDescriptionAttribute"));
        TypeReferenceHandle metadataAttribute = metadata.AddTypeReference(
            systemRuntime,
            S("System.Reflection"),
            S("AssemblyMetadataAttribute"));
        TypeReferenceHandle informationalVersionAttribute =
            metadata.AddTypeReference(
                systemRuntime,
                S("System.Reflection"),
                S("AssemblyInformationalVersionAttribute"));

        MemberReferenceHandle descriptionConstructor =
            AddStringAttributeConstructor(
                metadata,
                descriptionAttribute,
                parameterCount: 1);
        MemberReferenceHandle metadataConstructor =
            AddStringAttributeConstructor(
                metadata,
                metadataAttribute,
                parameterCount: 2);
        MemberReferenceHandle informationalVersionConstructor =
            AddStringAttributeConstructor(
                metadata,
                informationalVersionAttribute,
                parameterCount: 1);

        CustomAttributeHandle description = metadata.AddCustomAttribute(
            assembly,
            descriptionConstructor,
            AttributeValue(metadata, Description));
        Add(
            specimens,
            "assembly-description-controls",
            "AssemblyDescriptionAttribute.Value",
            description,
            Description,
            "control",
            "line-injection",
            "terminal");

        CustomAttributeHandle repository = metadata.AddCustomAttribute(
            assembly,
            metadataConstructor,
            AttributeValue(metadata, "RepositoryUrl", RepositoryUrl));
        Add(
            specimens,
            "repository-url-bidi",
            "AssemblyMetadataAttribute.RepositoryUrl",
            repository,
            RepositoryUrl,
            "format/bidi",
            "provenance-claim");

        CustomAttributeHandle informationalVersion =
            metadata.AddCustomAttribute(
                assembly,
                informationalVersionConstructor,
                AttributeValue(metadata, InformationalVersion));
        Add(
            specimens,
            "informational-version-carriage-return",
            "AssemblyInformationalVersionAttribute.Value",
            informationalVersion,
            InformationalVersion,
            "control",
            "line-injection",
            "provenance-claim");

        ParameterHandle parameter = metadata.AddParameter(
            ParameterAttributes.None,
            S(ParameterName),
            sequenceNumber: 1);
        Add(
            specimens,
            "parameter-carriage-return",
            "Param.Name",
            parameter,
            ParameterName,
            "control",
            "line-injection");

        FieldDefinitionHandle field = metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static,
            S(FieldName),
            metadata.GetOrAddBlob(new byte[] { 0x06, 0x0E }));
        Add(
            specimens,
            "field-ansi",
            "Field.Name",
            field,
            FieldName,
            "control",
            "terminal");

        var methodBodies = new BlobBuilder();
        int followBody = AddBody(
            methodBodies,
            encoder => encoder.OpCode(ILOpCode.Ret));
        MethodDefinitionHandle follow = metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            S(MethodName),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x01, 0x01, 0x0E }),
            followBody,
            parameter);
        Add(
            specimens,
            "method-line-separator",
            "MethodDef.Name",
            follow,
            MethodName,
            "line-separator",
            "display-grammar");

        UserStringHandle userString = metadata.GetOrAddUserString(UserString);
        int literalBody = AddBody(
            methodBodies,
            encoder =>
            {
                encoder.LoadString(userString);
                encoder.OpCode(ILOpCode.Pop);
                encoder.OpCode(ILOpCode.Ret);
            });
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            S("ReadMarker"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            literalBody,
            MetadataTokens.ParameterHandle(2));
        specimens.Add(
            new Specimen(
                "user-string-osc52",
                "UserString",
                0x70000000 | MetadataTokens.GetHeapOffset(userString),
                UserString,
                ["control", "terminal", "clipboard"]));

        ModuleReferenceHandle moduleReference =
            metadata.AddModuleReference(S(ModuleReferenceName));
        Add(
            specimens,
            "module-reference-path-bidi",
            "ModuleRef.Name",
            moduleReference,
            ModuleReferenceName,
            "path/parent-segment",
            "format/bidi");

        MethodDefinitionHandle imported = metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.HideBySig
                | MethodAttributes.PinvokeImpl,
            MethodImplAttributes.PreserveSig,
            S("NativeOpen"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(2));
        metadata.AddMethodImport(
            imported,
            MethodImportAttributes.CallingConventionWinApi,
            S(ImportName),
            moduleReference);
        Add(
            specimens,
            "pinvoke-import-carriage-return",
            "MethodDef.PInvokeImportName",
            imported,
            ImportName,
            "control",
            "line-injection");

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            S("<Module>"),
            default,
            field,
            follow);
        TypeDefinitionHandle hostileType = metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            S(NamespaceName),
            S(TypeName),
            objectType,
            field,
            follow);
        Add(
            specimens,
            "namespace-zero-width",
            "TypeDef.Namespace",
            hostileType,
            NamespaceName,
            "format/invisible",
            "identity");
        Add(
            specimens,
            "type-name-bidi",
            "TypeDef.Name",
            hostileType,
            TypeName,
            "format/bidi",
            "identity");

        FieldDefinitionHandle endField =
            MetadataTokens.FieldDefinitionHandle(2);
        MethodDefinitionHandle endMethod =
            MetadataTokens.MethodDefinitionHandle(4);
        TypeDefinitionHandle literalNested = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            S(LiteralNestedName),
            objectType,
            endField,
            endMethod);
        Add(
            specimens,
            "literal-nested-separator",
            "TypeDef.Name",
            literalNested,
            LiteralNestedName,
            "display-grammar",
            "identity");

        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            S("Outer"),
            objectType,
            endField,
            endMethod);
        TypeDefinitionHandle inner = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic,
            default,
            S("Inner"),
            objectType,
            endField,
            endMethod);
        metadata.AddNestedType(inner, outer);

        TypeDefinitionHandle genericArity = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            S(GenericArityName),
            objectType,
            endField,
            endMethod);
        Add(
            specimens,
            "generic-arity-mismatch",
            "TypeDef.Name",
            genericArity,
            GenericArityName,
            "display-grammar",
            "identity");
        GenericParameterHandle genericParameter =
            metadata.AddGenericParameter(
                genericArity,
                GenericParameterAttributes.None,
                S(GenericParameterName),
                index: 0);
        Add(
            specimens,
            "generic-parameter-word-joiner",
            "GenericParam.Name",
            genericParameter,
            GenericParameterName,
            "format/invisible",
            "identity");

        var resourceData = new BlobBuilder();
        byte[] resourceContent =
            Encoding.UTF8.GetBytes("dotnet-inspect metadata fixture");
        resourceData.WriteInt32(resourceContent.Length);
        resourceData.WriteBytes(resourceContent);
        ManifestResourceHandle resource = metadata.AddManifestResource(
            ManifestResourceAttributes.Public,
            S(ResourceName),
            implementation: default,
            offset: 0);
        Add(
            specimens,
            "resource-path-bidi",
            "ManifestResource.Name",
            resource,
            ResourceName,
            "path/parent-segment",
            "format/bidi");

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            methodBodies,
            managedResources: resourceData,
            flags: CorFlags.ILOnly,
            deterministicIdProvider: DeterministicId);
        var image = new BlobBuilder();
        pe.Serialize(image);

        if (specimens.Count != ExpectedSpecimenCount)
        {
            throw new InvalidOperationException(
                $"Expected {ExpectedSpecimenCount} specimens, generated {specimens.Count}.");
        }
        return new Fixture(image.ToArray(), specimens);
    }

    static MemberReferenceHandle AddStringAttributeConstructor(
        MetadataBuilder metadata,
        TypeReferenceHandle attributeType,
        int parameterCount)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        for (int i = 0; i < parameterCount; i++)
        {
            signature.WriteByte(0x0E);
        }
        return metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(signature));
    }

    static BlobHandle AttributeValue(
        MetadataBuilder metadata,
        params string[] values)
    {
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        foreach (string item in values)
        {
            value.WriteSerializedString(item);
        }
        value.WriteUInt16(0);
        return metadata.GetOrAddBlob(value);
    }

    static int AddBody(
        BlobBuilder methodBodies,
        Action<InstructionEncoder> write)
    {
        methodBodies.Align(4);
        var il = new BlobBuilder();
        var encoder =
            new InstructionEncoder(il, new ControlFlowBuilder());
        write(encoder);
        return new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(encoder);
    }

    static BlobContentId DeterministicId(IEnumerable<Blob> blobs)
    {
        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (Blob blob in blobs)
        {
            hash.AppendData(blob.GetBytes().AsSpan());
        }
        return BlobContentId.FromHash(hash.GetHashAndReset());
    }

    static void Add(
        ICollection<Specimen> specimens,
        string id,
        string location,
        EntityHandle handle,
        string raw,
        params string[] concerns) =>
        specimens.Add(
            new Specimen(
                id,
                location,
                MetadataTokens.GetToken(handle),
                raw,
                concerns));
}

static class FixtureVerifier
{
    internal static void VerifyImage(byte[] image)
    {
        using var pe = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();

        Require(
            reader.GetString(reader.GetModuleDefinition().Name),
            FixtureBuilder.ModuleName,
            "module name");
        Require(
            reader.GetString(reader.GetAssemblyDefinition().Name),
            FixtureBuilder.AssemblyName,
            "assembly name");
        Require(
            reader.AssemblyReferences
                .Select(handle =>
                    reader.GetString(
                        reader.GetAssemblyReference(handle).Name))
                .Contains(
                    FixtureBuilder.HomoglyphAssemblyReference,
                    StringComparer.Ordinal),
            "homoglyph assembly reference");

        TypeDefinitionHandle hostileType = FindType(
            reader,
            FixtureBuilder.NamespaceName,
            FixtureBuilder.TypeName);
        TypeDefinition hostile = reader.GetTypeDefinition(hostileType);
        Require(
            hostile.GetFields()
                .Select(handle =>
                    reader.GetString(
                        reader.GetFieldDefinition(handle).Name))
                .SequenceEqual(
                    [FixtureBuilder.FieldName],
                    StringComparer.Ordinal),
            "hostile type field ownership");
        string[] hostileMethods =
        [
            .. hostile.GetMethods()
                .Select(handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)),
        ];
        Require(
            hostileMethods.SequenceEqual(
                [FixtureBuilder.MethodName, "ReadMarker", "NativeOpen"],
                StringComparer.Ordinal),
            "hostile type method ownership");

        TypeDefinitionHandle literalNested = FindType(
            reader,
            string.Empty,
            FixtureBuilder.LiteralNestedName);
        Require(
            reader.GetTypeDefinition(literalNested)
                .GetDeclaringType()
                .IsNil,
            "literal nested separator remains top-level");
        TypeDefinitionHandle outer =
            FindType(reader, string.Empty, "Outer");
        TypeDefinitionHandle inner =
            FindType(reader, string.Empty, "Inner");
        Require(
            reader.GetTypeDefinition(inner).GetDeclaringType() == outer,
            "genuine nested type control");

        TypeDefinitionHandle genericArity = FindType(
            reader,
            string.Empty,
            FixtureBuilder.GenericArityName);
        GenericParameterHandle genericParameter =
            reader.GetTypeDefinition(genericArity)
                .GetGenericParameters()
                .Single();
        Require(
            reader.GetString(
                reader.GetGenericParameter(genericParameter).Name),
            FixtureBuilder.GenericParameterName,
            "generic parameter");

        MethodDefinitionHandle follow = hostile.GetMethods().Single(
            handle =>
                reader.GetString(
                    reader.GetMethodDefinition(handle).Name)
                == FixtureBuilder.MethodName);
        ParameterHandle parameter =
            reader.GetMethodDefinition(follow)
                .GetParameters()
                .Single();
        Require(
            reader.GetString(reader.GetParameter(parameter).Name),
            FixtureBuilder.ParameterName,
            "method parameter ownership");

        MethodDefinitionHandle readMarker =
            hostile.GetMethods().Single(
                handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                    == "ReadMarker");
        byte[] il = pe
            .GetMethodBody(
                reader.GetMethodDefinition(readMarker)
                    .RelativeVirtualAddress)
            .GetILBytes()
            ?? throw new InvalidDataException(
                "ReadMarker has no IL body.");
        Require(
            il.Length >= 6 && il[0] == (byte)ILOpCode.Ldstr,
            "ReadMarker ldstr body");
        int userStringToken =
            BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(1, 4));
        Require(
            reader.GetUserString(
                MetadataTokens.UserStringHandle(
                    userStringToken & 0x00FFFFFF)),
            FixtureBuilder.UserString,
            "user string");
        Require(
            Handles(
                    reader.GetTableRowCount(TableIndex.ModuleRef),
                    MetadataTokens.ModuleReferenceHandle)
                .Select(handle =>
                    reader.GetString(
                        reader.GetModuleReference(handle).Name))
                .Contains(
                    FixtureBuilder.ModuleReferenceName,
                    StringComparer.Ordinal),
            "module reference");
        MethodDefinitionHandle nativeOpen =
            hostile.GetMethods().Single(
                handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                    == "NativeOpen");
        MethodImport import =
            reader.GetMethodDefinition(nativeOpen).GetImport();
        Require(
            reader.GetString(import.Name),
            FixtureBuilder.ImportName,
            "P/Invoke import");
        Require(
            reader.ManifestResources
                .Select(handle =>
                    reader.GetString(
                        reader.GetManifestResource(handle).Name))
                .Contains(
                    FixtureBuilder.ResourceName,
                    StringComparer.Ordinal),
            "manifest resource");

        DecodedAttribute[] attributes =
        [
            .. reader.GetAssemblyDefinition()
                .GetCustomAttributes()
                .Select(handle => ReadAttribute(reader, handle)),
        ];
        Require(
            attributes.Length == 3,
            "assembly custom attribute count");
        RequireAttribute(
            attributes,
            "System.Reflection.AssemblyDescriptionAttribute",
            [FixtureBuilder.Description]);
        RequireAttribute(
            attributes,
            "System.Reflection.AssemblyMetadataAttribute",
            ["RepositoryUrl", FixtureBuilder.RepositoryUrl]);
        RequireAttribute(
            attributes,
            "System.Reflection.AssemblyInformationalVersionAttribute",
            [FixtureBuilder.InformationalVersion]);
    }

    static TypeDefinitionHandle FindType(
        MetadataReader reader,
        string @namespace,
        string name) =>
        reader.TypeDefinitions.Single(
            handle =>
            {
                TypeDefinition type =
                    reader.GetTypeDefinition(handle);
                return reader.GetString(type.Namespace) == @namespace
                    && reader.GetString(type.Name) == name;
            });

    static DecodedAttribute ReadAttribute(
        MetadataReader reader,
        CustomAttributeHandle handle)
    {
        CustomAttribute attribute =
            reader.GetCustomAttribute(handle);
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
        {
            throw new InvalidDataException(
                "The fixture attributes must use MemberRef constructors.");
        }
        MemberReference constructor = reader.GetMemberReference(
            (MemberReferenceHandle)attribute.Constructor);
        if (constructor.Parent.Kind != HandleKind.TypeReference)
        {
            throw new InvalidDataException(
                "The fixture attribute constructors must belong to TypeRefs.");
        }
        TypeReference type = reader.GetTypeReference(
            (TypeReferenceHandle)constructor.Parent);
        string typeName =
            $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
        byte[] signature =
            reader.GetBlobBytes(constructor.Signature);

        BlobReader value = reader.GetBlobReader(attribute.Value);
        if (value.ReadUInt16() != 1)
        {
            throw new InvalidDataException(
                "Invalid custom attribute prolog.");
        }

        var values = new List<string>();
        while (value.RemainingBytes > 2)
        {
            values.Add(
                value.ReadSerializedString()
                ?? throw new InvalidDataException(
                    "Unexpected null custom attribute string."));
        }
        if (value.ReadUInt16() != 0)
        {
            throw new InvalidDataException(
                "The fixture does not use named custom arguments.");
        }
        if (value.RemainingBytes != 0)
        {
            throw new InvalidDataException(
                "Trailing bytes in custom attribute value.");
        }
        return new DecodedAttribute(typeName, signature, values);
    }

    static void RequireAttribute(
        IReadOnlyList<DecodedAttribute> attributes,
        string typeName,
        string[] expectedValues)
    {
        DecodedAttribute attribute =
            attributes.Single(value => value.TypeName == typeName);
        byte[] expectedSignature =
        [
            0x20,
            checked((byte)expectedValues.Length),
            0x01,
            .. Enumerable.Repeat(
                (byte)0x0E,
                expectedValues.Length),
        ];
        Require(
            attribute.Signature
                .AsSpan()
                .SequenceEqual(expectedSignature),
            $"{typeName} constructor signature");
        Require(
            attribute.Values.SequenceEqual(
                expectedValues,
                StringComparer.Ordinal),
            $"{typeName} values");
    }

    static IEnumerable<THandle> Handles<THandle>(
        int count,
        Func<int, THandle> create)
    {
        for (int row = 1; row <= count; row++)
        {
            yield return create(row);
        }
    }

    static void Require(
        string actual,
        string expected,
        string description)
    {
        if (!string.Equals(
            actual,
            expected,
            StringComparison.Ordinal))
        {
            int index = 0;
            while (index < actual.Length
                && index < expected.Length
                && actual[index] == expected[index])
            {
                index++;
            }
            string actualCodeUnit = index < actual.Length
                ? $"U+{(int)actual[index]:X4}"
                : "<end>";
            string expectedCodeUnit = index < expected.Length
                ? $"U+{(int)expected[index]:X4}"
                : "<end>";
            throw new InvalidDataException(
                $"Unexpected {description} at UTF-16 index {index}: "
                    + $"actual {actualCodeUnit}, expected {expectedCodeUnit}.");
        }
    }

    static void Require(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidDataException(
                $"Missing {description}.");
        }
    }

    sealed record DecodedAttribute(
        string TypeName,
        byte[] Signature,
        IReadOnlyList<string> Values);
}
