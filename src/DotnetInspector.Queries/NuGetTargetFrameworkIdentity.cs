using DotnetInspector.Packages;
using DotnetInspector.Services;
using NuGet.Frameworks;

namespace DotnetInspector.Queries;

/// <summary>Creates canonical NuGet framework identity without repairing target-plus-RID text.</summary>
static class NuGetTargetFrameworkIdentity
{
    public static bool TryNormalize(string source, out string canonical)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(source)
            || source.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            NuGetFramework framework;
            if (source.Contains(',', StringComparison.Ordinal))
            {
                string normalized = TfmSelector.NormalizeTfm(source);
                framework = string.Equals(normalized, source, StringComparison.Ordinal)
                    ? NuGetFramework.Parse(source)
                    : NuGetFramework.ParseFolder(normalized);
            }
            else
            {
                framework = NuGetFramework.ParseFolder(source);
            }

            if (framework.IsUnsupported)
                return false;

            string shortFolder = framework.GetShortFolderName().ToLowerInvariant();
            if (!PackageCoordinateResolver.IsAcquisitionTargetText(shortFolder))
                return false;

            canonical = shortFolder;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FrameworkException)
        {
            return false;
        }
    }
}
