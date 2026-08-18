using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>A project package file that could not be acquired or read.</summary>
public sealed record ProjectContentFailure(
    string Package,
    string Path,
    string Reason);

/// <summary>One skill declared by a direct project dependency.</summary>
public sealed record ProjectSkillData(
    string Package,
    string Version,
    string Path,
    long? Size,
    string Name,
    string Description,
    string? Content);

/// <summary>Typed result of inspecting dependency skill files.</summary>
public sealed record ProjectSkillsResult(
    ImmutableArray<ProjectSkillData> Skills,
    ImmutableArray<ProjectContentFailure> Failures);

/// <summary>Projects already-acquired skill documents into a deterministic result.</summary>
public static class ProjectSkillsQuery
{
    public static InspectionQuery<ProjectSkillsResult> Definition { get; } =
        new("Project skills", InspectionCost.NetworkFree);

    public static ProjectSkillsResult Execute(
        IEnumerable<ProjectSkillData> skills,
        IEnumerable<ProjectContentFailure>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(skills);
        return new ProjectSkillsResult(
            [.. skills
                .OrderBy(skill => skill.Package, StringComparer.OrdinalIgnoreCase)
                .ThenBy(skill => skill.Path, StringComparer.Ordinal)],
            failures is null ? [] : [.. failures]);
    }
}

/// <summary>One direct dependency's optional AGENTS.md guidance.</summary>
public sealed record ProjectAgentGuidanceData(
    string Package,
    string Version,
    string Path,
    string Name,
    string Description,
    string? Content);

/// <summary>Typed result of inspecting direct-dependency agent guidance.</summary>
public sealed record ProjectAgentGuidanceResult(
    ImmutableArray<ProjectAgentGuidanceData> Guidance,
    ImmutableArray<ProjectContentFailure> Failures);

/// <summary>Projects already-acquired AGENTS.md documents into a deterministic result.</summary>
public static class ProjectAgentGuidanceQuery
{
    public static InspectionQuery<ProjectAgentGuidanceResult> Definition { get; } =
        new("Project agent guidance", InspectionCost.NetworkFree);

    public static ProjectAgentGuidanceResult Execute(
        IEnumerable<ProjectAgentGuidanceData> guidance,
        IEnumerable<ProjectContentFailure>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(guidance);
        return new ProjectAgentGuidanceResult(
            [.. guidance.OrderBy(item => item.Package, StringComparer.OrdinalIgnoreCase)],
            failures is null ? [] : [.. failures]);
    }
}

/// <summary>One README or PROJECT document from a direct dependency.</summary>
public sealed record ProjectPackageDocumentData(
    string Package,
    string Version,
    string Path,
    long Size,
    string Content);

/// <summary>Typed result of inspecting direct-dependency package documents.</summary>
public sealed record ProjectPackageDocumentsResult(
    ImmutableArray<ProjectPackageDocumentData> Documents,
    ImmutableArray<ProjectContentFailure> Failures);

/// <summary>Projects already-acquired package documents into a deterministic result.</summary>
public static class ProjectPackageDocumentsQuery
{
    public static InspectionQuery<ProjectPackageDocumentsResult> Definition { get; } =
        new("Project package documents", InspectionCost.Unbounded);

    public static ProjectPackageDocumentsResult Execute(
        IEnumerable<ProjectPackageDocumentData> documents,
        IEnumerable<ProjectContentFailure>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return new ProjectPackageDocumentsResult(
            [.. documents.OrderBy(document => document.Package, StringComparer.OrdinalIgnoreCase)],
            failures is null ? [] : [.. failures]);
    }
}
