using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.Metadata;

/// <summary>Why a structured metadata type-definition name could not be created.</summary>
public enum MetadataTypeNameRejectionKind
{
    MissingNamespace = 0,
    MissingSegments = 1,
    MissingSegment = 2,
    TooManySegments = 7,
    InvalidSerializedName = 3,
    AssemblyQualifiedSerializedName = 4,
    NonDefinitionSerializedName = 5,

    /// <summary>
    /// The namespace and segments together exceed
    /// <see cref="MetadataSafetyPolicy.MaxTypeNameCharacters"/>.
    /// </summary>
    SegmentsTooLong = 6,
}

/// <summary>Typed evidence for a rejected structured metadata type-definition name.</summary>
public sealed record MetadataTypeNameRejection(
    MetadataTypeNameRejectionKind Kind,
    int? SegmentIndex = null);

/// <summary>The result of validating a structured metadata type-definition name.</summary>
public abstract class MetadataTypeDefinitionNameResult
{
    private protected MetadataTypeDefinitionNameResult()
    {
    }

    public sealed class Valid : MetadataTypeDefinitionNameResult
    {
        internal Valid(MetadataTypeDefinitionName name) => Name = name;

        public MetadataTypeDefinitionName Name { get; }
    }

    public sealed class Rejected : MetadataTypeDefinitionNameResult
    {
        internal Rejected(MetadataTypeNameRejection rejection) =>
            Rejection = rejection;

        public MetadataTypeNameRejection Rejection { get; }
    }
}

/// <summary>
/// An exact reader-independent metadata lookup name: namespace plus
/// root-to-leaf metadata-name segments, including generic arity.
/// </summary>
[JsonConverter(typeof(MetadataTypeDefinitionNameJsonConverter))]
public sealed class MetadataTypeDefinitionName : IEquatable<MetadataTypeDefinitionName>
{
    readonly int hashCode;

    MetadataTypeDefinitionName(string @namespace, ImmutableArray<string> segments)
    {
        Namespace = @namespace;
        Segments = segments;

        var hash = new HashCode();
        hash.Add(@namespace, StringComparer.Ordinal);
        foreach (string segment in segments)
            hash.Add(segment, StringComparer.Ordinal);
        hashCode = hash.ToHashCode();
    }

    public sealed class MetadataTypeDefinitionNameJsonConverter
        : JsonConverter<MetadataTypeDefinitionName>
    {
        public override MetadataTypeDefinitionName Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException(
                    "A metadata type definition name must be an object.");
            }

