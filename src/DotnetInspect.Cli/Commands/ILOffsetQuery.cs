using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using ILInspector.CSharp;
using ILInspector.Metadata;
using ILInspector.Research;
using System.Diagnostics;
using System.Globalization;

namespace DotnetInspect.Cli.Commands;

internal static class ILOffsetQuery
{
    internal const int MaximumCoordinatePopulation = 1024;

    internal static Task<(int ExitCode, ILOffsetProjection? Result)> ResolveAsync(
        SourceLinkService service,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger) =>
        ResolveAsync(
            service,
            packageName,
            packageVersion,
            isPlatformAssembly,
            options,
            httpClient,
            logger,
            writeErrors: true);

    internal static async Task<(int ExitCode, ILOffsetProjection? Result, string? Error)> ResolveBatchAsync(
        SourceLinkService service,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        var (exitCode, result) = await ResolveAsync(
            service,
            packageName,
            packageVersion,
            isPlatformAssembly,
            options,
            httpClient,
            logger,
            writeErrors: false);
        return (exitCode, result, exitCode == 0 ? null : "could not resolve");
    }

    static async Task<(int ExitCode, ILOffsetProjection? Result)> ResolveAsync(
        SourceLinkService service,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger,
        bool writeErrors)
    {
        if (options.CoordinateRequest
            is not LibraryCoordinateRequest.IlPoint coordinate)
        {
            throw new UnreachableException(
                "IL coordinate resolution requires an admitted IL point.");
        }

        var capabilities = ProjectionCapabilities(options);
        if ((capabilities & ILOffsetProjectionCapabilities.SourceLocation) != 0)
        {
            await SourceEnricher.AcquirePdbAsync(
                service.Context,
                httpClient,
                packageName,
                packageVersion,
                isPlatformAssembly,
                logger.Log,
                sourceOptions: options.SourceOptions);

            if (service.HasPdb && !service.HasSourceLink)
                logger.LogWarning("No SourceLink information found. URLs will not be available.");
        }

        var outcome = ResearchViews.ProjectILOffset(new ILOffsetProjectionRequest(
            service,
            coordinate.MethodToken,
            coordinate.ILOffset,
            capabilities,
            options.BrowsableUrls,
            logger.Log));
        if (!outcome.Succeeded)
        {
            var failure = outcome.Failure!;
            if (failure.Kind == ILOffsetProjectionFailureKind.SourceUnavailable
                && !service.HasPdb)
            {
                if (writeErrors)
                    WritePdbWarning(service.Context);
            }
            else
            {
                WriteError(writeErrors, $"{failure.Message}");
                if (failure.Detail is { Length: > 0 } detail)
                    WriteError(writeErrors, detail);
            }
            return (1, null);
        }

        return (0, outcome.Projection);
    }

    static ILOffsetProjectionCapabilities ProjectionCapabilities(LibraryOptions options)
    {
        var capabilities = ILOffsetProjectionCapabilities.None;
        if (RequiresSourceLocation(options))
            capabilities |= ILOffsetProjectionCapabilities.SourceLocation;
        if (RequiresInstructionContext(options))
            capabilities |= ILOffsetProjectionCapabilities.InstructionContext;
        if (RequiresExceptionContext(options))
            capabilities |= ILOffsetProjectionCapabilities.ExceptionContext;
        if (RequiresCallsiteContext(options))
            capabilities |= ILOffsetProjectionCapabilities.CallsiteContext;
        if (RequiresReturnAddressContext(options))
            capabilities |= ILOffsetProjectionCapabilities.ReturnAddressContext;
        if (RequiresAllocationContext(options))
            capabilities |= ILOffsetProjectionCapabilities.AllocationContext;
        if (RequiresSafetyContext(options))
            capabilities |= ILOffsetProjectionCapabilities.SafetyContext;
        if (RequiresCostContext(options))
            capabilities |= ILOffsetProjectionCapabilities.CostContext;
        return capabilities;
    }

    static void WriteError(bool enabled, string message)
    {
        if (enabled)
            CommandError.Write(message);
    }

    static bool RequiresSourceLocation(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.ILOffset) == true;

