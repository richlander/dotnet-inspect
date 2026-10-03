using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Builds a small assembly for method-row gate fixtures: types, methods with
/// hand-written signature blobs and no bodies, custom attributes, TypeRefs,
/// and TypeSpecs, so a test controls every structure the gate walks.
/// </summary>
internal sealed class GateFixtureImage
{
    readonly MetadataBuilder _metadata = new();
    readonly List<FixtureType> _types = [];
    readonly AssemblyReferenceHandle _dependency;
    readonly ModuleReferenceHandle _native;
    readonly List<string> _namespacePatches = [];

    public GateFixtureImage()
    {
        _metadata.AddModule(
            0,
            _metadata.GetOrAddString("GateFixture.dll"),
            _metadata.GetOrAddGuid(new Guid("0b5f1f3c-6a0e-4ab2-9c0f-4d1c2c1b7a11")),
            default,
            default);
        _metadata.AddAssembly(
            _metadata.GetOrAddString("GateFixture"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        _dependency = _metadata.AddAssemblyReference(
            _metadata.GetOrAddString("Dependency"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        _native = _metadata.AddModuleReference(_metadata.GetOrAddString("native.dll"));
        Type("", "<Module>");
    }

    public MetadataBuilder Metadata => _metadata;

    /// <summary>The types added so far, in order.</summary>
    public IReadOnlyList<FixtureType> Types => _types;

    /// <summary>A TypeRef in the dependency assembly, or nested in <paramref name="enclosing"/>.</summary>
    public TypeReferenceHandle TypeRef(string ns, string name, TypeReferenceHandle? enclosing = null) =>
        _metadata.AddTypeReference(
            enclosing is { } outer ? outer : _dependency,
            _metadata.GetOrAddString(ns),
            _metadata.GetOrAddString(name));

    public TypeSpecificationHandle TypeSpec(BlobBuilder signature) =>
        _metadata.AddTypeSpecification(_metadata.GetOrAddBlob(signature));

    /// <summary>An attribute constructor on <paramref name="parent"/> taking <paramref name="parameters"/>.</summary>
    public MemberReferenceHandle AttributeConstructor(EntityHandle parent, params Action<SignatureTypeEncoder>[] parameters)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                parameters.Length,
                r => r.Void(),
                p =>
                {
                    foreach (Action<SignatureTypeEncoder> parameter in parameters)
                        parameter(p.AddParameter().Type());
                });
        return _metadata.AddMemberReference(
            parent,
            _metadata.GetOrAddString(".ctor"),
            _metadata.GetOrAddBlob(signature));
    }

    /// <summary>Starts a type; methods added next belong to it.</summary>
    public FixtureType Type(
        string ns,
        string name,
        FixtureType? enclosing = null,
        StringHandle? namespaceOverride = null)
    {
        var type = new FixtureType(ns, name, enclosing) { NamespaceOverride = namespaceOverride };
        if (namespaceOverride is not null)
            _namespacePatches.Add(name);
        _types.Add(type);
        return type;
    }

    public ImmutableArray<byte> Build(Machine machine = Machine.Unknown)
    {
        int nextMethod = 1;
        int nextParameter = 1;
        var typeHandles = new Dictionary<FixtureType, TypeDefinitionHandle>();
        foreach (FixtureType type in _types)
        {
            TypeDefinitionHandle handle = _metadata.AddTypeDefinition(
                type.Name == "<Module>"
                    ? default
                    : type.Shape
                    ?? (type.Enclosing is null
                        ? TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed
                        : TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed),
                _metadata.GetOrAddString(type.Namespace),
                _metadata.GetOrAddString(type.Name),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(nextMethod));
            typeHandles[type] = handle;
            foreach (EntityHandle constructor in type.AttributeConstructors)
                _metadata.AddCustomAttribute(handle, constructor, default);
            foreach ((EntityHandle constructor, BlobBuilder value) in type.ValuedAttributes)
                _metadata.AddCustomAttribute(handle, constructor, _metadata.GetOrAddBlob(value));
            if (type.Enclosing is { } enclosing)
                _metadata.AddNestedType(handle, typeHandles[enclosing]);

            foreach (FixtureMethod method in type.Methods)
            {
                MethodDefinitionHandle methodHandle = _metadata.AddMethodDefinition(
                    method.Attributes,
                    method.ImplAttributes,
                    _metadata.GetOrAddString(method.Name),
                    _metadata.GetOrAddBlob(method.Signature),
                    -1,
                    MetadataTokens.ParameterHandle(nextParameter));
                nextMethod++;
                for (int p = 0; p < method.ParameterNames.Length; p++)
                {
                    _metadata.AddParameter(
                        ParameterAttributes.None,
                        _metadata.GetOrAddString(method.ParameterNames[p]),
                        p + 1);
                    nextParameter++;
                }
                if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                {
                    _metadata.AddMethodImport(
                        methodHandle,
                        MethodImportAttributes.None,
                        _metadata.GetOrAddString(method.Name),
                        method.ModuleName is { } module
                            ? _metadata.AddModuleReference(_metadata.GetOrAddString(module))
                            : _native);
                }

                foreach (EntityHandle constructor in method.AttributeConstructors)
                    _metadata.AddCustomAttribute(methodHandle, constructor, default);
                foreach ((EntityHandle constructor, BlobBuilder value) in method.ValuedAttributes)
                    _metadata.AddCustomAttribute(methodHandle, constructor, _metadata.GetOrAddBlob(value));
            }
        }

        var root = new MetadataRootBuilder(_metadata);
        var pe = new ManagedPEBuilder(
            machine == Machine.Unknown
                ? PEHeaderBuilder.CreateLibraryHeader()
                : new PEHeaderBuilder(
                    machine: machine,
                    imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
            root,
            new BlobBuilder());
        var output = new BlobBuilder();
        pe.Serialize(output);
        byte[] bytes = output.ToArray();
        if (_namespacePatches.Count > 0)
            PatchNamespaces(bytes);
        return [.. bytes];
    }

    /// <summary>
    /// Rewrites the metadata root's version string to <paramref name="length"/>
    /// characters. MetadataRootBuilder caps the version at 254 bytes, so the
    /// root is patched: the version grows in place, every stream offset and the
    /// CLI metadata directory grow with it, and the .text section is extended.
    /// Only valid for an image whose metadata ends its only section's data.
    /// </summary>
    public static ImmutableArray<byte> WithMetadataVersion(ImmutableArray<byte> image, int length)
    {
        byte[] bytes = image.ToArray();
        int metadataStart;
        int metadataDirectoryOffset;
        int textHeaderOffset;
        using (var peReader = new PEReader(ImmutableArray.Create(bytes)))
        {
            PEHeaders headers = peReader.PEHeaders;
            metadataStart = headers.MetadataStartOffset;
            Require(headers.SectionHeaders.Length == 1, "The fixture must have one section.");
            // CorHeader: cb (4), runtime versions (4), MetaData directory (8).
            metadataDirectoryOffset = headers.CorHeaderStartOffset + 8;
            textHeaderOffset = headers.PEHeaderStartOffset + headers.PEHeader!.SizeOfHeaders is var _ ? SectionTableOffset(headers) : 0;
        }

        int oldLength = BitConverter.ToInt32(bytes, metadataStart + 12);
        int newLength = (length + 1 + 3) & ~3;
        int delta = newLength - oldLength;
        int versionStart = metadataStart + 16;
        var patched = new byte[bytes.Length + delta];
        Array.Copy(bytes, 0, patched, 0, versionStart);
        for (int i = 0; i < length; i++)
            patched[versionStart + i] = (byte)'v';
        Array.Copy(bytes, versionStart + oldLength, patched, versionStart + newLength, bytes.Length - versionStart - oldLength);
        BitConverter.GetBytes(newLength).CopyTo(patched, metadataStart + 12);

        // Stream headers follow flags (2) and the stream count (2); offsets
        // are relative to the metadata root, so each shifts by delta.
        int cursor = versionStart + newLength + 2;
        int streams = BitConverter.ToUInt16(patched, cursor);
        cursor += 2;
        for (int i = 0; i < streams; i++)
        {
            int offset = BitConverter.ToInt32(patched, cursor);
            BitConverter.GetBytes(offset + delta).CopyTo(patched, cursor);
            cursor += 8;
            while (patched[cursor] != 0)
                cursor++;
            cursor = (cursor + 4) & ~3;
        }

        int metadataSize = BitConverter.ToInt32(patched, metadataDirectoryOffset + 4);
        BitConverter.GetBytes(metadataSize + delta).CopyTo(patched, metadataDirectoryOffset + 4);

        // The section header: VirtualSize (+8) and SizeOfRawData (+16).
        int virtualSize = BitConverter.ToInt32(patched, textHeaderOffset + 8);
        BitConverter.GetBytes(virtualSize + delta).CopyTo(patched, textHeaderOffset + 8);
        int rawSize = BitConverter.ToInt32(patched, textHeaderOffset + 16);
        BitConverter.GetBytes(rawSize + delta).CopyTo(patched, textHeaderOffset + 16);
        return [.. patched];
    }

    static int SectionTableOffset(PEHeaders headers) =>
        headers.PEHeaderStartOffset
        + (headers.PEHeader!.Magic == PEMagic.PE32Plus ? 240 : 224);

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Points each patched type's Namespace column past the #Strings heap:
    /// MetadataBuilder cannot emit an invalid string handle, so the column is
    /// rewritten in the serialized image.
    /// </summary>
    void PatchNamespaces(byte[] bytes)
    {
        using var peReader = new PEReader(ImmutableArray.Create(bytes));
        MetadataReader reader = peReader.GetMetadataReader();
        int start = peReader.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(TableIndex.TypeDef);
        int rowSize = reader.GetTableRowSize(TableIndex.TypeDef);
        int stringIndexSize = reader.GetHeapSize(HeapIndex.String) > ushort.MaxValue ? 4 : 2;
        int row = 0;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            if (_namespacePatches.Contains(reader.GetString(reader.GetTypeDefinition(handle).Name)))
            {
                int column = start + row * rowSize + 4 + stringIndexSize;
                bytes[column] = 0xFF;
                bytes[column + 1] = 0xFF;
                if (stringIndexSize == 4)
                {
                    bytes[column + 2] = 0;
                    bytes[column + 3] = 0;
                }
            }

            row++;
        }
    }

    public static BlobBuilder StaticSignature(Action<SignatureTypeEncoder> returnType, params Action<SignatureTypeEncoder>[] parameters)
    {
        var blob = new BlobBuilder();
        new BlobEncoder(blob)
            .MethodSignature()
            .Parameters(
                parameters.Length,
                r => returnType(r.Type()),
                p =>
                {
                    foreach (Action<SignatureTypeEncoder> parameter in parameters)
                        parameter(p.AddParameter().Type());
                });
        return blob;
    }

    public static BlobBuilder VoidSignature(params Action<SignatureTypeEncoder>[] parameters)
    {
        var blob = new BlobBuilder();
        new BlobEncoder(blob)
            .MethodSignature()
            .Parameters(
                parameters.Length,
                r => r.Void(),
                p =>
                {
                    foreach (Action<SignatureTypeEncoder> parameter in parameters)
                        parameter(p.AddParameter().Type());
                });
        return blob;
    }

    /// <summary>A custom attribute value with one <c>int32</c> fixed argument and no named arguments.</summary>
    public static BlobBuilder AttributeValue(int fixedArgument)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteInt32(fixedArgument);
        blob.WriteUInt16(0);
        return blob;
    }

    /// <summary>A custom attribute value with one string fixed argument and no named arguments.</summary>
    public static BlobBuilder AttributeValue(string fixedArgument)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteSerializedString(fixedArgument);
        blob.WriteUInt16(0);
        return blob;
    }

