using System.Collections.Immutable;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.Research;
using NuGetFetch;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspect.Cli.Commands;

public partial class LibraryCommand
{
    private static async Task<int> ExecutePackageAddressAsync(
        PackageReferenceTarget target,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline,
        Verbosity userVerbosity,
        bool discoveryInspection,
        bool fullEffectiveDiscovery,
        HashSet<string>? discoveryExecutionScope,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.AssemblyName))
        {
            CommandError.Write(
                "Package-backed library address requires one exact Library.");
            return 1;
        }

        LibraryAddressIntent intent;
        try
        {
            intent = CreateAddressIntent(options);
        }
        catch (ArgumentException failure)
        {
            CommandError.Write(failure.Message);
            return 1;
        }

        ILOffsetProjectionCapabilities capabilities =
            intent switch
            {
                LibraryAddressIntent.IlPoint point =>
                    point.Capabilities,
                LibraryAddressIntent.Population population =>
                    population.Capabilities,
                _ => ILOffsetProjectionCapabilities.None,
            };
        PackageHouseLibraryCompanionDemand companionDemand =
            (capabilities
                & ILOffsetProjectionCapabilities.SourceLocation) != 0
                ? PackageHouseLibraryCompanionDemand
                    .ImplementationPortablePdb
                : PackageHouseLibraryCompanionDemand.None;
        string requestedLibraryPath =
            options.AssemblyName.Replace('\\', '/');
        if (!requestedLibraryPath.EndsWith(
                ".dll",
                StringComparison.OrdinalIgnoreCase))
        {
            requestedLibraryPath += ".dll";
        }
        if (!TryGetPackageCompileLibrarySelection(
                requestedLibraryPath,
                out string? pathTargetFramework))
        {
            throw new InvalidOperationException(
                "Package-backed Library Address execution requires "
                + "an admitted compile Library selection.");
        }
        string targetFramework =
            options.Tfm
            ?? pathTargetFramework
            ?? TfmResolver.ExtractTfmFromPath(requestedLibraryPath)
            ?? TraversalTargetFrameworkPolicy
                .ProductDefaultTargetFramework;
        var librarySelector =
            new PackageLibrarySelector(requestedLibraryPath);

        using var stores =
            new DesktopPackageStoreScope("inspect-cli-address");
        PackageLibraryRealizationResult? library;
        if (target.IsLocalFile)
        {
            library = await RealizeLocalPackageAddressAsync(
                    target,
                    targetFramework,
                    librarySelector,
                    companionDemand,
                    stores,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            library = await RealizeConfiguredPackageAddressAsync(
                    target,
                    targetFramework,
                    librarySelector,
                    companionDemand,
                    stores,
                    options,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (library is null)
            return 1;

        if (library is not PackageLibraryRealizationResult.Realized realized)
        {
            if (library.Status
                is PackageLibraryRealizationStatus.Missing
                    or PackageLibraryRealizationStatus.Ambiguous)
            {
                CommandError.Write(
                    library.Status == PackageLibraryRealizationStatus.Missing
                        ? $"Library '{options.AssemblyName}' was not selected "
                            + $"from package '{target.PackageName}'."
                        : $"Library '{options.AssemblyName}' selected more "
                            + "than one package Library.");
            }
            else
            {
                WritePackageHouseFailure(
                    library.Settlement.Result,
                    options.AssemblyName);
            }
            return 1;
        }

        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope =
            await PackageLibraryAddressInspection.ExecuteAsync(
                    realized.Acquired,
                    realized.Handoff,
                    intent,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        return await RenderPackageAddressAsync(
                envelope,
                realized.Handoff,
                realized.Realization.Selection.TargetFramework,
                options,
                pipeline,
                userVerbosity,
                discoveryInspection,
                fullEffectiveDiscovery,
                discoveryExecutionScope)
            .ConfigureAwait(false);
    }

    private static LibraryAddressIntent CreateAddressIntent(
        LibraryOptions options)
    {
        LibraryOptions capabilityOptions =
            options.AddressRequest
                    is LibraryAddressRequest.FilePopulation
                && options.Discover is null
                && options.IncludeSections is not { Count: > 0 }
                    ? options with
                    {
                        IncludeSections =
                            [.. BatchCoordinateSections],
                    }
                    : options;
        ILOffsetProjectionCapabilities capabilities =
            ILOffsetQuery.ProjectionCapabilities(capabilityOptions);
        return options.AddressRequest switch
        {
            LibraryAddressRequest.IlPoint point =>
                new LibraryAddressIntent.IlPoint(
                    point.MethodToken,
                    point.ILOffset,
                    capabilities,
                    options.PreferRenderedUrls),
            LibraryAddressRequest.HeapPoint point =>
                new LibraryAddressIntent.HeapPoint(
                    options.MetadataRoot,
                    point.Heap,
                    point.Address),
            LibraryAddressRequest.FilePopulation
                {
                    Population: { } population,
                } =>
                new LibraryAddressIntent.Population(
                    population.Records.Select(
                        ConvertAddressRecord),
                    capabilities,
                    options.PreferRenderedUrls,
                    allowNonBoundaryContextAbsence:
                        options.Discover is not null,
                    query:
                        CreateAddressPopulationQuery(options)),
            _ => throw new InvalidOperationException(
                "Package-backed Library Address execution requires an admitted intent."),
        };
    }

    private static QuerySpaceRequest CreateAddressPopulationQuery(
        LibraryOptions options)
    {
        RowSelectionIntent<string> rows =
            options.Discover is not null
                ? RowSelectionIntent<string>.Empty
                : options.AddressRowSelection
                    ?? RowSelectionIntent<string>.Empty;
        QuerySpaceTerminalRequirement terminal =
            options.Discover is not null || !options.Count
                ? QuerySpaceTerminalRequirement.Rows
                : IsSingleHeadOne(rows)
                    ? QuerySpaceTerminalRequirement.Exists
                    : QuerySpaceTerminalRequirement.Count;
        return LibraryAddressPopulationQuery.CreateRequest(
            rows,
            terminal);
    }

    private static bool IsSingleHeadOne(
        RowSelectionIntent<string> rows)
    {
        if (rows.Operations.Count != 1)
            return false;
        RowSelectionIntentOperation<string> operation =
            rows.Operations[0];
        return operation.Kind is RowSelectionStageKind.Head
            && operation.Count == 1;
    }

    private static async Task<PackageLibraryRealizationResult?>
        RealizeConfiguredPackageAddressAsync(
        PackageReferenceTarget target,
        string targetFramework,
        PackageLibrarySelector librarySelector,
        PackageHouseLibraryCompanionDemand companionDemand,
        DesktopPackageStoreScope stores,
        LibraryOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        await using DesktopPackageSourceComposition composition =
            context.CreatePackageSourceComposition();
        try
        {
            return DotnetInspector.Packages.PackageExtractor
                    .TryNormalizePackageVersion(
                    target.Version,
                    out string pinnedVersion)
                ? await composition.RealizeLibraryAsync(
                        PackageSourceCoordinate.Create(
                            target.PackageName,
                            pinnedVersion),
                        targetFramework,
                        librarySelector,
                        PackageLibraryRealizationDepth.Implementation,
                        stores.Get,
                        options.SourceOptions,
                        context.Logger.Log,
                        cancellationToken,
                        companionDemand: companionDemand)
                    .ConfigureAwait(false)
                : await composition.RealizeSelectedLibraryAsync(
                        target.PackageName,
                        string.IsNullOrWhiteSpace(target.Version)
                            ? null
                            : target.Version,
                        targetFramework,
                        librarySelector,
                        PackageLibraryRealizationDepth.Implementation,
                        stores.Get,
                        options.SourceOptions,
                        context.Logger.Log,
                        options.IncludePrerelease,
                        cancellationToken: cancellationToken,
                        companionDemand: companionDemand)
                    .ConfigureAwait(false);
        }
        catch (ArgumentException failure)
        {
            CommandError.Write(failure.Message);
            return null;
        }
    }

    private static async Task<PackageLibraryRealizationResult?>
        RealizeLocalPackageAddressAsync(
        PackageReferenceTarget target,
        string targetFramework,
        PackageLibrarySelector librarySelector,
        PackageHouseLibraryCompanionDemand companionDemand,
        DesktopPackageStoreScope stores,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(target.OriginalArgument);
        if (!file.Exists)
        {
            CommandError.Write(
                $"Package archive not found: {target.OriginalArgument}");
            return null;
        }
        if (file.Length
            > ExactPackageArchiveSourceOptions.DefaultMaxPackageBytes)
        {
            CommandError.Write(
                "The package archive exceeds its configured byte limit.");
            return null;
        }

        byte[] content =
            await File.ReadAllBytesAsync(
                    file.FullName,
                    cancellationToken)
                .ConfigureAwait(false);
        ExactPackageArchiveSourceAdmission admission =
            await PackageSourceClientFactory.CreateExactArchiveAsync(
                    content,
                    PackageSourceAssociation.Create(),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is ExactPackageArchiveSourceAdmission.Rejected rejected)
        {
            CommandError.Write(rejected.Failure.Message);
            return null;
        }

        var available =
            (ExactPackageArchiveSourceAdmission.Available)admission;
        using IPackageSourceClient client = available.Client;
        var source = new PackageSource(
            "exact archive",
            file.FullName);
        var fetch = new NuGetFetchOptions();
        var operation = PackageHouseOperation.Create(
            PackageHouseOperationProfile.Realize,
            fetch.RequestTimeout,
            fetch.OperationTimeout);
        var request = new PackageLibraryRealizationRequest(
            new PackageHouseDemand.Exact(available.Coordinate),
            targetFramework,
            librarySelector,
            PackageLibraryRealizationDepth.Implementation,
            operation,
            companionDemand);
        await using PackageSourceSettlementLease sourceLease =
            PackageSourceSettlementService.IssueLease(
                authority =>
                    ReferenceEquals(
                        authority.Association,
                        client.Source.Association)
                        ? client
                        : throw new InvalidOperationException(
                            "The exact archive request selected another source."));
        using PackageSourceOperationLease sourceOperation =
            sourceLease.IssueOperationLease(
                cancellationToken,
                operation.RequestTimeout,
                operation.OperationTimeout);
        return await PackageLibraryRealization.ExecuteAsync(
                request,
                new SingleAddressSourceAuthorization(
                    available.Coordinate.PackageId,
                    PackageSourceAuthorization.Authorize(
                        source,
                        client.Source.Association)),
                new PackageLibraryRealizationPlan(
                    stores.Get,
                    log: context.Logger.Log),
                sourceOperation)
            .ConfigureAwait(false);
    }

    private static LibraryAddressPopulationRecord ConvertAddressRecord(
        ILCoordinatePopulationRecord record) =>
        record switch
        {
            ILCoordinatePopulationRecord.Coordinate value =>
                new LibraryAddressPopulationRecord.Coordinate(
                    value.LineNumber,
                    value.Value,
                    value.Label,
                    value.MethodToken,
                    value.ILOffset),
            ILCoordinatePopulationRecord.Malformed value =>
                new LibraryAddressPopulationRecord.Malformed(
                    value.LineNumber,
                    value.Label,
                    value.Error),
            _ => throw new InvalidOperationException(
                "Unknown CLI Library Address population record."),
        };

    private static async Task<int> RenderPackageAddressAsync(
        InspectionEnvelope<LibraryAddressInspectionOutcome> envelope,
        PackageHouseLibraryHandoff.Compile handoff,
        string? targetFramework,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline,
        Verbosity userVerbosity,
        bool discoveryInspection,
        bool fullEffectiveDiscovery,
        HashSet<string>? discoveryExecutionScope)
    {
        LibraryAddressDocument? document =
            envelope.Content switch
            {
                LibraryAddressInspectionOutcome.Completed completed =>
                    completed.Document,
                LibraryAddressInspectionOutcome.Partial partial =>
                    partial.Document,
                _ => null,
            };
        if (document is null)
        {
            WriteAddressDiagnostics(envelope.Diagnostics);
            return 1;
        }

        bool hasErrors = WriteAddressDiagnostics(
            envelope.Diagnostics,
            suppressPopulationRowErrors:
                document is LibraryAddressDocument.Population
                && !discoveryInspection);

        var inspection = new LibraryInspection
        {
            FileName = Path.GetFileName(handoff.Asset.Path),
            Tfm = targetFramework,
            Source = SourceKind.NuGet,
        };
        if (document
            is LibraryAddressDocument.Population population)
        {
            if (population.Result.Terminal
                is QuerySpaceTerminalRequirement.Count
                    or QuerySpaceTerminalRequirement.Exists)
            {
                int count =
                    population.Result.Terminal
                        is QuerySpaceTerminalRequirement.Count
                        ? population.Result.Count!.Value
                        : population.Result.Exists!.Value
                            ? 1
                            : 0;
                if (!LensProjection.TryProject(
                        options with { Rows = null },
                        "library address --file",
                        count,
                        out int projectionExitCode,
                        [
                            "Coordinate",
                            "Label",
                            "Member",
                            "IL Offset",
                            "Meaning",
                            "Evidence",
                        ]))
                {
                    throw new InvalidOperationException(
                        "A scalar Library Address population requires a count projection.");
                }
                return hasErrors || !population.Result.IsComplete
                    ? Math.Max(1, projectionExitCode)
                    : projectionExitCode;
            }

            if (discoveryInspection)
            {
                if (hasErrors || !population.Result.IsComplete)
                    return 1;
                List<ILOffsetProjection> projections =
                [
                    .. population.Result.Rows
                        .OfType<
                            LibraryAddressPopulationRow.Resolved>()
                        .Select(
                            static row => row.Projection),
                ];
                if (projections.Count == 0)
                {
                    CommandError.Write(
                        "Coordinate file contains no resolvable records "
                        + "for effective discovery.");
                    return 1;
                }
                inspection.ILOffset =
                    MergeILCoordinateProjectionsForDiscovery(
                        projections);
            }
            else
            {
                List<ILCoordinateBatchRow> rows =
                [
                    .. population.Result.Rows.Select(
                        static row => row switch
                        {
                            LibraryAddressPopulationRow.Resolved resolved =>
                                BuildILCoordinateBatchRow(
                                    new ILCoordinatePopulationRecord.Coordinate(
                                        resolved.LineNumber,
                                        resolved.Value,
                                        resolved.Label,
                                        resolved.MethodToken,
                                        resolved.ILOffset),
                                    resolved.Projection),
                            LibraryAddressPopulationRow.Malformed malformed =>
                                new ILCoordinateBatchRow(
                                    null,
                                    malformed.Label,
                                    null,
                                    null,
                                    "error",
                                    malformed.Error),
                            LibraryAddressPopulationRow.Unresolved unresolved =>
                                new ILCoordinateBatchRow(
                                    unresolved.Value,
                                    unresolved.Label,
                                    null,
                                    null,
                                    "error",
                                    ILOffsetQuery.FormatFailure(
                                        unresolved.Failure)),
                            _ => throw new InvalidOperationException(
                                "Unknown Library Address population row."),
                        }),
                ];
                int result = WriteAddressPopulation(rows, options);
                return hasErrors || !population.Result.IsComplete
                    ? Math.Max(1, result)
                    : result;
            }
        }
        else
        {
            switch (document)
            {
                case LibraryAddressDocument.IlPoint
                    {
                        Result: LibraryIlAddressOutcome.Resolved resolved,
                    }:
                    inspection.ILOffset = resolved.Projection;
                    break;
                case LibraryAddressDocument.HeapPoint
                    {
                        Result: LibraryHeapAddressOutcome.Resolved resolved,
                    }:
                    inspection.MetadataHeap = new(
                        resolved.Heap,
                        resolved.Address,
                        resolved.Value);
                    break;
                default:
                    return 1;
            }
        }

        if (discoveryInspection)
        {
            bool sourceLinkAvailable =
                inspection.ILOffset?.File is not null;
            return WriteEffectiveSections(
                handoff.Asset.Path,
                inspection,
                options,
                pipeline,
                userVerbosity,
                fullEffectiveDiscovery,
                discoveryExecutionScope,
                sourceLinkAvailable,
                cache: false,
                inspectedContentHash: null);
        }
        if (options.Print)
        {
            return await WriteLibraryPrintProjectionAsync(
                    inspection,
                    options)
                .ConfigureAwait(false);
        }
        if (options.Value || options.Urls || options.Paths)
            return WriteLibraryShapeProjection(inspection, options);
        if (RejectEmptyExactSection(inspection, options, pipeline))
            return 1;
        WarnEmptySections(inspection, options, pipeline);
        if (ProjectionAudit.RejectUnloweredJson(
                options,
                options.JsonOutput))
        {
            return 1;
        }

        OutputFormatter.WriteLibraryResult(
            inspection,
            options,
            pipeline);
        return hasErrors ? 1 : 0;
    }

    private static int WriteAddressPopulation(
        List<ILCoordinateBatchRow> rows,
        LibraryOptions options)
    {
        int rowExitCode =
            rows.Any(static row => row.Meaning == "error")
                ? 1
                : 0;
        if (LensProjection.TryProject(
                options,
                "library address --file",
                rows.Count,
                out int projectionExitCode,
                [
                    "Coordinate",
                    "Label",
                    "Member",
                    "IL Offset",
                    "Meaning",
                    "Evidence",
                ]))
        {
            return projectionExitCode != 0
                ? projectionExitCode
                : rowExitCode;
        }
        if (ProjectionAudit.RejectUnloweredJson(
                options,
                options.JsonOutput))
        {
            return 1;
        }

        WriteILCoordinateBatchRows(
            rows,
            options with { Rows = null });
        return rowExitCode;
    }

    private static bool WriteAddressDiagnostics(
        IEnumerable<InspectionDiagnostic> diagnostics,
        bool suppressPopulationRowErrors = false)
    {
        bool hasErrors = false;
        foreach (InspectionDiagnostic diagnostic in diagnostics)
        {
            if (suppressPopulationRowErrors
                && (diagnostic.Code.Equals(
                        "library-address.population.malformed",
                        StringComparison.Ordinal)
                    || diagnostic.Code.StartsWith(
                        "library-address.il.",
                        StringComparison.Ordinal)))
            {
                continue;
            }

            string message = diagnostic.Correspondence is { } correspondence
                ? $"{diagnostic.Summary} {correspondence}"
                : diagnostic.Summary.ToString();
            switch (diagnostic.Severity)
            {
                case InspectionDiagnosticSeverity.Information:
                    CommandError.WriteNote(message);
                    break;
                case InspectionDiagnosticSeverity.Warning:
                    CommandError.WriteWarning(message);
                    break;
                case InspectionDiagnosticSeverity.Error:
                    hasErrors = true;
                    CommandError.Write(message);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Library Address diagnostic severity.");
            }
        }
        return hasErrors;
    }

    private static void WritePackageHouseFailure(
        PackageHouseResult result,
        string library)
    {
        string message = result switch
        {
            PackageHouseResult.NotFound value =>
                value.Reason.ToString(),
            PackageHouseResult.NoMatch value =>
                value.Reason.ToString(),
            PackageHouseResult.Ambiguous value =>
                value.Reason.ToString(),
            PackageHouseResult.Rejected value =>
                value.Reason.ToString(),
            PackageHouseResult.Unavailable value =>
                value.Reason.ToString(),
            PackageHouseResult.Incomplete value =>
                value.Reason.ToString(),
            _ => "PackageHouse did not produce a Library Address settlement.",
        };
        CommandError.Write(
            $"Library '{library}': {message}");
        var written = new HashSet<string>(
            [message],
            StringComparer.Ordinal);
        foreach (PackageHouseFailure failure in result.Evidence.Failures)
        {
            string? detail = failure switch
            {
                PackageHouseFailure.Authority authority =>
                    FormatPackageAuthorityFailure(
                        authority.Failure),
                PackageHouseFailure.Stage stage =>
                    stage.Reason.ToString(),
                _ => null,
            };
            if (detail is not null && written.Add(detail))
                CommandError.Write(detail);
        }
    }

    private static string FormatPackageAuthorityFailure(
        PackageAuthorityFailure failure)
    {
        string authority = failure.Authority.ToString();
        return authority.Length == 0
            ? failure.Message
            : $"{authority}: {failure.Message}";
    }

    private sealed class SingleAddressSourceAuthorization(
        string packageId,
        PackageSourceAuthorization authorization)
        : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string requestedPackageId) =>
            requestedPackageId.Equals(
                packageId,
                StringComparison.OrdinalIgnoreCase)
                ? authorization
                : PackageSourceAuthorization.Deny(
                    "The exact archive source serves one package ID.");
    }
}
