using System.Text;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Displays sample code references for a type or entire assembly.
/// </summary>
public class SamplesCommand
{
    public static async Task<int> ExecuteAsync(string? typeName, SamplesOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        
        // Get package info from options
        string? packageName = null;
        string? packageVersion = null;
        if (!string.IsNullOrEmpty(options.PackagePath))
        {
            (packageName, packageVersion) = ParsePackageReference(options.PackagePath);
        }

        // If type name is specified, get samples for that type only
        if (!string.IsNullOrEmpty(typeName))
        {
            return await ExecuteForTypeAsync(typeName, options, packageName, packageVersion, logger, context.HttpClient);
        }

        // No type specified - get samples for entire assembly
        return await ExecuteForAssemblyAsync(options, packageName, packageVersion, logger, context.HttpClient);
    }

    private static async Task<int> ExecuteForTypeAsync(
        string typeName, 
        SamplesOptions options, 
        string? packageName, 
        string? packageVersion,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        // Convert C# generic syntax (List<T>) to metadata format (List`1)
        typeName = ApiCommand.ConvertGenericTypeName(typeName);

        var apiOptions = new ApiOptions
        {
            PackagePath = options.PackagePath,
            AssemblyPath = options.AssemblyPath,
            PlatformAssembly = options.PlatformAssembly,
            PlatformFramework = options.PlatformFramework,
            Tfm = options.Tfm,
            ShowDocs = true,
            ShowSamples = true,
            BrowsableUrls = options.BrowsableUrls,
            Verbose = options.Verbose,
            FieldsOnly = true
        };

        var (apiType, foundIn) = await ApiCommand.ExtractTypeAsync(typeName, apiOptions, logger, httpClient);
        if (apiType == null)
        {
            Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
            return 1;
        }

        if (apiType.Documentation?.Samples == null || apiType.Documentation.Samples.Count == 0)
        {
            Console.Error.WriteLine($"No samples found for type '{typeName}'.");
            return 0;
        }

        var samples = apiType.Documentation.Samples
            .Select(s => new TypedSample(apiType.Name, apiType.Namespace, s))
            .ToList();

        return await ProcessSamplesAsync(samples, options, packageName, packageVersion, null, logger, httpClient);
    }

    private static async Task<int> ExecuteForAssemblyAsync(
        SamplesOptions options, 
        string? packageName, 
        string? packageVersion,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        // Use ApiCommand to extract all types with docs
        var apiOptions = new ApiOptions
        {
            PackagePath = options.PackagePath,
            AssemblyPath = options.AssemblyPath,
            PlatformAssembly = options.PlatformAssembly,
            PlatformFramework = options.PlatformFramework,
            Tfm = options.Tfm,
            ShowDocs = true,
            ShowSamples = true,
            SourceLinkOnly = true, // Only types with sourcelink
            BrowsableUrls = options.BrowsableUrls,
            Verbose = options.Verbose,
            FieldsOnly = true
        };

        logger.Log("Extracting samples from all types in assembly...");
        var (api, selectedTfm) = await ApiCommand.ExtractApiSurfaceAsync(apiOptions, logger, httpClient);
        
        if (api == null)
        {
            Console.Error.WriteLine("Error: Could not extract API surface.");
            return 1;
        }

        // Collect all samples from all types
        var allSamples = new List<TypedSample>();
        foreach (var type in api.Types)
        {
            if (type.Documentation?.Samples != null)
            {
                foreach (var sample in type.Documentation.Samples)
                {
                    allSamples.Add(new TypedSample(type.Name, type.Namespace, sample));
                }
            }
        }

        if (allSamples.Count == 0)
        {
            Console.Error.WriteLine("No samples found in assembly.");
            return 0;
        }

        logger.Log($"Found {allSamples.Count} samples across {api.Types.Count(t => t.Documentation?.Samples?.Count > 0)} types");

        return await ProcessSamplesAsync(allSamples, options, packageName, packageVersion, api.Name, logger, httpClient);
    }

