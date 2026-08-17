using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Represents a type as a tree structure for applying nullability annotations.
/// Built by <see cref="TypeNodeProvider"/> during signature decoding, then
/// nullability bytes are applied via <see cref="ApplyNullability"/> before
/// rendering to a C# string with <see cref="Render"/>.
/// </summary>
internal abstract class TypeNode
{
    /// <summary>Whether this node is a reference type (can be annotated with ?).</summary>
    public abstract bool IsReferenceType { get; }

    /// <summary>Whether guarded decoding substituted any part of this type tree.</summary>
    public virtual bool IsDegraded => false;

    /// <summary>Set to true when nullability byte is 2.</summary>
    public bool IsNullableAnnotated { get; set; }

    /// <summary>
    /// Set to true when a <c>DynamicAttribute</c> transform flag marks this
    /// (<c>System.Object</c>) position as <c>dynamic</c>.
    /// </summary>
    public bool IsDynamic { get; set; }

    /// <summary>
    /// The C# element name for this node when it is a positional element of an
    /// enclosing <c>System.ValueTuple</c> instantiation authored with a named
    /// element (e.g. <c>count</c> in <c>(int count, string name)</c>). Null when
    /// the element is unnamed or this node is not a tuple element. Set by
    /// <see cref="ApplyTupleNames"/>.
    /// </summary>
    public string? TupleElementName { get; set; }

    /// <summary>Renders this type to a C# display string with nullability annotations,
    /// including C# tuple syntax (<c>(int count, string name)</c>) for
    /// <c>System.ValueTuple</c> instantiations.</summary>
    public string Render() => Render(canonicalTuples: false);

    /// <summary>
    /// Renders the structural, presentation-independent spelling used for member
    /// identity and correspondence: <c>System.ValueTuple&lt;int, string&gt;</c> rather
    /// than tuple syntax, and element names erased. Every other facet (nullability,
    /// <c>dynamic</c>, modifiers) is identical to <see cref="Render()"/>, so a non-tuple
    /// member's canonical spelling equals its display spelling byte-for-byte.
    /// </summary>
    public string RenderCanonical() => Render(canonicalTuples: true);

    /// <summary>Core renderer. When <paramref name="canonicalTuples"/> is true, a
    /// <c>System.ValueTuple</c> instantiation renders in generic form with no element
    /// names; otherwise it renders as C# tuple syntax.</summary>
    public abstract string Render(bool canonicalTuples);

    /// <summary>The exact length of <see cref="Render(bool)"/> without materializing it.</summary>
    public abstract long RenderLength(bool canonicalTuples);

    public virtual bool HasRequiredModifier(string ns, string name) => false;

