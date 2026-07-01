using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Owner of the PE and metadata readers for one assembly, with an explicit
/// lifetime contract (docs/decompiler-ir.md). Everything that
/// resolves tokens borrows from a live source; results that escape its
/// scope must be fully materialized (resolved <see cref="TypeRef"/>s,
/// strings, byte arrays) and never hold metadata handles. The importer's
/// outputs honor that rule by construction.
/// </summary>
public sealed class MetadataSource : IDisposable
{
    static readonly TypeRef s_enumerable = TypeRef.CoreLib("System.Collections", "IEnumerable");

    readonly FileStream _stream;
    MetadataReaderProvider? _pdbProvider;
    MetadataReader? _pdbReader;
    bool _pdbProbed;
    DecompilerSymbolSource _symbols = DecompilerSymbolSource.None;
    readonly string? _externalPdbPath;
    readonly bool _readSymbols;
    readonly AssemblyLocator _locator;
    readonly MetadataContext? _suppliedContext;
    MetadataContext? _crossContext;
    bool _ownsCrossContext;
    CrossAssemblyTypeResolver? _crossAssembly;

    MetadataSource(string path, FileStream stream, PEReader peReader, MetadataReader reader, string assemblyName, string? externalPdbPath, bool readSymbols, AssemblyLocator locator, MetadataContext? context)
    {
        Path = path;
        _stream = stream;
        Pe = peReader;
        Reader = reader;
        AssemblyName = assemblyName;
        _externalPdbPath = externalPdbPath;
        _readSymbols = readSymbols;
        _locator = locator;
        _suppliedContext = context;
    }

    public string Path { get; }

    /// <summary>Simple assembly name (no version/culture).</summary>
    public string AssemblyName { get; }

    /// <summary>
    /// Optimistic ("simulate") rendering: when set, the importer treats the
    /// module as if it opted into the updated memory-safety rules even when it
    /// carries no <c>MemorySafetyRulesAttribute</c>, so the printer emits the
    /// explicit <c>unsafe { }</c> contexts the new rules would require for legacy
    /// input. A migration preview that deliberately overlaps a source fixer; it
    /// fabricates contexts the original binary never had to satisfy, so it must
    /// stay opt-in and clearly labeled (see docs/design/memory-safety-modes.md).
    /// </summary>
    public bool SimulateNewRules { get; set; }

    internal PEReader Pe { get; }

    internal MetadataReader Reader { get; }

    /// <summary>
    /// The symbol source consulted for local names so far: <see cref="DecompilerSymbolSource.None"/>
    /// until a method with locals triggers the lazy PDB probe, then the kind of PDB found
    /// (embedded, sidecar, or the external path supplied at open). Reflects observed work,
    /// so a host can report honestly whether symbols were actually used for a render.
    /// </summary>
    public DecompilerSymbolSource Symbols => _symbols;

    /// <summary>
    /// Opens an assembly. Throws <see cref="BadImageFormatException"/> for files
    /// without managed metadata. <paramref name="externalPdbPath"/> is a portable
    /// PDB to use for source local names when the assembly carries no embedded
    /// or sidecar PDB — e.g. one the CLI downloaded from a symbol server.
    /// <paramref name="locator"/> resolves referenced assemblies for
    /// cross-assembly type facts (value-type-ness of a bare token); when null,
    /// the default policy looks only beside the opened assembly. A richer caller
    /// (the CLI) supplies a platform/package-aware locator.
    /// <paramref name="context"/> is a shared <see cref="MetadataContext"/> a
    /// batch caller may pass so a dependency such as CoreLib is opened once
    /// across many sources; when null, this source creates and owns one (and the
    /// supplied <paramref name="locator"/> seeds it). A supplied context is
    /// borrowed — the caller owns its disposal.
    /// </summary>
    public static MetadataSource Open(string path, string? externalPdbPath = null, AssemblyLocator? locator = null, MetadataContext? context = null)
        => OpenCore(path, externalPdbPath, readSymbols: true, locator, context);

    /// <summary>
    /// Opens an assembly without consulting any portable PDB, so local names are
    /// never recovered and the printer renders <c>V_index</c> slots. Use this for
    /// deterministic, symbol-independent output: the same DLL renders identically
    /// whether or not a PDB happens to be embedded, sidecar, or downloaded.
    /// </summary>
    public static MetadataSource OpenWithoutSymbols(string path, AssemblyLocator? locator = null, MetadataContext? context = null)
        => OpenCore(path, externalPdbPath: null, readSymbols: false, locator, context);

