using System.Globalization;
using System.Text;
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

internal static class CorpusControlFlowOutputIdentity
{
    const string OutputPrefix = "output@";
    const string SourceMarker = "@source_IL_";
    const string BlockMarker = "@block_IL_";
    const string LocalFunctionMarker = "@local_";

    public static string FormatKey(
        string kind,
        bool source,
        int ilOffset,
        string targets,
        string? localFunctionName = null)
    {
        if (!CorpusControlFlowSiteSnapshot.IsSupportedKind(kind)
            || ilOffset < 0
            || localFunctionName is { Length: 0 }
            || !IsValidTargets(kind, targets))
        {
            throw new InvalidOperationException(
                $"Invalid control-flow output identity components "
                + $"'{kind}', '{ilOffset}', '{targets}'.");
        }

        string marker = source ? SourceMarker : BlockMarker;
        string owner = localFunctionName is not null
            ? $"{LocalFunctionMarker}n{Convert.ToHexString(Encoding.UTF8.GetBytes(localFunctionName))}"
            : "";
        return $"{kind}{marker}{ilOffset:X4}{owner}->{targets}";
    }

    public static string Format(string key, int ordinal)
    {
        if (ordinal < 0 || !TryParseKey(key, out _, out _, out _))
            throw new InvalidOperationException($"Invalid control-flow output key '{key}'.");
        return $"{OutputPrefix}{key}#{ordinal}";
    }

    public static bool TryParse(
        string identity,
        out string kind,
        out int ilOffset,
        out int ordinal)
    {
        kind = "";
        ilOffset = -1;
        ordinal = -1;
        if (!identity.StartsWith(OutputPrefix, StringComparison.Ordinal))
            return false;

        int ordinalMarker = identity.LastIndexOf('#');
        if (ordinalMarker < OutputPrefix.Length
            || !TryParseKey(
                identity[OutputPrefix.Length..ordinalMarker],
                out kind,
                out ilOffset,
                out _)
            || !int.TryParse(
                identity.AsSpan(ordinalMarker + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out ordinal)
            || ordinal < 0)
        {
            kind = "";
            ilOffset = -1;
            ordinal = -1;
            return false;
        }

        return true;
    }

    public static bool TryParseKey(
        string key,
        out string kind,
        out int ilOffset,
        out bool source)
    {
        kind = "";
        ilOffset = -1;
        source = false;

        int markerStart = key.IndexOf('@');
        if (markerStart <= 0)
            return false;
        kind = key[..markerStart];
        if (!CorpusControlFlowSiteSnapshot.IsSupportedKind(kind))
            return false;

        string marker;
        if (key.AsSpan(markerStart).StartsWith(SourceMarker, StringComparison.Ordinal))
        {
            marker = SourceMarker;
            source = true;
        }
        else if (key.AsSpan(markerStart).StartsWith(BlockMarker, StringComparison.Ordinal))
        {
            marker = BlockMarker;
        }
        else
        {
            return false;
        }

        int offsetStart = markerStart + marker.Length;
        int arrow = key.IndexOf("->", offsetStart, StringComparison.Ordinal);
        int ownerStart = key.IndexOf(LocalFunctionMarker, offsetStart, StringComparison.Ordinal);
        int offsetEnd = ownerStart >= 0 ? ownerStart : arrow;
        bool ownerIsValid = ownerStart < 0
            || ownerStart < arrow
                && IsValidLocalFunctionOwner(
                    key.AsSpan(
                        ownerStart + LocalFunctionMarker.Length,
                        arrow - ownerStart - LocalFunctionMarker.Length));
        return offsetEnd > offsetStart
            && arrow >= offsetEnd
            && int.TryParse(
                key.AsSpan(offsetStart, offsetEnd - offsetStart),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out ilOffset)
            && ilOffset >= 0
            && ownerIsValid
            && IsValidTargets(kind, key.AsSpan(arrow + 2));
    }

    static bool IsValidLocalFunctionOwner(ReadOnlySpan<char> owner)
    {
        if (int.TryParse(
                owner,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int legacyOrdinal))
        {
            return legacyOrdinal >= 0;
        }
        if (owner.Length < 3 || owner[0] != 'n' || (owner.Length - 1) % 2 != 0)
            return false;
        foreach (char digit in owner[1..])
        {
            if (digit is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))
                return false;
        }
        return true;
    }

    static bool IsValidTargets(string kind, ReadOnlySpan<char> targets)
    {
        if (kind is "end-finally" or "end-filter")
            return targets.SequenceEqual("-");
        if (targets.IsEmpty)
            return kind == "switch-branch";

        int targetCount = 0;
        foreach (Range range in targets.Split(','))
        {
            targetCount++;
            ReadOnlySpan<char> target = targets[range];
            if (!target.StartsWith("IL_", StringComparison.Ordinal)
                || !int.TryParse(
                    target[3..],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out int offset)
                || offset < 0)
            {
                return false;
            }
        }
        return kind == "switch-branch" || targetCount == 1;
    }
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
        => CorpusControlFlowOutputIdentity.TryParse(
                identity,
                out string identityKind,
                out int identityOffset,
                out int identityOrdinal)
            && identityKind == kind
            && identityOffset == ilOffset
            && identityOrdinal == ordinal;

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
