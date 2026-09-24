using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// Opaque reference identity for one exact realized package Root. Descriptive
/// fields do not define equality; consumers correlate Roots by object identity.
/// </summary>
public sealed class PackageRootIdentity
{
    internal PackageRootIdentity(
        string packageId,
        string packageVersion,
        string? requestedTargetFramework,
        string? requestedRuntimeIdentifier)
    {
        PackageId = packageId;
        PackageVersion = packageVersion;
        RequestedTargetFramework = requestedTargetFramework;
        RequestedRuntimeIdentifier = requestedRuntimeIdentifier;
    }

    public string PackageId { get; }

    public string PackageVersion { get; }

    public string? RequestedTargetFramework { get; }

    public string? RequestedRuntimeIdentifier { get; }
}

/// <summary>
/// Opaque identity for one immutable package compile-asset selection.
/// </summary>
/// <remarks>
/// Equality is reference identity. A token is occurrence-local: equal tokens
/// guarantee the same typed selection arm and ordered asset sequences, while
/// independently repeated equal selections may receive different tokens.
/// </remarks>
public sealed class PackageRootSelectionIdentity
{
    internal PackageRootSelectionIdentity()
    {
    }
}

/// <summary>
/// Acquisition-issued binding among one package Root, its authoritative
/// realized coordinate, retained content generation, compile-asset selection,
/// and target-selection intent.
/// </summary>
public sealed class PackageRootBinding
{
    PackageRootBinding(
        PackageRootRealization root,
        RealizedMemberCoordinate.Package coordinate,
        PackageProducerIdentity? sourceProducer,
        PackageContentGenerationIdentity contentGenerationIdentity,
        PackageRootSelectionIdentity selectionIdentity,
        string? compileTargetFramework,
        bool usesCompatibleImplementationSelection,
        bool allowsCompatibleTargetSelection)
    {
        Root = root;
        Coordinate = coordinate;
        SourceProducer = sourceProducer;
        ContentGenerationIdentity = contentGenerationIdentity;
        SelectionIdentity = selectionIdentity;
        CompileTargetFramework =
            compileTargetFramework
            ?? root.AssetSelection.TargetFramework;
        ImplementationSelectionTargetFramework =
            root.AssetSelection.ImplementationTargetFramework
            ?? root.RequestedTargetFramework
            ?? root.AssetSelection.TargetFramework;
        HasSelectedImplementationUniverse =
            root.AssetSelection.ImplementationTargetFramework is not null;
        UsesCompatibleImplementationSelection =
            usesCompatibleImplementationSelection;
        AllowsCompatibleTargetSelection =
            allowsCompatibleTargetSelection;
    }

    public PackageRootRealization Root { get; }

    public RealizedMemberCoordinate.Package Coordinate { get; }

    internal PackageProducerIdentity? SourceProducer { get; }

    public PackageContentGenerationIdentity ContentGenerationIdentity { get; }

    public PackageRootSelectionIdentity SelectionIdentity { get; }

    internal string? CompileTargetFramework { get; }

    internal string? ImplementationSelectionTargetFramework { get; }

    internal bool HasSelectedImplementationUniverse { get; }

    internal bool UsesCompatibleImplementationSelection { get; }

    internal bool AllowsCompatibleTargetSelection { get; }

    /// <summary>
    /// Issues the exact, resource-free request that repeats this logical Root
    /// under another host's acquisition capabilities.
    /// </summary>
    /// <remarks>
    /// The issued value preserves the realized producer-pinned coordinate and
    /// the normalized compile and implementation selection targets, whether
    /// one implementation universe was selected, compatible target-selection
    /// authorization, and observed compatible implementation outcome
    /// separately. It carries no content, generation identity,
    /// selection identity, workspace identity, lease, opener, or path
    /// authority. Gated by
    /// <c>SparsePackageAssemblyProjectionTests.ReacquisitionRequest_IsExactResourceFreeAndSeparatesTargets</c>.
    /// </remarks>
    public PackageRootReacquisitionRequest CreateReacquisitionRequest() =>
        new(PackageArtifactRootRequest.From(this));

    /// <summary>
    /// Binds a payload acquired through the typed source-client path.
    /// </summary>
    public static PackageRootBinding CreateFromSource(
        AcquiredPackageSourcePayload payload,
        string? selectionTargetFramework = null,
        string? runtimeIdentifier = null,
        string? displayPackageId = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (runtimeIdentifier is not null
            && !RealizedMemberCoordinate.IsCanonicalRuntimeIdentifier(
                runtimeIdentifier))
        {
            throw new ArgumentException(
                "A package Root runtime identifier must be a canonical lowercase moniker.",
                nameof(runtimeIdentifier));
        }
        string? acquisitionFramework =
            SourceAcquisitionFramework(selectionTargetFramework);
        if (runtimeIdentifier is not null
            && acquisitionFramework is null)
        {
            throw new ArgumentException(
                "A package Root runtime identifier requires a canonical acquisition framework.",
                nameof(selectionTargetFramework));
        }

        return Create(
            payload,
            payload.Coordinate.PackageId,
            displayPackageId ?? payload.Coordinate.PackageId,
            payload.Coordinate.Version,
            payload.Content,
            payload.ProducerKey,
            SourceCoordinateProducer(payload),
            payload.Producer,
            payload.LegacyProducerKey,
            acquisitionFramework,
            selectionTargetFramework,
            runtimeIdentifier);
    }

    /// <summary>
    /// Binds a typed-source payload to the exact compile selection receipt
    /// already issued for its retained content generation.
    /// </summary>
    internal static PackageRootBinding CreateFromSourceSelection(
        AcquiredPackageSourcePayload payload,
        PackageCompileAssetSelectionReceipt receipt,
        string? displayPackageId = null)
    {
        string? acquisitionFramework =
            ValidateSourceSelection(payload, receipt);
        string? selectionTargetFramework =
            ReceiptSelectionTargetFramework(receipt);
        string? compileTargetFramework =
            ReceiptCompileTargetFramework(receipt);
        bool usesCompatibleImplementationSelection =
            ReceiptUsesCompatibleImplementationSelection(receipt);
        return Create(
            payload,
            payload.Coordinate.PackageId,
            displayPackageId ?? payload.Coordinate.PackageId,
            payload.Coordinate.Version,
            payload.Content,
            payload.ProducerKey,
            SourceCoordinateProducer(payload),
            payload.Producer,
            payload.LegacyProducerKey,
            acquisitionFramework,
            selectionTargetFramework,
            receipt.RequestedRuntimeIdentifier,
            assetSelection: receipt.Selection,
            compileTargetFramework: compileTargetFramework,
            usesCompatibleImplementationSelection:
                usesCompatibleImplementationSelection,
            allowsCompatibleTargetSelection:
                receipt.Policy
                    == PackageCompileAssetSelectionPolicy.ExplicitTarget);
    }

    /// <summary>
    /// Attempts to bind a typed-source payload to its exact compile selection
    /// when the complete package Root coordinate is representable.
    /// </summary>
    internal static bool TryCreateFromSourceSelection(
        AcquiredPackageSourcePayload payload,
        PackageCompileAssetSelectionReceipt receipt,
        [NotNullWhen(true)] out PackageRootBinding? binding,
        string? displayPackageId = null)
    {
        if (!TryValidateSourceSelection(
                payload,
                receipt,
                out string? acquisitionFramework))
        {
            binding = null;
            return false;
        }
        string? selectionTargetFramework =
            ReceiptSelectionTargetFramework(receipt);
        string? compileTargetFramework =
            ReceiptCompileTargetFramework(receipt);
        return TryCreate(
            payload,
            payload.Coordinate.PackageId,
            displayPackageId ?? payload.Coordinate.PackageId,
            payload.Coordinate.Version,
            payload.Content,
            payload.ProducerKey,
            SourceCoordinateProducer(payload),
            payload.Producer,
            payload.LegacyProducerKey,
            acquisitionFramework,
            selectionTargetFramework,
            receipt.RequestedRuntimeIdentifier,
            receipt.Selection,
            compileTargetFramework: compileTargetFramework,
            usesCompatibleImplementationSelection:
                ReceiptUsesCompatibleImplementationSelection(receipt),
            allowsCompatibleTargetSelection:
                receipt.Policy
                    == PackageCompileAssetSelectionPolicy.ExplicitTarget,
            out binding,
            out _);
    }

