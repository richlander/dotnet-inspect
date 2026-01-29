using System.Text;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Displays sample code references for a specific type.
/// </summary>
public class SamplesCommand
{
    public static async Task<int> ExecuteAsync(string typeName, SamplesOptions options)
    {
        // Delegate to ApiCommand with docs enabled, then extract samples
        var apiOptions = new ApiOptions
        {
            PackagePath = options.PackagePath,
            AssemblyPath = options.AssemblyPath,
            Tfm = options.Tfm,
            ShowDocs = true,
            BrowsableUrls = options.BrowsableUrls,
            Verbose = options.Verbose,
            FieldsOnly = true // We only need type info, not member tables
        };

        var logger = new VerboseLogger(options.Verbose);

        // Use the existing ExtractTypeAsync which handles package extraction
        var (apiType, foundIn) = await ApiCommand.ExtractTypeAsync(typeName, apiOptions, logger);
        if (apiType == null)
        {
            Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
            return 1;
        }

        // Check if samples exist
        if (apiType.Documentation?.Samples == null || apiType.Documentation.Samples.Count == 0)
        {
            Console.Error.WriteLine($"No samples found for type '{typeName}'.");
            return 0;
        }

        var samples = apiType.Documentation.Samples;

        // Handle --print option
        if (options.Print.HasValue)
        {
            return await PrintSamplesAsync(samples, options, logger);
        }

        // Get package info from options
        string? packageName = null;
        string? packageVersion = null;
        if (!string.IsNullOrEmpty(options.PackagePath))
        {
            (packageName, packageVersion) = ParsePackageReference(options.PackagePath);
        }

        // Render numbered list output
        var output = RenderSamplesList(apiType, packageName, packageVersion, options);
        Console.WriteLine(output);

        return 0;
    }

    private static async Task<int> PrintSamplesAsync(List<SampleReference> samples, SamplesOptions options, VerboseLogger logger)
    {
        var fetcher = new SourceFetcher();
        int printValue = options.Print!.Value;

        // Validate sample number if specific sample requested
        if (printValue > 0)
        {
            if (printValue > samples.Count)
            {
                Console.Error.WriteLine($"Error: Sample #{printValue} not found. Available samples: 1-{samples.Count}");
                return 1;
            }

            // Print single sample (plain code, no markdown)
            var sample = samples[printValue - 1];
            var content = await FetchSampleContentAsync(fetcher, sample, logger);
            if (content == null)
            {
                Console.Error.WriteLine($"Error: Failed to fetch sample content from {sample.ResolvedUrl ?? sample.RelativePath}");
                return 1;
            }
            Console.WriteLine(content);
            return 0;
        }

        // Print all samples with markdown code fences
        var sb = new StringBuilder();
        for (int i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var description = sample.Description ?? Path.GetFileName(sample.RelativePath);
            var regionInfo = sample.Region != null ? $" (region: {sample.Region})" : "";

            sb.AppendLine($"## {i + 1}. {description}{regionInfo}");
            sb.AppendLine();

            var content = await FetchSampleContentAsync(fetcher, sample, logger);
            if (content != null)
            {
                // Detect language from file extension
                var lang = GetLanguageFromPath(sample.RelativePath);
                sb.AppendLine($"```{lang}");
                sb.AppendLine(content);
                sb.AppendLine("```");
            }
            else
            {
                sb.AppendLine("*Failed to fetch sample content*");
            }
            sb.AppendLine();
        }

        Console.WriteLine(sb.ToString().TrimEnd());
        return 0;
    }

    private static async Task<string?> FetchSampleContentAsync(SourceFetcher fetcher, SampleReference sample, VerboseLogger logger)
    {
        if (string.IsNullOrEmpty(sample.ResolvedUrl))
        {
            logger.Log($"No resolved URL for sample: {sample.RelativePath}");
            return null;
        }

        // Convert to raw URL for fetching
        var rawUrl = ConvertToRawGitHubUrl(sample.ResolvedUrl);
        logger.Log($"Fetching: {rawUrl}");

        var content = await fetcher.FetchSourceAsync(rawUrl);
        if (content == null)
            return null;

        // Extract region if specified
        if (!string.IsNullOrEmpty(sample.Region))
        {
            var regionContent = SourceFetcher.ExtractRegion(content, sample.Region);
            if (regionContent == null)
            {
                logger.Log($"Region '{sample.Region}' not found in file");
            }
            return regionContent ?? content;
        }

        return content;
    }

