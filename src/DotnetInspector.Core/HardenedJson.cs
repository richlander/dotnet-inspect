using System.Text.Json;

namespace DotnetInspector.Core;

/// <summary>
/// Parses JSON from untrusted sources (NuGet feed responses, package contents, restored project
/// inputs, product caches) with duplicate property names rejected.
/// </summary>
/// <remarks>
/// <para>
/// JSON does not define how duplicate object keys are resolved, so independent readers of the same
/// payload can disagree. <c>JsonElement.TryGetProperty</c> returns the first match while
/// <c>JsonElement.EnumerateObject</c> yields every occurrence, and any two readers that filter or
/// order that set differently can end up on different values. The disagreement is exploitable
/// whenever one reader decides policy and another decides what is shown or acted on, letting a
/// single document present two views of itself. See CVE-2017-12635 for the canonical instance.
/// </para>
/// <para>
/// This mirrors <see cref="HardenedXml"/>: parsing untrusted input goes through a named, hardened
/// entry point rather than through per-call-site options that are easy to omit. Malformed and
/// duplicate-bearing input surfaces as <see cref="JsonException"/> so callers fail visibly instead
/// of silently binding one of several possible readings.
/// </para>
/// </remarks>
public static class HardenedJson
{
    /// <summary>
    /// Document options that reject duplicate property names. Use this only for parses that cannot
    /// go through <see cref="Parse(string)"/>, such as a <see cref="Utf8JsonReader"/> loop.
    /// </summary>
    public static JsonDocumentOptions DocumentOptions => new() { AllowDuplicateProperties = false };

    /// <summary>Parses a <see cref="JsonDocument"/>, rejecting duplicate property names.</summary>
    /// <exception cref="JsonException">The input is malformed or contains a duplicate property name.</exception>
    public static JsonDocument Parse(string json) => JsonDocument.Parse(json, DocumentOptions);

    /// <summary>Parses a <see cref="JsonDocument"/> from UTF-8 bytes, rejecting duplicate property names.</summary>
    /// <exception cref="JsonException">The input is malformed or contains a duplicate property name.</exception>
    public static JsonDocument Parse(ReadOnlyMemory<byte> utf8Json) => JsonDocument.Parse(utf8Json, DocumentOptions);
}