    static bool ReceiptUsesCompatibleImplementationSelection(
        PackageCompileAssetSelectionReceipt receipt) =>
        receipt.Policy == PackageCompileAssetSelectionPolicy.ExplicitTarget
        && receipt.Selection.UsesCompatibleImplementationSelection;

    static string? ReceiptSelectionTargetFramework(
        PackageCompileAssetSelectionReceipt receipt) =>
        receipt.Policy != PackageCompileAssetSelectionPolicy.ExactTarget
            ? receipt.Selection.ImplementationTargetFramework
                ?? receipt.Selection.TargetFramework
                ?? receipt.RequestedTargetFramework
            : receipt.Selection.TargetFramework
                ?? receipt.RequestedTargetFramework;

    static string? ReceiptCompileTargetFramework(
        PackageCompileAssetSelectionReceipt receipt) =>
        receipt.RequestedTargetFramework
        ?? receipt.Selection.TargetFramework;

    private static string? ValidateSourceSelection(
        AcquiredPackageSourcePayload payload,
        PackageCompileAssetSelectionReceipt receipt)
    {
        if (!TryValidateSourceSelection(
                payload,
                receipt,
                out string? acquisitionFramework))
        {
            throw new ArgumentException(
                "A package Root runtime identifier requires a canonical acquisition framework.",
                nameof(receipt));
        }
        return acquisitionFramework;
    }

