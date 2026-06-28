using System;
using System.Collections.Immutable;
using System.Linq;

namespace ILInspector.Analysis;

public enum TypeRefKind
{
    Definition,
    GenericInstance,
    SzArray,
    Array,
    ByRef,
    Pointer,
    Pinned,
    GenericParameter,
    MethodGenericParameter,
    Unsupported,
}

/// <summary>
/// Semantic type identity for IL analysis. Display names are for humans; equality
/// is structural and canonicalizes core-library facade spellings.
/// </summary>
public sealed class TypeRef : IEquatable<TypeRef>
{
    public const string CoreLibrary = "corelib";

    TypeRef(TypeRefKind kind)
    {
        Kind = kind;
        Assembly = "";
        Namespace = "";
        Name = "";
        TypeArguments = [];
    }

    public TypeRefKind Kind { get; private init; }
    public string Assembly { get; private init; }
    public string Namespace { get; private init; }
    public string Name { get; private init; }
    public TypeRef? ElementType { get; private init; }
    public ImmutableArray<TypeRef> TypeArguments { get; private init; }
    public int Rank { get; private init; }
    public int GenericParameterIndex { get; private init; } = -1;
    public string GenericParameterName { get; private init; } = "";
    public string UnsupportedReason { get; private init; } = "";

    // Whether this type's declaring assembly carries a known Microsoft framework
    // public-key-token (#1708 Row A). Advisory classification used by the framework
    // signal predicates to reject simple-name spoofs (a user assembly named
    // "System.Linq" exposing System.Linq.Enumerable). Defaults to true so synthetic
    // and corelib refs stay trusted; only the decoder lowers it for a decoded
    // reference whose assembly is not framework-signed. Excluded from equality/hash:
    // it is derived metadata, not part of structural type identity.
    public bool TrustedFrameworkAssembly { get; private init; } = true;

    // Whether this type's declaring assembly identity is NOT a Google.Protobuf spoof: false
    // only for a type resolved through a reference (or self-assembly) named Google.Protobuf
    // whose public-key-token is not the real one (#1735). Stamped per decoded reference so
    // generated-code suppression rejects a spoofed Google.Protobuf reference even when an
    // authentic one coexists. Defaults to true (synthetic/corelib refs and all non-protobuf
    // identities are not spoofs). Excluded from equality/hash: derived metadata, not identity.
    public bool TrustedProtobufAssembly { get; private init; } = true;

    public static TypeRef Definition(string assembly, string ns, string name, bool trustedFrameworkAssembly = true, bool trustedProtobufAssembly = true)
        => new(TypeRefKind.Definition)
        {
            Assembly = CanonicalAssembly(assembly),
            Namespace = ns,
            Name = name,
            TrustedFrameworkAssembly = trustedFrameworkAssembly,
            TrustedProtobufAssembly = trustedProtobufAssembly,
        };

    public static TypeRef CoreLib(string ns, string name) => Definition(CoreLibrary, ns, name);

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
    public static TypeRef Unsupported(string reason) => new(TypeRefKind.Unsupported) { UnsupportedReason = reason };

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
                bool changed = !ReferenceEquals(definition, ElementType);
                var builder = ImmutableArray.CreateBuilder<TypeRef>(TypeArguments.Length);
                foreach (var argument in TypeArguments)
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

    public string ToDisplayString() => Kind switch
    {
        TypeRefKind.Definition => DisplayName(),
        TypeRefKind.GenericInstance => RenderGenericInstance(qualified: false),
        TypeRefKind.SzArray => $"{ElementType!.ToDisplayString()}[]",
        TypeRefKind.Array => $"{ElementType!.ToDisplayString()}[{new string(',', Rank - 1)}]",
        TypeRefKind.ByRef => $"ref {ElementType!.ToDisplayString()}",
        TypeRefKind.Pointer => $"{ElementType!.ToDisplayString()}*",
        TypeRefKind.Pinned => $"pinned {ElementType!.ToDisplayString()}",
        TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter =>
            GenericParameterName.Length > 0 ? GenericParameterName : $"!{GenericParameterIndex}",
        _ => $"<unsupported: {UnsupportedReason}>",
    };