    static bool RequiresInstructionContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.InstructionContext) == true;

    static bool RequiresExceptionContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.ExceptionContext) == true;

    static bool RequiresCallsiteContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.CallsiteContext) == true;

    static bool RequiresReturnAddressContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.ReturnAddressContext) == true;

    static bool RequiresAllocationContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.AllocationContext) == true;

    static bool RequiresSafetyContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.SafetyContext) == true;

    static bool RequiresCostContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.CostContext) == true;

    public static bool TryParse(string value, out int methodToken, out int ilOffset)
    {
        methodToken = 0;
        ilOffset = 0;

        var plusIndex = value.IndexOf('+');
        if (plusIndex <= 0 || plusIndex >= value.Length - 1)
            return false;

        var tokenPart = value[..plusIndex];
        var offsetPart = value[(plusIndex + 1)..];

        if (!TryParseHexInt(tokenPart, out methodToken))
            return false;
        if (!TryParseHexInt(offsetPart, out ilOffset))
            return false;

        return (methodToken & unchecked((int)0xFF000000)) == 0x06000000;
    }

    internal static ILCoordinatePopulationOutcome ReadPopulation(string path)
    {
        if (!File.Exists(path))
        {
            return ILCoordinatePopulationOutcome.Failed(
                new ILCoordinatePopulationFailure(
                    ILCoordinatePopulationFailureKind.FileNotFound,
                    path));
        }

        try
        {
            return ParsePopulation(File.ReadLines(path), path);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException)
        {
            return ILCoordinatePopulationOutcome.Failed(
                new ILCoordinatePopulationFailure(
                    ILCoordinatePopulationFailureKind.FileReadFailed,
                    path,
                    ex.Message));
        }
    }

    internal static ILCoordinatePopulationOutcome ParsePopulation(
        IEnumerable<string> lines,
        string path)
    {
        var records = new List<ILCoordinatePopulationRecord>();
        int lineNumber = 0;
        int significantRecordCount = 0;

        foreach (string rawLine in lines)
        {
            lineNumber++;
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            significantRecordCount++;
            if (significantRecordCount > MaximumCoordinatePopulation)
            {
                return ILCoordinatePopulationOutcome.Failed(
                    new ILCoordinatePopulationFailure(
                        ILCoordinatePopulationFailureKind
                            .CoordinatePopulationLimitExceeded,
                        path,
                        Limit: MaximumCoordinatePopulation,
                        ObservedLineNumber: lineNumber));
            }

            string[] tokens = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);
            int methodToken = 0;
            int ilOffset = 0;
            int coordinateIndex = Array.FindIndex(
                tokens,
                token => TryParse(
                    token,
                    out methodToken,
                    out ilOffset));
            if (coordinateIndex < 0)
            {
                records.Add(
                    new ILCoordinatePopulationRecord.Malformed(
                        lineNumber,
                        $"{path}:{lineNumber}",
                        "expected a MethodDef token + IL offset coordinate"));
                continue;
            }

            string[] labelTokens = tokens
                .Where((_, index) => index != coordinateIndex)
                .ToArray();
            records.Add(
                new ILCoordinatePopulationRecord.Coordinate(
                    lineNumber,
                    tokens[coordinateIndex],
                    labelTokens.Length == 0
                        ? null
                        : string.Join(' ', labelTokens),
                    methodToken,
                    ilOffset));
        }

        if (records.Count == 0)
        {
            return ILCoordinatePopulationOutcome.Failed(
                new ILCoordinatePopulationFailure(
                    ILCoordinatePopulationFailureKind.NoCoordinates,
                    path));
        }

        return ILCoordinatePopulationOutcome.Success(
            new ILCoordinatePopulation(records));
    }

    internal static string PopulationFailureMessage(
        ILCoordinatePopulationFailure failure) =>
        failure.Kind switch
        {
            ILCoordinatePopulationFailureKind.FileNotFound =>
                $"Coordinate file not found: {failure.Path}",
            ILCoordinatePopulationFailureKind.FileReadFailed =>
                $"Could not read coordinate file '{failure.Path}': {failure.Detail}",
            ILCoordinatePopulationFailureKind.NoCoordinates =>
                $"{failure.Path} did not contain any IL coordinates.",
            ILCoordinatePopulationFailureKind.CoordinatePopulationLimitExceeded =>
                "Coordinate population exceeds the "
                + $"{failure.Limit?.ToString("N0", CultureInfo.InvariantCulture)}"
                + "-record limit "
                + $"at {failure.Path}:{failure.ObservedLineNumber}.",
            _ => throw new InvalidOperationException(
                $"Unknown IL coordinate population failure: {failure.Kind}."),
        };

    static bool TryParseHexInt(string value, out int result)
    {
        result = 0;
        if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return false;
        return int.TryParse(
            value[2..],
            System.Globalization.NumberStyles.HexNumber,
            provider: null,
            out result);
    }

    static void WritePdbWarning(PdbContext context)
    {
        CommandError.WriteBlankLine();
        if (context.WindowsPdbDetected)
        {
            CommandError.Write("PDB is Windows format (not supported).");
            CommandError.WriteLine("       Only Portable PDBs are supported.");
        }
        else
        {
            CommandError.Write("No readable PDB found.");
        }
        CommandError.WriteLine("       Use 'library <target> -S \"SourceLink: Availability\"' for full source reachability.");
        CommandError.WriteBlankLine();
    }
}
