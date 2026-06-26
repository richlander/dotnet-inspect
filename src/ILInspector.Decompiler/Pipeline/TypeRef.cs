using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

public enum TypeRefKind
{
    /// <summary>A named type definition or reference (including primitives).</summary>
    Definition,
    /// <summary>A generic instantiation: <see cref="TypeRef.ElementType"/> over <see cref="TypeRef.TypeArguments"/>.</summary>
    GenericInstance,
    /// <summary>A single-dimensional zero-based array of <see cref="TypeRef.ElementType"/>.</summary>
    SzArray,
    /// <summary>A multi-dimensional array of <see cref="TypeRef.ElementType"/> with <see cref="TypeRef.Rank"/>.</summary>
    Array,
    ByRef,
    Pointer,
    Pinned,
    /// <summary>A type generic parameter (<c>T</c> in <c>List&lt;T&gt;</c>).</summary>
    GenericParameter,
    /// <summary>A method generic parameter (<c>T</c> in <c>M&lt;T&gt;()</c>).</summary>
    MethodGenericParameter,
    /// <summary>A function pointer: <see cref="TypeRef.ElementType"/> is the return type, <see cref="TypeRef.TypeArguments"/> the parameters, rendered <c>delegate*&lt;...&gt;</c>.</summary>
    FunctionPointer,
    /// <summary>A shape outside the supported core (function pointers, custom modifiers). Carries a reason; lowers fidelity, never lies.</summary>
    Unsupported,
}

/// <summary>
/// Whether a type was decoded from an <c>ELEMENT_TYPE_VALUETYPE</c> or
/// <c>ELEMENT_TYPE_CLASS</c> signature token — recoverable locally, with no
/// external assembly resolution. <see cref="Unknown"/> when the type was reached
/// outside a signature context (where the token is absent). Provenance, not
/// identity: deliberately excluded from <see cref="TypeRef.Equals(TypeRef)"/> so
/// the same type compares equal whether or not the hint happened to be present.
/// </summary>
public enum ValueTypeHint
{
    /// <summary>No signature token was available; value-type-ness is unknown here.</summary>
    Unknown,
    /// <summary>Decoded from <c>ELEMENT_TYPE_VALUETYPE</c> (0x11) — a struct/enum.</summary>
    ValueType,
    /// <summary>Decoded from <c>ELEMENT_TYPE_CLASS</c> (0x12) — a reference type.</summary>
    ReferenceType,
}

readonly record struct TypeRefCustomModifier(bool IsRequired, string Namespace, string Name);

/// <summary>
/// Symbolic type identity for the pipeline (docs/decompiler-ir.md):
/// assembly identity, name, and shape as a structured, comparable value.
/// Equality is semantic — structural over the shape, never textual.
/// Rendering is a printer concern; <see cref="ToDisplayString"/> exists for
/// diagnostics and tests, not as the product output path.
/// </summary>
/// <remarks>
/// Core-first slice, two deliberate gaps versus the full design contract:
/// no definition token (identity is assembly + name, which cannot
/// distinguish two same-named types in one assembly scope), and generic
/// parameters compare by index and kind only — without owner identity,
/// parameter equality is meaningful only within a single owner's scope.
/// </remarks>
public sealed class TypeRef : IEquatable<TypeRef>
{
    /// <summary>Canonical assembly identity for primitives and other corelib types, regardless of which facade spelled them.</summary>
    public const string CoreLibrary = "corelib";

    TypeRef(TypeRefKind kind)
    {
        Kind = kind;
        Namespace = "";
        Name = "";
        TypeArguments = [];
    }

    public TypeRefKind Kind { get; private init; }

    /// <summary>Simple assembly name the type resolves through (or <see cref="CoreLibrary"/>). Empty for shapes.</summary>
    public string Assembly { get; private init; } = "";

    public string Namespace { get; private init; }

    /// <summary>Metadata name, including arity backtick for generic definitions (e.g. <c>List`1</c>). Nested types use <c>Declaring+Nested</c>.</summary>
    public string Name { get; private init; }

    public TypeRef? ElementType { get; private init; }

    public ImmutableArray<TypeRef> TypeArguments { get; private init; }

    public int Rank { get; private init; }

    /// <summary>Index for generic parameters; -1 otherwise.</summary>
    public int GenericParameterIndex { get; private init; } = -1;

