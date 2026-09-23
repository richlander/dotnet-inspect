using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

public enum ResourceEffectSelectorBindingGapKind
{
    DirectCallDefinition,
    TypeDefinition,
    GenericScope,
    UnsupportedSignature,
}

public sealed record ResourceEffectSelectorBindingGap(
    ResourceEffectSelectorBindingGapKind Kind,
    DirectCallDefinitionGap? DirectCallGap = null,
    TypeRef? Type = null,
    TypeResolutionOutcome? TypeResolution = null,
    DefinitionJoinTokenProjection? DefinitionProjection = null);

public sealed class ResolvedResourceEffectType
    : IEquatable<ResolvedResourceEffectType>
{
    readonly ImmutableArray<ResolvedResourceEffectType> _arguments;

    internal ResolvedResourceEffectType(
        TypeRef type,
        AssemblyReferenceIdentity? definingAssembly,
        DefinitionJoinToken? definition,
        ResolvedResourceEffectGenericScope? genericScope,
        ResolvedResourceEffectType? element,
        ImmutableArray<ResolvedResourceEffectType> arguments,
        ImmutableArray<TypeForwardingHop> forwarding = default)
    {
        Type = type;
        DefiningAssembly = definingAssembly;
        Definition = definition;
        GenericScope = genericScope;
        Element = element;
        _arguments = arguments;
        Forwarding = forwarding.IsDefault ? [] : forwarding;
    }

    public TypeRef Type { get; }
    public AssemblyReferenceIdentity? DefiningAssembly { get; }
    public DefinitionJoinToken? Definition { get; }
    public ResolvedResourceEffectGenericScope? GenericScope { get; }
    public ResolvedResourceEffectType? Element { get; }
    public ImmutableArray<ResolvedResourceEffectType> Arguments =>
        _arguments;
    public ImmutableArray<TypeForwardingHop> Forwarding { get; }

    public bool Equals(ResolvedResourceEffectType? other) =>
        other is not null
        && Type.Kind == other.Type.Kind
        && Type.Rank == other.Type.Rank
        && Type.RawTypeKind == other.Type.RawTypeKind
        && Type.ArraySizes.AsSpan().SequenceEqual(
            other.Type.ArraySizes.AsSpan())
        && Type.ArrayLowerBounds.AsSpan().SequenceEqual(
            other.Type.ArrayLowerBounds.AsSpan())
        && (Definition is not null || other.Definition is not null
            ? Equals(Definition, other.Definition)
            : TypeRef.ExactSignatureEquals(Type, other.Type)
                && Equals(DefiningAssembly, other.DefiningAssembly))
        && Equals(GenericScope, other.GenericScope)
        && Equals(Element, other.Element)
        && _arguments.SequenceEqual(other._arguments);

    public override bool Equals(object? obj) =>
        obj is ResolvedResourceEffectType other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Type.Kind);
        hash.Add(Type.Rank);
        hash.Add(Type.RawTypeKind);
        foreach (int size in Type.ArraySizes)
            hash.Add(size);
        foreach (int bound in Type.ArrayLowerBounds)
            hash.Add(bound);
        if (Definition is not null)
            hash.Add(Definition);
        else
        {
            hash.Add(Type);
            hash.Add(DefiningAssembly);
        }
        hash.Add(GenericScope);
        hash.Add(Element);
        ImmutableArrayValueEquality.AddToHash(ref hash, _arguments);
        return hash.ToHashCode();
    }
}

public sealed record ResolvedResourceEffectGenericBinding
{
    internal ResolvedResourceEffectGenericBinding(
        ResourceEffectGenericVariable variable,
        ResolvedResourceEffectType value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Variable = variable;
        Value = value;
    }

    public ResourceEffectGenericVariable Variable { get; }
    public ResolvedResourceEffectType Value { get; }
}

public sealed record ResolvedResourceEffectGenericScope
{
    internal ResolvedResourceEffectGenericScope(
        ResourceEffectGenericVariableKind kind,
        GraphNodeStorageKey owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Kind = kind;
        Owner = owner;
    }

    public ResourceEffectGenericVariableKind Kind { get; }
    public GraphNodeStorageKey Owner { get; }
}

public sealed class ResolvedResourceKindReference
    : IEquatable<ResolvedResourceKindReference>
{
    readonly ImmutableArray<ResolvedResourceEffectType> _arguments;

    internal ResolvedResourceKindReference(
        ResourceKindIdentity identity,
        ImmutableArray<ResolvedResourceEffectType> arguments)
    {
        Identity = identity;
        _arguments =
            ImmutableArrayValueEquality.RequireInitialized(
                arguments,
                nameof(arguments));
    }

    public ResourceKindIdentity Identity { get; }
    public ImmutableArray<ResolvedResourceEffectType> Arguments =>
        _arguments;

    public bool Equals(ResolvedResourceKindReference? other) =>
        other is not null
        && Identity == other.Identity
        && _arguments.SequenceEqual(other._arguments);

    public override bool Equals(object? obj) =>
        obj is ResolvedResourceKindReference other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Identity);
        ImmutableArrayValueEquality.AddToHash(ref hash, _arguments);
        return hash.ToHashCode();
    }
}

public abstract class ResourceEffectSelectorBinding
{
    private protected ResourceEffectSelectorBinding(
        AdmittedResourceEffectDeclaration declaration,
        DirectCallDefinitionResolution directCall)
    {
        Declaration = declaration;
        DirectCall = directCall;
    }

    public AdmittedResourceEffectDeclaration Declaration { get; }
    public DirectCallDefinitionResolution DirectCall { get; }

    public sealed class Resolved : ResourceEffectSelectorBinding
    {
        internal Resolved(
            AdmittedResourceEffectDeclaration declaration,
            DirectCallDefinitionResolution.Resolved directCall,
            ImmutableArray<ResolvedResourceEffectGenericBinding>
                genericBindings,
            ImmutableArray<ResolvedResourceKindReference> resourceKinds,
            DirectCallDefinitionOccurrence? declarationOrigin = null)
            : base(declaration, directCall)
        {
            GenericBindings = genericBindings;
            ResourceKinds = resourceKinds;
            DeclarationOrigin = declarationOrigin ?? directCall.Definition;
        }

