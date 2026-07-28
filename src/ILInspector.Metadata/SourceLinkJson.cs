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
/// divergence. It deliberately does <em>not</em> close the SourceLink provenance gap: the
/// repository-URL reader in <see cref="AssemblyInspector"/> stops at the first <c>documents</c>
/// entry, while <see cref="SourceDocumentPathResolver"/> orders mappings by descending pattern
/// length and takes the first match. Because a duplicated key keeps document order under a stable
/// sort, both readers land on the same first entry, so duplication alone cannot make them
/// disagree. They diverge on maps with <em>distinct</em> keys, which are well-formed and remain
/// accepted. That gap is tracked separately; see the SourceLink entry in
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
