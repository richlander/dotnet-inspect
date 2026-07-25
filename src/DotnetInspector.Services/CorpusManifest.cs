using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.Services;

/// <summary>
/// One logical origin in a <see cref="CorpusManifest"/>: the pinned recipe for reproducing a slice of
/// a corpus, not an individual assembly file. A package that expands to many assemblies collapses to a
/// single entry; a path-bound source (loose assembly, project, directory) is normalized to a
/// reload-by-path <see cref="AssemblySetSourceKind.Assembly"/> entry so every entry is re-populatable.
/// </summary>
/// <param name="Kind">How the entry is re-populated (package, platform framework, loose assembly, …).</param>
/// <param name="Id">
/// The re-population identity for <see cref="Kind"/>: a package name for packages, a framework name for
/// a whole platform framework, or — for path-bound kinds (loose assembly, project, directory, and a
/// single platform assembly) — the assembly's full local file path.
/// </param>
/// <param name="Version">The pinned version, when the producer resolved one (informational for platform kinds).</param>
/// <param name="Tfm">The target framework moniker the entry was selected for, when known.</param>
public sealed record CorpusManifestEntry(
    AssemblySetSourceKind Kind,
    string Id,
    string? Version = null,
    string? Tfm = null);

/// <summary>
/// A serializable, offline-durable description of a corpus: the ordered set of logical entries needed
/// to repopulate it. This is the shareable/primary state a corpus round-trips through — serialize it to
/// persist or share, deserialize and re-populate to obtain an equivalent live <see cref="ILInspector.Metadata.Corpus"/>.
/// The manifest itself performs no resolution or I/O; repopulation is a separate, explicit step in
/// <see cref="CorpusProducer"/>.
/// </summary>
public sealed record CorpusManifest
{
    /// <summary>The current manifest schema version emitted by this build.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of this manifest, so older documents can be detected on load.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// The logical entries, in the order they should be repopulated. Assignment defensively copies into
    /// a genuine read-only collection so a caller cannot downcast the exposed list and mutate the recipe
    /// (which would break the invariant that a manifest describes its corpus).
    /// </summary>
    public IReadOnlyList<CorpusManifestEntry> Entries
    {
        get => _entries;
        init => _entries = value is null
            ? EmptyEntries
            : new ReadOnlyCollection<CorpusManifestEntry>([.. value]);
    }

    private readonly IReadOnlyList<CorpusManifestEntry> _entries = EmptyEntries;

    private static readonly ReadOnlyCollection<CorpusManifestEntry> EmptyEntries =
        new([]);

    /// <summary>Serializes this manifest to indented JSON using the AOT-safe source-generated context.</summary>
    public string ToJson() =>
        JsonSerializer.Serialize(this, CorpusManifestJsonContext.Default.CorpusManifest);

    /// <summary>
    /// Deserializes a manifest from JSON produced by <see cref="ToJson"/>. Throws when the payload is
    /// null/empty JSON, carries an unsupported <see cref="SchemaVersion"/>, or contains no entries, so a
    /// malformed or empty document cannot silently reload as an empty corpus.
    /// </summary>
    public static CorpusManifest FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);

        var manifest = JsonSerializer.Deserialize(json, CorpusManifestJsonContext.Default.CorpusManifest)
            ?? throw new JsonException("Corpus manifest JSON deserialized to null.");

        if (manifest.SchemaVersion != CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Unsupported corpus manifest schema version {manifest.SchemaVersion}; expected {CurrentSchemaVersion}.");
        }

        if (manifest.Entries.Count == 0)
        {
            throw new JsonException(
                "Corpus manifest contains no entries; a manifest that describes no assemblies cannot be loaded.");
        }

        return manifest;
    }
}

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for <see cref="CorpusManifest"/>. Using the
/// generator (rather than reflection-based serialization) keeps manifest round-tripping compatible with
/// the NativeAOT-published CLI.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CorpusManifest))]
public sealed partial class CorpusManifestJsonContext : JsonSerializerContext
{
}
