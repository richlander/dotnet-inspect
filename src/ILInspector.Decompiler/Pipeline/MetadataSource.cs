using System.Collections.Immutable;
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
    HashSet<TypeRef>? _interfaces;
    Dictionary<TypeRef, ImmutableArray<TypeRef>>? _interfaceImpls;

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
        var interfaces = new HashSet<TypeRef>();
        var interfaceImpls = new Dictionary<TypeRef, ImmutableArray<TypeRef>>();
        foreach (var handle in Reader.TypeDefinitions)
        {
            var typeDef = Reader.GetTypeDefinition(handle);
            // The decoder produces the same nested-aware TypeRef the IR
            // carries, so the map keys match by semantic equality.
            var key = TypeRefDecoder.Instance.GetTypeFromDefinition(Reader, handle, 0);
            var shape = ClassifyShape(typeDef);
            shapes[key] = shape;
            // The type's own generic parameters scope the base and interface
            // signatures, so a generic-instance base (List<T>) or interface
            // (IEqualityComparer<T>) decodes to an open TypeRef carrying T as a
            // generic parameter — later substituted by the concrete instance.
            var scope = new GenericScope(GenericParameterNames(typeDef.GetGenericParameters()), []);
            bases[key] = DecodeBaseType(typeDef.BaseType, scope);
            if ((typeDef.Attributes & System.Reflection.TypeAttributes.Interface) != 0)
                interfaces.Add(key);
            interfaceImpls[key] = DecodeInterfaces(typeDef, scope);
            if (shape == TypeShape.Enum)
                enums[key] = BuildEnumMembers(typeDef);
        }
        _enumMembers = enums;
        _baseTypes = bases;
        _interfaces = interfaces;
        _interfaceImpls = interfaceImpls;
        _shapes = shapes;   // assign last: ResolveShape gates on _shapes
    }

    ImmutableArray<string> GenericParameterNames(GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            names.Add(Reader.GetString(Reader.GetGenericParameter(handle).Name));
        return names.MoveToImmutable();
    }

    /// <summary>The interfaces a definition directly implements, decoded with the type's own generic scope (open — concrete instances substitute later).</summary>
    ImmutableArray<TypeRef> DecodeInterfaces(TypeDefinition typeDef, GenericScope scope)
    {
        var impls = typeDef.GetInterfaceImplementations();
        if (impls.Count == 0)
            return [];
        var builder = ImmutableArray.CreateBuilder<TypeRef>(impls.Count);
        foreach (var implHandle in impls)
        {
            var iface = Reader.GetInterfaceImplementation(implHandle).Interface;
            if (DecodeBaseType(iface, scope) is { } decoded)
                builder.Add(decoded);
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// The base type (or an interface) of a same-assembly definition, decoded
    /// to the same nested-aware <see cref="TypeRef"/> the IR carries. A
    /// definition, reference, or generic-instance (TypeSpecification) base
    /// resolves — the spec is decoded under <paramref name="scope"/>, so a base
    /// like <c>Bar&lt;T&gt;</c> keeps the type's own parameter as an open
    /// generic parameter. Object's nil base returns null, ending the chain.
    /// </summary>
    TypeRef? DecodeBaseType(EntityHandle baseHandle, GenericScope scope)
    {
        if (baseHandle.IsNil)
            return null;
        return baseHandle.Kind switch
        {
            HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(Reader, (TypeDefinitionHandle)baseHandle, 0),
            HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(Reader, (TypeReferenceHandle)baseHandle, 0),
            HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(Reader, scope, (TypeSpecificationHandle)baseHandle, 0),
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
        EnsureTypeMaps();
        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                return _baseTypes!.GetValueOrDefault(type);
            case TypeRefKind.GenericInstance when type.ElementType is { } definition:
                // The base of List<int> is the base of List<T> with T := int.
                // The stored base is open (carries List's own parameters), so
                // substitute the instance's arguments to close it.
                return _baseTypes!.GetValueOrDefault(definition)?.Instantiate(type.TypeArguments, []);
            default:
                return null;
        }
    }

    /// <summary>True when <paramref name="type"/> is <c>System.Object</c>.</summary>
    static bool IsObject(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Object" };

    /// <summary>True when <paramref name="type"/> (or the definition it instantiates) is an interface.</summary>
    internal bool IsInterface(TypeRef type)
    {
        EnsureTypeMaps();
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        return definition is not null && _interfaces!.Contains(definition);
    }

    /// <summary>True when <paramref name="type"/> implements <paramref name="iface"/> (matched structurally after substitution, so generic arguments must agree).</summary>
    internal bool Implements(TypeRef type, TypeRef iface)
    {
        foreach (var implemented in InterfacesOf(type))
        {
            if (implemented.Equals(iface))
                return true;
        }
        return false;
    }

    /// <summary>Every interface <paramref name="type"/> implements — its own, its base classes', and those interfaces' bases — fully instantiated.</summary>
    IEnumerable<TypeRef> InterfacesOf(TypeRef type)
    {
        EnsureTypeMaps();
        var seen = new HashSet<TypeRef>();
        var pending = new Stack<TypeRef>();
        for (var current = type; current is not null; current = ResolveBaseType(current))
            pending.Push(current);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var definition = current.Kind == TypeRefKind.GenericInstance ? current.ElementType : current;
            if (definition is null || !_interfaceImpls!.TryGetValue(definition, out var impls))
                continue;
            var arguments = current.Kind == TypeRefKind.GenericInstance ? current.TypeArguments : [];
            foreach (var open in impls)
            {
                var iface = open.Instantiate(arguments, []);
                if (seen.Add(iface))
                {
                    yield return iface;
                    pending.Push(iface);   // an interface's own base interfaces
                }
            }
        }
    }

    /// <summary>
    /// The nearest common supertype of two reference types, both assignable to
    /// it without a cast — an interface one side implements, else the nearest
    /// common base class. Returns <c>object</c> only when both base chains
    /// genuinely resolve to it; null when a chain stops at an unresolvable
    /// (cross-assembly) link before a common ancestor, so the merge never
    /// guesses a supertype the IL did not prove.
    /// </summary>
    internal TypeRef? MergeReferenceTypes(TypeRef a, TypeRef b)
    {
        if (a.Equals(b))
            return a;
        // object is the supertype of every reference type, including interfaces
        // (whose nil base class the base-walk below cannot climb to it).
        if (IsObject(a))
            return a;
        if (IsObject(b))
            return b;
        if (IsInterface(a) && Implements(b, a))
            return a;
        if (IsInterface(b) && Implements(a, b))
            return b;
        var ancestorsA = new HashSet<TypeRef>();
        for (var current = a; current is not null && ancestorsA.Count < 64; current = ResolveBaseType(current))
            ancestorsA.Add(current);
        var fromB = b;
        for (int depth = 0; fromB is not null && depth < 64; depth++, fromB = ResolveBaseType(fromB))
        {
            if (ancestorsA.Contains(fromB))
                return fromB;
        }
        return null;
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
