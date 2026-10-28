using System.Collections.Immutable;

namespace DotnetInspector.Sections;

/// <summary>One owner-issued structural declaration and its canonical paths.</summary>
public sealed record StructuralDiscoveryProjection(
    DiscoveryDocument Document,
    ImmutableArray<StructuralResourcePathRegistration> ResourcePaths);

/// <summary>An owner-issued item augmenting a generated structural section.</summary>
public sealed record StructuralDiscoveryItem(
    string Name,
    string Kind,
    string PathSegment);