    /// <summary>Generic parameter name when the importer could recover it (<c>T</c>, <c>TKey</c>); empty otherwise.</summary>
    public string GenericParameterName { get; private init; } = "";

    /// <summary>Why this shape is unsupported; empty for supported shapes.</summary>
    public string UnsupportedReason { get; private init; } = "";

    /// <summary>The C# calling-convention spelling of a function pointer (empty = managed, e.g. <c>unmanaged</c>, <c>unmanaged[Cdecl]</c>); empty otherwise.</summary>
    public string CallingConvention { get; private init; } = "";

    /// <summary>Function-pointer parameter ref-kinds, aligned with <see cref="TypeArguments"/> when <see cref="Kind"/> is <see cref="TypeRefKind.FunctionPointer"/>.</summary>
    public ImmutableArray<ArgumentRefKind> FunctionPointerParameterRefKinds { get; private init; } = [];

    ImmutableArray<TypeRefCustomModifier> CustomModifiers { get; init; } = [];

    /// <summary>
    /// Whether the signature token that produced this type said struct or class
    /// (<c>ELEMENT_TYPE_VALUETYPE</c> vs <c>ELEMENT_TYPE_CLASS</c>), or
    /// <see cref="ValueTypeHint.Unknown"/> when decoded outside a signature.
    /// Provenance, not identity — excluded from equality. Read it through
    /// <see cref="DeclaredValueTypeHint"/>, which unwraps a generic instance to
    /// the definition the token actually qualified.
    /// </summary>
    public ValueTypeHint ValueTypeHint { get; private init; } = ValueTypeHint.Unknown;

    /// <summary>
    /// The value-type-ness recorded for the named definition this type refers
    /// to: a generic instance unwraps to its definition (the
    /// <c>VALUETYPE</c>/<c>CLASS</c> byte qualifies the definition, not the
    /// instantiation), everything else is its own hint. Lets a classifier ask
    /// "is this a struct enumerator?" without external resolution.
    /// </summary>
    public ValueTypeHint DeclaredValueTypeHint =>
        Kind == TypeRefKind.GenericInstance ? ElementType?.ValueTypeHint ?? ValueTypeHint.Unknown : ValueTypeHint;

    /// <summary>
    /// Metadata <c>[InlineArray]</c> evidence on this named definition. Like
    /// <see cref="ValueTypeHint"/>, this is provenance rather than identity and is
    /// intentionally excluded from equality.
    /// </summary>
    public MetadataFactState InlineArray { get; private init; } = MetadataFactState.Unknown;

    public MetadataFactState DeclaredInlineArray =>
        Kind == TypeRefKind.GenericInstance ? ElementType?.InlineArray ?? MetadataFactState.Unknown : InlineArray;

    public static TypeRef Definition(
        string assembly,
        string ns,
        string name,
        ValueTypeHint valueTypeHint = ValueTypeHint.Unknown,
        MetadataFactState inlineArray = MetadataFactState.Unknown)
        => new(TypeRefKind.Definition)
        {
            Assembly = assembly,
            Namespace = ns,
            Name = name,
            ValueTypeHint = valueTypeHint,
            InlineArray = inlineArray,
        };

    /// <summary>
    /// Returns a copy of this named definition carrying <paramref name="hint"/>.
    /// Used to stamp value-type-ness recovered by cross-assembly resolution onto
    /// a bare token whose signature carried no <c>VALUETYPE</c>/<c>CLASS</c> byte.
    /// Only a <see cref="TypeRefKind.Definition"/> is stamped; any other kind is
    /// returned unchanged.
    /// </summary>
    public TypeRef WithValueTypeHint(ValueTypeHint hint)
        => Kind == TypeRefKind.Definition ? Definition(Assembly, Namespace, Name, hint, InlineArray) : this;

    public TypeRef WithInlineArrayFact(MetadataFactState fact)
        => Kind == TypeRefKind.Definition ? Definition(Assembly, Namespace, Name, ValueTypeHint, fact) : this;

    public static TypeRef CoreLib(string ns, string name)
        => Definition(CoreLibrary, ns, name);

    public static TypeRef GenericInstance(TypeRef definition, ImmutableArray<TypeRef> typeArguments)
        => new(TypeRefKind.GenericInstance) { ElementType = definition, TypeArguments = typeArguments };