    /// <summary>
    /// Walks the type tree in preorder, consuming bytes from the NullableAttribute array
    /// and setting <see cref="IsNullableAnnotated"/> where byte == 2.
    /// </summary>
    public abstract void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte);

    /// <summary>
    /// Walks the type tree in preorder, consuming flags from the DynamicAttribute
    /// transform-flags array and setting <see cref="IsDynamic"/> on
    /// <c>System.Object</c> positions whose flag is set. Mirrors the
    /// <see cref="ApplyNullability"/> traversal so the two annotation streams stay
    /// position-aligned. Absent flags (null) leave every position as authored.
    /// </summary>
    public abstract void ApplyDynamic(byte[]? flags, ref int position);

    protected static byte ConsumeByte(byte[]? bytes, ref int position, byte defaultByte)
    {
        if (bytes == null) return defaultByte;
        // Single-byte attribute: all positions use the same value
        if (bytes.Length == 1) { position++; return bytes[0]; }
        return position < bytes.Length ? bytes[position++] : defaultByte;
    }

    /// <summary>
    /// Consumes one DynamicAttribute flag for this node, marking it dynamic when
    /// the flag is set and the node is an <c>object</c> position that can spell
    /// <c>dynamic</c>. Non-object positions still consume their flag (always 0) to
    /// keep the preorder walk aligned.
    /// </summary>
    protected void ConsumeDynamicFlag(byte[]? flags, ref int position, bool canBeDynamic)
    {
        if (flags is null)
            return;
        // Unlike NullableAttribute, a single-element (marker-form) DynamicAttribute
        // describes only the bare top-level object; it must NOT broadcast into inner
        // nodes. Index strictly and treat positions past the end as non-dynamic.
        byte flag = position < flags.Length ? flags[position] : (byte)0;
        position++;
        if (canBeDynamic && flag != 0) IsDynamic = true;
    }

    /// <summary>Whether a rendered type name denotes <c>System.Object</c>.</summary>
    private protected static bool IsObjectName(string name) =>
        name is "object" or "System.Object";

    /// <summary>The fully qualified base name of a <c>System.ValueTuple</c> instantiation.</summary>
    private const string ValueTupleBaseName = "System.ValueTuple";

    /// <summary>
    /// When <paramref name="node"/> is a C# tuple — a <c>System.ValueTuple</c>
    /// instantiation of flattened arity &gt;= 2 — returns its logical element
    /// nodes in source order, absorbing the 8th "Rest" argument's nested
    /// <c>ValueTuple</c> chain (<c>ValueTuple&lt;T1..T7, ValueTuple&lt;T8..&gt;&gt;</c>
    /// flattens to <c>(T1..Tn)</c>). Returns null otherwise. A 1-arity
    /// <c>ValueTuple&lt;T&gt;</c> is intentionally not a tuple: C# <c>(T)</c> is
    /// parenthesization, not a one-element tuple, so it keeps generic spelling.
    /// </summary>
    internal static List<TypeNode>? TryGetTupleElements(TypeNode node)
    {
        if (node is not GenericTypeNode { BaseName: ValueTupleBaseName } tuple)
            return null;

        var elements = new List<TypeNode>();
        var current = tuple;
        while (true)
        {
            var args = current.Arguments;
            // A well-formed C# tuple of arity > 7 is ValueTuple<T1..T7, TRest>
            // where TRest is itself a ValueTuple holding the remaining elements.
            if (args.Length == 8)
            {
                if (args[7] is GenericTypeNode { BaseName: ValueTupleBaseName } rest)
                {
                    for (int i = 0; i < 7; i++)
                        elements.Add(args[i]);
                    current = rest;
                    continue;
                }

                // An 8-argument ValueTuple whose 8th ("Rest") argument is not itself a
                // ValueTuple cannot arise from C# tuple syntax (TRest must be a ValueTuple).
                // Treat it as an ordinary generic instantiation so it keeps its
                // System.ValueTuple<...> spelling and never collides with a genuine
                // eight-element tuple.
                return null;
            }
            elements.AddRange(args);
            break;
        }
        return elements.Count >= 2 ? elements : null;
    }

    /// <summary>
    /// The count of trailing null padding entries a tuple of the given flattened
    /// arity contributes to a <c>TupleElementNamesAttribute</c> names stream,
    /// accounting for its nested "Rest" <c>ValueTuple</c> containers:
    /// <c>f(n) = (n-7) + f(n-7)</c> for <c>n &gt; 7</c>, else 0. Empirically
    /// verified against the Roslyn encoding across arities 7..29.
    /// </summary>
    private static int TupleNamePadding(int arity)
        => arity <= 7 ? 0 : (arity - 7) + TupleNamePadding(arity - 7);

    /// <summary>
    /// Applies <c>TupleElementNamesAttribute</c> element names to the tuple
    /// positions in this type tree. The names are a flat stream ordered
    /// per tuple — a tuple's own element names first, then recursing into each
    /// element — with trailing null padding per tuple for its 8+ arity "Rest"
    /// nesting (see <see cref="TupleNamePadding"/>). Only tuple element positions
    /// consume the stream; non-tuple structure (arrays, generics, by-ref, etc.)
    /// consumes nothing. A null stream (attribute absent) leaves every tuple
    /// unnamed; unnamed tuples still render with positional <c>(...)</c> syntax.
    /// </summary>
    public void ApplyTupleNames(string?[]? names)
    {
        if (names is null) return;
        int position = 0;
        DecodeTupleNames(this, names, ref position);
    }

    private static void DecodeTupleNames(TypeNode node, string?[] names, ref int position)
    {
        if (TryGetTupleElements(node) is { } elements)
        {
            foreach (var element in elements)
            {
                element.TupleElementName = position < names.Length ? names[position] : null;
                position++;
            }
            position += TupleNamePadding(elements.Count);
            foreach (var element in elements)
                DecodeTupleNames(element, names, ref position);
        }
        else
        {
            foreach (var child in TypeChildren(node))
                DecodeTupleNames(child, names, ref position);
        }
    }

    /// <summary>
    /// The immediate type-node children of a non-tuple node, in preorder, used to
    /// find tuples nested inside non-tuple structure (e.g. <c>Func&lt;(int a,int b)&gt;</c>).
    /// </summary>
    private static IEnumerable<TypeNode> TypeChildren(TypeNode node) => node switch
    {
        GenericTypeNode generic => generic.Arguments,
        SZArrayTypeNode array => [array.ElementType],
        MDArrayTypeNode array => [array.ElementType],
        PointerTypeNode pointer => [pointer.ElementType],
        ByRefTypeNode byRef => [byRef.ElementType],
        FunctionPointerTypeNode fnptr => fnptr.ChildTypes,
        PassthroughTypeNode passthrough => [passthrough.Inner],
        _ => [],
    };

    protected static long JoinedLength(IEnumerable<long> lengths)
    {
        long total = 0;
        bool first = true;
        foreach (long length in lengths)
        {
            if (!first)
                total += 2;
            total += length;
            first = false;
        }
        return total;
    }
}

