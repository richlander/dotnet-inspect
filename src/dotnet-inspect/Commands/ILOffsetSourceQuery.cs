using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Commands;

internal static class ILOffsetSourceQuery
{
    public static async Task<(int ExitCode, ILOffsetResult? Result)> ResolveAsync(
        string dllPath,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (!TryParse(options.ILOffsetParameter!, out var methodToken, out var ilOffset))
        {
            Console.Error.WriteLine($"Error: Invalid --il-offset value '{options.ILOffsetParameter}'.");
            Console.Error.WriteLine("Expected format: 0x6000001+0x5 (method token + IL offset)");
            return (1, null);
        }

        using var service = SourceLinkService.Open(dllPath, logger.Log);
        var pdbContext = service.Context;

        if (!pdbContext.HasMetadata)
        {
            Console.Error.WriteLine("Error: No metadata in library.");
            return (1, null);
        }

        var memberContext = pdbContext.ResolveMemberContext(methodToken, ilOffset);
        if (memberContext == null)
        {
            Console.Error.WriteLine($"Error: Could not resolve member context for token 0x{methodToken:X}.");
            Console.Error.WriteLine("The method token may be invalid or may not identify a MethodDef row.");
            return (1, null);
        }

        SourceLinkResolver.ILOffsetSourceInfo? result = null;
        if (RequiresSourceLocation(options))
        {
            await SourceEnricher.AcquirePdbAsync(
                pdbContext,
                httpClient,
                packageName,
                packageVersion,
                isPlatformAssembly,
                logger.Log);

            if (!pdbContext.HasPdb)
            {
                WritePdbWarning(pdbContext);
                return (1, null);
            }

            if (!service.HasSourceLink)
                logger.Log("Warning: No SourceLink information found. URLs will not be available.");

            result = service.ResolveByILOffset(methodToken, ilOffset);
            if (result == null)
            {
                Console.Error.WriteLine($"Error: Could not resolve source location for token 0x{methodToken:X}+0x{ilOffset:X}.");
                Console.Error.WriteLine("The method token may be invalid or the PDB may not contain sequence points for this method.");
                return (1, null);
            }
        }

        string? url = options.BrowsableUrls ? result?.GitHubBrowseUrl : result?.SourceUrl;
        if (url != null)
            url += $"#L{result!.Line}";

        var resolved = new ILOffsetResult
        {
            Method = result?.MethodName ?? memberContext.Member,
            Token = $"0x{methodToken:X}",
            ILOffset = $"0x{ilOffset:X}",
            MatchedOffset = result != null && result.MatchedOffset != ilOffset ? $"0x{result.MatchedOffset:X}" : null,
            File = result?.FilePath,
            Line = result?.Line,
            Url = url,
            MemberContext = new ILOffsetMemberContext
            {
                Assembly = memberContext.Assembly,
                Type = memberContext.Type,
                TypeKind = memberContext.TypeKind,
                Member = memberContext.Member,
                Signature = memberContext.Signature,
                MemberKind = memberContext.MemberKind,
                Visibility = memberContext.Visibility,
                Static = memberContext.Static ? "Yes" : "No",
                Async = memberContext.Async,
                MetadataToken = $"0x{memberContext.MetadataToken:X}",
                ILOffset = $"0x{memberContext.ILOffset:X}"
            }
        };

        return (0, resolved);
    }

    private static bool RequiresSourceLocation(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.ILOffset) == true;

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

        // MethodDef tokens have table id 0x06 in the high byte.
        return (methodToken & unchecked((int)0xFF000000)) == 0x06000000;
    }

    private static bool TryParseHexInt(string value, out int result)
    {
        result = 0;
        if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return false;
        return int.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out result);
    }

    private static void WritePdbWarning(PdbContext pdbContext)
    {
        Console.Error.WriteLine();
        if (pdbContext.WindowsPdbDetected)
        {
            Console.Error.WriteLine("Error: PDB is Windows format (not supported).");
            Console.Error.WriteLine("       Only Portable PDBs are supported.");
        }
        else
        {
            Console.Error.WriteLine("Error: No readable PDB found.");
        }
        Console.Error.WriteLine("       Use 'library <target> -S \"SourceLink Availability\"' for full source reachability.");
        Console.Error.WriteLine();
    }
}