    public static TypeRef SzArray(TypeRef element) => new(TypeRefKind.SzArray) { ElementType = element };

    public static TypeRef MdArray(TypeRef element, int rank) => new(TypeRefKind.Array) { ElementType = element, Rank = rank };

    public static TypeRef ByRef(TypeRef element) => new(TypeRefKind.ByRef) { ElementType = element };

    public static TypeRef Pointer(TypeRef element) => new(TypeRefKind.Pointer) { ElementType = element };

    public static TypeRef Pinned(TypeRef element) => new(TypeRefKind.Pinned) { ElementType = element };

    public static TypeRef GenericParameter(int index, string name = "")
        => new(TypeRefKind.GenericParameter) { GenericParameterIndex = index, GenericParameterName = name };

    public static TypeRef MethodGenericParameter(int index, string name = "")
        => new(TypeRefKind.MethodGenericParameter) { GenericParameterIndex = index, GenericParameterName = name };

    public static TypeRef Unsupported(string reason)
        => new(TypeRefKind.Unsupported) { UnsupportedReason = reason };

    /// <summary>A function pointer over <paramref name="parameters"/> returning <paramref name="returnType"/>; <paramref name="callingConvention"/> is the C# spelling (empty = managed).</summary>
    public static TypeRef FunctionPointer(TypeRef returnType, ImmutableArray<TypeRef> parameters, string callingConvention)
    {
        bool suppressGcTransition = HasCustomModifier(
            returnType,
            isRequired: false,
            "System.Runtime.CompilerServices",
            "CallConvSuppressGCTransition");
        var cleanReturn = returnType.WithoutCustomModifiers();
        var cleanParameters = ImmutableArray.CreateRange(parameters.Select(p => p.WithoutCustomModifiers()));
        var parameterRefKinds = FunctionPointerParameterRefKindsFor(parameters);
        string convention = AddSuppressGcTransition(callingConvention, suppressGcTransition);
        return FunctionPointer(cleanReturn, cleanParameters, convention, parameterRefKinds);
    }

    static TypeRef FunctionPointer(
        TypeRef returnType,
        ImmutableArray<TypeRef> parameters,
        string callingConvention,
        ImmutableArray<ArgumentRefKind> parameterRefKinds)
        => new(TypeRefKind.FunctionPointer)
        {
            ElementType = returnType,
            TypeArguments = parameters,
            CallingConvention = callingConvention,
            FunctionPointerParameterRefKinds = parameterRefKinds,
        };

    internal static ImmutableArray<ArgumentRefKind> FunctionPointerParameterRefKindsFor(ImmutableArray<TypeRef> parameters)
        => [.. parameters.Select(FunctionPointerParameterRefKind)];

    static ArgumentRefKind FunctionPointerParameterRefKind(TypeRef parameter)
    {
        if (parameter.Kind != TypeRefKind.ByRef)
            return ArgumentRefKind.Value;
        if (HasCustomModifier(parameter, isRequired: true, "System.Runtime.InteropServices", "InAttribute")
            || HasCustomModifier(parameter, isRequired: true, "System.Runtime.CompilerServices", "IsReadOnlyAttribute")
            || HasCustomModifier(parameter, isRequired: true, "System.Runtime.CompilerServices", "RequiresLocationAttribute"))
        {
            return ArgumentRefKind.In;
        }
        if (HasCustomModifier(parameter, isRequired: true, "System.Runtime.InteropServices", "OutAttribute"))
            return ArgumentRefKind.Out;
        return ArgumentRefKind.Ref;
    }

    static string AddSuppressGcTransition(string callingConvention, bool suppressGcTransition)
    {
        if (!suppressGcTransition)
            return callingConvention;
        if (callingConvention.Length == 0 || callingConvention == "unmanaged")
            return "unmanaged[SuppressGCTransition]";
        const string prefix = "unmanaged[";
        return callingConvention.StartsWith(prefix, StringComparison.Ordinal) && callingConvention.EndsWith(']')
            ? callingConvention[..^1] + ", SuppressGCTransition]"
            : callingConvention;
    }

    internal TypeRef WithCustomModifier(TypeRef modifier, bool isRequired)
        => modifier is { Kind: TypeRefKind.Definition }
            ? Copy(customModifiers: CustomModifiers.Add(new TypeRefCustomModifier(isRequired, modifier.Namespace, modifier.Name)))
            : this;

