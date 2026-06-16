using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Owner of the PE and metadata readers for one assembly — the explicit
/// lifetime the old pipeline hid (docs/decompiler-ir.md). Everything that
/// resolves tokens borrows from a live source; results that escape its
/// scope must be fully materialized (resolved <see cref="TypeRef"/>s,
/// strings, byte arrays) and never hold metadata handles. The importer's
/// outputs honor that rule by construction.
/// </summary>
public sealed class MetadataSource : IDisposable
{
    readonly FileStream _stream;

    MetadataSource(string path, FileStream stream, PEReader peReader, MetadataReader reader, string assemblyName)
    {
        Path = path;
        _stream = stream;
        Pe = peReader;
        Reader = reader;
        AssemblyName = assemblyName;
    }

    public string Path { get; }

    /// <summary>Simple assembly name (no version/culture).</summary>
    public string AssemblyName { get; }

    internal PEReader Pe { get; }

    internal MetadataReader Reader { get; }

    /// <summary>Opens an assembly. Throws <see cref="BadImageFormatException"/> for files without managed metadata.</summary>
    public static MetadataSource Open(string path)
    {
        var stream = File.OpenRead(path);
        PEReader? peReader = null;
        try
        {
            peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                throw new BadImageFormatException($"No managed metadata: {path}");
            var reader = peReader.GetMetadataReader();
            string assemblyName = reader.IsAssembly
                ? reader.GetString(reader.GetAssemblyDefinition().Name)
                : System.IO.Path.GetFileNameWithoutExtension(path);
            return new MetadataSource(path, stream, peReader, reader, assemblyName);
        }
        catch
        {
            peReader?.Dispose();
            stream.Dispose();
            throw;
        }
    }

    Dictionary<TypeRef, TypeShape>? _shapes;
    Dictionary<TypeRef, IReadOnlyDictionary<long, string>>? _enumMembers;
    Dictionary<TypeRef, TypeRef?>? _baseTypes;

    /// <summary>
    /// The C# shape of a type defined in THIS assembly — enum, struct, or
    /// reference — read from its base type. Cross-assembly types and non
    /// -definitions return <see cref="TypeShape.Unknown"/>: resolving them
    /// would need an assembly loader the SRM-only pipeline deliberately does
    /// not carry. The same-assembly map covers the whole single-assembly
    /// sweep (every CoreLib type resolves against CoreLib). Built once, lazily.
    /// </summary>
    internal TypeShape ResolveShape(TypeRef type)
    {
        if (type.Kind != TypeRefKind.Definition)
            return TypeShape.Unknown;
        EnsureTypeMaps();
        return _shapes!.GetValueOrDefault(type, TypeShape.Unknown);
    }

    /// <summary>
    /// The named members of a same-assembly enum, as value → name (every
    /// underlying integer width normalized to <see cref="long"/>). Null for a
    /// non-enum or cross-assembly type. Aliases keep the first declared name.
    /// </summary>
    internal IReadOnlyDictionary<long, string>? ResolveEnumMembers(TypeRef type)
    {
        if (type.Kind != TypeRefKind.Definition)
            return null;
        EnsureTypeMaps();
        return _enumMembers!.GetValueOrDefault(type);
    }

    void EnsureTypeMaps()
    {
        if (_shapes is not null)
            return;
        var shapes = new Dictionary<TypeRef, TypeShape>();
        var enums = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>();
        var bases = new Dictionary<TypeRef, TypeRef?>();
        foreach (var handle in Reader.TypeDefinitions)
        {
            var typeDef = Reader.GetTypeDefinition(handle);
            // The decoder produces the same nested-aware TypeRef the IR
            // carries, so the map keys match by semantic equality.
            var key = TypeRefDecoder.Instance.GetTypeFromDefinition(Reader, handle, 0);
            var shape = ClassifyShape(typeDef);
            shapes[key] = shape;
            bases[key] = DecodeBaseType(typeDef.BaseType);
            if (shape == TypeShape.Enum)
                enums[key] = BuildEnumMembers(typeDef);
        }
        _enumMembers = enums;
        _baseTypes = bases;
        _shapes = shapes;   // assign last: ResolveShape gates on _shapes
    }

    /// <summary>
    /// The base type of a same-assembly definition, decoded to the same
    /// nested-aware <see cref="TypeRef"/> the IR carries. A definition or
    /// reference base resolves; a generic-instance base (TypeSpecification)
    /// returns null — walking it needs a generic context this map does not
    /// hold. Object's nil base also returns null, ending the chain.
    /// </summary>
    TypeRef? DecodeBaseType(EntityHandle baseHandle)
    {
        if (baseHandle.IsNil)
            return null;
        return baseHandle.Kind switch
        {
            HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(Reader, (TypeDefinitionHandle)baseHandle, 0),
            HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(Reader, (TypeReferenceHandle)baseHandle, 0),
            _ => null,
        };
    }

