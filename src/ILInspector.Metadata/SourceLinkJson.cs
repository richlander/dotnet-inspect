using System.Text.Json;

namespace ILInspector.Metadata;

/// <summary>
/// Parses JSON from untrusted assembly-derived sources (SourceLink maps recovered from portable
/// PDBs) with duplicate property names rejected.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors <c>DotnetInspector.Core.HardenedJson</c> deliberately. Metadata sits below the
/// Core infrastructure layer and references only Findings, MetadataPrimitives, and Text; taking a
/// dependency on Core to share ten lines would pull HTTP, caching, and download infrastructure
/// into the SRM layer and invert the ownership documented in <c>docs/overview.md</c>.
/// </para>
/// <para>
/// This is generic fail-visible hardening for an attacker-controlled map, not a fix for a known
/// divergence. The SourceLink provenance divergence it does <em>not</em> address is closed
/// separately by <c>SourceLinkFetch.SourceLinkProvenance</c>, which reads the origin off the URL
/// source is fetched from; see the SourceLink provenance control in
/// <c>docs/design/untrusted-data-threat-model.md</c>.
/// </para>
/// </remarks>
internal static class SourceLinkJson
{
    private static JsonDocumentOptions DocumentOptions => new() { AllowDuplicateProperties = false };

    /// <summary>Parses a <see cref="JsonDocument"/>, rejecting duplicate property names.</summary>
    /// <exception cref="JsonException">The input is malformed or contains a duplicate property name.</exception>
    public static JsonDocument Parse(string json) => JsonDocument.Parse(json, DocumentOptions);
}
