using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Networking;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using NuGet.Versioning;

namespace DotnetInspect.Cli.Commands;

public sealed record WorkspacePackageInspectionContent(
    InspectionResult? Inspection,
    int ExitCode);

public partial class PackageCommand
{
    internal static ViewFacetId SavedLibrariesFacet => ExactPackageLibrariesFacet;

    static readonly ViewFacetId ExactPackageLibrariesFacet =
        InspectionViewFacetCatalog.Registry
            .GetRequiredDescriptor(
                StructuralSubjectKind.Package,
                ViewFacetRole.PackageLibraries)
            .Id;

    static readonly ViewFacetId ExactPackageOverviewFacet =
        InspectionViewFacetCatalog.Registry
            .GetRequiredDescriptor(
                StructuralSubjectKind.Package,
                ViewFacetRole.PackageOverview)
            .Id;

    // The share names the facet the command rendered: the default Libraries
    // Tree, or the section document for any other Package selection.
    static ViewFacetId ExactPackageShareFacet(InspectionOptions options) =>
        IsPackageChildrenProjection(options)
            ? ExactPackageLibrariesFacet
            : ExactPackageOverviewFacet;

    internal static async ValueTask<int> ExecuteSavedLibrariesAsync(
        WorkspaceOptions presentation,
        CompleteRestorationSelectedSubject target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (target is not CompleteRestorationSelectedSubject.Package package
            || package.Lens.Facet != ExactPackageLibrariesFacet
            || package.Queries.Length != 0)
        {
            CommandError.Write("The saved subject has no Package Libraries execution binding.");
            return 1;
        }

        // The label feeds ordinary presentation only; the exact Root supplies content.
        var options = new InspectionOptions
        {
            PackageArgs = [package.Root.Root.PackageId],
            Format = presentation.Format,
            JsonOutput = presentation.Format == OutputFormat.Json,
            Count = presentation.Count,
            Rows = presentation.Rows,
            NoHeader = presentation.NoHeader,
            Verbose = presentation.Verbose,
            CompanionOutput = CompanionOutput.None,
        };
        WorkspacePackageInspectionContent result = await ExecuteAdmittedPackageAsync(
            options, new CommandContext(presentation.Verbose), package.Root,
            contextTargetFramework: package.Root.Coordinate.Framework).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result.ExitCode;
    }