    public string ToQualifiedDisplayString() => Kind switch
    {
        TypeRefKind.Definition => QualifiedDisplayName(),
        TypeRefKind.GenericInstance => RenderGenericInstance(qualified: true),
        TypeRefKind.SzArray => $"{ElementType!.ToQualifiedDisplayString()}[]",
        TypeRefKind.Array => $"{ElementType!.ToQualifiedDisplayString()}[{new string(',', Rank - 1)}]",
        TypeRefKind.ByRef => $"ref {ElementType!.ToQualifiedDisplayString()}",
        TypeRefKind.Pointer => $"{ElementType!.ToQualifiedDisplayString()}*",
        TypeRefKind.Pinned => $"pinned {ElementType!.ToQualifiedDisplayString()}",
        _ => ToDisplayString(),
    };

    public override string ToString() => ToDisplayString();

    /// <summary>
    /// Whether a pointer or function pointer appears anywhere in this type. This
    /// is the signature-level test that drives the legacy implicit notion of
    /// requires-unsafe (a pointer in a parameter/return type is visible at the
    /// call site). Pinned is a local-only modifier, not a signature pointer, so
    /// it is deliberately excluded — matching Roslyn's signature check.
    /// </summary>
    public bool ContainsPointer()
    {
        if (Kind == TypeRefKind.Pointer)
            return true;
        if (Kind == TypeRefKind.Unsupported
            && UnsupportedReason.Contains("function pointer", StringComparison.OrdinalIgnoreCase))
            return true;
        if (ElementType is not null && ElementType.ContainsPointer())
            return true;
        return TypeArguments.Any(argument => argument.ContainsPointer());
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
        hash.Add(UnsupportedReason);
        hash.Add(ElementType);
        foreach (var argument in TypeArguments)
            hash.Add(argument);
        return hash.ToHashCode();
    }

    string DisplayName()
    {
        if (Assembly == CoreLibrary && Namespace == "System" && s_keywords.TryGetValue(Name, out var keyword))
            return keyword;
        // Preserve the full nested declaring-type path. Metadata joins a containing
        // type and its nested type with '+' (see TypeRefDecoder), so a name like
        // "Left+Dup" must render as "Left.Dup", not the innermost "Dup" alone —
        // collapsing to the innermost segment merges distinct nested types that share
        // a simple name (Left+Dup and Right+Dup). Per-segment arity is stripped.
        if (Name.IndexOf('+') < 0)
            return StripArity(Name);
        return string.Join('.', Name.Split('+').Select(StripArity));
    }

    string QualifiedDisplayName()
    {
        string display = DisplayName();
        if (Assembly == CoreLibrary && Namespace == "System" && s_keywords.ContainsKey(Name))
            return display;
        return Namespace.Length == 0 || display.StartsWith('<') ? display : $"{Namespace}.{display}";
    }

    string RenderGenericInstance(bool qualified)
    {
        int nested = ElementType!.Name.LastIndexOf('+');
        string innermost = nested < 0 ? ElementType.Name : ElementType.Name[(nested + 1)..];
        int ownArity = ArityOf(innermost);
        string simpleName = qualified ? ElementType.QualifiedDisplayName() : ElementType.DisplayName();
        if (ownArity == 0)
            return simpleName;
        var ownArguments = TypeArguments.Skip(Math.Max(0, TypeArguments.Length - ownArity));
        string arguments = string.Join(", ", ownArguments.Select(a => qualified ? a.ToQualifiedDisplayString() : a.ToDisplayString()));
        return $"{simpleName}<{arguments}>";
    }

    static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    static int ArityOf(string name)
    {
        int tick = name.IndexOf('`');
        return tick >= 0 && int.TryParse(name[(tick + 1)..], out int arity) ? arity : 0;
    }

    internal static string CanonicalAssembly(string assemblyName)
        => assemblyName is "System.Private.CoreLib" or "System.Runtime" or "mscorlib" or "netstandard" or "System.Runtime.Extensions"
            ? CoreLibrary
            : assemblyName;

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
