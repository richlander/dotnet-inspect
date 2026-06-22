using System.Text.Json;

namespace DotnetInspector.BuildEvents.Events;

public sealed record BuildStartedPayload(string? Message);

public sealed record BuildFinishedPayload(bool Succeeded, string? Message);

public sealed record ProjectDimensions(
    string? TargetFramework,
    string? TargetFrameworks,
    string? RuntimeIdentifier,
    string? RuntimeIdentifiers,
    string? Configuration,
    string? Platform,
    string? SelfContained)
{
    public bool HasAnyDimension =>
        TargetFramework is not null ||
        TargetFrameworks is not null ||
        RuntimeIdentifier is not null ||
        RuntimeIdentifiers is not null ||
        Configuration is not null ||
        Platform is not null ||
        SelfContained is not null;
}

public sealed record ProjectStartedPayload(
    int ProjectId,
    string? ProjectFile,
    string? TargetNames,
    string? ToolsVersion,
    ProjectDimensions Dimensions,
    bool HasParentContext,
    BuildEventContextData ParentContext);

public sealed record ProjectFinishedPayload(
    string? ProjectFile,
    bool Succeeded,
    ProjectDimensions Dimensions,
    bool HasParentContext,
    BuildEventContextData ParentContext);

public sealed record TargetStartedPayload(
    string? ProjectFile,
    string? TargetFile,
    string? TargetName,
    string? ParentTarget,
    int BuildReason);

public sealed record TargetFinishedPayload(
    string? ProjectFile,
    string? TargetFile,
    string? TargetName,
    bool Succeeded);

public sealed record TaskStartedPayload(
    string? ProjectFile,
    string? TaskFile,
    string? TaskName,
    string? TaskAssemblyLocation,
    int LineNumber,
    int ColumnNumber);

public sealed record TaskFinishedPayload(
    string? ProjectFile,
    string? TaskFile,
    string? TaskName,
    bool Succeeded);

public sealed record DiagnosticPayload(
    string Severity,
    string? Subcategory,
    string? Code,
    string? File,
    string? ProjectFile,
    int LineNumber,
    int ColumnNumber,
    int EndLineNumber,
    int EndColumnNumber,
    string? Message,
    string? HelpKeyword);

public sealed record OutputPayload(
    string? Message,
    JsonElement Importance);

public sealed record ArtifactPayload(
    string? Path,
    string? ArtifactKind,
    string? ProjectFile);

public sealed record SummaryPayload(
    bool Succeeded,
    int ErrorCount,
    int WarningCount);

public sealed record ExtensionPayload(string EventArgsType, string? Message);
