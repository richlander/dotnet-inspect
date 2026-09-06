using System.Collections.Immutable;

namespace DotnetInspector.Sections;

/// <summary>CLI query bindings, independent of a section's projected columns.</summary>
public sealed record SectionQueryFacet(
    string Name,
    ImmutableArray<string> Operators,
    ImmutableArray<string> Comparisons,
    string ValueKind,
    ImmutableArray<string> Values,
    string Example);
