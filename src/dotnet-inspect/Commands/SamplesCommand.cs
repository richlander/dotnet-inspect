using System.Text;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Displays sample code references for a type or entire assembly.
/// </summary>
public class SamplesCommand
{
    public static async Task<int> ExecuteAsync(SamplesOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        var typeName = options.TypeName;

        try
        {
            // Local .cs file mode: read file directly, skip SourceLink/PDB
            if (!string.IsNullOrEmpty(options.FilePath))
            {
                return ExecuteForLocalFile(options.FilePath, options.Region);
            }

            // Get package info from options
            string? packageName = null;
            string? packageVersion = null;
            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                (packageName, packageVersion) = PackageExtractor.ParsePackageReference(options.PackagePath);
            }

            // If type name is specified, get samples for that type only
            if (!string.IsNullOrEmpty(typeName))
            {
                return await ExecuteForTypeAsync(typeName, options, packageName, packageVersion, logger, context.HttpClient);
            }

            // No type specified - get samples for entire assembly
            return await ExecuteForAssemblyAsync(options, packageName, packageVersion, logger, context.HttpClient);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteForLocalFile(string filePath, string? region)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: File not found: {filePath}");
            return 1;
        }

        var content = File.ReadAllText(filePath);

        if (!string.IsNullOrEmpty(region))
        {
            var regionContent = SourceFetcher.ExtractRegion(content, region);
            if (regionContent == null)
            {
                Console.Error.WriteLine($"Error: Region '{region}' not found in {filePath}");
                return 1;
            }
            content = regionContent;
        }

        Console.Write(content);
        return 0;
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
        typeName = GenericTypeNameConverter.Convert(typeName);

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
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

        var (apiType, foundIn) = await ApiServices.ExtractTypeAsync(typeName, apiOptions, logger, httpClient);
        if (apiType == null)
        {
            Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
            return 1;
        }

        if (apiType.Documentation.Samples.Count == 0)
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
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

        logger.Log("Extracting samples from all types in library...");
        var (api, selectedTfm) = await ApiServices.ExtractApiSurfaceAsync(apiOptions, logger, httpClient);
        
        if (api == null)
        {
            Console.Error.WriteLine("Error: Could not extract API surface.");
            return 1;
        }

        // Collect all samples from all types
        List<TypedSample> allSamples = [];
        foreach (var type in api.Types)
        {
            foreach (var sample in type.Documentation.Samples)
            {
                allSamples.Add(new TypedSample(type.Name, type.Namespace, sample));
            }
        }

        if (allSamples.Count == 0)
        {
            Console.Error.WriteLine("No samples found in library.");
            return 0;
        }

        logger.Log($"Found {allSamples.Count} samples across {api.Types.Count(t => t.Documentation.Samples.Count > 0)} types");

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
        var writer = new MarkoutWriter(Console.Out);

        // H1 title - output immediately
        SamplesOutputFormatter.WriteSamplesTitle(writer, assemblyName, packageName, packageVersion);

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
                SamplesOutputFormatter.WriteSamplesWithContent(writer, result.Index, result.TypedSample, result.Content);
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
        return SamplesOutputFormatter.FormatSamplesList(samples, packageName, packageVersion, assemblyName, options.BrowsableUrls);
    }

    private static async Task<string?> FetchSampleContentAsync(SourceFetcher fetcher, SampleReference sample, VerboseLogger logger)
    {
        if (string.IsNullOrEmpty(sample.ResolvedUrl))
        {
            logger.Log($"No resolved URL for sample: {sample.RelativePath}");
            return null;
        }

        var rawUrl = GitHubUrlResolver.ConvertToRawGitHubContentUrl(sample.ResolvedUrl);
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

}

/// <summary>
/// A sample reference with its owning type information.
/// </summary>
public record TypedSample(string TypeName, string? TypeNamespace, SampleReference Sample)
{
    public string FullTypeName => string.IsNullOrEmpty(TypeNamespace) ? TypeName : $"{TypeNamespace}.{TypeName}";
}

public record SamplesOptions
{
    /// <summary>
    /// Type name to get samples for (positional argument). Null for library-wide samples.
    /// </summary>
    public string? TypeName { get; init; }

    /// <summary>
    /// Local .cs file path to read directly (skips SourceLink/PDB).
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Optional region name to extract from a local file (used with --file).
    /// </summary>
    public string? Region { get; init; }

    public string? PackagePath { get; init; }
    public string? AssemblyPath { get; init; }
    public string? PlatformAssembly { get; init; }
    public string? PlatformFramework { get; init; }
    public string? Tfm { get; init; }
    public bool BrowsableUrls { get; init; }
    public bool Verbose { get; init; }
    public bool ListOnly { get; init; }
    public int? PrintSample { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
}
