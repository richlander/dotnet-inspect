using System.Text;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Compares API surfaces between two package versions.
/// </summary>
public class DiffCommand
{
    public static async Task<int> ExecuteAsync(string? typeName, DiffOptions options)
    {
        if (string.IsNullOrEmpty(options.PackageVersionRange))
        {
            Console.Error.WriteLine("Error: --package with version range required (e.g., System.Text.Json@9.0.0..10.0.2)");
            return 1;
        }

        // Parse version range: Package@v1..v2
        var (packageName, fromVersion, toVersion) = ParseVersionRange(options.PackageVersionRange);
        if (packageName == null || fromVersion == null || toVersion == null)
        {
            Console.Error.WriteLine("Error: Invalid version range. Use format: Package@v1..v2");
            Console.Error.WriteLine("Example: System.Text.Json@9.0.0..10.0.2");
            return 1;
        }

        var logger = new VerboseLogger(options.Verbose);
        logger.Log($"Comparing {packageName} v{fromVersion} → v{toVersion}");

        try
        {
            // Extract API from both versions
            var fromOptions = new ApiOptions
            {
                PackagePath = $"{packageName}@{fromVersion}",
                Tfm = options.Tfm,
                IncludeAll = options.IncludeAll,
                Verbose = options.Verbose
            };

            var toOptions = new ApiOptions
            {
                PackagePath = $"{packageName}@{toVersion}",
                Tfm = options.Tfm,
                IncludeAll = options.IncludeAll,
                Verbose = options.Verbose
            };

            ApiType? fromType = null;
            ApiType? toType = null;

            if (!string.IsNullOrEmpty(typeName))
            {
                // Compare specific type
                typeName = ApiCommand.ConvertGenericTypeName(typeName);
                
                var (fromResult, _) = await ApiCommand.ExtractTypeAsync(typeName, fromOptions, logger);
                var (toResult, _) = await ApiCommand.ExtractTypeAsync(typeName, toOptions, logger);

                fromType = fromResult;
                toType = toResult;

                if (fromType == null && toType == null)
                {
                    Console.Error.WriteLine($"Error: Type '{typeName}' not found in either version.");
                    return 1;
                }
            }
            else
            {
                Console.Error.WriteLine("Error: Type name required for diff command.");
                Console.Error.WriteLine("Usage: dotnet-inspect diff <type> --package Package@v1..v2");
                return 1;
            }

            // Generate diff output
            var diff = GenerateTypeDiff(typeName, fromType, toType, fromVersion, toVersion);
            Console.WriteLine(diff);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static (string? package, string? fromVersion, string? toVersion) ParseVersionRange(string input)
    {
        // Format: Package@v1..v2
        int atIndex = input.IndexOf('@');
        if (atIndex <= 0)
            return (null, null, null);

        string packageName = input[..atIndex];
        string versionPart = input[(atIndex + 1)..];

        int dotDotIndex = versionPart.IndexOf("..", StringComparison.Ordinal);
        if (dotDotIndex <= 0)
            return (null, null, null);

        string fromVersion = versionPart[..dotDotIndex];
        string toVersion = versionPart[(dotDotIndex + 2)..];

        if (string.IsNullOrEmpty(fromVersion) || string.IsNullOrEmpty(toVersion))
            return (null, null, null);

        return (packageName, fromVersion, toVersion);
    }

    private static string GenerateTypeDiff(string typeName, ApiType? fromType, ApiType? toType, string fromVersion, string toVersion)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## API Diff: {typeName}");
        sb.AppendLine();
        sb.AppendLine($"**{fromVersion}** → **{toVersion}**");
        sb.AppendLine();

        if (fromType == null)
        {
            sb.AppendLine($"✅ **Type added in {toVersion}**");
            sb.AppendLine();
            if (toType?.Members != null)
            {
                sb.AppendLine("**Members:**");
                foreach (var member in toType.Members.OrderBy(m => m.Kind).ThenBy(m => m.Name))
                {
                    sb.AppendLine($"+ `{member.Signature ?? member.Name}`");
                }
            }
            return sb.ToString().TrimEnd();
        }

        if (toType == null)
        {
            sb.AppendLine($"❌ **Type removed in {toVersion}**");
            return sb.ToString().TrimEnd();
        }

        // Compare members
        var fromMembers = fromType.Members ?? [];
        var toMembers = toType.Members ?? [];

        // Create signature-based lookup for comparison, filtering out compiler-generated members
        var fromSignatures = fromMembers
            .Where(m => !string.IsNullOrEmpty(m.Signature) && !IsCompilerGenerated(m.Name))
            .ToDictionary(m => m.Signature!, m => m);
        var toSignatures = toMembers
            .Where(m => !string.IsNullOrEmpty(m.Signature) && !IsCompilerGenerated(m.Name))
            .ToDictionary(m => m.Signature!, m => m);

        var added = toSignatures.Keys.Except(fromSignatures.Keys).OrderBy(s => s).ToList();
        var removed = fromSignatures.Keys.Except(toSignatures.Keys).OrderBy(s => s).ToList();

        if (added.Count == 0 && removed.Count == 0)
        {
            sb.AppendLine("*No API changes detected.*");
            return sb.ToString().TrimEnd();
        }

        // Summary
        sb.AppendLine($"**Summary:** +{added.Count} added, -{removed.Count} removed");
        sb.AppendLine();

        if (removed.Count > 0)
        {
            sb.AppendLine("### Removed");
            sb.AppendLine();
            foreach (var sig in removed)
            {
                sb.AppendLine($"- `{sig}`");
            }
            sb.AppendLine();
        }

        if (added.Count > 0)
        {
            sb.AppendLine("### Added");
            sb.AppendLine();
            foreach (var sig in added)
            {
                sb.AppendLine($"+ `{sig}`");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static bool IsCompilerGenerated(string name)
    {
        return name.StartsWith('<') ||
               name.StartsWith("__") ||
               name.StartsWith("s_") ||
               name.Contains("__BackingField");
    }
}

/// <summary>
/// Options for the diff command.
/// </summary>
public record DiffOptions
{
    public string? PackageVersionRange { get; init; }
    public string? Tfm { get; init; }
    public bool IncludeAll { get; init; }
    public bool Verbose { get; init; }
}