    /// <summary>
    /// The base type of a same-assembly definition, or null for a cross
    /// -assembly type, a non-definition, or a generic-instance base. Built
    /// once with the shape map; the same-assembly chain covers the whole
    /// single-assembly sweep.
    /// </summary>
    internal TypeRef? ResolveBaseType(TypeRef type)
    {
        if (type.Kind != TypeRefKind.Definition)
            return null;
        EnsureTypeMaps();
        return _baseTypes!.GetValueOrDefault(type);
    }

    /// <summary>
    /// True when <paramref name="ancestor"/> is a (strict) base class of
    /// <paramref name="derived"/>, found by walking the same-assembly base
    /// chain. The walk stops at the first base it cannot resolve — a cross
    /// -assembly or generic-instance link — and never guesses past it.
    /// </summary>
    internal bool IsBaseClassOf(TypeRef ancestor, TypeRef derived)
    {
        var current = ResolveBaseType(derived);
        for (int depth = 0; depth < 64 && current is not null; depth++)
        {
            if (current.Equals(ancestor))
                return true;
            current = ResolveBaseType(current);
        }
        return false;
    }

    Dictionary<long, string> BuildEnumMembers(TypeDefinition enumType)
    {
        var members = new Dictionary<long, string>();
        foreach (var fieldHandle in enumType.GetFields())
        {
            var field = Reader.GetFieldDefinition(fieldHandle);
            // The named constants are the literal static fields; the special
            // instance value__ field carries no default value and is skipped.
            if ((field.Attributes & System.Reflection.FieldAttributes.Literal) == 0)
                continue;
            if (ReadConstant(field.GetDefaultValue()) is { } value)
                members.TryAdd(value, Reader.GetString(field.Name));
        }
        return members;
    }

    long? ReadConstant(ConstantHandle handle)
    {
        if (handle.IsNil)
            return null;
        var constant = Reader.GetConstant(handle);
        var blob = Reader.GetBlobReader(constant.Value);
        // The lookup key is the member's ldc.i4 form widened from int, so a
        // 32-bit unsigned value with the high bit set must be keyed by its
        // signed-int reinterpretation (UInt32 0x80000000 -> int -2147483648),
        // or it would never match. 64-bit enums emit ldc.i8 and are not retyped
        // by the int-only constant pass, so their true long value is fine.
        return constant.TypeCode switch
        {
            ConstantTypeCode.SByte => blob.ReadSByte(),
            ConstantTypeCode.Byte => blob.ReadByte(),
            ConstantTypeCode.Int16 => blob.ReadInt16(),
            ConstantTypeCode.UInt16 => blob.ReadUInt16(),
            ConstantTypeCode.Int32 => blob.ReadInt32(),
            ConstantTypeCode.UInt32 => unchecked((int)blob.ReadUInt32()),
            ConstantTypeCode.Int64 => blob.ReadInt64(),
            ConstantTypeCode.UInt64 => unchecked((long)blob.ReadUInt64()),
            ConstantTypeCode.Char => blob.ReadChar(),
            ConstantTypeCode.Boolean => blob.ReadBoolean() ? 1L : 0L,
            _ => null,
        };
    }

    TypeShape ClassifyShape(TypeDefinition typeDef)
    {
        if ((typeDef.Attributes & System.Reflection.TypeAttributes.Interface) != 0)
            return TypeShape.Reference;
        var baseHandle = typeDef.BaseType;
        if (baseHandle.IsNil)
            return TypeShape.Reference;   // System.Object itself
        var (ns, name) = BaseName(baseHandle);
        if (ns == "System" && name == "Enum")
            return TypeShape.Enum;
        if (ns == "System" && name == "ValueType")
            return TypeShape.ValueType;   // a non-enum struct
        return TypeShape.Reference;       // any class base, or a generic (TypeSpec) base
    }

    (string Namespace, string Name) BaseName(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeReference => NameOf(Reader.GetTypeReference((TypeReferenceHandle)handle)),
        HandleKind.TypeDefinition => NameOf(Reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
        _ => ("", ""),   // a TypeSpec base is never System.Enum/ValueType
    };

    (string, string) NameOf(TypeReference reference)
        => (Reader.GetString(reference.Namespace), Reader.GetString(reference.Name));

    (string, string) NameOf(TypeDefinition definition)
        => (Reader.GetString(definition.Namespace), Reader.GetString(definition.Name));

    public void Dispose()
    {
        Pe.Dispose();
        _stream.Dispose();
    }
}