    static async Task<int> ExecuteWorkspaceExactPackageAsync(
        InspectionOptions options,
        CommandContext context,
        WorkspaceContextLoadOptions? workspaceLoadOptions)
    {
        if (!TryCreateWorkspacePackageRequest(
                options,
                out SelectedContextExactPackageInspectionRequest? request))
        {
            return 1;
        }

#if DEBUG
        string? evidencePath = null;
        if (options.EvidenceEnvelopePath is { } requestedEvidencePath
            && !EvidenceEnvelopeOutput.TryResolvePath(
                requestedEvidencePath,
                out evidencePath,
                out string? evidencePathError))
        {
            CommandError.Write(evidencePathError!);
            return 1;
        }
        if (evidencePath is not null
            && options.OutputPath is { } requestedOutputPath)
        {
            string outputPath;
            try
            {
                outputPath = Path.GetFullPath(requestedOutputPath);
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    or NotSupportedException
                    or PathTooLongException)
            {
                CommandError.Write("--out requires a valid file path.");
                return 1;
            }

            if (EvidenceEnvelopeOutput.PathsMayIdentifySameFile(
                    evidencePath,
                    outputPath))
            {
                CommandError.Write(
                    "--out and --evidence-envelope must name distinct files.");
                return 1;
            }
        }
#endif

        string packet = options.WorkspacePacket!;
        if (options.WorkspaceLibrarySelection is null)
        {
            try
            {
                WorkspaceSharePacket source = WorkspaceSharePacketCodec.Decode(
                    WorkspacePacketRestoration.GetPacketInput(packet, "--workspace"));
                WorkspaceSharePacketProjectionResult derived =
                    WorkspacePackageScenarioDerivation.Prepare(
                        source, request!.PackageId, request.Version);
                if (!derived.Succeeded)
                {
                    CommandError.Write(
                        "The Workspace Package declaration could not be derived.",
                        [derived.Failure!.Message]);
                    return 1;
                }
                if (!ReferenceEquals(source, derived.Packet))
                    packet = WorkspaceSharePacketCodec.Encode(derived.Packet!);
            }
            catch (Exception error) when (error is WorkspaceSharePacketException
                or InvalidDataException or ArgumentException)
            {
                CommandError.Write("The Workspace packet input is invalid.", [error.Message]);
                return 1;
            }
        }

        WorkspacePacketRestorationResult restorationResult =
            await WorkspacePacketRestoration.RestoreAsync(
                packet,
                workspaceLoadOptions
                    ?? CreateWorkspaceContextLoadOptions(options),
                CancellationToken.None).ConfigureAwait(false);
        if (restorationResult is WorkspacePacketRestorationResult.Failed failed)
        {
            CommandError.Write(failed.Summary, failed.Details);
            return 1;
        }

        WorkspacePacketRestoration restoration =
            ((WorkspacePacketRestorationResult.Restored)restorationResult)
                .Value;
        return await restoration.ExecuteAsync(async activeRestoration =>
        {
            InspectionShare.NonProjectable? shareRefusal =
                WorkspacePackageShareRefusal(options);
            SelectedContextExactPackageEvidenceOperationResult<
                WorkspacePackageInspectionContent> operation =
                    await SelectedContextExactPackageInspectionOperation
                        .ExecuteWithEvidenceAsync(
                            activeRestoration.Workspace,
                            activeRestoration.Activation,
                            request!,
                            target => ExecuteWorkspaceTargetAsync(
                                options,
                                context,
                                target),
                            facet: ExactPackageShareFacet(options),
                            shareRefusal).ConfigureAwait(false);
            if (operation
                is SelectedContextExactPackageEvidenceOperationResult<
                    WorkspacePackageInspectionContent>.Failed selectionFailure)
            {
                CommandError.Write(selectionFailure.Failure.Message);
                return 1;
            }

            EvidenceInspectionEnvelope<
                WorkspacePackageInspectionContent,
                SelectedContextPackageRoutingEvidence> envelope =
                    ((SelectedContextExactPackageEvidenceOperationResult<
                        WorkspacePackageInspectionContent>.Completed)operation)
                        .Envelope;
            int exitCode = envelope.Inspection.Content.ExitCode;

#if DEBUG
            if (evidencePath is not null)
            {
                var contract =
                    new InspectionEnvelopeJsonContract<
                        WorkspacePackageInspectionContent>(
                        "package-workspace",
                        1,
                        JsonContext.Default.WorkspacePackageInspectionContent);
                if (!InspectionEnvelopeOutput.TrySerializeEvidence(
                        envelope,
                        contract,
                        SelectedContextPackageInspectionJsonContext.Default
                            .SelectedContextPackageRoutingEvidence,
                        compactJson: false,
                        out byte[] payload,
                        out Exception? serializationError))
                {
                    CommandError.Write(
                        $"Evidence envelope serialization failed for "
                            + $"'{evidencePath}': "
                            + serializationError!.Message);
                    exitCode = 1;
                }
                else if (!EvidenceEnvelopeOutput.TryPublish(
                        evidencePath,
                        payload,
                        out Exception? publicationError))
                {
                    CommandError.Write(
                        $"Evidence envelope publication failed for "
                            + $"'{evidencePath}': "
                            + publicationError!.Message);
                    exitCode = 1;
                }
                else
                {
                    CommandError.WriteLine(
                        $"Evidence envelope: {evidencePath}");
                }
            }
#endif

            if (options.ShareFormat is not null)
            {
                int shareExitCode = WorkspaceShareOutput.Write(
                    envelope.Inspection.Share,
                    options.ShareFormat.Value);
                if (shareExitCode != 0)
                    exitCode = 1;
            }

            return exitCode;
        }).ConfigureAwait(false);
    }

    static async ValueTask<WorkspacePackageInspectionContent>
        ExecuteWorkspaceTargetAsync(
            InspectionOptions options,
            CommandContext context,
            SelectedContextExactPackageLiveTarget target)
    {
        return await ExecuteAdmittedPackageAsync(
            options, context, target.Root, target.ContextTargetFramework)
            .ConfigureAwait(false);
    }

