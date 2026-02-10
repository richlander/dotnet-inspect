using System.Text;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.Commands;

/// <summary>
/// Lists platform/framework assemblies.
/// </summary>
public class PlatformCommand
{
    public const string Name = "platform";
    public static Task<int> ExecuteAsync(PlatformOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        var packsDir = PlatformResolver.GetPacksDirectory();
        if (packsDir == null)
        {
            Console.Error.WriteLine("Error: Could not locate .NET SDK packs directory.");
            Console.Error.WriteLine("Ensure .NET SDK is installed and DOTNET_ROOT is set if using a non-standard location.");
            return Task.FromResult(1);
        }

        logger.Log($"Using packs directory: {packsDir}");

        // Handle --list-versions
        if (options.ListVersions)
        {
            return Task.FromResult(ListVersions(packsDir, options));
        }

        // If --framework specified, list assemblies for that framework
        if (!string.IsNullOrEmpty(options.Framework))
        {
            return Task.FromResult(ListAssemblies(packsDir, options, logger));
        }

        // Default: list frameworks (consistent with api/samples requiring explicit source)
        return Task.FromResult(ListFrameworks(packsDir, options));
    }

    private static int ListFrameworks(string packsDir, PlatformOptions options)
    {
        var frameworks = PlatformResolver.GetInstalledFrameworks(packsDir);

        if (frameworks.Count == 0)
        {
            Console.Error.WriteLine("No frameworks found.");
            return 1;
        }

        if (options.JsonOutput)
        {
            var data = frameworks.Select(f => new PlatformFrameworkJson(f.ShortName, f.LatestVersion, f.AssemblyCount)).ToList();
            var typeInfo = options.CompactJson 
                ? PlatformCompactJsonContext.Default.ListPlatformFrameworkJson 
                : PlatformJsonContext.Default.ListPlatformFrameworkJson;
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(data, typeInfo));
            return 0;
        }

        Console.WriteLine(PlatformOutputFormatter.FormatFrameworks(frameworks, options.Verbosity, packsDir));
        return 0;
    }

    private static int ListVersions(string packsDir, PlatformOptions options)
    {
        var frameworks = PlatformResolver.GetInstalledFrameworks(packsDir);

        if (frameworks.Count == 0)
        {
            Console.Error.WriteLine("No frameworks found.");
            return 1;
        }

        // Filter to specific framework if specified
        if (!string.IsNullOrEmpty(options.Framework))
        {
            var frameworkName = options.Framework.Contains('@') 
                ? options.Framework[..options.Framework.LastIndexOf('@')] 
                : options.Framework;
            
            frameworks = frameworks.Where(f => 
                f.ShortName.Equals(frameworkName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (frameworks.Count == 0)
            {
                Console.Error.WriteLine($"Framework '{frameworkName}' not found.");
                return 1;
            }
        }

        if (options.JsonOutput)
        {
            var data = frameworks.Select(f => new PlatformVersionsJson(f.ShortName, f.AllVersions)).ToList();
            var typeInfo = options.CompactJson 
                ? PlatformCompactJsonContext.Default.ListPlatformVersionsJson 
                : PlatformJsonContext.Default.ListPlatformVersionsJson;
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(data, typeInfo));
            return 0;
        }

        Console.WriteLine(PlatformOutputFormatter.FormatVersions(frameworks, options.Limit));
        return 0;
    }

    private static int ListAssemblies(string packsDir, PlatformOptions options, VerboseLogger logger)
    {
        var frameworks = PlatformResolver.GetInstalledFrameworks(packsDir);

        if (frameworks.Count == 0)
        {
            Console.Error.WriteLine("No frameworks found.");
            return 1;
        }

        // Filter to specific framework if specified
        string? requestedVersion = null;
        if (!string.IsNullOrEmpty(options.Framework))
        {
            var (refPath, version, error) = PlatformResolver.ResolveFramework(options.Framework, packsDir);
            if (error != null)
            {
                Console.Error.WriteLine($"Error: {error}");
                return 1;
            }

            var frameworkName = options.Framework.Contains('@') 
                ? options.Framework[..options.Framework.LastIndexOf('@')] 
                : options.Framework;

            frameworks = frameworks.Where(f => 
                f.ShortName.Equals(frameworkName, StringComparison.OrdinalIgnoreCase)).ToList();
            requestedVersion = version;
        }

        var frameworkData = ResolveFrameworkAssemblies(frameworks, requestedVersion, options.IncludeTypes, logger);

        if (options.JsonOutput)
        {
            return ListAssembliesJson(frameworkData, options);
        }

        var multipleFrameworks = frameworks.Count > 1 || string.IsNullOrEmpty(options.Framework);
        Console.WriteLine(PlatformOutputFormatter.FormatAssemblies(
            frameworkData, options.IncludeTypes, options.Limit, packsDir, multipleFrameworks));
        return 0;
    }

    private static List<FrameworkAssemblyData> ResolveFrameworkAssemblies(
        List<FrameworkInfo> frameworks, string? requestedVersion, bool includeTypes, VerboseLogger logger)
    {
        var result = new List<FrameworkAssemblyData>();

        foreach (var framework in frameworks)
        {
            var version = requestedVersion ?? framework.LatestVersion;
            var refPath = PlatformResolver.GetRefAssemblyPath(framework.Path, version);

            if (refPath == null)
            {
                logger.Log($"Could not find ref path for {framework.ShortName} {version}");
                continue;
            }

            var assemblies = PlatformResolver.GetAssemblies(refPath);
            var entries = assemblies.Select(a => new AssemblyEntry(
                a.Name,
                includeTypes ? CountPublicTypes(a.Path) : null
            )).ToList();

            result.Add(new FrameworkAssemblyData(framework.ShortName, version, entries));
        }

        return result;
    }

    private static int ListAssembliesJson(List<FrameworkAssemblyData> frameworkData, PlatformOptions options)
    {
        var result = frameworkData.Select(data =>
        {
            var displayAssemblies = data.Assemblies.AsEnumerable();
            if (options.Limit.HasValue)
            {
                displayAssemblies = displayAssemblies.Take(options.Limit.Value);
            }

            var assemblyList = displayAssemblies.Select(a => new PlatformAssemblyJson(
                a.Name, a.PublicTypeCount
            )).ToList();

            return new PlatformAssembliesJson(data.FrameworkName, data.Version, assemblyList);
        }).ToList();

        var typeInfo = options.CompactJson 
            ? PlatformCompactJsonContext.Default.ListPlatformAssembliesJson 
            : PlatformJsonContext.Default.ListPlatformAssembliesJson;
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, typeInfo));
        return 0;
    }

    private static int CountPublicTypes(string assemblyPath)
    {
        return AssemblyReader.CountPublicTypes(assemblyPath);
    }
}

public record PlatformOptions
{
    public bool ListVersions { get; init; }
    public string? Framework { get; init; }
    public bool IncludeTypes { get; init; }
    public int? Limit { get; init; }
    public bool JsonOutput { get; init; }
    public bool CompactJson { get; init; }
    public bool Verbose { get; init; }
    public Verbosity Verbosity { get; init; } = Verbosity.Normal;
}
