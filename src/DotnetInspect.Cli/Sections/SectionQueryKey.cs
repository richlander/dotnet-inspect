using System.Collections.Immutable;

namespace DotnetInspect.Cli.Sections;

/// <summary>CLI query bindings, independent of a section's projected columns.</summary>
public sealed record SectionQueryKey(
    string Name,
    ImmutableArray<string> Operators,
    ImmutableArray<string> Comparisons,
    string ValueKind,
    ImmutableArray<string> Values,
    string Example,
    string? ExecutionClass = null,
    string? ResourcePath = null,
    SectionQueryValueVocabulary? ValueVocabulary = null);

/// <summary>
/// The product value vocabulary whose identities a query key accepts: its
/// display name and the canonical explanation path of its resource.
/// </summary>
public sealed record SectionQueryValueVocabulary(
    string Name,
    string ResourcePath);
