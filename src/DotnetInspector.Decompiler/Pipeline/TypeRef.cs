using System.Collections.Immutable;

namespace DotnetInspector.Decompiler.Pipeline;

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
    /// <summary>A shape outside the supported core (function pointers, custom modifiers). Carries a reason; lowers fidelity, never lies.</summary>
    Unsupported,
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

    public static TypeRef Definition(string assembly, string ns, string name)
        => new(TypeRefKind.Definition) { Assembly = assembly, Namespace = ns, Name = name };

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
            default:
                return this;
        }
    }

    /// <summary>True if this type or any constituent shape is <see cref="TypeRefKind.Unsupported"/> (feeds the fidelity computation).</summary>
    public bool ContainsUnsupported =>
        Kind == TypeRefKind.Unsupported
        || ElementType?.ContainsUnsupported == true
        || TypeArguments.Any(a => a.ContainsUnsupported);

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
        hash.Add(ElementType);
        foreach (var arg in TypeArguments)
            hash.Add(arg);
        return hash.ToHashCode();
    }

    /// <summary>Diagnostic/test rendering in C# style. Product output paths render at the printer, not here.</summary>
    public string ToDisplayString() => Kind switch
    {
        TypeRefKind.Definition => DisplayName(),
        TypeRefKind.GenericInstance =>
            $"{StripArity(ElementType!.DisplayName())}<{string.Join(", ", TypeArguments.Select(a => a.ToDisplayString()))}>",
        TypeRefKind.SzArray => $"{ElementType!.ToDisplayString()}[]",
        TypeRefKind.Array => $"{ElementType!.ToDisplayString()}[{new string(',', Rank - 1)}]",
        TypeRefKind.ByRef => $"ref {ElementType!.ToDisplayString()}",
        TypeRefKind.Pointer => $"{ElementType!.ToDisplayString()}*",
        TypeRefKind.Pinned => $"pinned {ElementType!.ToDisplayString()}",
        TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter =>
            GenericParameterName.Length > 0 ? GenericParameterName : $"!{GenericParameterIndex}",
        _ => $"<unsupported: {UnsupportedReason}>",
    };

    public override string ToString() => ToDisplayString();

    string DisplayName()
    {
        if (Assembly == CoreLibrary && Namespace == "System" && s_keywords.TryGetValue(Name, out var keyword))
            return keyword;
        return Name;
    }

    static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    static readonly Dictionary<string, string> s_keywords = new()
    {
        ["Boolean"] = "bool", ["Byte"] = "byte", ["SByte"] = "sbyte",
        ["Char"] = "char", ["Int16"] = "short", ["UInt16"] = "ushort",
        ["Int32"] = "int", ["UInt32"] = "uint", ["Int64"] = "long",
        ["UInt64"] = "ulong", ["Single"] = "float", ["Double"] = "double",
        ["IntPtr"] = "nint", ["UIntPtr"] = "nuint", ["String"] = "string",
        ["Object"] = "object", ["Void"] = "void",
    };
}
