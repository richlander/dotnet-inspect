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
    bool Raised)
{
    [JsonIgnore]
    public string StableKey => $"{Kind}@IL_{IlOffset:X4}#{Ordinal}";
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
        foreach (string entry in encoded.Split(';'))
        {
            string[] fields = entry.Split('|');
            if (fields.Length != 4
                || fields[0] is not (
                    "branch"
                    or "conditional-branch"
                    or "switch-branch"
                    or "leave"
                    or "end-finally"
                    or "end-filter")
                || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int ilOffset)
                || ilOffset < 0
                || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int ordinal)
                || ordinal < 0
                || fields[3] is not ("0" or "1"))
            {
                throw new JsonException($"Invalid control-flow site encoding '{entry}'.");
            }

            sites.Add(new CorpusControlFlowSiteSnapshot(
                fields[0],
                ilOffset,
                ordinal,
                Raised: fields[3] == "1"));
        }
        return sites;
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<CorpusControlFlowSiteSnapshot> value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(string.Join(
            ';',
            value.Select(static site => string.Create(
                CultureInfo.InvariantCulture,
                $"{site.Kind}|{site.IlOffset}|{site.Ordinal}|{(site.Raised ? 1 : 0)}"))));
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
