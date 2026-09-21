using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using DotnetInspector.Packages;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems;

/// <summary>
/// Produces detached Package ecosystem-dependency recognition from admitted
/// Package content and its exact compile realization.
/// </summary>
public static class PackageEcosystemDependencyRecognitionInspection
{
    public static async Task<
        InspectionEnvelope<EcosystemDependencyRecognitionOutcome>> ExecuteAsync(
            PackageHouseSettlement.Acquired settlement,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        cancellationToken.ThrowIfCancellationRequested();

        PackageHouseRootContributionOutcome rootOutcome =
            PackageHouseRootContributionAdapter.Create(settlement);
        RealizedMemberCoordinate.Package subjectCoordinate =
            SubjectCoordinate(settlement, rootOutcome);
        PackageHouseRootContribution? contribution =
            (rootOutcome as PackageHouseRootContributionOutcome.Contributed)
                ?.Contribution;
        PackageCompileAssetSelectionReceipt? receipt =
            contribution?.SelectionReceipt;
        string? effectiveFramework =
            receipt?.RequestedTargetFramework
            ?? receipt?.Selection.TargetFramework
            ?? subjectCoordinate.Framework;
        return await ExecuteCoreAsync(
                settlement.Payload.Content,
                contribution?.Binding,
                receipt,
                subjectCoordinate,
                effectiveFramework,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Produces detached Package ecosystem-dependency recognition from one
    /// already-admitted Package Root.
    /// </summary>
    public static Task<
        InspectionEnvelope<EcosystemDependencyRecognitionOutcome>> ExecuteAsync(
            PackageRootBinding root,
            string? effectiveTargetFramework = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        cancellationToken.ThrowIfCancellationRequested();
        string? framework =
            effectiveTargetFramework
            ?? root.Root.RequestedTargetFramework
            ?? root.Root.AssetSelection.TargetFramework;
        return root.Root.UseContent(content =>
        {
            PackageCompileAssetSelectionReceipt receipt =
                PackageCompileAssetSelector.RetainSelection(
                    content,
                    root.Root.PackageId,
                    framework is null
                        ? PackageCompileAssetSelectionPolicy.HighestAvailable
                        : PackageCompileAssetSelectionPolicy.ExplicitTarget,
                    root.Root.AssetSelection,
                    framework,
                    root.Root.RequestedRuntimeIdentifier);
            return ExecuteCoreAsync(
                content,
                root,
                receipt,
                root.Coordinate,
                framework,
                cancellationToken);
        });
    }

    private static async Task<
        InspectionEnvelope<EcosystemDependencyRecognitionOutcome>>
        ExecuteCoreAsync(
            IPackageContent content,
            PackageRootBinding? root,
            PackageCompileAssetSelectionReceipt? receipt,
            RealizedMemberCoordinate.Package subjectCoordinate,
            string? effectiveFramework,
            CancellationToken cancellationToken)
    {
        var subject = new EcosystemDependencySubject.Package(
            subjectCoordinate);
        var issues = ImmutableArray.CreateBuilder<
            EcosystemDependencyInputIssue>();
        var diagnostics = ImmutableArray.CreateBuilder<
            InspectionDiagnostic>();
        var observations = ImmutableArray.CreateBuilder<
            EcosystemDependencyObservation>();
        int nextIssue = 1;
        int nextObservation = 1;

        PackageDependencyProjection dependencies =
            await ProjectDependenciesAsync(
                    content,
                    subjectCoordinate,
                    effectiveFramework,
                    issues,
                    diagnostics,
                    nextIssue,
                    cancellationToken)
                .ConfigureAwait(false);
        nextIssue = dependencies.NextIssue;
        observations.AddRange(
            DependencyObservations(
                dependencies.Groups,
                subjectCoordinate,
                ref nextObservation));

        PackageCompileProjection compile =
            await ProjectCompileLibrariesAsync(
                    root,
                    receipt,
                    subjectCoordinate,
                    issues,
                    diagnostics,
                    nextIssue,
                    nextObservation,
                    cancellationToken)
                .ConfigureAwait(false);
        observations.AddRange(compile.Observations);

        var context = new EcosystemDependencyInputContext.Package(
            dependencies.Manifest,
            dependencies.DependencyGroups,
            compile.CompileSelection);
        EcosystemDependencyObservationBatch batch =
            CreateBatch(
                subject,
                context,
                observations.ToImmutable(),
                issues.ToImmutable());
        EcosystemDependencyRecognitionPortableProjection share = CreateShare(subject);
        return EcosystemDependencyRecognizer.Recognize(
            ProductEcosystemPacks.DependencyRecognitionProfile,
            batch,
            share,
            diagnostics.ToImmutable());
    }

    public static bool TryCreateUnavailableWithoutAcquiredSettlement(
        string? packageId,
        string? version,
        string? producer,
        [NotNullWhen(true)]
        out InspectionEnvelope<EcosystemDependencyRecognitionOutcome>?
            inspection)
    {
        inspection = null;
        if (string.IsNullOrWhiteSpace(packageId)
            || string.IsNullOrWhiteSpace(producer)
            || !PackageExtractor.TryNormalizePackageVersion(
                version,
                out string normalizedVersion))
        {
            return false;
        }

        if (!RealizedMemberCoordinate.Package.TryCreate(
                packageId.ToLowerInvariant(),
                normalizedVersion,
                producer,
                framework: null,
                runtimeIdentifier: null,
                out RealizedMemberCoordinate.Package? coordinate,
                out _))
        {
            return false;
        }

        var subject = new EcosystemDependencySubject.Package(coordinate);
        var source = new EcosystemDependencyInputIssueSource.Package(
            coordinate);
        var issues = ImmutableArray.CreateBuilder<
            EcosystemDependencyInputIssue>();
        var diagnostics = ImmutableArray.CreateBuilder<
            InspectionDiagnostic>();
        int nextIssue = 1;
        EcosystemDependencyInputIssueIdentity manifestIssue = AddIssue(
            issues,
            diagnostics,
            ref nextIssue,
            EcosystemDependencyInputRole.PackageManifestProjection,
            "ecosystem-dependency-recognition.package-manifest-unavailable",
            "Package manifest recognition was unavailable because the "
            + "package inspection did not retain an acquired PackageHouse "
            + "settlement.",
            source);
        EcosystemDependencyInputIssueIdentity targetIssue = AddIssue(
            issues,
            diagnostics,
            ref nextIssue,
            EcosystemDependencyInputRole.EffectiveTargetFrameworkSelection,
            "ecosystem-dependency-recognition.package-dependencies-not-attempted",
            "Dependency-group selection was not attempted because Package "
            + "manifest recognition was unavailable.",
            source);
        EcosystemDependencyInputIssueIdentity compileIssue = AddIssue(
            issues,
            diagnostics,
            ref nextIssue,
            EcosystemDependencyInputRole.SelectedCompileLibraryEnumeration,
            "ecosystem-dependency-recognition.package-compile-selection-unavailable",
            "Selected compile libraries were unavailable because the package "
            + "inspection did not retain an acquired PackageHouse settlement.",
            source);
        var context = new EcosystemDependencyInputContext.Package(
            new EcosystemDependencyInputComponent<
                PackageManifestFacts>.Unavailable(manifestIssue),
            new EcosystemDependencyInputComponent<
                PackageDependencyGroups>.NotAttempted(targetIssue),
            new EcosystemDependencyInputComponent<
                EcosystemDependencyPackageCompileSelection>.Unavailable(
                    compileIssue));
        EcosystemDependencyObservationBatch batch = CreateBatch(
            subject,
            context,
            [],
            issues.ToImmutable());

        inspection = EcosystemDependencyRecognizer.Recognize(
            ProductEcosystemPacks.DependencyRecognitionProfile,
            batch,
            CreateShare(subject),
            diagnostics.ToImmutable());
        return true;
    }

    private static EcosystemDependencyRecognitionPortableProjection CreateShare(
        EcosystemDependencySubject.Package subject) =>
        new(
            subject,
            new InspectionPortableProjection.NonProjectable(
                "ecosystem-dependency-recognition/package-share",
                InspectionPortableProjectionFailureReason.NotSupported));

    private static RealizedMemberCoordinate.Package SubjectCoordinate(
        PackageHouseSettlement.Acquired settlement,
        PackageHouseRootContributionOutcome rootOutcome) =>
        rootOutcome switch
        {
            PackageHouseRootContributionOutcome.Contributed contributed =>
                contributed.Contribution.Binding.Coordinate,
            _ => CreateSubjectCoordinate(settlement),
        };

    private static RealizedMemberCoordinate.Package CreateSubjectCoordinate(
        PackageHouseSettlement.Acquired settlement)
    {
        PackageHouseAcquisitionReceipt acquisition =
            settlement.Result.Evidence.Acquisition
            ?? throw new ArgumentException(
                "Package ecosystem recognition requires acquired PackageHouse evidence.",
                nameof(settlement));
        PackageCompileAssetSelectionReceipt? selection =
            (settlement.Result.Evidence.Realization
                as PackageHouseRealizationReceipt.Compile)?.Receipt;
        return new RealizedMemberCoordinate.Package(
            acquisition.Candidate.Coordinate.PackageId.ToLowerInvariant(),
            acquisition.Candidate.Coordinate.Version.ToLowerInvariant(),
            acquisition.Producer.Key,
            NormalizeFramework(
                selection?.RequestedTargetFramework
                ?? selection?.Selection.TargetFramework),
            runtimeIdentifier: null);
    }

    private static string? NormalizeFramework(string? framework) =>
        string.IsNullOrWhiteSpace(framework)
            ? null
            : DotnetInspector.Services.TfmSelector
                .NormalizeTfm(framework)
                .ToLowerInvariant();

    private static async Task<PackageDependencyProjection>
        ProjectDependenciesAsync(
            IPackageContent content,
            RealizedMemberCoordinate.Package subject,
            string? effectiveFramework,
            ImmutableArray<EcosystemDependencyInputIssue>.Builder issues,
            ImmutableArray<InspectionDiagnostic>.Builder diagnostics,
            int nextIssue,
            CancellationToken cancellationToken)
    {
        PackageDependencyGroupsResult result =
            await PackageDependencyGroupsQuery.ExecuteAsync(
                    content,
                    subject.PackageId,
                    subject.Version,
                    effectiveFramework,
                    cancellationToken,
                    allowCompatibleFallbackForRequestedTfm: true)
                .ConfigureAwait(false);

        switch (result)
        {
            case PackageDependencyGroupsResult.Available available
                when available.Value.SelectionStatus
                    is PackageDependencyGroupSelectionStatus.Selected
                        or PackageDependencyGroupSelectionStatus
                            .NoDependencyGroups:
                return new(
                    new EcosystemDependencyInputComponent<
                        PackageManifestFacts>.Available(
                            available.Manifest),
                    new EcosystemDependencyInputComponent<
                        PackageDependencyGroups>.Available(
                            available.Value),
                    available.Value,
                    nextIssue);

            case PackageDependencyGroupsResult.Available available:
            {
                EcosystemDependencyInputIssueIdentity issue =
                    AddIssue(
                        issues,
                        diagnostics,
                        ref nextIssue,
                        EcosystemDependencyInputRole
                            .EffectiveTargetFrameworkSelection,
                        "ecosystem-dependency-recognition.package-target-unavailable",
                        "The Package dependency group could not be selected for the effective target framework.",
                        new EcosystemDependencyInputIssueSource.Package(
                            subject));
                return new(
                    new EcosystemDependencyInputComponent<
                        PackageManifestFacts>.Available(
                            available.Manifest),
                    new EcosystemDependencyInputComponent<
                        PackageDependencyGroups>.Unavailable(issue),
                    Groups: null,
                    nextIssue);
            }

            case PackageDependencyGroupsResult.NoManifest:
            {
                EcosystemDependencyInputIssueIdentity manifestIssue =
                    AddIssue(
                        issues,
                        diagnostics,
                        ref nextIssue,
                        EcosystemDependencyInputRole
                            .PackageManifestProjection,
                        "ecosystem-dependency-recognition.package-manifest-missing",
                        "The Package does not contain one root manifest.",
                        new EcosystemDependencyInputIssueSource.Package(
                            subject));
                EcosystemDependencyInputIssueIdentity dependencyIssue =
                    AddIssue(
                        issues,
                        diagnostics,
                        ref nextIssue,
                        EcosystemDependencyInputRole
                            .EffectiveTargetFrameworkSelection,
                        "ecosystem-dependency-recognition.package-dependencies-not-attempted",
                        "Dependency-group selection was not attempted because the Package manifest is unavailable.",
                        new EcosystemDependencyInputIssueSource.Package(
                            subject));
                return new(
                    new EcosystemDependencyInputComponent<
                        PackageManifestFacts>.Unavailable(manifestIssue),
                    new EcosystemDependencyInputComponent<
                        PackageDependencyGroups>.NotAttempted(
                            dependencyIssue),
                    Groups: null,
                    nextIssue);
            }

            case PackageDependencyGroupsResult.Failed:
            {
                EcosystemDependencyInputIssueIdentity manifestIssue =
                    AddIssue(
                        issues,
                        diagnostics,
                        ref nextIssue,
                        EcosystemDependencyInputRole
                            .PackageManifestProjection,
                        "ecosystem-dependency-recognition.package-manifest-failed",
                        "The Package manifest could not be projected.",
                        new EcosystemDependencyInputIssueSource.Package(
                            subject));
                EcosystemDependencyInputIssueIdentity dependencyIssue =
                    AddIssue(
                        issues,
                        diagnostics,
                        ref nextIssue,
                        EcosystemDependencyInputRole
                            .EffectiveTargetFrameworkSelection,
                        "ecosystem-dependency-recognition.package-dependencies-not-attempted",
                        "Dependency-group selection was not attempted because the Package manifest projection failed.",
                        new EcosystemDependencyInputIssueSource.Package(
                            subject));
                return new(
                    new EcosystemDependencyInputComponent<
                        PackageManifestFacts>.Unavailable(manifestIssue),
                    new EcosystemDependencyInputComponent<
                        PackageDependencyGroups>.NotAttempted(
                            dependencyIssue),
                    Groups: null,
                    nextIssue);
            }

            default:
                throw new InvalidOperationException(
                    "Unknown Package dependency-group result.");
        }
    }

    private static ImmutableArray<EcosystemDependencyObservation>
        DependencyObservations(
            PackageDependencyGroups? groups,
            RealizedMemberCoordinate.Package subject,
            ref int nextObservation)
    {
        if (groups?.SelectedGroup is not { } selected)
            return [];

        var observations = ImmutableArray.CreateBuilder<
            EcosystemDependencyObservation>(
                selected.Dependencies.Length);
        foreach (DeclaredPackageDependency dependency in
                 selected.Dependencies)
        {
            observations.Add(
                new EcosystemDependencyObservation.PackageDeclaration(
                    new(nextObservation),
                    nextObservation,
                    dependency,
                    subject));
            nextObservation++;
        }

        return observations.ToImmutable();
    }

    private static async Task<PackageCompileProjection>
        ProjectCompileLibrariesAsync(
            PackageRootBinding? root,
            PackageCompileAssetSelectionReceipt? receipt,
            RealizedMemberCoordinate.Package subject,
            ImmutableArray<EcosystemDependencyInputIssue>.Builder issues,
            ImmutableArray<InspectionDiagnostic>.Builder diagnostics,
            int nextIssue,
            int nextObservation,
            CancellationToken cancellationToken)
    {
        if (root is null || receipt is null)
        {
            EcosystemDependencyInputIssueIdentity issue =
                AddIssue(
                    issues,
                    diagnostics,
                    ref nextIssue,
                    EcosystemDependencyInputRole
                        .SelectedCompileLibraryEnumeration,
                    "ecosystem-dependency-recognition.package-compile-selection-unavailable",
                    "The PackageHouse result does not retain an available compile-asset selection.",
                    new EcosystemDependencyInputIssueSource.Package(subject));
            return new(
                new EcosystemDependencyInputComponent<
                    EcosystemDependencyPackageCompileSelection>.Unavailable(
                        issue),
                [],
                nextIssue);
        }

        if (receipt.Selection.Status is not (
                PackageCompileAssetSelectionStatus.Selected
                or PackageCompileAssetSelectionStatus.NoCompileAssets
                or PackageCompileAssetSelectionStatus.EmptyCompileGroup))
        {
            EcosystemDependencyInputIssueIdentity issue =
                AddIssue(
                    issues,
                    diagnostics,
                    ref nextIssue,
                    EcosystemDependencyInputRole
                        .SelectedCompileLibraryEnumeration,
                    "ecosystem-dependency-recognition.package-compile-selection-failed",
                    "The Package compile-asset selection did not produce an inspectable selected Library population.",
                    new EcosystemDependencyInputIssueSource.Package(subject));
            return new(
                new EcosystemDependencyInputComponent<
                    EcosystemDependencyPackageCompileSelection>.Unavailable(
                        issue),
                [],
                nextIssue);
        }

        if (receipt.Selection.Status is not
            PackageCompileAssetSelectionStatus.Selected)
        {
            return new(
                new EcosystemDependencyInputComponent<
                    EcosystemDependencyPackageCompileSelection>.Available(
                        new(receipt)),
                [],
                nextIssue);
        }

        InspectionWorkspace workspace = new();
        PackageAssemblyContextRealization? realization = null;
        try
        {
            try
            {
                realization =
                    await workspace.RealizePackageAssemblyContextRolesAsync(
                            root,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (Exception failure) when (
                failure is PackageAssemblyRoleCorrespondenceException
                    or InvalidDataException
                    or BadImageFormatException
                && !failure.Data.Contains(
                    "Inspector.Artifacts.Workspaces.CleanupFailures")
                && !failure.Data.Contains(
                    "DotnetInspector.Queries.WorkspaceCleanupFailure"))
            {
                EcosystemDependencyInputIssueIdentity issue =
                    AddIssue(
                        issues,
                        diagnostics,
                        ref nextIssue,
                        EcosystemDependencyInputRole
                            .SelectedCompileLibraryEnumeration,
                        "ecosystem-dependency-recognition.package-library-realization-failed",
                        "The selected Package compile Libraries could not be realized for metadata inspection.",
                        new EcosystemDependencyInputIssueSource.Package(
                            subject));
                return new(
                    new EcosystemDependencyInputComponent<
                        EcosystemDependencyPackageCompileSelection>.Unavailable(
                            issue),
                    [],
                    nextIssue);
            }

            if (!realization.HasAssemblyContexts
                || realization.SurfaceParticipants.Length
                    != receipt.Selection.Assets.Count)
            {
                EcosystemDependencyInputIssueIdentity issue =
                    AddIssue(
                        issues,
                        diagnostics,
                        ref nextIssue,
                        EcosystemDependencyInputRole
                            .SelectedCompileLibraryEnumeration,
                        "ecosystem-dependency-recognition.package-library-correspondence-incomplete",
                        "The selected Package compile assets did not produce one exact Library participant each.",
                        new EcosystemDependencyInputIssueSource.Package(
                            subject));
                return new(
                    new EcosystemDependencyInputComponent<
                        EcosystemDependencyPackageCompileSelection>.Unavailable(
                            issue),
                    [],
                    nextIssue);
            }

            var selectedLibraries = ImmutableArray.CreateBuilder<
                EcosystemDependencySelectedLibrary>(
                    realization.SurfaceParticipants.Length);
            var observations = ImmutableArray.CreateBuilder<
                EcosystemDependencyObservation>();
            foreach (PackageAssemblyRoleParticipant participant in
                     realization.SurfaceParticipants)
            {
                if (!TryCreatePortableIdentity(
                        participant.Participant.Assembly.Identity,
                        out PortableLibraryIdentity? library))
                {
                    _ = AddIssue(
                        issues,
                        diagnostics,
                        ref nextIssue,
                        EcosystemDependencyInputRole
                            .SelectedCompileLibraryEnumeration,
                        "ecosystem-dependency-recognition.package-library-identity-unavailable",
                        "One selected Package compile Library does not have a portable exact assembly identity.",
                        new EcosystemDependencyInputIssueSource.Package(
                            subject));
                    continue;
                }

                selectedLibraries.Add(
                    new EcosystemDependencySelectedLibrary(
                        participant.Asset,
                        library));
                AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceIdentity>> references =
                    AssemblyContextReferencesQuery.ExecuteParticipant(
                        realization.SurfaceGroup,
                        participant.Participant);
                if (references
                    is AssemblyContextEntry<
                        ImmutableArray<AssemblyReferenceIdentity>>.Available
                            available)
                {
                    foreach (AssemblyReferenceIdentity reference in
                             available.Value)
                    {
                        observations.Add(
                            new EcosystemDependencyObservation
                                .AssemblyReference(
                                    new(nextObservation),
                                    nextObservation,
                                    reference,
                                    library));
                        nextObservation++;
                    }
                }
                else
                {
                    _ = AddIssue(
                        issues,
                        diagnostics,
                        ref nextIssue,
                        EcosystemDependencyInputRole
                            .AssemblyReferenceProjection,
                        "ecosystem-dependency-recognition.package-library-references-unavailable",
                        "Direct assembly references could not be projected for one selected Package Library.",
                        new EcosystemDependencyInputIssueSource.Library(
                            library));
                }
            }

            return new(
                new EcosystemDependencyInputComponent<
                    EcosystemDependencyPackageCompileSelection>.Available(
                        new(receipt, selectedLibraries)),
                observations.ToImmutable(),
                nextIssue);
        }
        finally
        {
            await CloseWorkspaceAsync(realization, workspace)
                .ConfigureAwait(false);
        }
    }

    private static bool TryCreatePortableIdentity(
        AssemblyReferenceIdentity identity,
        [NotNullWhen(true)]
        out PortableLibraryIdentity? library)
    {
        library = null;
        if (identity.Version is null)
            return false;

        try
        {
            library = new PortableLibraryIdentity(
                identity.Name,
                identity.Version.ToString(4),
                string.IsNullOrWhiteSpace(identity.Culture)
                    || identity.Culture.Equals(
                        "neutral",
                        StringComparison.OrdinalIgnoreCase)
                        ? null
                        : identity.Culture,
                identity.PublicKeyToken?.ToLowerInvariant());
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async ValueTask CloseWorkspaceAsync(
        PackageAssemblyContextRealization? realization,
        InspectionWorkspace workspace)
    {
        List<Exception>? failures = null;
        try
        {
            realization?.Dispose();
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        try
        {
            InspectionWorkspaceCloseReport report =
                await workspace.CloseAsync().ConfigureAwait(false);
            if (!report.ArtifactSessionCleanupFailures.IsEmpty)
            {
                (failures ??= []).AddRange(
                    report.ArtifactSessionCleanupFailures);
            }
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }

        if (failures is not null)
            throw new AggregateException(failures);
    }

    private static EcosystemDependencyObservationBatch CreateBatch(
        EcosystemDependencySubject.Package subject,
        EcosystemDependencyInputContext.Package context,
        ImmutableArray<EcosystemDependencyObservation> observations,
        ImmutableArray<EcosystemDependencyInputIssue> issues)
    {
        if (issues.IsEmpty)
        {
            return new EcosystemDependencyObservationBatch.Available(
                subject,
                context,
                observations);
        }

        bool hasAvailableDomain =
            context.DependencyGroup
                is EcosystemDependencyInputComponent<
                    PackageDependencyGroups>.Available
            || context.CompileAssets
                is EcosystemDependencyInputComponent<
                    EcosystemDependencyPackageCompileSelection>.Available;
        return hasAvailableDomain
            ? new EcosystemDependencyObservationBatch.Incomplete(
                subject,
                context,
                observations,
                issues)
            : new EcosystemDependencyObservationBatch.Unavailable(
                subject,
                context,
                issues);
    }

    private static EcosystemDependencyInputIssueIdentity AddIssue(
        ImmutableArray<EcosystemDependencyInputIssue>.Builder issues,
        ImmutableArray<InspectionDiagnostic>.Builder diagnostics,
        ref int nextIssue,
        EcosystemDependencyInputRole role,
        string code,
        string summary,
        EcosystemDependencyInputIssueSource source)
    {
        var identity = new EcosystemDependencyInputIssueIdentity(nextIssue++);
        var diagnostic = new InspectionDiagnostic(
            code,
            InspectionDiagnosticSeverity.Warning,
            summary);
        issues.Add(new(identity, role, diagnostic, source));
        diagnostics.Add(diagnostic);
        return identity;
    }

    private sealed record PackageDependencyProjection(
        EcosystemDependencyInputComponent<PackageManifestFacts> Manifest,
        EcosystemDependencyInputComponent<PackageDependencyGroups>
            DependencyGroups,
        PackageDependencyGroups? Groups,
        int NextIssue);

    private sealed record PackageCompileProjection(
        EcosystemDependencyInputComponent<
            EcosystemDependencyPackageCompileSelection> CompileSelection,
        ImmutableArray<EcosystemDependencyObservation> Observations,
        int NextIssue);
}
