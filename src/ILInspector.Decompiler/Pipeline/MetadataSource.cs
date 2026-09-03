using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

internal static class OperatorHierarchyLimits
{
    // Bound both graph depth and breadth: malformed metadata can attach an
    // arbitrary number of duplicate InterfaceImpl rows to one definition.
    // Gated by WideInterfaceHierarchy_EnforcesWorkBudget and
    // CrossAssemblyGenericParameters_EnforceWorkBudget.
    public const int Types = 256;
    public const int WorkItems = 4096;
}

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

    readonly Stream? _stream;
    MetadataReaderProvider? _pdbProvider;
    MetadataReader? _pdbReader;
    volatile bool _pdbProbed;
    DecompilerSymbolSource _symbols = DecompilerSymbolSource.None;
    readonly string? _externalPdbPath;
    readonly bool _readSymbols;
    readonly IAssemblyBindingPolicy _bindingPolicy;
    readonly ResolvedAssemblyReference _assembly;
    readonly MetadataContext? _suppliedContext;
    MetadataContext? _crossContext;
    bool _ownsCrossContext;
    CrossAssemblyTypeResolver? _crossAssembly;
    readonly object _crossLock = new();
    readonly object _acquisitionGuard = new();
    readonly Lazy<StateMachineRelationshipIndex> _stateMachineRelationships;
    readonly Lazy<MemorySafetyMetadataIndex> _memorySafety;

    MetadataSource(string path, string? filePath, Stream? stream, PEReader peReader, MetadataReader reader, string assemblyName, ResolvedAssemblyReference assembly, string? externalPdbPath, bool readSymbols, IAssemblyBindingPolicy bindingPolicy, MetadataContext? context)
    {
        Path = path;
        FilePath = filePath;
        _stream = stream;
        Pe = peReader;
        Reader = reader;
        AssemblyName = assemblyName;
        _assembly = assembly;
        _externalPdbPath = externalPdbPath;
        _readSymbols = readSymbols;
        _bindingPolicy = bindingPolicy;
        _suppliedContext = context;
        _stateMachineRelationships =
            new(() => StateMachineRelationshipIndex.Create(reader));
        _memorySafety = new(() => MemorySafetyMetadataIndex.Create(reader));
    }

    public string Path { get; }

    /// <summary>
    /// The filesystem path this source was opened from, or <see langword="null"/> when it was
    /// opened from a stream-backed <see cref="ResolvedAssemblyReference"/> that has none.
    /// <see cref="Path"/> falls back to the assembly's identity name so a source always has
    /// something to name itself by; that fallback is a label, not a file, and a caller that
    /// intends to open a file — or to key a path-shaped cache — must consult this instead.
    /// For a source opened from a path the two are the same string, so no existing caller's
    /// answer changes. Gated by
    /// <c>ContentShapedMemberProjectionTests.PathlessSourceProjectsWithoutFabricatingAFilePath</c>.
    /// </summary>
    internal string? FilePath { get; }

    /// <summary>Simple assembly name (no version/culture).</summary>
    public string AssemblyName { get; }

    /// <summary>The opened module's stable metadata identity.</summary>
    public Guid ModuleVersionId =>
        Reader.GetGuid(Reader.GetModuleDefinition().Mvid);

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

    internal object AcquisitionGuard => _acquisitionGuard;

    internal MemorySafetyMetadataIndex MemorySafety => _memorySafety.Value;

    internal ClassicAsyncRequestAdapterResult AdaptClassicAsyncRequest(
        MethodDefinitionHandle method,
        MethodClassification? classification) =>
        ClassicAsyncRequestAdapter.Adapt(
            Reader,
            _stateMachineRelationships.Value,
            method,
            classification,
            _acquisitionGuard);

    /// <summary>
    /// The symbol source consulted for local names so far: <see cref="DecompilerSymbolSource.None"/>
    /// until a method with locals triggers the lazy PDB probe, then the kind of PDB found
    /// (embedded, sidecar, or the external path supplied at open). Reflects observed work,
    /// so a host can report honestly whether symbols were actually used for a render.
    /// </summary>
    public DecompilerSymbolSource Symbols => _symbols;

    /// <summary>
    /// Extracts this assembly's <see cref="ApiSurface"/> from the already-open PE image,
    /// mirroring <c>AssemblyInspectionSession</c>/<c>PdbContext</c> so callers do not re-open
    /// the file. Presentation-neutral type projection (<c>ResearchViews.ProjectType</c>) uses
    /// this to compose type-level views from one live source.
    /// </summary>
    public ApiSurface ExtractApiSurface(bool includeAll = false, bool typesOnly = false)
        => ApiSurfaceExtractor.Extract(Pe, includeAll, typesOnly);

    /// <summary>
    /// Classifies the async shape of the given MethodDef metadata token (runtime or
    /// state-machine async), or <see langword="null"/> when the token is not a MethodDef or
    /// the method is not async. Async is a body-gated fact the API surface deliberately omits
    /// (docs/design/member-body-substrate.md, <c>ApiMember.IsAsync</c>), so callers that need
    /// an accurate async signal recover it here from live metadata.
    /// </summary>
    /// <summary>
    /// Reports whether <paramref name="methodDefToken"/> names an existing row in this image's
    /// MethodDef table. A caller that accepts a raw token from a user needs to answer that before
    /// handing the token to analysis, which validates handles by throwing.
    /// </summary>
    public bool ContainsMethodDefinition(int methodDefToken)
    {
        var entity = MetadataTokens.EntityHandle(methodDefToken);
        if (entity.Kind != HandleKind.MethodDefinition)
            return false;

        int row = MetadataTokens.GetRowNumber(entity);
        return row > 0 && row <= Reader.GetTableRowCount(TableIndex.MethodDef);
    }

    public MethodClassification? ClassifyAsync(int methodDefToken)
    {
        var entity = MetadataTokens.EntityHandle(methodDefToken);
        if (entity.Kind != HandleKind.MethodDefinition)
            return null;

        var method = Reader.GetMethodDefinition((MethodDefinitionHandle)entity);
        return MethodClassificationScanner.ClassifyAsyncMethod(Reader, method);
    }

    /// <summary>
    /// Opens an assembly. Throws <see cref="BadImageFormatException"/> for files
    /// without managed metadata. <paramref name="externalPdbPath"/> is a portable
    /// PDB to use for source local names when the assembly carries no embedded
    /// or sidecar PDB — e.g. one the CLI downloaded from a symbol server.
    /// Referenced assemblies for cross-assembly type facts (value-type-ness of a
    /// bare token) are resolved by the default policy, which looks only beside
    /// the opened assembly. Callers that need identity- or stream-backed
    /// resolution should use the <see cref="IAssemblyReferenceResolver"/> overload.
    /// <paramref name="context"/> is a shared <see cref="MetadataContext"/> a
    /// batch caller may pass so a dependency such as CoreLib is opened once
    /// across many sources; when null, this source creates and owns one seeded
    /// by the effective resolver. A supplied context is
    /// borrowed — the caller owns its disposal.
    /// </summary>
    public static MetadataSource Open(string path, string? externalPdbPath = null, MetadataContext? context = null)
        => OpenCore(path, externalPdbPath, readSymbols: true, resolver: null, context);

    public static MetadataSource Open(string path, string? externalPdbPath, IAssemblyReferenceResolver resolver, MetadataContext? context = null)
        => OpenCore(path, externalPdbPath, readSymbols: true, resolver, context);

    public static MetadataSource Open(ResolvedAssemblyReference assembly, string? externalPdbPath, IAssemblyReferenceResolver resolver, MetadataContext? context = null)
        => OpenCore(assembly, externalPdbPath, readSymbols: true, resolver, context);

    /// <summary>
    /// Opens a descriptor-backed assembly under an existing immutable
    /// assembly-binding policy.
    /// </summary>
    public static MetadataSource Open(
        ResolvedAssemblyReference assembly,
        string? externalPdbPath,
        IAssemblyBindingPolicy bindingPolicy,
        MetadataContext? context = null)
        => OpenCore(
            assembly,
            externalPdbPath,
            readSymbols: true,
            bindingPolicy,
            context);

    /// <summary>
    /// Opens an assembly without consulting any portable PDB, so local names are
    /// never recovered and the printer renders <c>V_index</c> slots. Use this for
    /// deterministic, symbol-independent output: the same DLL renders identically
    /// whether or not a PDB happens to be embedded, sidecar, or downloaded.
    /// </summary>
    public static MetadataSource OpenWithoutSymbols(string path, MetadataContext? context = null)
        => OpenCore(path, externalPdbPath: null, readSymbols: false, resolver: null, context);

    public static MetadataSource OpenWithoutSymbols(string path, IAssemblyReferenceResolver resolver, MetadataContext? context = null)
        => OpenCore(path, externalPdbPath: null, readSymbols: false, resolver, context);

    public static MetadataSource OpenWithoutSymbols(ResolvedAssemblyReference assembly, IAssemblyReferenceResolver resolver, MetadataContext? context = null)
        => OpenCore(assembly, externalPdbPath: null, readSymbols: false, resolver, context);

    public static MetadataSource OpenWithoutSymbols(
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy,
        MetadataContext? context = null)
        => OpenCore(
            assembly,
            externalPdbPath: null,
            readSymbols: false,
            bindingPolicy,
            context);

    /// <summary>
    /// Opens an immutable PE snapshot without reopening <paramref name="path"/>.
    /// </summary>
    /// <param name="path">
    /// Original assembly path, used for identity, diagnostics, sidecar symbol lookup, and the
    /// default sibling-assembly resolver. The PE bytes are read only from
    /// <paramref name="image"/>.
    /// </param>
    /// <param name="image">Fully prefetched immutable PE image.</param>
    /// <param name="externalPdbPath">Optional portable PDB used for local names.</param>
    /// <param name="resolver">
    /// Optional referenced-assembly resolver. When omitted, referenced assemblies are resolved
    /// beside <paramref name="path"/>.
    /// </param>
    /// <param name="context">
    /// Optional shared cross-assembly metadata context. The caller retains ownership.
    /// </param>
    /// <returns>A live metadata source over the supplied snapshot.</returns>
    public static MetadataSource OpenFromPrefetchedImage(
        string path,
        ImmutableArray<byte> image,
        string? externalPdbPath = null,
        IAssemblyReferenceResolver? resolver = null,
        MetadataContext? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (image.IsDefaultOrEmpty)
            throw new ArgumentException("The prefetched PE image must not be empty.", nameof(image));

        PEReader? peReader = null;
        try
        {
            peReader = new PEReader(image);
            if (!peReader.HasMetadata)
                throw new BadImageFormatException($"No managed metadata: {path}");

            var reader = peReader.GetMetadataReader();
            string assemblyName = reader.IsAssembly
                ? reader.GetString(reader.GetAssemblyDefinition().Name)
                : System.IO.Path.GetFileNameWithoutExtension(path);
            string fullPath = System.IO.Path.GetFullPath(path);
            var identity = reader.IsAssembly
                ? AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
                : new AssemblyReferenceIdentity(
                    assemblyName,
                    Version: null,
                    Culture: null,
                    PublicKeyToken: null);
            var assembly = ResolvedAssemblyReference.Create(
                identity,
                fullPath,
                () => new MemoryStream(image.ToArray(), writable: false),
                AssemblyResolutionProvenance.Local("MetadataSource snapshot"));
            var bindingPolicy = new AssemblyReferenceBindingPolicy(
                resolver ?? DefaultAssemblyReferenceResolver(path));
            // The caller named this exact image, which is a designation, so it
            // is entitled to core-library identity; see CoreLibraryIdentityTrust.
            // The resolved reference above records Local provenance because that
            // describes how the stream is reopened, not how the assembly was
            // acquired. Routing through GrantIfEntitled keeps the rule the only
            // source of entitlement.
            CoreLibraryIdentityTrust.GrantIfEntitled(
                reader,
                AssemblyResolutionProvenance.Designated("MetadataSource snapshot"));
            return new MetadataSource(
                path,
                fullPath,
                stream: null,
                peReader,
                reader,
                assemblyName,
                assembly,
                externalPdbPath,
                readSymbols: true,
                bindingPolicy,
                context);
        }
        catch
        {
            peReader?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Default referenced-assembly probing policy for callers that need to share
    /// a <see cref="MetadataContext"/> across several <see cref="MetadataSource"/>
    /// instances. It resolves only non-platform assemblies copied beside
    /// <paramref name="path"/>.
    /// </summary>
    public static IAssemblyReferenceResolver DefaultAssemblyReferenceResolver(string path) => new SiblingAssemblyReferenceResolver(path);

    static MetadataSource OpenCore(string path, string? externalPdbPath, bool readSymbols, IAssemblyReferenceResolver? resolver, MetadataContext? context)
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
            var effectiveResolver = resolver ?? DefaultAssemblyReferenceResolver(path);
            string fullPath = System.IO.Path.GetFullPath(path);
            var identity = reader.IsAssembly
                ? AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
                : new AssemblyReferenceIdentity(
                    assemblyName,
                    Version: null,
                    Culture: null,
                    PublicKeyToken: null);
            var assembly = ResolvedAssemblyReference.Create(
                identity,
                fullPath,
                () => File.OpenRead(fullPath),
                AssemblyResolutionProvenance.Local("MetadataSource"));
            // The caller named this exact path, which is a designation; see the
            // sibling site above and CoreLibraryIdentityTrust.
            CoreLibraryIdentityTrust.GrantIfEntitled(
                reader,
                AssemblyResolutionProvenance.Designated("MetadataSource"));
            return new MetadataSource(
                path,
                path,
                stream,
                peReader,
                reader,
                assemblyName,
                assembly,
                externalPdbPath,
                readSymbols,
                new AssemblyReferenceBindingPolicy(effectiveResolver),
                context);
        }
        catch
        {
            peReader?.Dispose();
            stream.Dispose();
            throw;
        }
    }

    static MetadataSource OpenCore(ResolvedAssemblyReference assembly, string? externalPdbPath, bool readSymbols, IAssemblyReferenceResolver resolver, MetadataContext? context)
        => OpenCore(
            assembly,
            externalPdbPath,
            readSymbols,
            new AssemblyReferenceBindingPolicy(resolver),
            context);

    static MetadataSource OpenCore(
        ResolvedAssemblyReference assembly,
        string? externalPdbPath,
        bool readSymbols,
        IAssemblyBindingPolicy bindingPolicy,
        MetadataContext? context)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        var stream = assembly.OpenRead();
        PEReader? peReader = null;
        try
        {
            peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                throw new BadImageFormatException($"No managed metadata: {assembly.Identity.Name}");
            var reader = peReader.GetMetadataReader();
            string assemblyName = reader.IsAssembly
                ? reader.GetString(reader.GetAssemblyDefinition().Name)
                : assembly.Identity.Name;
            string path = assembly.Path ?? assembly.Identity.Name;
            CoreLibraryIdentityTrust.GrantIfEntitled(
                reader,
                assembly.Provenance);
            return new MetadataSource(
                path,
                assembly.Path,
                stream,
                peReader,
                reader,
                assemblyName,
                assembly,
                externalPdbPath,
                readSymbols,
                bindingPolicy,
                context);
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
    internal sealed class SiblingAssemblyReferenceResolver
        : IAssemblyReferenceResolver
    {
        readonly string? _directory;
        readonly Lazy<IReadOnlyList<string>> _candidates;
        readonly ConcurrentDictionary<
            string,
            Lazy<ResolvedAssemblyReference?>> _assemblies =
                new(StringComparer.Ordinal);

        public SiblingAssemblyReferenceResolver(string path)
        {
            _directory = System.IO.Path.GetDirectoryName(
                System.IO.Path.GetFullPath(path));
            _candidates = new(
                EnumerateCandidates,
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        {
            if (scope == AssemblyResolutionScope.Platform)
                return null;

            foreach (string sibling in _candidates.Value)
            {
                if (!System.IO.Path.GetFileNameWithoutExtension(sibling)
                    .Equals(identity.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                ResolvedAssemblyReference? candidate =
                    _assemblies.GetOrAdd(
                        sibling,
                        static path => new Lazy<ResolvedAssemblyReference?>(
                            () => ResolvedAssemblyReference.TryCreateFromPath(
                                path,
                                AssemblyResolutionProvenance.Local(
                                    "SiblingAssembly"),
                                out ResolvedAssemblyReference? reference)
                                    ? reference
                                    : null,
                            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
                if (candidate is not null
                    && identity.MatchesCandidate(
                        candidate.Identity,
                        ignoreVersion: true))
                {
                    return candidate;
                }
            }

            return null;
        }

        IReadOnlyList<string> EnumerateCandidates()
            => _directory is not null && Directory.Exists(_directory)
                ? Directory.EnumerateFiles(_directory, "*.dll").ToArray()
                : [];
    }

    /// <summary>
    /// Resolver for facts about referenced types that this assembly's metadata
    /// cannot state on its own (value-type-ness of a bare cross-assembly token).
    /// </summary>
    internal CrossAssemblyTypeResolver CrossAssembly
    {
        get
        {
            if (_crossAssembly is null)
            {
                lock (_crossLock)
                {
                    _crossAssembly ??= new CrossAssemblyTypeResolver(
                        Reader,
                        _assembly,
                        CrossContext);
                }
            }
            return _crossAssembly;
        }
    }

    /// <summary>
    /// The shared assembly-reading environment cross-assembly resolution reads
    /// through. A borrowed context (passed to <see cref="Open(string, string?, IAssemblyReferenceResolver, MetadataContext?)"/>)
    /// is reused and left for its owner to dispose; otherwise this source creates
    /// and owns one seeded with its resolver.
    /// </summary>
    MetadataContext CrossContext
    {
        get
        {
            if (_suppliedContext is not null)
                return _suppliedContext;
                
            if (_crossContext is null)
            {
                lock (_crossLock)
                {
                    if (_crossContext is null)
                    {
                        _crossContext = new MetadataContext(
                            _bindingPolicy);
                        _ownsCrossContext = true;
                    }
                }
            }
            return _crossContext;
        }
    }

    volatile Dictionary<TypeRef, TypeShape>? _shapes;
    Dictionary<TypeRef, IReadOnlyDictionary<long, string>>? _enumMembers;
    Dictionary<TypeRef, TypeRef>? _enumUnderlyingTypes;
    Dictionary<TypeRef, TypeRef?>? _baseTypes;
    HashSet<TypeRef>? _interfaces;
    HashSet<TypeRef>? _genericDefinitions;
    Dictionary<TypeRef, ImmutableArray<TypeRef>>? _interfaceImpls;
    HashSet<TypeRef>? _unionTypes;
    HashSet<TypeRef>? _byRefLikeTypes;
    HashSet<TypeRef>? _delegates;
    readonly ConcurrentDictionary<(TypeDefinitionIdentity Type, string MethodName), MetadataFactState> _operatorHierarchyFacts = new();

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
    /// The full <see cref="TypeShapeKind"/> of a type — class, struct, enum,
    /// interface, or delegate — resolved same-assembly from the type maps or,
    /// cross-assembly, through the metadata context. <see cref="TypeShapeKind.Unknown"/>
    /// when no definition resolves (e.g. a cross-assembly type outside the loaded
    /// reference closure) or the type is not a named definition. The single product
    /// entry point that replaces per-consumer base-chain re-derivation; a richer
    /// projection over the same reads as <see cref="ResolveShape"/>, not a parallel
    /// resolver.
    /// </summary>
    public TypeShapeKind ClassifyType(TypeRef type)
    {
        if (type.Kind is TypeRefKind.SzArray or TypeRefKind.Array)
            return TypeShapeKind.Class;

        var resolved = ResolveShapeKind(type);
        if (resolved != TypeShapeKind.Unknown)
            return resolved;

        // Resolution fails for a cross-assembly type outside the reference closure
        // (e.g. a TypeSpec-constructed Nullable<int> opened with no platform
        // resolver). Fall back to the local signature hint the decoded TypeRef
        // carries — the ELEMENT_TYPE_VALUETYPE/CLASS byte — so this stays at least
        // as faithful as the legacy single-assembly newobj classifier it replaces.
        // The byte cannot distinguish enum from struct, so a value-type hint reports
        // Struct (constructed types are never enums).
        return type.DeclaredValueTypeHint switch
        {
            ValueTypeHint.ValueType => TypeShapeKind.Struct,
            ValueTypeHint.ReferenceType => TypeShapeKind.Class,
            _ => TypeShapeKind.Unknown,
        };
    }

    /// <summary>
    /// The definition-backed type shape without signature-hint fallback. Importer
    /// facts use this narrower result because a VALUETYPE signature byte cannot
    /// distinguish an unresolved struct from an unresolved enum.
    /// </summary>
    internal TypeShapeKind ClassifyResolvedType(TypeRef type)
        => type.Kind is TypeRefKind.SzArray or TypeRefKind.Array
            ? TypeShapeKind.Class
            : ResolveShapeKind(type);

    TypeShapeKind ResolveShapeKind(TypeRef type)
    {
        if (NamedDefinition(type) is not { } definition || string.IsNullOrEmpty(definition.Assembly))
            return TypeShapeKind.Unknown;

        if (TypeDefinitionIdentity.BelongsToAssembly(
            definition,
            Reader.IsAssembly ? TypeRefDecoder.CanonicalSelf(Reader) : "",
            _assembly.Identity))
        {
            EnsureTypeMaps();
            if (_delegates!.Contains(definition))
                return TypeShapeKind.Delegate;
            if (_interfaces!.Contains(definition))
                return TypeShapeKind.Interface;
            return _shapes!.GetValueOrDefault(definition, TypeShape.Unknown) switch
            {
                TypeShape.Enum => TypeShapeKind.Enum,
                TypeShape.ValueType => TypeShapeKind.Struct,
                TypeShape.Reference => TypeShapeKind.Class,
                _ => TypeShapeKind.Unknown,
            };
        }

        return CrossAssembly.ClassifyShape(definition);
    }

    /// <summary>
    /// <see cref="ClassifyType(TypeRef)"/> for a raw type handle
    /// (<c>TypeDefinition</c>/<c>TypeReference</c>/<c>TypeSpecification</c>).
    /// </summary>
    public TypeShapeKind ClassifyType(EntityHandle handle)
    {
        TypeRef? type = handle.Kind switch
        {
            HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(Reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(Reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(Reader, new GenericScope([], []), (TypeSpecificationHandle)handle, 0),
            _ => null,
        };
        return type is null ? TypeShapeKind.Unknown : ClassifyType(type);
    }

    /// <summary>
    /// The <see cref="TypeShapeKind"/> of the type constructed/referenced by a
    /// constructor token: the declaring type of a <c>MethodDefinition</c> or
    /// <c>MemberReference</c> token (a <c>newobj</c> target).
    /// <see cref="TypeShapeKind.Unknown"/> for any other token kind.
    /// </summary>
    public TypeShapeKind ClassifyConstructedType(int metadataToken)
    {
        var handle = MetadataTokens.EntityHandle(metadataToken);
        EntityHandle typeHandle = handle.Kind switch
        {
            HandleKind.MethodDefinition => Reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType(),
            HandleKind.MemberReference => Reader.GetMemberReference((MemberReferenceHandle)handle).Parent,
            _ => default,
        };
        return typeHandle.IsNil ? TypeShapeKind.Unknown : ClassifyType(typeHandle);
    }

    internal bool IsUnionType(TypeRef type)
    {
        EnsureTypeMaps();
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        return definition is not null && _unionTypes!.Contains(definition);
    }

    internal bool IsByRefLikeType(TypeRef type)
    {
        if (NamedDefinition(type) is not { } definition || string.IsNullOrEmpty(definition.Assembly))
            return false;
        EnsureTypeMaps();
        // Same-assembly ref structs are authoritative in the enumerated set; a
        // same-assembly type absent from it is not a ref struct. Only a
        // cross-assembly (referenced) definition needs the resolver, which reads
        // [IsByRefLike] from the defining assembly's metadata.
        if (TypeDefinitionIdentity.BelongsToAssembly(
            definition,
            Reader.IsAssembly ? TypeRefDecoder.CanonicalSelf(Reader) : "",
            _assembly.Identity))
            return _byRefLikeTypes!.Contains(definition);
        return CrossAssembly.IsByRefLike(definition) == MetadataFactState.Yes;
    }

    /// <summary>
    /// Whether the named reference type's C# binding hierarchy declares the
    /// requested equality operator. <see cref="MetadataFactState.No"/> is
    /// returned only after the reachable class/base or interface hierarchy has
    /// been inspected; unresolved hierarchy edges remain unknown.
    /// </summary>
    /// <remarks>
    /// Gated by <c>BoxedReferenceEqualityTests</c>' same-assembly inherited
    /// operator and operator-free class cases.
    /// </remarks>
    internal MetadataFactState HasOperatorInBindingHierarchy(TypeRef type, string methodName)
    {
        if (NamedDefinition(type) is not { } definition
            || string.IsNullOrEmpty(definition.Assembly)
            || TypeDefinitionIdentity.Create(definition) is not { } identity)
            return MetadataFactState.Unknown;
        var cacheKey = (identity, methodName);
        if (_operatorHierarchyFacts.TryGetValue(cacheKey, out var cached))
            return cached;

        string self = Reader.IsAssembly ? TypeRefDecoder.CanonicalSelf(Reader) : "";
        if (!TypeDefinitionIdentity.BelongsToAssembly(
            definition,
            self,
            _assembly.Identity))
        {
            var crossAssembly = CrossAssembly.HasOperatorInBindingHierarchy(type, methodName);
            _operatorHierarchyFacts.TryAdd(cacheKey, crossAssembly);
            return crossAssembly;
        }

        _ = methodName switch
        {
            "op_Equality" => true,
            "op_Inequality" => true,
            _ => throw new ArgumentOutOfRangeException(nameof(methodName)),
        };
        bool unresolved = false;
        int remainingWork = OperatorHierarchyLimits.WorkItems;
        var seen = new HashSet<TypeDefinitionIdentity>();
        var pending = new Stack<TypeRef>();
        pending.Push(type);
        while (pending.Count > 0
            && seen.Count < OperatorHierarchyLimits.Types
            && remainingWork-- > 0)
        {
            var current = pending.Pop();
            if (NamedDefinition(current) is not { } currentDefinition
                || TypeDefinitionIdentity.Create(currentDefinition) is not { } currentIdentity)
            {
                unresolved = true;
                continue;
            }
            if (!seen.Add(currentIdentity))
                continue;

            if (!TypeDefinitionIdentity.BelongsToAssembly(
                currentDefinition,
                self,
                _assembly.Identity))
            {
                var crossAssembly = CrossAssembly.HasOperatorInBindingHierarchy(current, methodName);
                if (crossAssembly == MetadataFactState.Yes)
                {
                    _operatorHierarchyFacts.TryAdd(cacheKey, MetadataFactState.Yes);
                    return MetadataFactState.Yes;
                }
                if (crossAssembly == MetadataFactState.Unknown)
                    unresolved = true;
                continue;
            }

            TypeDefinitionHandle handle =
                currentDefinition.DefinitionModuleVersionId is { } sourceMvid
                    && sourceMvid != Guid.Empty
                    && ModuleVersionId != Guid.Empty
                    && sourceMvid == ModuleVersionId
                    && IsLocalRowFor(
                        currentDefinition.DefinitionHandle,
                        currentIdentity.DefinitionName)
                    ? currentDefinition.DefinitionHandle
                    : default;
            if (handle.IsNil)
            {
                var lookup = FindLocalTypeDefinition(
                    currentIdentity.DefinitionName,
                    ref remainingWork);
                if (lookup.Kind != LocalTypeDefinitionLookupKind.Found)
                {
                    unresolved = true;
                    if (lookup.Kind
                        == LocalTypeDefinitionLookupKind.BudgetExceeded)
                    {
                        break;
                    }
                    continue;
                }
                handle = lookup.Handle;
            }

            var typeDefinition = Reader.GetTypeDefinition(handle);
            foreach (var methodHandle in typeDefinition.GetMethods())
            {
                if (remainingWork-- <= 0)
                {
                    unresolved = true;
                    break;
                }
                var method = Reader.GetMethodDefinition(methodHandle);
                if (!Reader.StringComparer.Equals(method.Name, methodName))
                    continue;
                bool hasThis =
                    (method.Attributes
                        & System.Reflection.MethodAttributes.Static) == 0;
                if (MethodDefinitionFacts.IsOperator(
                    method,
                    methodName,
                    hasThis))
                {
                    _operatorHierarchyFacts.TryAdd(
                        cacheKey,
                        MetadataFactState.Yes);
                    return MetadataFactState.Yes;
                }
            }
            if (remainingWork < 0)
                break;

            var genericParameters = typeDefinition.GetGenericParameters();
            if (!TryCreateHierarchyGenericScope(
                genericParameters,
                ref remainingWork,
                out var scope))
            {
                unresolved = true;
                break;
            }
            bool isGenericInstance =
                current.Kind == TypeRefKind.GenericInstance;

            if ((typeDefinition.Attributes
                & System.Reflection.TypeAttributes.Interface) != 0)
            {
                foreach (var implementationHandle
                    in typeDefinition.GetInterfaceImplementations())
                {
                    if (remainingWork-- <= 0)
                    {
                        unresolved = true;
                        break;
                    }
                    var implementation =
                        Reader.GetInterfaceImplementation(
                            implementationHandle);
                    TypeRef? baseInterface = DecodeBaseType(
                        implementation.Interface,
                        scope);
                    if (baseInterface is null)
                    {
                        unresolved = true;
                        continue;
                    }
                    pending.Push(isGenericInstance
                        ? baseInterface.Instantiate(
                            current.TypeArguments,
                            [])
                        : baseInterface);
                }
                if (remainingWork < 0)
                    break;
                continue;
            }

            if (!typeDefinition.BaseType.IsNil)
            {
                // Charged before the decode and for every non-nil edge,
                // including the one that reaches System.Object: the decode is
                // itself the work being bounded, and the cross-assembly walk
                // charges the same way. Skipping either case lets the local
                // path run past the budget and still answer with confidence
                // where the cross-assembly path answers Unknown.
                if (remainingWork-- <= 0)
                {
                    unresolved = true;
                    break;
                }
                TypeRef? baseType = DecodeBaseType(
                    typeDefinition.BaseType,
                    scope);
                if (baseType is null)
                {
                    unresolved = true;
                    continue;
                }
                if (!IsObject(baseType))
                {
                    pending.Push(isGenericInstance
                        ? baseType.Instantiate(current.TypeArguments, [])
                        : baseType);
                }
            }
        }

        var result = !unresolved && pending.Count == 0
            ? MetadataFactState.No
            : MetadataFactState.Unknown;
        _operatorHierarchyFacts.TryAdd(cacheKey, result);
        return result;
    }

    /// <summary>
    /// Whether a stored TypeDef row handle really names <paramref name="name"/>
    /// in <em>this</em> module. A module version id is metadata like any other:
    /// a hostile or accidentally duplicated image can carry the MVID this
    /// module carries, so a matching MVID makes the recorded row a hint rather
    /// than a fact. Confirm the row is in range and still spells the expected
    /// structured name before reusing it; callers fall back to the bounded
    /// <see cref="FindLocalTypeDefinition"/> scan when this returns false.
    /// </summary>
    bool IsLocalRowFor(
        TypeDefinitionHandle handle,
        MetadataTypeDefinitionName name)
    {
        if (handle.IsNil)
            return false;
        // Range check first as defence in depth: the structured-name match
        // below already refuses a row this module does not have, so this guard
        // is deliberately not independently gated.
        int row = MetadataTokens.GetRowNumber(handle);
        if (row < 1 || row > Reader.GetTableRowCount(TableIndex.TypeDef))
            return false;
        return MetadataTypeDefinitionName.Matches(
            Reader,
            handle,
            name,
            out _) == MetadataTypeDefinitionNameMatchResult.Match;
    }

    LocalTypeDefinitionLookup FindLocalTypeDefinition(
        MetadataTypeDefinitionName name,
        ref int remainingWork)
    {
        TypeDefinitionHandle match = default;
        bool rejected = false;
        foreach (var handle in Reader.TypeDefinitions)
        {
            if (remainingWork-- <= 0)
            {
                return new LocalTypeDefinitionLookup(
                    LocalTypeDefinitionLookupKind.BudgetExceeded);
            }

            switch (MetadataTypeDefinitionName.Matches(
                Reader,
                handle,
                name,
                out _))
            {
                case MetadataTypeDefinitionNameMatchResult.Match
                    when !match.IsNil:
                    return new LocalTypeDefinitionLookup(
                        LocalTypeDefinitionLookupKind.Ambiguous);
                case MetadataTypeDefinitionNameMatchResult.Match:
                    match = handle;
                    break;
                case MetadataTypeDefinitionNameMatchResult.Rejected:
                    rejected = true;
                    break;
            }
        }

        if (!match.IsNil)
        {
            return new LocalTypeDefinitionLookup(
                LocalTypeDefinitionLookupKind.Found,
                match);
        }
        return new LocalTypeDefinitionLookup(
            rejected
                ? LocalTypeDefinitionLookupKind.Rejected
                : LocalTypeDefinitionLookupKind.Missing);
    }

    enum LocalTypeDefinitionLookupKind
    {
        Found,
        Missing,
        Ambiguous,
        Rejected,
        BudgetExceeded,
    }

    readonly record struct LocalTypeDefinitionLookup(
        LocalTypeDefinitionLookupKind Kind,
        TypeDefinitionHandle Handle = default);

    static bool TryCreateHierarchyGenericScope(
        GenericParameterHandleCollection parameters,
        ref int remainingWork,
        out GenericScope scope)
    {
        if (parameters.Count == 0)
        {
            scope = new GenericScope([], []);
            return true;
        }
        if (parameters.Count > remainingWork)
        {
            remainingWork = 0;
            scope = new GenericScope([], []);
            return false;
        }

        var names = ImmutableArray.CreateBuilder<string>(
            parameters.Count);
        remainingWork -= parameters.Count;
        foreach (var _ in parameters)
            names.Add("");
        scope = new GenericScope(names.MoveToImmutable(), []);
        return true;
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

    readonly object _mapLock = new();

    void EnsureTypeMaps()
    {
        if (_shapes is not null)
            return;
        lock (_mapLock)
        {
            if (_shapes is not null)
                return;
            var shapes = new Dictionary<TypeRef, TypeShape>();
        var enums = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>();
        var enumUnderlyingTypes = new Dictionary<TypeRef, TypeRef>();
        var bases = new Dictionary<TypeRef, TypeRef?>();
        var interfaces = new HashSet<TypeRef>();
        var genericDefinitions = new HashSet<TypeRef>();
        var interfaceImpls = new Dictionary<TypeRef, ImmutableArray<TypeRef>>();
        var unionTypes = new HashSet<TypeRef>();
        var byRefLikeTypes = new HashSet<TypeRef>();
        var delegates = new HashSet<TypeRef>();
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
            var genericParameters = typeDef.GetGenericParameters();
            var scope = new GenericScope(GenericParameterNames(genericParameters), []);
            if (genericParameters.Count > 0)
                genericDefinitions.Add(key);
            bases[key] = DecodeBaseType(typeDef.BaseType, scope);
            if ((typeDef.Attributes & System.Reflection.TypeAttributes.Interface) != 0)
                interfaces.Add(key);
            if (!typeDef.BaseType.IsNil
                && BaseName(typeDef.BaseType) is ("System", "MulticastDelegate") or ("System", "Delegate"))
                delegates.Add(key);
            var impls = DecodeInterfaces(typeDef, scope);
            if (MethodDefinitionFacts.HasUnionAttribute(Reader, typeDef)
                && impls.Any(IsUnionInterface))
                unionTypes.Add(key);
            if (MethodDefinitionFacts.HasByRefLikeAttribute(Reader, typeDef))
                byRefLikeTypes.Add(key);
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
        _genericDefinitions = genericDefinitions;
        _interfaceImpls = interfaceImpls;
        _unionTypes = unionTypes;
        _byRefLikeTypes = byRefLikeTypes;
        _delegates = delegates;
        _shapes = shapes;   // assign last: ResolveShape gates on _shapes
        }
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

    /// <summary>
    /// True when no single value can match both <paramref name="a"/> and
    /// <paramref name="b"/> as a type pattern — the two named class types share no
    /// common instance. A common instance would be a value of the more-derived
    /// type, so overlap requires one type to be an ancestor of the other (a value
    /// of the derived type <c>is</c> both). The proof is only issued when both are
    /// named, non-interface, non-generic definitions whose base chains fully
    /// resolve to <c>System.Object</c>; then the two are disjoint exactly when
    /// neither type appears in the other's (self-inclusive) base chain. Any
    /// interface, generic (open) definition, shape type, or base chain that
    /// cannot be walked to <c>System.Object</c> (e.g. a cross-assembly base this
    /// same-assembly view cannot follow) yields <c>false</c> — <em>cannot prove
    /// disjoint</em>, never a false claim of it — so a caller can treat
    /// <c>true</c> as a hard guarantee. Generic definitions are excluded because a
    /// derived type's generic base appears in the base chain as a closed
    /// generic instance, which never equals the open definition, so ancestry
    /// through a generic supertype cannot be observed here.
    /// </summary>
    public bool AreProvablyDisjoint(TypeRef a, TypeRef b)
    {
        if (a is not { Kind: TypeRefKind.Definition } || b is not { Kind: TypeRefKind.Definition })
            return false;
        // An interface can be implemented by an unrelated class, so a value could
        // satisfy both an interface pattern and any class pattern; never provable.
        if (IsInterface(a) || IsInterface(b))
            return false;
        // A generic definition's derived types reference it through a closed
        // generic-instance base that never equals the open definition, so an
        // ancestor relationship would be silently missed; decline conservatively.
        if (IsGenericDefinition(a) || IsGenericDefinition(b))
            return false;
        if (!TryBaseChainToObject(a, out var chainA) || !TryBaseChainToObject(b, out var chainB))
            return false;
        return !chainA.Contains(b) && !chainB.Contains(a);
    }

    /// <summary>True when <paramref name="type"/> is a generic type definition (arity &gt; 0).</summary>
    bool IsGenericDefinition(TypeRef type)
    {
        EnsureTypeMaps();
        return _genericDefinitions!.Contains(type);
    }

    /// <summary>
    /// Collects <paramref name="type"/> and every base type up to and including
    /// <c>System.Object</c>. Returns <c>false</c> — leaving <paramref name="chain"/>
    /// unusable as a proof — when the walk cannot reach <c>System.Object</c>
    /// (a cross-assembly or otherwise unresolvable base ends
    /// <see cref="ResolveBaseType"/> early) or a cycle appears, so an incomplete
    /// ancestry is never mistaken for a complete one.
    /// </summary>
    bool TryBaseChainToObject(TypeRef type, out HashSet<TypeRef> chain)
    {
        chain = new HashSet<TypeRef>();
        var current = type;
        // Bounded so a malformed same-assembly base cycle cannot spin forever.
        for (int guard = 0; guard < 4096 && current is not null; guard++)
        {
            if (!chain.Add(current))
                return false;
            if (IsObject(current))
                return true;
            current = ResolveBaseType(current);
        }
        return false;
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

        if (TypeDefinitionIdentity.BelongsToAssembly(
            definition,
            Reader.IsAssembly ? TypeRefDecoder.CanonicalSelf(Reader) : "",
            _assembly.Identity))
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
                return GuardedDecode.FieldType(Reader, field, scope);
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
        lock (_crossLock)
        {
            if (_ownsCrossContext)
                _crossContext?.Dispose();
        }
        Pe.Dispose();
        _stream?.Dispose();
    }

    /// <summary>
    /// The associated portable PDB reader — embedded in the PE, or a sidecar
    /// <c>.pdb</c> when the assembly descriptor exposes a path — opened once
    /// and cached. Null when no PDB is found or it cannot be read; the importer
    /// then leaves local names absent and the printer falls back to
    /// <c>V_index</c>.
    /// </summary>
    readonly object _pdbLock = new();
    
    MetadataReader? PdbReader()
    {
        if (_pdbProbed)
            return _pdbReader;
            
        lock (_pdbLock)
        {
            if (_pdbProbed)
                return _pdbReader;

            if (!_readSymbols)
            {
                _pdbProbed = true;
                return null;
            }
        try
        {
            string peImagePath = FilePath ?? Path;
            Func<string, Stream?> pdbStreamProvider =
                FilePath is null
                    ? static _ => null
                    : static path =>
                        File.Exists(path)
                            ? File.OpenRead(path)
                            : null;
            if (Pe.TryOpenAssociatedPortablePdb(
                    peImagePath,
                    pdbStreamProvider,
                    out var provider,
                    out var pdbPath)
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
            // Unreadable PDB (format error, locked file): act like no PDB exists.
        }
        _pdbProbed = true;
        return _pdbReader;
        }
    }

    /// <summary>
    /// Source names and declaration scopes for a method's local slots from its
    /// portable PDB, indexed by IL local slot. No PDB returns two empty arrays.
    /// Present PDB entries with no recorded name, a compiler-generated
    /// (debugger-hidden) local, or a name that is not a usable identifier stay null,
    /// and the printer renders <c>V_index</c>. A slot with no scope entry at all — a
    /// compiler temp the source never declared — keeps a null scope, which is itself
    /// usable evidence that the slot is synthetic.
    /// </summary>
    /// <remarks>
    /// Names and scopes come from the same <c>LocalScope</c> rows, so they are read in
    /// one walk: splitting them would traverse the table twice per method and let the
    /// two views disagree about which entries were skipped.
    /// <para>
    /// The two halves fail differently, so a malformed table is not handled the same
    /// way for both. A missing name degrades visibly to <c>V_index</c> and affects
    /// nothing else, so a partial name array is kept. A scope drives where the printer
    /// puts a declaration, so a <em>partial</em> scope array would make output shape a
    /// function of where the corruption happened to stop. Scopes are therefore dropped
    /// wholesale on a decode failure, which yields exactly the documented no-PDB
    /// behavior: no evidence, no sinking, byte-stable output.
    /// </para>
    /// <para>
    /// This fallback is <b>unverified by test</b>. It is not reachable from any
    /// fixture the repository can build: a truncated or otherwise malformed portable
    /// PDB throws in <c>MetadataReaderProvider.FromPortablePdbStream</c> and is handled
    /// by <see cref="PdbReader"/>'s own catch, which disables symbols entirely, and a
    /// PDB belonging to a different assembly returns empty scopes without throwing
    /// (measured over 18,242 method handles). Reaching this catch needs a PDB that
    /// opens cleanly and then fails mid-walk.
    /// </para>
    /// </remarks>
    internal (ImmutableArray<string?> Names, ImmutableArray<LocalSlotScope?> Scopes) LocalDeclarations(
        MethodDefinitionHandle methodHandle,
        int localCount)
    {
        if (localCount == 0)
            return ([], []);
        var pdb = PdbReader();
        if (pdb is null)
            return ([], []);

        var names = new string?[localCount];
        var scopes = new LocalSlotScope?[localCount];
        bool scopesUsable = true;
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
                    if (variable.Index < 0 || variable.Index >= localCount)
                        continue;
                    names[variable.Index] = pdb.GetString(variable.Name);
                    // A slot listed in more than one scope is malformed or merged
                    // metadata. Keep the narrowest range: it is the weaker claim about
                    // how far the declaration reaches, so it cannot widen a scope.
                    var candidate = new LocalSlotScope(scope.StartOffset, scope.EndOffset);
                    if (scopes[variable.Index] is not { } existing || candidate.Length < existing.Length)
                        scopes[variable.Index] = candidate;
                }
            }
        }
        catch (BadImageFormatException)
        {
            // Malformed scope table. Keep the names read so far, but discard the
            // scopes: a partial set would silently place some declarations from
            // evidence and others from the fallback. Anything other than a decode
            // failure is a bug here and is left to propagate.
            scopesUsable = false;
        }
        return ([.. names], scopesUsable ? [.. scopes] : []);
    }
}