    TypeRef WithoutCustomModifiers()
    {
        TypeRef? element = ElementType?.WithoutCustomModifiers();
        ImmutableArray<TypeRef> typeArguments = TypeArguments;
        bool changed = !CustomModifiers.IsDefaultOrEmpty || !ReferenceEquals(element, ElementType);
        if (!TypeArguments.IsDefaultOrEmpty)
        {
            var builder = ImmutableArray.CreateBuilder<TypeRef>(TypeArguments.Length);
            foreach (var argument in TypeArguments)
            {
                var clean = argument.WithoutCustomModifiers();
                changed |= !ReferenceEquals(clean, argument);
                builder.Add(clean);
            }
            typeArguments = builder.MoveToImmutable();
        }
        return changed ? Copy(element, typeArguments, customModifiers: []) : this;
    }

    static bool HasCustomModifier(TypeRef type, bool isRequired, string ns, string name)
        => type.CustomModifiers.Any(modifier =>
            modifier.IsRequired == isRequired
            && modifier.Namespace == ns
            && modifier.Name == name);

    TypeRef Copy(
        TypeRef? elementType = null,
        ImmutableArray<TypeRef>? typeArguments = null,
        ImmutableArray<TypeRefCustomModifier>? customModifiers = null,
        ImmutableArray<ArgumentRefKind>? functionPointerParameterRefKinds = null)
        => new(Kind)
        {
            Assembly = Assembly,
            Namespace = Namespace,
            Name = Name,
            ElementType = elementType ?? ElementType,
            TypeArguments = typeArguments ?? TypeArguments,
            Rank = Rank,
            GenericParameterIndex = GenericParameterIndex,
            GenericParameterName = GenericParameterName,
            UnsupportedReason = UnsupportedReason,
            CallingConvention = CallingConvention,
            FunctionPointerParameterRefKinds = functionPointerParameterRefKinds ?? FunctionPointerParameterRefKinds,
            ValueTypeHint = ValueTypeHint,
            InlineArray = InlineArray,
            CustomModifiers = customModifiers ?? CustomModifiers,
        };

    /// <summary>
    /// Substitutes generic parameters with the given arguments (type
    /// parameters from a generic instantiation, method parameters from a
    /// MethodSpec). Returns this instance when nothing substitutes.
    /// </summary>
    public TypeRef Instantiate(ImmutableArray<TypeRef> typeArguments, ImmutableArray<TypeRef> methodArguments)
    {
        switch (Kind)
        {
            case TypeRefKind.GenericParameter when GenericParameterIndex >= 0 && GenericParameterIndex < typeArguments.Length:
                return typeArguments[GenericParameterIndex];
            case TypeRefKind.MethodGenericParameter when GenericParameterIndex >= 0 && GenericParameterIndex < methodArguments.Length:
                return methodArguments[GenericParameterIndex];
            case TypeRefKind.SzArray or TypeRefKind.Array or TypeRefKind.ByRef or TypeRefKind.Pointer or TypeRefKind.Pinned:
            {
                var element = ElementType!.Instantiate(typeArguments, methodArguments);
                return ReferenceEquals(element, ElementType) ? this : new TypeRef(Kind) { ElementType = element, Rank = Rank };
            }
            case TypeRefKind.GenericInstance:
            {
                var definition = ElementType!.Instantiate(typeArguments, methodArguments);
                var arguments = TypeArguments;
                bool changed = !ReferenceEquals(definition, ElementType);
                var builder = ImmutableArray.CreateBuilder<TypeRef>(arguments.Length);
                foreach (var argument in arguments)
                {
                    var substituted = argument.Instantiate(typeArguments, methodArguments);
                    changed |= !ReferenceEquals(substituted, argument);
                    builder.Add(substituted);
                }
                return changed ? GenericInstance(definition, builder.MoveToImmutable()) : this;
            }
            case TypeRefKind.FunctionPointer:
            {
                var returnType = ElementType!.Instantiate(typeArguments, methodArguments);
                bool changed = !ReferenceEquals(returnType, ElementType);
                var builder = ImmutableArray.CreateBuilder<TypeRef>(TypeArguments.Length);
                foreach (var parameter in TypeArguments)
                {
                    var substituted = parameter.Instantiate(typeArguments, methodArguments);
                    changed |= !ReferenceEquals(substituted, parameter);
                    builder.Add(substituted);
                }
                return changed ? FunctionPointer(returnType, builder.MoveToImmutable(), CallingConvention, FunctionPointerParameterRefKinds) : this;
            }
            default:
                return this;
        }
    }