    private static bool TryValidateSourceSelection(
        AcquiredPackageSourcePayload payload,
        PackageCompileAssetSelectionReceipt receipt,
        out string? acquisitionFramework)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(receipt);
        if (!payload.Coordinate.PackageId.Equals(
                receipt.PackageId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The compile selection receipt must identify the acquired package.",
                nameof(receipt));
        }
        if (!ReferenceEquals(
                payload.Content.GenerationIdentity,
                receipt.Generation))
        {
            throw new ArgumentException(
                "The compile selection receipt must describe the acquired content generation.",
                nameof(receipt));
        }
        if (receipt.RequestedRuntimeIdentifier is not null
            && !RealizedMemberCoordinate.IsCanonicalRuntimeIdentifier(
                receipt.RequestedRuntimeIdentifier))
        {
            throw new ArgumentException(
                "A package Root runtime identifier must be a canonical lowercase moniker.",
                nameof(receipt));
        }
        acquisitionFramework =
            SourceAcquisitionFramework(receipt.RequestedTargetFramework);
        return receipt.RequestedRuntimeIdentifier is null
            || acquisitionFramework is not null;
    }

    /// <summary>
    /// Binds a source payload for a requested framework, selecting a compatible
    /// implementation universe only when exact compile selection has no match.
    /// </summary>
    public static PackageRootBinding CreateFromSourceWithCompatibleSelection(
        AcquiredPackageSourcePayload payload,
        string requestedTargetFramework,
        string? displayPackageId = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedTargetFramework);
        PackageRootBinding exact = CreateFromSource(
            payload,
            requestedTargetFramework,
            displayPackageId: displayPackageId);
        if (exact.Root.AssetSelection.Status
                is not PackageCompileAssetSelectionStatus.NoMatchingTargetFramework)
        {
            return WithCompatibleTargetSelectionAuthorization(exact);
        }
        if (TrySelectCompatibleCompileAssets(
                payload.Content,
                payload.Coordinate.PackageId,
                requestedTargetFramework,
                runtimeIdentifier: null,
                exact.Root.AssetSelection,
                out PackageCompileAssetSelection? compatibleSelection)
                is false)
        {
            compatibleSelection = exact.Root.AssetSelection;
        }

        string? acquisitionFramework =
            SourceAcquisitionFramework(requestedTargetFramework);
        if (acquisitionFramework is null)
            return WithCompatibleTargetSelectionAuthorization(exact);

        return Create(
            payload,
            payload.Coordinate.PackageId,
            displayPackageId ?? payload.Coordinate.PackageId,
            payload.Coordinate.Version,
            payload.Content,
            payload.ProducerKey,
            SourceCoordinateProducer(payload),
            payload.Producer,
            payload.LegacyProducerKey,
            acquisitionFramework,
            SelectionTargetFramework(
                compatibleSelection,
                requestedTargetFramework),
            runtimeIdentifier: null,
            assetSelection: compatibleSelection,
            compileTargetFramework: requestedTargetFramework,
            usesCompatibleImplementationSelection:
                compatibleSelection.UsesCompatibleImplementationSelection,
            allowsCompatibleTargetSelection: true);
    }

    internal static PackageRootBinding CreateFromReacquiredSource(
        AcquiredPackageSourcePayload payload,
        PackageRootReacquisitionRequest request)
    {
        RealizedMemberCoordinate.Package coordinate = request.Coordinate;
        ReacquiredSelection selection =
            ReacquiredCompatibleSelection(payload.Content, coordinate.PackageId, request);
        return Create(
            payload,
            coordinate.PackageId,
            coordinate.PackageId,
            coordinate.Version,
            payload.Content,
            payload.ProducerKey,
            coordinate.Producer,
            payload.Producer,
            payload.LegacyProducerKey,
            coordinate.Framework,
            selection.TargetFramework,
            coordinate.RuntimeIdentifier,
            selection.Value,
            selection.CompileTargetFramework,
            selection.UsesCompatibleImplementationSelection,
            request.AllowsCompatibleTargetSelection);
    }

    internal static PackageRootBinding CreateFromReacquiredResolved(
        AcquiredPackagePayload payload,
        PackageRootReacquisitionRequest request,
        PackageProducerIdentity? producer = null)
    {
        RealizedMemberCoordinate.Package coordinate = request.Coordinate;
        ReacquiredSelection selection =
            ReacquiredCompatibleSelection(payload.Content, coordinate.PackageId, request);
        return Create(
            payload,
            coordinate.PackageId,
            coordinate.PackageId,
            coordinate.Version,
            payload.Content,
            payload.ProducerKey,
            coordinate.Producer,
            producer,
            sourceProducerAlias: null,
            coordinate.Framework,
            selection.TargetFramework,
            coordinate.RuntimeIdentifier,
            selection.Value,
            selection.CompileTargetFramework,
            selection.UsesCompatibleImplementationSelection,
            request.AllowsCompatibleTargetSelection);
    }

    /// <summary>
    /// Binds a payload acquired through the resolved multi-source path.
    /// </summary>
    public static PackageRootBinding CreateFromResolved(
        AcquiredPackagePayload payload,
        string? selectionTargetFramework = null,
        string? displayPackageId = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Create(
            payload,
            payload.Coordinate.PackageId,
            displayPackageId ?? payload.Coordinate.PackageId,
            payload.Coordinate.Version,
            payload.Content,
            payload.ProducerKey,
            payload.ProducerKey,
            sourceProducer: null,
            sourceProducerAlias: null,
            payload.Coordinate.Framework,
            selectionTargetFramework ?? payload.Coordinate.Framework,
            payload.Coordinate.RuntimeIdentifier);
    }

    internal static PackageRootBinding CreateFromResolved(
        AcquiredPackagePayload payload,
        string? selectionTargetFramework,
        string? displayPackageId,
        string coordinateProducer,
        PackageProducerIdentity producer)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinateProducer);
        ArgumentNullException.ThrowIfNull(producer);
        return Create(
            payload,
            payload.Coordinate.PackageId,
            displayPackageId ?? payload.Coordinate.PackageId,
            payload.Coordinate.Version,
            payload.Content,
            payload.ProducerKey,
            coordinateProducer,
            producer,
            sourceProducerAlias: null,
            payload.Coordinate.Framework,
            selectionTargetFramework ?? payload.Coordinate.Framework,
            payload.Coordinate.RuntimeIdentifier);
    }

    /// <summary>
    /// Binds a resolved payload for a requested framework, selecting a
    /// compatible implementation universe only when exact compile selection
    /// has no match.
    /// </summary>
    public static PackageRootBinding CreateFromResolvedWithCompatibleSelection(
        AcquiredPackagePayload payload,
        string requestedTargetFramework,
        string? displayPackageId = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedTargetFramework);
        PackageRootBinding exact = CreateFromResolved(
            payload,
            requestedTargetFramework,
            displayPackageId);
        if (exact.Root.AssetSelection.Status
                is not PackageCompileAssetSelectionStatus.NoMatchingTargetFramework)
        {
            return WithCompatibleTargetSelectionAuthorization(exact);
        }
        if (TrySelectCompatibleCompileAssets(
                payload.Content,
                payload.Coordinate.PackageId,
                requestedTargetFramework,
                payload.Coordinate.RuntimeIdentifier,
                exact.Root.AssetSelection,
                out PackageCompileAssetSelection? compatibleSelection)
                is false)
        {
            compatibleSelection = exact.Root.AssetSelection;
        }

        return Create(
            payload,
            payload.Coordinate.PackageId,
            displayPackageId ?? payload.Coordinate.PackageId,
            payload.Coordinate.Version,
            payload.Content,
            payload.ProducerKey,
            payload.ProducerKey,
            sourceProducer: null,
            sourceProducerAlias: null,
            payload.Coordinate.Framework,
            SelectionTargetFramework(
                compatibleSelection,
                requestedTargetFramework),
            payload.Coordinate.RuntimeIdentifier,
            compatibleSelection,
            requestedTargetFramework,
            usesCompatibleImplementationSelection:
                compatibleSelection.UsesCompatibleImplementationSelection,
            allowsCompatibleTargetSelection: true);
    }

    internal static PackageRootBinding
        CreateFromResolvedWithCompatibleSelection(
            AcquiredPackagePayload payload,
            string requestedTargetFramework,
            string? displayPackageId,
            string coordinateProducer,
            PackageProducerIdentity producer)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedTargetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinateProducer);
        ArgumentNullException.ThrowIfNull(producer);
        PackageRootBinding exact = CreateFromResolved(
            payload,
            requestedTargetFramework,
            displayPackageId,
            coordinateProducer,
            producer);
        if (exact.Root.AssetSelection.Status
                is not PackageCompileAssetSelectionStatus.NoMatchingTargetFramework)
        {
            return WithCompatibleTargetSelectionAuthorization(exact);
        }
        if (!TrySelectCompatibleCompileAssets(
                payload.Content,
                payload.Coordinate.PackageId,
                requestedTargetFramework,
                payload.Coordinate.RuntimeIdentifier,
                exact.Root.AssetSelection,
                out PackageCompileAssetSelection? compatibleSelection))
        {
            compatibleSelection = exact.Root.AssetSelection;
        }

        return Create(
            payload,
            payload.Coordinate.PackageId,
            displayPackageId ?? payload.Coordinate.PackageId,
            payload.Coordinate.Version,
            payload.Content,
            payload.ProducerKey,
            coordinateProducer,
            producer,
            sourceProducerAlias: null,
            payload.Coordinate.Framework,
            SelectionTargetFramework(
                compatibleSelection,
                requestedTargetFramework),
            payload.Coordinate.RuntimeIdentifier,
            compatibleSelection,
            requestedTargetFramework,
            usesCompatibleImplementationSelection:
                compatibleSelection.UsesCompatibleImplementationSelection,
            allowsCompatibleTargetSelection: true);
    }

    readonly record struct ReacquiredSelection(
        PackageCompileAssetSelection? Value,
        string? CompileTargetFramework,
        string? TargetFramework,
        bool UsesCompatibleImplementationSelection);

    static ReacquiredSelection ReacquiredCompatibleSelection(
        IPackageContent content,
        string packageId,
        PackageRootReacquisitionRequest request)
    {
        if (request.CompileTargetFramework is not { } compileTargetFramework
            || request.SelectionTargetFramework is not { } selectionTargetFramework)
        {
            return new(
                Value: null,
                request.CompileTargetFramework,
                request.SelectionTargetFramework,
                UsesCompatibleImplementationSelection: false);
        }

        if (!request.UsesCompatibleImplementationSelection)
        {
            if (!request.AllowsCompatibleTargetSelection)
            {
                if (!compileTargetFramework.Equals(
                        selectionTargetFramework,
                        StringComparison.Ordinal))
                {
                    PackageCompileAssetSelection ownerDefaultSelection =
                        PackageCompileAssetSelector
                            .SelectForCompatibleImplementation(
                                content,
                                packageId,
                                compileTargetFramework,
                                selectionTargetFramework,
                                request.SelectionRuntimeIdentifier);
                    return new(
                        ownerDefaultSelection,
                        ownerDefaultSelection.TargetFramework,
                        ownerDefaultSelection
                            .ImplementationTargetFramework,
                        UsesCompatibleImplementationSelection: false);
                }

                return new(
                    Value: null,
                    compileTargetFramework,
                    selectionTargetFramework,
                    UsesCompatibleImplementationSelection: false);
            }

            PackageCompileAssetSelection selection =
                PackageCompileAssetSelector.Evaluate(
                    content,
                    packageId,
                    PackageCompileAssetSelectionPolicy.ExplicitTarget,
                    compileTargetFramework,
                    request.SelectionRuntimeIdentifier).Selection;
            string targetFramework =
                SelectionTargetFramework(
                    selection,
                    selectionTargetFramework)
                ?? selectionTargetFramework;
            return new(
                selection,
                compileTargetFramework,
                targetFramework,
                UsesCompatibleImplementationSelection:
                    selection.UsesCompatibleImplementationSelection);
        }

        if (string.Equals(
                compileTargetFramework,
                selectionTargetFramework,
                StringComparison.Ordinal))
        {
            PackageCompileAssetSelection exactSelection =
                PackageCompileAssetSelector.Select(
                    content,
                    packageId,
                    compileTargetFramework,
                    request.SelectionRuntimeIdentifier);
            PackageCompileAssetSelection? compatibleSelection = null;
            bool attemptedCompatibleFallback =
                exactSelection.Status
                        is PackageCompileAssetSelectionStatus.NoMatchingTargetFramework
                && TrySelectCompatibleCompileAssets(
                    content,
                    packageId,
                    compileTargetFramework,
                    request.SelectionRuntimeIdentifier,
                    exactSelection,
                    out compatibleSelection);
            PackageCompileAssetSelection selection =
                compatibleSelection ?? exactSelection;
            return new(
                selection,
                compileTargetFramework,
                SelectionTargetFramework(
                    selection,
                    selectionTargetFramework),
                UsesCompatibleImplementationSelection:
                    attemptedCompatibleFallback
                    || selection.UsesCompatibleImplementationSelection);
        }

        PackageCompileAssetSelection reselected =
            PackageCompileAssetSelector.SelectForCompatibleImplementation(
                content,
                packageId,
                compileTargetFramework,
                selectionTargetFramework,
                request.SelectionRuntimeIdentifier);
        return new(
            reselected,
            compileTargetFramework,
            SelectionTargetFramework(
                reselected,
                selectionTargetFramework),
            reselected.UsesCompatibleImplementationSelection);
    }

    static bool TrySelectCompatibleCompileAssets(
        IPackageContent content,
        string packageId,
        string requestedTargetFramework,
        string? runtimeIdentifier,
        PackageCompileAssetSelection exactSelection,
        [NotNullWhen(true)] out PackageCompileAssetSelection? selection)
    {
        PackageAssetSelection implementationSelection =
            PackageAssetSelector.Select(content, requestedTargetFramework);
        if (implementationSelection is PackageAssetSelection.NoMatch)
        {
            selection = null;
            return false;
        }

        selection = implementationSelection switch
        {
            PackageAssetSelection.Selected compatible =>
                PackageCompileAssetSelector.SelectForCompatibleImplementation(
                    content,
                    packageId,
                    requestedTargetFramework,
                    compatible.Universe.TargetFramework,
                    runtimeIdentifier),
            PackageAssetSelection.Ambiguous ambiguous =>
                exactSelection with
                {
                    Status =
                        PackageCompileAssetSelectionStatus.InvalidImplementationAssets,
                    Message = ambiguous.Message,
                    UsesCompatibleImplementationSelection = true,
                },
            PackageAssetSelection.Invalid invalid =>
                exactSelection with
                {
                    Status =
                        PackageCompileAssetSelectionStatus.InvalidImplementationAssets,
                    Message = invalid.Message,
                    UsesCompatibleImplementationSelection = true,
                },
            _ => throw new UnreachableException(
                "Package asset selection returned an unsupported outcome."),
        };
        return true;
    }

    static string? SelectionTargetFramework(
        PackageCompileAssetSelection selection,
        string? fallback) =>
        selection.ImplementationTargetFramework
        ?? selection.TargetFramework
        ?? fallback;

    static PackageRootBinding Create(
        object acquiredPayload,
        string coordinatePackageId,
        string displayPackageId,
        string packageVersion,
        IPackageContent content,
        string producerKey,
        string coordinateProducer,
        PackageProducerIdentity? sourceProducer,
        string? sourceProducerAlias,
        string? acquisitionFramework,
        string? targetFramework,
        string? runtimeIdentifier,
        PackageCompileAssetSelection? assetSelection = null,
        string? compileTargetFramework = null,
        bool usesCompatibleImplementationSelection = false,
        bool allowsCompatibleTargetSelection = false)
    {
        if (!TryCreate(
                acquiredPayload,
                coordinatePackageId,
                displayPackageId,
                packageVersion,
                content,
                producerKey,
                coordinateProducer,
                sourceProducer,
                sourceProducerAlias,
                acquisitionFramework,
                targetFramework,
                runtimeIdentifier,
                assetSelection,
                compileTargetFramework,
                usesCompatibleImplementationSelection,
                allowsCompatibleTargetSelection,
                out PackageRootBinding? binding,
                out string? problem))
        {
            throw new ArgumentException(
                $"The acquired package payload cannot form a realized coordinate: {problem}.",
                nameof(acquiredPayload));
        }

        return binding;
    }

    static bool TryCreate(
        object acquiredPayload,
        string coordinatePackageId,
        string displayPackageId,
        string packageVersion,
        IPackageContent content,
        string producerKey,
        string coordinateProducer,
        PackageProducerIdentity? sourceProducer,
        string? sourceProducerAlias,
        string? acquisitionFramework,
        string? targetFramework,
        string? runtimeIdentifier,
        PackageCompileAssetSelection? assetSelection,
        string? compileTargetFramework,
        bool usesCompatibleImplementationSelection,
        bool allowsCompatibleTargetSelection,
        [NotNullWhen(true)] out PackageRootBinding? binding,
        [NotNullWhen(false)] out string? problem)
    {
        binding = null;
        if (!displayPackageId.Equals(
                coordinatePackageId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A package Root display id must identify the acquired package.",
                nameof(displayPackageId));
        }
        if (!content.ProducerKey.Equals(producerKey, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The acquired package payload and retained content name different producers.",
                nameof(acquiredPayload));
        }
        if (sourceProducer is null
                ? !producerKey.Equals(
                    coordinateProducer,
                    StringComparison.Ordinal)
                : !MatchesSourceProducer(
                    sourceProducer,
                    producerKey,
                    coordinateProducer,
                    sourceProducerAlias))
        {
            throw new ArgumentException(
                "The acquired package payload does not establish the coordinate producer.",
                nameof(acquiredPayload));
        }

        string? effectiveFramework =
            (string.IsNullOrWhiteSpace(acquisitionFramework)
                ? null
                : acquisitionFramework)
            ?.ToLowerInvariant();
        string? effectiveRuntimeIdentifier =
            runtimeIdentifier;
        if (!RealizedMemberCoordinate.Package.TryCreate(
                coordinatePackageId,
                packageVersion,
                coordinateProducer,
                effectiveFramework,
                effectiveRuntimeIdentifier,
                out RealizedMemberCoordinate.Package? coordinate,
                out problem))
        {
            return false;
        }

        string? effectiveTargetFramework =
            string.IsNullOrWhiteSpace(targetFramework)
                ? null
                : targetFramework;
        var root = new PackageRootRealization(
            content,
            displayPackageId,
            packageVersion,
            effectiveTargetFramework,
            runtimeIdentifier,
            assetSelection);
        binding = new PackageRootBinding(
            root,
            coordinate,
            sourceProducer,
            content.GenerationIdentity,
            new PackageRootSelectionIdentity(),
            compileTargetFramework ?? effectiveTargetFramework,
            usesCompatibleImplementationSelection,
            allowsCompatibleTargetSelection
                || usesCompatibleImplementationSelection);
        return true;
    }

    static PackageRootBinding WithCompatibleTargetSelectionAuthorization(
        PackageRootBinding binding)
    {
        PackageCompileAssetSelection selection =
            binding.Root.AssetSelection;
        string? selectionTargetFramework =
            SelectionTargetFramework(
                selection,
                binding.Root.RequestedTargetFramework);
        bool usesCompatibleImplementationSelection =
            selection.UsesCompatibleImplementationSelection;
        if (binding.AllowsCompatibleTargetSelection
            && binding.UsesCompatibleImplementationSelection
                == usesCompatibleImplementationSelection
            && string.Equals(
                binding.Root.RequestedTargetFramework,
                selectionTargetFramework,
                StringComparison.Ordinal))
        {
            return binding;
        }

        var root = new PackageRootRealization(
            binding.Root.Content,
            binding.Root.PackageId,
            binding.Root.PackageVersion,
            selectionTargetFramework,
            binding.Root.RequestedRuntimeIdentifier,
            selection);
        return new PackageRootBinding(
            root,
            binding.Coordinate,
            binding.SourceProducer,
            binding.ContentGenerationIdentity,
            binding.SelectionIdentity,
            binding.CompileTargetFramework,
            usesCompatibleImplementationSelection,
            allowsCompatibleTargetSelection: true);
    }

    /// <summary>
    /// The same Root with another asset demand. A surface-only Root realizes
    /// no implementation role, so realization never opens its implementation
    /// assets; broad surface consumers such as <c>find</c> use it.
    /// </summary>
    public PackageRootBinding WithAssetDemand(PackageAssetDemand demand) =>
        WithAssetDemand(demand, implementationNames: null);

    /// <summary>
    /// The same Root with another asset demand and, for
    /// <see cref="PackageAssetDemand.SurfaceAndImplementation"/>, the
    /// implementation assemblies it names: realization prepares an
    /// implementation role, and a role correspondence, only for the named
    /// assets (docs/design/package-read-demand.md#named-implementation-and-aligned-blocks).
    /// A name that selects no implementation asset is a
    /// <see cref="PackageImplementationNameException"/>.
    /// </summary>
    public PackageRootBinding WithAssetDemand(
        PackageAssetDemand demand,
        PackageImplementationNames? implementationNames)
    {
        if (!Enum.IsDefined(demand))
            throw new ArgumentOutOfRangeException(nameof(demand));
        if (implementationNames is not null
            && demand != PackageAssetDemand.SurfaceAndImplementation)
        {
            throw new ArgumentException(
                "Named implementation assemblies require the SurfaceAndImplementation demand.",
                nameof(implementationNames));
        }
        if (Root.AssetDemand == demand
            && SameNames(Root.ImplementationNames, implementationNames))
        {
            return this;
        }
        // A surface-only Root's content was read for the surface alone, and a
        // named Root's for its named implementation alone, so neither is
        // upgraded in place (docs/design/package-read-demand.md).
        if (Root.AssetDemand == PackageAssetDemand.Surface
            && demand != PackageAssetDemand.Surface)
        {
            throw new InvalidOperationException(
                "A surface-only package Root cannot be upgraded in place; realize it with the demand it needs.");
        }
        if (demand == PackageAssetDemand.SurfaceAndImplementation
            && Root.ImplementationNames is { } realized
            && (implementationNames is null
                || implementationNames.Unmatched(realized.Names).Count > 0))
        {
            throw new InvalidOperationException(
                "A package Root realized with named implementation assemblies cannot name others; realize it with the names it needs.");
        }
        var root = new PackageRootRealization(
            Root.Content,
            Root.PackageId,
            Root.PackageVersion,
            Root.RequestedTargetFramework,
            Root.RequestedRuntimeIdentifier,
            Root.AssetSelection,
            demand,
            implementationNames);
        return new PackageRootBinding(
            root,
            Coordinate,
            SourceProducer,
            ContentGenerationIdentity,
            SelectionIdentity,
            CompileTargetFramework,
            UsesCompatibleImplementationSelection,
            AllowsCompatibleTargetSelection);
    }

    static bool SameNames(
        PackageImplementationNames? left,
        PackageImplementationNames? right) =>
        left is null
            ? right is null
            : right is not null && left.SetEquals(right);

    internal bool ReferencesRetainedContent() =>
        ReferenceEquals(
            ContentGenerationIdentity,
            Root.Content.GenerationIdentity)
        && Root.ProducerKey.Equals(
            Root.Content.ProducerKey,
            StringComparison.Ordinal)
        && (SourceProducer is null
            ? Coordinate.Producer.Equals(
                Root.Content.ProducerKey,
                StringComparison.Ordinal)
            : true);

    internal static bool MatchesSourceProducer(
        AcquiredPackageSourcePayload payload,
        string coordinateProducer)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinateProducer);
        return payload.Producer is { } producer
            ? MatchesSourceProducer(
                producer,
                payload.ProducerKey,
                coordinateProducer,
                payload.LegacyProducerKey)
            : payload.ProducerKey.Equals(
                coordinateProducer,
                StringComparison.Ordinal);
    }

    static bool MatchesSourceProducer(
        PackageProducerIdentity producer,
        string contentProducerKey,
        string coordinateProducer,
        string? legacyProducerKey = null) =>
        producer.PortableKey.Equals(
            coordinateProducer,
            StringComparison.Ordinal)
        || producer.Key.Equals(
            coordinateProducer,
            StringComparison.Ordinal)
        || contentProducerKey.Equals(
            coordinateProducer,
            StringComparison.Ordinal)
        || legacyProducerKey?.Equals(
            coordinateProducer,
            StringComparison.Ordinal) is true;

    static string SourceCoordinateProducer(
        AcquiredPackageSourcePayload payload) =>
        payload.Producer?.PortableKey
        ?? payload.ProducerKey;

    internal static string? SourceAcquisitionFramework(string? targetFramework) =>
        PackageCoordinateResolver.IsAcquisitionTargetText(targetFramework)
            ? targetFramework!.ToLowerInvariant()
            : null;
}

