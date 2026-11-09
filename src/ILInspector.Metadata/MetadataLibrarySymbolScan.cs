using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using CSharpText;

namespace ILInspector.Metadata;

/// <summary>
/// Which declarations a Library symbol scan admits
/// (docs/design/library-symbol-document.md#request-and-admission).
/// </summary>
public enum MetadataLibrarySymbolAdmission
{
    /// <summary>
    /// The API axis: Types reachable from another assembly, and their
    /// public, protected, and protected internal Members, hidden ones
    /// included.
    /// </summary>
    PublicApi,

    /// <summary>Every declaration except the module Type.</summary>
    All,
}

public enum MetadataSymbolDeclarationKind
{
    Definition,
    Forwarder,
}

/// <summary>One parameter of a Member record.</summary>
public sealed record MetadataSymbolParameter(
    string Name,
    string Type,
    ImmutableArray<string> TypeMentions,
    string Modifier,
    bool HasDefaultValue);

/// <summary>
/// One Member record of a Library symbol document. Its display signature,
/// canonical signature, documentation identity, accessibility, and token are
/// the Type document's for the same declaration.
/// </summary>
public sealed record MetadataSymbolMember(
    string DocumentationId,
    string CanonicalSignature,
    string DisplaySignature,
    string Kind,
    string Name,
    string Accessibility,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    bool IsSealed,
    bool IsExtension,
    ImmutableArray<string> GenericParameters,
    string ReturnType,
    ImmutableArray<string> ReturnTypeMentions,
    ImmutableArray<string> ProducedTypes,
    ImmutableArray<MetadataSymbolParameter> Parameters,
    string Overrides,
    ImmutableArray<string> AttributeTypes,
    int MetadataToken);

/// <summary>One Type record of a Library symbol document.</summary>
public sealed record MetadataSymbolType(
    string DocumentationId,
    string Name,
    string MetadataName,
    string Namespace,
    string DeclaringType,
    MetadataSymbolDeclarationKind DeclarationKind,
    string ForwardedToAssembly,
    string DefinitionKind,
    string Accessibility,
    bool VisibleOutsideLibrary,
    bool IsStatic,
    bool IsAbstract,
    bool IsSealed,
    string ConstructorAccessibility,
    ImmutableArray<string> GenericParameters,
    string BaseType,
    ImmutableArray<string> Interfaces,
    ImmutableArray<string> RequiredOverrides,
    int InaccessibleRequiredOverrides,
    bool RequiredOverridesComplete,
    ImmutableArray<string> AttributeTypes,
    int MetadataToken)
{
    internal TypeDefinitionHandle Handle { get; init; }
}

public enum MetadataLibrarySymbolBound
{
    Types,
    TypeForwarders,
    Members,
    RetainedTextCharacters,
}

public abstract record MetadataLibrarySymbolTypesOutcome
{
    private protected MetadataLibrarySymbolTypesOutcome()
    {
    }

    /// <summary>
    /// The admitted Types ordered by documentation identity with ordinal
    /// comparison; Types that share an identity keep metadata order.
    /// </summary>
    public sealed record Available(ImmutableArray<MetadataSymbolType> Types)
        : MetadataLibrarySymbolTypesOutcome;

    public sealed record Incomplete(
        MetadataLibrarySymbolBound Bound,
        long Limit,
        long Measured)
        : MetadataLibrarySymbolTypesOutcome;

    public sealed record Failed(string Detail)
        : MetadataLibrarySymbolTypesOutcome;
}

public abstract record MetadataLibrarySymbolMembersOutcome
{
    private protected MetadataLibrarySymbolMembersOutcome()
    {
    }

    /// <summary>
    /// Every admitted Member of one Type was delivered, in metadata order.
    /// </summary>
    public sealed record Available(int Members)
        : MetadataLibrarySymbolMembersOutcome;

    public sealed record Incomplete(
        MetadataLibrarySymbolBound Bound,
        long Limit,
        long Measured)
        : MetadataLibrarySymbolMembersOutcome;

    public sealed record Failed(string Detail)
        : MetadataLibrarySymbolMembersOutcome;
}

/// <summary>
/// Reads a whole Library's declarations as symbol records in one metadata
/// session (docs/design/library-symbol-document.md#work-and-bounds). Member
/// records come from the same declaration population that issues the Type
/// document, so their spellings and identities are the Type document's.
/// </summary>
public sealed class MetadataLibrarySymbolScan
{
    static readonly string[] s_constructorOrder =
    [
        "public",
        "protected_internal",
        "protected",
        "internal",
        "private_protected",
        "private",
    ];

    readonly MetadataReader _reader;
    readonly Guid _moduleVersionId;
    readonly MetadataLibrarySymbolAdmission _admission;
    readonly ApiSurfaceExtractionBounds _bounds;
    readonly SignatureProvider _signatures;
    readonly Dictionary<string, TypeDefinitionHandle> _definitionsByIdentity =
        new(StringComparer.Ordinal);
    readonly Dictionary<MethodDefinitionHandle, string> _methodIdentities = [];
    readonly Dictionary<TypeDefinitionHandle, MetadataTypeDefinitionName>
        _typeNames = [];
    readonly Dictionary<TypeDefinitionHandle, SortedDictionary<string, bool>>
        _requiredOverrides = [];

    internal MetadataLibrarySymbolScan(
        MetadataReader reader,
        Guid moduleVersionId,
        MetadataLibrarySymbolAdmission admission,
        ApiSurfaceExtractionBounds bounds)
    {
        if (!Enum.IsDefined(admission))
            throw new ArgumentOutOfRangeException(nameof(admission));
        _reader = reader;
        _moduleVersionId = moduleVersionId;
        _admission = admission;
        _bounds = bounds;
        _signatures = new SignatureProvider(reader);
    }

    public MetadataLibrarySymbolAdmission Admission => _admission;

