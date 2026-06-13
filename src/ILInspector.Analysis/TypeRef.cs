using System.Collections.Immutable;

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

    public static TypeRef Definition(string assembly, string ns, string name)
        => new(TypeRefKind.Definition) { Assembly = CanonicalAssembly(assembly), Namespace = ns, Name = name };

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
        int nested = Name.LastIndexOf('+');
        string innermost = nested < 0 ? Name : Name[(nested + 1)..];
        return StripArity(innermost);
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
        ["IntPtr"] = "nint", ["UIntPtr"] = "nuint", ["String"] = "string",
        ["Object"] = "object", ["Void"] = "void",
    };
}
