using System.Text;
using System.Text.Json;

namespace DotnetInspector.PortableQueries.Tests;

/// <summary>
/// The codec against every normative witness.
/// </summary>
/// <remarks>
/// These tests replace the design-stage probe that formerly ran from
/// <c>eng/</c>: the same file now gates the product codec, so a vector and the
/// implementation can no longer drift apart unnoticed.
/// </remarks>
public sealed class PortableQueryPayloadVectorTests
{
    /// <summary>
    /// An intent becomes exactly the bytes the vector names, those bytes decode,
    /// and the intent they decode to emits them again unchanged.
    /// </summary>
    [Theory]
    [MemberData(nameof(PortableQueryVectors.Encode), MemberType = typeof(PortableQueryVectors))]
    public void Encode_ProducesTheCanonicalBytes(string name)
    {
        JsonElement vector = PortableQueryVectors.Get("encode", name);
        string expected = vector.GetProperty("canonical").GetString()!;

        PortableQueryIntent parsed = CodecUnderTest.ParseJson(
            vector.GetProperty("intent").GetRawText());

        Assert.Equal(expected, CodecUnderTest.Encode(parsed));

        // The canonical bytes are themselves admissible, and the intent they carry
        // emits them again: one intent, one byte sequence, in both directions.
        PortableQueryIntent decoded = CodecUnderTest.Decode(expected);
        Assert.Equal(expected, CodecUnderTest.Encode(decoded));

        if (vector.TryGetProperty("expectBytes", out JsonElement expectBytes))
            Assert.Equal(expectBytes.GetInt32(), Encoding.UTF8.GetByteCount(expected));

        if (vector.TryGetProperty("expectValues", out JsonElement expectValues))
            Assert.Equal(expectValues.GetInt32(), CountJsonValues(expected));
    }

    /// <summary>An intent the contract refuses is refused for the stated reason.</summary>
    [Theory]
    [MemberData(nameof(PortableQueryVectors.EncodeReject), MemberType = typeof(PortableQueryVectors))]
    public void Encode_RefusesTheIntent(string name)
    {
        JsonElement vector = PortableQueryVectors.Get("encode-reject", name);

        PortableQueryPayloadException failure = Assert.Throws<PortableQueryPayloadException>(
            () => CodecUnderTest.Encode(
                CodecUnderTest.ParseJson(vector.GetProperty("intent").GetRawText())));

        Assert.Equal(PortableQueryVectors.ExpectedFailure(vector), failure.Kind);
    }

    /// <summary>Bytes the contract refuses are refused for the stated reason.</summary>
    [Theory]
    [MemberData(nameof(PortableQueryVectors.Reject), MemberType = typeof(PortableQueryVectors))]
    public void Decode_RefusesTheBytes(string name)
    {
        JsonElement vector = PortableQueryVectors.Get("reject", name);

        PortableQueryPayloadException failure = Assert.Throws<PortableQueryPayloadException>(
            () => CodecUnderTest.Decode(vector.GetProperty("bytes").GetString()!));

        Assert.Equal(PortableQueryVectors.ExpectedFailure(vector), failure.Kind);
    }

    /// <summary>Two states are one query exactly when the vector says they are.</summary>
    [Theory]
    [MemberData(nameof(PortableQueryVectors.Pair), MemberType = typeof(PortableQueryVectors))]
    public void Identity_IsTheVocabularyAndBytesTogether(string name)
    {
        JsonElement vector = PortableQueryVectors.Get("pair", name);

        PortableQueryIdentity left = Read(vector.GetProperty("a"));
        PortableQueryIdentity right = Read(vector.GetProperty("b"));

        Assert.Equal(vector.GetProperty("same").GetBoolean(), left == right);

        static PortableQueryIdentity Read(JsonElement state) => new(
            state.GetProperty("queryId").GetString()!,
            state.GetProperty("canonical").GetString()!);
    }

    /// <summary>Every vector file kind is populated, so a silent empty read fails.</summary>
    [Fact]
    public void Vectors_AreLoaded()
    {
        Assert.NotEmpty(PortableQueryVectors.EncodeNames);
        Assert.NotEmpty(PortableQueryVectors.EncodeRejectNames);
        Assert.NotEmpty(PortableQueryVectors.RejectNames);
        Assert.NotEmpty(PortableQueryVectors.PairNames);
    }

    /// <summary>
    /// Every rejection the codec declares is named by at least one witness.
    /// </summary>
    /// <remarks>
    /// A reason with no vector is a gap in the file rather than a reason that needs
    /// no evidence, so the closed set and the witnesses are checked against each
    /// other rather than assumed to agree.
    /// </remarks>
    [Fact]
    public void EveryFailureKind_HasAWitness()
    {
        var witnessed = new HashSet<PortableQueryPayloadFailureKind>();
        foreach (string name in PortableQueryVectors.EncodeRejectNames)
            witnessed.Add(PortableQueryVectors.ExpectedFailure(PortableQueryVectors.Get("encode-reject", name)));
        foreach (string name in PortableQueryVectors.RejectNames)
            witnessed.Add(PortableQueryVectors.ExpectedFailure(PortableQueryVectors.Get("reject", name)));

        Assert.Equal(
            Enum.GetValues<PortableQueryPayloadFailureKind>().Order().ToArray(),
            witnessed.Order().ToArray());
    }

    private static int CountJsonValues(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return Count(document.RootElement);

        static int Count(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Object => 1 + element.EnumerateObject().Sum(p => Count(p.Value)),
            JsonValueKind.Array => 1 + element.EnumerateArray().Sum(Count),
            _ => 1
        };
    }
}
