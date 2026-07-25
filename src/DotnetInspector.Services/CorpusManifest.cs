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
/// The re-population identity for <see cref="Kind"/>: a package name, a platform framework/assembly
/// name, or — for path-bound kinds — the assembly's local file path.
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

    /// <summary>The logical entries, in the order they should be repopulated.</summary>
    public IReadOnlyList<CorpusManifestEntry> Entries { get; init; } = [];

    /// <summary>Serializes this manifest to indented JSON using the AOT-safe source-generated context.</summary>
    public string ToJson() =>
        JsonSerializer.Serialize(this, CorpusManifestJsonContext.Default.CorpusManifest);

    /// <summary>
    /// Deserializes a manifest from JSON produced by <see cref="ToJson"/>. Throws when the payload is
    /// null/empty JSON or carries an unsupported <see cref="SchemaVersion"/>, so a malformed document
    /// cannot silently reload as an empty corpus.
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
