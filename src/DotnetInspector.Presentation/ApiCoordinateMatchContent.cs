using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Presentation;

public enum ApiCoordinateMatchStatus
{
    Exact,
    Absent,
    Ambiguous,
    Refused,
    Failed,
}

public enum ApiCoordinateMatchStage
{
    Acquisition,
    Scope,
    Observation,
    SourceSelection,
    SourceBinding,
    LibraryPairing,
    TypeResolution,
    DeclarationCorrespondence,
}

public sealed record ApiCoordinateMatchEndpoint(
    InertString PackageId,
    InertString Version,
    InertString? TargetFramework);

public sealed record ApiCoordinateMatchAssembly(
    InertString Name,
    InertString? Version,
    InertString? Culture,
    InertString? PublicKeyToken);

/// <summary>A descriptive coordinate; it is not permission to reopen an artifact.</summary>
public sealed record ApiCoordinateMatchLocation(
    ApiCoordinateMatchEndpoint? Package,
    InertString? Asset,
    ApiCoordinateMatchAssembly Assembly,
    InertString? Type,
    InertString? Member,
    InertString? Signature,
    Guid? ModuleVersionId,
    int? MetadataToken,
    ApiDeclarationKind? DeclarationKind = null);

public sealed record ApiCoordinateMatchHop(
    ApiCoordinateMatchLocation Source,
    ApiCoordinateMatchAssembly Target,
    ImmutableArray<int> ExportedTypeTokens,
    AssemblyResolutionScope Scope);

/// <summary>One native producer outcome, with its owner-issued reason when applicable.</summary>
public sealed record ApiCoordinateMatchStageEvidence(
    ApiCoordinateMatchStage Stage,
    string Outcome,
    string? Reason,
    InertString? Detail,
    ApiCoordinateMatchAssembly? Target = null,
    int? Budget = null,
    int? CandidateCount = null);

/// <summary>
/// A completed, host-neutral match document. Transport contains descriptive
/// coordinates and staged outcomes; process-local evidence retains exact
/// Workspace associations without serializing them as portable authority.
/// </summary>
public sealed record ApiCoordinateMatchContent
{
    public required ApiCoordinateMatchStatus Status { get; init; }
    public required ApiCoordinateMatchStage Stage { get; init; }
    public required InertString Summary { get; init; }
    public required ApiCoordinateMatchEndpoint Before { get; init; }
    public required ApiCoordinateMatchEndpoint After { get; init; }
    public ApiCoordinateMatchLocation? Source { get; init; }
    public ApiCoordinateMatchLocation? DestinationEntry { get; init; }
    public ApiCoordinateMatchLocation? Destination { get; init; }
    public ImmutableArray<ApiCoordinateMatchLocation> Candidates { get; init; } = [];
    public ImmutableArray<ApiCoordinateMatchHop> ForwardingHops { get; init; } = [];
    public ImmutableArray<ApiCoordinateMatchStageEvidence> Stages { get; init; } = [];

    /// <summary>Exact, resource-free evidence for in-process consumers; never a portable receipt.</summary>
    [JsonIgnore]
    public ApiCoordinateCorrespondenceResult? Evidence { get; init; }
}
