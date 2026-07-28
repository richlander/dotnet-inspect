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
/// The duplicate-key hazard is concrete here. <see cref="SourceDocumentPathResolver"/> and the
/// SourceLink readers in <see cref="AssemblyInspector"/> parse the same attacker-controlled map,
/// and they select differently over a duplicated <c>documents</c> entry: the resolver orders
/// mappings by descending pattern length and takes the first match, while the repository-URL
/// reader takes the first entry whose value mentions a known host. Given two entries under one
/// key, those rules pick different values, so a hostile PDB can resolve source from one origin
/// while the tool reports provenance from another.
/// </para>
/// </remarks>
internal static class SourceLinkJson
{
    private static JsonDocumentOptions DocumentOptions => new() { AllowDuplicateProperties = false };

    /// <summary>Parses a <see cref="JsonDocument"/>, rejecting duplicate property names.</summary>
    /// <exception cref="JsonException">The input is malformed or contains a duplicate property name.</exception>
    public static JsonDocument Parse(string json) => JsonDocument.Parse(json, DocumentOptions);
}
