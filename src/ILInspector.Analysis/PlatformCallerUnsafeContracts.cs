using System.Collections.Frozen;
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
public sealed class PlatformCallerUnsafeContracts
{
    internal const string ResourceName = "ILInspector.Analysis.PlatformCallerUnsafeContracts.txt";

    static readonly Lazy<PlatformCallerUnsafeContracts> s_embedded = new(
        LoadEmbedded,
        LazyThreadSafetyMode.ExecutionAndPublication);

    readonly FrozenSet<string> _identifiers;

    PlatformCallerUnsafeContracts(
        UnsafeContractSource.PlatformProjection source,
        string digest,
        FrozenSet<string> identifiers)
    {
        Source = source;
        Digest = digest;
        _identifiers = identifiers;
    }

    /// <summary>The embedded projection, loaded on first use.</summary>
    public static PlatformCallerUnsafeContracts Embedded => s_embedded.Value;

    public UnsafeContractSource.PlatformProjection Source { get; }

    public string Digest { get; }

    public int Count => _identifiers.Count;

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
        return declaring.Kind == TypeRefKind.Definition
            && declaring.TrustedFrameworkAssembly
            && PlatformCallerUnsafeKey.TryCreate(callee, out string? key)
            && _identifiers.Contains(key);
    }

    /// <summary>Parses projection text, verifying its header.</summary>
    public static PlatformCallerUnsafeContracts Parse(string text)
    {
        string? pack = null, version = null, digest = null;
        int? count = null;
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
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
            if (!identifiers.Add(line[(tab + 1)..]))
                throw new InvalidDataException($"Duplicate projection identifier '{line[(tab + 1)..]}'.");
            body.Append(line).Append('\n');
        }

        if (pack is null || version is null || digest is null || count is null)
            throw new InvalidDataException("The projection header is incomplete.");
        if (count != identifiers.Count)
            throw new InvalidDataException($"The projection header records {count} entries; found {identifiers.Count}.");
        string actual = PlatformCallerUnsafeProjectionBuilder.ComputeDigest(body.ToString());
        if (!string.Equals(actual, digest, StringComparison.Ordinal))
            throw new InvalidDataException("The projection digest does not match its entries.");

        return new(new(pack, version), digest, identifiers.ToFrozenSet(StringComparer.Ordinal));
    }

    static PlatformCallerUnsafeContracts LoadEmbedded()
    {
        using Stream stream = typeof(PlatformCallerUnsafeContracts).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource '{ResourceName}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd());
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

    internal static string ComputeDigest(string body)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
}