    /// <summary>
    /// Default referenced-assembly probing policy for callers that need to share a
    /// <see cref="MetadataContext"/> across several <see cref="MetadataSource"/>
    /// instances.
    /// </summary>
    public static AssemblyLocator DefaultAssemblyLocator(string path) => SiblingLocator(path);

    static MetadataSource OpenCore(string path, string? externalPdbPath, bool readSymbols, AssemblyLocator? locator, MetadataContext? context)
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
            return new MetadataSource(path, stream, peReader, reader, assemblyName, externalPdbPath, readSymbols, locator ?? DefaultAssemblyLocator(path), context);
        }
        catch
        {
            peReader?.Dispose();
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The decompiler's default "next-by" policy: a non-platform referenced
    /// assembly is expected to sit beside the one being decompiled. Richer
    /// resolution (packages, deps.json, projects, shared frameworks) belongs to
    /// callers that inject a resolver.
    /// </summary>
    static AssemblyLocator SiblingLocator(string path)
    {
        string? dir = System.IO.Path.GetDirectoryName(path);
        return (name, scope) =>
        {
            if (scope == AssemblyResolutionScope.Platform)
                return null;

            if (dir is not null)
            {
                string sibling = System.IO.Path.Combine(dir, name + ".dll");
                if (File.Exists(sibling))
                    return sibling;
            }

            return null;
        };
    }

    /// <summary>
    /// Resolver for facts about referenced types that this assembly's metadata
    /// cannot state on its own (value-type-ness of a bare cross-assembly token).
    /// </summary>
    internal CrossAssemblyTypeResolver CrossAssembly
        => _crossAssembly ??= new CrossAssemblyTypeResolver(AssemblyName, Reader, CrossContext);

    /// <summary>
    /// The shared assembly-reading environment cross-assembly resolution reads
    /// through. A borrowed context (passed to <see cref="Open(string, string?, AssemblyLocator?, MetadataContext?)"/>)
    /// is reused and left for its owner to dispose; otherwise this source creates
    /// and owns one seeded with its own locator.
    /// </summary>
    MetadataContext CrossContext
    {
        get
        {
            if (_crossContext is null)
            {
                _crossContext = _suppliedContext ?? new MetadataContext(_locator);
                _ownsCrossContext = _suppliedContext is null;
            }
            return _crossContext;
        }
    }

    Dictionary<TypeRef, TypeShape>? _shapes;
    Dictionary<TypeRef, IReadOnlyDictionary<long, string>>? _enumMembers;
    Dictionary<TypeRef, TypeRef>? _enumUnderlyingTypes;
    Dictionary<TypeRef, TypeRef?>? _baseTypes;
    HashSet<TypeRef>? _interfaces;
    Dictionary<TypeRef, ImmutableArray<TypeRef>>? _interfaceImpls;
    HashSet<TypeRef>? _unionTypes;

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

    internal bool IsUnionType(TypeRef type)
    {
        EnsureTypeMaps();
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        return definition is not null && _unionTypes!.Contains(definition);
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

    internal TypeRef? ResolveEnumUnderlyingType(TypeRef type)
    {
        if (type.Kind != TypeRefKind.Definition)
            return null;
        EnsureTypeMaps();
        return _enumUnderlyingTypes!.GetValueOrDefault(type);
    }

    void EnsureTypeMaps()
    {
        if (_shapes is not null)
            return;
        var shapes = new Dictionary<TypeRef, TypeShape>();
        var enums = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>();
        var enumUnderlyingTypes = new Dictionary<TypeRef, TypeRef>();
        var bases = new Dictionary<TypeRef, TypeRef?>();
        var interfaces = new HashSet<TypeRef>();
        var interfaceImpls = new Dictionary<TypeRef, ImmutableArray<TypeRef>>();
        var unionTypes = new HashSet<TypeRef>();
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
            var impls = DecodeInterfaces(typeDef, scope);
            if (MethodDefinitionFacts.HasUnionAttribute(Reader, typeDef)
                && impls.Any(IsUnionInterface))
                unionTypes.Add(key);
            interfaceImpls[key] = impls;
            if (shape == TypeShape.Enum)
            {
                enums[key] = BuildEnumMembers(typeDef);
                if (ResolveEnumUnderlyingType(typeDef, scope) is { } underlying)
                    enumUnderlyingTypes[key] = underlying;
            }
        }
        _enumMembers = enums;
        _enumUnderlyingTypes = enumUnderlyingTypes;
        _baseTypes = bases;
        _interfaces = interfaces;
        _interfaceImpls = interfaceImpls;
        _unionTypes = unionTypes;
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

    static bool IsUnionInterface(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Namespace: "System.Runtime.CompilerServices", Name: "IUnion" };

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

    static TypeRef? NamedDefinition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;

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

    /// <summary>
    /// Whether <paramref name="type"/> can legally be the receiver of C#
    /// collection-initializer `Add` entries. Exact evidence is the non-generic
    /// <c>System.Collections.IEnumerable</c> interface, resolved same-assembly or
    /// through the cross-assembly metadata context.
    /// </summary>
    internal MetadataFactState SupportsCollectionInitializer(TypeRef type)
    {
        if (NamedDefinition(type) is not { } definition || string.IsNullOrEmpty(definition.Assembly))
            return MetadataFactState.No;

        if (type.Equals(s_enumerable) || definition.Equals(s_enumerable))
            return MetadataFactState.Yes;

        if (definition.Assembly == TypeRefDecoder.Canonical(AssemblyName))
            return Implements(type, s_enumerable) ? MetadataFactState.Yes : MetadataFactState.No;

        return CrossAssembly.Implements(type, s_enumerable);
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

    TypeRef? ResolveEnumUnderlyingType(TypeDefinition enumType, GenericScope scope)
    {
        foreach (var fieldHandle in enumType.GetFields())
        {
            var field = Reader.GetFieldDefinition(fieldHandle);
            if (Reader.GetString(field.Name) == "value__")
                return field.DecodeSignature(TypeRefDecoder.Instance, scope);
        }
        return null;
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
        _pdbProvider?.Dispose();
        if (_ownsCrossContext)
            _crossContext?.Dispose();
        Pe.Dispose();
        _stream.Dispose();
    }

    /// <summary>
    /// The associated portable PDB reader — embedded in the PE or a sidecar
    /// <c>.pdb</c> next to it — opened once and cached. Null when no PDB is
    /// found or it cannot be read; the importer then leaves local names absent
    /// and the printer falls back to <c>V_index</c>.
    /// </summary>
    MetadataReader? PdbReader()
    {
        if (_pdbProbed)
            return _pdbReader;
        _pdbProbed = true;
        if (!_readSymbols)
            return null;
        try
        {
            if (Pe.TryOpenAssociatedPortablePdb(Path, p => File.Exists(p) ? File.OpenRead(p) : null, out var provider, out var pdbPath)
                && provider is not null)
            {
                _pdbProvider = provider;
                _pdbReader = provider.GetMetadataReader();
                _symbols = string.IsNullOrEmpty(pdbPath)
                    ? DecompilerSymbolSource.Embedded
                    : DecompilerSymbolSource.Sidecar;
            }
            else if (!string.IsNullOrEmpty(_externalPdbPath) && File.Exists(_externalPdbPath))
            {
                // No embedded or sidecar PDB, but the CLI supplied one (e.g. a
                // symbol-server download). PrefetchMetadata copies it in, so the
                // stream can close immediately; the provider owns the lifetime.
                using var pdbStream = File.OpenRead(_externalPdbPath);
                _pdbProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream, MetadataStreamOptions.PrefetchMetadata);
                _pdbReader = _pdbProvider.GetMetadataReader();
                _symbols = DecompilerSymbolSource.External;
            }
        }
        catch
        {
            // No PDB, or an unreadable one — names stay absent.
        }
        return _pdbReader;
    }

    /// <summary>
    /// Source local-variable names for a method from its portable PDB, indexed
    /// by IL local slot. No PDB returns an empty array. Present PDB entries with
    /// no recorded name, a compiler-generated (debugger-hidden) local, or a name
    /// that is not a usable identifier stay null, and the printer renders
    /// <c>V_index</c>.
    /// </summary>
    internal ImmutableArray<string?> LocalNames(MethodDefinitionHandle methodHandle, int localCount)
    {
        if (localCount == 0)
            return [];
        var pdb = PdbReader();
        if (pdb is null)
            return [];

        var names = new string?[localCount];
        try
        {
            foreach (var scopeHandle in pdb.GetLocalScopes(methodHandle))
            {
                var scope = pdb.GetLocalScope(scopeHandle);
                foreach (var varHandle in scope.GetLocalVariables())
                {
                    var variable = pdb.GetLocalVariable(varHandle);
                    if ((variable.Attributes & LocalVariableAttributes.DebuggerHidden) != 0)
                        continue;
                    if (variable.Index >= 0 && variable.Index < localCount)
                        names[variable.Index] = pdb.GetString(variable.Name);
                }
            }
        }
        catch
        {
            // Malformed scope table — keep whatever names were read.
        }
        return [.. names];
    }
}
