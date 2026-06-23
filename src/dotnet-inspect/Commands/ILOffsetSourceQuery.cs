using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Metadata;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Commands;

internal static class ILOffsetSourceQuery
{
    public static async Task<int> ExecuteAsync(
        string dllPath,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (!TryParse(options.ILOffset!, out var methodToken, out var ilOffset))
        {
            Console.Error.WriteLine($"Error: Invalid --il-offset format '{options.ILOffset}'.");
            Console.Error.WriteLine("Expected format: 0x6000001+0x5 (method token + IL offset)");
            return 1;
        }

        using var service = SourceLinkService.Open(dllPath, logger.Log);
        var pdbContext = service.Context;

        if (!pdbContext.HasMetadata)
        {
            Console.Error.WriteLine("Error: No metadata in library.");
            return 1;
        }

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
            return 1;
        }

        if (!service.HasSourceLink)
            logger.Log("Warning: No SourceLink information found. URLs will not be available.");

        var result = service.ResolveByILOffset(methodToken, ilOffset);
        if (result == null)
        {
            Console.Error.WriteLine($"Error: Could not resolve source location for token 0x{methodToken:X}+0x{ilOffset:X}.");
            Console.Error.WriteLine("The method token may be invalid or the PDB may not contain sequence points for this method.");
            return 1;
        }

        string? url = options.BrowsableUrls ? result.GitHubBrowseUrl : result.SourceUrl;
        if (url != null)
            url += $"#L{result.Line}";

        if (options.JsonOutput)
        {
            var jsonResult = new ILOffsetResult
            {
                Method = result.MethodName,
                Token = $"0x{methodToken:X}",
                ILOffset = $"0x{ilOffset:X}",
                MatchedOffset = $"0x{result.MatchedOffset:X}",
                File = result.FilePath,
                Line = result.Line,
                Url = url
            };
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonResult, SourceJsonContext.Default.ILOffsetResult));
            return 0;
        }

        bool showSections = options.Verbosity >= Verbosity.Minimal;
        bool showSource = options.Verbosity >= Verbosity.Normal;
        string token = $"0x{methodToken:X}";
        string ilOffsetHex = $"0x{ilOffset:X}";
        string? matchedOffset = result.MatchedOffset != ilOffset ? $"0x{result.MatchedOffset:X}" : null;

        var view = new SourceILOffsetView
        {
            Title = result.MethodName ?? token,
            Token = showSections ? null : token,
            ILOffset = showSections ? null : ilOffsetHex,
            MatchedOffset = showSections ? null : matchedOffset,
            Offset = showSections
                ? new ILOffsetInfoSection { Token = token, ILOffset = ilOffsetHex, MatchedOffset = matchedOffset }
                : null,
            Location = showSource ? [new ILOffsetSourceRow(result.FilePath, result.Line, url)] : null,
        };

        if (options.OneLine && !options.JsonOutput)
        {
            Console.WriteLine(url ?? $"{result.FilePath}:{result.Line}");
            return 0;
        }

        var writerOpts = OutputFormatter.CreateProjectedWriterOptions(options.Columns, options.Fields);
        var formatter = options.PlainText ? (IMarkoutFormatter)new PlainTextFormatter() : new MarkdownFormatter();
        if (options.PlainText)
        {
            MarkoutSerializer.Serialize(view, Console.Out, formatter, SourceViewContext.Default, writerOpts);
        }
        else
        {
            OutputFormatter.WriteLimitedMarkdown(Console.Out,
                MarkoutSerializer.Serialize(view, SourceViewContext.Default, writerOpts), options.Rows);
        }

        return 0;
    }

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

public class ILOffsetResult
{
    public string? Method { get; init; }
    public string? Token { get; init; }
    public string? ILOffset { get; init; }
    public string? MatchedOffset { get; init; }
    public string? File { get; init; }
    public int? Line { get; init; }
    public string? Url { get; init; }
}
