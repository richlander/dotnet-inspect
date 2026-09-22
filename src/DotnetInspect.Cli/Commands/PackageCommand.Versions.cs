using DotnetInspect.Cli.Models;
using ILInspector.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;
using SemanticRowSelection =
    DotnetInspect.Cli.CommandLine.CliSemanticRowSelection;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using InertText;
using Inspector.Findings;
using Markout;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{
    private static readonly InspectionEnvelopeJsonContract<PackageVersionListingOutcome>
        PackageVersionListingJson =
            new(
                "package-version-listing",
                2,
                PackageVersionListingJsonContext.Default.PackageVersionListingOutcome);

    private static readonly InspectionEnvelopeJsonContract<int>
        PackageVersionCountJson =
            new(
                "package-version-count",
                2,
                PackageVersionCountJsonContext.Default.Int32);

    private static readonly InspectionEnvelopeJsonContract<PackageVersionPopulationOutcome>
        PackageVersionPopulationJson =
            new(
                "package-version-population",
                2,
                PackageVersionPopulationJsonContext.Default.PackageVersionPopulationOutcome);

    private static async Task<int> ExecuteOnlineVersionQueryAsync(
        string packageReference,
        InspectionOptions options,
        CommandContext context)
    {
        PackageVersionRange? range = null;
        string? rangeError = null;
        bool isRange = !File.Exists(packageReference)
            && PackageVersionRange.TryParse(packageReference, out range, out rangeError);
        if (rangeError is not null)
        {
            CommandError.Write(rangeError);
            return 1;
        }

        var (name, requestedVersion) = PackageExtractor.ParsePackageReference(packageReference);
        string packageId = isRange ? range!.PackageId : name.ToLowerInvariant();
        bool latest = !isRange && (options.ForceLatest
            || string.Equals(requestedVersion, "latest", StringComparison.OrdinalIgnoreCase));
        bool pinned = !isRange && !latest
            && !string.IsNullOrEmpty(requestedVersion)
            && (options.Limit == 1 || HasSemanticSingleVersionLimit(options.VersionRowSelection));
        bool ordinaryListing = !pinned && !options.SingleVersionQuery;
        NuGet.Versioning.NuGetVersion? pinnedVersion = null;
        if (pinned && !NuGet.Versioning.NuGetVersion.TryParse(requestedVersion, out pinnedVersion))
        {
            CommandError.Write($"Version '{requestedVersion}' is not an exact NuGet version.");
            return 1;
        }
        if ((latest || isRange || ordinaryListing || options.SingleVersionQuery)
            && PackageCoordinateResolver.Validate(new PackageCoordinate(packageId)) is { } invalid)
        {
            CommandError.Write(
                $"Package '{packageId}' version discovery failed.",
                [invalid.Message, "Correct the package command input and retry."]);
            return 1;
        }

        using var requestScope = RequestTelemetry.Scope($"package {packageId}", "package versions");
        await using DesktopPackageSourceComposition composition = context.CreatePackageSourceComposition();
        if (latest)
        {
            using PackageSourceOperationLease operation = composition.IssueSettlementOperation();
            InspectionEnvelope<PackageVersionSettlementOutcome> envelope =
                await PackageVersionSettlementInspection.ExecuteAsync(
                    new PackageCoordinate(packageId),
                    composition.CreateSettlementHouse(
                        packageId, options.SourceOptions, context.Logger.Log),
                    operation,
                    options.IncludePrerelease);
            return WriteVersionSettlement(envelope, packageReference, options);
        }
        if (isRange)
        {
            using PackageSourceOperationLease operation =
                composition.IssueSettlementOperation();
            InspectionEnvelope<PackageVersionPopulationOutcome> population =
                await PackageVersionPopulationInspection.ExecuteAsync(
                    range!,
                    composition.CreateSettlementHouse(
                        packageId,
                        options.SourceOptions,
                        context.Logger.Log),
                    operation,
                    options.IncludePrerelease,
                    options.IncludeUnlisted);
            return WriteVersionPopulationSettlement(
                population,
                packageReference,
                options);
        }

        {
            using PackageSourceOperationLease operation =
                composition.IssueSettlementOperation();
            InspectionEnvelope<PackageVersionListingOutcome> listing =
                await PackageVersionListingInspection.ExecuteAsync(
                    packageId,
                    composition.CreateSettlementHouse(
                        packageId,
                        options.SourceOptions,
                        context.Logger.Log),
                    operation,
                    pinned || options.IncludePrerelease,
                    pinned || options.IncludeUnlisted);
            if (pinned)
            {
                return WritePinnedVersionListingSettlement(
                    listing,
                    packageId,
                    requestedVersion!,
                    pinnedVersion!,
                    options);
            }
            if (options.SingleVersionQuery)
            {
                return WriteSingleVersionListingSettlement(
                    listing,
                    packageId,
                    packageReference,
                    options);
            }
            return WriteVersionListingSettlement(
                listing,
                packageReference,
                options);
        }
    }

    private static int WriteSingleVersionListingSettlement(
        InspectionEnvelope<PackageVersionListingOutcome> envelope,
        string packageId,
        string packageReference,
        InspectionOptions options)
    {
        string notFoundMessage =
            $"Package '{packageReference}' not found on eligible configured sources.";
        if (envelope.Content
            is PackageVersionListingOutcome.NotAvailable notAvailable)
        {
            return WriteVersionListingFailure(
                notAvailable.Failure,
                notFoundMessage);
        }
        if (envelope.Content
            is not PackageVersionListingOutcome.Listed available)
        {
            throw new InvalidOperationException(
                "Unknown package version listing outcome.");
        }
        if (available.Document.Completeness
            == PackageVersionListingCompleteness.Partial)
        {
            WriteVersionDiscoveryFailureDetails(
                packageId,
                [
                    .. available.AuthorityFailures.Select(value =>
                        (value.Kind, value.Message.ToString())),
                ]);
            return 1;
        }

        return WriteVersionQueryRows(
            [.. available.Document.Versions.Take(1)],
            available.Document.SourceListings,
            options);
    }

    private static int WritePinnedVersionListingSettlement(
        InspectionEnvelope<PackageVersionListingOutcome> envelope,
        string packageId,
        string requestedVersion,
        NuGet.Versioning.NuGetVersion pinnedVersion,
        InspectionOptions options)
    {
        string notFoundMessage =
            $"Version '{requestedVersion}' of package '{packageId}' not found. "
            + "Use --versions to see available versions.";
        if (envelope.Content
            is PackageVersionListingOutcome.NotAvailable notAvailable)
        {
            return WriteVersionListingFailure(
                notAvailable.Failure,
                notFoundMessage);
        }
        if (envelope.Content
            is not PackageVersionListingOutcome.Listed available)
        {
            throw new InvalidOperationException(
                "Unknown package version listing outcome.");
        }

        IReadOnlyList<PackageVersionInfo> listings =
        [
            .. available.Document.Versions.Where(row =>
                NuGet.Versioning.VersionComparer.VersionRelease.Equals(
                    NuGet.Versioning.NuGetVersion.Parse(row.Version),
                    pinnedVersion)),
        ];
        if (listings.Count == 0)
        {
            if (available.AuthorityFailures.Any(failure =>
                    failure.Kind
                        != PackageAuthorityFailureKind.IncompleteMetadata))
            {
                WriteVersionDiscoveryFailureDetails(
                    packageId,
                    [
                        .. available.AuthorityFailures.Select(value =>
                            (value.Kind, value.Message.ToString())),
                    ]);
                return 1;
            }

            CommandError.Write(notFoundMessage);
            return 1;
        }

        WriteVersionListingDiagnostics(envelope, available);
        return WriteVersionQueryRows(
            listings,
            available.Document.SourceListings,
            options);
    }

    private static int WriteVersionListingSettlement(
        InspectionEnvelope<PackageVersionListingOutcome> envelope,
        string packageReference,
        InspectionOptions options)
    {
        WriteVersionListingDiagnostics(
            envelope,
            envelope.Content as PackageVersionListingOutcome.Listed);

        if (options.EnvelopeOutput && !options.Count)
        {
            if (!InspectionEnvelopeOutput.TryWrite(
                    envelope,
                    PackageVersionListingJson,
                    includeEnvelope: true))
            {
                return 1;
            }
            return envelope.Content is PackageVersionListingOutcome.Listed
                ? 0
                : 1;
        }

        if (envelope.Content is PackageVersionListingOutcome.Listed available)
        {
            if (options.Count)
            {
                PackageVersionPopulationCountOutcome count =
                    PackageVersionListingInspection.Count(
                        available.Document,
                        new(
                            options.ListVersionsWithFeed
                                ? PackageVersionPopulationCountCohort
                                    .SourceListings
                                : PackageVersionPopulationCountCohort.Versions,
                            options.VersionRowSelection));
                if (!options.EnvelopeOutput)
                    return WriteVersionCount(count, options);
                if (count
                    is not PackageVersionPopulationCountOutcome.Completed
                        completed)
                {
                    return WriteVersionCount(count, options);
                }

                ProjectionAudit.MarkHonored(ProjectionAudit.Count);
                InspectionEnvelope<int> countEnvelope =
                    PackageVersionListingInspection.ProjectCountEnvelope(
                        envelope,
                        completed);
                return InspectionEnvelopeOutput.TryWrite(
                    countEnvelope,
                    PackageVersionCountJson,
                    includeEnvelope: true)
                        ? 0
                        : 1;
            }
            return WriteVersionQueryRows(
                available.Document.Versions,
                available.Document.SourceListings,
                options);
        }

        if (envelope.Content
            is not PackageVersionListingOutcome.NotAvailable notAvailable)
        {
            throw new InvalidOperationException(
                "Unknown package version listing outcome.");
        }
        return WriteVersionListingFailure(
            notAvailable.Failure,
            $"Package '{packageReference}' not found on eligible configured sources.");
    }

    private static void WriteVersionListingDiagnostics(
        InspectionEnvelope<PackageVersionListingOutcome> envelope,
        PackageVersionListingOutcome.Listed? listed)
    {
        if (listed?.Document.Completeness
            == PackageVersionListingCompleteness.Partial)
        {
            CommandError.WriteWarning(
                $"Version results for package '{listed.Document.Request.PackageId}' are partial.",
                [.. envelope.Diagnostics.Select(
                    diagnostic => diagnostic.Summary.ToString())]);
            return;
        }

        foreach (InspectionDiagnostic diagnostic in envelope.Diagnostics)
            CommandError.WriteWarning(diagnostic.Summary.ToString());
    }

    private static int WriteVersionListingFailure(
        PackageVersionListingFailure failure,
        string notFoundMessage)
    {
        if (failure.Kind == PackageVersionListingFailureKind.NotFound)
        {
            CommandError.Write(notFoundMessage);
            return 1;
        }
        if (!failure.AuthorityFailures.IsEmpty)
        {
            WriteVersionDiscoveryFailureDetails(
                failure.Request.PackageId,
                [
                    .. failure.AuthorityFailures.Select(value =>
                        (value.Kind, value.Message.ToString())),
                ]);
            return 1;
        }

        CommandError.Write(
            $"Package '{failure.Request.PackageId}' version discovery failed.",
            [failure.Reason.ToString()]);
        return 1;
    }

    private static int WriteVersionPopulationSettlement(
        InspectionEnvelope<PackageVersionPopulationOutcome> envelope,
        string packageReference,
        InspectionOptions options)
    {
        foreach (InspectionDiagnostic diagnostic in envelope.Diagnostics)
            CommandError.WriteWarning(diagnostic.Summary.ToString());

        if (options.EnvelopeOutput && !options.Count)
        {
            if (!InspectionEnvelopeOutput.TryWrite(
                    envelope,
                    PackageVersionPopulationJson,
                    includeEnvelope: true))
            {
                return 1;
            }
            return envelope.Content is PackageVersionPopulationOutcome.Populated
                ? 0
                : 1;
        }

        if (envelope.Content is PackageVersionPopulationOutcome.Populated available)
        {
            if (options.Count)
            {
                PackageVersionPopulationCountOutcome count =
                    PackageVersionPopulationInspection.Count(
                        available.Document,
                        new(
                            options.ListVersionsWithFeed
                                ? PackageVersionPopulationCountCohort
                                    .SourceListings
                                : PackageVersionPopulationCountCohort.Versions,
                            options.VersionRowSelection));
                if (!options.EnvelopeOutput)
                    return WriteVersionCount(count, options);
                if (count
                    is not PackageVersionPopulationCountOutcome.Completed
                        completed)
                {
                    return WriteVersionCount(count, options);
                }

                ProjectionAudit.MarkHonored(ProjectionAudit.Count);
                InspectionEnvelope<int> countEnvelope =
                    PackageVersionPopulationInspection.ProjectCountEnvelope(
                        envelope,
                        completed);
                return InspectionEnvelopeOutput.TryWrite(
                    countEnvelope,
                    PackageVersionCountJson,
                    includeEnvelope: true)
                        ? 0
                        : 1;
            }

            IReadOnlyList<PackageVersionInfo> listings =
            [
                .. available.Document.Versions.Select(version =>
                    new PackageVersionInfo(version.Version, version.Listed)),
            ];
            return WriteVersionQueryRows(
                listings,
                available.Document.SourceListings,
                options);
        }

        if (envelope.Content
            is not PackageVersionPopulationOutcome.NotAvailable notAvailable)
        {
            throw new InvalidOperationException(
                "Unknown package version population outcome.");
        }
        PackageVersionPopulationFailure failure = notAvailable.Failure;
        var (displayPackageId, _) =
            PackageExtractor.ParsePackageReference(packageReference);
        if (failure.Kind == PackageVersionPopulationFailureKind.NotFound)
        {
            CommandError.Write(
                $"Package '{packageReference}' not found on eligible configured sources.");
            return 1;
        }

        if (failure.Kind == PackageVersionPopulationFailureKind.NoMatch)
        {
            CommandError.Write(failure.Reason.ToString());
            return 1;
        }

        if (!failure.AuthorityFailures.IsEmpty)
        {
            WriteVersionDiscoveryFailureDetails(
                displayPackageId,
                [
                    .. failure.AuthorityFailures.Select(value =>
                        (value.Kind, value.Message.ToString())),
                ]);
            return 1;
        }

        CommandError.Write(
            $"Package '{displayPackageId}' version discovery failed.",
            [failure.Reason.ToString()]);
        return 1;
    }

    private static int WriteVersionCount(
        PackageVersionPopulationCountOutcome? count,
        InspectionOptions options)
    {
        if (count
            is PackageVersionPopulationCountOutcome.Completed completed)
        {
            string lens = options.ListVersionsWithFeed
                ? "--versions-with-feed"
                : "--versions";
            return LensProjection.TryProject(
                options,
                lens,
                completed.Result.Value,
                out int exit,
                ["Count"])
                    ? exit
                    : throw new InvalidOperationException(
                        "Package version Count was not projected.");
        }

        if (count
            is PackageVersionPopulationCountOutcome.Rejected rejected)
        {
            PackageVersionPopulationCountFailure countFailure =
                rejected.Failure;
            CommandError.Write(
                $"Version row selection stage {countFailure.StageNumber} "
                + $"requires row {countFailure.RequiredPosition}, but "
                + $"{countFailure.AvailableCount} version rows are available.");
            return 1;
        }

        throw new InvalidOperationException(
            "Package version listing did not retain requested Count.");
    }

    private static int WriteVersionQueryRows(
        IReadOnlyList<PackageVersionInfo> listings,
        IReadOnlyList<PackageVersionSourceInfo> sourceListings,
        InspectionOptions options)
    {
        if (options.ListVersionsWithFeed)
        {
            var rowsByVersion = sourceListings.ToLookup(
                row => row.Version, StringComparer.OrdinalIgnoreCase);
            if (!TrySelectVersionRows(
                    listings.SelectMany(row => rowsByVersion[row.Version]).ToList(),
                    options,
                    out IReadOnlyList<PackageVersionSourceInfo> rows))
                return 1;
            if (LensProjection.TryProject(options, "--versions-with-feed",
                    rows.Count, out var exit, VersionFeedColumns(rows, options)))
                return exit;
            OutputFormatter.WriteVersionFeedTable(rows, options, Console.Out);
        }
        else if (options.IncludeUnlisted)
        {
            if (!TrySelectVersionRows(listings, options, out IReadOnlyList<PackageVersionInfo> rows))
                return 1;
            if (LensProjection.TryProject(options, "--versions",
                    rows.Count, out var exit, ["Version", "Listing"]))
                return exit;
            OutputFormatter.WriteVersionListings(rows, options, Console.Out);
        }
        else
        {
            if (!TrySelectVersionRows(
                    listings.Select(row => row.Version).ToList(),
                    options,
                    out IReadOnlyList<string> rows))
                return 1;
            if (LensProjection.TryProject(options, GetVersionQueryLens(options),
                    rows.Count, out var exit, ["Version"]))
                return exit;
            WriteVersions(rows, options);
        }
        return 0;
    }

    private static string GetVersionQueryLens(InspectionOptions options) =>
        options.ForceLatest
            ? "--latest-version"
            : options.SingleVersionQuery
            ? "--version"
            : options.ListVersionsWithFeed
                ? "--versions-with-feed"
                : "--versions";

    private static int WriteVersionSettlement(
        InspectionEnvelope<PackageVersionSettlementOutcome> envelope,
        string packageReference,
        InspectionOptions options)
    {
        foreach (InspectionDiagnostic diagnostic in envelope.Diagnostics)
            CommandError.WriteWarning(diagnostic.Summary.ToString());

        if (envelope.Content is PackageVersionSettlementOutcome.Settled settled)
        {
            return WriteVersionQueryRows(
                settled.Result.Listings, settled.Result.SourceListings, options);
        }

        if (envelope.Content is not PackageVersionSettlementOutcome.NotSettled notSettled)
            throw new InvalidOperationException("Unknown package version settlement outcome.");
        PackageVersionSettlementFailure failure = notSettled.Failure;
        if (failure.Kind is PackageVersionSettlementFailureKind.NotFound
            or PackageVersionSettlementFailureKind.NoMatch)
        {
            CommandError.Write($"Package '{packageReference}' not found on eligible configured sources.");
            return 1;
        }

        if (!failure.AuthorityFailures.IsEmpty)
        {
            WriteVersionDiscoveryFailureDetails(
                failure.Request.PackageId,
                [.. failure.AuthorityFailures.Select(value => (value.Kind, value.Message.ToString()))]);
            return 1;
        }

        CommandError.Write(
            $"Package '{failure.Request.PackageId}' version discovery failed.",
            [failure.Reason.ToString()]);
        return 1;
    }

    private static void WriteVersions(
        IEnumerable<string> versions,
        InspectionOptions options)
    {
        if (options.JsonOutput
            && options.Limit is null)
        {
            Console.Out.WriteLine(
                JsonSerializer.Serialize(
                    versions
                        .Select(version => new VersionJson(version))
                        .ToList(),
                    JsonContext.Default.ListVersionJson));
            return;
        }

        OutputFormatter.WriteStringList(
            versions,
            "Version",
            "Version",
            options.Tsv,
            options.Jsonl,
            Console.Out);
    }

    private static bool TrySelectVersionRows<T>(
        IReadOnlyList<T> rows,
        InspectionOptions options,
        out IReadOnlyList<T> selected)
    {
        if (options.VersionRowSelection is null)
        {
            selected = RowWindow.Apply(options.Rows, rows);
            return true;
        }

        return SemanticRowSelection.TrySelect(
            options.VersionRowSelection,
            rows,
            "Package versions",
            failure =>
                $"Version row selection stage "
                + $"{failure.Failure.StageNumber} requires row "
                + $"{failure.Failure.RequiredPosition}, but "
                + $"{failure.Failure.AvailableCount} version rows are "
                + "available.",
            out selected);
    }

    private static bool HasSemanticSingleVersionLimit(
        RowSelectionIntent<string>? intent) =>
        intent?.Operations.Any(
            static operation =>
                operation.Kind is
                    RowSelectionStageKind.Head
                    or RowSelectionStageKind.Tail
                && operation.Count == 1) == true;

    private static int WriteVersionFeedRows(
        string packageName,
        IReadOnlyList<PackageVersionSourceInfo> rows,
        InspectionOptions options)
    {
        WritePartialVersionFeedWarning(packageName);
        if (!TrySelectVersionRows(
                rows,
                options,
                out IReadOnlyList<PackageVersionSourceInfo> visibleRows))
        {
            return 1;
        }
        if (LensProjection.TryProject(
                options,
                "--versions-with-feed",
                visibleRows.Count,
                out int exitCode,
                VersionFeedColumns(visibleRows, options)))
        {
            return exitCode;
        }

        OutputFormatter.WriteVersionFeedTable(
            visibleRows,
            options,
            Console.Out);
        return 0;
    }

    private static bool PackageVersionsEqual(
        string left,
        string right)
    {
        return NuGet.Versioning.NuGetVersion.TryParse(
                left,
                out var leftVersion)
            && NuGet.Versioning.NuGetVersion.TryParse(
                right,
                out var rightVersion)
            ? NuGet.Versioning.VersionComparer
                .VersionReleaseMetadata
                .Equals(
                    leftVersion,
                    rightVersion)
            : string.Equals(
                left,
                right,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasVersionSourceFailures() =>
        FeedFailureTelemetry.Current?.Failures.Any(
            failure => failure.Phase is
                FeedFailurePhase.PackageSourceDiscovery
                or FeedFailurePhase.PackageVersionList) == true;

    private static void WritePartialVersionFeedWarning(
        string packageName)
    {
        FeedFailure[] failures =
        [
            .. FeedFailureTelemetry.Current?.Failures.Where(
                failure => failure.Phase is
                    FeedFailurePhase.PackageSourceDiscovery
                    or FeedFailurePhase.PackageVersionList)
                ?? [],
        ];
        if (failures.Length == 0)
            return;

        CommandError.WriteWarning(
            $"Version results for package '{packageName}' are partial.",
            [
                .. failures.Select(
                    static failure => failure.Kind switch
                    {
                        FeedFailureKind.Authentication =>
                            $"{failure.Url} — source requires credentials while {failure.PhaseText}.",
                        FeedFailureKind.Authorization =>
                            $"{failure.Url} — source denied access while {failure.PhaseText}.",
                        _ =>
                            $"{failure.Url} — {failure.StatusText} while {failure.PhaseText}.",
                    }),
            ]);
    }

    private static void WriteVersionLookupFailure(
        string packageName,
        string notFoundMessage)
    {
        var sourceFailure =
            FeedFailureTelemetry.Current?.DescribeFailure(packageName);
        CommandError.Write(sourceFailure?.ToString() ?? notFoundMessage);
    }

    private static void WriteVersionDiscoveryFailure(
        string packageName,
        IReadOnlyList<PackageAuthorityFailure> failures) =>
        WriteVersionDiscoveryFailureDetails(
            packageName, [.. failures.Select(failure => (failure.Kind, failure.Message))]);

    private static void WriteVersionDiscoveryFailureDetails(
        string packageName,
        IReadOnlyList<(PackageAuthorityFailureKind Kind, string Message)> failures)
    {
        var kinds = failures
            .Select(failure => failure.Kind)
            .ToHashSet();
        var remediation = new List<string>();
        if (kinds.Contains(PackageAuthorityFailureKind.Input))
        {
            remediation.Add(
                "Correct the package command input and retry.");
        }
        if (kinds.Contains(PackageAuthorityFailureKind.Configuration))
        {
            remediation.Add(
                "Correct the package source configuration and retry.");
        }
        if (kinds.Contains(PackageAuthorityFailureKind.AuthenticationRequired))
        {
            remediation.Add(
                "Supply credentials for the source and retry.");
        }
        if (kinds.Contains(PackageAuthorityFailureKind.Unsupported))
        {
            remediation.Add(
                "Use a package source that supports version enumeration in this host.");
        }
        if (kinds.Contains(PackageAuthorityFailureKind.IncompleteMetadata))
        {
            remediation.Add(
                "Retry to obtain complete package version metadata.");
        }
        if (kinds.Contains(PackageAuthorityFailureKind.Timeout))
        {
            remediation.Add(
                "Retry, or increase --http-timeout for a slow package source.");
        }
        if (kinds.Contains(PackageAuthorityFailureKind.InvalidResponse)
            || kinds.Contains(PackageAuthorityFailureKind.ResponseRejected))
        {
            remediation.Add(
                "Check the package source metadata and configuration before retrying.");
        }
        if (kinds.Contains(PackageAuthorityFailureKind.Transport))
        {
            remediation.Add(
                "Check the package source location, access permissions, and connectivity before retrying.");
        }

        CommandError.Write(
            $"Package '{packageName}' version discovery failed.",
            [
                .. failures.Select(failure => failure.Message),
                .. remediation,
            ]);
    }
}
