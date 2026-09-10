using DotnetInspector.Packages;

namespace DotnetInspector.Queries;

/// <summary>
/// Opaque process-local correspondence for one logical Artifact Root request.
/// </summary>
/// <remarks>
/// Correspondence is resource-free and Workspace-scoped. Equality uses only
/// owner-issued logical request facts; it never uses a physical generation,
/// display text, or retained artifact resource.
/// </remarks>
public abstract record ArtifactRootCorrespondence
{
    private protected ArtifactRootCorrespondence(
        InspectionWorkspaceIdentity workspaceIdentity)
    {
        ArgumentNullException.ThrowIfNull(workspaceIdentity);
        WorkspaceIdentity = workspaceIdentity;
    }

    internal InspectionWorkspaceIdentity WorkspaceIdentity { get; }
}

/// <summary>
/// Resource-free correspondence for one exact package Root request.
/// </summary>
public sealed record PackageArtifactRootCorrespondence :
    ArtifactRootCorrespondence
{
    readonly PackageArtifactRootRequest _request;

    internal PackageArtifactRootCorrespondence(
        InspectionWorkspaceIdentity workspaceIdentity,
        PackageArtifactRootRequest request)
        : base(workspaceIdentity)
    {
        _request = request;
    }

    internal bool Matches(PackageArtifactRootRequest request) =>
        _request == request;
}

internal readonly record struct PackageArtifactRootRequest(
    RealizedMemberCoordinate.Package Coordinate,
    string? CompileTargetFramework,
    string? SelectionTargetFramework,
    string? SelectionRuntimeIdentifier,
    bool UsesCompatibleImplementationSelection,
    bool HasFrozenImplementationSelection)
{
    internal static PackageArtifactRootRequest From(
        PackageRootBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return Create(
            binding.Coordinate,
            binding.CompileTargetFramework,
            binding.Root.RequestedTargetFramework,
            binding.Root.RequestedRuntimeIdentifier,
            binding.UsesCompatibleImplementationSelection,
            binding.HasFrozenImplementationSelection);
    }

    internal static PackageArtifactRootRequest Create(
        RealizedMemberCoordinate.Package coordinate,
        string? compileTargetFramework,
        string? selectionTargetFramework,
        string? selectionRuntimeIdentifier,
        bool usesCompatibleImplementationSelection = false,
        bool hasFrozenImplementationSelection = false)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        string? normalizedCompileTarget =
            NormalizeFramework(compileTargetFramework);
        string? normalizedSelectionTarget =
            NormalizeFramework(selectionTargetFramework);
        if ((normalizedCompileTarget is null)
            != (normalizedSelectionTarget is null))
        {
            throw new ArgumentException(
                "Compile and implementation selection targets must both be present or both be absent.",
                nameof(selectionTargetFramework));
        }
        if (normalizedCompileTarget is null
            && usesCompatibleImplementationSelection)
        {
            throw new ArgumentException(
                "Compatible implementation selection requires a target framework.",
                nameof(usesCompatibleImplementationSelection));
        }

        bool effectiveCompatibleSelection =
            usesCompatibleImplementationSelection
            || !string.Equals(
                normalizedCompileTarget,
                normalizedSelectionTarget,
                StringComparison.Ordinal);
        if (hasFrozenImplementationSelection && !effectiveCompatibleSelection)
        {
            throw new ArgumentException(
                "A frozen implementation selection requires compatible implementation selection.",
                nameof(hasFrozenImplementationSelection));
        }

        return new(
            coordinate,
            normalizedCompileTarget,
            normalizedSelectionTarget,
            NormalizeRuntime(selectionRuntimeIdentifier),
            effectiveCompatibleSelection,
            hasFrozenImplementationSelection);
    }

    internal static string? NormalizeFramework(string? framework)
    {
        if (string.IsNullOrWhiteSpace(framework))
            return null;

        return NuGetTargetFrameworkIdentity.TryNormalize(
            framework,
            out string canonical)
                ? canonical
                : PackageCoordinateResolver.IsAcquisitionTargetText(
                    framework)
                    ? framework.ToLowerInvariant()
                    : framework;
    }

    internal static string? NormalizeRuntime(string? runtimeIdentifier) =>
        RealizedMemberCoordinate.IsCanonicalRuntimeIdentifier(
            runtimeIdentifier)
            ? runtimeIdentifier!.ToLowerInvariant()
            : runtimeIdentifier;
}

public sealed partial class InspectionWorkspace
{
    internal PackageArtifactRootCorrespondence
        CreatePackageArtifactRootCorrespondence(
            PackageRootBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open,
                this);
            return new(
                _identity,
                PackageArtifactRootRequest.From(binding));
        }
    }
}