    private static async Task<int> ProcessSamplesAsync(
        List<TypedSample> samples,
        SamplesOptions options,
        string? packageName,
        string? packageVersion,
        string? assemblyName,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        // Sort samples by URL for consistent ordering between --list and full output
        samples = samples
            .OrderBy(s => s.Sample.ResolvedUrl ?? s.Sample.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Handle --print N: print specific sample as raw code
        if (options.PrintSample.HasValue)
        {
            return await PrintSingleSampleAsync(samples, options.PrintSample.Value, logger, httpClient);
        }

        // Handle --list: show numbered list only
        if (options.ListOnly)
        {
            var listOutput = RenderSamplesList(samples, packageName, packageVersion, assemblyName, options);
            Console.WriteLine(listOutput);
            return 0;
        }

        // Default: fetch and print all samples with numbered sections
        return await PrintAllSamplesAsync(samples, packageName, packageVersion, assemblyName, logger, httpClient);
    }

    private static async Task<int> PrintSingleSampleAsync(List<TypedSample> samples, int sampleNumber, VerboseLogger logger, HttpClient httpClient)
    {
        if (sampleNumber < 1 || sampleNumber > samples.Count)
        {
            Console.Error.WriteLine($"Error: Sample #{sampleNumber} not found. Available samples: 1-{samples.Count}");
            return 1;
        }

        var fetcher = new SourceFetcher(httpClient);
        var sample = samples[sampleNumber - 1].Sample;
        var content = await FetchSampleContentAsync(fetcher, sample, logger);
        
        if (content == null)
        {
            Console.Error.WriteLine($"Error: Failed to fetch sample content from {sample.ResolvedUrl ?? sample.RelativePath}");
            return 1;
        }
        
        Console.WriteLine(content);
        return 0;
    }

    private const int BatchSize = 10;
    private const int InterBatchDelayMs = 50;

    private static async Task<int> PrintAllSamplesAsync(
        List<TypedSample> samples, 
        string? packageName, 
        string? packageVersion,
        string? assemblyName,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var fetcher = new SourceFetcher(httpClient);

        // H1 title - output immediately
        var title = assemblyName ?? packageName ?? "Samples";
        var packageInfo = packageName != null && packageVersion != null
            ? $" ({packageName} {packageVersion})"
            : packageName != null && assemblyName != packageName ? $" ({packageName})" : "";
        Console.WriteLine($"# Samples: {title}{packageInfo}");
        Console.WriteLine();

        // Process samples in batches for parallel fetching with progressive output
        for (int batchStart = 0; batchStart < samples.Count; batchStart += BatchSize)
        {
            var batchEnd = Math.Min(batchStart + BatchSize, samples.Count);
            var batch = samples.Skip(batchStart).Take(batchEnd - batchStart).ToList();

            logger.Log($"Fetching batch {batchStart / BatchSize + 1}: samples {batchStart + 1}-{batchEnd}");

            // Fetch all samples in this batch in parallel
            var fetchTasks = batch.Select(async (typedSample, batchIndex) =>
            {
                var content = await FetchSampleContentAsync(fetcher, typedSample.Sample, logger);
                return (Index: batchStart + batchIndex, TypedSample: typedSample, Content: content);
            }).ToList();

            var results = await Task.WhenAll(fetchTasks);

            // Output results in order (already sorted by index)
            foreach (var result in results.OrderBy(r => r.Index))
            {
                var sample = result.TypedSample.Sample;
                var description = sample.Description ?? Path.GetFileName(sample.RelativePath);

                // Format: ## 1. Namespace.Type - Description
                Console.WriteLine($"## {result.Index + 1}. {result.TypedSample.FullTypeName} - {description}");
                Console.WriteLine();

                if (result.Content != null)
                {
                    var lang = GetLanguageFromPath(sample.RelativePath);
                    Console.WriteLine($"```{lang}");
                    Console.WriteLine(result.Content);
                    Console.WriteLine("```");
                }
                else
                {
                    Console.WriteLine("*Failed to fetch sample content*");
                }
                Console.WriteLine();
            }

            // Small delay between batches to reduce contention
            if (batchEnd < samples.Count)
            {
                await Task.Delay(InterBatchDelayMs);
            }
        }

        return 0;
    }

    private static string RenderSamplesList(
        List<TypedSample> samples, 
        string? packageName, 
        string? packageVersion, 
        string? assemblyName,
        SamplesOptions options)
    {
        var sb = new StringBuilder();

        // H1 title
        var title = assemblyName ?? packageName ?? "Samples";
        var packageInfo = packageName != null && packageVersion != null
            ? $" ({packageName} {packageVersion})"
            : packageName != null && assemblyName != packageName ? $" ({packageName})" : "";
        sb.AppendLine($"# Samples: {title}{packageInfo}");
        sb.AppendLine();

        for (int i = 0; i < samples.Count; i++)
        {
            var typedSample = samples[i];
            var sample = typedSample.Sample;
            var description = sample.Description ?? Path.GetFileName(sample.RelativePath);
            var url = sample.ResolvedUrl != null
                ? ConvertToGitHubRawUrl(sample.ResolvedUrl) ?? sample.ResolvedUrl
                : sample.RelativePath;

            if (options.BrowsableUrls && url != sample.RelativePath)
            {
                url = ConvertRawToBlobUrl(url);
            }

            // Format: 1. Namespace.Type - Description: URL
            sb.AppendLine($"{i + 1}. {typedSample.FullTypeName} - {description}: {url}");
        }

        return sb.ToString().TrimEnd();
    }

    private static async Task<string?> FetchSampleContentAsync(SourceFetcher fetcher, SampleReference sample, VerboseLogger logger)
    {
        if (string.IsNullOrEmpty(sample.ResolvedUrl))
        {
            logger.Log($"No resolved URL for sample: {sample.RelativePath}");
            return null;
        }

        var rawUrl = ConvertToRawGitHubUrl(sample.ResolvedUrl);
        logger.Log($"Fetching: {rawUrl}");

        var content = await fetcher.FetchSourceAsync(rawUrl);
        if (content == null)
            return null;

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
        if (url.Contains("github.com"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(url,
                @"https://github\.com/([^/]+)/([^/]+)/(raw|blob)/([^/]+)/(.+)");
            if (match.Success)
            {
                return $"https://raw.githubusercontent.com/{match.Groups[1].Value}/{match.Groups[2].Value}/{match.Groups[4].Value}/{match.Groups[5].Value}";
            }
        }
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
        var atIndex = packageRef.LastIndexOf('@');
        if (atIndex > 0)
        {
            return (packageRef[..atIndex], packageRef[(atIndex + 1)..]);
        }
        if (packageRef.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileNameWithoutExtension(packageRef);
            var parts = fileName.Split('.');
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

    private static string? ConvertToGitHubRawUrl(string url)
    {
        if (url.Contains("raw.githubusercontent.com"))
        {
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
        return url.Replace("/raw/", "/blob/");
    }
}

/// <summary>
/// A sample reference with its owning type information.
/// </summary>
internal record TypedSample(string TypeName, string? TypeNamespace, SampleReference Sample)
{
    public string FullTypeName => string.IsNullOrEmpty(TypeNamespace) ? TypeName : $"{TypeNamespace}.{TypeName}";
}

public record SamplesOptions
{
    public string? PackagePath { get; init; }
    public string? AssemblyPath { get; init; }
    public string? PlatformAssembly { get; init; }
    public string? PlatformFramework { get; init; }
    public string? Tfm { get; init; }
    public bool BrowsableUrls { get; init; }
    public bool Verbose { get; init; }
    public bool ListOnly { get; init; }
    public int? PrintSample { get; init; }
}
