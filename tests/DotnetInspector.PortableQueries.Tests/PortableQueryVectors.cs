using System.Reflection;
using System.Text.Json;

namespace DotnetInspector.PortableQueries.Tests;

/// <summary>
/// The normative witnesses, read from the design model rather than restated here.
/// </summary>
/// <remarks>
/// <c>docs/design/models/portable-query-payload/vectors.json</c> is embedded in this
/// assembly and is the gate for the codec: every canonical form the contract
/// promises and every rejection it requires. A rule with no vector is a gap in that
/// file, not a rule this suite may assert on its own.
/// </remarks>
public static class PortableQueryVectors
{
    private const string ResourceName =
        "DotnetInspector.PortableQueries.Tests.PortableQueryPayloadVectors.json";

    private static readonly JsonDocument s_document = Load();

    /// <summary>An intent and the bytes it must become.</summary>
    public static IReadOnlyList<string> EncodeNames { get; } = NamesIn("encode");

    /// <summary>An intent that must be refused, and why.</summary>
    public static IReadOnlyList<string> EncodeRejectNames { get; } = NamesIn("encode-reject");

    /// <summary>Bytes that must be refused, and why.</summary>
    public static IReadOnlyList<string> RejectNames { get; } = NamesIn("reject");

    /// <summary>Two states that are, or are not, one query.</summary>
    public static IReadOnlyList<string> PairNames { get; } = NamesIn("pair");

    public static TheoryData<string> Encode => Cases(EncodeNames);

    public static TheoryData<string> EncodeReject => Cases(EncodeRejectNames);

    public static TheoryData<string> Reject => Cases(RejectNames);

    public static TheoryData<string> Pair => Cases(PairNames);

    /// <summary>Looks one vector up by kind and name.</summary>
    public static JsonElement Get(string kind, string name)
    {
        foreach (JsonElement vector in s_document.RootElement.GetProperty(kind).EnumerateArray())
        {
            if (string.Equals(vector.GetProperty("name").GetString(), name, StringComparison.Ordinal))
                return vector;
        }

        throw new InvalidOperationException($"No {kind} vector named '{name}'.");
    }

    /// <summary>Reads the failure kind a vector names, by its wire text.</summary>
    public static PortableQueryPayloadFailureKind ExpectedFailure(JsonElement vector)
    {
        string text = vector.GetProperty("reject").GetString()!;
        return PortableQueryPayloadFailure.TryParse(text, out PortableQueryPayloadFailureKind kind)
            ? kind
            : throw new InvalidOperationException(
                $"Vector '{vector.GetProperty("name").GetString()}' names an unknown failure '{text}'.");
    }

    private static TheoryData<string> Cases(IReadOnlyList<string> names)
    {
        var data = new TheoryData<string>();
        foreach (string name in names) data.Add(name);
        return data;
    }

    private static IReadOnlyList<string> NamesIn(string kind)
    {
        var names = new List<string>();
        foreach (JsonElement vector in s_document.RootElement.GetProperty(kind).EnumerateArray())
            names.Add(vector.GetProperty("name").GetString()!);
        return names;
    }

    private static JsonDocument Load()
    {
        using Stream stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The vectors resource '{ResourceName}' is missing from the test assembly.");
        using var reader = new StreamReader(stream);
        return JsonDocument.Parse(reader.ReadToEnd());
    }
}
