using System.Collections.Immutable;

using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using Markout;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{
    private static bool IsPackageChildrenProjection(
        InspectionOptions options) =>
        options.Discover is null
        && options.IncludeSections is not { Count: > 0 }
        && !options.SelectExplicitlySet
        && !options.SelectDefault
        && !options.FixedOverview
        && !options.ListVersions
        && !options.ListLayout
        && !options.ListTfms
        && !options.ShowContent
        && !options.IsRawOutput
        && options.Rows is null
        && options.ShareFormat is null
        && options.Columns is not { Length: > 0 }
        && options.Fields is not { Length: > 0 }
        && (options.Tree || !options.FormatFlagExplicitlySet);

    private static async Task<int> WritePackageChildrenAsync(
        InspectionResult result,
        PackageExtractionResult resolution,
        string extractPath,
        string packageName,
        string version,
        InspectionOptions options)
    {
        InspectionEnvelope<PackageChildrenDocument> inspection =
            await InspectPackageChildrenAsync(
                    result,
                    resolution,
                    extractPath,
                    packageName,
                    version,
                    options,
                    CancellationToken.None)
                .ConfigureAwait(false);
        OutputDestination.Write(
            options.OutputPath,
            null,
            writer => WritePackageChildrenTree(
                writer,
                result,
                inspection.Content,
                options.Verbosity));
        foreach (InspectionDiagnostic diagnostic in inspection.Diagnostics)
        {
            CommandError.WriteLine(
                $"{diagnostic.Code}: {diagnostic.Summary}");
        }
        return inspection.Content.IsComplete
            ? PackageIntegrityExitCode(result)
            : 1;
    }

    private static async ValueTask<
        InspectionEnvelope<PackageChildrenDocument>>
        InspectPackageChildrenAsync(
            InspectionResult result,
            PackageExtractionResult resolution,
            string extractPath,
            string packageName,
            string version,
            InspectionOptions options,
            CancellationToken cancellationToken)
    {
        ToolWrapperPackage? requestedToolWrapper =
            result.IsRidSpecificPointerPackage
                ? resolution.ToolWrapperChain.FirstOrDefault()
                : null;
        var subject = new PackageChildrenSubject(
            requestedToolWrapper?.PackageName
                ?? result.PackageName
                ?? packageName,
            requestedToolWrapper?.Version
                ?? result.Version
                ?? version,
            targetFramework: null);
        if (result.IsRidSpecificPointerPackage
            && result.RuntimeIdentifierPackages is { Count: > 0 }
                ridPackages)
        {
            PackageChildrenDocument pointer =
                PackageChildrenDocument.FromRuntimeIdentifierPackages(
                    subject,
                    ridPackages.Select(
                        static package =>
                            new PackageRuntimeIdentifierChild(
                                package.RuntimeIdentifier,
                                package.PackageId)));
            return PackageChildrenEnvelope(pointer);
        }

        List<PackageChildCandidate> candidates;
        string? targetFramework;
        if (result.IsToolPackage)
        {
            TfmSelector.PackageLibraryResolution toolSelection =
                TfmSelector.SelectPackageLibraries(
                    extractPath,
                    options.Tfm);
            if (!toolSelection.IsSelected)
            {
                return toolSelection.Status
                    == TfmSelector.PackageLibraryResolutionStatus
                        .NoMatchingTargetFramework
                    ? PackageChildrenEnvelope(
                        PackageChildrenDocument.UnavailableLibraries(
                            subject,
                            PackageChildrenStatus.NoApplicableTarget,
                            $"No tool Library slice matches target "
                                + $"'{options.Tfm}'."))
                    : PackageChildrenEnvelope(
                        PackageChildrenDocument.NoManagedLibraries(
                            subject,
                            "The selected tool payload contains no managed "
                                + "Libraries."));
            }

            targetFramework = toolSelection.Tfm;
            HashSet<string> entryPoints =
                ToolEntryPoints(extractPath);
            candidates =
            [
                .. toolSelection.Paths
                    .Select(path =>
                    {
                        string assetPath = PackageAssetPath(
                            extractPath,
                            path);
                        bool entryPoint = entryPoints.Contains(
                            Path.GetFileName(assetPath));
                        return new PackageChildCandidate(
                            assetPath,
                            assetPath,
                            Path.GetFileNameWithoutExtension(assetPath),
                            targetFramework,
                            entryPoint
                                ? PackageLibraryChildRole.ToolEntryPoint
                                : PackageLibraryChildRole.ToolLibrary);
                    })
                    .OrderBy(static candidate =>
                        candidate.Role
                            == PackageLibraryChildRole.ToolEntryPoint
                                ? 0
                                : 1)
                    .ThenBy(
                        static candidate => candidate.AssetPath,
                        StringComparer.Ordinal),
            ];
        }
        else
        {
            PackageCompileAssetSelection selection =
                ResolvePackageCompileSelection(
                    extractPath,
                    packageName,
                    resolution,
                    options.Tfm);
            if (!selection.IsSelected)
            {
                return PackageChildrenEnvelope(
                    CompileSelectionDocument(subject, selection));
            }

            targetFramework = selection.TargetFramework;
            candidates =
            [
                .. selection.Assets.Select(
                    asset => new PackageChildCandidate(
                        asset.Id,
                        asset.Path,
                        asset.AssemblyName,
                        targetFramework,
                        PackageLibraryChildRole.Compile)),
            ];
        }

        subject = new(
            subject.PackageId,
            subject.PackageVersion,
            targetFramework is null
                ? null
                : new InertText.InertString(
                    InertText.TextPolicy.Field,
                    targetFramework));
        PackageInspectionInput input =
            resolution.AcquiredPayload is { } acquired
                ? PackageInspectionInput.CreateFromPayload(acquired)
                : PackageInspectionInput.CreateLocal(
                    new FileSystemPackageContent(
                        extractPath,
                        resolution.NupkgPath,
                        resolution.FromCache,
                        resolution.ProducerKey ?? packageName),
                    result.PackageName ?? packageName,
                    result.Version ?? version);
        PackageInspectionSelection inspectionSelection =
            input.SelectAssemblies(
                candidates.Select(
                    static candidate =>
                        new PackageInspectionAssembly(
                            candidate.AssetPath,
                            candidate.TargetFramework,
                            candidate.TargetFramework)));

        await using var workspace = new InspectionWorkspace();
        using PackageInspectionAssemblyContext realization =
            await workspace.RealizePackageInspectionAsync(
                    inspectionSelection,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        var outcomes = realization.Assemblies.ToDictionary(
            static outcome => outcome.Selection.Path,
            StringComparer.Ordinal);
        PackageLibraryInspectionTarget[] targets =
        [
            .. candidates.Select(candidate =>
                CreateTarget(
                    candidate,
                    outcomes[candidate.AssetPath])),
        ];
        return await PackageChildrenInspection.ExecuteLibrariesAsync(
                subject,
                targets,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static PackageChildrenDocument CompileSelectionDocument(
        PackageChildrenSubject subject,
        PackageCompileAssetSelection selection) =>
        selection.Status switch
        {
            PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                new(
                    new(
                        subject.PackageId,
                        subject.PackageVersion,
                        selection.TargetFramework is null
                            ? null
                            : new InertText.InertString(
                                InertText.TextPolicy.Field,
                                selection.TargetFramework)),
                    PackageChildrenKind.Libraries,
                    PackageChildrenStatus.SelectedEmpty,
                    [],
                    [],
                    detail: null,
                    isComplete: true),
            PackageCompileAssetSelectionStatus.NoCompileAssets =>
                new(
                    subject,
                    PackageChildrenKind.Libraries,
                    PackageChildrenStatus.NoCompileAssets,
                    [],
                    [],
                    new InertText.InertString(
                        InertText.TextPolicy.Field,
                        "The Package contains no compile Libraries."),
                    isComplete: true),
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework =>
                PackageChildrenDocument.UnavailableLibraries(
                    subject,
                    PackageChildrenStatus.NoApplicableTarget,
                    selection.Message
                        ?? "No compile Library target matches the request."),
            PackageCompileAssetSelectionStatus.InvalidImplementationAssets =>
                PackageChildrenDocument.UnavailableLibraries(
                    subject,
                    PackageChildrenStatus.InvalidSelection,
                    selection.Message
                        ?? "The selected compile Libraries have invalid "
                            + "implementation correspondence."),
            _ => PackageChildrenDocument.UnavailableLibraries(
                subject,
                PackageChildrenStatus.Unavailable,
                selection.Message
                    ?? "The compile Library population is unavailable."),
        };

    private static PackageLibraryInspectionTarget CreateTarget(
        PackageChildCandidate candidate,
        PackageInspectionAssemblyOutcome outcome) =>
        outcome switch
        {
            PackageInspectionAssemblyOutcome.Available available =>
                new(
                    candidate.AssetId,
                    candidate.AssetPath,
                    candidate.Role,
                    available.Group,
                    available.Participant),
            PackageInspectionAssemblyOutcome.WithoutAssembly =>
                PackageLibraryInspectionTarget.CreateUnavailable(
                    candidate.AssetId,
                    candidate.AssetPath,
                    candidate.AssemblyName,
                    candidate.Role,
                    PackageLibraryChildUnavailableReason.NotManagedAssembly,
                    "The selected Package Library is not a managed "
                        + "assembly."),
            PackageInspectionAssemblyOutcome.Unavailable unavailable =>
                PackageLibraryInspectionTarget.CreateUnavailable(
                    candidate.AssetId,
                    candidate.AssetPath,
                    candidate.AssemblyName,
                    candidate.Role,
                    PackageLibraryChildUnavailableReason
                        .AssemblyUnavailable,
                    unavailable.Reason),
            _ => throw new InvalidOperationException(
                "Unknown Package inspection assembly outcome."),
        };

    private static HashSet<string> ToolEntryPoints(
        string extractPath)
    {
        string tools = Path.Combine(extractPath, "tools");
        DotnetToolSettingsData? settings =
            Directory.Exists(tools)
                ? DotnetToolSettingsParser.FindAndParse(tools)
                : null;
        return new(
            settings?.CommandEntries?
                .Select(static command => command.EntryPoint)
                .OfType<string>()
                .Select(Path.GetFileName)
                .Where(static entry => !string.IsNullOrWhiteSpace(entry))
                .Select(static entry => entry!)
                ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    private static string PackageAssetPath(
        string extractPath,
        string path) =>
        Path.GetRelativePath(
                extractPath,
                Path.GetFullPath(path))
            .Replace('\\', '/');

    private static InspectionEnvelope<PackageChildrenDocument>
        PackageChildrenEnvelope(
            PackageChildrenDocument document) =>
        new(
            document,
            new InspectionShare.NonProjectable(
                "package-children/share",
                "Package children do not yet have a canonical Workspace "
                    + "Share projection."));

    private static void WritePackageChildrenTree(
        TextWriter output,
        InspectionResult package,
        PackageChildrenDocument document,
        Verbosity verbosity)
    {
        output.WriteLine(DescribePackageChildrenSubject(
            package,
            document));
        var writer = new MarkoutWriter(
            output,
            new MarkdownFormatter());
        writer.WriteTree(
            [.. PackageChildrenNodes(document, verbosity)]);
    }

    private static string DescribePackageChildrenSubject(
        InspectionResult package,
        PackageChildrenDocument document)
    {
        var context = new List<string>();
        if (package.Source is { } source)
            context.Add(source.ToString());
        if (!string.IsNullOrWhiteSpace(package.ToolFormat))
            context.Add(ShortToolFormat(package.ToolFormat));
        if (document.Subject.TargetFramework is { } framework)
            context.Add(framework.ToString());
        if (package.ContentDirectories is { Count: > 0 } directories)
        {
            context.Add(string.Join(
                ", ",
                directories.Order(StringComparer.Ordinal)));
        }
        if (package.ToolCommands is { Count: > 0 } commands)
        {
            context.Add(
                "command: "
                    + string.Join(
                        ", ",
                        commands.Order(StringComparer.Ordinal)));
        }

        string identity =
            $"{document.Subject.PackageId} "
                + $"{document.Subject.PackageVersion}";
        return context.Count == 0
            ? identity
            : $"{identity} ({string.Join("; ", context)})";
    }

    private static string ShortToolFormat(string format) =>
        format.Contains(
            "Version=\"2\"",
            StringComparison.Ordinal)
            ? "DotNetCliTool v2"
            : format.Contains(
                "Version=\"1\"",
                StringComparison.Ordinal)
                ? "DotNetCliTool v1"
                : format;

    private static List<TreeNode> PackageChildrenNodes(
        PackageChildrenDocument document,
        Verbosity verbosity) =>
        document.Kind switch
        {
            PackageChildrenKind.Libraries =>
                LibraryNodes(document, verbosity),
            PackageChildrenKind.RuntimeIdentifierPackages =>
            [
                new(
                    $"RID packages "
                        + $"({document.RuntimeIdentifierPackages.Length})")
                {
                    Children =
                    [
                        .. document.RuntimeIdentifierPackages.Select(
                            static package =>
                                new TreeNode(
                                    $"{package.RuntimeIdentifier}: "
                                        + $"{package.PackageId}")),
                    ],
                },
            ],
            PackageChildrenKind.NoManagedLibraries =>
            [
                new(
                    "No managed Libraries"
                        + (document.Detail is { } detail
                            ? $" ({detail})"
                            : "")),
            ],
            _ => throw new InvalidOperationException(
                "Unknown Package children kind."),
        };

    private static List<TreeNode> LibraryNodes(
        PackageChildrenDocument document,
        Verbosity verbosity)
    {
        if (document.Libraries.IsEmpty)
        {
            return
            [
                new(
                    document.Status switch
                    {
                        PackageChildrenStatus.SelectedEmpty =>
                            "No Libraries in the selected compile group",
                        PackageChildrenStatus.NoCompileAssets =>
                            "No compile Libraries",
                        PackageChildrenStatus.NoApplicableTarget =>
                            "No applicable Library target",
                        PackageChildrenStatus.InvalidSelection =>
                            "Invalid Library selection",
                        PackageChildrenStatus.Unavailable =>
                            "Library population unavailable",
                        _ => "No Libraries",
                    }
                    + (document.Detail is { } detail
                        ? $" ({detail})"
                        : "")),
            ];
        }

        HashSet<string> duplicateNames =
        [
            .. document.Libraries
                .GroupBy(
                    static library =>
                        library.AssemblyName.ToString(),
                    StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key),
        ];
        PackageLibraryChild[] entryPoints =
        [
            .. document.Libraries.Where(
                static library =>
                    library.Role
                        == PackageLibraryChildRole.ToolEntryPoint),
        ];
        PackageLibraryChild[] dependencies =
        [
            .. document.Libraries.Where(
                static library =>
                    library.Role
                        != PackageLibraryChildRole.ToolEntryPoint),
        ];
        var nodes = new List<TreeNode>(document.Libraries.Length);
        nodes.AddRange(
            entryPoints.Select(
                library => LibraryNode(
                    library,
                    duplicateNames)));
        if (verbosity == Verbosity.Minimal
            && entryPoints.Length > 0
            && dependencies.Length > 8
            && dependencies.All(static library => library.IsAvailable))
        {
            nodes.Add(
                new(
                    $"Dependencies ({dependencies.Length} Libraries)"));
        }
        else
        {
            nodes.AddRange(
                dependencies.Select(
                    library => LibraryNode(
                        library,
                        duplicateNames)));
        }
        return nodes;
    }

    private static TreeNode LibraryNode(
        PackageLibraryChild library,
        IReadOnlySet<string> duplicateNames)
    {
        string assemblyName = library.AssemblyName.ToString();
        string label = duplicateNames.Contains(assemblyName)
            ? library.AssetPath.ToString()
            : assemblyName;
        if (library.Role
            == PackageLibraryChildRole.ToolEntryPoint)
        {
            label += " (entry point)";
        }
        label += library.PublicTypeDeclarations switch
        {
            LibraryTypePopulationCountOutcome.Counted counted =>
                $" ({counted.Total} Type declarations)",
            LibraryTypePopulationCountOutcome.Incomplete incomplete =>
                $" (incomplete: {incomplete.Bound})",
            LibraryTypePopulationCountOutcome.Unavailable unavailable =>
                $" (Count unavailable: {unavailable.Reason})",
            null when library.Unavailable is { } unavailable =>
                $" (unavailable: {unavailable.Detail})",
            _ => " (Count unavailable)",
        };
        return new(label);
    }

    private sealed record PackageChildCandidate(
        string AssetId,
        string AssetPath,
        string AssemblyName,
        string? TargetFramework,
        PackageLibraryChildRole Role);
}