        public new DirectCallDefinitionResolution.Resolved DirectCall =>
            (DirectCallDefinitionResolution.Resolved)base.DirectCall;
        public ImmutableArray<ResolvedResourceEffectGenericBinding>
            GenericBindings
        { get; }
        public ImmutableArray<ResolvedResourceKindReference> ResourceKinds
            { get; }
        internal DirectCallDefinitionOccurrence DeclarationOrigin { get; }
    }

    public sealed class Unmatched : ResourceEffectSelectorBinding
    {
        internal Unmatched(
            AdmittedResourceEffectDeclaration declaration,
            DirectCallDefinitionResolution directCall)
            : base(declaration, directCall)
        {
        }
    }

    public sealed class Ambiguous : ResourceEffectSelectorBinding
    {
        internal Ambiguous(
            AdmittedResourceEffectDeclaration declaration,
            DirectCallDefinitionResolution directCall,
            ResourceEffectSelectorBindingGap gap)
            : base(declaration, directCall) =>
            Gap = gap;

        public ResourceEffectSelectorBindingGap Gap { get; }
    }

    public sealed class Unsupported : ResourceEffectSelectorBinding
    {
        internal Unsupported(
            AdmittedResourceEffectDeclaration declaration,
            DirectCallDefinitionResolution directCall,
            ResourceEffectSelectorBindingGap gap)
            : base(declaration, directCall) =>
            Gap = gap;

        public ResourceEffectSelectorBindingGap Gap { get; }
    }

    public sealed class Incomplete : ResourceEffectSelectorBinding
    {
        internal Incomplete(
            AdmittedResourceEffectDeclaration declaration,
            DirectCallDefinitionResolution directCall,
            ResourceEffectSelectorBindingGap gap)
            : base(declaration, directCall) =>
            Gap = gap;

        public ResourceEffectSelectorBindingGap Gap { get; }
    }
}

public static class ResourceEffectSelectorBinder
{
    const byte ExplicitThis = 0x40;
    const byte CallingConventionMask = 0x0F;

    public static ResourceEffectSelectorBinding Bind(
        AdmittedResourceEffectDeclaration declaration,
        DirectCallDefinitionResolution directCall)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(directCall);

        if (declaration.Target
                is not ResourceEffectTargetSelector.Member target
            || target.Selector.Kind == ResourceEffectMemberKind.Field
            || !CouldMatch(target.Selector, directCall.Call.Callee))
        {
            return new ResourceEffectSelectorBinding.Unmatched(
                declaration,
                directCall);
        }

        switch (directCall)
        {
            case DirectCallDefinitionResolution.Unmatched:
                return new ResourceEffectSelectorBinding.Unmatched(
                    declaration,
                    directCall);
            case DirectCallDefinitionResolution.Ambiguous ambiguous:
                return new ResourceEffectSelectorBinding.Ambiguous(
                    declaration,
                    directCall,
                    DirectCallGap(ambiguous.Gap));
            case DirectCallDefinitionResolution.Unsupported unsupported:
                return new ResourceEffectSelectorBinding.Unsupported(
                    declaration,
                    directCall,
                    DirectCallGap(unsupported.Gap));
            case DirectCallDefinitionResolution.Incomplete incomplete:
                return new ResourceEffectSelectorBinding.Incomplete(
                    declaration,
                    directCall,
                    DirectCallGap(incomplete.Gap));
        }

        var resolved =
            (DirectCallDefinitionResolution.Resolved)directCall;
        var bindings =
            new Dictionary<ResourceEffectGenericVariable, TypeRef>();
        MatchResult match = MatchMember(
            target.Selector,
            resolved,
            bindings);
        if (match.Kind != MatchKind.Match)
            return Failure(declaration, directCall, match);

