using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using System.Reflection;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Builds one <see cref="MetadataContext"/> shared across a whole corpus sweep so
/// a dependency such as CoreLib is opened and indexed once for the entire run
/// rather than once per assembly. The locator searches every directory the
/// corpus assemblies live in, plus the running trusted platform assembly set for
/// platform-trusted references. That keeps same-directory framework sweeps
/// version-local while letting product artifact directories resolve framework
/// dependencies that are not copied beside the product DLLs.
/// </summary>
static class CorpusMetadata
{
    public static MetadataContext Create(IEnumerable<string> assemblies)
    {
        var assemblyPaths = assemblies.Select(Path.GetFullPath).ToList();
        var directories = assemblyPaths
            .Select(Path.GetDirectoryName)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var defaultLocators = assemblyPaths
            .Select(MetadataSource.DefaultAssemblyLocator)
            .ToList();
        var platformAssemblies = TrustedPlatformAssemblies();

        AssemblyLocator locator = (name, trust) =>
        {
            if (trust == AssemblyResolutionScope.Platform)
            {
                foreach (var directory in directories)
                {
                    string candidate = Path.Combine(directory!, name + ".dll");
                    if (File.Exists(candidate) && IsTrustedPlatformAssembly(candidate))
                        return candidate;
                }

                return platformAssemblies.GetValueOrDefault(name);
            }

            foreach (var directory in directories)
            {
                string candidate = Path.Combine(directory!, name + ".dll");
                if (File.Exists(candidate))
                    return candidate;
            }
            foreach (var locator in defaultLocators)
                if (locator(name, trust) is { } resolved)
                    return resolved;
            return null;
        };

        return new MetadataContext(locator);
    }

    static IReadOnlyDictionary<string, string> TrustedPlatformAssemblies()
    {
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var assemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                continue;
            assemblies.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }
        return assemblies;
    }

    static bool IsTrustedPlatformAssembly(string path)
    {
        try
        {
            return PlatformKeys.IsPlatform(ToHex(AssemblyName.GetAssemblyName(path).GetPublicKeyToken()));
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static string? ToHex(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return null;
        var chars = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i * 2] = "0123456789abcdef"[bytes[i] >> 4];
            chars[i * 2 + 1] = "0123456789abcdef"[bytes[i] & 0xF];
        }
        return new string(chars);
    }
}
