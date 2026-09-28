using System.Buffers;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace DotnetInspector.Vocabulary;

/// <summary>The primitive kind of values in one scalar vocabulary map.</summary>
public enum VocabularyScalarKind
{
    Text,
    Integer,
    Boolean,
}

/// <summary>The number of values one map permits for each source term.</summary>
public enum VocabularyMapCardinality
{
    ExactlyOne,
    OptionalOne,
    OneOrMore,
    ZeroOrMore,
}

/// <summary>Whether one map makes an assertion for every source term.</summary>
public enum VocabularyMapCoverage
{
    Complete,
    Partial,
}

/// <summary>The stable identity of one independently published vocabulary family.</summary>
public readonly record struct VocabularyCatalogIdentity
{
    public VocabularyCatalogIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>The stable identity of one vocabulary within a catalog.</summary>
public readonly record struct VocabularyIdentity
{
    public VocabularyIdentity(
        VocabularyCatalogIdentity catalog,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Catalog = catalog;
        Value = value;
    }

    public VocabularyCatalogIdentity Catalog { get; }

    public string Value { get; }

    public override string ToString() => $"{Catalog}/{Value}";
}

/// <summary>The stable identity of one term within a vocabulary.</summary>
public readonly record struct VocabularyTermIdentity
{
    public VocabularyTermIdentity(
        VocabularyIdentity vocabulary,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vocabulary.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Vocabulary = vocabulary;
        Value = value;
    }

    public VocabularyIdentity Vocabulary { get; }

    public string Value { get; }

    public override string ToString() => $"{Vocabulary}/{Value}";
}

/// <summary>The stable identity of one map within its source vocabulary.</summary>
public readonly record struct VocabularyMapIdentity
{
    public VocabularyMapIdentity(
        VocabularyIdentity sourceVocabulary,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceVocabulary.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        SourceVocabulary = sourceVocabulary;
        Value = value;
    }

    public VocabularyIdentity SourceVocabulary { get; }

    public string Value { get; }

    public override string ToString() => $"{SourceVocabulary}/{Value}";
}

/// <summary>The exact content identity of one immutable vocabulary snapshot.</summary>
public readonly record struct VocabularySnapshotIdentity
{
    public VocabularySnapshotIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 71
            || !value.StartsWith("sha256:", StringComparison.Ordinal)
            || value.AsSpan(7).IndexOfAnyExcept(
                "0123456789abcdef".AsSpan()) >= 0)
        {
            throw new ArgumentException(
                "A vocabulary snapshot identity must be 'sha256:' followed by "
                + "64 lowercase hexadecimal digits.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>One typed primitive value in a scalar vocabulary map.</summary>
public readonly record struct VocabularyScalarValue
{
    private VocabularyScalarValue(
        VocabularyScalarKind kind,
        string? text,
        long integer,
        bool boolean)
    {
        Kind = kind;
        Text = text;
        Integer = integer;
        Boolean = boolean;
    }

    public VocabularyScalarKind Kind { get; }

    public string? Text { get; }

    public long Integer { get; }

    public bool Boolean { get; }

    public static VocabularyScalarValue FromText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(VocabularyScalarKind.Text, value, 0, false);
    }

    public static VocabularyScalarValue FromInteger(long value) =>
        new(VocabularyScalarKind.Integer, null, value, false);

    public static VocabularyScalarValue FromBoolean(bool value) =>
        new(VocabularyScalarKind.Boolean, null, 0, value);
}

/// <summary>The target kind of one vocabulary map.</summary>
public abstract record VocabularyMapTarget
{
    private VocabularyMapTarget()
    {
    }

    public sealed record Scalar(VocabularyScalarKind Kind)
        : VocabularyMapTarget;

    public sealed record Terms(VocabularyTermSetReference Reference)
        : VocabularyMapTarget;
}

/// <summary>The exact term set targeted by a term-reference map.</summary>
public abstract record VocabularyTermSetReference
{
    private VocabularyTermSetReference()
    {
    }

    public sealed record Local(VocabularyIdentity Vocabulary)
        : VocabularyTermSetReference;

    public sealed record External(
        VocabularySnapshotIdentity Snapshot,
        VocabularyIdentity Vocabulary)
        : VocabularyTermSetReference;
}

/// <summary>One typed value in a vocabulary map entry.</summary>
public abstract record VocabularyMapValue
{
    private VocabularyMapValue()
    {
    }

    public sealed record Scalar(VocabularyScalarValue Value)
        : VocabularyMapValue;

    public sealed record Term(VocabularyTermIdentity Identity)
        : VocabularyMapValue;
}

/// <summary>The declared contract of one map on a source vocabulary.</summary>
public sealed record VocabularyMapDefinition
{
    public VocabularyMapDefinition(
        VocabularyMapIdentity identity,
        string displayLabel,
        string summary,
        VocabularyMapTarget target,
        VocabularyMapCardinality cardinality,
        VocabularyMapCoverage coverage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        Identity = identity;
        DisplayLabel = displayLabel;
        Summary = summary;
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Cardinality = cardinality;
        Coverage = coverage;
    }

    public VocabularyMapIdentity Identity { get; }

    public string DisplayLabel { get; }

    public string Summary { get; }

    public VocabularyMapTarget Target { get; }

    public VocabularyMapCardinality Cardinality { get; }

    public VocabularyMapCoverage Coverage { get; }
}

/// <summary>The values one source term declares for one map.</summary>
public sealed record VocabularyMapEntry
{
    public VocabularyMapEntry(
        VocabularyMapIdentity map,
        IEnumerable<VocabularyMapValue> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(map.Value);
        ArgumentNullException.ThrowIfNull(values);
        Map = map;
        Values = [.. values];
    }

    public VocabularyMapIdentity Map { get; }

    public ImmutableArray<VocabularyMapValue> Values { get; }
}

/// <summary>One stable term and its typed map values.</summary>
public sealed record VocabularyTerm
{
    public VocabularyTerm(
        VocabularyTermIdentity identity,
        string displayLabel,
        string? summary,
        IEnumerable<VocabularyMapEntry>? mapEntries = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLabel);
        Identity = identity;
        DisplayLabel = displayLabel;
        Summary = summary;
        MapEntries = [.. mapEntries ?? []];
    }

    public VocabularyTermIdentity Identity { get; }

    public string DisplayLabel { get; }

    public string? Summary { get; }

    public ImmutableArray<VocabularyMapEntry> MapEntries { get; }

    public bool TryGetValues(
        VocabularyMapIdentity map,
        out ImmutableArray<VocabularyMapValue> values)
    {
        foreach (VocabularyMapEntry entry in MapEntries)
        {
            if (entry.Map == map)
            {
                values = entry.Values;
                return true;
            }
        }

        values = [];
        return false;
    }

    public ImmutableArray<VocabularyMapValue> GetRequiredValues(
        VocabularyMapIdentity map) =>
        TryGetValues(map, out ImmutableArray<VocabularyMapValue> values)
            ? values
            : throw new KeyNotFoundException(
                $"Term '{Identity}' has no entry for map '{map}'.");
}

/// <summary>One ordered vocabulary declaration.</summary>
public sealed record VocabularyDefinition
{
    public VocabularyDefinition(
        VocabularyIdentity identity,
        string displayLabel,
        string summary,
        IEnumerable<VocabularyMapDefinition>? maps,
        IEnumerable<VocabularyTerm> terms)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(terms);
        Identity = identity;
        DisplayLabel = displayLabel;
        Summary = summary;
        Maps = [.. maps ?? []];
        Terms = [.. terms];
    }

    public VocabularyIdentity Identity { get; }

    public string DisplayLabel { get; }

    public string Summary { get; }

    public ImmutableArray<VocabularyMapDefinition> Maps { get; }

    public ImmutableArray<VocabularyTerm> Terms { get; }

    public VocabularyMapDefinition GetMap(string identity) =>
        Maps.FirstOrDefault(map =>
            map.Identity.Value.Equals(identity, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException(
            $"Vocabulary '{Identity}' has no map '{identity}'.");

    public VocabularyTerm GetTerm(string identity) =>
        Terms.FirstOrDefault(term =>
            term.Identity.Value.Equals(identity, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException(
            $"Vocabulary '{Identity}' has no term '{identity}'.");
}

/// <summary>One complete immutable vocabulary catalog snapshot.</summary>
public sealed class VocabularySnapshot
{
    private readonly IReadOnlyDictionary<
        VocabularyIdentity,
        VocabularyDefinition> _vocabularies;
    private readonly IReadOnlyDictionary<
        VocabularyTermIdentity,
        VocabularyTerm> _terms;
    private readonly IReadOnlyDictionary<
        VocabularyMapIdentity,
        VocabularyMapDefinition> _maps;

    private VocabularySnapshot(
        int formatVersion,
        VocabularyCatalogIdentity catalog,
        VocabularySnapshotIdentity identity,
        ImmutableArray<VocabularyDefinition> vocabularies)
    {
        FormatVersion = formatVersion;
        Catalog = catalog;
        Identity = identity;
        Vocabularies = vocabularies;
        _vocabularies = vocabularies.ToDictionary(
            vocabulary => vocabulary.Identity);
        _terms = vocabularies
            .SelectMany(vocabulary => vocabulary.Terms)
            .ToDictionary(term => term.Identity);
        _maps = vocabularies
            .SelectMany(vocabulary => vocabulary.Maps)
            .ToDictionary(map => map.Identity);
    }

    public int FormatVersion { get; }

    public VocabularyCatalogIdentity Catalog { get; }

    public VocabularySnapshotIdentity Identity { get; }

    public ImmutableArray<VocabularyDefinition> Vocabularies { get; }

    public static VocabularySnapshot Create(
        int formatVersion,
        VocabularyCatalogIdentity catalog,
        IEnumerable<VocabularyDefinition> vocabularies,
        IEnumerable<VocabularySnapshot>? externalSnapshots = null,
        VocabularySnapshotIdentity? expectedIdentity = null)
    {
        if (formatVersion <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(formatVersion),
                "The vocabulary format version must be positive.");
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog.Value);
        ArgumentNullException.ThrowIfNull(vocabularies);

        ImmutableArray<VocabularyDefinition> declarations = [.. vocabularies];
        Dictionary<VocabularySnapshotIdentity, VocabularySnapshot> dependencies =
            (externalSnapshots ?? []).ToDictionary(snapshot => snapshot.Identity);
        ImmutableArray<VocabularyDefinition> normalized = ValidateAndNormalize(
            catalog,
            declarations,
            dependencies);
        VocabularySnapshotIdentity identity = ComputeIdentity(
            formatVersion,
            catalog,
            normalized);
        if (expectedIdentity is { } expected && expected != identity)
        {
            throw new InvalidOperationException(
                $"Vocabulary snapshot identity '{expected}' does not match "
                + $"constructed content identity '{identity}'.");
        }

        return new(formatVersion, catalog, identity, normalized);
    }

    public VocabularyDefinition GetVocabulary(VocabularyIdentity identity) =>
        _vocabularies.TryGetValue(identity, out VocabularyDefinition? value)
            ? value
            : throw new KeyNotFoundException(
                $"Vocabulary snapshot '{Identity}' has no vocabulary '{identity}'.");

    public VocabularyTerm GetTerm(VocabularyTermIdentity identity) =>
        _terms.TryGetValue(identity, out VocabularyTerm? value)
            ? value
            : throw new KeyNotFoundException(
                $"Vocabulary snapshot '{Identity}' has no term '{identity}'.");

    public VocabularyMapDefinition GetMap(VocabularyMapIdentity identity) =>
        _maps.TryGetValue(identity, out VocabularyMapDefinition? value)
            ? value
            : throw new KeyNotFoundException(
                $"Vocabulary snapshot '{Identity}' has no map '{identity}'.");

    private static ImmutableArray<VocabularyDefinition> ValidateAndNormalize(
        VocabularyCatalogIdentity catalog,
        ImmutableArray<VocabularyDefinition> vocabularies,
        IReadOnlyDictionary<
            VocabularySnapshotIdentity,
            VocabularySnapshot> externalSnapshots)
    {
        var vocabularyById =
            new Dictionary<VocabularyIdentity, VocabularyDefinition>();
        foreach (VocabularyDefinition vocabulary in vocabularies)
        {
            if (vocabulary.Identity.Catalog != catalog)
            {
                throw new InvalidOperationException(
                    $"Vocabulary '{vocabulary.Identity}' does not belong to "
                    + $"catalog '{catalog}'.");
            }
            if (!vocabularyById.TryAdd(vocabulary.Identity, vocabulary))
            {
                throw new InvalidOperationException(
                    $"Vocabulary '{vocabulary.Identity}' occurs more than once.");
            }
        }

        var termById =
            new Dictionary<VocabularyTermIdentity, VocabularyTerm>();
        foreach (VocabularyDefinition vocabulary in vocabularies)
        {
            foreach (VocabularyTerm term in vocabulary.Terms)
            {
                if (term.Identity.Vocabulary != vocabulary.Identity)
                {
                    throw new InvalidOperationException(
                        $"Term '{term.Identity}' does not belong to vocabulary "
                        + $"'{vocabulary.Identity}'.");
                }
                if (!termById.TryAdd(term.Identity, term))
                {
                    throw new InvalidOperationException(
                        $"Term '{term.Identity}' occurs more than once.");
                }
            }
        }

        var normalized = ImmutableArray.CreateBuilder<VocabularyDefinition>(
            vocabularies.Length);
        foreach (VocabularyDefinition vocabulary in vocabularies)
        {
            var maps =
                new Dictionary<VocabularyMapIdentity, VocabularyMapDefinition>();
            foreach (VocabularyMapDefinition map in vocabulary.Maps)
            {
                if (map.Identity.SourceVocabulary != vocabulary.Identity)
                {
                    throw new InvalidOperationException(
                        $"Map '{map.Identity}' does not belong to source "
                        + $"vocabulary '{vocabulary.Identity}'.");
                }
                if (!maps.TryAdd(map.Identity, map))
                {
                    throw new InvalidOperationException(
                        $"Map '{map.Identity}' occurs more than once.");
                }
                if (!Enum.IsDefined(map.Cardinality)
                    || !Enum.IsDefined(map.Coverage))
                {
                    throw new InvalidOperationException(
                        $"Map '{map.Identity}' has an unsupported cardinality "
                        + "or coverage value.");
                }
                if (map.Target is VocabularyMapTarget.Scalar scalar
                    && !Enum.IsDefined(scalar.Kind))
                {
                    throw new InvalidOperationException(
                        $"Map '{map.Identity}' has an unsupported scalar kind.");
                }

                ValidateTarget(
                    map,
                    vocabularyById,
                    externalSnapshots);
            }

            var normalizedTerms = ImmutableArray.CreateBuilder<VocabularyTerm>(
                vocabulary.Terms.Length);

            foreach (VocabularyTerm term in vocabulary.Terms)
            {
                var entries =
                    new Dictionary<VocabularyMapIdentity, VocabularyMapEntry>();
                foreach (VocabularyMapEntry entry in term.MapEntries)
                {
                    if (!maps.ContainsKey(entry.Map))
                    {
                        throw new InvalidOperationException(
                            $"Term '{term.Identity}' names undeclared map "
                            + $"'{entry.Map}'.");
                    }
                    if (!entries.TryAdd(entry.Map, entry))
                    {
                        throw new InvalidOperationException(
                            $"Term '{term.Identity}' repeats map "
                            + $"'{entry.Map}'.");
                    }
                }

                var normalizedEntries =
                    ImmutableArray.CreateBuilder<VocabularyMapEntry>(
                        entries.Count);
                foreach (VocabularyMapDefinition map in vocabulary.Maps)
                {
                    if (!entries.TryGetValue(
                            map.Identity,
                            out VocabularyMapEntry? entry))
                    {
                        if (map.Coverage == VocabularyMapCoverage.Complete)
                        {
                            throw new InvalidOperationException(
                                $"Complete map '{map.Identity}' has no entry for "
                                + $"term '{term.Identity}'.");
                        }
                        continue;
                    }

                    ValidateEntry(
                        term,
                        map,
                        entry,
                        vocabularyById,
                        termById,
                        externalSnapshots);
                    normalizedEntries.Add(entry);
                }

                normalizedTerms.Add(new(
                    term.Identity,
                    term.DisplayLabel,
                    term.Summary,
                    normalizedEntries));
            }

            normalized.Add(new(
                vocabulary.Identity,
                vocabulary.DisplayLabel,
                vocabulary.Summary,
                vocabulary.Maps,
                normalizedTerms));
        }

        return normalized.MoveToImmutable();
    }

    private static void ValidateTarget(
        VocabularyMapDefinition map,
        IReadOnlyDictionary<VocabularyIdentity, VocabularyDefinition> vocabularies,
        IReadOnlyDictionary<
            VocabularySnapshotIdentity,
            VocabularySnapshot> externalSnapshots)
    {
        if (map.Target is not VocabularyMapTarget.Terms terms)
            return;

        switch (terms.Reference)
        {
            case VocabularyTermSetReference.Local local:
                if (!vocabularies.ContainsKey(local.Vocabulary))
                {
                    throw new InvalidOperationException(
                        $"Map '{map.Identity}' targets unknown local "
                        + $"vocabulary '{local.Vocabulary}'.");
                }
                break;

            case VocabularyTermSetReference.External external:
                if (!externalSnapshots.TryGetValue(
                        external.Snapshot,
                        out VocabularySnapshot? snapshot))
                {
                    throw new InvalidOperationException(
                        $"Map '{map.Identity}' requires unavailable external "
                        + $"snapshot '{external.Snapshot}'.");
                }
                snapshot.GetVocabulary(external.Vocabulary);
                break;

            default:
                throw new InvalidOperationException(
                    $"Map '{map.Identity}' has an unsupported target.");
        }
    }

    private static void ValidateEntry(
        VocabularyTerm term,
        VocabularyMapDefinition map,
        VocabularyMapEntry entry,
        IReadOnlyDictionary<VocabularyIdentity, VocabularyDefinition> vocabularies,
        IReadOnlyDictionary<VocabularyTermIdentity, VocabularyTerm> localTerms,
        IReadOnlyDictionary<
            VocabularySnapshotIdentity,
            VocabularySnapshot> externalSnapshots)
    {
        int count = entry.Values.Length;
        bool validCardinality = map.Cardinality switch
        {
            VocabularyMapCardinality.ExactlyOne => count == 1,
            VocabularyMapCardinality.OptionalOne => count <= 1,
            VocabularyMapCardinality.OneOrMore => count >= 1,
            VocabularyMapCardinality.ZeroOrMore => true,
            _ => false,
        };
        if (!validCardinality)
        {
            throw new InvalidOperationException(
                $"Term '{term.Identity}' supplies {count} values for map "
                + $"'{map.Identity}', violating {map.Cardinality} cardinality.");
        }

        var values = new HashSet<VocabularyMapValue>();
        foreach (VocabularyMapValue value in entry.Values)
        {
            if (!values.Add(value))
            {
                throw new InvalidOperationException(
                    $"Term '{term.Identity}' repeats a value for map "
                    + $"'{map.Identity}'.");
            }

            switch (map.Target, value)
            {
                case (
                    VocabularyMapTarget.Scalar scalar,
                    VocabularyMapValue.Scalar scalarValue)
                    when scalar.Kind == scalarValue.Value.Kind:
                    if (scalar.Kind == VocabularyScalarKind.Text
                        && scalarValue.Value.Text is null)
                    {
                        throw new InvalidOperationException(
                            $"Term '{term.Identity}' supplies an invalid text "
                            + $"value for map '{map.Identity}'.");
                    }
                    break;

                case (
                    VocabularyMapTarget.Terms terms,
                    VocabularyMapValue.Term termValue):
                    ValidateTermReference(
                        map,
                        terms.Reference,
                        termValue.Identity,
                        vocabularies,
                        localTerms,
                        externalSnapshots);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Term '{term.Identity}' supplies a value of the wrong "
                        + $"kind for map '{map.Identity}'.");
            }
        }
    }

    private static void ValidateTermReference(
        VocabularyMapDefinition map,
        VocabularyTermSetReference reference,
        VocabularyTermIdentity target,
        IReadOnlyDictionary<VocabularyIdentity, VocabularyDefinition> vocabularies,
        IReadOnlyDictionary<VocabularyTermIdentity, VocabularyTerm> localTerms,
        IReadOnlyDictionary<
            VocabularySnapshotIdentity,
            VocabularySnapshot> externalSnapshots)
    {
        switch (reference)
        {
            case VocabularyTermSetReference.Local local:
                if (target.Vocabulary != local.Vocabulary
                    || !vocabularies.ContainsKey(local.Vocabulary)
                    || !localTerms.ContainsKey(target))
                {
                    throw new InvalidOperationException(
                        $"Map '{map.Identity}' has dangling local target "
                        + $"'{target}'.");
                }
                break;

            case VocabularyTermSetReference.External external:
                if (target.Vocabulary != external.Vocabulary
                    || !externalSnapshots.TryGetValue(
                        external.Snapshot,
                        out VocabularySnapshot? snapshot))
                {
                    throw new InvalidOperationException(
                        $"Map '{map.Identity}' has unavailable external target "
                        + $"'{target}'.");
                }
                snapshot.GetTerm(target);
                break;

            default:
                throw new InvalidOperationException(
                    $"Map '{map.Identity}' has an unsupported term target.");
        }
    }

    private static VocabularySnapshotIdentity ComputeIdentity(
        int formatVersion,
        VocabularyCatalogIdentity catalog,
        ImmutableArray<VocabularyDefinition> vocabularies)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", formatVersion);
            writer.WriteString("catalog", catalog.Value);
            writer.WriteStartArray("vocabularies");
            foreach (VocabularyDefinition vocabulary in vocabularies)
            {
                writer.WriteStartObject();
                writer.WriteString("identity", vocabulary.Identity.Value);
                writer.WriteString("displayLabel", vocabulary.DisplayLabel);
                writer.WriteString("summary", vocabulary.Summary);
                writer.WriteStartArray("maps");
                foreach (VocabularyMapDefinition map in vocabulary.Maps)
                    WriteMap(writer, map);
                writer.WriteEndArray();
                writer.WriteStartArray("terms");
                foreach (VocabularyTerm term in vocabulary.Terms)
                    WriteTerm(writer, term);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        string digest = Convert.ToHexString(
            SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
        return new($"sha256:{digest}");
    }

    private static void WriteMap(
        Utf8JsonWriter writer,
        VocabularyMapDefinition map)
    {
        writer.WriteStartObject();
        writer.WriteString("identity", map.Identity.Value);
        writer.WriteString("displayLabel", map.DisplayLabel);
        writer.WriteString("summary", map.Summary);
        writer.WriteString("cardinality", Name(map.Cardinality));
        writer.WriteString("coverage", Name(map.Coverage));
        writer.WritePropertyName("target");
        writer.WriteStartObject();
        switch (map.Target)
        {
            case VocabularyMapTarget.Scalar scalar:
                writer.WriteString("kind", "scalar");
                writer.WriteString("scalarKind", Name(scalar.Kind));
                break;

            case VocabularyMapTarget.Terms terms:
                writer.WriteString("kind", "terms");
                switch (terms.Reference)
                {
                    case VocabularyTermSetReference.Local local:
                        writer.WriteString("scope", "local");
                        writer.WriteString(
                            "vocabulary",
                            local.Vocabulary.Value);
                        break;

                    case VocabularyTermSetReference.External external:
                        writer.WriteString("scope", "external");
                        writer.WriteString(
                            "snapshot",
                            external.Snapshot.Value);
                        writer.WriteString(
                            "catalog",
                            external.Vocabulary.Catalog.Value);
                        writer.WriteString(
                            "vocabulary",
                            external.Vocabulary.Value);
                        break;
                }
                break;
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteTerm(
        Utf8JsonWriter writer,
        VocabularyTerm term)
    {
        writer.WriteStartObject();
        writer.WriteString("identity", term.Identity.Value);
        writer.WriteString("displayLabel", term.DisplayLabel);
        if (term.Summary is null)
            writer.WriteNull("summary");
        else
            writer.WriteString("summary", term.Summary);
        writer.WriteStartArray("maps");
        foreach (VocabularyMapEntry entry in term.MapEntries)
        {
            writer.WriteStartObject();
            writer.WriteString("identity", entry.Map.Value);
            writer.WriteStartArray("values");
            foreach (VocabularyMapValue value in entry.Values)
                WriteValue(writer, value);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteValue(
        Utf8JsonWriter writer,
        VocabularyMapValue value)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case VocabularyMapValue.Scalar scalar:
                writer.WriteString("kind", "scalar");
                writer.WriteString(
                    "scalarKind",
                    Name(scalar.Value.Kind));
                switch (scalar.Value.Kind)
                {
                    case VocabularyScalarKind.Text:
                        writer.WriteString("value", scalar.Value.Text);
                        break;
                    case VocabularyScalarKind.Integer:
                        writer.WriteNumber("value", scalar.Value.Integer);
                        break;
                    case VocabularyScalarKind.Boolean:
                        writer.WriteBoolean("value", scalar.Value.Boolean);
                        break;
                }
                break;

            case VocabularyMapValue.Term term:
                writer.WriteString("kind", "term");
                writer.WriteString(
                    "catalog",
                    term.Identity.Vocabulary.Catalog.Value);
                writer.WriteString(
                    "vocabulary",
                    term.Identity.Vocabulary.Value);
                writer.WriteString("value", term.Identity.Value);
                break;
        }
        writer.WriteEndObject();
    }

    private static string Name(VocabularyScalarKind value) => value switch
    {
        VocabularyScalarKind.Text => "text",
        VocabularyScalarKind.Integer => "integer",
        VocabularyScalarKind.Boolean => "boolean",
        _ => throw new InvalidOperationException(
            $"Unsupported vocabulary scalar kind '{value}'."),
    };

    private static string Name(VocabularyMapCardinality value) => value switch
    {
        VocabularyMapCardinality.ExactlyOne => "exactly-one",
        VocabularyMapCardinality.OptionalOne => "optional-one",
        VocabularyMapCardinality.OneOrMore => "one-or-more",
        VocabularyMapCardinality.ZeroOrMore => "zero-or-more",
        _ => throw new InvalidOperationException(
            $"Unsupported vocabulary map cardinality '{value}'."),
    };

    private static string Name(VocabularyMapCoverage value) => value switch
    {
        VocabularyMapCoverage.Complete => "complete",
        VocabularyMapCoverage.Partial => "partial",
        _ => throw new InvalidOperationException(
            $"Unsupported vocabulary map coverage '{value}'."),
    };
}