    static async Task<WorkspacePackageInspectionContent> ExecuteAdmittedPackageAsync(
        InspectionOptions options,
        CommandContext context,
        PackageRootBinding root,
        string? contextTargetFramework,
        bool exactReference = false,
        ConfiguredPackagePayloadResult? acquisition = null)
    {
        string? temporaryPath = null;
        try
        {
            WorkspacePackageResolution workspaceResolution =
                await root.Root.UseContent(
                    content => CreateWorkspaceResolutionAsync(
                        root,
                        content,
                        path => temporaryPath = path))
                    .ConfigureAwait(false);
            InspectionResult? inspection = null;
            string? requestedTargetFramework =
                contextTargetFramework
                ?? root.Root.RequestedTargetFramework
                ?? root.Root.AssetSelection.TargetFramework;
            InspectionOptions ordinaryOptions = acquisition is not null
                ? options
                : options with
            {
                WorkspacePacket = null,
                ShareFormat = null,
                Tfm = options.WorkspaceLibrarySelection is null
                    && !exactReference
                    ? requestedTargetFramework
                    : root.Root.AssetSelection.TargetFramework
                        ?? requestedTargetFramework,
                WorkspaceLibraryAssetPaths =
                    options.WorkspaceLibrarySelection is null
                        && !exactReference
                        ? null
                        :
                        [
                            .. root.Root.AssetSelection.Assets.Select(
                                static asset => asset.Path),
                        ],
#if DEBUG
                EvidenceEnvelopePath = null,
#endif
            };
            int exitCode = await ExecuteCoreAsync(
                ordinaryOptions,
                context,
                acquisition is null
                    ? workspaceResolution.Resolution
                    : PackageExtractionResult.FromAcquisition(acquisition),
                root,
                workspaceResolution.ManifestBytes,
                () => PackageInfoMeasurementInspection.ProjectAdmittedRoot(
                    root,
                    workspaceResolution.ManifestBytes,
                    ordinaryOptions.Tfm?.Equals(
                        "all",
                        StringComparison.OrdinalIgnoreCase) == true
                            ? null
                            : ordinaryOptions.Tfm),
                () => acquisition?.HouseSettlement
                        is PackageHouseSettlement.Acquired settlement
                    ? PackageEcosystemDependencyRecognitionInspection
                        .ExecuteAsync(settlement)
                    : PackageEcosystemDependencyRecognitionInspection
                        .ExecuteAsync(
                            root,
                            ordinaryOptions.Tfm),
                result => inspection = result,
                workspaceLoadOptions: null).ConfigureAwait(false);
            return new(inspection, exitCode);
        }
        finally
        {
            if (temporaryPath is not null)
                PackageExtractor.Cleanup(temporaryPath);
        }
    }

