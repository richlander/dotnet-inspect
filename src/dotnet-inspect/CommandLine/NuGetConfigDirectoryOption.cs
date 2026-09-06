using System.CommandLine;
using DotnetInspector.Packages;

namespace DotnetInspector.CommandLine;

internal static class NuGetConfigDirectoryOption
{
    internal static Option<string?> Create() => new("--nugetconfig-directory")
    {
        Description = "Source: discover the ambient NuGet.Config hierarchy from this directory",
    };

    internal static bool TryApply(
        string? directory,
        NuGetSourceOptions original,
        out NuGetSourceOptions sourceOptions,
        out string? error)
    {
        sourceOptions = original;
        error = null;
        if (directory is not null)
        {
            try
            {
                directory = Path.GetFullPath(directory);
            }
            catch (Exception exception) when (exception is
                ArgumentException or IOException or NotSupportedException)
            {
                error = "--nugetconfig-directory must identify a usable directory.";
                return false;
            }

            if (!Directory.Exists(directory))
            {
                error = $"NuGet config discovery directory not found: '{directory}'.";
                return false;
            }
            if (original.ConfigFile is not null)
            {
                error = "--nugetconfig and --nugetconfig-directory cannot be combined.";
                return false;
            }
        }

        sourceOptions = original with { ConfigDirectory = directory };
        return true;
    }
}
