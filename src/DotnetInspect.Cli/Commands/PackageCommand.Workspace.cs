using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Networking;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using NuGet.Versioning;

namespace DotnetInspect.Cli.Commands;

public sealed record WorkspacePackageInspectionContent(
    InspectionResult? Inspection,
    int ExitCode);

public partial class PackageCommand
{
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
                CommandError.Write("--output requires a valid file path.");
                return 1;
            }

            if (EvidenceEnvelopeOutput.PathsMayIdentifySameFile(
                    evidencePath,
                    outputPath))
            {
                CommandError.Write(
                    "--output and --evidence-envelope must name distinct files.");
                return 1;
            }
        }
#endif

        WorkspacePacketRestorationResult restorationResult =
            await WorkspacePacketRestoration.RestoreAsync(
                options.WorkspacePacket!,
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
                            facet: new("package.overview"),
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
        string? temporaryPath = null;
        try
        {
            WorkspacePackageResolution workspaceResolution =
                await target.UseContent(
                    content => CreateWorkspaceResolutionAsync(
                        target,
                        content,
                        path => temporaryPath = path))
                    .ConfigureAwait(false);
            InspectionResult? inspection = null;
            string? requestedTargetFramework =
                target.ContextTargetFramework
                ?? target.Root.Root.RequestedTargetFramework
                ?? target.Root.Root.AssetSelection.TargetFramework;
            InspectionOptions ordinaryOptions = options with
            {
                WorkspacePacket = null,
                ShareFormat = null,
                Tfm = options.WorkspaceLibrarySelection is null
                    ? requestedTargetFramework
                    : target.Root.Root.AssetSelection.TargetFramework
                        ?? requestedTargetFramework,
                WorkspaceLibraryAssetPaths =
                    options.WorkspaceLibrarySelection is null
                        ? null
                        :
                        [
                            .. target.Root.Root.AssetSelection.Assets.Select(
                                static asset => asset.Path),
                        ],
#if DEBUG
                EvidenceEnvelopePath = null,
#endif
            };
            int exitCode = await ExecuteCoreAsync(
                ordinaryOptions,
                context,
                workspaceResolution.Resolution,
                target.Root,
                workspaceResolution.ManifestBytes,
                () => PackageInfoMeasurementInspection.ProjectAdmittedRoot(
                    target.Root,
                    workspaceResolution.ManifestBytes,
                    ordinaryOptions.Tfm),
                () => PackageEcosystemDependencyRecognitionInspection
                    .ExecuteAsync(
                        target.Root,
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
            SelectedContextExactPackageLiveTarget target,
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
                target.Root.Root.PackageId,
                target.Root.Root.PackageVersion,
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
                    + "cannot combine with --tfm, version populations, "
                    + "preview/latest selection, discovery, or Package "
                    + "Library routes.");
            return false;
        }
#if DEBUG
        if (options.EvidenceEnvelopePath is not null
            && (options.ListLayout
                || options.ListTfms
                || options.ShowContent))
        {
            CommandError.Write(
                "--evidence-envelope requires section-based Package "
                    + "inspection; it cannot combine with --layout, --tfms, "
                    + "or --content.");
            return false;
        }
#endif

        PackageReferenceTarget target = PackageExtractor.ParsePackageTarget(
            options.PackageArgs[0],
            options.ExplicitVersion);
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
            || options.ListLayout
            || options.ListTfms
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
