using DotnetInspector.Packages;
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
            bool requirePlatformRoundTrip = false;
            if (source.Contains(',', StringComparison.Ordinal))
            {
                if (!TryReadLongFormAttributes(
                        source,
                        out string? platform,
                        out Version? platformVersion))
                {
                    return false;
                }

                framework = NuGetFramework.Parse(source);
                if (platform is not null)
                {
                    if (!framework.Framework.Equals(
                            FrameworkConstants.FrameworkIdentifiers.NetCoreApp,
                            StringComparison.Ordinal)
                        || framework.Version.Major < 5)
                    {
                        return false;
                    }

                    framework = new NuGetFramework(
                        framework.Framework,
                        framework.Version,
                        platform.ToLowerInvariant(),
                        platformVersion ?? FrameworkConstants.EmptyVersion);
                    requirePlatformRoundTrip = true;
                }
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
            NuGetFramework roundTripped = NuGetFramework.ParseFolder(shortFolder);
            if ((framework.Framework.Equals(
                        FrameworkConstants.FrameworkIdentifiers.Portable,
                        StringComparison.Ordinal)
                    && HasUnsupportedComponent(shortFolder))
                || !framework.Framework.Equals(
                    roundTripped.Framework,
                    StringComparison.OrdinalIgnoreCase)
                || (requirePlatformRoundTrip
                    && !NuGetFrameworkFullComparer.Instance.Equals(
                        framework,
                        roundTripped))
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

    static bool TryReadLongFormAttributes(
        string source,
        out string? platform,
        out Version? platformVersion)
    {
        platform = null;
        platformVersion = null;
        int separatorIndex = source.IndexOf(',');
        if (separatorIndex < 1
            || string.IsNullOrWhiteSpace(source[..separatorIndex]))
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasVersion = false;
        bool hasProfile = false;
        foreach (string rawAttribute in source[(separatorIndex + 1)..].Split(','))
        {
            string attribute = rawAttribute.TrimStart();
            string[] parts = attribute.Split('=', 2);
            string rawValue = parts.Length == 2 ? parts[1] : "";
            string value = rawValue.Trim();
            if (parts.Length != 2
                || string.IsNullOrWhiteSpace(parts[0])
                || string.IsNullOrWhiteSpace(value)
                || !seen.Add(parts[0]))
            {
                return false;
            }

            if (parts[0].Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                hasVersion = true;
            }
            else if (parts[0].Equals(
                "Profile",
                StringComparison.OrdinalIgnoreCase))
            {
                hasProfile = true;
                if (!string.Equals(rawValue, value, StringComparison.Ordinal)
                    || !PackageCoordinateResolver.IsAcquisitionTargetText(value))
                {
                    return false;
                }
            }
            else if (parts[0].Equals(
                "Platform",
                StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(rawValue, value, StringComparison.Ordinal)
                    || !IsQualifierToken(value))
                {
                    return false;
                }

                platform = value;
            }
            else if (parts[0].Equals(
                "PlatformVersion",
                StringComparison.OrdinalIgnoreCase))
            {
                string version = value[0] is 'v' or 'V'
                    ? value[1..]
                    : value;
                if (!string.Equals(rawValue, value, StringComparison.Ordinal)
                    || version.Length == 0
                    || version[0] is 'v' or 'V'
                    || !Version.TryParse(version, out platformVersion))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        if (!hasVersion)
            return false;

        if (platformVersion is not null && platform is null)
            return false;

        return !(hasProfile && platform is not null);
    }

    static bool IsQualifierToken(string value) =>
        value.All(character => char.IsAsciiLetterOrDigit(character));

    static bool HasUnsupportedComponent(string shortFolder) =>
        shortFolder
            .Split(['-', '+'], StringSplitOptions.RemoveEmptyEntries)
            .Any(component => component.Equals(
                "unsupported",
                StringComparison.Ordinal));
}