    internal sealed class FixtureType(string ns, string name, FixtureType? enclosing)
    {
        /// <summary>Overrides the default static (public sealed abstract) type shape.</summary>
        public TypeAttributes? Shape { get; set; }
        public List<(EntityHandle Constructor, BlobBuilder Value)> ValuedAttributes { get; } = [];

        public FixtureType WithShape(TypeAttributes shape)
        {
            Shape = shape;
            return this;
        }

        /// <summary>Attaches a custom attribute with a value to the type itself.</summary>
        public FixtureType Attribute(EntityHandle constructor, BlobBuilder value)
        {
            ValuedAttributes.Add((constructor, value));
            return this;
        }

        /// <summary>Attaches a custom attribute with a value to the most recently added method.</summary>
        public FixtureType MethodAttribute(EntityHandle constructor, BlobBuilder value)
        {
            Methods[^1].ValuedAttributes.Add((constructor, value));
            return this;
        }

        public string Namespace { get; } = ns;
        public string Name { get; } = name;
        public FixtureType? Enclosing { get; } = enclosing;
        public StringHandle? NamespaceOverride { get; init; }
        public List<FixtureMethod> Methods { get; } = [];
        public List<EntityHandle> AttributeConstructors { get; } = [];

        /// <summary>Attaches custom attributes to the type itself.</summary>
        public FixtureType Attributes(params EntityHandle[] attributeConstructors)
        {
            AttributeConstructors.AddRange(attributeConstructors);
            return this;
        }

        public FixtureType Method(
            string name,
            BlobBuilder? signature = null,
            MethodAttributes attributes = MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes implAttributes = MethodImplAttributes.IL,
            string? moduleName = null,
            string[]? parameterNames = null,
            params EntityHandle[] attributeConstructors)
        {
            Methods.Add(new FixtureMethod(
                name,
                signature ?? VoidSignature(),
                attributes,
                implAttributes,
                attributeConstructors,
                moduleName,
                parameterNames ?? []));
            return this;
        }
    }

    internal sealed record FixtureMethod(
        string Name,
        BlobBuilder Signature,
        MethodAttributes Attributes,
        MethodImplAttributes ImplAttributes,
        EntityHandle[] AttributeConstructors,
        string? ModuleName = null,
        string[]? ParameterNames = null)
    {
        public string[] ParameterNames { get; init; } = ParameterNames ?? [];
        public List<(EntityHandle Constructor, BlobBuilder Value)> ValuedAttributes { get; } = [];
    }
}