    static async Task<WorkspacePackageResolution>
        CreateWorkspaceResolutionAsync(
            PackageRootBinding root,
            IPackageContent content,
            Action<string> ownsTemporaryPath)
    {
        string[] entries = [.. content.EnumerateEntries()];
        byte[]? manifestBytes = null;
        string? manifestEntry =
            PackageManifestContent.FindRootManifest(content);
        if (manifestEntry is not null)
        {
            if (!content.TryOpenEntry(
                    manifestEntry,
                    PackageManifestFactsQuery.MaxManifestBytes,
                    out Stream? manifest))
            {
                throw new IOException(
                    $"The admitted Package entry '{manifestEntry}' could not be opened.");
            }

            await using (manifest.ConfigureAwait(false))
            {
                manifestBytes = await BoundedContentReader.ReadAllBytesAsync(
                    manifest,
                    PackageManifestFactsQuery.MaxManifestBytes)
                    .ConfigureAwait(false);
            }
        }

        string extractPath;
        if (content.RootPath is { } rootPath)
        {
            extractPath = rootPath;
        }
        else
        {
            extractPath = Path.Combine(
                Path.GetTempPath(),
                $"dotnet-inspect-package-{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractPath);
            ownsTemporaryPath(extractPath);
            foreach (string entry in entries)
            {
                string destination = ResolveContainedEntry(
                    extractPath,
                    entry);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(destination)!);
                if (!content.TryOpenEntry(entry, out Stream? input))
                {
                    throw new IOException(
                        $"The admitted Package entry '{entry}' could not be opened.");
                }
                await using (input.ConfigureAwait(false))
                await using (var output = new FileStream(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    await input.CopyToAsync(output).ConfigureAwait(false);
                }
            }
        }

        return new WorkspacePackageResolution(
            new PackageExtractionResult(
                extractPath,
                TempDir: null,
                root.Root.PackageId,
                root.Root.PackageVersion,
                content.NupkgPath,
                content.FromCache,
                content.ProducerKey),
            manifestBytes);
    }

    sealed record WorkspacePackageResolution(
        PackageExtractionResult Resolution,
        byte[]? ManifestBytes);

    static string ResolveContainedEntry(
        string root,
        string entry)
    {
        if (string.IsNullOrWhiteSpace(entry)
            || Path.IsPathRooted(entry)
            || entry.Contains('\\'))
        {
            throw new InvalidDataException(
                "The admitted Package contains an invalid entry path.");
        }

        string destination = Path.GetFullPath(
            entry.Replace('/', Path.DirectorySeparatorChar),
            root);
        string relative = Path.GetRelativePath(root, destination);
        if (relative == ".."
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The admitted Package contains an entry outside its root.");
        }

        return destination;
    }

    static WorkspaceContextLoadOptions CreateWorkspaceContextLoadOptions(
        InspectionOptions options) =>
        new()
        {
            HttpClient = HttpClientFactory.Shared,
            SourceAuthorization =
                new SourcePolicyPackageSourceAuthorization(
                    options.SourceOptions),
            PackageStore = new FileSystemPackageStore(),
            UseVersionCache = true,
            Log = options.Verbose
                ? CommandError.WriteLine
                : null,
        };

    static bool TryCreateWorkspacePackageRequest(
        InspectionOptions options,
        out SelectedContextExactPackageInspectionRequest? request)
    {
        request = null;
        if (options.PackageArgs.Length != 1)
        {
            CommandError.Write(
                "--workspace requires exactly one positional Package ID.");
            return false;
        }
        bool libraryRoute =
            options.WorkspaceLibrarySelection is not null;
        if (options.Tfm is not null
            || options.ExplicitVersion is not null
            || options.ListVersions
            || options.IncludePrerelease
            || options.ForceLatest
            || (!libraryRoute
                && (options.Discover is not null
                    || options.Schema
                    || options.PackageLibrary is not null
                    || options.AllLibraries)))
        {
            CommandError.Write(
                "--workspace supplies the Package location and target; it "
                    + "cannot combine with --tfm, version selection or populations, "
                    + "preview/latest selection, discovery, or Package "
                    + "Library routes.");
            return false;
        }
#if DEBUG
        if (options.EvidenceEnvelopePath is not null
            && options.ShowContent)
        {
            CommandError.Write(
                "--evidence-envelope requires section-based Package "
                    + "inspection; it cannot combine with --content.");
            return false;
        }
#endif

        PackageReferenceTarget target =
            PackageExtractor.ParsePackageTarget(options.PackageArgs[0]);
        if (target.IsLocalFile
            || !PackageExtractor.IsValidPackageId(target.PackageName))
        {
            CommandError.Write(
                "--workspace requires one canonical NuGet Package ID, "
                    + "not a local path or URL.");
            return false;
        }
        NuGetVersion? exactVersion = null;
        if (target.Version.Length > 0
            && !NuGetVersion.TryParse(target.Version, out exactVersion))
        {
            CommandError.Write(
                "--workspace accepts only an exact Package version.");
            return false;
        }

        request = new(
            target.PackageName,
            target.Version.Length == 0
                ? null
                : exactVersion!.ToNormalizedString());
        return true;
    }

    static InspectionShare.NonProjectable?
        WorkspacePackageShareRefusal(InspectionOptions options)
    {
        if (options.ShareFormat is null)
            return null;
        if (options.ListVersions
            || options.ShowContent
            || options.Discover is not null
            || options.PackageLibrary is not null
            || options.AllLibraries)
        {
            return new(
                "package/lens",
                "The requested Package lens has no portable Workspace "
                    + "Package facet.");
        }
        if (options.IncludeSections is { Count: > 0 }
            && (options.IncludeSections.Count != 1
                || !options.IncludeSections.Contains(
                    Views.PackageSections.PackageInfo)))
        {
            return new(
                "package/sections",
                "The requested Package section selection has no portable "
                    + "Workspace Package facet.");
        }

        return null;
    }
}