/// <summary>
/// One exact, already-acquired package Root and its compile-asset selection outcome.
/// </summary>
public sealed class PackageRootRealization
{
    readonly IPackageContent _content;

    public PackageRootRealization(
        IPackageContent content,
        string packageId,
        string packageVersion,
        string? targetFramework = null,
        string? runtimeIdentifier = null)
        : this(
            content,
            packageId,
            packageVersion,
            targetFramework,
            runtimeIdentifier,
            assetSelection: null)
    {
    }

    internal PackageRootRealization(
        IPackageContent content,
        string packageId,
        string packageVersion,
        string? targetFramework,
        string? runtimeIdentifier,
        PackageCompileAssetSelection? assetSelection,
        PackageAssetDemand assetDemand =
            PackageAssetDemand.SurfaceAndImplementation,
        PackageImplementationNames? implementationNames = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        if (implementationNames is not null
            && assetDemand != PackageAssetDemand.SurfaceAndImplementation)
        {
            throw new ArgumentException(
                "Named implementation assemblies require the SurfaceAndImplementation demand.",
                nameof(implementationNames));
        }

        _content = content;
        AssetDemand = assetDemand;
        ImplementationNames = implementationNames;
        PackageId = packageId;
        PackageVersion = packageVersion;
        RequestedTargetFramework = targetFramework;
        RequestedRuntimeIdentifier = runtimeIdentifier;
        Identity = new PackageRootIdentity(
            packageId,
            packageVersion,
            targetFramework,
            runtimeIdentifier);
        AssetSelection = Freeze(
            assetSelection
            ?? PackageCompileAssetSelector.Select(
                content,
                packageId,
                targetFramework,
                runtimeIdentifier));
        HasUnselectedTargetFrameworkAssemblyCandidates =
            HasUnselectedTargetFrameworkAssemblyCandidatesCore(
                content,
                AssetSelection);
        if (implementationNames is not null)
        {
            // A name that selects no implementation asset is a visible
            // realization failure, never an empty implementation role.
            IReadOnlyList<string> unmatched = implementationNames.Unmatched(
                AssetSelection.IsSelected
                    ? AssetSelection.ImplementationAssets.Select(
                        static asset => asset.Path)
                    : []);
            if (unmatched.Count > 0)
                throw new PackageImplementationNameException(unmatched);
        }
    }

