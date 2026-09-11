using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>
/// One package occurrence in an ordered Workspace view.
/// </summary>
public sealed class InspectionWorkspacePackageOccurrenceDescriptor
{
    internal InspectionWorkspacePackageOccurrenceDescriptor(
        PackageRootOccurrenceBinding occurrence,
        InspectionWorkspacePackageOccurrenceAction action)
    {
        Occurrence = occurrence;
        Action = action;
    }

    /// <summary>The exact Workspace-bound package occurrence.</summary>
    public PackageRootOccurrenceBinding Occurrence { get; }

    /// <summary>An opaque request to activate this exact occurrence.</summary>
    public InspectionWorkspacePackageOccurrenceAction Action { get; }

    public string PackageId => Occurrence.RootBinding.Root.PackageId;

    public string Version => Occurrence.RootBinding.Root.PackageVersion;

    public string? Framework =>
        Occurrence.RootBinding.Root.AssetSelection.TargetFramework
        ?? Occurrence.RootBinding.Root.RequestedTargetFramework
        ?? Occurrence.RootBinding.Coordinate.Framework;
}

/// <summary>
/// Opaque request to activate one exact package occurrence in one exact view.
/// </summary>
public sealed class InspectionWorkspacePackageOccurrenceAction
{
    internal InspectionWorkspacePackageOccurrenceAction(
        InspectionWorkspacePackageOccurrenceView view,
        PackageRootOccurrenceBinding occurrence)
    {
        View = view;
        Occurrence = occurrence;
    }

    internal InspectionWorkspacePackageOccurrenceView View { get; }

    internal PackageRootOccurrenceBinding Occurrence { get; }
}

public enum InspectionWorkspacePackageOccurrenceActivationRejection
{
    ViewMismatch,
    WorkspaceClosed,
}

public abstract record InspectionWorkspacePackageOccurrenceActivation
{
    private protected InspectionWorkspacePackageOccurrenceActivation()
    {
    }

    public sealed record Activated(
        PackageRootOccurrenceBinding Occurrence)
        : InspectionWorkspacePackageOccurrenceActivation;

    public sealed record Rejected(
        InspectionWorkspacePackageOccurrenceActivationRejection Reason)
        : InspectionWorkspacePackageOccurrenceActivation;
}

/// <summary>
/// Immutable ordered package-occurrence view for one exact Workspace.
/// </summary>
public sealed class InspectionWorkspacePackageOccurrenceView
{
    readonly InspectionWorkspace _workspace;

    internal InspectionWorkspacePackageOccurrenceView(
        InspectionWorkspace workspace,
        ImmutableArray<PackageRootOccurrenceBinding> occurrences)
    {
        _workspace = workspace;
        Workspace = workspace.Identity;

        var descriptors =
            ImmutableArray.CreateBuilder<
                InspectionWorkspacePackageOccurrenceDescriptor>(
                    occurrences.Length);
        foreach (PackageRootOccurrenceBinding occurrence in occurrences)
        {
            var action =
                new InspectionWorkspacePackageOccurrenceAction(
                    this,
                    occurrence);
            descriptors.Add(
                new InspectionWorkspacePackageOccurrenceDescriptor(
                    occurrence,
                    action));
        }
        Occurrences = descriptors.MoveToImmutable();
    }

    public InspectionWorkspaceIdentity Workspace { get; }

    public ImmutableArray<InspectionWorkspacePackageOccurrenceDescriptor>
        Occurrences { get; }

    public InspectionWorkspacePackageOccurrenceActivation Activate(
        InspectionWorkspacePackageOccurrenceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _workspace.ActivatePackageOccurrence(this, action);
    }
}

public sealed partial class InspectionWorkspace
{
    /// <summary>
    /// Issues an immutable ordered view over already-acquired package Roots.
    /// </summary>
    public InspectionWorkspacePackageOccurrenceView
        CreatePackageOccurrenceView(
            IEnumerable<PackageRootBinding> rootBindings)
    {
        ArgumentNullException.ThrowIfNull(rootBindings);
        ImmutableArray<PackageRootBinding> bindings = [.. rootBindings];
        if (bindings.Any(static binding => binding is null))
        {
            throw new ArgumentException(
                "A package occurrence view cannot contain a null Root binding.",
                nameof(rootBindings));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open,
                this);
            ImmutableArray<PackageRootOccurrenceBinding> occurrences =
            [
                .. bindings.Select(binding =>
                    new PackageRootOccurrenceBinding(
                        _identity,
                        binding)),
            ];
            return new InspectionWorkspacePackageOccurrenceView(
                this,
                occurrences);
        }
    }

    internal InspectionWorkspacePackageOccurrenceActivation
        ActivatePackageOccurrence(
            InspectionWorkspacePackageOccurrenceView view,
            InspectionWorkspacePackageOccurrenceAction action)
    {
        lock (_gate)
        {
            if (_state != InspectionWorkspaceState.Open)
            {
                return new InspectionWorkspacePackageOccurrenceActivation
                    .Rejected(
                        InspectionWorkspacePackageOccurrenceActivationRejection
                            .WorkspaceClosed);
            }
            if (!ReferenceEquals(action.View, view))
            {
                return new InspectionWorkspacePackageOccurrenceActivation
                    .Rejected(
                        InspectionWorkspacePackageOccurrenceActivationRejection
                            .ViewMismatch);
            }

            return new InspectionWorkspacePackageOccurrenceActivation
                .Activated(action.Occurrence);
        }
    }
}