/// <summary>A visible fail-closed substitute for a rejected signature type.</summary>
internal sealed class DegradedTypeNode : TypeNode
{
    public override bool IsReferenceType => true;
    public override bool IsDegraded => true;

    public override string Render(bool canonicalTuples) => IsDynamic
        ? (IsNullableAnnotated ? "dynamic?" : "dynamic")
        : (IsNullableAnnotated ? "object?" : "object");

    public override long RenderLength(bool canonicalTuples) =>
        (IsDynamic ? "dynamic".Length : "object".Length)
        + (IsNullableAnnotated ? 1 : 0);

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (b == 2) IsNullableAnnotated = true;
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
        => ConsumeDynamicFlag(flags, ref position, canBeDynamic: true);
}

/// <summary>C# primitive types (int, string, object, etc.).</summary>
internal sealed class PrimitiveTypeNode(string name, bool isReferenceType) : TypeNode
{
    public override bool IsReferenceType => isReferenceType;

    public override string Render(bool canonicalTuples)
    {
        string effective = IsDynamic ? "dynamic" : name;
        return IsReferenceType && IsNullableAnnotated ? $"{effective}?" : effective;
    }

    public override long RenderLength(bool canonicalTuples) =>
        (IsDynamic ? "dynamic".Length : name.Length)
        + (IsReferenceType && IsNullableAnnotated ? 1 : 0);

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (IsReferenceType && b == 2) IsNullableAnnotated = true;
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
        => ConsumeDynamicFlag(flags, ref position, canBeDynamic: IsObjectName(name));
}

