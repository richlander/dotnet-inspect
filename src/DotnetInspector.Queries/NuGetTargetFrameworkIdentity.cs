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
                int separatorIndex = source.IndexOf(',');
                if (string.IsNullOrWhiteSpace(source[..separatorIndex]))
                    return false;
                if (!HasValidLongFormAttributes(source[(separatorIndex + 1)..]))
                    return false;

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
            if (!DefaultFrameworkNameProvider.Instance.TryGetShortIdentifier(
                    framework.Framework,
                    out _))
            {
                return false;
            }

            string shortFolder = framework.GetShortFolderName().ToLowerInvariant();
            if (shortFolder.Contains("unsupported", StringComparison.Ordinal)
                || !PackageCoordinateResolver.IsAcquisitionTargetText(shortFolder))
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

    static bool HasValidLongFormAttributes(string attributes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasVersion = false;
        string? platform = null;
        string? platformVersion = null;
        foreach (string attribute in attributes.Split(
            ',',
            StringSplitOptions.TrimEntries))
        {
            string[] parts = attribute.Split('=', 2);
            if (parts.Length != 2
                || string.IsNullOrWhiteSpace(parts[0])
                || string.IsNullOrWhiteSpace(parts[1])
                || !seen.Add(parts[0]))
            {
                return false;
            }

            if (parts[0].Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                hasVersion = Version.TryParse(
                    parts[1].Trim().TrimStart('v', 'V'),
                    out _);
                if (!hasVersion)
                    return false;
            }
            else if (parts[0].Equals(
                "Profile",
                StringComparison.OrdinalIgnoreCase))
            {
            }
            else if (parts[0].Equals(
                "Platform",
                StringComparison.OrdinalIgnoreCase))
            {
                platform = parts[1];
            }
            else if (parts[0].Equals(
                "PlatformVersion",
                StringComparison.OrdinalIgnoreCase))
            {
                platformVersion = parts[1];
            }
            else
            {
                return false;
            }
        }

        if (!hasVersion)
            return false;

        return platformVersion is null
            || (platform is not null
                && Version.TryParse(
                    platformVersion.Trim().TrimStart('v', 'V'),
                    out _));
    }
}
