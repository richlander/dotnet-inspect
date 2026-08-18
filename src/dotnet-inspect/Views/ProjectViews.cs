using System.Text.Json.Serialization;
using ILInspector.CSharp;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable]
internal sealed class ProjectInspectionView
{
    [MarkoutSection(
        Name = "Skills",
        EmptyText = "No skills found in the restored direct package dependencies.")]
    public List<ProjectSkillRow>? Skills { get; init; }

    [MarkoutSection(Name = "Agent Guidance")]
    public List<ProjectAgentGuidanceRow>? AgentGuidance { get; init; }

    [MarkoutSection(
        Name = "Package Docs",
        EmptyText = "No package documents found in the restored direct package dependencies.")]
    public List<ProjectPackageDocumentRow>? PackageDocs { get; init; }
}

[MarkoutSerializable]
internal sealed record ProjectSkillRow(
    string Package,
    string Version,
    string Path,
    long? Size,
    string Name,
    string Description)
{
    public string Package { get; init; } = CSharpIdentifier.ContainRenderedText(Package);
    public string Version { get; init; } = CSharpIdentifier.ContainRenderedText(Version);
    public string Path { get; init; } = CSharpIdentifier.ContainRenderedText(Path);
    public long? Size { get; init; } = Size;
    public string Name { get; init; } = CSharpIdentifier.ContainRenderedText(Name);
    public string Description { get; init; } = CSharpIdentifier.ContainRenderedText(Description);
}

[MarkoutSerializable]
internal sealed record ProjectAgentGuidanceRow(
    string Package,
    string Version,
    string Path,
    string Name,
    string Description)
{
    public string Package { get; init; } = CSharpIdentifier.ContainRenderedText(Package);
    public string Version { get; init; } = CSharpIdentifier.ContainRenderedText(Version);
    public string Path { get; init; } = CSharpIdentifier.ContainRenderedText(Path);
    public string Name { get; init; } = CSharpIdentifier.ContainRenderedText(Name);
    public string Description { get; init; } = CSharpIdentifier.ContainRenderedText(Description);
}

[MarkoutSerializable]
internal sealed record ProjectPackageDocumentRow(
    string Package,
    string Version,
    string Path,
    long Size)
{
    public string Package { get; init; } = CSharpIdentifier.ContainRenderedText(Package);
    public string Version { get; init; } = CSharpIdentifier.ContainRenderedText(Version);
    public string Path { get; init; } = CSharpIdentifier.ContainRenderedText(Path);
    public long Size { get; init; } = Size;
}

[MarkoutContext(typeof(ProjectInspectionView))]
[MarkoutContext(typeof(ProjectSkillRow))]
[MarkoutContext(typeof(ProjectAgentGuidanceRow))]
[MarkoutContext(typeof(ProjectPackageDocumentRow))]
internal partial class ProjectViewContext : MarkoutSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectInspectionView))]
internal partial class ProjectViewJsonContext : JsonSerializerContext
{
}