/// <summary>Non-generic named types (JsonSerializer, Stream, etc.).</summary>
/// <remarks>
/// Bounded API-surface extraction may construct these with a deferred metadata
/// handle so signature decode does not <c>GetString</c> TypeDef/TypeRef names
/// before retained-budget <see cref="RenderLength"/> preflight. Erased custom
/// modifiers never render, so deferral also keeps giant <c>modopt</c>/<c>modreq</c>
/// names off the retained budget (Sol R10/R11).
/// </remarks>
internal sealed class NamedTypeNode : TypeNode
{
    string? _name;
    readonly MetadataReader? _reader;
    readonly EntityHandle _handle;
    readonly Action<long>? _beforeMaterializeName;
    readonly Dictionary<EntityHandle, string>? _sharedNames;
    long _countedCharacters = -1;

    public NamedTypeNode(string name, bool isReferenceType)
    {
        _name = name;
        IsReferenceType = isReferenceType;
    }

    public NamedTypeNode(
        MetadataReader reader,
        EntityHandle handle,
        bool isReferenceType,
        Action<long>? beforeMaterializeName,
        Dictionary<EntityHandle, string> sharedNames)
    {
        _reader = reader;
        _handle = handle;
        _beforeMaterializeName = beforeMaterializeName;
        _sharedNames = sharedNames;
        IsReferenceType = isReferenceType;
        if (sharedNames.TryGetValue(handle, out string? cached))
            _name = cached;
    }

    public string Name => EnsureMaterialized();
    public override bool IsReferenceType { get; }

    public override string Render(bool canonicalTuples)
    {
        string effective = IsDynamic ? "dynamic" : Name;
        return IsReferenceType && IsNullableAnnotated ? $"{effective}?" : effective;
    }

    public override long RenderLength(bool canonicalTuples) =>
        (IsDynamic ? "dynamic".Length : CountedCharacters())
        + (IsReferenceType && IsNullableAnnotated ? 1 : 0);

    /// <summary>
    /// Compares against a fully-qualified expected name without materializing a
    /// differently-sized deferred metadata name (custom-modifier identity).
    /// Same-length matches materialize without the retained-budget preflight so an
    /// unrecognized modreq cannot false-trip exact accept-set headroom (Sol R13).
    /// </summary>
    public bool EqualsResolvedName(string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (_name is not null)
            return _name == expected;
        if (CountedCharacters() != expected.Length)
            return false;
        // Length already matched a short recognized modifier spelling; skip
        // EnsureCanMaterialize (MaterializeName has no retained preflight).
        return MaterializeName() == expected;
    }

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (IsReferenceType && b == 2) IsNullableAnnotated = true;
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
        => ConsumeDynamicFlag(flags, ref position, canBeDynamic: CouldBeObjectName());

    long CountedCharacters()
    {
        if (_name is not null)
            return _name.Length;
        if (_countedCharacters >= 0)
            return _countedCharacters;
        if (_reader is not null
            && MetadataSafetyPolicy.TryCountTypeNameCharacters(
                _reader,
                _handle,
                out long characters))
        {
            return _countedCharacters = characters;
        }

        // Uncountable chains (cycles / node-budget rejects) cannot preflight.
        // Materialize once without re-entering this method (Opus R12).
        return MaterializeName().Length;
    }

    bool CouldBeObjectName()
    {
        if (_name is not null)
            return IsObjectName(_name);

        if (_reader is not null
            && MetadataSafetyPolicy.TryCountTypeNameCharacters(
                _reader,
                _handle,
                out long characters))
        {
            _countedCharacters = characters;
            // Only materialize when the counted spelling could be object/System.Object.
            if (characters != "object".Length
                && characters != "System.Object".Length)
            {
                return false;
            }

            return IsObjectName(EnsureMaterialized());
        }

        // Uncountable: do not force materialization just to test dynamic-ness.
        return false;
    }