    public MetadataLibrarySymbolTypesOutcome ReadTypes(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Base chains and inherited interfaces resolve against every
            // definition, admitted or not, so index them all first.
            foreach (TypeDefinitionHandle handle in _reader.TypeDefinitions)
                _definitionsByIdentity.TryAdd(TypeIdentity(TypeName(handle)), handle);

            var types = new List<MetadataSymbolType>();
            // The Type records are retained until the Members are written, so
            // their text counts against the retained-text budget.
            long retainedText = 0;
            int forwarders = 0;
            foreach (TypeDefinitionHandle handle in _reader.TypeDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TypeDefinition definition = _reader.GetTypeDefinition(handle);
                MetadataTypeDefinitionName name = TypeName(handle);
                if (name.Namespace.Length == 0
                    && name.Segments is ["<Module>"])
                {
                    continue;
                }
                if (_admission is MetadataLibrarySymbolAdmission.PublicApi
                    && !ApiSurfaceExtractor.AdmitsApiAxisType(
                        _reader,
                        handle,
                        definition))
                {
                    continue;
                }
                if (types.Count >= _bounds.MaxTypes)
                {
                    return new MetadataLibrarySymbolTypesOutcome.Incomplete(
                        MetadataLibrarySymbolBound.Types,
                        _bounds.MaxTypes,
                        types.Count + 1L);
                }
                MetadataSymbolType record = DefinitionRecord(handle, definition, name);
                if (Retain(record, ref retainedText) is { } textBudget)
                    return textBudget;
                types.Add(record);
            }

            foreach (ExportedTypeHandle handle in _reader.ExportedTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExportedType exported = _reader.GetExportedType(handle);
                if (!exported.IsForwarder
                    && exported.Implementation.Kind
                        != HandleKind.ExportedType)
                {
                    continue;
                }
                if (ForwarderRecord(handle) is not { } record)
                    continue;
                if (++forwarders > _bounds.MaxTypeForwarders)
                {
                    return new MetadataLibrarySymbolTypesOutcome.Incomplete(
                        MetadataLibrarySymbolBound.TypeForwarders,
                        _bounds.MaxTypeForwarders,
                        forwarders);
                }
                if (Retain(record, ref retainedText) is { } textBudget)
                    return textBudget;
                if (types.Count >= _bounds.MaxTypes)
                {
                    return new MetadataLibrarySymbolTypesOutcome.Incomplete(
                        MetadataLibrarySymbolBound.Types,
                        _bounds.MaxTypes,
                        types.Count + 1L);
                }
                types.Add(record);
            }

            // A stable sort keeps metadata order among records that share an
            // identity, so the first declaration keeps the `types` entry.
            return new MetadataLibrarySymbolTypesOutcome.Available(
                [
                    .. types.OrderBy(
                        type => type.DocumentationId,
                        StringComparer.Ordinal),
                ]);
        }
        catch (Exception exception) when (
            MetadataTypeMemberCompositionInspection.IsMetadataFailure(
                exception))
        {
            return new MetadataLibrarySymbolTypesOutcome.Failed(
                exception.Message);
        }
    }

    /// <summary>
    /// Delivers each admitted Member record of one Type to
    /// <paramref name="member"/> as it is read, retaining none of them
    /// (#work-and-bounds).
    /// </summary>
    public MetadataLibrarySymbolMembersOutcome ReadMembers(
        MetadataSymbolType type,
        Action<MetadataSymbolMember> member,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        if (type.DeclarationKind is MetadataSymbolDeclarationKind.Forwarder
            || type.Handle.IsNil)
        {
            return new MetadataLibrarySymbolMembersOutcome.Available(0);
        }

        try
        {
            var observer = new MemberObserver(this, type, member);
            MetadataTypeDeclarationPopulationOutcome population =
                MetadataTypeDeclarationPopulationInspection.ReadResolved(
                    _reader,
                    new MetadataTypeDeclarationPopulationRequest(
                        TypeName(type.Handle),
                        MetadataMemberSpelling.CSharp,
                        includeHidden: true,
                        MetadataMethodAccessibilityFilter.All,
                        MetadataTypeMemberGroupReceiverFilter.All),
                    _bounds,
                    new MetadataExactTypeDefinitionResolution.Resolved(
                        type.Handle,
                        _moduleVersionId),
                    cancellationToken,
                    observer);
            return population switch
            {
                MetadataTypeDeclarationPopulationOutcome.Available =>
                    new MetadataLibrarySymbolMembersOutcome.Available(
                        observer.Delivered),
                MetadataTypeDeclarationPopulationOutcome.Incomplete incomplete =>
                    new MetadataLibrarySymbolMembersOutcome.Incomplete(
                        incomplete.Bound
                            is MetadataTypeDeclarationPopulationBound.Members
                            ? MetadataLibrarySymbolBound.Members
                            : MetadataLibrarySymbolBound
                                .RetainedTextCharacters,
                        incomplete.Limit,
                        incomplete.Measured),
                _ => new MetadataLibrarySymbolMembersOutcome.Failed(
                    $"The Members of {type.DocumentationId} could not be read."),
            };
        }
        catch (Exception exception) when (
            MetadataTypeMemberCompositionInspection.IsMetadataFailure(
                exception))
        {
            return new MetadataLibrarySymbolMembersOutcome.Failed(
                exception.Message);
        }
    }

    MetadataLibrarySymbolTypesOutcome.Incomplete? Retain(
        MetadataSymbolType record,
        ref long retainedText)
    {
        retainedText = checked(retainedText + TextLength(record));
        return retainedText > _bounds.MaxRetainedTextCharacters
            ? new MetadataLibrarySymbolTypesOutcome.Incomplete(
                MetadataLibrarySymbolBound.RetainedTextCharacters,
                _bounds.MaxRetainedTextCharacters,
                retainedText)
            : null;
    }

    static long TextLength(MetadataSymbolType record)
    {
        long length = (long)record.DocumentationId.Length
            + record.Name.Length
            + record.MetadataName.Length
            + record.Namespace.Length
            + record.DeclaringType.Length
            + record.ForwardedToAssembly.Length
            + record.DefinitionKind.Length
            + record.Accessibility.Length
            + record.ConstructorAccessibility.Length
            + record.BaseType.Length;
        foreach (ImmutableArray<string> values in new[]
        {
            record.GenericParameters,
            record.Interfaces,
            record.RequiredOverrides,
            record.AttributeTypes,
        })
        {
            foreach (string value in values)
                length += value.Length;
        }
        return length;
    }

    // ---- Type records ----------------------------------------------------

    MetadataSymbolType DefinitionRecord(
        TypeDefinitionHandle handle,
        TypeDefinition definition,
        MetadataTypeDefinitionName name)
    {
        TypeAttributes attributes = definition.Attributes;
        string metadataName = _reader.GetString(definition.Name);
        ImmutableArray<string> genericParameters =
            IntroducedGenericParameters(definition, metadataName);
        TypeDefinitionHandle declaringType = definition.GetDeclaringType();
        Signature? baseSignature = definition.BaseType.IsNil
            ? null
            : _signatures.Decode(definition.BaseType);
        string definitionKind = DefinitionKind(attributes, baseSignature);
        bool isClass = definitionKind == "class";
        bool isAbstract = (attributes & TypeAttributes.Abstract) != 0;
        bool isSealed = (attributes & TypeAttributes.Sealed) != 0;
        SortedDictionary<string, bool> required = RequiredOverrideSet(handle);
        return new MetadataSymbolType(
            TypeIdentity(name),
            genericParameters.IsEmpty
                ? StripArity(metadataName)
                : $"{StripArity(metadataName)}<{string.Join(", ", genericParameters)}>",
            metadataName,
            name.Namespace,
            declaringType.IsNil ? "" : TypeIdentity(TypeName(declaringType)),
            MetadataSymbolDeclarationKind.Definition,
            "",
            definitionKind,
            TypeAccessibility(attributes),
            ApiSurfaceExtractor.AdmitsApiAxisType(_reader, handle, definition),
            IsStatic: isClass && isAbstract && isSealed,
            IsAbstract: isClass && isAbstract && !isSealed,
            IsSealed: isClass && isSealed && !isAbstract,
            ConstructorAccessibility(definition),
            genericParameters,
            baseSignature is { } baseType && Identity(baseType) is { } baseIdentity
                ? baseIdentity
                : "",
            Sorted(
                definition.GetInterfaceImplementations()
                    .Select(implementation => Identity(
                        _signatures.Decode(
                            _reader.GetInterfaceImplementation(implementation)
                                .Interface)))
                    .OfType<string>()),
            [.. required.Keys],
            required.Values.Count(accessible => !accessible),
            RequiredOverridesComplete(handle, definition),
            AttributeTypes(definition.GetCustomAttributes()),
            MetadataTokens.GetToken(handle))
        {
            Handle = handle,
        };
    }

    MetadataSymbolType? ForwarderRecord(ExportedTypeHandle handle)
    {
        var segments = new List<string>();
        ExportedType current = _reader.GetExportedType(handle);
        string assembly;
        int remaining = _reader.GetTableRowCount(TableIndex.ExportedType);
        while (true)
        {
            segments.Insert(0, _reader.GetString(current.Name));
            if (current.Implementation.Kind == HandleKind.ExportedType)
            {
                if (remaining-- == 0)
                {
                    throw new BadImageFormatException(
                        "The exported type graph contains a cycle.");
                }
                current = _reader.GetExportedType(
                    (ExportedTypeHandle)current.Implementation);
                continue;
            }
            if (!current.IsForwarder
                || current.Implementation.Kind
                    != HandleKind.AssemblyReference)
            {
                return null;
            }
            assembly = _reader.GetString(
                _reader.GetAssemblyReference(
                    (AssemblyReferenceHandle)current.Implementation).Name);
            break;
        }

        // Every Type forwarder is admitted (#request-and-admission). Its
        // visibility belongs to the assembly it forwards to: a top-level row
        // records none, and the compiler writes a nested row without one,
        // so an unrecorded visibility reads as public.
        string ns = _reader.GetString(current.Namespace);
        string metadataName = segments[^1];
        ExportedType own = _reader.GetExportedType(handle);
        string declaringType = segments.Count == 1
            ? ""
            : $"T:{XmlDocumentationNotation.FormatDefinitionName(ns, segments[..^1])}";
        return new MetadataSymbolType(
            $"T:{XmlDocumentationNotation.FormatDefinitionName(ns, segments)}",
            // A forwarder carries no generic parameter names, so its name is
            // its metadata name.
            metadataName,
            metadataName,
            ns,
            declaringType,
            MetadataSymbolDeclarationKind.Forwarder,
            assembly,
            "",
            ForwarderAccessibility(own.Attributes, nested: segments.Count > 1),
            VisibleOutsideLibrary: true,
            IsStatic: false,
            IsAbstract: false,
            IsSealed: false,
            "",
            [],
            "",
            [],
            [],
            0,
            RequiredOverridesComplete: true,
            [],
            MetadataTokens.GetToken(handle));
    }

    static string ForwarderAccessibility(TypeAttributes attributes, bool nested) =>
        !nested || (attributes & TypeAttributes.VisibilityMask) == 0
            ? "public"
            : TypeAccessibility(attributes);

    ImmutableArray<string> IntroducedGenericParameters(
        TypeDefinition definition,
        string metadataName)
    {
        int tick = metadataName.LastIndexOf('`');
        if (tick < 0
            || !int.TryParse(
                metadataName.AsSpan(tick + 1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int introduced))
        {
            return [];
        }

        GenericParameterHandleCollection parameters =
            definition.GetGenericParameters();
        introduced = Math.Min(introduced, parameters.Count);
        return
        [
            .. parameters
                .Skip(parameters.Count - introduced)
                .Select(parameter => _reader.GetString(
                    _reader.GetGenericParameter(parameter).Name)),
        ];
    }

    static string StripArity(string metadataName)
    {
        int tick = metadataName.LastIndexOf('`');
        return tick > 0 ? metadataName[..tick] : metadataName;
    }

    static string DefinitionKind(TypeAttributes attributes, Signature? baseType)
    {
        if ((attributes & TypeAttributes.Interface) != 0)
            return "interface";
        return baseType switch
        {
            Signature.Named { Namespace: "System", Segments: ["Enum"] } =>
                "enum",
            Signature.Named { Namespace: "System", Segments: ["ValueType"] } =>
                "struct",
            Signature.Named { Namespace: "System", Segments: ["MulticastDelegate"] } =>
                "delegate",
            _ => "class",
        };
    }

    static string TypeAccessibility(TypeAttributes attributes) =>
        (attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public or TypeAttributes.NestedPublic => "public",
            TypeAttributes.NestedFamily => "protected",
            TypeAttributes.NestedFamORAssem => "protected_internal",
            TypeAttributes.NestedFamANDAssem => "private_protected",
            TypeAttributes.NestedPrivate => "private",
            _ => "internal",
        };

    static string MemberAccessibility(MethodAttributes access) =>
        (access & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => "public",
            MethodAttributes.FamORAssem => "protected_internal",
            MethodAttributes.Family => "protected",
            MethodAttributes.Assembly => "internal",
            MethodAttributes.FamANDAssem => "private_protected",
            _ => "private",
        };

    string ConstructorAccessibility(TypeDefinition definition)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (MethodDefinitionHandle handle in definition.GetMethods())
        {
            MethodDefinition method = _reader.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.Static) == 0
                && _reader.StringComparer.Equals(method.Name, ".ctor"))
            {
                found.Add(MemberAccessibility(method.Attributes));
            }
        }
        return s_constructorOrder.FirstOrDefault(found.Contains) ?? "";
    }

    ImmutableArray<string> AttributeTypes(
        CustomAttributeHandleCollection attributes)
    {
        var identities = new SortedSet<string>(StringComparer.Ordinal);
        AddAttributeTypes(attributes, identities);
        return [.. identities];
    }

    void AddAttributeTypes(
        CustomAttributeHandleCollection attributes,
        SortedSet<string> into)
    {
        foreach (CustomAttributeHandle handle in attributes)
        {
            string? name = AttributeReader.GetAttributeTypeName(
                _reader,
                _reader.GetCustomAttribute(handle).Constructor);
            if (name is null || AttributeReader.IsReEmittedAttribute(name))
                continue;
            into.Add($"T:{XmlDocumentationNotation.NormalizeTypeName(name)}");
        }
    }

    // ---- Inheritance -----------------------------------------------------

    (TypeDefinitionHandle Handle, ImmutableArray<Signature> Arguments)? BaseOf(
        TypeDefinitionHandle handle)
    {
        TypeDefinition definition = _reader.GetTypeDefinition(handle);
        if (definition.BaseType.IsNil
            || _signatures.Decode(definition.BaseType)
                is not Signature.Named { IsLocal: true } named
            || !_definitionsByIdentity.TryGetValue(
                TypeIdentity(named.Namespace, named.Segments),
                out TypeDefinitionHandle baseHandle))
        {
            return null;
        }
        return (baseHandle, named.Arguments);
    }

    SortedDictionary<string, bool> RequiredOverrideSet(
        TypeDefinitionHandle handle)
    {
        if (_requiredOverrides.TryGetValue(handle, out var cached))
            return cached;
        var computed = ComputeRequiredOverrides(handle);
        _requiredOverrides[handle] = computed;
        return computed;
    }

    // Every abstract Member a concrete subclass or implementer must provide,
    // accessible or not, once the base chain in this Library has implemented
    // what it implements (#inheritance). The value says whether a class in
    // another Library can override it.
    SortedDictionary<string, bool> ComputeRequiredOverrides(
        TypeDefinitionHandle handle)
    {
        var pending = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        TypeDefinition definition = _reader.GetTypeDefinition(handle);
        bool isInterface =
            (definition.Attributes & TypeAttributes.Interface) != 0;
        if (!isInterface
            && ((definition.Attributes & TypeAttributes.Abstract) == 0
                || (definition.Attributes & TypeAttributes.Sealed) != 0))
        {
            return pending;
        }

        if (isInterface)
            return InterfaceObligations(handle);

        var chain = new List<TypeDefinitionHandle> { handle };
        int remaining = _reader.GetTableRowCount(TableIndex.TypeDef);
        while (BaseOf(chain[^1]) is var (baseHandle, _))
        {
            if (remaining-- == 0 || chain.Contains(baseHandle))
                break;
            chain.Add(baseHandle);
        }

        // Obligations are tracked per method, so a property whose getter and
        // setter differ in access, or are overridden separately, keeps each
        // accessor's obligation; they are listed under their owner at the end.
        var outstanding = new Dictionary<MethodDefinitionHandle, bool>();
        foreach (TypeDefinitionHandle current in Enumerable.Reverse(chain))
        {
            foreach (MethodDefinitionHandle methodHandle
                in _reader.GetTypeDefinition(current).GetMethods())
            {
                MethodAttributes attributes =
                    _reader.GetMethodDefinition(methodHandle).Attributes;
                if ((attributes & MethodAttributes.Static) != 0)
                    continue;
                if (OverriddenMethod(methodHandle) is { } overridden)
                    outstanding.Remove(overridden);
                if ((attributes & MethodAttributes.Abstract) != 0)
                {
                    outstanding[methodHandle] = Overridable(attributes);
                }
            }
        }

        // An obligation is overridable from another Library only when every
        // outstanding accessor it lists is.
        foreach ((MethodDefinitionHandle method, bool accessible) in outstanding)
        {
            string identity = ObligationIdentity(method);
            pending[identity] = pending.TryGetValue(identity, out bool listed)
                ? listed && accessible
                : accessible;
        }
        return pending;
    }

    static bool Overridable(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask)
            is MethodAttributes.Public
            or MethodAttributes.Family
            or MethodAttributes.FamORAssem;

    bool RequiredOverridesComplete(
        TypeDefinitionHandle handle,
        TypeDefinition definition)
    {
        if ((definition.Attributes & TypeAttributes.Interface) != 0)
        {
            InterfaceClosure(handle, out bool leavesLibrary);
            return !leavesLibrary;
        }
        if ((definition.Attributes & TypeAttributes.Abstract) == 0
            || (definition.Attributes & TypeAttributes.Sealed) != 0)
        {
            return true;
        }

        TypeDefinitionHandle current = handle;
        int remaining = _reader.GetTableRowCount(TableIndex.TypeDef);
        while (BaseOf(current) is var (baseHandle, _) && remaining-- > 0)
            current = baseHandle;
        TypeDefinition last = _reader.GetTypeDefinition(current);
        return last.BaseType.IsNil
            || _signatures.Decode(last.BaseType)
                is Signature.Named { Namespace: "System", Segments: ["Object"] };
    }

    List<TypeDefinitionHandle> InterfaceClosure(
        TypeDefinitionHandle handle,
        out bool leavesLibrary)
    {
        leavesLibrary = false;
        var seen = new List<TypeDefinitionHandle> { handle };
        for (int index = 0; index < seen.Count; index++)
        {
            foreach (InterfaceImplementationHandle implementation
                in _reader.GetTypeDefinition(seen[index])
                    .GetInterfaceImplementations())
            {
                if (_signatures.Decode(
                        _reader.GetInterfaceImplementation(implementation)
                            .Interface)
                        is Signature.Named { IsLocal: true } named
                    && _definitionsByIdentity.TryGetValue(
                        TypeIdentity(named.Namespace, named.Segments),
                        out TypeDefinitionHandle inherited))
                {
                    if (!seen.Contains(inherited))
                        seen.Add(inherited);
                }
                else
                {
                    leavesLibrary = true;
                }
            }
        }
        return seen;
    }

    // The base-class method a method overrides, found along the base chain in
    // this Library by name, generic arity, and parameter types after the
    // base's type arguments are substituted.
    MethodDefinitionHandle? OverriddenMethod(MethodDefinitionHandle handle)
    {
        if (ExplicitOverride(handle) is { Local: { } mapped })
            return mapped;
        MethodDefinition method = _reader.GetMethodDefinition(handle);
        if ((method.Attributes & MethodAttributes.Virtual) == 0
            || (method.Attributes & MethodAttributes.NewSlot) != 0)
        {
            return null;
        }

        int arity = method.GetGenericParameters().Count;
        ImmutableArray<Signature> wanted = DecodeMethod(method).ParameterTypes;
        TypeDefinitionHandle current = method.GetDeclaringType();
        ImmutableArray<Signature>? substitution = null;
        int remaining = _reader.GetTableRowCount(TableIndex.TypeDef);
        while (remaining-- > 0
            && BaseOf(current) is var (baseHandle, arguments))
        {
            ImmutableArray<Signature> inDerived = substitution is { } outer
                ? [.. arguments.Select(argument => Substitute(argument, outer))]
                : arguments;
            foreach (MethodDefinitionHandle candidateHandle
                in _reader.GetTypeDefinition(baseHandle).GetMethods())
            {
                MethodDefinition candidate =
                    _reader.GetMethodDefinition(candidateHandle);
                if ((candidate.Attributes & MethodAttributes.Virtual) == 0
                    || !_reader.StringComparer.Equals(
                        candidate.Name,
                        _reader.GetString(method.Name))
                    || candidate.GetGenericParameters().Count != arity)
                {
                    continue;
                }
                ImmutableArray<Signature> parameters = DecodeMethod(candidate).ParameterTypes;
                if (parameters.Length == wanted.Length
                    && parameters
                        .Select(parameter => Substitute(parameter, inDerived))
                        .SequenceEqual(wanted))
                {
                    return candidateHandle;
                }
            }
            current = baseHandle;
            substitution = inDerived;
        }
        return null;
    }

    /// <summary>
    /// The class Member a method overrides through an explicit MethodImpl
    /// mapping, as a covariant return override is emitted: <c>Found</c> when
    /// the mapping names a class Member, and <c>Local</c> when that Member is
    /// declared in this Library. Interface mappings are not overrides.
    /// </summary>
    (bool Found, MethodDefinitionHandle? Local) ExplicitOverride(
        MethodDefinitionHandle handle)
    {
        TypeDefinition owner = _reader.GetTypeDefinition(
            _reader.GetMethodDefinition(handle).GetDeclaringType());
        foreach (MethodImplementationHandle implementationHandle
            in owner.GetMethodImplementations())
        {
            MethodImplementation implementation =
                _reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody != (EntityHandle)handle)
                continue;
            switch (implementation.MethodDeclaration.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var declared = (MethodDefinitionHandle)implementation.MethodDeclaration;
                    TypeDefinition declaring = _reader.GetTypeDefinition(
                        _reader.GetMethodDefinition(declared).GetDeclaringType());
                    if ((declaring.Attributes & TypeAttributes.Interface) == 0)
                        return (true, declared);
                    break;
                }
                case HandleKind.MemberReference:
                {
                    MemberReference reference = _reader.GetMemberReference(
                        (MemberReferenceHandle)implementation.MethodDeclaration);
                    if (_signatures.Decode(reference.Parent) is not Signature.Named parent)
                        break;
                    if (!parent.IsLocal
                        || !_definitionsByIdentity.TryGetValue(
                            TypeIdentity(parent.Namespace, parent.Segments),
                            out TypeDefinitionHandle parentHandle))
                    {
                        // A Member of another Library: an override whose
                        // target lies outside the file, unless it is an
                        // interface, which metadata here cannot tell.
                        return (true, null);
                    }
                    TypeDefinition parentType = _reader.GetTypeDefinition(parentHandle);
                    if ((parentType.Attributes & TypeAttributes.Interface) != 0)
                        break;
                    return (true, ReferencedMethod(reference, parentType));
                }
            }
        }
        return (false, null);
    }

    /// <summary>
    /// The method of <paramref name="parentType"/> a MemberRef names, matched
    /// by name, generic arity, and structural parameter Types; both sides are
    /// written in the parent's own generic parameters.
    /// </summary>
    MethodDefinitionHandle? ReferencedMethod(
        MemberReference reference,
        TypeDefinition parentType)
    {
        MethodSignature<Signature> wanted = GuardedProviderDecode.MemberRefMethod(
            _reader,
            reference,
            _signatures,
            null,
            (Signature)new Signature.Opaque());
        foreach (MethodDefinitionHandle candidateHandle in parentType.GetMethods())
        {
            MethodDefinition candidate = _reader.GetMethodDefinition(candidateHandle);
            if (!_reader.StringComparer.Equals(candidate.Name, _reader.GetString(reference.Name))
                || candidate.GetGenericParameters().Count != wanted.GenericParameterCount)
            {
                continue;
            }
            if (DecodeMethod(candidate).ParameterTypes.SequenceEqual(wanted.ParameterTypes))
                return candidateHandle;
        }
        return null;
    }

    /// <summary>
    /// An interface's obligations (#inheritance), resolved as the runtime
    /// resolves interface slots. A slot is a method declared by a
    /// constructed interface in the closure, so <c>IGeneric&lt;int&gt;.M</c>
    /// and <c>IGeneric&lt;string&gt;.M</c> are different slots. Each
    /// interface's own default body, and each explicit MethodImpl body or
    /// reabstraction, implements a slot at that interface; the most specific
    /// implementation, the one no other implementing interface derives from,
    /// decides it. A slot whose most specific implementation is abstract, or
    /// which has no single most specific implementation, is outstanding,
    /// listed under its declaration with the declaration's access.
    /// </summary>
    SortedDictionary<string, bool> InterfaceObligations(TypeDefinitionHandle handle)
    {
        var pending = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        Signature.Named root = SelfSignature(handle);
        var definitions = new Dictionary<Signature.Named, TypeDefinitionHandle> { [root] = handle };
        var parents = new Dictionary<Signature.Named, List<Signature.Named>>();
        var closure = new List<Signature.Named> { root };
        for (int index = 0;
            index < closure.Count && index < MetadataSafetyPolicy.MaxRelationshipNodes;
            index++)
        {
            Signature.Named node = closure[index];
            var direct = new List<Signature.Named>();
            parents[node] = direct;
            foreach (InterfaceImplementationHandle implementation
                in _reader.GetTypeDefinition(definitions[node]).GetInterfaceImplementations())
            {
                if (_signatures.Decode(_reader.GetInterfaceImplementation(implementation).Interface)
                        is not Signature.Named { IsLocal: true } named
                    || !_definitionsByIdentity.TryGetValue(
                        TypeIdentity(named.Namespace, named.Segments),
                        out TypeDefinitionHandle inherited)
                    || Substitute(named, node.Arguments) is not Signature.Named constructed)
                {
                    continue;
                }
                direct.Add(constructed);
                if (definitions.TryAdd(constructed, inherited))
                    closure.Add(constructed);
            }
        }

        var ancestors = new Dictionary<Signature.Named, HashSet<Signature.Named>>();
        HashSet<Signature.Named> AncestorsOf(Signature.Named node)
        {
            if (ancestors.TryGetValue(node, out HashSet<Signature.Named>? known))
                return known;
            var found = new HashSet<Signature.Named>();
            var queue = new Queue<Signature.Named>(parents.GetValueOrDefault(node) ?? []);
            while (queue.Count > 0 && found.Count < MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                Signature.Named next = queue.Dequeue();
                if (found.Add(next))
                {
                    foreach (Signature.Named parent in parents.GetValueOrDefault(next) ?? [])
                        queue.Enqueue(parent);
                }
            }
            ancestors[node] = found;
            return found;
        }

        // Every slot, with each implementation and whether it is abstract.
        var slots = new Dictionary<(Signature.Named Interface, MethodDefinitionHandle Method), List<(Signature.Named At, bool Abstract)>>();
        void Implement(Signature.Named slotInterface, MethodDefinitionHandle method, Signature.Named at, bool isAbstract)
        {
            if (!definitions.ContainsKey(slotInterface))
                return;
            if (!slots.TryGetValue((slotInterface, method), out var implementations))
            {
                implementations = [];
                slots[(slotInterface, method)] = implementations;
            }
            implementations.Add((at, isAbstract));
        }
        foreach (Signature.Named node in closure)
        {
            TypeDefinition definition = _reader.GetTypeDefinition(definitions[node]);
            var bodies = new HashSet<MethodDefinitionHandle>();
            foreach (MethodImplementationHandle implementationHandle in definition.GetMethodImplementations())
            {
                MethodImplementation implementation = _reader.GetMethodImplementation(implementationHandle);
                if (implementation.MethodBody.Kind != HandleKind.MethodDefinition
                    || ExplicitInterfaceTarget(implementation.MethodDeclaration)
                        is not var (targetInterface, targetMethod))
                {
                    continue;
                }
                var body = (MethodDefinitionHandle)implementation.MethodBody;
                bodies.Add(body);
                // Obligations are instance Members (#inheritance): a static
                // abstract slot or its reabstraction is not one.
                if ((_reader.GetMethodDefinition(body).Attributes & MethodAttributes.Static) != 0
                    || (_reader.GetMethodDefinition(targetMethod).Attributes & MethodAttributes.Static) != 0)
                {
                    continue;
                }
                Implement(
                    (Signature.Named)Substitute(targetInterface, node.Arguments),
                    targetMethod,
                    node,
                    (_reader.GetMethodDefinition(body).Attributes & MethodAttributes.Abstract) != 0);
            }
            foreach (MethodDefinitionHandle method in definition.GetMethods())
            {
                MethodAttributes attributes = _reader.GetMethodDefinition(method).Attributes;
                if ((attributes & (MethodAttributes.Static | MethodAttributes.Virtual)) != MethodAttributes.Virtual
                    || bodies.Contains(method))
                {
                    continue;
                }
                Implement(node, method, node, (attributes & MethodAttributes.Abstract) != 0);
            }
        }

        foreach (((Signature.Named _, MethodDefinitionHandle method), var implementations) in slots)
        {
            Signature.Named[] mostSpecific =
            [
                .. implementations
                    .Select(implementation => implementation.At)
                    .Distinct()
                    .Where(at => !implementations.Any(other =>
                        !other.At.Equals(at) && AncestorsOf(other.At).Contains(at))),
            ];
            bool outstanding = mostSpecific.Length != 1
                || implementations.Any(implementation =>
                    implementation.At.Equals(mostSpecific[0]) && implementation.Abstract);
            if (!outstanding)
                continue;
            string identity = ObligationIdentity(method);
            bool accessible = Overridable(_reader.GetMethodDefinition(method).Attributes);
            pending[identity] = pending.TryGetValue(identity, out bool listed)
                ? listed && accessible
                : accessible;
        }
        return pending;
    }

    /// <summary>
    /// The constructed interface and method of this Library an explicit
    /// MethodImpl declaration names, written in the declaring Type's own
    /// generic parameters.
    /// </summary>
    (Signature.Named Interface, MethodDefinitionHandle Method)? ExplicitInterfaceTarget(
        EntityHandle declaration)
    {
        switch (declaration.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var method = (MethodDefinitionHandle)declaration;
                TypeDefinitionHandle owner = _reader.GetMethodDefinition(method).GetDeclaringType();
                return (_reader.GetTypeDefinition(owner).Attributes & TypeAttributes.Interface) != 0
                    ? (SelfSignature(owner), method)
                    : null;
            }
            case HandleKind.MemberReference:
            {
                MemberReference reference = _reader.GetMemberReference((MemberReferenceHandle)declaration);
                if (_signatures.Decode(reference.Parent) is not Signature.Named { IsLocal: true } parent
                    || !_definitionsByIdentity.TryGetValue(
                        TypeIdentity(parent.Namespace, parent.Segments),
                        out TypeDefinitionHandle parentHandle))
                {
                    return null;
                }
                TypeDefinition parentType = _reader.GetTypeDefinition(parentHandle);
                return (parentType.Attributes & TypeAttributes.Interface) != 0
                    && ReferencedMethod(reference, parentType) is { } method
                    ? (parent, method)
                    : null;
            }
            default:
                return null;
        }
    }

    /// <summary>A Type in its own generic parameters: <c>IGeneric&lt;!0&gt;</c>.</summary>
    Signature.Named SelfSignature(TypeDefinitionHandle handle)
    {
        MetadataTypeDefinitionName name = TypeName(handle);
        int arity = _reader.GetTypeDefinition(handle).GetGenericParameters().Count;
        return new Signature.Named(
            name.Namespace,
            name.Segments,
            [.. Enumerable.Range(0, arity).Select(index => (Signature)new Signature.TypeParameter(index, IsMethod: false))],
            IsLocal: true,
            Scope: "");
    }

    MethodSignature<Signature> DecodeMethod(MethodDefinition method) =>
        GuardedProviderDecode.Method(
            _reader,
            method,
            _signatures,
            null,
            (Signature)new Signature.Opaque());

    static Signature Substitute(
        Signature signature,
        ImmutableArray<Signature> arguments) => signature switch
        {
            Signature.TypeParameter { IsMethod: false } parameter
                when parameter.Index < arguments.Length =>
                arguments[parameter.Index],
            Signature.Named named => named with
            {
                Arguments =
                [
                    .. named.Arguments.Select(
                        argument => Substitute(argument, arguments)),
                ],
            },
            Signature.Element element => element with
            {
                Inner = Substitute(element.Inner, arguments),
            },
            Signature.Modified modified => modified with
            {
                Inner = Substitute(modified.Inner, arguments),
            },
            Signature.FunctionPointer pointer => pointer with
            {
                Return = Substitute(pointer.Return, arguments),
                Parameters =
                [
                    .. pointer.Parameters.Select(
                        parameter => Substitute(parameter, arguments)),
                ],
            },
            _ => signature,
        };

    /// <summary>
    /// The identity an obligation is listed under: a property or event
    /// accessor is listed as its property or event.
    /// </summary>
    string ObligationIdentity(MethodDefinitionHandle method) =>
        AccessorOwnerIdentity(method) ?? MethodIdentity(method);

    string? AccessorOwnerIdentity(MethodDefinitionHandle accessor)
    {
        TypeDefinitionHandle typeHandle =
            _reader.GetMethodDefinition(accessor).GetDeclaringType();
        TypeDefinition type = _reader.GetTypeDefinition(typeHandle);
        foreach (PropertyDefinitionHandle propertyHandle in type.GetProperties())
        {
            PropertyDefinition property =
                _reader.GetPropertyDefinition(propertyHandle);
            PropertyAccessors accessors = property.GetAccessors();
            if (accessors.Getter == accessor || accessors.Setter == accessor)
                return PropertyIdentity(typeHandle, type, property);
        }
        foreach (EventDefinitionHandle eventHandle in type.GetEvents())
        {
            EventDefinition definition = _reader.GetEventDefinition(eventHandle);
            EventAccessors accessors = definition.GetAccessors();
            if (accessors.Adder == accessor
                || accessors.Remover == accessor
                || accessors.Raiser == accessor)
            {
                return EventIdentity(typeHandle, definition);
            }
        }
        return null;
    }

    string MethodIdentity(MethodDefinitionHandle handle)
    {
        if (_methodIdentities.TryGetValue(handle, out string? cached))
            return cached;
        MethodDefinition method = _reader.GetMethodDefinition(handle);
        TypeDefinitionHandle typeHandle = method.GetDeclaringType();
        TypeDefinition type = _reader.GetTypeDefinition(typeHandle);
        MetadataMethodDeclaration declaration =
            MetadataDeclarationQuery.GetMethod(_reader, type, method);
        ApiSignature model = ApiSurfaceExtractor.GetMethodSignatureForIdentity(
            _reader,
            GenericContext.ForType(_reader, type),
            handle,
            method,
            typeNullableContext: 0).Model;
        string identity = MetadataTypeDeclarationPopulationInspection
            .MethodDocumentationId(
                TypeName(typeHandle),
                declaration.MetadataName,
                _reader.GetString(method.Name),
                model);
        _methodIdentities[handle] = identity;
        return identity;
    }

    string PropertyIdentity(
        TypeDefinitionHandle typeHandle,
        TypeDefinition type,
        PropertyDefinition property)
    {
        MetadataPropertyDeclaration declaration =
            MetadataDeclarationQuery.GetProperty(_reader, type, property);
        ApiSignature model = ApiSurfaceExtractor.GetPropertySignatureForIdentity(
            _reader,
            GenericContext.ForType(_reader, type),
            property,
            property.GetAccessors(),
            ApiSurfaceExtractor.GetExplicitImplementationBodies(_reader, type),
            typeNullableContext: 0).Model;
        return MetadataTypeDeclarationPopulationInspection
            .PropertyDocumentationId(
                TypeName(typeHandle),
                declaration.MetadataName,
                model);
    }

    string EventIdentity(
        TypeDefinitionHandle typeHandle,
        EventDefinition definition)
    {
        MetadataTypeDefinitionName type = TypeName(typeHandle);
        return XmlDocumentationNotation.CreateMemberIdentity(
            "E",
            type.Namespace,
            type.Segments,
            _reader.GetString(definition.Name),
            [],
            methodGenericArity: 0,
            conversionReturnType: null,
            isVararg: false).Value;
    }

    // ---- Identity helpers --------------------------------------------------

    MetadataTypeDefinitionName TypeName(TypeDefinitionHandle handle)
    {
        if (!_typeNames.TryGetValue(handle, out MetadataTypeDefinitionName? name))
        {
            name = MetadataDeclarationQuery.GetTypeDefinitionName(_reader, handle);
            _typeNames[handle] = name;
        }
        return name;
    }

    static string TypeIdentity(MetadataTypeDefinitionName name) =>
        TypeIdentity(name.Namespace, name.Segments);

    static string TypeIdentity(string ns, IReadOnlyList<string> segments) =>
        XmlDocumentationNotation.CreateTypeIdentity(ns, segments).Value;

    static string? Identity(Signature signature) => signature switch
    {
        Signature.Named named => TypeIdentity(named.Namespace, named.Segments),
        Signature.Primitive primitive => primitive.Identity,
        _ => null,
    };

    static ImmutableArray<string> Sorted(IEnumerable<string> identities) =>
        [.. identities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    internal static string TypeIdentity(ApiTypeReferenceIdentity reference) =>
        reference.DefinitionName is { } definition
            ? TypeIdentity(definition)
            : $"T:{XmlDocumentationNotation.NormalizeTypeName(reference.FullName)}";

    // ---- Member records ----------------------------------------------------

    sealed class MemberObserver(
        MetadataLibrarySymbolScan scan,
        MetadataSymbolType type,
        Action<MetadataSymbolMember> deliver)
        : IMetadataTypeMemberDeclarationObserver
    {
        public int Delivered { get; private set; }

        public void Observe(
            in ClassifiedMember member,
            MetadataTypeMemberDeclaration declaration,
            ApiSignature model)
        {
            if (!ApiSurfaceExtractor.AdmitsMethodAccess(
                    member.Access,
                    includeAll:
                        scan._admission is MetadataLibrarySymbolAdmission.All,
                    apiAxis: true))
            {
                return;
            }
            deliver(scan.MemberRecord(member, declaration, model, type));
            Delivered++;
        }
    }

    MetadataSymbolMember MemberRecord(
        in ClassifiedMember member,
        MetadataTypeMemberDeclaration declaration,
        ApiSignature model,
        MetadataSymbolType type)
    {
        var attributes = new SortedSet<string>(StringComparer.Ordinal);
        ApiMethodModifiers modifiers = default;
        MethodAttributes modifierSource = 0;
        MethodDefinitionHandle overrideSource = default;
        string overrides = "";
        bool producesReturn = true;
        switch (member.Handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var handle = (MethodDefinitionHandle)member.Handle;
                MethodDefinition method = _reader.GetMethodDefinition(handle);
                modifierSource = method.Attributes;
                overrideSource = handle;
                modifiers = ApiMethodModifiers.FromAttributes(
                    method.Attributes,
                    member.IsExplicitInterfaceImplementation);
                AddAttributeTypes(method.GetCustomAttributes(), attributes);
                if (OverriddenMethod(handle) is { } overridden)
                    overrides = MethodIdentity(overridden);
                break;
            }
            case HandleKind.PropertyDefinition:
            {
                PropertyDefinition property = _reader.GetPropertyDefinition(
                    (PropertyDefinitionHandle)member.Handle);
                PropertyAccessors accessors = property.GetAccessors();
                AddAttributeTypes(property.GetCustomAttributes(), attributes);
                MethodDefinitionHandle principal = accessors.Getter.IsNil
                    ? accessors.Setter
                    : accessors.Getter;
                foreach (MethodDefinitionHandle accessor
                    in new[] { accessors.Getter, accessors.Setter })
                {
                    if (accessor.IsNil)
                        continue;
                    AddAttributeTypes(
                        _reader.GetMethodDefinition(accessor).GetCustomAttributes(),
                        attributes);
                    if (overrides.Length == 0
                        && OverriddenMethod(accessor) is { } overridden
                        && AccessorOwnerIdentity(overridden) is { } owner)
                    {
                        overrides = owner;
                    }
                }
                if (!principal.IsNil)
                {
                    modifierSource = _reader.GetMethodDefinition(principal).Attributes;
                    overrideSource = principal;
                    modifiers = ApiMethodModifiers.FromAttributes(
                        modifierSource,
                        member.IsExplicitInterfaceImplementation);
                }
                // A property yields its type only to a caller whose access the
                // admission admits for its getter.
                producesReturn = !accessors.Getter.IsNil
                    && ApiSurfaceExtractor.AdmitsMethodAccess(
                        _reader.GetMethodDefinition(accessors.Getter).Attributes
                            & MethodAttributes.MemberAccessMask,
                        includeAll: _admission is MetadataLibrarySymbolAdmission.All,
                        apiAxis: true);
                break;
            }
            case HandleKind.EventDefinition:
            {
                EventDefinition definition = _reader.GetEventDefinition(
                    (EventDefinitionHandle)member.Handle);
                EventAccessors accessors = definition.GetAccessors();
                AddAttributeTypes(definition.GetCustomAttributes(), attributes);
                foreach (MethodDefinitionHandle accessor
                    in new[] { accessors.Adder, accessors.Remover })
                {
                    if (accessor.IsNil)
                        continue;
                    AddAttributeTypes(
                        _reader.GetMethodDefinition(accessor).GetCustomAttributes(),
                        attributes);
                    if (overrides.Length == 0
                        && OverriddenMethod(accessor) is { } overridden
                        && AccessorOwnerIdentity(overridden) is { } owner)
                    {
                        overrides = owner;
                    }
                }
                if (!accessors.Adder.IsNil)
                {
                    modifierSource = _reader.GetMethodDefinition(accessors.Adder).Attributes;
                    overrideSource = accessors.Adder;
                    modifiers = ApiMethodModifiers.FromAttributes(
                        modifierSource,
                        member.IsExplicitInterfaceImplementation);
                }
                producesReturn = false;
                break;
            }
            case HandleKind.FieldDefinition:
            {
                FieldDefinition field = _reader.GetFieldDefinition(
                    (FieldDefinitionHandle)member.Handle);
                AddAttributeTypes(field.GetCustomAttributes(), attributes);
                modifiers = new ApiMethodModifiers(
                    (field.Attributes & FieldAttributes.Static) != 0,
                    IsVirtual: false,
                    IsAbstract: false,
                    IsOverride: false,
                    IsSealed: false);
                break;
            }
        }

        // A covariant return override is emitted newslot with an explicit
        // MethodImpl, yet C# declares it override.
        // A static virtual or abstract interface Member occupies a slot
        // without overriding anything.
        bool isOverride = !modifiers.IsStatic
            && (modifiers.IsOverride
                || !member.IsExplicitInterfaceImplementation
                    && overrideSource is { IsNil: false } source
                    && ExplicitOverride(source).Found);
        bool isConstructor =
            declaration.Category is MetadataTypeMemberGroupCategory.Constructor;
        bool isExtension = member.Receiver is MetadataMethodReceiver.Extension;

        // Return and parameter shapes, with by-ref wrappers and custom
        // modifiers removed, are decoded here by the batch alone rather than
        // retained for every signature every API surface caller decodes.
        (TypeNode? returnNode, ImmutableArray<TypeNode> parameterTypes) =
            StructuralSignature(member.Handle);
        ApiTypeShape? returnShape = returnNode is null
            ? model.ReturnTypeShape
            : ApiTypeShapeFactory.FromParameterTypeNode(returnNode);
        var produced = new SortedSet<string>(StringComparer.Ordinal);
        if (isConstructor)
        {
            produced.Add(type.DocumentationId);
        }
        else if (producesReturn)
        {
            AddProduced(returnShape, produced);
        }
        bool isClassicExtension = member.Handle.Kind == HandleKind.MethodDefinition
            && AttributeReader.HasExtensionAttribute(
                _reader,
                _reader.GetMethodDefinition((MethodDefinitionHandle)member.Handle)
                    .GetCustomAttributes());
        var parameters = ImmutableArray.CreateBuilder<MetadataSymbolParameter>(
            model.Parameters.Count);
        for (int index = 0; index < model.Parameters.Count; index++)
        {
            ApiParameter parameter = model.Parameters[index];
            string modifier = parameter.Modifier ?? "";
            // A classic extension method's receiver is the first parameter
            // of a method that carries ExtensionAttribute itself.
            if (modifier.Length == 0 && index == 0 && isClassicExtension)
            {
                modifier = "this";
            }
            ApiTypeShape? shape = index < parameterTypes.Length
                ? ApiTypeShapeFactory.FromParameterTypeNode(parameterTypes[index])
                : null;
            if (modifier == "out")
                AddProduced(shape, produced);
            parameters.Add(new MetadataSymbolParameter(
                parameter.Name,
                parameter.Type,
                Mentions(
                    parameter.TypeReferences,
                    index < parameterTypes.Length ? parameterTypes[index] : null,
                    shape),
                modifier,
                parameter.HasDefault));
        }

        return new MetadataSymbolMember(
            declaration.DocumentationId,
            declaration.CanonicalSignature,
            declaration.DisplaySignature,
            Kind(declaration.Category),
            declaration.Name,
            declaration.Accessibility.Replace(' ', '_'),
            modifiers.IsStatic,
            modifiers.IsAbstract,
            // The booleans state C# declaration modifiers (#records): an
            // abstract, override, or sealed Member is not declared virtual.
            modifiers.IsVirtual
                && !modifiers.IsAbstract
                && !isOverride
                && (modifierSource & MethodAttributes.Final) == 0,
            isOverride,
            // Sealing follows the corrected override reading, so a covariant
            // sealed override (newslot, final, MethodImpl) is sealed.
            isOverride && (modifierSource & MethodAttributes.Final) != 0,
            isExtension,
            [.. model.TypeParameters.Select(parameter => parameter.Name)],
            isConstructor ? "" : model.ReturnType ?? "",
            isConstructor
                ? []
                : Mentions(model.ReturnTypeReferences, returnNode, returnShape),
            [.. produced],
            parameters.MoveToImmutable(),
            overrides,
            [.. attributes],
            declaration.MetadataToken);
    }

    /// <summary>
    /// The decoded return (or declared) Type and parameter Types of a
    /// method, property, or field, through the guarded decoders; an event's
    /// Type is read from its model.
    /// </summary>
    (TypeNode? Return, ImmutableArray<TypeNode> Parameters) StructuralSignature(
        EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                MethodDefinition method =
                    _reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                MethodSignature<TypeNode> signature = GuardedProviderDecode.Method(
                    _reader,
                    method,
                    TypeNodeProvider.Instance,
                    Context(method.GetDeclaringType()),
                    (TypeNode)new DegradedTypeNode());
                return (signature.ReturnType, signature.ParameterTypes);
            }
            case HandleKind.PropertyDefinition:
            {
                PropertyDefinition property =
                    _reader.GetPropertyDefinition((PropertyDefinitionHandle)handle);
                TypeDefinitionHandle owner = property.GetAccessors() switch
                {
                    { Getter.IsNil: false } accessors =>
                        _reader.GetMethodDefinition(accessors.Getter).GetDeclaringType(),
                    { Setter.IsNil: false } accessors =>
                        _reader.GetMethodDefinition(accessors.Setter).GetDeclaringType(),
                    _ => default,
                };
                if (owner.IsNil)
                    return (null, []);
                MethodSignature<TypeNode> signature = GuardedProviderDecode.Property(
                    _reader,
                    property,
                    TypeNodeProvider.Instance,
                    Context(owner),
                    (TypeNode)new DegradedTypeNode());
                return (signature.ReturnType, signature.ParameterTypes);
            }
            case HandleKind.FieldDefinition:
            {
                FieldDefinition field =
                    _reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                return (
                    GuardedProviderDecode.Field(
                        _reader,
                        field,
                        TypeNodeProvider.Instance,
                        Context(field.GetDeclaringType()),
                        (TypeNode)new DegradedTypeNode()),
                    []);
            }
            default:
                return (null, []);
        }
    }

    GenericContext Context(TypeDefinitionHandle type) =>
        GenericContext.ForType(_reader, _reader.GetTypeDefinition(type));

    static string Kind(MetadataTypeMemberGroupCategory category) => category switch
    {
        MetadataTypeMemberGroupCategory.Method => "method",
        MetadataTypeMemberGroupCategory.Constructor => "constructor",
        MetadataTypeMemberGroupCategory.Operator => "operator",
        MetadataTypeMemberGroupCategory.Finalizer => "finalizer",
        MetadataTypeMemberGroupCategory.ExplicitInterfaceImplementation =>
            "explicit_interface_implementation",
        MetadataTypeMemberGroupCategory.Property => "property",
        MetadataTypeMemberGroupCategory.Field => "field",
        MetadataTypeMemberGroupCategory.Event => "event",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    /// <summary>
    /// Every Type definition a spelling names, at any depth: the named
    /// references the decoder issues and the primitive Types its shape
    /// names. Generic parameters and <c>void</c> name nothing.
    /// </summary>
    static ImmutableArray<string> Mentions(
        IEnumerable<ApiTypeReferenceIdentity> references,
        TypeNode? node,
        ApiTypeShape? shape)
    {
        var identities = new SortedSet<string>(StringComparer.Ordinal);
        foreach (ApiTypeReferenceIdentity reference in references)
            identities.Add(TypeIdentity(reference));
        if (node is null)
        {
            AddPrimitives(shape, identities);
        }
        else
        {
            // The node reaches through pointers and function pointers, which
            // have no shape.
            foreach (ApiPrimitiveType primitive in node.ReferencedPrimitives())
            {
                if (PrimitiveIdentity(primitive) is { } identity)
                    identities.Add(identity);
            }
        }
        return [.. identities];
    }

    static void AddPrimitives(ApiTypeShape? shape, SortedSet<string> into)
    {
        if (shape is null)
            return;
        if (shape.Kind is ApiTypeShapeKind.Primitive
            && PrimitiveIdentity(shape.Primitive) is { } primitive)
        {
            into.Add(primitive);
        }
        AddPrimitives(shape.ElementType, into);
        foreach (ApiTypeShape argument in shape.TypeArguments.IsDefault
            ? []
            : shape.TypeArguments)
        {
            AddPrimitives(argument, into);
        }
    }

    static string? PrimitiveIdentity(ApiPrimitiveType? primitive) =>
        primitive is null or ApiPrimitiveType.Void
            ? null
            : $"T:System.{primitive}";

    /// <summary>
    /// The Type definition a caller obtains: <c>Task&lt;T&gt;</c>,
    /// <c>ValueTask&lt;T&gt;</c>, <c>Nullable&lt;T&gt;</c>, and array
    /// wrappers are removed, then the outermost definition is kept.
    /// Non-generic <c>Task</c> and <c>ValueTask</c>, <c>void</c>, and
    /// generic parameters produce nothing.
    /// </summary>
    static void AddProduced(ApiTypeShape? shape, SortedSet<string> into)
    {
        int remaining = MetadataSafetyPolicy.MaxRelationshipNodes;
        while (shape is not null && remaining-- > 0)
        {
            switch (shape.Kind)
            {
                case ApiTypeShapeKind.SzArray or ApiTypeShapeKind.Array:
                    shape = shape.ElementType;
                    continue;
                case ApiTypeShapeKind.GenericInstance
                    when shape.Definition is { } definition
                        && TypeIdentity(definition)
                            is "T:System.Threading.Tasks.Task`1"
                            or "T:System.Threading.Tasks.ValueTask`1"
                            or "T:System.Nullable`1"
                        && shape.TypeArguments.Length == 1:
                    shape = shape.TypeArguments[0];
                    continue;
                case ApiTypeShapeKind.GenericInstance or ApiTypeShapeKind.Named
                    when shape.Definition is { } definition:
                {
                    string identity = TypeIdentity(definition);
                    if (identity is not ("T:System.Threading.Tasks.Task"
                        or "T:System.Threading.Tasks.ValueTask"))
                    {
                        into.Add(identity);
                    }
                    return;
                }
                case ApiTypeShapeKind.Primitive:
                    if (PrimitiveIdentity(shape.Primitive) is { } primitive)
                        into.Add(primitive);
                    return;
                default:
                    return;
            }
        }
    }

    // ---- Structural signatures for override matching -----------------------

    internal abstract record Signature
    {
        public sealed record Primitive(string Identity) : Signature;

        /// <summary>
        /// A named Type. <see cref="IsLocal"/> says it is defined in this
        /// module, so its definition here is the Type it names; a reference
        /// to another assembly never resolves to a local namesake. It does
        /// not take part in structural equality.
        /// </summary>
        public sealed record Named(
            string Namespace,
            ImmutableArray<string> Segments,
            ImmutableArray<Signature> Arguments,
            bool IsLocal,
            string Scope) : Signature
        {
            // Scope is the referenced assembly's name ("" for this module),
            // so namesakes from different assemblies are different Types.
            public bool Equals(Named? other) =>
                other is not null
                && Namespace == other.Namespace
                && Scope == other.Scope
                && Segments.SequenceEqual(other.Segments)
                && Arguments.SequenceEqual(other.Arguments);

            public override int GetHashCode() =>
                HashCode.Combine(Namespace, Segments.Length, Arguments.Length);
        }

        public sealed record TypeParameter(int Index, bool IsMethod) : Signature;

        /// <summary>
        /// A custom modifier, kept because override matching compares them,
        /// and because a function pointer's calling convention is one.
        /// </summary>
        public sealed record Modified(Signature Modifier, Signature Inner, bool IsRequired)
            : Signature;

        /// <summary>A function pointer, compared by its whole signature.</summary>
        public sealed record FunctionPointer(
            SignatureHeader Header,
            Signature Return,
            ImmutableArray<Signature> Parameters) : Signature
        {
            public bool Equals(FunctionPointer? other) =>
                other is not null
                && Header.RawValue == other.Header.RawValue
                && Return.Equals(other.Return)
                && Parameters.SequenceEqual(other.Parameters);

            public override int GetHashCode() =>
                HashCode.Combine(Header.RawValue, Parameters.Length);
        }

        public sealed record Element(string Wrapper, int Rank, Signature Inner)
            : Signature;

        /// <summary>
        /// A Type the decoder could not represent; it equals only itself, so
        /// it can never make two signatures match.
        /// </summary>
        public sealed record Opaque : Signature
        {
            public bool Equals(Opaque? other) => ReferenceEquals(this, other);

            public override int GetHashCode() =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }
    }

    sealed class SignatureProvider(MetadataReader reader)
        : ISignatureTypeProvider<Signature, object?>
    {
        public Signature Decode(EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(
                reader,
                (TypeDefinitionHandle)handle,
                0),
            HandleKind.TypeReference => GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                0),
            HandleKind.TypeSpecification => GetTypeFromSpecification(
                reader,
                null,
                (TypeSpecificationHandle)handle,
                0),
            _ => new Signature.Opaque(),
        };

        public Signature GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            new Signature.Primitive($"T:System.{typeCode}");

        public Signature GetTypeFromDefinition(
            MetadataReader metadata,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            MetadataTypeDefinitionName name =
                MetadataDeclarationQuery.GetTypeDefinitionName(reader, handle);
            return new Signature.Named(name.Namespace, name.Segments, [], IsLocal: true, Scope: "");
        }

        public Signature GetTypeFromReference(
            MetadataReader metadata,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            var segments = new List<string>();
            TypeReferenceHandle current = handle;
            int remaining = reader.GetTableRowCount(TableIndex.TypeRef);
            while (true)
            {
                TypeReference reference = reader.GetTypeReference(current);
                segments.Insert(0, reader.GetString(reference.Name));
                if (reference.ResolutionScope.Kind == HandleKind.TypeReference
                    && remaining-- > 0)
                {
                    current = (TypeReferenceHandle)reference.ResolutionScope;
                    continue;
                }
                bool local = reference.ResolutionScope.Kind == HandleKind.ModuleDefinition;
                return new Signature.Named(
                    reader.GetString(reference.Namespace),
                    [.. segments],
                    [],
                    IsLocal: local,
                    Scope: local ? "" : ScopeName(reference.ResolutionScope));
            }
        }

        string ScopeName(EntityHandle scope) => scope.Kind switch
        {
            HandleKind.AssemblyReference => reader.GetString(
                reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name),
            HandleKind.ModuleReference => reader.GetString(
                reader.GetModuleReference((ModuleReferenceHandle)scope).Name),
            _ => "?",
        };

        public Signature GetTypeFromSpecification(
            MetadataReader metadata,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) =>
            // The guard bounds blob depth and cross-handle TypeSpec cycles,
            // which would otherwise overflow the stack.
            GuardedProviderDecode.TypeSpec(
                reader,
                handle,
                this,
                genericContext,
                (Signature)new Signature.Opaque());

        public Signature GetSZArrayType(Signature elementType) =>
            new Signature.Element("[]", 1, elementType);

        public Signature GetArrayType(Signature elementType, ArrayShape shape) =>
            new Signature.Element("[,]", shape.Rank, elementType);

        public Signature GetByReferenceType(Signature elementType) =>
            new Signature.Element("&", 0, elementType);

        public Signature GetPointerType(Signature elementType) =>
            new Signature.Element("*", 0, elementType);

        public Signature GetGenericInstantiation(
            Signature genericType,
            ImmutableArray<Signature> typeArguments) =>
            genericType is Signature.Named named
                ? named with { Arguments = typeArguments }
                : genericType;

        public Signature GetGenericTypeParameter(object? genericContext, int index) =>
            new Signature.TypeParameter(index, IsMethod: false);

        public Signature GetGenericMethodParameter(object? genericContext, int index) =>
            new Signature.TypeParameter(index, IsMethod: true);

        public Signature GetFunctionPointerType(MethodSignature<Signature> signature) =>
            new Signature.FunctionPointer(
                signature.Header,
                signature.ReturnType,
                signature.ParameterTypes);

        public Signature GetModifiedType(
            Signature modifier,
            Signature unmodifiedType,
            bool isRequired) =>
            new Signature.Modified(modifier, unmodifiedType, isRequired);

        public Signature GetPinnedType(Signature elementType) => elementType;
    }
}
