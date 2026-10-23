using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace ILInspector.Analysis;

/// <summary>
/// Where an <see cref="UnsafeMemberUseKind.ExplicitContractCall"/> role's
/// callee contract came from.
/// </summary>
public abstract record UnsafeContractSource
{
    private protected UnsafeContractSource()
    {
    }

    /// <summary>The callee is a primary-image definition carrying the marker.</summary>
    public sealed record SameImage : UnsafeContractSource
    {
        public static SameImage Instance { get; } = new();

        SameImage()
        {
        }
    }

    /// <summary>
    /// The committed platform projection supplied the contract, generated from
    /// the named reference pack.
    /// </summary>
    public sealed record PlatformProjection(string PackId, string PackVersion)
        : UnsafeContractSource;
}

/// <summary>
/// The committed projection of .NET platform members that upstream's
/// reference assemblies mark with <c>RequiresUnsafeAttribute</c>
/// (docs/design/platform-caller-unsafe-contracts.md). The projection is a
/// positive source only: an absent member is not asserted safe.
/// </summary>
/// <remarks>
/// The embedded projection is the generated tables in
/// <c>PlatformCallerUnsafeContracts.Tables.cs</c>: sorted UTF-8 identifiers,
/// member names, and innermost declaring-type names, each one literal with an
/// offset table, so it is read in place and nothing is built at run time.
/// </remarks>
public sealed class PlatformCallerUnsafeContracts
{
    // Null for the embedded projection, which reads the generated tables.
    readonly SortedUtf8Table? _identifiers;
    readonly SortedUtf8Table? _memberNames;
    readonly SortedUtf8Table? _declaringTypeNames;

    PlatformCallerUnsafeContracts(
        UnsafeContractSource.PlatformProjection source,
        string digest,
        SortedUtf8Table? identifiers,
        SortedUtf8Table? memberNames,
        SortedUtf8Table? declaringTypeNames)
    {
        Source = source;
        Digest = digest;
        _identifiers = identifiers;
        _memberNames = memberNames;
        _declaringTypeNames = declaringTypeNames;
    }

    /// <summary>The embedded projection.</summary>
    public static PlatformCallerUnsafeContracts Embedded { get; } = new(
        new(PlatformCallerUnsafeTables.PackId, PlatformCallerUnsafeTables.PackVersion),
        PlatformCallerUnsafeTables.Digest,
        identifiers: null,
        memberNames: null,
        declaringTypeNames: null);

    public UnsafeContractSource.PlatformProjection Source { get; }

    public string Digest { get; }

    public int Count => Identifiers.Count;

    SortedUtf8Span Identifiers => _identifiers is { } table
        ? table.Span
        : new(PlatformCallerUnsafeTables.Identifiers, PlatformCallerUnsafeTables.IdentifierOffsets);

    SortedUtf8Span MemberNames => _memberNames is { } table
        ? table.Span
        : new(PlatformCallerUnsafeTables.MemberNames, PlatformCallerUnsafeTables.MemberNameOffsets);

    SortedUtf8Span DeclaringTypeNames => _declaringTypeNames is { } table
        ? table.Span
        : new(PlatformCallerUnsafeTables.DeclaringTypeNames, PlatformCallerUnsafeTables.DeclaringTypeNameOffsets);

    /// <summary>
    /// Whether <paramref name="callee"/> is a platform member the projection
    /// marks. The callee's declaring type must come from an assembly with a
    /// .NET framework public key token; generic instantiations reduce to
    /// their definitions.
    /// </summary>
    public bool Contains(MemberRef callee)
    {
        TypeRef declaring = callee.DeclaringType.Kind == TypeRefKind.GenericInstance
            ? callee.DeclaringType.ElementType!
            : callee.DeclaringType;
        if (declaring.Kind != TypeRefKind.Definition
            || !declaring.TrustedFrameworkAssembly)
        {
            return false;
        }
        ReadOnlySpan<char> name = declaring.Name;
        return MemberNames.Contains(callee.Name)
            && DeclaringTypeNames.Contains(name[(name.LastIndexOf('+') + 1)..])
            && PlatformCallerUnsafeKey.TryCreate(callee, out string? key)
            && Identifiers.Contains(key);
    }

