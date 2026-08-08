using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using ILInspector.CSharp;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Commands;

internal static class ILOffsetQuery
{
    public static async Task<(int ExitCode, ILOffsetProjection? Result)> ResolveAsync(
        string dllPath,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        using var service = SourceLinkService.Open(dllPath, logger.Log);
        return await ResolveAsync(
            service,
            packageName,
            packageVersion,
            isPlatformAssembly,
            options,
            httpClient,
            logger,
            writeErrors: true);
    }

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
        if (!TryParse(options.ILOffsetParameter!, out var methodToken, out var ilOffset))
        {
            // One diagnostic, not two: the hint is a continuation of the
            // error, so CommandError indents it rather than prefixing it a
            // second time. Containment belongs to that writer, so the value is
            // interpolated raw here.
            if (writeErrors)
                CommandError.Write(
                    $"Invalid --il-offset value '{options.ILOffsetParameter ?? string.Empty}'.",
                    "Expected format: 0x6000001+0x5 (method token + IL offset)");
            return (1, null);
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
                logger.Log);

            if (service.HasPdb && !service.HasSourceLink)
                logger.LogWarning("No SourceLink information found. URLs will not be available.");
        }

        var outcome = ResearchViews.ProjectILOffset(new ILOffsetProjectionRequest(
            service,
            methodToken,
            ilOffset,
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
