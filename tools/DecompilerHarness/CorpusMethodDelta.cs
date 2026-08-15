using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.DecompilerHarness;

// The corpus-method snapshot and changed-method delta artifact schema. Kept in
// their own file (separate from CorpusSensor, which emits them) so the targeted
// changed-method fidelity consumer in FidelityCheck can be linked into the
// decompiler test project without dragging in the rest of the sensor pipeline.

internal sealed record CorpusMethodSnapshot(
    string Assembly,
    string AssemblyPath,
    string Type,
    string Method,
    int Overload,
    string Signature,
    string Fidelity,
    bool FullyRaised,
    string? Residual,
    string? PassBug,
    string Validity,
    string FidelityCheck,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FidelityCapture = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FidelityReference = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CorpusFidelityCauseSnapshot>? FidelityCauses = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonConverter(typeof(CorpusControlFlowSiteSnapshotsJsonConverter))]
    IReadOnlyList<CorpusControlFlowSiteSnapshot>? ControlFlowSites = null)
{
    public string DisplayMethod => $"{Assembly}!{Type}::{Method}#{Overload}";

    [JsonIgnore]
    public string StableKey => $"{AssemblyPath}!{Type}::{Method}{Signature}";
}

internal sealed record CorpusControlFlowSiteSnapshot(
    string Kind,
    int IlOffset,
    int Ordinal,
    bool Raised,
    string? OutputIdentity = null)
{
    [JsonIgnore]
    public string StableKey => OutputIdentity is null
        ? $"{Kind}@IL_{IlOffset:X4}#{Ordinal}"
        : $"{Kind}:{OutputIdentity}";

    [JsonIgnore]
    public bool Imported => OutputIdentity is null;

    public static bool IsSupportedKind(string kind)
        => kind is
            "branch"
            or "conditional-branch"
            or "switch-branch"
            or "leave"
            or "end-finally"
            or "end-filter";
}

internal sealed class CorpusControlFlowSiteSnapshotsJsonConverter
    : JsonConverter<IReadOnlyList<CorpusControlFlowSiteSnapshot>>
{
    public override IReadOnlyList<CorpusControlFlowSiteSnapshot> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        string encoded = reader.GetString()
            ?? throw new JsonException("Control-flow sites must be encoded as a string.");
        if (encoded.Length == 0)
            return [];

        var sites = new List<CorpusControlFlowSiteSnapshot>();
        var stableKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string entry in encoded.Split(';'))
        {
            string[] fields = entry.Split('|');
            if (fields.Length is not (4 or 5)
                || !CorpusControlFlowSiteSnapshot.IsSupportedKind(fields[0])
                || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int ilOffset)
                || ilOffset < 0
                || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int ordinal)
                || ordinal < 0
                || fields[3] is not ("0" or "1")
                || fields.Length == 5
                    && (fields[3] != "0"
                        || !IsValidOutputIdentity(fields[0], ilOffset, ordinal, fields[4])))
            {
                throw new JsonException($"Invalid control-flow site encoding '{entry}'.");
            }

            var site = new CorpusControlFlowSiteSnapshot(
                fields[0],
                ilOffset,
                ordinal,
                Raised: fields[3] == "1",
                OutputIdentity: fields.Length == 5 ? fields[4] : null);
            if (!stableKeys.Add(site.StableKey))
                throw new JsonException($"Duplicate control-flow site key '{site.StableKey}'.");
            sites.Add(site);
        }
        return sites;
    }

    static bool IsValidOutputIdentity(
        string kind,
        int ilOffset,
        int ordinal,
        string identity)
    {
        string sourcePrefix = $"output@{kind}@source_IL_";
        string blockPrefix = $"output@{kind}@block_IL_";
        string prefix = identity.StartsWith(sourcePrefix, StringComparison.Ordinal)
            ? sourcePrefix
            : blockPrefix;
        int arrow = identity.IndexOf("->", StringComparison.Ordinal);
        int ordinalMarker = identity.LastIndexOf('#');
        return identity.StartsWith(prefix, StringComparison.Ordinal)
            && arrow > prefix.Length
            && int.TryParse(
                identity.AsSpan(prefix.Length, arrow - prefix.Length),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out int identityOffset)
            && identityOffset == ilOffset
            && ordinalMarker > arrow + 2
            && IsValidTargets(kind, identity.AsSpan(arrow + 2, ordinalMarker - arrow - 2))
            && int.TryParse(
                identity.AsSpan(ordinalMarker + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int identityOrdinal)
            && identityOrdinal == ordinal;
    }

    static bool IsValidTargets(string kind, ReadOnlySpan<char> targets)
    {
        if (kind is "end-finally" or "end-filter")
            return targets.SequenceEqual("-");
        if (targets.IsEmpty)
            return kind == "switch-branch";

        foreach (Range range in targets.Split(','))
        {
            ReadOnlySpan<char> target = targets[range];
            if (!target.StartsWith("IL_", StringComparison.Ordinal)
                || !int.TryParse(
                    target[3..],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                return false;
            }
        }
        return true;
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<CorpusControlFlowSiteSnapshot> value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(string.Join(
            ';',
            value.Select(static site => string.Join(
                '|',
                site.Kind,
                site.IlOffset.ToString(CultureInfo.InvariantCulture),
                site.Ordinal.ToString(CultureInfo.InvariantCulture),
                site.Raised ? "1" : "0",
                site.OutputIdentity).TrimEnd('|'))));
    }
}

internal sealed record CorpusFidelityCauseSnapshot(
    string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Discriminator,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Sites = null)
{
    [JsonIgnore]
    public int SiteCount => Sites switch
    {
        null => 1,
        > 0 => Sites.Value,
        _ => throw new InvalidDataException("A fidelity-cause facet must represent at least one site."),
    };
}

internal sealed record CorpusMethodDeltaArtifact(
    int SchemaVersion,
    DateTimeOffset GeneratedUtc,
    DateTimeOffset BaselineGeneratedUtc,
    DateTimeOffset CurrentGeneratedUtc,
    int BaselineFidelityContractVersion,
    int CurrentFidelityContractVersion,
    bool BaselineHasMethodDetails,
    bool CurrentHasMethodDetails,
    IReadOnlyList<CorpusMethodDeltaRow> ChangedMethods);

internal sealed record CorpusMethodDeltaRow(
    string Method,
    string Assembly,
    string AssemblyPath,
    string Type,
    string MethodName,
    int Overload,
    string Signature,
    CorpusMethodSnapshot? Baseline,
    CorpusMethodSnapshot? Current,
    IReadOnlyList<string> Deltas);
