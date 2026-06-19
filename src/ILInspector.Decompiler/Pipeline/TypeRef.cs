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

/// <summary>
/// Symbolic type identity for the replacement pipeline (docs/decompiler-ir.md):
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
/// Both must close before the parity gate makes this the contract.
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

    public static TypeRef Definition(string assembly, string ns, string name, ValueTypeHint valueTypeHint = ValueTypeHint.Unknown)
        => new(TypeRefKind.Definition) { Assembly = assembly, Namespace = ns, Name = name, ValueTypeHint = valueTypeHint };

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
        => new(TypeRefKind.FunctionPointer) { ElementType = returnType, TypeArguments = parameters, CallingConvention = callingConvention };

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
                return changed ? FunctionPointer(returnType, builder.MoveToImmutable(), CallingConvention) : this;
            }
            default:
                return this;
        }
    }

    /// <summary>True if this type or any constituent shape is <see cref="TypeRefKind.Unsupported"/> (feeds the fidelity computation).</summary>
    public bool ContainsUnsupported =>
        Kind == TypeRefKind.Unsupported
        || ElementType?.ContainsUnsupported == true
        || TypeArguments.Any(a => a.ContainsUnsupported);

    /// <summary>
    /// The reasons of every <see cref="TypeRefKind.Unsupported"/> shape reachable
    /// from this type (element types and type arguments included), in pre-order.
    /// Drives the importer's type-level diagnostics so a signature the slice
    /// cannot represent (a function pointer, a custom modifier) reports *why* it
    /// lowered fidelity instead of sinking it silently.
    /// </summary>
    public IEnumerable<string> UnsupportedReasons()
    {
        if (Kind == TypeRefKind.Unsupported)
            yield return UnsupportedReason;
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
        hash.Add(ElementType);
        foreach (var arg in TypeArguments)
            hash.Add(arg);
        return hash.ToHashCode();
    }

    /// <summary>Diagnostic/test rendering in C# style. Product output paths render at the printer, not here.</summary>
    public string ToDisplayString() => Kind switch
    {
        TypeRefKind.Definition => DisplayName(),
        TypeRefKind.GenericInstance => RenderGenericInstance(),
        TypeRefKind.SzArray => $"{ElementType!.ToDisplayString()}[]",
        TypeRefKind.Array => $"{ElementType!.ToDisplayString()}[{new string(',', Rank - 1)}]",
        TypeRefKind.ByRef => $"ref {ElementType!.ToDisplayString()}",
        TypeRefKind.Pointer => $"{ElementType!.ToDisplayString()}*",
        TypeRefKind.Pinned => $"pinned {ElementType!.ToDisplayString()}",
        TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter =>
            GenericParameterName.Length > 0 ? GenericParameterName : $"!{GenericParameterIndex}",
        TypeRefKind.FunctionPointer => RenderFunctionPointer(),
        _ => $"<unsupported: {UnsupportedReason}>",
    };

    public override string ToString() => ToDisplayString();

    string DisplayName()
    {
        if (Assembly == CoreLibrary && Namespace == "System" && s_keywords.TryGetValue(Name, out var keyword))
            return keyword;
        // Nested types carry the metadata `Outer+Inner` name; C# refers to
        // them by the innermost simple name (the namespace-stripping
        // convention extended inward), so `Interop+Error` renders `Error`.
        int nested = Name.LastIndexOf('+');
        string innermost = nested < 0 ? Name : Name[(nested + 1)..];
        return StripArity(innermost);
    }

    /// <summary>
    /// A generic instance shows only the innermost segment's own type
    /// arguments. The metadata name's cumulative arity counts the enclosing
    /// types' parameters too — but in the innermost-only spelling they belong
    /// to the (elided) outer name, so attaching them is invalid C#:
    /// <c>List`1+Enumerator</c> is <c>Enumerator</c>, never <c>Enumerator&lt;T&gt;</c>
    /// (CS0308); <c>Outer`1+Inner`1</c> is <c>Inner&lt;TInner&gt;</c>.
    /// </summary>
    string RenderGenericInstance()
    {
        int nested = ElementType!.Name.LastIndexOf('+');
        string innermost = nested < 0 ? ElementType.Name : ElementType.Name[(nested + 1)..];
        int ownArity = ArityOf(innermost);
        string simpleName = ElementType.DisplayName();
        if (ownArity == 0)
            return simpleName;
        var ownArguments = TypeArguments.Skip(Math.Max(0, TypeArguments.Length - ownArity));
        return $"{simpleName}<{string.Join(", ", ownArguments.Select(a => a.ToDisplayString()))}>";
    }

    /// <summary>
    /// A function pointer in C# spelling: <c>delegate*&lt;p1, …, returnType&gt;</c>,
    /// the return type last, with the calling-convention keyword (when the
    /// pointer is unmanaged) between <c>delegate*</c> and the type list.
    /// </summary>
    string RenderFunctionPointer()
    {
        var parts = TypeArguments.Select(p => p.ToDisplayString()).Append(ElementType!.ToDisplayString());
        string convention = CallingConvention.Length > 0 ? $" {CallingConvention}" : "";
        return $"delegate*{convention}<{string.Join(", ", parts)}>";
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