            string? @namespace = null;
            bool hasNamespace = false;
            ImmutableArray<string>.Builder? segments = null;
            int remainingCharacters =
                MetadataSafetyPolicy.MaxTypeNameCharacters;
            while (reader.Read()
                && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("Expected a property name.");
                bool isNamespace =
                    reader.ValueTextEquals("namespace"u8);
                bool isSegments =
                    reader.ValueTextEquals("segments"u8);
                if (!reader.Read())
                    throw new JsonException("Unexpected end of JSON.");

                if (isNamespace)
                {
                    if (hasNamespace)
                        throw new JsonException(
                            "The metadata namespace must occur once.");
                    if (reader.TokenType != JsonTokenType.String)
                        throw new JsonException(
                            "The metadata namespace must be a string.");
                    @namespace = ReadBoundedString(
                        ref reader,
                        remainingCharacters);
                    remainingCharacters -= @namespace.Length;
                    hasNamespace = true;
                }
                else if (isSegments)
                {
                    if (segments is not null)
                        throw new JsonException(
                            "Metadata name segments must occur once.");
                    if (reader.TokenType != JsonTokenType.StartArray)
                        throw new JsonException(
                            "Metadata name segments must be an array.");
                    segments = ImmutableArray.CreateBuilder<string>();
                    while (reader.Read()
                        && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.String)
                            throw new JsonException(
                                "A metadata name segment must be a string.");
                        if (segments.Count
                            == MetadataSafetyPolicy.MaxRelationshipNodes)
                        {
                            throw new JsonException(
                                "The metadata name has too many segments.");
                        }
                        if (remainingCharacters == 0)
                        {
                            throw new JsonException(
                                "The metadata name exceeds the character budget.");
                        }
                        remainingCharacters--;
                        string segment = ReadBoundedString(
                            ref reader,
                            remainingCharacters);
                        remainingCharacters -= segment.Length;
                        segments.Add(segment);
                    }
                    if (reader.TokenType != JsonTokenType.EndArray)
                        throw new JsonException(
                            "Unexpected end of metadata name segments.");
                }
                else
                {
                    reader.Skip();
                }
            }
            if (reader.TokenType != JsonTokenType.EndObject)
                throw new JsonException("Unexpected end of JSON.");
            if (!hasNamespace || segments is null)
                throw new JsonException(
                    "A metadata type definition name requires namespace and segments.");

            return RequireValid(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    segments.ToImmutable()));

            static MetadataTypeDefinitionName RequireValid(
                MetadataTypeDefinitionNameResult result) =>
                result switch
                {
                    MetadataTypeDefinitionNameResult.Valid valid =>
                        valid.Name,
                    MetadataTypeDefinitionNameResult.Rejected rejected =>
                        throw new JsonException(
                            $"Invalid metadata type definition name: "
                                + $"{rejected.Rejection.Kind}."),
                    _ => throw new JsonException(
                        "Unexpected metadata type definition name result."),
                };
        }

        static string ReadBoundedString(
            ref Utf8JsonReader reader,
            int maxCharacters)
        {
            Span<char> buffer =
                stackalloc char[maxCharacters + 1];
            int length;
            try
            {
                length = reader.CopyString(buffer);
            }
            catch (ArgumentException)
            {
                throw new JsonException(
                    "The metadata name exceeds the character budget.");
            }
            if (length > maxCharacters)
            {
                throw new JsonException(
                    "The metadata name exceeds the character budget.");
            }
            return new string(buffer[..length]);
        }

        public override void Write(
            Utf8JsonWriter writer,
            MetadataTypeDefinitionName value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("namespace", value.Namespace);
            writer.WritePropertyName("segments");
            writer.WriteStartArray();
            foreach (string segment in value.Segments)
                writer.WriteStringValue(segment);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    public string Namespace { get; }
    public ImmutableArray<string> Segments { get; }

    /// <summary>
    /// Projects this lookup name to the dotted spelling used by metadata
    /// search surfaces. The structured value remains authoritative for exact
    /// declaration lookup.
    /// </summary>
    public string ToMetadataFullName()
    {
        string typeName = string.Join('.', Segments);
        return Namespace.Length == 0
            ? typeName
            : $"{Namespace}.{typeName}";
    }

    /// <summary>
    /// Projects an injective text identity for browser and composition keys. Delimiters that are
    /// literal metadata-name characters are escaped before nested segments are joined.
    /// <c>AssemblyContextApiSurfaceQueryTests.MetadataTypeIdentity_PreservesStructuredSegments</c>
    /// gates the segment-boundary and literal-delimiter distinction.
    /// </summary>
    public string ToEscapedFullName()
    {
        static string EscapeNamespace(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("+", "\\+", StringComparison.Ordinal);
        static string EscapeSegment(string value) =>
            EscapeNamespace(value)
                .Replace(".", "\\.", StringComparison.Ordinal);

        string typeName = string.Join(
            '+',
            Segments.Select(EscapeSegment));
        return Namespace.Length == 0
            ? typeName
            : $"{EscapeNamespace(Namespace)}.{typeName}";
    }

    /// <summary>
    /// Projects the flattened metadata spelling of the nested segments alone: root-to-leaf
    /// segments joined by <c>+</c>, without the namespace. This is the spelling IL and analysis
    /// display surfaces carry as a type's <c>Name</c>.
    /// </summary>
    /// <remarks>
    /// Construction is a single linear pass over already-validated segments, and the validated
    /// name is bounded by <see cref="MetadataSafetyPolicy.MaxTypeNameCharacters"/>
    /// and <see cref="MetadataSafetyPolicy.MaxRelationshipNodes"/>, so a caller
    /// can neither rebuild a growing prefix per level nor flatten an unbounded name.
    /// <c>MetadataTypeNameBudgetTests</c> gates both properties.
    /// </remarks>
    public string ToNestedMetadataName()
    {
        if (Segments.Length == 1)
            return Segments[0];

        int length = Segments.Length - 1;
        foreach (string segment in Segments)
            length += segment.Length;

        var builder = new StringBuilder(length);
        for (int i = 0; i < Segments.Length; i++)
        {
            if (i > 0)
                builder.Append('+');
            builder.Append(Segments[i]);
        }

        return builder.ToString();
    }

    public static MetadataTypeDefinitionNameResult Create(
        string? @namespace,
        ImmutableArray<string> segments)
    {
        if (@namespace is null)
        {
            return new MetadataTypeDefinitionNameResult.Rejected(
                new MetadataTypeNameRejection(
                    MetadataTypeNameRejectionKind.MissingNamespace));
        }

        if (segments.IsDefaultOrEmpty)
        {
            return new MetadataTypeDefinitionNameResult.Rejected(
                new MetadataTypeNameRejection(
                    MetadataTypeNameRejectionKind.MissingSegments));
        }
        if (segments.Length > MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            return new MetadataTypeDefinitionNameResult.Rejected(
                new MetadataTypeNameRejection(
                    MetadataTypeNameRejectionKind.TooManySegments));
        }

        long characters = @namespace.Length;
        for (int i = 0; i < segments.Length; i++)
        {
            if (string.IsNullOrEmpty(segments[i]))
            {
                return new MetadataTypeDefinitionNameResult.Rejected(
                    new MetadataTypeNameRejection(
                        MetadataTypeNameRejectionKind.MissingSegment,
                        i));
            }

            // Reserve one delimiter for every segment, including the root
            // separator when the namespace is empty, matching SRM projections.
            characters++;
            characters += segments[i].Length;
            if (characters > MetadataSafetyPolicy.MaxTypeNameCharacters)
            {
                return new MetadataTypeDefinitionNameResult.Rejected(
                    new MetadataTypeNameRejection(
                        MetadataTypeNameRejectionKind.SegmentsTooLong,
                        i));
            }
        }

        return new MetadataTypeDefinitionNameResult.Valid(
            new MetadataTypeDefinitionName(@namespace, segments));
    }

    /// <summary>
    /// Reads an exact TypeDef name while enforcing metadata relationship and
    /// aggregate name budgets before materializing artifact-authored strings.
    /// Gated by
    /// <c>SharedOversizeNestedDefinitionName_IsRejectedBeforeRepeatedMaterialization</c>.
    /// </summary>
    public static MetadataTypeDefinitionNameReadResult Read(
        MetadataReader reader,
        TypeDefinitionHandle handle) =>
        MetadataTypeDefinitionNameReader.Read(reader, handle);

    /// <summary>
    /// Reads an exact TypeRef name while enforcing metadata relationship and
    /// aggregate name budgets before materializing artifact-authored strings.
    /// Gated by
    /// <c>SharedOversizeNestedReferenceName_IsRejectedBeforeRepeatedMaterialization</c>.
    /// </summary>
    public static MetadataTypeDefinitionNameReadResult Read(
        MetadataReader reader,
        TypeReferenceHandle handle) =>
        MetadataTypeDefinitionNameReader.Read(reader, handle);

    /// <summary>
    /// Compares a TypeDef to an exact structured name without materializing
    /// metadata-authored strings. Gated by
    /// <c>OperatorHierarchyFallback_StopsBeforeMaterializingUnrelatedNames</c>.
    /// </summary>
    public static MetadataTypeDefinitionNameMatchResult Matches(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        MetadataTypeDefinitionName name,
        out MetadataTypeNameFailure? failure) =>
        MetadataTypeDefinitionNameReader.Matches(
            reader,
            handle,
            name,
            out failure) switch
        {
            MetadataTypeDefinitionNameMatch.NoMatch =>
                MetadataTypeDefinitionNameMatchResult.NoMatch,
            MetadataTypeDefinitionNameMatch.Match =>
                MetadataTypeDefinitionNameMatchResult.Match,
            MetadataTypeDefinitionNameMatch.Rejected =>
                MetadataTypeDefinitionNameMatchResult.Rejected,
            _ => throw new InvalidOperationException(
                "unknown metadata type-name match result"),
        };

    /// <summary>
    /// Parses a reflection-serialized type name into exact metadata definition
    /// identity while rejecting assembly-qualified and constructed forms.
    /// Gated by
    /// <c>SerializedName_UsesRuntimeGrammarAndPreservesExactSegments</c>.
    /// </summary>
    public static MetadataTypeDefinitionNameResult ParseSerialized(
        string serializedName)
    {
        ArgumentNullException.ThrowIfNull(serializedName);
        if (serializedName.Length
            > MetadataSafetyPolicy.MaxTypeNameCharacters)
        {
            return RejectSerialized(
                MetadataTypeNameRejectionKind.SegmentsTooLong);
        }

        var options = new TypeNameParseOptions
        {
            MaxNodes = MetadataSafetyPolicy.MaxRelationshipNodes,
        };
        if (!TypeName.TryParse(
                serializedName,
                out TypeName? parsed,
                options))
        {
            return RejectSerialized(
                MetadataTypeNameRejectionKind.InvalidSerializedName);
        }
        if (parsed.AssemblyName is not null)
        {
            return RejectSerialized(
                MetadataTypeNameRejectionKind.AssemblyQualifiedSerializedName);
        }
        return FromParsedSerializedName(parsed);
    }

    internal static MetadataTypeDefinitionNameResult
        FromParsedSerializedName(TypeName parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        if (!parsed.IsSimple)
        {
            return RejectSerialized(
                MetadataTypeNameRejectionKind.NonDefinitionSerializedName);
        }

        var segments = ImmutableArray.CreateBuilder<string>();
        TypeName current = parsed;
        while (true)
        {
            if (!current.IsSimple)
            {
                return RejectSerialized(
                    MetadataTypeNameRejectionKind.NonDefinitionSerializedName);
            }
            segments.Add(TypeName.Unescape(current.Name));
            if (!current.IsNested)
                break;
            current = current.DeclaringType;
        }
        var rootToLeaf =
            ImmutableArray.CreateBuilder<string>(segments.Count);
        for (int i = segments.Count - 1; i >= 0; i--)
            rootToLeaf.Add(segments[i]);
        return Create(
            TypeName.Unescape(current.Namespace),
            rootToLeaf.MoveToImmutable());

    }

    static MetadataTypeDefinitionNameResult RejectSerialized(
        MetadataTypeNameRejectionKind kind) =>
        new MetadataTypeDefinitionNameResult.Rejected(
            new MetadataTypeNameRejection(kind));

    public bool Equals(MetadataTypeDefinitionName? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || !StringComparer.Ordinal.Equals(Namespace, other.Namespace)
            || Segments.Length != other.Segments.Length)
        {
            return false;
        }

        for (int i = 0; i < Segments.Length; i++)
        {
            if (!StringComparer.Ordinal.Equals(Segments[i], other.Segments[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is MetadataTypeDefinitionName other && Equals(other);

    public override int GetHashCode() => hashCode;

    public static bool operator ==(
        MetadataTypeDefinitionName? left,
        MetadataTypeDefinitionName? right) =>
        EqualityComparer<MetadataTypeDefinitionName>.Default.Equals(left, right);

    public static bool operator !=(
        MetadataTypeDefinitionName? left,
        MetadataTypeDefinitionName? right) =>
        !(left == right);
}

/// <summary>The outcome of an exact, non-materializing TypeDef-name comparison.</summary>
public enum MetadataTypeDefinitionNameMatchResult
{
    NoMatch,
    Match,
    Rejected,
}

public abstract record MetadataTypeDefinitionNameReadResult
{
    private protected MetadataTypeDefinitionNameReadResult()
    {
    }

    public sealed record Read(MetadataTypeDefinitionName Name) :
        MetadataTypeDefinitionNameReadResult;

    public sealed record Rejected(MetadataTypeNameFailure Failure) :
        MetadataTypeDefinitionNameReadResult;
}

internal enum MetadataTypeDefinitionNameMatch
{
    NoMatch,
    Match,
    Rejected,
}

internal static class MetadataTypeDefinitionNameReader
{
    internal static MetadataTypeDefinitionNameReadResult Read(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        Action<int>? beforeMaterialize = null,
        Action<int>? chargeChain = null,
        Action<int>? chargeCharacters = null)
    {
        Span<TypeDefinitionHandle> rootToLeaf =
            stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                reader,
                handle,
                rootToLeaf,
                out int consumedNodes,
                out _,
                out RelationshipTraversalRejection? rejection))
        {
            chargeChain?.Invoke(consumedNodes);
            return RejectedTraversal(rejection!);
        }

        chargeChain?.Invoke(consumedNodes);
        return ReadChain<TypeDefinitionHandle, TypeDefinitionNameRow>(
            reader,
            rootToLeaf[..consumedNodes],
            beforeMaterialize,
            chargeCharacters);
    }

    internal static MetadataTypeDefinitionNameMatch Matches(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        MetadataTypeDefinitionName name,
        out MetadataTypeNameFailure? failure)
    {
        if (!LeafMatches<TypeDefinitionHandle, TypeDefinitionNameRow>(
                reader,
                handle,
                name,
                out MetadataTypeDefinitionNameMatch leafResult,
                out failure))
        {
            return leafResult;
        }

        Span<TypeDefinitionHandle> rootToLeaf =
            stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                reader,
                handle,
                rootToLeaf,
                out int consumedNodes,
                out _,
                out RelationshipTraversalRejection? rejection))
        {
            failure = MetadataTypeNameFailure.From(rejection!);
            return MetadataTypeDefinitionNameMatch.Rejected;
        }

        return MatchChain<TypeDefinitionHandle, TypeDefinitionNameRow>(
            reader,
            rootToLeaf[..consumedNodes],
            name,
            out failure);
    }

    internal static MetadataTypeDefinitionNameMatch Matches(
        MetadataReader reader,
        ExportedTypeHandle handle,
        MetadataTypeDefinitionName name,
        out MetadataTypeNameFailure? failure)
    {
        if (!LeafMatches<ExportedTypeHandle, ExportedTypeNameRow>(
                reader,
                handle,
                name,
                out MetadataTypeDefinitionNameMatch leafResult,
                out failure))
        {
            return leafResult;
        }

        Span<ExportedTypeHandle> rootToLeaf =
            stackalloc ExportedTypeHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkExportedTypeImplementationChain(
                reader,
                handle,
                rootToLeaf,
                out int consumedNodes,
                out _,
                out RelationshipTraversalRejection? rejection))
        {
            failure = MetadataTypeNameFailure.From(rejection!);
            return MetadataTypeDefinitionNameMatch.Rejected;
        }

        return MatchChain<ExportedTypeHandle, ExportedTypeNameRow>(
            reader,
            rootToLeaf[..consumedNodes],
            name,
            out failure);
    }

    static bool LeafMatches<THandle, TRow>(
        MetadataReader reader,
        THandle handle,
        MetadataTypeDefinitionName name,
        out MetadataTypeDefinitionNameMatch result,
        out MetadataTypeNameFailure? failure)
        where THandle : struct
        where TRow : struct, IMetadataTypeNameRow<THandle>
    {
        failure = null;
        try
        {
            var (_, leafName) = TRow.GetName(reader, handle);
            if (!reader.StringComparer.Equals(leafName, name.Segments[^1]))
            {
                result = MetadataTypeDefinitionNameMatch.NoMatch;
                return false;
            }

            result = MetadataTypeDefinitionNameMatch.Match;
            return true;
        }
        catch (BadImageFormatException ex)
        {
            failure = RelationshipFailure(ex, TRow.ToEntity(handle), consumedNodes: 1);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            failure = RelationshipFailure(ex, TRow.ToEntity(handle), consumedNodes: 1);
        }

        result = MetadataTypeDefinitionNameMatch.Rejected;
        return false;
    }

    static MetadataTypeDefinitionNameMatch MatchChain<THandle, TRow>(
        MetadataReader reader,
        ReadOnlySpan<THandle> rootToLeaf,
        MetadataTypeDefinitionName name,
        out MetadataTypeNameFailure? failure)
        where THandle : struct
        where TRow : struct, IMetadataTypeNameRow<THandle>
    {
        failure = null;
        if (rootToLeaf.Length != name.Segments.Length)
            return MetadataTypeDefinitionNameMatch.NoMatch;

        for (int i = 0; i < rootToLeaf.Length; i++)
        {
            try
            {
                var (namespaceHandle, nameHandle) =
                    TRow.GetName(reader, rootToLeaf[i]);
                if (i == 0
                    && !reader.StringComparer.Equals(namespaceHandle, name.Namespace))
                {
                    return MetadataTypeDefinitionNameMatch.NoMatch;
                }

                if (!reader.StringComparer.Equals(nameHandle, name.Segments[i]))
                    return MetadataTypeDefinitionNameMatch.NoMatch;
            }
            catch (BadImageFormatException ex)
            {
                failure = RelationshipFailure(
                    ex,
                    TRow.ToEntity(rootToLeaf[i]),
                    i + 1);
                return MetadataTypeDefinitionNameMatch.Rejected;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                failure = RelationshipFailure(
                    ex,
                    TRow.ToEntity(rootToLeaf[i]),
                    i + 1);
                return MetadataTypeDefinitionNameMatch.Rejected;
            }
        }

        return MetadataTypeDefinitionNameMatch.Match;
    }

    internal static MetadataTypeDefinitionNameReadResult Read(
        MetadataReader reader,
        TypeReferenceHandle handle,
        Action<int>? beforeMaterialize = null,
        Action<int>? chargeChain = null,
        Action<int>? chargeCharacters = null)
    {
        Span<TypeReferenceHandle> rootToLeaf =
            stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                reader,
                handle,
                rootToLeaf,
                out int consumedNodes,
                out _,
                out RelationshipTraversalRejection? rejection))
        {
            chargeChain?.Invoke(consumedNodes);
            return RejectedTraversal(rejection!);
        }

        chargeChain?.Invoke(consumedNodes);
        return ReadChain<TypeReferenceHandle, TypeReferenceNameRow>(
            reader,
            rootToLeaf[..consumedNodes],
            beforeMaterialize,
            chargeCharacters);
    }

    internal static MetadataTypeDefinitionNameReadResult Read(
        MetadataReader reader,
        ExportedTypeHandle handle,
        Action<int>? beforeMaterialize = null)
    {
        Span<ExportedTypeHandle> rootToLeaf =
            stackalloc ExportedTypeHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkExportedTypeImplementationChain(
                reader,
                handle,
                rootToLeaf,
                out int consumedNodes,
                out _,
                out RelationshipTraversalRejection? rejection))
        {
            return RejectedTraversal(rejection!);
        }

        return ReadChain<ExportedTypeHandle, ExportedTypeNameRow>(
            reader,
            rootToLeaf[..consumedNodes],
            beforeMaterialize);
    }

    static MetadataTypeDefinitionNameReadResult ReadChain<THandle, TRow>(
        MetadataReader reader,
        ReadOnlySpan<THandle> rootToLeaf,
        Action<int>? beforeMaterialize = null,
        Action<int>? chargeCharacters = null)
        where THandle : struct
        where TRow : struct, IMetadataTypeNameRow<THandle>
    {
        // The builder and its ToImmutable() copy each allocate one reference
        // per chain node before any name is read, so charge that structural
        // cost up front rather than relying on the per-component charges alone.
        beforeMaterialize?.Invoke(rootToLeaf.Length);
        var segments = ImmutableArray.CreateBuilder<string>(rootToLeaf.Length);
        string? @namespace = null;
        var budget = new MetadataTypeNameBudget();

        for (int i = 0; i < rootToLeaf.Length; i++)
        {
            THandle handle = rootToLeaf[i];
            try
            {
                var (namespaceHandle, nameHandle) = TRow.GetName(reader, handle);
                if (i == 0)
                {
                    bool namespaceRead = budget.TryRead(
                        reader,
                        namespaceHandle,
                        delimiterChars: 0,
                        beforeMaterialize,
                        out @namespace);
                    chargeCharacters?.Invoke(@namespace.Length);
                    if (!namespaceRead)
                    {
                        return NameTooLong(TRow.ToEntity(handle), i + 1);
                    }
                }

                bool segmentRead = budget.TryRead(
                    reader,
                    nameHandle,
                    delimiterChars: 1,
                    beforeMaterialize,
                    out string segment);
                chargeCharacters?.Invoke(segment.Length + 1);
                if (!segmentRead)
                {
                    return NameTooLong(TRow.ToEntity(handle), i + 1);
                }

                segments.Add(segment);
            }
            catch (BadImageFormatException ex)
            {
                return Malformed(ex, TRow.ToEntity(handle), i + 1);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Malformed(ex, TRow.ToEntity(handle), i + 1);
            }
        }

        MetadataTypeDefinitionNameResult created =
            MetadataTypeDefinitionName.Create(@namespace, segments.ToImmutable());
        if (created is MetadataTypeDefinitionNameResult.Valid valid)
            return new MetadataTypeDefinitionNameReadResult.Read(valid.Name);

        MetadataTypeNameRejection invalid =
            ((MetadataTypeDefinitionNameResult.Rejected)created).Rejection;
        EntityHandle subject =
            TRow.ToEntity(rootToLeaf[invalid.SegmentIndex ?? 0]);
        return new MetadataTypeDefinitionNameReadResult.Rejected(
            MetadataTypeNameFailure.Malformed(
                subject,
                $"Invalid structured metadata type name: {invalid.Kind}."));
    }

    static MetadataTypeDefinitionNameReadResult NameTooLong(
        EntityHandle subject,
        int consumedNodes) =>
        new MetadataTypeDefinitionNameReadResult.Rejected(
            MetadataTypeNameFailure.From(
                new RelationshipTraversalRejection(
                    RelationshipTraversalRejectionKind.NameBudget,
                    $"The structured type name exceeds "
                    + $"{MetadataSafetyPolicy.MaxTypeNameCharacters} characters.",
                    subject,
                    consumedNodes)));

    static MetadataTypeDefinitionNameReadResult RejectedTraversal(
        RelationshipTraversalRejection rejection) =>
        new MetadataTypeDefinitionNameReadResult.Rejected(
            MetadataTypeNameFailure.From(rejection));

    static MetadataTypeDefinitionNameReadResult Malformed(
        Exception exception,
        EntityHandle subject,
        int consumedNodes) =>
        new MetadataTypeDefinitionNameReadResult.Rejected(
            RelationshipFailure(exception, subject, consumedNodes));

    static MetadataTypeNameFailure RelationshipFailure(
        Exception exception,
        EntityHandle subject,
        int consumedNodes) =>
        MetadataTypeNameFailure.From(
            new RelationshipTraversalRejection(
                RelationshipTraversalRejectionKind.MalformedMetadata,
                exception.Message,
                subject,
                consumedNodes));

    interface IMetadataTypeNameRow<THandle>
        where THandle : struct
    {
        static abstract (StringHandle Namespace, StringHandle Name) GetName(
            MetadataReader reader,
            THandle handle);

        static abstract EntityHandle ToEntity(THandle handle);
    }

    readonly struct TypeDefinitionNameRow :
        IMetadataTypeNameRow<TypeDefinitionHandle>
    {
        public static (StringHandle Namespace, StringHandle Name) GetName(
            MetadataReader reader,
            TypeDefinitionHandle handle)
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            return (definition.Namespace, definition.Name);
        }

        public static EntityHandle ToEntity(TypeDefinitionHandle handle) => handle;
    }

    readonly struct ExportedTypeNameRow :
        IMetadataTypeNameRow<ExportedTypeHandle>
    {
        public static (StringHandle Namespace, StringHandle Name) GetName(
            MetadataReader reader,
            ExportedTypeHandle handle)
        {
            ExportedType exported = reader.GetExportedType(handle);
            return (exported.Namespace, exported.Name);
        }

        public static EntityHandle ToEntity(ExportedTypeHandle handle) => handle;
    }

    readonly struct TypeReferenceNameRow :
        IMetadataTypeNameRow<TypeReferenceHandle>
    {
        public static (StringHandle Namespace, StringHandle Name) GetName(
            MetadataReader reader,
            TypeReferenceHandle handle)
        {
            TypeReference reference = reader.GetTypeReference(handle);
            return (reference.Namespace, reference.Name);
        }

        public static EntityHandle ToEntity(TypeReferenceHandle handle) => handle;
    }
}
