using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using CSharpText;
using ILInspector.Metadata;

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
    public MetadataTypeNameFailure? MetadataNameFailure { get; private init; }

    // Preserve identity-bearing signature payload for catalog correspondence
    // without changing existing Unsupported structural equality or display.
    internal TypeRef? UnmodifiedType { get; private init; }
    internal TypeRef? ModifierType { get; private init; }
    internal bool IsRequiredModifier { get; private init; }
    internal MethodSignature<TypeRef>? FunctionPointerSignature
        { get; private init; }

    // Retained exact signature shape for identity-sensitive consumers. Legacy
    // TypeRef equality and display remain rank-based.
    internal ImmutableArray<int> ArraySizes { get; private init; } = [];
    internal ImmutableArray<int> ArrayLowerBounds { get; private init; } = [];
    internal byte RawTypeKind { get; set; }

    /// <summary>
    /// Decoder-retained origin and exact metadata name. The origin is
    /// provenance, while the name's segments participate in definition
    /// identity so literal delimiter text cannot alias nesting.
    /// </summary>
    public ResolvableTypeReference? Resolution { get; private init; }

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
        => Definition(
            assembly,
            ns,
            name,
            resolution: null,
            trustedFrameworkAssembly,
            trustedProtobufAssembly);

    internal static TypeRef Definition(
        string assembly,
        string ns,
        string name,
        ResolvableTypeReference? resolution,
        bool trustedFrameworkAssembly = true,
        bool trustedProtobufAssembly = true,
        byte rawTypeKind = 0)
        => new(TypeRefKind.Definition)
        {
            Assembly = CanonicalAssembly(assembly),
            Namespace = ns,
            Name = name,
            Resolution = resolution,
            TrustedFrameworkAssembly = trustedFrameworkAssembly,
            TrustedProtobufAssembly = trustedProtobufAssembly,
            RawTypeKind = rawTypeKind,
        };

    public static TypeRef CoreLib(string ns, string name) => Definition(CoreLibrary, ns, name);

    public static TypeRef GenericInstance(TypeRef definition, ImmutableArray<TypeRef> typeArguments)
        => new(TypeRefKind.GenericInstance) { ElementType = definition, TypeArguments = typeArguments };

    public static TypeRef SzArray(TypeRef element) => new(TypeRefKind.SzArray) { ElementType = element };
    public static TypeRef MdArray(TypeRef element, int rank) => new(TypeRefKind.Array) { ElementType = element, Rank = rank };
    internal static TypeRef MdArray(TypeRef element, ArrayShape shape) =>
        new(TypeRefKind.Array)
        {
            ElementType = element,
            Rank = shape.Rank,
            ArraySizes = shape.Sizes,
            ArrayLowerBounds = shape.LowerBounds,
        };
    public static TypeRef ByRef(TypeRef element) => new(TypeRefKind.ByRef) { ElementType = element };
    public static TypeRef Pointer(TypeRef element) => new(TypeRefKind.Pointer) { ElementType = element };
    public static TypeRef Pinned(TypeRef element) => new(TypeRefKind.Pinned) { ElementType = element };
    public static TypeRef GenericParameter(int index, string name = "")
        => new(TypeRefKind.GenericParameter) { GenericParameterIndex = index, GenericParameterName = name };
    public static TypeRef MethodGenericParameter(int index, string name = "")
        => new(TypeRefKind.MethodGenericParameter) { GenericParameterIndex = index, GenericParameterName = name };
    public static TypeRef Unsupported(
        string reason,
        MetadataTypeNameFailure? metadataNameFailure = null)
        => new(TypeRefKind.Unsupported)
        {
            UnsupportedReason = reason,
            MetadataNameFailure = metadataNameFailure,
        };

    internal static TypeRef UnsupportedModified(
        TypeRef modifier,
        TypeRef unmodifiedType,
        bool isRequired)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        ArgumentNullException.ThrowIfNull(unmodifiedType);
        return new(TypeRefKind.Unsupported)
        {
            UnsupportedReason =
                $"custom modifier ({(isRequired ? "modreq" : "modopt")} "
                + $"{modifier.ToDisplayString()})",
            ModifierType = modifier,
            UnmodifiedType = unmodifiedType,
            IsRequiredModifier = isRequired,
        };
    }

    internal static TypeRef UnsupportedFunctionPointer(
        MethodSignature<TypeRef> signature) =>
        new(TypeRefKind.Unsupported)
        {
            UnsupportedReason = "function pointer",
            FunctionPointerSignature = signature,
        };

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
                return ReferenceEquals(element, ElementType)
                    ? this
                    : new TypeRef(Kind)
                    {
                        ElementType = element,
                        Rank = Rank,
                        ArraySizes = ArraySizes,
                        ArrayLowerBounds = ArrayLowerBounds,
                    };
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
        int fastPathBudget = 64;
        TypeRefComparison fastPath = TryEqualsShallow(
            this,
            other,
            depth: 0,
            ref fastPathBudget);
        if (fastPath != TypeRefComparison.Fallback)
            return fastPath == TypeRefComparison.Equal;

        var pending = new Stack<(TypeRef Left, TypeRef Right)>();
        var visited = new HashSet<(TypeRef Left, TypeRef Right)>(
            TypeRefPairReferenceComparer.Instance);
        pending.Push((this, other));
        while (pending.Count > 0)
        {
            (TypeRef left, TypeRef right) = pending.Pop();
            if (ReferenceEquals(left, right)
                || !visited.Add((left, right)))
            {
                continue;
            }
            if (left.Kind != right.Kind
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    left.Assembly,
                    right.Assembly)
                || left.Namespace != right.Namespace
                || !SameNameIdentity(left, right)
                || left.Rank != right.Rank
                || left.GenericParameterIndex
                    != right.GenericParameterIndex
                || left.UnsupportedReason
                    != right.UnsupportedReason
                || (left.ElementType is null)
                    != (right.ElementType is null)
                || left.TypeArguments.Length
                    != right.TypeArguments.Length)
            {
                return false;
            }
            if (left.ElementType is not null)
            {
                pending.Push((
                    left.ElementType,
                    right.ElementType!));
            }
            for (int i = 0;
                i < left.TypeArguments.Length;
                i++)
            {
                pending.Push((
                    left.TypeArguments[i],
                    right.TypeArguments[i]));
            }
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as TypeRef);

    public override int GetHashCode()
    {
        int fastPathBudget = 64;
        if (TryStructuralHashShallow(
                this,
                depth: 0,
                ref fastPathBudget,
                out int hash))
        {
            return hash;
        }
        return StructuralHash(
            this,
            new Dictionary<TypeRef, int>(
                ReferenceEqualityComparer.Instance));
    }

    static TypeRefComparison TryEqualsShallow(
        TypeRef left,
        TypeRef right,
        int depth,
        ref int budget)
    {
        if (ReferenceEquals(left, right))
            return TypeRefComparison.Equal;
        if (left.Kind != right.Kind
            || !StringComparer.OrdinalIgnoreCase.Equals(
                left.Assembly,
                right.Assembly)
            || left.Namespace != right.Namespace
            || !SameNameIdentity(left, right)
            || left.Rank != right.Rank
            || left.GenericParameterIndex
                != right.GenericParameterIndex
            || left.UnsupportedReason
                != right.UnsupportedReason
            || (left.ElementType is null)
                != (right.ElementType is null)
            || left.TypeArguments.Length
                != right.TypeArguments.Length)
        {
            return TypeRefComparison.NotEqual;
        }
        if (budget-- == 0 || depth == 8)
            return TypeRefComparison.Fallback;
        if (left.ElementType is not null)
        {
            TypeRefComparison element = TryEqualsShallow(
                left.ElementType,
                right.ElementType!,
                depth + 1,
                ref budget);
            if (element != TypeRefComparison.Equal)
                return element;
        }
        for (int i = 0; i < left.TypeArguments.Length; i++)
        {
            TypeRefComparison argument = TryEqualsShallow(
                left.TypeArguments[i],
                right.TypeArguments[i],
                depth + 1,
                ref budget);
            if (argument != TypeRefComparison.Equal)
                return argument;
        }
        return TypeRefComparison.Equal;
    }

    static bool TryStructuralHashShallow(
        TypeRef type,
        int depth,
        ref int budget,
        out int result)
    {
        result = 0;
        if (budget-- == 0 || depth == 8)
            return false;
        var hash = new HashCode();
        hash.Add(type.Kind);
        hash.Add(
            type.Assembly,
            StringComparer.OrdinalIgnoreCase);
        hash.Add(type.Namespace);
        AddNameIdentity(ref hash, type);
        hash.Add(type.Rank);
        hash.Add(type.GenericParameterIndex);
        hash.Add(type.UnsupportedReason);
        if (type.ElementType is null)
        {
            hash.Add(0);
        }
        else
        {
            if (!TryStructuralHashShallow(
                    type.ElementType,
                    depth + 1,
                    ref budget,
                    out int elementHash))
            {
                return false;
            }
            hash.Add(elementHash);
        }
        foreach (TypeRef argument in type.TypeArguments)
        {
            if (!TryStructuralHashShallow(
                    argument,
                    depth + 1,
                    ref budget,
                    out int argumentHash))
            {
                return false;
            }
            hash.Add(argumentHash);
        }
        result = hash.ToHashCode();
        return true;
    }

    static int StructuralHash(
        TypeRef type,
        Dictionary<TypeRef, int> memo)
    {
        if (memo.TryGetValue(type, out int cached))
            return cached;
        var hash = new HashCode();
        hash.Add(type.Kind);
        hash.Add(
            type.Assembly,
            StringComparer.OrdinalIgnoreCase);
        hash.Add(type.Namespace);
        AddNameIdentity(ref hash, type);
        hash.Add(type.Rank);
        hash.Add(type.GenericParameterIndex);
        hash.Add(type.UnsupportedReason);
        hash.Add(
            type.ElementType is null
                ? 0
                : StructuralHash(
                    type.ElementType,
                    memo));
        foreach (TypeRef argument in type.TypeArguments)
        {
            hash.Add(StructuralHash(argument, memo));
        }
        int result = hash.ToHashCode();
        memo.Add(type, result);
        return result;
    }

    sealed class TypeRefPairReferenceComparer
        : IEqualityComparer<(TypeRef Left, TypeRef Right)>
    {
        internal static TypeRefPairReferenceComparer Instance
            { get; } = new();

        public bool Equals(
            (TypeRef Left, TypeRef Right) x,
            (TypeRef Left, TypeRef Right) y)
            => ReferenceEquals(x.Left, y.Left)
                && ReferenceEquals(x.Right, y.Right);

        public int GetHashCode(
            (TypeRef Left, TypeRef Right) pair)
            => HashCode.Combine(
                RuntimeHelpers.GetHashCode(pair.Left),
                RuntimeHelpers.GetHashCode(pair.Right));
    }

    enum TypeRefComparison
    {
        Equal,
        NotEqual,
        Fallback,
    }

    static bool SameNameIdentity(TypeRef left, TypeRef right)
    {
        if (left.Kind != TypeRefKind.Definition)
            return left.Name == right.Name;
        MetadataTypeDefinitionName? leftName =
            left.Resolution?.Type;
        MetadataTypeDefinitionName? rightName =
            right.Resolution?.Type;
        if (leftName is null)
            return rightName is null
                ? left.Name == right.Name
                : SameUnambiguousLegacyName(left.Name, rightName);
        if (rightName is null)
            return SameUnambiguousLegacyName(right.Name, leftName);
        return leftName == rightName;
    }

    static bool SameUnambiguousLegacyName(
        string legacyName,
        MetadataTypeDefinitionName exactName) =>
        exactName.Segments.Length == 1
        && IsUnambiguousLegacyName(legacyName)
        && exactName.Segments[0] == legacyName;

    static bool IsUnambiguousLegacyName(string name) =>
        name.IndexOf('.') < 0
        && name.IndexOf('+') < 0
        && name.IndexOf('\\') < 0;

    static void AddNameIdentity(ref HashCode hash, TypeRef type)
    {
        if (type.Kind != TypeRefKind.Definition)
        {
            hash.Add(type.Name);
            return;
        }

        if (type.Resolution?.Type is not { } exactName)
        {
            hash.Add(0);
            hash.Add(type.Name);
            return;
        }

        if (exactName.Segments.Length == 1
            && IsUnambiguousLegacyName(exactName.Segments[0]))
        {
            hash.Add(0);
            hash.Add(exactName.Segments[0]);
        }
        else
        {
            hash.Add(1);
            hash.Add(exactName);
        }
    }

    string DisplayName()
    {
        if (Assembly == CoreLibrary && Namespace == "System" && PrimitiveTypeNames.TryToKeywordForSystemType(Name, out var keyword))
            return keyword;
        if (Resolution?.Type is { } exactName)
            return RenderExactSegments(
                exactName.Segments,
                stripArity: true);
        if (Name.IndexOfAny(['.', '+']) >= 0)
            return Name;
        return StripArity(Name);
    }

    string QualifiedDisplayName()
    {
        string display = DisplayName();
        if (Assembly == CoreLibrary && Namespace == "System" && PrimitiveTypeNames.TryToKeywordForSystemType(Name, out _))
            return display;
        return Namespace.Length == 0 || display.StartsWith('<') ? display : $"{Namespace}.{display}";
    }

    string RenderGenericInstance(bool qualified)
    {
        var arguments = TypeArguments
            .Select(argument => qualified
                ? argument.ToQualifiedDisplayString()
                : argument.ToDisplayString())
            .ToArray();

        if (ElementType!.Resolution?.Type is { } exactName)
        {
            string display = TryRenderNestedGenericInstance(
                    exactName.Segments,
                    arguments,
                    out string rendered)
                ? rendered
                : RenderExactSegments(
                    exactName.Segments,
                    stripArity: false);
            return qualified && exactName.Namespace.Length > 0
                ? $"{exactName.Namespace}.{display}"
                : display;
        }

        if (TryInferNestedSegments(
                ElementType.Name,
                arguments.Length,
                out string[] inferredSegments)
            && TryRenderNestedGenericInstance(
                inferredSegments,
                arguments,
                out string inferredDisplay))
        {
            return qualified && ElementType.Namespace.Length > 0
                ? $"{ElementType.Namespace}.{inferredDisplay}"
                : inferredDisplay;
        }

        if (ElementType.Name.IndexOfAny(['.', '+']) >= 0)
            return qualified
                ? ElementType.QualifiedRawName()
                : ElementType.Name;

        int ownArity = ArityOf(ElementType.Name);
        if (ownArity == 0 || ownArity != arguments.Length)
            return qualified
                ? ElementType.QualifiedRawName()
                : ElementType.Name;
        string displayName = qualified
            ? ElementType.QualifiedDisplayName()
            : ElementType.DisplayName();
        return $"{displayName}<{string.Join(", ", arguments)}>";
    }

    static bool TryRenderNestedGenericInstance(
        IReadOnlyList<string> segments,
        IReadOnlyList<string> arguments,
        out string display)
    {
        display = "";
        if (segments.Count == 0)
            return false;

        long totalArity = 0;
        foreach (string segment in segments)
            totalArity += ArityOf(segment);
        int ownArity = ArityOf(segments[^1]);
        if (totalArity == 0 && arguments.Count > 0)
        {
            display =
                $"{RenderExactSegments(segments, stripArity: false)}"
                + $"<{string.Join(", ", arguments)}>";
            return true;
        }
        bool completeCompilerGeneratedName =
            arguments.Count > 0
            && arguments.Count < totalArity
            && totalArity <= TypeResolver.MaxDisplayedPlaceholders
            && IsCompilerGeneratedName(segments[^1]);
        if (totalArity != arguments.Count
            && !completeCompilerGeneratedName)
        {
            return false;
        }

        string typeName = RenderExactSegments(
            segments,
            stripArity: true);
        if (ownArity == 0)
        {
            display = typeName;
            return true;
        }

        int outerArity = checked((int)totalArity - ownArity);
        var ownArguments = new string[ownArity];
        for (int index = 0; index < ownArguments.Length; index++)
        {
            int argumentIndex = outerArity + index;
            ownArguments[index] = argumentIndex < arguments.Count
                ? arguments[argumentIndex]
                : $"T{argumentIndex + 1}";
        }
        display = $"{typeName}<{string.Join(", ", ownArguments)}>";
        return true;
    }

    IReadOnlyList<string> MetadataNameSegments()
        => Resolution?.Type is { } exactName
            ? exactName.Segments
            : Name.Split('+');

    internal static string RenderExactSegments(
        IReadOnlyList<string> segments,
        bool stripArity)
        => string.Join(
            '.',
            segments.Select(segment =>
            {
                string value = stripArity
                    ? StripArity(segment)
                    : segment;
                return value
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace(".", "\\.", StringComparison.Ordinal)
                    .Replace("+", "\\+", StringComparison.Ordinal);
            }));

    static bool IsCompilerGeneratedName(string name)
    {
        string simpleName = StripArity(name);
        return simpleName.Length > 1
            && simpleName[0] == '<'
            && simpleName.IndexOf('>') > 0;
    }

    static bool TryInferNestedSegments(
        string name,
        int argumentCount,
        out string[] segments)
    {
        segments = [];
        int boundary = name.IndexOf('+');
        if (boundary <= 0
            || boundary != name.LastIndexOf('+')
            || boundary == name.Length - 1)
        {
            return false;
        }

        string outer = name[..boundary];
        string inner = name[(boundary + 1)..];
        long nestedArity = (long)ArityOf(outer) + ArityOf(inner);
        int literalArity = ArityOf(name);
        bool completeCompilerGeneratedName =
            argumentCount > literalArity
            && argumentCount < nestedArity
            && nestedArity <= TypeResolver.MaxDisplayedPlaceholders
            && IsCompilerGeneratedName(inner);
        if ((nestedArity != argumentCount
                && !completeCompilerGeneratedName)
            || literalArity == argumentCount)
        {
            return false;
        }

        segments = [outer, inner];
        return true;
    }

    string QualifiedRawName()
        => Namespace.Length == 0 ? Name : $"{Namespace}.{Name}";

    // Metadata owns what a generic-arity suffix is: only a canonical trailing `N is
    // one, so a name whose backtick is literal (Widget`Literal) keeps its identity
    // instead of collapsing onto the unsuffixed name. See MetadataNameArity.
    static string StripArity(string name)
        => MetadataNameArity.StripFromSegment(name);

    static int ArityOf(string name)
        => MetadataNameArity.OfSegment(name);

    internal static string CanonicalAssembly(string assemblyName)
    {
        return assemblyName.Equals(
                "System.Private.CoreLib",
                StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals(
                "System.Runtime",
                StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals(
                "mscorlib",
                StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals(
                "netstandard",
                StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals(
                "System.Runtime.Extensions",
                StringComparison.OrdinalIgnoreCase)
                ? CoreLibrary
                : assemblyName;
    }
}
