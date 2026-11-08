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

    /// <summary>The admitted Members of one Type in metadata order.</summary>
    public sealed record Available(
        ImmutableArray<MetadataSymbolMember> Members)
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
                types.Add(DefinitionRecord(handle, definition, name));
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

    public MetadataLibrarySymbolMembersOutcome ReadMembers(
        MetadataSymbolType type,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.DeclarationKind is MetadataSymbolDeclarationKind.Forwarder
            || type.Handle.IsNil)
        {
            return new MetadataLibrarySymbolMembersOutcome.Available([]);
        }

        try
        {
            var observer = new MemberObserver(this, type);
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
                        observer.Members.ToImmutable()),
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
        bool visible = true;
        int remaining = _reader.GetTableRowCount(TableIndex.ExportedType);
        while (true)
        {
            segments.Insert(0, _reader.GetString(current.Name));
            if (current.Implementation.Kind == HandleKind.ExportedType)
            {
                // A nested row records its visibility; a top-level forwarder
                // records none, because its Type's visibility belongs to the
                // assembly it forwards to, where only a public Type can be
                // forwarded from.
                visible &= (current.Attributes & TypeAttributes.VisibilityMask)
                    is TypeAttributes.NestedPublic
                    or TypeAttributes.NestedFamily
                    or TypeAttributes.NestedFamORAssem;
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

        if (_admission is MetadataLibrarySymbolAdmission.PublicApi && !visible)
            return null;

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
            segments.Count == 1 ? "public" : TypeAccessibility(own.Attributes),
            visible,
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
                is not Signature.Named named
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

        var chain = new List<TypeDefinitionHandle> { handle };
        if (isInterface)
        {
            chain.AddRange(
                InterfaceClosure(handle, out _).Where(inherited => inherited != handle));
        }
        else
        {
            int remaining = _reader.GetTableRowCount(TableIndex.TypeDef);
            while (BaseOf(chain[^1]) is var (baseHandle, _))
            {
                if (remaining-- == 0 || chain.Contains(baseHandle))
                    break;
                chain.Add(baseHandle);
            }
        }

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
                    pending.Remove(ObligationIdentity(overridden));
                if ((attributes & MethodAttributes.Abstract) != 0)
                {
                    pending[ObligationIdentity(methodHandle)] =
                        (attributes & MethodAttributes.MemberAccessMask)
                            is MethodAttributes.Public
                            or MethodAttributes.Family
                            or MethodAttributes.FamORAssem;
                }
            }
        }
        return pending;
    }

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
                        is Signature.Named named
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
        MethodDefinition method = _reader.GetMethodDefinition(handle);
        if ((method.Attributes & MethodAttributes.Virtual) == 0
            || (method.Attributes & MethodAttributes.NewSlot) != 0)
        {
            return null;
        }

        int arity = method.GetGenericParameters().Count;
        ImmutableArray<Signature> wanted =
            method.DecodeSignature(_signatures, null).ParameterTypes;
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
                ImmutableArray<Signature> parameters =
                    candidate.DecodeSignature(_signatures, null).ParameterTypes;
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
        MetadataSymbolType type)
        : IMetadataTypeMemberDeclarationObserver
    {
        public ImmutableArray<MetadataSymbolMember>.Builder Members { get; } =
            ImmutableArray.CreateBuilder<MetadataSymbolMember>();

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
            Members.Add(scan.MemberRecord(member, declaration, model, type));
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
        string overrides = "";
        bool producesReturn = true;
        switch (member.Handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var handle = (MethodDefinitionHandle)member.Handle;
                MethodDefinition method = _reader.GetMethodDefinition(handle);
                modifierSource = method.Attributes;
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

        bool isConstructor =
            declaration.Category is MetadataTypeMemberGroupCategory.Constructor;
        bool isExtension = member.Receiver is MetadataMethodReceiver.Extension;
        var produced = new SortedSet<string>(StringComparer.Ordinal);
        if (isConstructor)
        {
            produced.Add(type.DocumentationId);
        }
        else if (producesReturn)
        {
            AddProduced(model.ReturnTypeShape, produced);
        }

        // Parameter shapes (for primitive mentions and out-parameter
        // production) are decoded here, by the batch alone, rather than
        // retained for every parameter every API surface caller decodes.
        ImmutableArray<TypeNode> parameterTypes =
            member.Handle.Kind == HandleKind.MethodDefinition
                && model.Parameters.Count > 0
                ? ParameterTypes((MethodDefinitionHandle)member.Handle)
                : [];
        var parameters = ImmutableArray.CreateBuilder<MetadataSymbolParameter>(
            model.Parameters.Count);
        for (int index = 0; index < model.Parameters.Count; index++)
        {
            ApiParameter parameter = model.Parameters[index];
            string modifier = parameter.Modifier ?? "";
            if (modifier.Length == 0 && index == 0 && isExtension
                && declaration.Category is MetadataTypeMemberGroupCategory.Method
                && member.Kind is ClassifiedMemberKind.ExtensionMethod)
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
                Mentions(parameter.TypeReferences, shape ?? ParameterShape(parameter)),
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
                && !modifiers.IsOverride
                && (modifierSource & MethodAttributes.Final) == 0,
            modifiers.IsOverride,
            modifiers.IsSealed,
            isExtension,
            [.. model.TypeParameters.Select(parameter => parameter.Name)],
            isConstructor ? "" : model.ReturnType ?? "",
            isConstructor
                ? []
                : Mentions(model.ReturnTypeReferences, model.ReturnTypeShape),
            [.. produced],
            parameters.MoveToImmutable(),
            overrides,
            [.. attributes],
            declaration.MetadataToken);
    }

    ImmutableArray<TypeNode> ParameterTypes(MethodDefinitionHandle handle)
    {
        MethodDefinition method = _reader.GetMethodDefinition(handle);
        MethodSignature<TypeNode> signature = GuardedProviderDecode.Method(
            _reader,
            method,
            TypeNodeProvider.Instance,
            GenericContext.ForType(
                _reader,
                _reader.GetTypeDefinition(method.GetDeclaringType())),
            (TypeNode)new DegradedTypeNode());
        return signature.ParameterTypes;
    }

    /// <summary>
    /// An indexer parameter's primitive mention, read from its display
    /// spelling when the parameter is exactly one primitive Type, possibly
    /// nullable or an array of one.
    /// </summary>
    static ApiTypeShape? ParameterShape(ApiParameter parameter) =>
        PrimitiveKeyword(parameter.Type.TrimEnd('?', ']', '[')) is { } primitive
            ? ApiTypeShape.PrimitiveType(primitive)
            : null;

    static ApiPrimitiveType? PrimitiveKeyword(string keyword) => keyword switch
    {
        "bool" => ApiPrimitiveType.Boolean,
        "char" => ApiPrimitiveType.Char,
        "sbyte" => ApiPrimitiveType.SByte,
        "byte" => ApiPrimitiveType.Byte,
        "short" => ApiPrimitiveType.Int16,
        "ushort" => ApiPrimitiveType.UInt16,
        "int" => ApiPrimitiveType.Int32,
        "uint" => ApiPrimitiveType.UInt32,
        "long" => ApiPrimitiveType.Int64,
        "ulong" => ApiPrimitiveType.UInt64,
        "float" => ApiPrimitiveType.Single,
        "double" => ApiPrimitiveType.Double,
        "decimal" => ApiPrimitiveType.Decimal,
        "string" => ApiPrimitiveType.String,
        "object" => ApiPrimitiveType.Object,
        "nint" => ApiPrimitiveType.IntPtr,
        "nuint" => ApiPrimitiveType.UIntPtr,
        _ => null,
    };

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
        ApiTypeShape? shape)
    {
        var identities = new SortedSet<string>(StringComparer.Ordinal);
        foreach (ApiTypeReferenceIdentity reference in references)
            identities.Add(TypeIdentity(reference));
        AddPrimitives(shape, identities);
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

        public sealed record Named(
            string Namespace,
            ImmutableArray<string> Segments,
            ImmutableArray<Signature> Arguments) : Signature
        {
            public bool Equals(Named? other) =>
                other is not null
                && Namespace == other.Namespace
                && Segments.SequenceEqual(other.Segments)
                && Arguments.SequenceEqual(other.Arguments);

            public override int GetHashCode() =>
                HashCode.Combine(Namespace, Segments.Length, Arguments.Length);
        }

        public sealed record TypeParameter(int Index, bool IsMethod) : Signature;

        public sealed record Element(string Wrapper, int Rank, Signature Inner)
            : Signature;

        public sealed record Opaque : Signature;
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
            return new Signature.Named(name.Namespace, name.Segments, []);
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
                return new Signature.Named(
                    reader.GetString(reference.Namespace),
                    [.. segments],
                    []);
            }
        }

        public Signature GetTypeFromSpecification(
            MetadataReader metadata,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

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
            new Signature.Opaque();

        public Signature GetModifiedType(
            Signature modifier,
            Signature unmodifiedType,
            bool isRequired) => unmodifiedType;

        public Signature GetPinnedType(Signature elementType) => elementType;
    }
}
