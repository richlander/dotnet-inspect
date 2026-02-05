using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Lists platform/framework assemblies.
/// </summary>
public class PlatformCommand
{
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

        var sb = new StringBuilder();
        sb.AppendLine("# Installed Frameworks");
        sb.AppendLine();
        sb.AppendLine($"**Packs Directory:** {packsDir}");
        sb.AppendLine();
        sb.AppendLine("| Framework | Version | Assemblies |");
        sb.AppendLine("| --- | --- | --- |");

        foreach (var framework in frameworks)
        {
            sb.AppendLine($"| {framework.ShortName} | {framework.LatestVersion} | {framework.AssemblyCount} |");
        }

        Console.WriteLine(sb.ToString().TrimEnd());
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

        var sb = new StringBuilder();
        sb.AppendLine("# Installed Versions");
        sb.AppendLine();

        foreach (var framework in frameworks)
        {
            sb.AppendLine($"## {framework.ShortName}");
            sb.AppendLine();
            
            var versions = framework.AllVersions;
            if (options.Limit.HasValue)
            {
                versions = versions.Take(options.Limit.Value).ToList();
            }

            foreach (var version in versions)
            {
                sb.AppendLine($"- {version}");
            }

            if (options.Limit.HasValue && framework.AllVersions.Count > options.Limit.Value)
            {
                sb.AppendLine($"- *... and {framework.AllVersions.Count - options.Limit.Value} more*");
            }

            sb.AppendLine();
        }

        Console.WriteLine(sb.ToString().TrimEnd());
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

        if (options.JsonOutput)
        {
            return ListAssembliesJson(frameworks, requestedVersion, options);
        }

        var sb = new StringBuilder();

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

            sb.AppendLine($"## {framework.ShortName} ({version})");
            sb.AppendLine();

            if (options.IncludeTypes)
            {
                sb.AppendLine("| Assembly | Types |");
                sb.AppendLine("| --- | --- |");
            }
            else
            {
                sb.AppendLine("| Assembly |");
                sb.AppendLine("| --- |");
            }

            var displayAssemblies = assemblies.AsEnumerable();
            if (options.Limit.HasValue)
            {
                displayAssemblies = displayAssemblies.Take(options.Limit.Value);
            }

            foreach (var assembly in displayAssemblies)
            {
                if (options.IncludeTypes)
                {
                    var typeCount = CountPublicTypes(assembly.Path);
                    sb.AppendLine($"| {assembly.Name} | {typeCount} |");
                }
                else
                {
                    sb.AppendLine($"| {assembly.Name} |");
                }
            }

            if (options.Limit.HasValue && assemblies.Count > options.Limit.Value)
            {
                sb.AppendLine();
                sb.AppendLine($"*... and {assemblies.Count - options.Limit.Value} more assemblies*");
            }

            sb.AppendLine();
        }

        // Add header if multiple frameworks
        if (frameworks.Count > 1 || string.IsNullOrEmpty(options.Framework))
        {
            Console.WriteLine("# Platform Assemblies");
            Console.WriteLine();
            Console.WriteLine($"**Packs Directory:** {packsDir}");
            Console.WriteLine();
        }

        Console.WriteLine(sb.ToString().TrimEnd());
        return 0;
    }

    private static int ListAssembliesJson(List<FrameworkInfo> frameworks, string? requestedVersion, PlatformOptions options)
    {
        var result = new List<PlatformAssembliesJson>();

        foreach (var framework in frameworks)
        {
            var version = requestedVersion ?? framework.LatestVersion;
            var refPath = PlatformResolver.GetRefAssemblyPath(framework.Path, version);
            
            if (refPath == null)
                continue;

            var assemblies = PlatformResolver.GetAssemblies(refPath);
            
            var displayAssemblies = assemblies.AsEnumerable();
            if (options.Limit.HasValue)
            {
                displayAssemblies = displayAssemblies.Take(options.Limit.Value);
            }

            var assemblyList = displayAssemblies.Select(a => new PlatformAssemblyJson(
                a.Name,
                options.IncludeTypes ? CountPublicTypes(a.Path) : null
            )).ToList();

            result.Add(new PlatformAssembliesJson(framework.ShortName, version, assemblyList));
        }

        var typeInfo = options.CompactJson 
            ? PlatformCompactJsonContext.Default.ListPlatformAssembliesJson 
            : PlatformJsonContext.Default.ListPlatformAssembliesJson;
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, typeInfo));
        return 0;
    }

    private static int CountPublicTypes(string assemblyPath)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            
            if (!peReader.HasMetadata)
                return 0;

            var reader = peReader.GetMetadataReader();
            int count = 0;

            foreach (var typeDefHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeDefHandle);
                var attributes = typeDef.Attributes;

                bool isPublic = (attributes & System.Reflection.TypeAttributes.VisibilityMask) == System.Reflection.TypeAttributes.Public ||
                                (attributes & System.Reflection.TypeAttributes.VisibilityMask) == System.Reflection.TypeAttributes.NestedPublic;

                if (isPublic)
                {
                    var name = reader.GetString(typeDef.Name);
                    if (!name.StartsWith("<") && !name.StartsWith("__"))
                    {
                        count++;
                    }
                }
            }

            return count;
        }
        catch
        {
            return 0;
        }
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
}