    /// <summary>Parses projection text, verifying its header and digest.</summary>
    public static PlatformCallerUnsafeContracts Parse(string text)
    {
        (UnsafeContractSource.PlatformProjection source, string digest, List<string> identifiers) =
            ParseEntries(text);
        return new(
            source,
            digest,
            SortedUtf8Table.Create(identifiers),
            SortedUtf8Table.Create(identifiers.Select(MemberName)),
            SortedUtf8Table.Create(identifiers.Select(InnermostDeclaringTypeName)));
    }

    internal static (UnsafeContractSource.PlatformProjection Source, string Digest, List<string> Identifiers)
        ParseEntries(string text)
    {
        string? pack = null, version = null, digest = null;
        int? count = null;
        var identifiers = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var body = new StringBuilder();
        foreach (string line in text.Split('\n'))
        {
            if (line.Length == 0)
                continue;
            if (line[0] == '#')
            {
                string[] header = line[1..].Trim().Split(' ', 2);
                switch (header[0])
                {
                    case "pack": pack = header[1]; break;
                    case "version": version = header[1]; break;
                    case "count": count = int.Parse(header[1], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "digest": digest = header[1]; break;
                }
                continue;
            }
            int tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1)
                throw new InvalidDataException($"Malformed projection line '{line}'.");
            string identifier = line[(tab + 1)..];
            if (!seen.Add(identifier))
                throw new InvalidDataException($"Duplicate projection identifier '{identifier}'.");
            identifiers.Add(identifier);
            body.Append(line).Append('\n');
        }

        if (pack is null || version is null || digest is null || count is null)
            throw new InvalidDataException("The projection header is incomplete.");
        if (count != identifiers.Count)
            throw new InvalidDataException($"The projection header records {count} entries; found {identifiers.Count}.");
        if (!string.Equals(
                PlatformCallerUnsafeProjectionBuilder.ComputeDigest(body.ToString()),
                digest,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The projection digest does not match its entries.");
        }

        return (new(pack, version), digest, identifiers);
    }

    // The member metadata name, for example "As" or ".ctor".
    internal static string MemberName(string identifier)
    {
        ReadOnlySpan<char> signature = MemberSignature(identifier);
        return signature[(signature.LastIndexOf('.') + 1)..].ToString().Replace('#', '.');
    }

    // The innermost declaring-type metadata name, for example "Span`1".
    internal static string InnermostDeclaringTypeName(string identifier)
    {
        ReadOnlySpan<char> signature = MemberSignature(identifier);
        ReadOnlySpan<char> type = signature[..signature.LastIndexOf('.')];
        return type[(type.LastIndexOf('.') + 1)..].ToString();
    }

    // "<type>.<member>" with the parameter list, conversion return, and
    // method generic arity removed.
    static ReadOnlySpan<char> MemberSignature(string identifier)
    {
        ReadOnlySpan<char> signature = identifier.AsSpan(2);
        int end = signature.IndexOfAny('(', '~');
        if (end >= 0)
            signature = signature[..end];
        int arity = signature.IndexOf("``", StringComparison.Ordinal);
        return arity >= 0 ? signature[..arity] : signature;
    }
}

/// <summary>
/// Distinct UTF-8 strings in ordinal byte order, concatenated, with an offset
/// table of <c>Count + 1</c> entries.
/// </summary>
readonly ref struct SortedUtf8Span(ReadOnlySpan<byte> bytes, ReadOnlySpan<int> offsets)
{
    readonly ReadOnlySpan<byte> _bytes = bytes;
    readonly ReadOnlySpan<int> _offsets = offsets;

    public int Count => _offsets.Length - 1;

    public ReadOnlySpan<byte> this[int index] =>
        _bytes[_offsets[index].._offsets[index + 1]];

    public bool Contains(ReadOnlySpan<char> value)
    {
        int max = Encoding.UTF8.GetMaxByteCount(value.Length);
        byte[]? rented = max > 512 ? System.Buffers.ArrayPool<byte>.Shared.Rent(max) : null;
        Span<byte> buffer = rented ?? stackalloc byte[512];
        try
        {
            return Contains(buffer[..Encoding.UTF8.GetBytes(value, buffer)]);
        }
        finally
        {
            if (rented is not null)
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public bool Contains(ReadOnlySpan<byte> value)
    {
        int low = 0;
        int high = Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int order = this[middle].SequenceCompareTo(value);
            if (order == 0)
                return true;
            if (order < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return false;
    }
}

/// <summary>A <see cref="SortedUtf8Span"/> over arrays, for parsed projection text.</summary>
sealed class SortedUtf8Table
{
    readonly byte[] _bytes;
    readonly int[] _offsets;

    SortedUtf8Table(byte[] bytes, int[] offsets)
    {
        _bytes = bytes;
        _offsets = offsets;
    }

    public SortedUtf8Span Span => new(_bytes, _offsets);

    public static SortedUtf8Table Create(IEnumerable<string> values)
    {
        byte[][] encoded = [.. values.Select(Encoding.UTF8.GetBytes)];
        Array.Sort(encoded, static (left, right) => left.AsSpan().SequenceCompareTo(right));
        var bytes = new List<byte>();
        var offsets = new List<int> { 0 };
        for (int i = 0; i < encoded.Length; i++)
        {
            if (i > 0 && encoded[i].AsSpan().SequenceEqual(encoded[i - 1]))
                continue;
            bytes.AddRange(encoded[i]);
            offsets.Add(bytes.Count);
        }
        return new([.. bytes], [.. offsets]);
    }

    /// <summary>The table's bytes as a C# UTF-8 literal and its offsets.</summary>
    internal (string Bytes, string Offsets) ToSource()
    {
        // The literal spells the decoded text, which a u8 literal encodes back
        // to the same bytes; fixed-width escapes keep it unambiguous.
        var literal = new StringBuilder();
        foreach (char c in Encoding.UTF8.GetString(_bytes))
        {
            if (c is >= ' ' and < (char)0x7F && c != '"' && c != '\\')
                literal.Append(c);
            else
                literal.Append(@"\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
        }
        return (
            literal.ToString(),
            string.Join(", ", _offsets.Select(o => o.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }
}

/// <summary>
/// The one identifier builder that both projection generation and call lookup
/// use: a documentation-comment-style identifier over a method's declaring
/// type definition, name, generic arity, and open signature. Custom modifiers
/// are ignored, as documentation-comment identifiers ignore them.
/// </summary>
public static class PlatformCallerUnsafeKey
{
    public static bool TryCreate(MemberRef method, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? key)
    {
        key = null;
        TypeRef declaring = method.DeclaringType.Kind == TypeRefKind.GenericInstance
            ? method.DeclaringType.ElementType!
            : method.DeclaringType;
        if (declaring.Kind != TypeRefKind.Definition || method.Kind == MemberKind.Unsupported)
            return false;

        var builder = new StringBuilder("M:");
        AppendDefinitionName(builder, declaring);
        builder.Append('.').Append(method.Name.Replace('.', '#'));
        if (method.GenericArity > 0)
            builder.Append("``").Append(method.GenericArity);

        ImmutableArray<TypeRef> parameters = method.RequiredParameterPrefix(method.OpenSignatureParameters);
        if (parameters.Length > 0)
        {
            builder.Append('(');
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                    builder.Append(',');
                if (!AppendType(builder, parameters[i]))
                    return false;
            }
            builder.Append(')');
        }

        if (method.Name is "op_Implicit" or "op_Explicit")
        {
            builder.Append('~');
            if (!AppendType(builder, method.OpenSignatureReturn))
                return false;
        }

        key = builder.ToString();
        return true;
    }

    static void AppendDefinitionName(StringBuilder builder, TypeRef definition)
    {
        if (definition.Namespace.Length > 0)
            builder.Append(definition.Namespace).Append('.');
        builder.Append(definition.Name.Replace('+', '.'));
    }

    static bool AppendType(StringBuilder builder, TypeRef type)
    {
        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                AppendDefinitionName(builder, type);
                return true;
            case TypeRefKind.GenericInstance:
                return AppendGenericInstance(builder, type);
            case TypeRefKind.SzArray:
                if (!AppendType(builder, type.ElementType!))
                    return false;
                builder.Append("[]");
                return true;
            case TypeRefKind.Array:
                if (!AppendType(builder, type.ElementType!))
                    return false;
                builder.Append('[');
                for (int i = 0; i < type.Rank; i++)
                    builder.Append(i == 0 ? "" : ",").Append("0:");
                builder.Append(']');
                return true;
            case TypeRefKind.ByRef:
                if (!AppendType(builder, type.ElementType!))
                    return false;
                builder.Append('@');
                return true;
            case TypeRefKind.Pointer:
                if (!AppendType(builder, type.ElementType!))
                    return false;
                builder.Append('*');
                return true;
            case TypeRefKind.GenericParameter:
                builder.Append('`').Append(type.GenericParameterIndex);
                return true;
            case TypeRefKind.MethodGenericParameter:
                builder.Append("``").Append(type.GenericParameterIndex);
                return true;
            case TypeRefKind.Unsupported when type.UnmodifiedType is { } unmodified:
                return AppendType(builder, unmodified);
            case TypeRefKind.Unsupported when type.FunctionPointerSignature is { } signature:
                builder.Append("=FUNC:");
                if (!AppendType(builder, signature.ReturnType))
                    return false;
                builder.Append('(');
                for (int i = 0; i < signature.ParameterTypes.Length; i++)
                {
                    if (i > 0)
                        builder.Append(',');
                    if (!AppendType(builder, signature.ParameterTypes[i]))
                        return false;
                }
                builder.Append(')');
                return true;
            default:
                return false;
        }
    }

    // A nested generic instance distributes its flattened arguments across
    // the name segments by each segment's own arity: Outer{`0}.Inner{`1}.
    static bool AppendGenericInstance(StringBuilder builder, TypeRef type)
    {
        TypeRef definition = type.ElementType!;
        if (definition.Kind != TypeRefKind.Definition)
            return false;
        if (definition.Namespace.Length > 0)
            builder.Append(definition.Namespace).Append('.');

        string[] segments = definition.Name.Split('+');
        int consumed = 0;
        for (int s = 0; s < segments.Length; s++)
        {
            if (s > 0)
                builder.Append('.');
            string segment = segments[s];
            int tick = segment.LastIndexOf('`');
            int arity = tick >= 0
                && int.TryParse(segment.AsSpan(tick + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : 0;
            builder.Append(arity > 0 ? segment[..tick] : segment);
            if (arity == 0)
                continue;
            if (consumed + arity > type.TypeArguments.Length)
                return false;
            builder.Append('{');
            for (int i = 0; i < arity; i++)
            {
                if (i > 0)
                    builder.Append(',');
                if (!AppendType(builder, type.TypeArguments[consumed + i]))
                    return false;
            }
            builder.Append('}');
            consumed += arity;
        }
        return consumed == type.TypeArguments.Length;
    }
}

/// <summary>
/// Builds projection text from an exact reference pack. Generation fails rather
/// than producing an incomplete projection.
/// </summary>
public static class PlatformCallerUnsafeProjectionBuilder
{
    public static string Build(
        string packId,
        string packVersion,
        IEnumerable<(string FileName, byte[] Image)> assemblies)
    {
        var entries = new List<(string Assembly, string Key)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string fileName, byte[] image) in assemblies)
        {
            System.Reflection.Metadata.MetadataReader reader;
            System.Reflection.PortableExecutable.PEReader pe;
            try
            {
                pe = new System.Reflection.PortableExecutable.PEReader(ImmutableArray.Create(image));
                if (!pe.HasMetadata)
                    throw new InvalidDataException($"'{fileName}' has no metadata.");
                reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            }
            catch (BadImageFormatException ex)
            {
                throw new InvalidDataException($"'{fileName}' cannot be read: {ex.Message}", ex);
            }

            using (pe)
            {
                ILInspector.Metadata.MemorySafetyMetadataIndex index =
                    ILInspector.Metadata.MemorySafetyMetadataIndex.Create(reader);
                bool updatedRules = index.Rules is ILInspector.Metadata.MemorySafetyRulesResult.Available
                {
                    State: ILInspector.Metadata.MemorySafetyRulesState.Updated,
                };
                string assembly = reader.GetString(reader.GetAssemblyDefinition().Name);
                foreach (var handle in reader.MethodDefinitions)
                {
                    var contract = index.GetMemberContract(handle);
                    if (contract is ILInspector.Metadata.MemorySafetyMemberContractResult.Unavailable unavailable)
                        throw new InvalidDataException($"'{fileName}' method 0x{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle):X8} contract is unavailable: {unavailable}.");
                    bool marked = contract.Evidence.DirectAttribute.HasValidRow
                        || contract.Evidence.AssociatedAttribute.HasValidRow;
                    if (!marked)
                        continue;
                    if (!updatedRules)
                        throw new InvalidDataException($"'{fileName}' marks method 0x{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle):X8} without MemorySafetyRulesAttribute.");
                    if (contract is not ILInspector.Metadata.MemorySafetyMemberContractResult.Explicit)
                        throw new InvalidDataException($"'{fileName}' marks method 0x{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle):X8} but its contract is {contract.GetType().Name}.");

                    MemberRef method = MemberResolver.ResolveMethod(reader, handle, GenericScope.Empty);
                    if (!PlatformCallerUnsafeKey.TryCreate(method, out string? key))
                        throw new InvalidDataException($"'{fileName}' method 0x{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle):X8} has no projection identifier.");
                    if (!seen.Add(key))
                        throw new InvalidDataException($"Projection identifier '{key}' collides.");
                    entries.Add((assembly, key));
                }
            }
        }

        if (entries.Count == 0)
            throw new InvalidDataException("The reference pack marks no caller-unsafe members.");

        entries.Sort(static (left, right) =>
        {
            int order = string.CompareOrdinal(left.Assembly, right.Assembly);
            return order != 0 ? order : string.CompareOrdinal(left.Key, right.Key);
        });
        var body = new StringBuilder();
        foreach ((string assembly, string key) in entries)
            body.Append(assembly).Append('\t').Append(key).Append('\n');
        string text = body.ToString();

        return new StringBuilder()
            .Append("# Generated by eng/generate-platform-caller-unsafe-contracts.cs. Do not edit.\n")
            .Append("# pack ").Append(packId).Append('\n')
            .Append("# version ").Append(packVersion).Append('\n')
            .Append("# count ").Append(entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n')
            .Append("# digest ").Append(ComputeDigest(text)).Append('\n')
            .Append(text)
            .ToString();
    }

    /// <summary>
    /// The C# source of the embedded tables for committed projection
    /// <paramref name="text"/>, which <c>PlatformCallerUnsafeContracts.Tables.cs</c>
    /// must equal.
    /// </summary>
    public static string BuildTables(string text)
    {
        (UnsafeContractSource.PlatformProjection projection, string digest, List<string> identifiers) =
            PlatformCallerUnsafeContracts.ParseEntries(text);
        var source = new StringBuilder()
            .Append("// <auto-generated>\n")
            .Append("// Generated by eng/generate-platform-caller-unsafe-contracts.cs from\n")
            .Append("// PlatformCallerUnsafeContracts.txt. Do not edit.\n")
            .Append("// </auto-generated>\n\n")
            .Append("namespace ILInspector.Analysis;\n\n")
            .Append("static class PlatformCallerUnsafeTables\n{\n")
            .Append("    internal const string PackId = \"").Append(projection.PackId).Append("\";\n")
            .Append("    internal const string PackVersion = \"").Append(projection.PackVersion).Append("\";\n")
            .Append("    internal const string Digest = \"").Append(digest).Append("\";\n");
        AppendTable(source, "Identifiers", "IdentifierOffsets", SortedUtf8Table.Create(identifiers));
        AppendTable(source, "MemberNames", "MemberNameOffsets", SortedUtf8Table.Create(identifiers.Select(PlatformCallerUnsafeContracts.MemberName)));
        AppendTable(source, "DeclaringTypeNames", "DeclaringTypeNameOffsets", SortedUtf8Table.Create(identifiers.Select(PlatformCallerUnsafeContracts.InnermostDeclaringTypeName)));
        return source.Append("}\n").ToString();

        static void AppendTable(StringBuilder source, string name, string offsetsName, SortedUtf8Table table)
        {
            (string bytes, string offsets) = table.ToSource();
            source.Append('\n')
                .Append("    internal static ReadOnlySpan<byte> ").Append(name).Append(" => \"").Append(bytes).Append("\"u8;\n\n")
                .Append("    internal static ReadOnlySpan<int> ").Append(offsetsName).Append(" => [").Append(offsets).Append("];\n");
        }
    }

    internal static string ComputeDigest(string body)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
}
