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
using DotnetInspector.RowSelection;
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
        NuGet.Versioning.NuGetVersion? pinnedVersion = null;
        if (pinned && !NuGet.Versioning.NuGetVersion.TryParse(requestedVersion, out pinnedVersion))
        {
            CommandError.Write($"Version '{requestedVersion}' is not an exact NuGet version.");
            return 1;
        }
        if (latest
            && PackageCoordinateResolver.Validate(new PackageCoordinate(packageId)) is { } invalid)
        {
            CommandError.Write(
                $"Package '{packageId}' version discovery failed.",
                [invalid.Message, "Correct the package command input and retry."]);
            return 1;
        }

        using var requestScope = RequestTelemetry.Scope($"package {packageId}", "package versions");
        await using DesktopPackageSourceComposition composition = context.CreatePackageSourceComposition();
        PackageVersionDiscoveryResult discovery;
        string? selectedVersion = null;
        if (latest)
        {
            PackageHouseResult settlement = await composition.SettleVersionAsync(
                new PackageVersionSelectionRequest.AlwaysLatest(
                    packageId, options.IncludePrerelease),
                options.SourceOptions,
                context.Logger.Log);
            if (settlement is not PackageHouseResult.Settled)
            {
                WriteVersionSettlementFailure(packageId, packageReference, settlement);
                return 1;
            }
            if (settlement.Decision?.VersionResolution
                is not PackageVersionResolutionReceipt.Resolved resolved)
            {
                throw new InvalidOperationException(
                    "Latest-version settlement did not retain its resolved version receipt.");
            }
            discovery = resolved.Discovery;
            selectedVersion = resolved.Coordinate.Version;
        }
        else
        {
            discovery = await composition.GetVersionsAsync(
                packageId,
                pinned || options.IncludePrerelease || (range?.IncludesPrerelease ?? false),
                options.VersionRowSelection is not null || isRange || pinned
                    ? null : options.Limit,
                options.SourceOptions,
                context.Logger.Log,
                includeUnlisted: pinned || options.IncludeUnlisted);
        }

        bool requiresCompleteEvidence = isRange
            || (!pinned && options.SingleVersionQuery);
        if (discovery.State == PackageVersionDiscoveryState.Failed
            || (requiresCompleteEvidence && discovery.State != PackageVersionDiscoveryState.Authoritative))
        {
            WriteVersionDiscoveryFailure(packageId, discovery.Failures);
            return 1;
        }

        IReadOnlyList<PackageVersionInfo> listings = discovery.Listings;
        if (selectedVersion is not null)
            listings = [.. listings.Where(row => PackageVersionsEqual(row.Version, selectedVersion))];
        if (pinned)
        {
            listings = [.. listings.Where(row =>
                NuGet.Versioning.VersionComparer.VersionRelease.Equals(
                    NuGet.Versioning.NuGetVersion.Parse(row.Version), pinnedVersion))];
            if (listings.Count == 0
                && discovery.Failures.Any(failure => failure.Kind != PackageAuthorityFailureKind.IncompleteMetadata))
            {
                WriteVersionDiscoveryFailure(packageId, discovery.Failures);
                return 1;
            }
        }

        if (!discovery.HasAnyCandidate || ((pinned || latest) && listings.Count == 0))
        {
            CommandError.Write(pinned
                ? $"Version '{requestedVersion}' of package '{packageId}' not found. Use --versions to see available versions."
                : $"Package '{packageReference}' not found on eligible configured sources.");
            return 1;
        }

        if (isRange)
        {
            try
            {
                listings = [.. PackageVersionVector.CreateListingAware(
                    range!, listings, options.IncludePrerelease)
                    .Take(options.Limit ?? int.MaxValue)];
            }
            catch (ArgumentException exception)
            {
                CommandError.Write(exception.Message);
                return 1;
            }
        }

        if (discovery.State == PackageVersionDiscoveryState.Partial)
        {
            CommandError.WriteWarning(
                $"Version results for package '{packageId}' are partial.",
                [.. discovery.Failures.Select(failure => failure.Message)]);
        }

        if (options.ListVersionsWithFeed)
        {
            var rowsByVersion = discovery.SourceListings.ToLookup(
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
            if (LensProjection.TryProject(options, latest ? "--latest-version" : "--versions",
                    rows.Count, out var exit, ["Version"]))
                return exit;
            WriteVersions(rows, options);
        }
        return 0;
    }

    private static void WriteVersionSettlementFailure(
        string packageName,
        string packageReference,
        PackageHouseResult result)
    {
        if (result is PackageHouseResult.NotFound or PackageHouseResult.NoMatch)
        {
            CommandError.Write($"Package '{packageReference}' not found on eligible configured sources.");
            return;
        }

        PackageAuthorityFailure[] failures =
        [
            .. result.Evidence.Failures
                .OfType<PackageHouseFailure.Authority>()
                .Select(failure => failure.Failure),
        ];
        if (failures.Length != 0)
        {
            WriteVersionDiscoveryFailure(packageName, failures);
            return;
        }

        string reason = result switch
        {
            PackageHouseResult.Ambiguous ambiguous => ambiguous.Reason.ToString(),
            PackageHouseResult.Rejected rejected => rejected.Reason.ToString(),
            PackageHouseResult.Unavailable unavailable => unavailable.Reason.ToString(),
            PackageHouseResult.Incomplete incomplete => incomplete.Reason.ToString(),
            PackageHouseResult.Failed failed => failed.Reason.ToString(),
            _ => throw new InvalidOperationException(
                "Version settlement returned an unexpected outcome."),
        };
        CommandError.Write($"Package '{packageName}' version discovery failed.", [reason]);
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
        IReadOnlyList<PackageAuthorityFailure> failures)
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