    string EnsureMaterialized()
    {
        if (_name is not null)
            return _name;
        if (_sharedNames is not null
            && _sharedNames.TryGetValue(_handle, out string? cached))
        {
            return _name = cached;
        }

        bool canPreflight = _countedCharacters >= 0
            || (_reader is not null
                && MetadataSafetyPolicy.TryCountTypeNameCharacters(
                    _reader,
                    _handle,
                    out long characters)
                && (_countedCharacters = characters) >= 0);

        if (canPreflight)
            _beforeMaterializeName?.Invoke(_countedCharacters);

        // Resolve through TypeResolver (string-decode gateway allow list). Leaf
        // name materialization must not bypass that owner from TypeNode.
        return MaterializeName();
    }

    string MaterializeName()
    {
        if (_name is not null)
            return _name;
        if (_sharedNames is not null
            && _sharedNames.TryGetValue(_handle, out string? cached))
        {
            return _name = cached;
        }

        _name = _handle.Kind switch
        {
            HandleKind.TypeDefinition => TypeResolver.GetTypeNameFromDefinition(
                _reader!,
                (TypeDefinitionHandle)_handle),
            HandleKind.TypeReference => TypeResolver.GetTypeNameFromReference(
                _reader!,
                (TypeReferenceHandle)_handle),
            _ => throw new InvalidOperationException(
                "Deferred named types require a TypeDef or TypeRef handle."),
        };
        _sharedNames?.TryAdd(_handle, _name);
        _countedCharacters = _name.Length;
        return _name;
    }
}

/// <summary>Generic instantiations (Dictionary&lt;K,V&gt;, Task&lt;T&gt;, etc.).</summary>
internal sealed class GenericTypeNode : TypeNode
{
    readonly string _metadataName;
    readonly string _nestedSuffix;
    readonly bool _useMetadataArity;
    readonly bool _isReferenceType;
    readonly bool _degradedGenericType;

    public GenericTypeNode(
        string baseName,
        bool isReferenceType,
        ImmutableArray<TypeNode> arguments,
        string nestedSuffix = "",
        bool degradedGenericType = false,
        bool useMetadataArity = false)
    {
        _metadataName = baseName;
        _nestedSuffix = nestedSuffix;
        _useMetadataArity = useMetadataArity;
        _isReferenceType = isReferenceType;
        _degradedGenericType = degradedGenericType;
        Arguments = arguments;

        int backtick = useMetadataArity ? baseName.IndexOf('`') : -1;
        BaseName = backtick < 0 ? baseName : baseName[..backtick];
    }

    public string BaseName { get; }
    public ImmutableArray<TypeNode> Arguments { get; }
    public override bool IsReferenceType => _isReferenceType;
    public override bool IsDegraded =>
        _degradedGenericType || Arguments.Any(argument => argument.IsDegraded);

    public override string Render(bool canonicalTuples)
    {
        // A System.ValueTuple instantiation spells as C# tuple syntax
        // (int count, string name) rather than System.ValueTuple<int, string>,
        // flattening 8+ arity "Rest" nesting and applying any element names.
        // The canonical (identity) spelling keeps the generic form and drops names.
        if (!canonicalTuples && TryGetTupleElements(this) is { } elements)
        {
            var parts = elements.Select(element =>
                element.TupleElementName is { Length: > 0 } elementName
                    ? $"{element.Render()} {elementName}"
                    : element.Render());
            var tuple = $"({string.Join(", ", parts)})";
            return IsReferenceType && IsNullableAnnotated ? $"{tuple}?" : tuple;
        }

        string result;
        if (_useMetadataArity)
        {
            var builder = new System.Text.StringBuilder();
            int argumentIndex = 0;
            for (int index = 0; index < _metadataName.Length; index++)
            {
                if (_metadataName[index] != '`'
                    || !TryReadArity(
                        _metadataName,
                        index,
                        out int digitEnd,
                        out int arity))
                {
                    builder.Append(_metadataName[index]);
                    continue;
                }

                int take = Math.Min(arity, Arguments.Length - argumentIndex);
                builder.Append('<');
                for (int offset = 0; offset < take; offset++)
                {
                    if (offset > 0)
                        builder.Append(", ");
                    builder.Append(
                        Arguments[argumentIndex + offset].Render(
                            canonicalTuples));
                }
                builder.Append('>');
                argumentIndex += take;
                index = digitEnd - 1;
            }
            result = builder.ToString();
        }
        else
        {
            var argsStr = string.Join(
                ", ",
                Arguments.Select(argument => argument.Render(canonicalTuples)));
            result = $"{BaseName}<{argsStr}>{_nestedSuffix}";
        }
        return IsReferenceType && IsNullableAnnotated ? $"{result}?" : result;
    }