    /// <summary>True if this type or any constituent shape cannot be represented as valid C# (feeds the fidelity computation).</summary>
    public bool ContainsUnsupported =>
        Kind == TypeRefKind.Unsupported
        || IsPrivateImplementationDetails
        || ElementType?.ContainsUnsupported == true
        || TypeArguments.Any(a => a.ContainsUnsupported);

    /// <summary>
    /// The reasons of every unsupported shape or unspellable implementation-detail
    /// type reachable from this type (element types and type arguments included), in pre-order.
    /// Drives the importer's type-level diagnostics so a signature the slice
    /// cannot represent (a function pointer, a custom modifier) reports *why* it
    /// lowered fidelity instead of sinking it silently.
    /// </summary>
    public IEnumerable<string> UnsupportedReasons()
    {
        if (Kind == TypeRefKind.Unsupported)
            yield return UnsupportedReason;
        if (IsPrivateImplementationDetails)
            yield return $"unspellable C# type name '{ToDisplayString()}'";
        if (ElementType is { } element)
            foreach (var reason in element.UnsupportedReasons())
                yield return reason;
        foreach (var argument in TypeArguments)
            foreach (var reason in argument.UnsupportedReasons())
                yield return reason;
    }

    public bool Equals(TypeRef? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (Kind != other.Kind
            || Assembly != other.Assembly
            || Namespace != other.Namespace
            || Name != other.Name
            || Rank != other.Rank
            || GenericParameterIndex != other.GenericParameterIndex
            || UnsupportedReason != other.UnsupportedReason
            || CallingConvention != other.CallingConvention
            || FunctionPointerParameterRefKinds.Length != other.FunctionPointerParameterRefKinds.Length
            || !Equals(ElementType, other.ElementType)
            || TypeArguments.Length != other.TypeArguments.Length)
        {
            return false;
        }
        for (int i = 0; i < TypeArguments.Length; i++)
        {
            if (!TypeArguments[i].Equals(other.TypeArguments[i]))
                return false;
        }
        for (int i = 0; i < FunctionPointerParameterRefKinds.Length; i++)
        {
            if (FunctionPointerParameterRefKinds[i] != other.FunctionPointerParameterRefKinds[i])
                return false;
        }
        // GenericParameterName is a naming aid, not identity: 'T' at index 0
        // and 'TKey' at index 0 of the same owner are the same parameter.
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as TypeRef);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Assembly);
        hash.Add(Namespace);
        hash.Add(Name);
        hash.Add(Rank);
        hash.Add(GenericParameterIndex);
        hash.Add(CallingConvention);
        foreach (var kind in FunctionPointerParameterRefKinds)
            hash.Add(kind);
        hash.Add(ElementType);
        foreach (var arg in TypeArguments)
            hash.Add(arg);
        return hash.ToHashCode();
    }

    /// <summary>Diagnostic/test rendering in C# style. Product output paths render at the printer, not here.</summary>
    public string ToDisplayString() => ToDisplayString(null);

    /// <summary>
    /// Scope-aware C# rendering for the product printer. A nested type of a
    /// generic enclosing type is qualified through its declaring type
    /// (<c>ImmutableArray&lt;string&gt;.Builder</c>) UNLESS the reference is made
    /// from within that enclosing type (<paramref name="scope"/> — the declaring
    /// type of the method being printed), where the innermost simple name is in
    /// scope and idiomatic (<c>Enumerator</c> inside <c>List&lt;T&gt;.GetEnumerator</c>).
    /// A null scope keeps the bare innermost spelling used by diagnostics and tests.
    /// </summary>
    public string ToDisplayString(TypeRef? scope) => Kind switch
    {
        TypeRefKind.Definition => DisplayName(),
        TypeRefKind.GenericInstance => RenderGenericInstance(scope),
        TypeRefKind.SzArray => $"{ElementType!.ToDisplayString(scope)}[]",
        TypeRefKind.Array => $"{ElementType!.ToDisplayString(scope)}[{new string(',', Rank - 1)}]",
        TypeRefKind.ByRef => $"ref {ElementType!.ToDisplayString(scope)}",
        TypeRefKind.Pointer => $"{ElementType!.ToDisplayString(scope)}*",
        TypeRefKind.Pinned => $"pinned {ElementType!.ToDisplayString(scope)}",
        TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter =>
            GenericParameterName.Length > 0 ? CSharpNaming.EscapeIdentifier(GenericParameterName) : $"!{GenericParameterIndex}",
        TypeRefKind.FunctionPointer => RenderFunctionPointer(scope),
        _ => $"<unsupported: {UnsupportedReason}>",
    };

    public override string ToString() => ToDisplayString();

    bool IsPrivateImplementationDetails
        => Kind == TypeRefKind.Definition
            && (Name == "<PrivateImplementationDetails>"
                || Name.StartsWith("<PrivateImplementationDetails>+", StringComparison.Ordinal));

    string DisplayName()
    {
        if (Assembly == CoreLibrary && Namespace == "System" && s_keywords.TryGetValue(Name, out var keyword))
            return keyword;
        // Nested types carry the metadata `Outer+Inner` name; C# refers to
        // them by the innermost simple name (the namespace-stripping
        // convention extended inward), so `Interop+Error` renders `Error`.
        int nested = Name.LastIndexOf('+');
        string innermost = nested < 0 ? Name : Name[(nested + 1)..];
        return CSharpNaming.TypeNameSegment(innermost);
    }

    /// <summary>
    /// A generic instance shows only the innermost segment's own type
    /// arguments. The metadata name's cumulative arity counts the enclosing
    /// types' parameters too — but in the innermost-only spelling they belong
    /// to the (elided) outer name, so attaching them is invalid C#:
    /// <c>List`1+Enumerator</c> is <c>Enumerator</c>, never <c>Enumerator&lt;T&gt;</c>
    /// (CS0308); <c>Outer`1+Inner`1</c> is <c>Inner&lt;TInner&gt;</c>.
    ///
    /// That innermost spelling is only valid from inside the enclosing type. When
    /// a <paramref name="scope"/> is given and this nested type's enclosing type
    /// is not that scope, the reference is foreign and must be qualified through
    /// the declaring chain (<c>ImmutableArray&lt;string&gt;.Builder</c>, not a
    /// bare <c>Builder</c> that fails CS0246).
    /// </summary>
    string RenderGenericInstance(TypeRef? scope = null)
    {
        if (scope is not null
            && ElementType!.Name.Contains('+')
            && !EnclosingInScope(scope)
            && RenderNestedGenericInstance(scope) is { } qualified)
        {
            return qualified;
        }
        int nested = ElementType!.Name.LastIndexOf('+');
        string innermost = nested < 0 ? ElementType.Name : ElementType.Name[(nested + 1)..];
        int ownArity = ArityOf(innermost);
        string simpleName = ElementType.DisplayName();
        if (ownArity == 0)
            return simpleName;
        var ownArguments = TypeArguments.Skip(Math.Max(0, TypeArguments.Length - ownArity));
        return $"{simpleName}<{string.Join(", ", ownArguments.Select(a => a.ToDisplayString(scope)))}>";
    }

    /// <summary>
    /// True when the innermost simple name is in scope: the printing context is
    /// inside the enclosing type (<paramref name="scope"/> is that type or a type
    /// nested in it) AND this is the enclosing type's own instantiation. The
    /// second condition matters because a bare <c>Inner</c> inside <c>Outer&lt;T&gt;</c>
    /// binds to <c>Outer&lt;T&gt;.Inner</c>, so a reference to a different
    /// instantiation (<c>Outer&lt;int&gt;.Inner</c>) is foreign and must qualify —
    /// the enclosing-segment arguments must be the enclosing type's own generic
    /// parameters in order (<c>T0, T1, …</c>) to stay innermost.
    /// </summary>
    bool EnclosingInScope(TypeRef scope)
    {
        var scopeDefinition = scope.Kind == TypeRefKind.GenericInstance ? scope.ElementType! : scope;
        string name = ElementType!.Name;
        string enclosing = name[..name.LastIndexOf('+')];
        if (ElementType.Assembly != scopeDefinition.Assembly
            || ElementType.Namespace != scopeDefinition.Namespace
            || (enclosing != scopeDefinition.Name
                && !scopeDefinition.Name.StartsWith(enclosing + "+", StringComparison.Ordinal)))
        {
            return false;
        }
        int innermostArity = ArityOf(name[(name.LastIndexOf('+') + 1)..]);
        int enclosingArguments = TypeArguments.Length - innermostArity;
        for (int i = 0; i < enclosingArguments; i++)
        {
            var argument = TypeArguments[i];
            if (argument.Kind != TypeRefKind.GenericParameter || argument.GenericParameterIndex != i)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Renders a nested generic instance through its declaring chain
    /// (<c>Outer&lt;A&gt;.Inner&lt;B&gt;</c>), distributing the flat type-argument
    /// list across each <c>+</c>-separated segment by that segment's own arity.
    /// Returns null when the per-segment arities do not sum to the argument count
    /// (an unexpected metadata encoding) so the caller falls back to the safe
    /// innermost spelling rather than emitting wrong C#.
    /// </summary>
    string? RenderNestedGenericInstance(TypeRef? scope)
    {
        var segments = ElementType!.Name.Split('+');
        var arities = Array.ConvertAll(segments, ArityOf);
        int total = 0;
        foreach (int arity in arities)
            total += arity;
        if (total != TypeArguments.Length)
            return null;

        var parts = new List<string>(segments.Length);
        int argIndex = 0;
        for (int i = 0; i < segments.Length; i++)
        {
            string name = CSharpNaming.TypeNameSegment(segments[i]);
            int arity = arities[i];
            if (arity > 0)
            {
                var segmentArguments = TypeArguments.Skip(argIndex).Take(arity);
                name = $"{name}<{string.Join(", ", segmentArguments.Select(a => a.ToDisplayString(scope)))}>";
                argIndex += arity;
            }
            parts.Add(name);
        }
        return string.Join(".", parts);
    }

    /// <summary>
    /// A function pointer in C# spelling: <c>delegate*&lt;p1, …, returnType&gt;</c>,
    /// the return type last, with the calling-convention keyword (when the
    /// pointer is unmanaged) between <c>delegate*</c> and the type list.
    /// </summary>
    string RenderFunctionPointer(TypeRef? scope = null)
    {
        var parts = TypeArguments.Select((p, index) => FunctionPointerParameterText(p, index, scope)).Append(ElementType!.ToDisplayString(scope));
        string convention = CallingConvention.Length > 0 ? $" {CallingConvention}" : "";
        return $"delegate*{convention}<{string.Join(", ", parts)}>";
    }

    string FunctionPointerParameterText(TypeRef parameter, int index, TypeRef? scope)
    {
        var kind = index >= 0 && index < FunctionPointerParameterRefKinds.Length
            ? FunctionPointerParameterRefKinds[index]
            : parameter.Kind == TypeRefKind.ByRef ? ArgumentRefKind.Ref : ArgumentRefKind.Value;
        if (kind == ArgumentRefKind.Value || parameter.Kind != TypeRefKind.ByRef)
            return parameter.ToDisplayString(scope);
        string element = parameter.ElementType!.ToDisplayString(scope);
        return kind switch
        {
            ArgumentRefKind.In => $"in {element}",
            ArgumentRefKind.Out => $"out {element}",
            _ => $"ref {element}",
        };
    }

    static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    /// <summary>The generic arity encoded in a metadata name's trailing <c>`N</c>; 0 when absent.</summary>
    static int ArityOf(string name)
    {
        int tick = name.IndexOf('`');
        return tick >= 0 && int.TryParse(name[(tick + 1)..], out int arity) ? arity : 0;
    }

    static readonly Dictionary<string, string> s_keywords = new()
    {
        ["Boolean"] = "bool", ["Byte"] = "byte", ["SByte"] = "sbyte",
        ["Char"] = "char", ["Int16"] = "short", ["UInt16"] = "ushort",
        ["Int32"] = "int", ["UInt32"] = "uint", ["Int64"] = "long",
        ["UInt64"] = "ulong", ["Single"] = "float", ["Double"] = "double",
        ["Decimal"] = "decimal",
        ["IntPtr"] = "nint", ["UIntPtr"] = "nuint", ["String"] = "string",
        ["Object"] = "object", ["Void"] = "void",
    };
}
