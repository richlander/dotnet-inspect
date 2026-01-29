using System.Text;
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

        // Get package info from options
        string? packageName = null;
        string? packageVersion = null;
        if (!string.IsNullOrEmpty(options.PackagePath))
        {
            (packageName, packageVersion) = ParsePackageReference(options.PackagePath);
        }

        // Render output
        var output = RenderSamples(apiType, packageName, packageVersion, options);
        Console.WriteLine(output);

        return 0;
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

    private static string RenderSamples(ApiType type, string? packageName, string? packageVersion, SamplesOptions options)
    {
        var sb = new StringBuilder();

        // H1 title
        var fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
        var packageInfo = packageName != null && packageVersion != null
            ? $" ({packageName} {packageVersion})"
            : packageName != null ? $" ({packageName})" : "";
        sb.AppendLine($"# Samples: {fullName}{packageInfo}");
        sb.AppendLine();

        // Sample list
        foreach (var sample in type.Documentation!.Samples!)
        {
            var description = sample.Description ?? Path.GetFileName(sample.RelativePath);
            var url = sample.ResolvedUrl != null
                ? ConvertToGitHubRawUrl(sample.ResolvedUrl) ?? sample.ResolvedUrl
                : sample.RelativePath;

            if (options.BrowsableUrls && url != sample.RelativePath)
            {
                url = ConvertRawToBlobUrl(url);
            }

            var regionInfo = sample.Region != null ? $" (region: `{sample.Region}`)" : "";
            sb.AppendLine($"- {description}: {url}{regionInfo}");
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
}