    public override long RenderLength(bool canonicalTuples)
    {
        if (!canonicalTuples && TryGetTupleElements(this) is { } elements)
        {
            long elementsLength = JoinedLength(
                elements.Select(
                    element => element.RenderLength(canonicalTuples: false)
                        + (element.TupleElementName is { Length: > 0 } name
                            ? 1L + name.Length
                            : 0)));
            return 2 + elementsLength + (IsReferenceType && IsNullableAnnotated ? 1 : 0);
        }

        long length;
        if (_useMetadataArity)
        {
            length = 0;
            int argumentIndex = 0;
            for (int index = 0; index < _metadataName.Length; index++)
            {
                if (_metadataName[index] != '`'
                    || !TryReadArity(
                        _metadataName,
                        index,
                        out int digitEnd,
                        out int arity))
                {
                    length++;
                    continue;
                }

                int take = Math.Min(arity, Arguments.Length - argumentIndex);
                length += 2;
                for (int offset = 0; offset < take; offset++)
                {
                    if (offset > 0)
                        length += 2;
                    length += Arguments[argumentIndex + offset].RenderLength(
                        canonicalTuples);
                }
                argumentIndex += take;
                index = digitEnd - 1;
            }
        }
        else
        {
            length = BaseName.Length
                + 2
                + JoinedLength(
                    Arguments.Select(
                        argument => argument.RenderLength(canonicalTuples)))
                + _nestedSuffix.Length;
        }
        return length + (IsReferenceType && IsNullableAnnotated ? 1 : 0);
    }

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (IsReferenceType && b == 2) IsNullableAnnotated = true;
        foreach (var arg in Arguments)
            arg.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        // The generic-type head is never object; it still consumes a flag.
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        foreach (var arg in Arguments)
            arg.ApplyDynamic(flags, ref position);
    }

    static bool TryReadArity(
        string name,
        int backtick,
        out int digitEnd,
        out int arity)
    {
        int digitStart = backtick + 1;
        digitEnd = digitStart;
        arity = 0;
        while (digitEnd < name.Length && char.IsDigit(name[digitEnd]))
            digitEnd++;
        return digitEnd > digitStart
            && int.TryParse(
                name.AsSpan(digitStart, digitEnd - digitStart),
                out arity)
            && arity > 0;
    }
}

/// <summary>Single-dimensional arrays (string[], int[], etc.).</summary>
internal sealed class SZArrayTypeNode(TypeNode elementType) : TypeNode
{
    public TypeNode ElementType => elementType;
    public override bool IsReferenceType => true;
    public override bool IsDegraded => elementType.IsDegraded;

    public override string Render(bool canonicalTuples)
    {
        var result = $"{elementType.Render(canonicalTuples)}[]";
        return IsNullableAnnotated ? $"{result}?" : result;
    }

    public override long RenderLength(bool canonicalTuples) =>
        elementType.RenderLength(canonicalTuples) + 2 + (IsNullableAnnotated ? 1 : 0);

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (b == 2) IsNullableAnnotated = true;
        elementType.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        elementType.ApplyDynamic(flags, ref position);
    }
}

/// <summary>Multi-dimensional arrays (int[,], etc.).</summary>
internal sealed class MDArrayTypeNode(TypeNode elementType, int rank) : TypeNode
{
    public TypeNode ElementType => elementType;
    public override bool IsReferenceType => true;
    public override bool IsDegraded => elementType.IsDegraded;