    public string PackageId { get; }

    public string PackageVersion { get; }

    public PackageRootIdentity Identity { get; }

    public string? RequestedTargetFramework { get; }

    public string? RequestedRuntimeIdentifier { get; }

    public string ProducerKey => _content.ProducerKey;

    public bool FromCache => _content.FromCache;

    public PackageCompileAssetSelection AssetSelection { get; }

    /// <summary>
    /// Which roles this Root realizes: the surface only, or the surface and
    /// its implementation universe.
    /// </summary>
    public PackageAssetDemand AssetDemand { get; }

    /// <summary>
    /// The implementation assemblies this Root names, or
    /// <see langword="null"/> for every selected implementation asset. Only
    /// named assets join the implementation role.
    /// </summary>
    public PackageImplementationNames? ImplementationNames { get; }

    /// <summary>
    /// The selected implementation assets this Root realizes: none for a
    /// surface-only Root, the named ones for a named Root, else all.
    /// </summary>
    public IReadOnlyList<PackageCompileAsset> RealizedImplementationAssets =>
        !AssetSelection.IsSelected
        || AssetDemand == PackageAssetDemand.Surface
            ? []
            : ImplementationNames is { } names
                ? [
                    .. AssetSelection.ImplementationAssets.Where(
                        asset => names.MatchesPath(asset.Path)),
                ]
                : AssetSelection.ImplementationAssets;

    /// <summary>
    /// Whether the package contains a DLL candidate for the selected target
    /// framework outside the implementation universe.
    /// </summary>
    public bool HasUnselectedTargetFrameworkAssemblyCandidates { get; }

    internal IPackageContent Content => _content;

    /// <summary>
    /// Uses the already-admitted package content without transferring its
    /// lifetime beyond the caller's operation.
    /// </summary>
    public TResult UseContent<TResult>(
        Func<IPackageContent, TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation(_content);
    }

    public bool ReferencesContent(IPackageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return ReferenceEquals(_content, content);
    }