    private static string ConvertToRawGitHubUrl(string url)
    {
        // Convert github.com/raw/ or /blob/ URLs to raw.githubusercontent.com
        if (url.Contains("github.com"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(url,
                @"https://github\.com/([^/]+)/([^/]+)/(raw|blob)/([^/]+)/(.+)");
            if (match.Success)
            {
                return $"https://raw.githubusercontent.com/{match.Groups[1].Value}/{match.Groups[2].Value}/{match.Groups[4].Value}/{match.Groups[5].Value}";
            }
        }
        // Already a raw URL
        if (url.Contains("raw.githubusercontent.com"))
            return url;

        return url;
    }

    private static string GetLanguageFromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".fs" => "fsharp",
            ".vb" => "vb",
            ".json" => "json",
            ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".md" => "markdown",
            _ => ""
        };
    }

    private static (string name, string? version) ParsePackageReference(string packageRef)
    {
        // Handle name@version format
        var atIndex = packageRef.LastIndexOf('@');
        if (atIndex > 0)
        {
            return (packageRef[..atIndex], packageRef[(atIndex + 1)..]);
        }
        // Handle .nupkg file path
        if (packageRef.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileNameWithoutExtension(packageRef);
            // Parse package.version.nupkg format
            var parts = fileName.Split('.');
            // Find where version starts (first numeric segment)
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0 && char.IsDigit(parts[i][0]))
                {
                    var name = string.Join(".", parts[..i]);
                    var version = string.Join(".", parts[i..]);
                    return (name, version);
                }
            }
        }
        return (packageRef, null);
    }

    private static string RenderSamplesList(ApiType type, string? packageName, string? packageVersion, SamplesOptions options)
    {
        var sb = new StringBuilder();

        // H1 title
        var fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
        var packageInfo = packageName != null && packageVersion != null
            ? $" ({packageName} {packageVersion})"
            : packageName != null ? $" ({packageName})" : "";
        sb.AppendLine($"# Samples: {fullName}{packageInfo}");
        sb.AppendLine();

        // Numbered sample list
        var samples = type.Documentation!.Samples!;
        for (int i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var description = sample.Description ?? Path.GetFileName(sample.RelativePath);
            var url = sample.ResolvedUrl != null
                ? ConvertToGitHubRawUrl(sample.ResolvedUrl) ?? sample.ResolvedUrl
                : sample.RelativePath;

            if (options.BrowsableUrls && url != sample.RelativePath)
            {
                url = ConvertRawToBlobUrl(url);
            }

            var regionInfo = sample.Region != null ? $" (region: `{sample.Region}`)" : "";
            sb.AppendLine($"{i + 1}. {description}: {url}{regionInfo}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string? ConvertToGitHubRawUrl(string url)
    {
        // Convert raw.githubusercontent.com URLs to github.com/raw URLs
        if (url.Contains("raw.githubusercontent.com"))
        {
            // https://raw.githubusercontent.com/owner/repo/sha/path
            // -> https://github.com/owner/repo/raw/sha/path
            var match = System.Text.RegularExpressions.Regex.Match(url,
                @"https://raw\.githubusercontent\.com/([^/]+)/([^/]+)/([^/]+)/(.+)");
            if (match.Success)
            {
                return $"https://github.com/{match.Groups[1].Value}/{match.Groups[2].Value}/raw/{match.Groups[3].Value}/{match.Groups[4].Value}";
            }
        }
        return url;
    }

    private static string ConvertRawToBlobUrl(string url)
    {
        // Convert /raw/ to /blob/ for browsable URLs
        return url.Replace("/raw/", "/blob/");
    }
}

public record SamplesOptions
{
    public string? PackagePath { get; init; }
    public string? AssemblyPath { get; init; }
    public string? Tfm { get; init; }
    public bool BrowsableUrls { get; init; }
    public bool Verbose { get; init; }
    /// <summary>
    /// null = list only (no fetch), -1 = fetch all, N > 0 = fetch specific sample by number
    /// </summary>
    public int? Print { get; init; }
}