    public override string Render(bool canonicalTuples)
    {
        var result = $"{elementType.Render(canonicalTuples)}[{new string(',', rank - 1)}]";
        return IsNullableAnnotated ? $"{result}?" : result;
    }

    public override long RenderLength(bool canonicalTuples) =>
        elementType.RenderLength(canonicalTuples)
        + rank
        + 1
        + (IsNullableAnnotated ? 1 : 0);

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (b == 2) IsNullableAnnotated = true;
        elementType.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        elementType.ApplyDynamic(flags, ref position);
    }
}

/// <summary>Pointer types (int*, void*, etc.).</summary>
internal sealed class PointerTypeNode(TypeNode elementType) : TypeNode
{
    public TypeNode ElementType => elementType;
    public override bool IsReferenceType => false;
    public override bool IsDegraded => elementType.IsDegraded;

    public override string Render(bool canonicalTuples) => $"{elementType.Render(canonicalTuples)}*";

    public override long RenderLength(bool canonicalTuples) =>
        elementType.RenderLength(canonicalTuples) + 1;

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        ConsumeByte(bytes, ref position, defaultByte);
        elementType.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        elementType.ApplyDynamic(flags, ref position);
    }
}

/// <summary>By-reference types (ref T, out T, in T). Does not consume a nullability byte.</summary>
internal sealed class ByRefTypeNode(TypeNode elementType) : TypeNode
{
    public TypeNode ElementType => elementType;
    public override bool IsReferenceType => false;
    public override bool IsDegraded => elementType.IsDegraded;

    public override string Render(bool canonicalTuples) => $"ref {elementType.Render(canonicalTuples)}";

    public override long RenderLength(bool canonicalTuples) =>
        4 + elementType.RenderLength(canonicalTuples);

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        // ByRef is a modifier, not a type—does not consume a nullability byte.
        elementType.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        // Unlike nullability, the DynamicAttribute transform-flags array reserves a
        // (always-false) slot for the by-ref itself, so consume one before the element.
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        elementType.ApplyDynamic(flags, ref position);
    }

    public override bool HasRequiredModifier(string ns, string name)
        => elementType.HasRequiredModifier(ns, name);
}

/// <summary>Generic type or method parameters (T, TKey, etc.).</summary>
internal sealed class GenericParameterNode(string name, bool hasValueTypeConstraint) : TypeNode
{
    public override bool IsReferenceType => false;

    public override string Render(bool canonicalTuples) => IsNullableAnnotated ? $"{name}?" : name;

    public override long RenderLength(bool canonicalTuples) =>
        name.Length + (IsNullableAnnotated ? 1 : 0);

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        byte b = ConsumeByte(bytes, ref position, defaultByte);
        if (!hasValueTypeConstraint && b == 2) IsNullableAnnotated = true;
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
        => ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
}

/// <summary>Function pointer types (delegate*&lt;...&gt;).</summary>
internal sealed class FunctionPointerTypeNode(MethodSignature<TypeNode> signature) : TypeNode
{
    public IEnumerable<TypeNode> ChildTypes => signature.ParameterTypes.Prepend(signature.ReturnType);
    public override bool IsReferenceType => false;
    public override bool IsDegraded => signature.ReturnType.IsDegraded
        || signature.ParameterTypes.Any(parameter => parameter.IsDegraded);

    public override string Render(bool canonicalTuples)
    {
        var types = signature.ParameterTypes.Select(t => t.Render(canonicalTuples)).Append(signature.ReturnType.Render(canonicalTuples));
        string arguments = string.Join(", ", types);
        string convention = ConventionText(signature.Header.CallingConvention);
        return convention.Length == 0
            ? $"delegate*<{arguments}>"
            : $"delegate* {convention}<{arguments}>";
    }