    static PackageCompileAssetSelection Freeze(
        PackageCompileAssetSelection selection) =>
        new(
            selection.Status,
            selection.TargetFramework,
            Freeze(selection.AvailableTargetFrameworks),
            Freeze(selection.Assets),
            selection.DefaultAsset,
            Freeze(selection.CandidateAssets),
            Freeze(selection.ImplementationAssets),
            Freeze(selection.ExplicitEmptyTargetFrameworks),
            Freeze(selection.AvailableSlices.Select(slice =>
                new PackageCompileAssetSlice(
                    slice.TargetFramework,
                    Freeze(slice.CandidateAssets),
                    slice.HasExplicitEmptyReferenceGroup)).ToArray()),
            selection.Message)
        {
            ImplementationTargetFramework =
                selection.ImplementationTargetFramework,
            UsesCompatibleImplementationSelection =
                selection.UsesCompatibleImplementationSelection,
        };

    static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T> values) =>
        Array.AsReadOnly([.. values]);

    static bool HasUnselectedTargetFrameworkAssemblyCandidatesCore(
        IPackageContent content,
        PackageCompileAssetSelection selection)
    {
        if (selection.TargetFramework is not { } targetFramework)
            return false;

        HashSet<string> selectedPaths =
            selection.ImplementationAssets
                .Select(static asset => asset.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return content.EnumerateEntries().Any(
            entry =>
                entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && TfmResolver.ExtractTfmFromPath(entry)
                    ?.Equals(
                        targetFramework,
                        StringComparison.OrdinalIgnoreCase)
                    is true
                && !selectedPaths.Contains(entry));
    }
}

/// <summary>Resource admission policy for acquired-package role realization.</summary>
public sealed record PackageAssemblyContextRealizationOptions
{
    /// <summary>The largest participant count admitted into either role.</summary>
    public int MaxAssembliesPerRole { get; init; } = int.MaxValue;

    /// <summary>
    /// The retained-byte budget for one package realization.
    /// </summary>
    /// <remarks>
    /// Artifact-backed realization divides this budget between the artifact
    /// generation and the resulting role groups. Distinct surface and
    /// implementation groups divide the role-group share again.
    /// </remarks>
    public long MaxAggregateRetainedImageBytes { get; init; } =
        AssemblyContextGroupOptions.DefaultMaxRetainedImageBytes;

    /// <summary>The largest selected assembly entry the content opener may expand.</summary>
    public long MaxAssemblyEntryBytes { get; init; } =
        AssemblyContextGroupOptions.DefaultMaxRetainedImageBytes;

    /// <summary>
    /// Requires every package content to expose declared entry lengths so all
    /// selected assets can be rejected over budget before identity decoding.
    /// </summary>
    public bool RequireDeclaredEntryLengths { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxAssembliesPerRole);
        ArgumentOutOfRangeException.ThrowIfNegative(
            MaxAggregateRetainedImageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxAssemblyEntryBytes);
    }
}

/// <summary>
/// One selected package asset and the exact product participant realized from
/// it.
/// </summary>
public sealed class PackageAssemblyRoleParticipant
{
    internal PackageAssemblyRoleParticipant(
        PackageRootRealization package,
        PackageCompileAsset asset,
        AssemblyContextParticipant participant)
        : this(package.Identity, asset, participant)
    {
    }

    internal PackageAssemblyRoleParticipant(
        PackageRootIdentity package,
        PackageCompileAsset asset,
        AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(participant);
        Package = package;
        Asset = asset;
        Participant = participant;
    }

    public PackageRootIdentity Package { get; }

    public PackageCompileAsset Asset { get; }

    public AssemblyContextParticipant Participant { get; }
}