        MatchResult bindingResult = TryResolveBindings(
            bindings,
            resolved,
            out ImmutableArray<ResolvedResourceEffectGenericBinding>
                resolvedBindings);
        if (bindingResult.Kind != MatchKind.Match)
            return Failure(declaration, directCall, bindingResult);
        if (!TryResolveResourceKinds(
                declaration.Effect,
                resolvedBindings,
                out ImmutableArray<ResolvedResourceKindReference>
                    resourceKinds))
        {
            return new ResourceEffectSelectorBinding.Incomplete(
                declaration,
                directCall,
                new ResourceEffectSelectorBindingGap(
                    ResourceEffectSelectorBindingGapKind
                        .GenericScope));
        }
        return new ResourceEffectSelectorBinding.Resolved(
            declaration,
            resolved,
            resolvedBindings,
            resourceKinds);
    }

    static ResourceEffectSelectorBinding Failure(
        AdmittedResourceEffectDeclaration declaration,
        DirectCallDefinitionResolution directCall,
        MatchResult match)
    {
        if (match.Kind == MatchKind.NoMatch)
        {
            return new ResourceEffectSelectorBinding.Unmatched(
                declaration,
                directCall);
        }
        var gap = new ResourceEffectSelectorBindingGap(
            match.GapKind,
            Type: match.Type,
            TypeResolution: match.TypeResolution,
            DefinitionProjection:
                match.DefinitionProjection);
        return match.Kind switch
        {
            MatchKind.Ambiguous =>
                new ResourceEffectSelectorBinding.Ambiguous(
                    declaration,
                    directCall,
                    gap),
            MatchKind.Unsupported =>
                new ResourceEffectSelectorBinding.Unsupported(
                    declaration,
                    directCall,
                    gap),
            _ => new ResourceEffectSelectorBinding.Incomplete(
                declaration,
                directCall,
                gap),
        };
    }

    static ResourceEffectSelectorBindingGap DirectCallGap(
        DirectCallDefinitionGap gap) =>
        new(
            ResourceEffectSelectorBindingGapKind.DirectCallDefinition,
            gap);

    internal static bool CouldMatch(
        ResourceEffectMemberSelector selector,
        MemberRef member)
    {
        if (!MemberShapeCouldMatch(
                selector,
                member,
                allowExplicitInterfaceName: false))
        {
            return false;
        }

        TypeRef declaring = member.DeclaringType;
        if (declaring.Kind == TypeRefKind.GenericInstance)
        {
            if (declaring.ElementType is null)
                return true;
            declaring = declaring.ElementType;
        }
        if (declaring.Kind == TypeRefKind.Unsupported)
            return true;
        return TypeNameCouldMatch(selector.DeclaringType, declaring);
    }

    internal static bool MemberShapeCouldMatch(
        ResourceEffectMemberSelector selector,
        MemberRef member,
        bool allowExplicitInterfaceName)
    {
        if (member.Kind == MemberKind.Unsupported)
            return true;
        bool nameMatches = string.Equals(
            selector.MetadataName,
            member.Name,
            StringComparison.Ordinal);
        if (allowExplicitInterfaceName)
        {
            nameMatches |= member.Name.EndsWith(
                $".{selector.MetadataName}",
                StringComparison.Ordinal);
        }
        if (!nameMatches
            || selector.GenericArity != member.GenericArity
            || selector.IsStatic == member.HasThis
            || selector.HasThis != member.HasThis
            || selector.ExplicitThis
                != ((member.SignatureHeader & ExplicitThis) != 0)
            || !ParameterCountsCouldMatch(
                selector.CallingConvention,
                selector.Parameters.Length,
                member))
        {
            return false;
        }
        ResourceEffectCallingConvention? callingConvention =
            CallingConvention(member.SignatureHeader);
        if (callingConvention is not null
            && callingConvention != selector.CallingConvention)
        {
            return false;
        }
        if ((selector.Kind == ResourceEffectMemberKind.Constructor)
                != (member.Kind == MemberKind.Constructor)
            && (selector.Kind == ResourceEffectMemberKind.Constructor
                || member.Kind == MemberKind.Constructor))
        {
            return false;
        }
        return true;
    }

    internal static bool CouldMatchInterfaceImplementation(
        ResourceEffectMemberSelector selector,
        MemberRef member) =>
        CouldMatchInterfaceImplementation(
            selector,
            member,
            allowAnyName: false);

    internal static bool CouldMatchInterfaceImplementationBodyShape(
        ResourceEffectMemberSelector selector,
        MemberRef member) =>
        CouldMatchInterfaceImplementation(
            selector,
            member,
            allowAnyName: true);

    static bool CouldMatchInterfaceImplementation(
        ResourceEffectMemberSelector selector,
        MemberRef member,
        bool allowAnyName)
    {
        if (selector.Kind
                is ResourceEffectMemberKind.Field
                    or ResourceEffectMemberKind.Constructor
            || member.Kind == MemberKind.Constructor
            || !allowAnyName
                && (selector.IsStatic || !member.HasThis))
        {
            return false;
        }
        if (member.Kind == MemberKind.Unsupported)
            return true;
        if (!MemberShapeCouldMatch(
                selector,
                allowAnyName
                    ? member with { Name = selector.MetadataName }
                    : member,
                allowExplicitInterfaceName: false))
        {
            return false;
        }
        for (int i = 0; i < selector.Parameters.Length; i++)
        {
            TypeRef actual = member.ParameterTypes[i];
            bool actualByRef = actual.Kind == TypeRefKind.ByRef;
            if ((selector.Parameters[i].RefKind
                    == ResourceEffectRefKind.Value) == actualByRef)
            {
                return false;
            }
            if (actualByRef)
                actual = actual.ElementType!;
            if (!CandidateTypeCouldMatch(
                    selector.Parameters[i].Type,
                    actual))
            {
                return false;
            }
        }
        return CandidateTypeCouldMatch(
            selector.ReturnType,
            member.ReturnType);
    }

    internal static bool TypeNameCouldMatch(
        ResourceTypeExpression.Named selector,
        TypeRef actual)
    {
        MetadataTypeDefinitionName? name = actual.Resolution?.Type;
        if (name is null)
        {
            if (selector.Segments.Length != 1
                || !string.Equals(
                    selector.Namespace,
                    actual.Namespace,
                    StringComparison.Ordinal))
            {
                return false;
            }
            return string.Equals(
                SegmentName(selector.Segments[0]),
                actual.Name,
                StringComparison.Ordinal);
        }
        if (!string.Equals(
                selector.Namespace,
                name.Namespace,
                StringComparison.Ordinal)
            || selector.Segments.Length != name.Segments.Length)
        {
            return false;
        }
        for (int i = 0; i < selector.Segments.Length; i++)
        {
            if (!string.Equals(
                    SegmentName(selector.Segments[i]),
                    name.Segments[i],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    static bool CandidateTypeCouldMatch(
        ResourceTypeExpression selector,
        TypeRef actual)
    {
        if (actual.Kind
            is TypeRefKind.Unsupported
                or TypeRefKind.GenericParameter
                or TypeRefKind.MethodGenericParameter)
        {
            return true;
        }
        if (actual.Kind == TypeRefKind.Pinned)
            return CandidateTypeCouldMatch(selector, actual.ElementType!);
        return selector switch
        {
            ResourceTypeExpression.Variable => true,
            ResourceTypeExpression.Named named =>
                CandidateNamedTypeCouldMatch(named, actual),
            ResourceTypeExpression.SzArray array =>
                actual.Kind == TypeRefKind.SzArray
                && CandidateTypeCouldMatch(
                    array.Element,
                    actual.ElementType!),
            ResourceTypeExpression.Array array =>
                actual.Kind == TypeRefKind.Array
                && actual.Rank == array.Rank
                && CandidateTypeCouldMatch(
                    array.Element,
                    actual.ElementType!),
            ResourceTypeExpression.ByReference reference =>
                actual.Kind == TypeRefKind.ByRef
                && CandidateTypeCouldMatch(
                    reference.Element,
                    actual.ElementType!),
            ResourceTypeExpression.Pointer pointer =>
                actual.Kind == TypeRefKind.Pointer
                && CandidateTypeCouldMatch(
                    pointer.Element,
                    actual.ElementType!),
            _ => true,
        };
    }

    static bool CandidateNamedTypeCouldMatch(
        ResourceTypeExpression.Named selector,
        TypeRef actual)
    {
        TypeRef definition = actual.Kind == TypeRefKind.GenericInstance
            ? actual.ElementType!
            : actual;
        if (definition.Kind
            is TypeRefKind.Unsupported
                or TypeRefKind.GenericParameter
                or TypeRefKind.MethodGenericParameter)
        {
            return true;
        }
        if (definition.Kind != TypeRefKind.Definition
            || !TypeNameCouldMatch(selector, definition))
        {
            return false;
        }
        ImmutableArray<TypeRef> arguments =
            actual.Kind == TypeRefKind.GenericInstance
                ? actual.TypeArguments
                : [];
        if (arguments.Length != selector.Arguments.Length)
            return false;
        for (int i = 0; i < arguments.Length; i++)
        {
            if (!CandidateTypeCouldMatch(
                    selector.Arguments[i],
                    arguments[i]))
            {
                return false;
            }
        }
        return true;
    }

    static string SegmentName(ResourceTypeNameSegment segment) =>
        segment.GenericArity == 0
            ? segment.MetadataName
            : $"{segment.MetadataName}`{segment.GenericArity}";

    static MatchResult MatchMember(
        ResourceEffectMemberSelector selector,
        DirectCallDefinitionResolution.Resolved resolution,
        Dictionary<ResourceEffectGenericVariable, TypeRef> bindings)
    {
        MemberRef member = resolution.Call.Callee;
        MemberRef definition = resolution.Definition.Member;
        if (!MemberKindMatches(selector.Kind, resolution)
            || selector.IsStatic == member.HasThis
            || selector.HasThis != member.HasThis
            || selector.ExplicitThis
                != ((member.SignatureHeader & ExplicitThis) != 0)
            || selector.GenericArity != member.GenericArity)
        {
            return MatchResult.NoMatch();
        }

        ResourceEffectCallingConvention? callingConvention =
            CallingConvention(member.SignatureHeader);
        if (callingConvention is null)
            return MatchResult.UnsupportedSignature();
        if (callingConvention != selector.CallingConvention)
            return MatchResult.NoMatch();
        int parameterCount =
            callingConvention == ResourceEffectCallingConvention.VarArgs
                ? member.RequiredParameterCount
                : member.ParameterTypes.Length;
        if (parameterCount < 0
            || parameterCount > member.ParameterTypes.Length)
        {
            return MatchResult.UnsupportedSignature();
        }
        if (selector.Parameters.Length != parameterCount)
            return MatchResult.NoMatch();
        if (!definition.ParameterDirections.IsDefaultOrEmpty
            && definition.ParameterDirections.Length
                != member.ParameterTypes.Length)
        {
            return MatchResult.UnsupportedSignature();
        }

        MatchResult methodBindings = SeedMethodBindings(
            selector.GenericArity,
            member,
            bindings);
        if (methodBindings.Kind != MatchKind.Match)
            return methodBindings;

        MatchResult result = MatchType(
            selector.DeclaringType,
            member.DeclaringType,
            resolution,
            bindings,
            resolution.Definition.Assembly);
        if (result.Kind != MatchKind.Match)
            return result;

        for (int i = 0; i < selector.Parameters.Length; i++)
        {
            TypeRef actual = member.ParameterTypes[i];
            ParameterDirection direction =
                definition.ParameterDirections.IsDefaultOrEmpty
                    ? actual.Kind == TypeRefKind.ByRef
                        ? ParameterDirection.UnknownByRef
                        : ParameterDirection.Value
                    : definition.ParameterDirections[i];
            result = MatchRefKind(
                selector.Parameters[i].RefKind,
                direction,
                actual);
            if (result.Kind != MatchKind.Match)
                return result;
            if (actual.Kind == TypeRefKind.ByRef)
                actual = actual.ElementType!;
            result = MatchType(
                selector.Parameters[i].Type,
                actual,
                resolution,
                bindings);
            if (result.Kind != MatchKind.Match)
                return result;
        }
        return MatchType(
            selector.ReturnType,
            member.ReturnType,
            resolution,
            bindings);
    }

    static MatchResult SeedMethodBindings(
        int genericArity,
        MemberRef member,
        Dictionary<ResourceEffectGenericVariable, TypeRef> bindings)
    {
        if (genericArity == 0)
        {
            return member.TypeArguments.IsEmpty
                ? MatchResult.Match()
                : MatchResult.UnsupportedSignature();
        }
        if (!member.TypeArguments.IsEmpty
            && member.TypeArguments.Length != genericArity)
        {
            return MatchResult.IncompleteGenericScope();
        }
        for (int i = 0; i < genericArity; i++)
        {
            bindings.Add(
                new ResourceEffectGenericVariable(
                    ResourceEffectGenericVariableKind.Method,
                    i),
                member.TypeArguments.IsEmpty
                    ? TypeRef.MethodGenericParameter(i)
                    : member.TypeArguments[i]);
        }
        return MatchResult.Match();
    }

    static MatchResult MatchType(
        ResourceTypeExpression selector,
        TypeRef actual,
        DirectCallDefinitionResolution.Resolved resolution,
        Dictionary<ResourceEffectGenericVariable, TypeRef> bindings,
        AssemblyReferenceIdentity? definingAssembly = null)
    {
        if (actual.Kind == TypeRefKind.Unsupported)
        {
            return MatchResult.UnsupportedSignature(actual);
        }
        switch (selector)
        {
            case ResourceTypeExpression.Variable variable:
                if (bindings.TryGetValue(
                        variable.Value,
                        out TypeRef? existing))
                {
                    return MatchBoundType(
                        existing,
                        actual,
                        resolution);
                }
                bindings.Add(variable.Value, actual);
                return MatchResult.Match();

            case ResourceTypeExpression.Named named:
            {
                TypeRef definition = actual.Kind
                        == TypeRefKind.GenericInstance
                    ? actual.ElementType!
                    : actual;
                if (definition.Kind != TypeRefKind.Definition
                    || !TypeNameCouldMatch(named, definition))
                {
                    return MatchResult.NoMatch();
                }
                MatchResult assembly = MatchAssembly(
                    named.Assembly,
                    definition,
                    resolution,
                    definingAssembly);
                if (assembly.Kind != MatchKind.Match)
                    return assembly;
                ImmutableArray<TypeRef> actualArguments =
                    actual.Kind == TypeRefKind.GenericInstance
                        ? actual.TypeArguments
                        : [];
                if (actualArguments.Length != named.Arguments.Length)
                    return MatchResult.NoMatch();
                for (int i = 0; i < actualArguments.Length; i++)
                {
                    MatchResult argument = MatchType(
                        named.Arguments[i],
                        actualArguments[i],
                        resolution,
                        bindings,
                        definingAssembly);
                    if (argument.Kind != MatchKind.Match)
                        return argument;
                }
                return MatchResult.Match();
            }

            case ResourceTypeExpression.SzArray array:
                return actual.Kind == TypeRefKind.SzArray
                    ? MatchType(
                        array.Element,
                        actual.ElementType!,
                        resolution,
                        bindings,
                        definingAssembly)
                    : MatchResult.NoMatch();

            case ResourceTypeExpression.Array array:
                return actual.Kind == TypeRefKind.Array
                    && actual.Rank == array.Rank
                    ? MatchType(
                        array.Element,
                        actual.ElementType!,
                        resolution,
                        bindings,
                        definingAssembly)
                    : MatchResult.NoMatch();

            case ResourceTypeExpression.ByReference reference:
                return actual.Kind == TypeRefKind.ByRef
                    ? MatchType(
                        reference.Element,
                        actual.ElementType!,
                        resolution,
                        bindings,
                        definingAssembly)
                    : MatchResult.NoMatch();

            case ResourceTypeExpression.Pointer pointer:
                return actual.Kind == TypeRefKind.Pointer
                    ? MatchType(
                        pointer.Element,
                        actual.ElementType!,
                        resolution,
                        bindings,
                        definingAssembly)
                    : MatchResult.NoMatch();

            default:
                return MatchResult.UnsupportedSignature(actual);
        }
    }

    static MatchResult MatchAssembly(
        ResourceAssemblySelector selector,
        TypeRef actual,
        DirectCallDefinitionResolution.Resolved resolution,
        AssemblyReferenceIdentity? definingAssembly)
    {
        if (definingAssembly is not null)
        {
            if (IsCoreLibraryFacadeMatch(selector, actual))
                return MatchResult.Match();
            if (actual.Resolution?.Origin
                    is TypeReferenceOrigin.AssemblyReference origin
                && AssemblyMatches(selector, origin.Assembly))
            {
                return MatchResult.Match();
            }
            if (actual.Resolution?.Origin
                    is TypeReferenceOrigin.CurrentAssembly current)
            {
                return AssemblyMatches(
                        selector,
                        current.Assembly ?? definingAssembly)
                    ? MatchResult.Match()
                    : MatchResult.NoMatch();
            }
            if (resolution.TypeResolutions.TryGet(
                    actual,
                    out DirectCallTypeResolutionProjection selectedProjection)
                && selectedProjection.Kind
                    == DirectCallTypeResolutionKind.Resolved)
            {
                return AssemblyMatches(
                        selector,
                        selectedProjection.Assembly!)
                    ? MatchResult.Match()
                    : MatchResult.NoMatch();
            }
            return actual.Resolution is null
                && AssemblyMatches(selector, definingAssembly)
                    ? MatchResult.Match()
                    : MatchResult.NoMatch();
        }
        if (!resolution.TypeResolutions.TryGet(
                actual,
                out DirectCallTypeResolutionProjection projection))
        {
            return MatchResult.IncompleteType(actual);
        }
        return projection.Kind switch
        {
            DirectCallTypeResolutionKind.Resolved =>
                (IsCoreLibraryFacadeMatch(selector, actual)
                    || AssemblyMatches(
                        selector,
                        projection.Assembly!))
                    ? MatchResult.Match()
                    : MatchResult.NoMatch(),
            DirectCallTypeResolutionKind.IntrinsicCoreLibrary =>
                IsCoreLibraryFacadeMatch(selector, actual)
                    ? MatchResult.Match()
                    : MatchResult.IncompleteType(
                        actual,
                        projection.Outcome,
                        projection.DefinitionProjection),
            DirectCallTypeResolutionKind.Ambiguous =>
                MatchResult.AmbiguousType(
                    actual,
                    projection.Outcome,
                    projection.DefinitionProjection),
            DirectCallTypeResolutionKind.Unsupported =>
                MatchResult.UnsupportedType(
                    actual,
                    projection.Outcome,
                    projection.DefinitionProjection),
            _ => MatchResult.IncompleteType(
                actual,
                projection.Outcome,
                projection.DefinitionProjection),
        };
    }

    static bool IsCoreLibraryFacadeMatch(
        ResourceAssemblySelector selector,
        TypeRef actual) =>
        selector.AllowCoreLibraryFacade
        && actual.Assembly == TypeRef.CoreLibrary
        && actual.TrustedFrameworkAssembly
        && PlatformKeys.IsPlatform(selector.PublicKeyToken)
        && IsSupportedCoreLibraryFacade(selector.SimpleName)
        && selector.Version.Kind
            == ResourceAssemblyVersionPolicyKind.Any;

    static bool AssemblyMatches(
        ResourceAssemblySelector selector,
        AssemblyReferenceIdentity identity) =>
        string.Equals(
            selector.SimpleName,
            identity.Name,
            StringComparison.OrdinalIgnoreCase)
        && (selector.PublicKeyToken is null
            || string.Equals(
                selector.PublicKeyToken,
                identity.PublicKeyToken,
                StringComparison.OrdinalIgnoreCase))
        && (selector.Version.Kind
                == ResourceAssemblyVersionPolicyKind.Any
            || selector.Version.Version == identity.Version);

    static bool IsSupportedCoreLibraryFacade(string simpleName) =>
        simpleName.Equals(
            "mscorlib",
            StringComparison.OrdinalIgnoreCase)
        || simpleName.Equals(
            "netstandard",
            StringComparison.OrdinalIgnoreCase)
        || simpleName.Equals(
            "System.Private.CoreLib",
            StringComparison.OrdinalIgnoreCase)
        || simpleName.Equals(
            "System.Runtime",
            StringComparison.OrdinalIgnoreCase)
        || simpleName.Equals(
            "System.Runtime.Extensions",
            StringComparison.OrdinalIgnoreCase)
        || simpleName.Equals(
            "System.Buffers",
            StringComparison.OrdinalIgnoreCase)
        || simpleName.Equals(
            "System.Memory",
            StringComparison.OrdinalIgnoreCase);

    static MatchResult MatchBoundType(
        TypeRef left,
        TypeRef right,
        DirectCallDefinitionResolution.Resolved resolution)
    {
        MatchResult leftResult = TryResolveType(
            left,
            resolution,
            out ResolvedResourceEffectType leftResolved);
        if (leftResult.Kind != MatchKind.Match)
            return leftResult;
        MatchResult rightResult = TryResolveType(
            right,
            resolution,
            out ResolvedResourceEffectType rightResolved);
        if (rightResult.Kind != MatchKind.Match)
            return rightResult;
        return leftResolved.Equals(rightResolved)
            ? MatchResult.Match()
            : MatchResult.NoMatch();
    }

    static MatchResult TryResolveBindings(
        Dictionary<ResourceEffectGenericVariable, TypeRef> bindings,
        DirectCallDefinitionResolution.Resolved resolution,
        out ImmutableArray<ResolvedResourceEffectGenericBinding>
            resolvedBindings)
    {
        var resolved =
            ImmutableArray.CreateBuilder<
                ResolvedResourceEffectGenericBinding>(
                    bindings.Count);
        foreach (KeyValuePair<ResourceEffectGenericVariable, TypeRef>
            binding in bindings
                .OrderBy(binding => binding.Key.Kind)
                .ThenBy(binding => binding.Key.Index))
        {
            MatchResult result = TryResolveType(
                binding.Value,
                resolution,
                out ResolvedResourceEffectType value);
            if (result.Kind != MatchKind.Match)
            {
                resolvedBindings = [];
                return result;
            }
            resolved.Add(
                new ResolvedResourceEffectGenericBinding(
                    binding.Key,
                    value));
        }
        resolvedBindings = resolved.ToImmutable();
        return MatchResult.Match();
    }

    internal static MatchResult TryResolveType(
        TypeRef type,
        DirectCallDefinitionResolution.Resolved resolution,
        out ResolvedResourceEffectType result)
    {
        if (type.Kind == TypeRefKind.Unsupported
            || type.Kind == TypeRefKind.Pinned)
        {
            result = null!;
            return MatchResult.UnsupportedSignature(type);
        }
        if (type.Kind
            is TypeRefKind.GenericParameter
                or TypeRefKind.MethodGenericParameter)
        {
            DirectCallGenericScopeOwners? scopes =
                resolution.GenericScopes;
            if (scopes is null)
            {
                result = null!;
                return MatchResult.IncompleteGenericScope(type);
            }
            ResourceEffectGenericVariableKind kind =
                type.Kind == TypeRefKind.GenericParameter
                    ? ResourceEffectGenericVariableKind.Type
                    : ResourceEffectGenericVariableKind.Method;
            result = new ResolvedResourceEffectType(
                type,
                null,
                null,
                new ResolvedResourceEffectGenericScope(
                    kind,
                    kind == ResourceEffectGenericVariableKind.Type
                        ? scopes.Type
                        : scopes.Method),
                null,
                []);
            return MatchResult.Match();
        }

        ResolvedResourceEffectType? element = null;
        if (type.ElementType is not null)
        {
            MatchResult elementResult = TryResolveType(
                type.ElementType,
                resolution,
                out element);
            if (elementResult.Kind != MatchKind.Match)
            {
                result = null!;
                return elementResult;
            }
        }
        var arguments =
            ImmutableArray.CreateBuilder<ResolvedResourceEffectType>(
                type.TypeArguments.Length);
        foreach (TypeRef argument in type.TypeArguments)
        {
            MatchResult argumentResult = TryResolveType(
                argument,
                resolution,
                out ResolvedResourceEffectType resolvedArgument);
            if (argumentResult.Kind != MatchKind.Match)
            {
                result = null!;
                return argumentResult;
            }
            arguments.Add(resolvedArgument);
        }

        TypeRef definition = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType!
            : type;
        if (definition.Kind != TypeRefKind.Definition)
        {
            result = new ResolvedResourceEffectType(
                type,
                element?.DefiningAssembly,
                element?.Definition,
                null,
                element,
                arguments.ToImmutable(),
                element?.Forwarding ?? []);
            return MatchResult.Match();
        }
        if (IsIntrinsicCoreLibrarySignatureType(definition))
        {
            result = new ResolvedResourceEffectType(
                type,
                null,
                null,
                null,
                element,
                arguments.ToImmutable());
            return MatchResult.Match();
        }
        if (!resolution.TypeResolutions.TryGet(
                definition,
                out DirectCallTypeResolutionProjection projection))
        {
            result = null!;
            return MatchResult.IncompleteType(definition);
        }
        if (projection.Kind
            == DirectCallTypeResolutionKind.IntrinsicCoreLibrary)
        {
            result = new ResolvedResourceEffectType(
                type,
                null,
                null,
                null,
                element,
                arguments.ToImmutable());
            return MatchResult.Match();
        }
        if (projection.Kind
            != DirectCallTypeResolutionKind.Resolved)
        {
            result = null!;
            return projection.Kind switch
            {
                DirectCallTypeResolutionKind.Ambiguous =>
                    MatchResult.AmbiguousType(
                        definition,
                        projection.Outcome,
                        projection.DefinitionProjection),
                DirectCallTypeResolutionKind.Unsupported =>
                    MatchResult.UnsupportedType(
                        definition,
                        projection.Outcome,
                        projection.DefinitionProjection),
                _ => MatchResult.IncompleteType(
                    definition,
                    projection.Outcome,
                    projection.DefinitionProjection),
            };
        }
        result = new ResolvedResourceEffectType(
            type,
            projection.Assembly,
            projection.Definition,
            null,
            element,
            arguments.ToImmutable(),
            projection.Outcome?.Hops ?? []);
        return MatchResult.Match();
    }

    internal static bool IsIntrinsicCoreLibrarySignatureType(TypeRef type)
    {
        TypeRef definition = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType!
            : type;
        if (definition.Kind != TypeRefKind.Definition
            || definition.Assembly != TypeRef.CoreLibrary
            || !definition.TrustedFrameworkAssembly)
        {
            return false;
        }
        if (definition.Resolution?.Origin
            is TypeReferenceOrigin.IntrinsicCoreLibrary)
        {
            return true;
        }
        return definition.Resolution is null
            && definition.Namespace == "System"
            && definition.Name is
                "Void"
                or "Boolean"
                or "Char"
                or "SByte"
                or "Byte"
                or "Int16"
                or "UInt16"
                or "Int32"
                or "UInt32"
                or "Int64"
                or "UInt64"
                or "IntPtr"
                or "UIntPtr"
                or "Single"
                or "Double"
                or "String"
                or "Object"
                or "TypedReference";
    }

    static bool TryResolveResourceKinds(
        ResourceEffect effect,
        ImmutableArray<ResolvedResourceEffectGenericBinding> bindings,
        out ImmutableArray<ResolvedResourceKindReference> resolvedKinds)
    {
        var resolved =
            ImmutableArray.CreateBuilder<ResolvedResourceKindReference>();
        foreach (ResourceKindReference kind in EffectKinds(effect)
            .Distinct())
        {
            var arguments =
                ImmutableArray.CreateBuilder<ResolvedResourceEffectType>(
                    kind.Arguments.Length);
            foreach (ResourceEffectGenericVariable variable
                in kind.Arguments)
            {
                ResolvedResourceEffectGenericBinding? binding =
                    bindings.FirstOrDefault(candidate =>
                        candidate.Variable == variable);
                if (binding is null)
                {
                    resolvedKinds = [];
                    return false;
                }
                arguments.Add(binding.Value);
            }
            var candidate = new ResolvedResourceKindReference(
                kind.Identity,
                arguments.ToImmutable());
            if (!resolved.Contains(candidate))
                resolved.Add(candidate);
        }
        resolvedKinds = resolved.ToImmutable();
        return true;
    }

    static IEnumerable<ResourceKindReference> EffectKinds(
        ResourceEffect effect)
    {
        ResourceKindReference? direct = effect switch
        {
            ResourceEffect.Resource value => value.Kind,
            ResourceEffect.Authority value => value.Kind,
            ResourceEffect.Acquire value => value.Kind,
            ResourceEffect.Move value => value.Kind,
            ResourceEffect.Consume value => value.Kind,
            ResourceEffect.Release value => value.Kind,
            ResourceEffect.Borrow value => value.Kind,
            ResourceEffect.Accept value => value.Kind,
            _ => null,
        };
        if (direct is not null)
            yield return direct;
        foreach (ResourceEffectLocation location in EffectLocations(effect))
        {
            foreach (ResourceKindReference kind in LocationKinds(location))
                yield return kind;
        }
    }

    static IEnumerable<ResourceKindReference> LocationKinds(
        ResourceEffectLocation location)
    {
        switch (location)
        {
            case ResourceEffectLocation.OperationSlot operation:
                if (operation.Kind is not null)
                    yield return operation.Kind;
                foreach (ResourceKindReference kind
                    in LocationKinds(operation.Source))
                {
                    yield return kind;
                }
                break;
            case ResourceEffectLocation.StructuralField field:
                foreach (ResourceKindReference kind
                    in LocationKinds(field.Root))
                {
                    yield return kind;
                }
                break;
        }
    }

    static IEnumerable<ResourceEffectLocation> EffectLocations(
        ResourceEffect effect) =>
        effect switch
        {
            ResourceEffect.Authority value => [value.Target],
            ResourceEffect.Acquire value =>
                Present(
                    value.Target,
                    value.Correspondence,
                    value.Lender,
                    CompletionSource(value.When)),
            ResourceEffect.Move value =>
                Present(
                    value.Source,
                    value.Target,
                    CompletionSource(value.When)),
            ResourceEffect.Consume value => [value.Source, value.Target],
            ResourceEffect.Release value =>
                Present(
                    value.Source,
                    value.Correspondence,
                    value.Observation,
                    CompletionSource(value.When)),
            ResourceEffect.Borrow value =>
                Present(value.Source, value.Target, value.Lender),
            ResourceEffect.Derive value =>
                Present(value.Source, value.Target, GuardSubject(value.Guard)),
            ResourceEffect.Pass value => [value.Source, value.Target],
            ResourceEffect.Independent value =>
                [value.Source, value.Target],
            ResourceEffect.Callback value => [value.Delegate],
            ResourceEffect.Accept value =>
                Present(
                    value.Source,
                    value.Target,
                    CompletionSource(value.When)),
            ResourceEffect.Operation value =>
                Present(GuardSubject(value.Guard)),
            ResourceEffect.Outcome value => [value.Source],
            _ => [],
        };

    static ResourceEffectLocation? CompletionSource(
        ResourceEffectCompletion completion) =>
        completion is ResourceEffectCompletion.OutcomeCase outcome
            ? outcome.Source
            : null;

    static IEnumerable<ResourceEffectLocation> Present(
        params ResourceEffectLocation?[] locations) =>
        locations.OfType<ResourceEffectLocation>();

    static ResourceEffectLocation? GuardSubject(
        ResourceEffectGuard? guard) =>
        guard is ResourceEffectGuard.ExactRuntimeType exact
            ? exact.Subject
            : null;

    internal static MatchResult MatchOccurrenceType(
        ResourceTypeExpression selector,
        TypeRef actual,
        ResourceEffectSelectorBinding.Resolved binding,
        AssemblyReferenceIdentity? definingAssembly = null)
    {
        var bindings = binding.GenericBindings.ToDictionary(
            item => item.Variable,
            item => item.Value.Type);
        return MatchType(
            selector,
            actual,
            binding.DirectCall,
            bindings,
            definingAssembly);
    }

    internal static bool TryGetDefinition(
        TypeRef type,
        DirectCallDefinitionResolution.Resolved resolution,
        out ResolvedTypeDefinition definition)
    {
        TypeRef candidate = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType!
            : type;
        if (candidate.Kind == TypeRefKind.Definition
            && resolution.TypeResolutions.TryGet(
                candidate,
                out DirectCallTypeResolutionProjection projection)
            && projection.Outcome
                is TypeResolutionOutcome.Resolved resolved)
        {
            definition = resolved.Definition;
            return true;
        }

        definition = null!;
        return false;
    }

    static bool MemberKindMatches(
        ResourceEffectMemberKind selector,
        DirectCallDefinitionResolution.Resolved resolution) =>
        selector switch
        {
            ResourceEffectMemberKind.Method =>
                resolution.Definition.Semantics
                    == DirectCallDefinitionSemantics.Method
                && resolution.Definition.Member.Kind
                    == MemberKind.Method,
            ResourceEffectMemberKind.Constructor =>
                resolution.Definition.Member.Kind
                    == MemberKind.Constructor,
            ResourceEffectMemberKind.PropertyGetter =>
                resolution.Definition.Semantics
                    == DirectCallDefinitionSemantics.PropertyGetter,
            _ => false,
        };

    static bool ParameterCountsCouldMatch(
        ResourceEffectCallingConvention selectorConvention,
        int selectorParameterCount,
        MemberRef member)
    {
        ResourceEffectCallingConvention? memberConvention =
            CallingConvention(member.SignatureHeader);
        if (memberConvention is null)
            return true;
        if (selectorConvention
                == ResourceEffectCallingConvention.VarArgs
            && memberConvention
                == ResourceEffectCallingConvention.VarArgs)
        {
            return member.RequiredParameterCount < 0
                || member.RequiredParameterCount
                    > member.ParameterTypes.Length
                || selectorParameterCount
                    == member.RequiredParameterCount;
        }
        return selectorParameterCount == member.ParameterTypes.Length;
    }

    static ResourceEffectCallingConvention? CallingConvention(
        byte header) =>
        (header & CallingConventionMask) switch
        {
            0x00 => ResourceEffectCallingConvention.Default,
            0x05 => ResourceEffectCallingConvention.VarArgs,
            0x01 => ResourceEffectCallingConvention.CDecl,
            0x02 => ResourceEffectCallingConvention.StdCall,
            0x03 => ResourceEffectCallingConvention.ThisCall,
            0x04 => ResourceEffectCallingConvention.FastCall,
            _ => null,
        };

    static MatchResult MatchRefKind(
        ResourceEffectRefKind selector,
        ParameterDirection actual,
        TypeRef type)
    {
        if (selector == ResourceEffectRefKind.Value)
        {
            return actual == ParameterDirection.Value
                ? MatchResult.Match()
                : MatchResult.NoMatch();
        }
        if (actual == ParameterDirection.UnknownByRef)
            return MatchResult.UnsupportedSignature(type);
        return selector switch
        {
            ResourceEffectRefKind.Ref =>
                actual == ParameterDirection.Ref
                    ? MatchResult.Match()
                    : MatchResult.NoMatch(),
            ResourceEffectRefKind.In =>
                actual == ParameterDirection.In
                    ? MatchResult.Match()
                    : MatchResult.NoMatch(),
            ResourceEffectRefKind.Out =>
                actual == ParameterDirection.Out
                    ? MatchResult.Match()
                    : MatchResult.NoMatch(),
            _ => MatchResult.NoMatch(),
        };
    }

    internal enum MatchKind
    {
        Match,
        NoMatch,
        Ambiguous,
        Unsupported,
        Incomplete,
    }

    internal readonly record struct MatchResult(
        MatchKind Kind,
        ResourceEffectSelectorBindingGapKind GapKind =
            ResourceEffectSelectorBindingGapKind
                .UnsupportedSignature,
        TypeRef? Type = null,
        TypeResolutionOutcome? TypeResolution = null,
        DefinitionJoinTokenProjection? DefinitionProjection = null)
    {
        internal static MatchResult Match() =>
            new(MatchKind.Match);
        internal static MatchResult NoMatch() =>
            new(MatchKind.NoMatch);
        internal static MatchResult AmbiguousType(
            TypeRef type,
            TypeResolutionOutcome? outcome = null,
            DefinitionJoinTokenProjection? definitionProjection =
                null) =>
            new(
                MatchKind.Ambiguous,
                ResourceEffectSelectorBindingGapKind.TypeDefinition,
                type,
                outcome,
                definitionProjection);
        internal static MatchResult UnsupportedType(
            TypeRef type,
            TypeResolutionOutcome? outcome = null,
            DefinitionJoinTokenProjection? definitionProjection =
                null) =>
            new(
                MatchKind.Unsupported,
                ResourceEffectSelectorBindingGapKind.TypeDefinition,
                type,
                outcome,
                definitionProjection);
        internal static MatchResult IncompleteType(
            TypeRef type,
            TypeResolutionOutcome? outcome = null,
            DefinitionJoinTokenProjection? definitionProjection =
                null) =>
            new(
                MatchKind.Incomplete,
                ResourceEffectSelectorBindingGapKind.TypeDefinition,
                type,
                outcome,
                definitionProjection);
        internal static MatchResult UnsupportedSignature(
            TypeRef? type = null) =>
            new(
                MatchKind.Unsupported,
                ResourceEffectSelectorBindingGapKind
                    .UnsupportedSignature,
                type);
        internal static MatchResult IncompleteGenericScope(
            TypeRef? type = null) =>
            new(
                MatchKind.Incomplete,
                ResourceEffectSelectorBindingGapKind.GenericScope,
                type);
    }
}