    public override long RenderLength(bool canonicalTuples)
    {
        long argumentsLength = JoinedLength(
            signature.ParameterTypes
                .Select(parameter => parameter.RenderLength(canonicalTuples))
                .Append(signature.ReturnType.RenderLength(canonicalTuples)));
        string convention = ConventionText(signature.Header.CallingConvention);
        return convention.Length == 0
            ? 11 + argumentsLength
            : 12 + convention.Length + argumentsLength;
    }

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        ConsumeByte(bytes, ref position, defaultByte);
        signature.ReturnType.ApplyNullability(bytes, ref position, defaultByte);
        foreach (var parameter in signature.ParameterTypes)
            parameter.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        signature.ReturnType.ApplyDynamic(flags, ref position);
        foreach (var parameter in signature.ParameterTypes)
            parameter.ApplyDynamic(flags, ref position);
    }

    static string ConventionText(SignatureCallingConvention convention) => convention switch
    {
        SignatureCallingConvention.Default => "",
        SignatureCallingConvention.CDecl => "unmanaged[Cdecl]",
        SignatureCallingConvention.StdCall => "unmanaged[Stdcall]",
        SignatureCallingConvention.ThisCall => "unmanaged[Thiscall]",
        SignatureCallingConvention.FastCall => "unmanaged[Fastcall]",
        _ => "unmanaged",
    };
}

/// <summary>Modified or pinned types—pass through to the underlying type.</summary>
internal class PassthroughTypeNode(TypeNode inner) : TypeNode
{
    public TypeNode Inner => inner;
    public override bool IsReferenceType => inner.IsReferenceType;
    public override bool IsDegraded => inner.IsDegraded;

    public override string Render(bool canonicalTuples) => inner.Render(canonicalTuples);

    public override long RenderLength(bool canonicalTuples) => inner.RenderLength(canonicalTuples);

    public override void ApplyNullability(byte[]? bytes, ref int position, byte defaultByte)
    {
        inner.ApplyNullability(bytes, ref position, defaultByte);
    }

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        inner.ApplyDynamic(flags, ref position);
    }

    public override bool HasRequiredModifier(string ns, string name)
        => inner.HasRequiredModifier(ns, name);
}

/// <summary>Custom-modified types pass through for rendering while preserving declaration-site evidence.</summary>
internal sealed class ModifiedTypeNode(TypeNode modifier, TypeNode inner, bool isRequired) : PassthroughTypeNode(inner)
{
    public override bool IsDegraded => modifier.IsDegraded || base.IsDegraded;

    public override void ApplyDynamic(byte[]? flags, ref int position)
    {
        // Roslyn reserves one (always-false) DynamicAttribute slot per custom
        // modifier. Consume this modifier's slot before the modified type so the
        // flag stream stays aligned (e.g. a `ref readonly dynamic` return encodes
        // [byref, modreq(In), object] = [false, false, true]).
        ConsumeDynamicFlag(flags, ref position, canBeDynamic: false);
        base.ApplyDynamic(flags, ref position);
    }

    public override bool HasRequiredModifier(string ns, string name)
        => (isRequired && ModifierMatches(ns, name)) || base.HasRequiredModifier(ns, name);

    bool ModifierMatches(string ns, string name)
    {
        // A custom modifier is identified by its exact full type name. IL TypeReferences
        // always carry their namespace, so a real modreq renders fully qualified; a bare
        // render is the global namespace — a different type. No suffix or bare-name fallback:
        // Foo.IsVolatile and (global) IsVolatile are not System.Runtime.CompilerServices.IsVolatile.
        string expected = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        // Deferred NamedTypeNode: length-mismatch rejects without GetString so a giant
        // erased modopt cannot allocate or false-trip retained budgets (Sol R11).
        if (modifier is NamedTypeNode named)
            return named.EqualsResolvedName(expected);
        return modifier.Render() == expected;
    }
}