/// <summary>
/// Reports that selected package assembly assets cannot form an exact,
/// unambiguous assembly-role correspondence.
/// </summary>
public sealed class PackageAssemblyRoleCorrespondenceException :
    InvalidOperationException
{
    internal PackageAssemblyRoleCorrespondenceException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Product-realized package surface and implementation roles, including exact
/// package-asset-to-participant associations.
/// </summary>
public sealed class PackageAssemblyContextRealization : IDisposable
{
    readonly PackageAssemblyContextRoles? _roles;

    internal PackageAssemblyContextRealization(
        PackageAssemblyContextRoles? roles,
        ImmutableArray<PackageAssemblyRoleParticipant> surfaceParticipants,
        ImmutableArray<PackageAssemblyRoleParticipant> implementationParticipants)
    {
        _roles = roles;
        SurfaceParticipants = surfaceParticipants;
        ImplementationParticipants = implementationParticipants;
    }

    public bool HasAssemblyContexts => _roles is not null;

    public AssemblyContextGroup SurfaceGroup =>
        _roles?.SurfaceGroup
        ?? throw new InvalidOperationException(
            "The package realization has no selected compile assemblies.");

    public AssemblyContextGroup? ImplementationGroup =>
        _roles?.ImplementationGroup;

    public bool SharesGroup => _roles?.SharesGroup ?? false;

    public ImmutableArray<PackageAssemblyRoleParticipant> SurfaceParticipants
    {
        get;
    }

    public ImmutableArray<PackageAssemblyRoleParticipant> ImplementationParticipants
    {
        get;
    }

    public PackageAssemblyRoleParticipant? ImplementationParticipant(
        PackageAssemblyRoleParticipant surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        PackageAssemblyRoleParticipant selected =
            SurfaceParticipants.FirstOrDefault(candidate =>
                ReferenceEquals(candidate, surface))
            ?? throw new ArgumentException(
                "The participant does not belong to the surface package role.",
                nameof(surface));
        AssemblyContextParticipant? implementation = _roles!
            .ImplementationParticipant(selected.Participant);
        return implementation is null
            ? null
            : ImplementationParticipants.First(candidate =>
                ReferenceEquals(candidate.Participant, implementation));
    }

    public void Dispose() => _roles?.Dispose();
}

public sealed partial class InspectionWorkspace
{
    /// <summary>
    /// Realizes already-acquired package contents into reference-preferred
    /// surface and body-bearing implementation roles.
    /// </summary>
    public PackageAssemblyContextRealization RealizePackageAssemblyContextRoles(
        IEnumerable<PackageRootRealization> packages,
        PackageAssemblyContextRealizationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        PackageRoleRealizationPreparation preparation =
            PreparePackageRoleRealization(
                packages,
                options,
                cancellationToken);
        if (preparation.SurfaceAssets.IsEmpty)
        {
            return new PackageAssemblyContextRealization(
                roles: null,
                [],
                []);
        }

        ImmutableArray<RoleAssembly> surfaceRole =
            CreateRole(
                preparation.SurfaceAssets,
                preparation.GroupBudget,
                preparation.Options,
                cancellationToken);
        ImmutableArray<RoleAssembly> implementationRole = preparation.Shared
            ? surfaceRole
            : CreateRole(
                preparation.ImplementationAssets,
                preparation.GroupBudget,
                preparation.Options,
                cancellationToken);
        return CreatePackageAssemblyContextRealization(
            preparation,
            surfaceRole,
            implementationRole,
            cancellationToken);
    }

    internal static PackageRoleRealizationPreparation
        PreparePackageRoleRealization(
        IEnumerable<PackageRootRealization> packages,
        PackageAssemblyContextRealizationOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packages);
        options ??= new PackageAssemblyContextRealizationOptions();
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        ImmutableArray<PackageRootRealization> packageRoots =
            [.. packages];
        if (packageRoots.IsEmpty)
        {
            throw new InvalidOperationException(
                "Package realization requires at least one package.");
        }
        if (packageRoots.Any(static package => package is null))
        {
            throw new ArgumentException(
                "Package realization cannot contain a null package.",
                nameof(packages));
        }

        ImmutableArray<RoleAsset> surfaceAssets =
        [
            .. packageRoots.SelectMany(
                (package, packageIndex) =>
                    package.AssetSelection.IsSelected
                        ? package.AssetSelection.Assets.Select(asset =>
                            new RoleAsset(
                                packageIndex,
                                package,
                                asset))
                        : []),
        ];
        ImmutableArray<RoleAsset> implementationAssets =
        [
            .. packageRoots.SelectMany(
                (package, packageIndex) =>
                    package.RealizedImplementationAssets.Select(
                        asset => new RoleAsset(
                            packageIndex,
                            package,
                            asset))),
        ];
        ValidateAssetCount(surfaceAssets.Length, options);
        ValidateAssetCount(implementationAssets.Length, options);
        bool shared = SameAssets(surfaceAssets, implementationAssets);
        bool hasSeparateImplementation =
            !shared && !implementationAssets.IsEmpty;
        long groupBudget = hasSeparateImplementation
            ? options.MaxAggregateRetainedImageBytes / 2
            : options.MaxAggregateRetainedImageBytes;

        ValidateAssets(surfaceAssets, groupBudget, options);
        if (hasSeparateImplementation)
            ValidateAssets(implementationAssets, groupBudget, options);

        return new PackageRoleRealizationPreparation(
            surfaceAssets,
            implementationAssets,
            shared,
            groupBudget,
            options);
    }

    PackageAssemblyContextRealization CreatePackageAssemblyContextRealization(
        PackageRoleRealizationPreparation preparation,
        ImmutableArray<RoleAssembly> surfaceRole,
        ImmutableArray<RoleAssembly> implementationRole,
        CancellationToken cancellationToken,
        bool provisional = false)
    {
        ImmutableArray<PackageAssemblyRoleCorrespondence> correspondences =
            Correspondences(surfaceRole, implementationRole);
        cancellationToken.ThrowIfCancellationRequested();
        var roleOptions = new AssemblyContextGroupOptions
        {
            MaxRetainedImageBytes = preparation.GroupBudget,
        };

        var roles = new PackageAssemblyContextRoles(
            this,
            surfaceRole.Select(entry => entry.Assembly),
            implementationRole.IsEmpty
                ? null
                : implementationRole.Select(entry => entry.Assembly),
            correspondences,
            shareImplementationGroup: preparation.Shared,
            surfaceOptions: roleOptions,
            implementationOptions: roleOptions,
            createRole: provisional
                ? static (_, participants, options) => new AssemblyContextGroup(
                    participants,
                    options,
                    static _ => { },
                    captureReleaseFailuresByDefault: true)
                : null);
        try
        {
            return new PackageAssemblyContextRealization(
                roles,
                Participants(surfaceRole, roles.SurfaceParticipants),
                Participants(
                    implementationRole,
                    roles.ImplementationParticipants));
        }
        catch (Exception creationFailure)
        {
            try
            {
                roles.Dispose();
            }
            catch (Exception disposalFailure)
            {
                throw new AggregateException(
                    creationFailure,
                    disposalFailure);
            }

            throw;
        }
    }

    static ImmutableArray<RoleAssembly> CreateRole(
        ImmutableArray<RoleAsset> assets,
        long groupBudget,
        PackageAssemblyContextRealizationOptions options,
        CancellationToken cancellationToken)
    {
        var assemblies = ImmutableArray.CreateBuilder<RoleAssembly>(assets.Length);
        long entryLimit = Math.Min(
            groupBudget,
            options.MaxAssemblyEntryBytes);
        for (int index = 0; index < assets.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assemblies.Add(
                CreateRoleAssembly(
                    assets[index],
                    entryLimit,
                    index));
        }

        return assemblies.MoveToImmutable();
    }

    static RoleAssembly CreateRoleAssembly(
        RoleAsset asset,
        long entryLimit,
        int roleIndex)
    {
        Func<Stream> openRead = () => OpenEntry(asset, entryLimit);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromStreamWithFallbackIdentity(
                openRead,
                RejectionCarrierIdentity(roleIndex),
                PackageProvenance(asset),
                out bool usedFallbackIdentity);
        return new RoleAssembly(
            asset.PackageIndex,
            asset.Package,
            asset.Asset,
            assembly,
            IdentityDecoded: !usedFallbackIdentity);
    }

    static AssemblyResolutionProvenance PackageProvenance(
        RoleAsset asset) =>
        AssemblyResolutionProvenance.Package(
            asset.Package.PackageId,
            asset.Package.PackageVersion,
            asset.Asset.TargetFramework,
            rid: null);

    static AssemblyReferenceIdentity RejectionCarrierIdentity(
        int roleIndex) =>
        new(
            "RejectedPackageAsset"
                + roleIndex.ToString(CultureInfo.InvariantCulture),
            Version: null,
            Culture: null,
            PublicKeyToken: null);

    static Stream OpenEntry(
        RoleAsset asset,
        long maxExpandedBytes,
        CancellationToken cancellationToken = default) =>
        OpenPackageEntry(
            asset.Package.Content,
            asset.Asset.Path,
            maxExpandedBytes,
            cancellationToken);

    static Stream OpenPackageEntry(
        IPackageContent content,
        string path,
        long maxExpandedBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!content.TryOpenEntry(
                path,
                maxExpandedBytes,
                out Stream? stream))
        {
            throw new InvalidOperationException(
                "A selected assembly entry is unavailable in the retained package content.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new BoundedPackageEntryStream(
                stream,
                maxExpandedBytes);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    static void ValidateAssets(
        ImmutableArray<RoleAsset> assets,
        long groupBudget,
        PackageAssemblyContextRealizationOptions options)
    {
        long expandedBytes = 0;
        foreach (RoleAsset asset in assets)
        {
            if (asset.Package.Content is not IPackageContentEntryManifest manifest)
            {
                if (options.RequireDeclaredEntryLengths)
                {
                    throw new InvalidOperationException(
                        "The selected package content cannot preflight declared entry lengths.");
                }
                continue;
            }
            if (!manifest.TryGetEntryLength(asset.Asset.Path, out long length))
            {
                throw new InvalidOperationException(
                    "A selected assembly entry disappeared from "
                    + $"{asset.Package.PackageId} {asset.Package.PackageVersion}.");
            }
            if (length < 0 || length > options.MaxAssemblyEntryBytes)
            {
                throw new InvalidOperationException(
                    "A selected assembly entry exceeds the configured "
                    + "assembly-entry byte limit.");
            }
            try
            {
                expandedBytes = checked(expandedBytes + length);
            }
            catch (OverflowException ex)
            {
                throw new InvalidOperationException(
                    "The selected package workspace role exceeds the configured "
                    + "retained-image budget.",
                    ex);
            }
        }

        if (expandedBytes > groupBudget)
        {
            throw new InvalidOperationException(
                "The selected package workspace role exceeds the configured "
                + "retained-image budget before assembly identity decoding.");
        }
    }

    static void ValidateAssetCount(
        int assetCount,
        PackageAssemblyContextRealizationOptions options)
    {
        if (assetCount > options.MaxAssembliesPerRole)
        {
            throw new InvalidOperationException(
                "The selected package workspace role exceeds the configured "
                + "assembly-count limit.");
        }
    }

    static ImmutableArray<PackageAssemblyRoleCorrespondence> Correspondences(
        ImmutableArray<RoleAssembly> surfaces,
        ImmutableArray<RoleAssembly> implementations)
    {
        var implementationsByAsset = new Dictionary<RoleAsset, RoleAssembly>(
            implementations.Length,
            RoleAssetIdentityComparer.Instance);
        foreach (RoleAssembly implementation in implementations)
        {
            var roleAsset = new RoleAsset(
                implementation.PackageIndex,
                implementation.Package,
                implementation.Asset);
            if (!implementationsByAsset.TryAdd(roleAsset, implementation))
            {
                throw new InvalidOperationException(
                    "Package asset selection produced duplicate implementation role assets.");
            }
        }

        var pairs =
            ImmutableArray.CreateBuilder<PackageAssemblyRoleCorrespondence>();
        foreach (RoleAssembly surface in surfaces)
        {
            // A surface-only Root realizes no implementation role, so it has
            // nothing to pair.
            if (surface.Package.AssetDemand == PackageAssetDemand.Surface)
                continue;
            PackageCompileAsset? selectedImplementation =
                surface.Package.AssetSelection.FindImplementationAsset(
                    surface.Asset);
            if (selectedImplementation is null)
            {
                continue;
            }
            // A named Root pairs only its named implementation assets.
            if (surface.Package.ImplementationNames is { } names
                && !names.MatchesPath(selectedImplementation.Path))
            {
                continue;
            }
            var implementationAsset = new RoleAsset(
                surface.PackageIndex,
                surface.Package,
                selectedImplementation);

            if (!implementationsByAsset.TryGetValue(
                implementationAsset,
                out RoleAssembly? implementation))
            {
                throw new InvalidOperationException(
                    "A selected implementation asset is not part of the "
                    + "implementation package role.");
            }
            bool identitiesDecoded =
                surface.IdentityDecoded
                && implementation.IdentityDecoded;
            if (identitiesDecoded
                && !surface.Assembly.Identity.IsEquivalentTo(
                    implementation.Assembly.Identity))
            {
                throw new PackageAssemblyRoleCorrespondenceException(
                    "The selected reference and implementation assets have "
                    + "different assembly identities.");
            }

            pairs.Add(PackageAssemblyRoleCorrespondence.SelectedAssets(
                surface.Assembly,
                implementation.Assembly,
                identitiesDecoded));
        }

        return pairs.ToImmutable();
    }

    static ImmutableArray<PackageAssemblyRoleParticipant> Participants(
        ImmutableArray<RoleAssembly> assemblies,
        ImmutableArray<AssemblyContextParticipant> participants)
    {
        if (assemblies.Length != participants.Length)
        {
            throw new InvalidOperationException(
                "Package asset realization did not preserve participant cardinality.");
        }

        var result =
            ImmutableArray.CreateBuilder<PackageAssemblyRoleParticipant>(
                participants.Length);
        for (int index = 0; index < participants.Length; index++)
        {
            if (!ReferenceEquals(
                    assemblies[index].Assembly,
                    participants[index].Assembly))
            {
                throw new InvalidOperationException(
                    "Package asset realization did not preserve participant order.");
            }
            result.Add(new PackageAssemblyRoleParticipant(
                assemblies[index].Package,
                assemblies[index].Asset,
                participants[index]));
        }

        return result.MoveToImmutable();
    }

    static bool SameAssets(
        ImmutableArray<RoleAsset> left,
        ImmutableArray<RoleAsset> right)
    {
        if (left.Length != right.Length)
            return false;

        var remaining = new HashSet<RoleAsset>(
            left,
            RoleAssetIdentityComparer.Instance);
        if (remaining.Count != left.Length)
        {
            throw new InvalidOperationException(
                "Package asset selection produced duplicate role assets.");
        }

        return right.All(remaining.Remove) && remaining.Count == 0;
    }

    internal sealed record RoleAsset(
        int PackageIndex,
        PackageRootRealization Package,
        PackageCompileAsset Asset);

    sealed class RoleAssetIdentityComparer : IEqualityComparer<RoleAsset>
    {
        internal static RoleAssetIdentityComparer Instance { get; } = new();

        public bool Equals(RoleAsset? left, RoleAsset? right) =>
            ReferenceEquals(left, right)
            || (left is not null
                && right is not null
                && ReferenceEquals(left.Package, right.Package)
                && left.Asset.Path.Equals(
                    right.Asset.Path,
                    StringComparison.Ordinal));

        public int GetHashCode(RoleAsset asset) =>
            HashCode.Combine(
                RuntimeHelpers.GetHashCode(asset.Package),
                StringComparer.Ordinal.GetHashCode(asset.Asset.Path));
    }

    internal sealed record RoleAssembly(
        int PackageIndex,
        PackageRootRealization Package,
        PackageCompileAsset Asset,
        ResolvedAssemblyReference Assembly,
        bool IdentityDecoded);

    internal sealed record PackageRoleRealizationPreparation(
        ImmutableArray<RoleAsset> SurfaceAssets,
        ImmutableArray<RoleAsset> ImplementationAssets,
        bool Shared,
        long GroupBudget,
        PackageAssemblyContextRealizationOptions Options);

    sealed class BoundedPackageEntryStream : Stream
    {
        readonly Stream _source;
        readonly long _maxBytes;
        readonly long _start;
        long _position;

        public BoundedPackageEntryStream(
            Stream source,
            long maxBytes)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
            if (!source.CanRead)
            {
                throw new IOException(
                    "The package entry opener did not return a readable stream.");
            }
            _source = source;
            _maxBytes = maxBytes;
            if (!source.CanSeek)
                return;

            _start = source.Position;
            long length = checked(source.Length - _start);
            if (length < 0 || length > maxBytes)
                ThrowLimit();
        }

        public override bool CanRead => _source.CanRead;

        public override bool CanSeek => _source.CanSeek;

        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                if (!CanSeek)
                    throw new NotSupportedException();
                long length = checked(_source.Length - _start);
                if (length < 0 || length > _maxBytes)
                    ThrowLimit();
                return length;
            }
        }

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush()
        {
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - offset < count)
                throw new ArgumentException("The buffer range is invalid.");
            if (count == 0)
                return 0;

            int allowed = Allowed(count);
            if (allowed == 0)
                return ProbeEnd();
            int read = _source.Read(buffer, offset, allowed);
            _position += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
                return 0;

            int allowed = Allowed(buffer.Length);
            if (allowed == 0)
                return ProbeEnd();
            int read = _source.Read(buffer[..allowed]);
            _position += read;
            return read;
        }

        public override int ReadByte()
        {
            if (_position == _maxBytes)
            {
                ProbeEnd();
                return -1;
            }
            int value = _source.ReadByte();
            if (value >= 0)
                _position++;
            return value;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(
                buffer.AsMemory(offset, count),
                cancellationToken)
            .AsTask();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
                return 0;

            int allowed = Allowed(buffer.Length);
            if (allowed == 0)
            {
                byte[] probe = new byte[1];
                int extra = await _source.ReadAsync(
                    probe,
                    cancellationToken);
                if (extra != 0)
                    ThrowLimit();
                return 0;
            }

            int read = await _source.ReadAsync(
                buffer[..allowed],
                cancellationToken);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!CanSeek)
                throw new NotSupportedException();

            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (target < 0 || target > _maxBytes)
                ThrowLimit();
            _source.Seek(checked(_start + target), SeekOrigin.Begin);
            _position = target;
            return target;
        }

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _source.Dispose();
            base.Dispose(disposing);
        }

        int Allowed(int requested) =>
            (int)Math.Min(requested, _maxBytes - _position);

        int ProbeEnd()
        {
            if (_source.ReadByte() >= 0)
                ThrowLimit();
            return 0;
        }

        void ThrowLimit() =>
            throw new InvalidDataException(
                "A selected assembly entry exceeds the configured "
                + "assembly-entry byte limit.");
    }
}

/// <summary>
/// A package Root named implementation assemblies that select no
/// implementation asset: a visible realization failure
/// (docs/design/package-read-demand.md#named-implementation-and-aligned-blocks).
/// </summary>
public sealed class PackageImplementationNameException : InvalidOperationException
{
    public PackageImplementationNameException(IReadOnlyList<string> unmatchedNames)
        : base(
            "No selected implementation asset is named "
            + string.Join(", ", unmatchedNames.Select(static name => $"'{name}'"))
            + ".")
    {
        UnmatchedNames = unmatchedNames;
    }

    /// <summary>The names that select no implementation asset.</summary>
    public IReadOnlyList<string> UnmatchedNames { get; }
}
