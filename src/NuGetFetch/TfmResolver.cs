namespace NuGetFetch;

/// <summary>
/// Resolves Target Framework Monikers (TFMs) in extracted NuGet packages.
/// Selects the highest-priority .NET TFM available.
/// </summary>
public static class TfmResolver
{
    /// <summary>
    /// Resolves the best assembly path for a specific or auto-selected TFM.
    /// When <paramref name="targetTfm"/> is provided, selects the highest-priority
    /// compatible TFM (priority &lt;= target). Returns the path to the TFM directory,
    /// or null if not found.
    /// </summary>
    public static string? ResolvePackagePath(string extractedPath, string? tfm = null, string? targetTfm = null)
    {
        if (tfm is not null)
        {
            return FindByTfm(extractedPath, tfm);
        }

        return FindHighestTfm(extractedPath, targetTfm);
    }

    /// <summary>
    /// Gets all DLLs in a package, grouped by TFM.
    /// </summary>
    public static IReadOnlyList<PackageDll> GetPackageDlls(string extractedPath)
    {
        List<PackageDll> dlls = [];

        // Check lib/ directory
        string libDir = Path.Combine(extractedPath, "lib");

        if (Directory.Exists(libDir))
        {
            CollectDlls(libDir, dlls);
        }

        // Check tools/ directory
        string toolsDir = Path.Combine(extractedPath, "tools");

        if (Directory.Exists(toolsDir))
        {
            CollectDlls(toolsDir, dlls);
        }

        return dlls;
    }

    /// <summary>
    /// Gets the priority score for a TFM. Higher is better.
    /// </summary>
    public static int GetTfmPriority(string tfm)
    {
        ReadOnlySpan<char> span = tfm.AsSpan();

        // Modern .NET (net5.0+)
        if (span.StartsWith("net") && !span.StartsWith("netstandard") && !span.StartsWith("netcoreapp"))
        {
            ReadOnlySpan<char> versionPart = span[3..];
            int dotIndex = versionPart.IndexOf('.');

            if (dotIndex > 0 &&
                int.TryParse(versionPart[..dotIndex], out int major) &&
                major >= 5)
            {
                int minor = 0;

                if (dotIndex + 1 < versionPart.Length)
                {
                    int.TryParse(versionPart[(dotIndex + 1)..], out minor);
                }

                return 10000 + (major * 100) + minor;
            }

            // Legacy .NET Framework (net45, net461, etc.)
            // Normalize: net45 → 4.5.0, net452 → 4.5.2, net46 → 4.6.0
            if (int.TryParse(versionPart, out int frameworkVersion))
            {
                int fwMajor, fwMinor, fwPatch;

                if (frameworkVersion < 100)
                {
                    // net45, net46 → major.minor.0
                    fwMajor = frameworkVersion / 10;
                    fwMinor = frameworkVersion % 10;
                    fwPatch = 0;
                }
                else
                {
                    // net451, net462 → major.minor.patch
                    fwMajor = frameworkVersion / 100;
                    fwMinor = (frameworkVersion / 10) % 10;
                    fwPatch = frameworkVersion % 10;
                }

                return 1000 + (fwMajor * 100) + (fwMinor * 10) + fwPatch;
            }
        }

        // .NET Core (netcoreapp2.1, netcoreapp3.1)
        if (span.StartsWith("netcoreapp"))
        {
            ReadOnlySpan<char> versionPart = span[10..];
            int dotIndex = versionPart.IndexOf('.');

            if (dotIndex > 0 &&
                int.TryParse(versionPart[..dotIndex], out int major))
            {
                int minor = 0;

                if (dotIndex + 1 < versionPart.Length)
                {
                    int.TryParse(versionPart[(dotIndex + 1)..], out minor);
                }

                return 5000 + (major * 100) + minor;
            }
        }

        // .NET Standard
        if (span.StartsWith("netstandard"))
        {
            ReadOnlySpan<char> versionPart = span[11..];
            int dotIndex = versionPart.IndexOf('.');

            if (dotIndex > 0 &&
                int.TryParse(versionPart[..dotIndex], out int major))
            {
                int minor = 0;

                if (dotIndex + 1 < versionPart.Length)
                {
                    int.TryParse(versionPart[(dotIndex + 1)..], out minor);
                }

                return 3000 + (major * 100) + minor;
            }
        }

        return 0;
    }

    /// <summary>
    /// Gets the framework family for a TFM. Used to prevent cross-family matching
    /// (e.g., net8.0 should not accept net481 assemblies).
    /// </summary>
    public static TfmFamily GetTfmFamily(string tfm)
    {
        ReadOnlySpan<char> span = tfm.AsSpan();

        if (span.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            return TfmFamily.NetStandard;

        if (span.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
            return TfmFamily.NetCore;

        if (span.StartsWith("net", StringComparison.OrdinalIgnoreCase) && span.Length >= 4)
        {
            // net5.0+ (modern .NET) vs net45/net461 (.NET Framework)
            ReadOnlySpan<char> versionPart = span[3..];
            int dotIndex = versionPart.IndexOf('.');

            if (dotIndex > 0 && int.TryParse(versionPart[..dotIndex], out int major) && major >= 5)
                return TfmFamily.NetModern;

            if (char.IsAsciiDigit(versionPart[0]))
                return TfmFamily.NetFramework;
        }

        return TfmFamily.Unknown;
    }

    /// <summary>
    /// Returns true if a candidate TFM is compatible with a target TFM.
    /// </summary>
    /// <remarks>
    /// This is the family-level test, kept for the directory-scanning callers
    /// that pair it with a separate <see cref="GetTfmPriority"/> gate. It
    /// answers whether the two families can ever be paired, not whether the
    /// candidate's version is one the target actually implements. A caller
    /// deciding which asset to bind must use
    /// <see cref="IsFrameworkCompatible"/>, which is version aware: this test
    /// admits <c>netstandard2.1</c> for <c>netcoreapp1.0</c>, which is wrong,
    /// and priority order alone rejects <c>netstandard2.0</c> for
    /// <c>net472</c>, which is also wrong.
    /// </remarks>
    public static bool IsTfmCompatible(string candidateTfm, string targetTfm)
    {
        TfmFamily candidateFamily = GetTfmFamily(candidateTfm);
        TfmFamily targetFamily = GetTfmFamily(targetTfm);

        if (candidateFamily == TfmFamily.NetStandard)
            return true;

        return targetFamily switch
        {
            TfmFamily.NetModern => candidateFamily is TfmFamily.NetModern or TfmFamily.NetCore or TfmFamily.NetStandard,
            TfmFamily.NetCore => candidateFamily is TfmFamily.NetCore or TfmFamily.NetStandard,
            TfmFamily.NetFramework => candidateFamily is TfmFamily.NetFramework or TfmFamily.NetStandard,
            _ => true,
        };
    }

    /// <summary>
    /// One recognized framework identity: its family and its version, with the
    /// version normalized to three components so two spellings of one release
    /// compare equal.
    /// </summary>
    public readonly record struct FrameworkIdentity(
        TfmFamily Family,
        Version Version);

    /// <summary>
    /// Parses a base TFM — no platform suffix — into its family and version.
    /// Returns false for an unrecognized or unparsable moniker rather than
    /// guessing a version for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Total and culture-independent by construction. A framework version is
    /// ASCII digits and nothing else, so the digits are read here rather than
    /// handed to a number parser: <see cref="int.TryParse(ReadOnlySpan{char}, out int)"/>
    /// uses the ambient culture, which accepts a leading sign, surrounding
    /// whitespace, and — under a culture whose negative sign is U+2212 — a
    /// non-ASCII minus. An archive folder named <c>netstandard\u22121.0</c>
    /// then parsed as version -1.0 and threw out of <see cref="Version"/>'s
    /// constructor, after the payload it came from had already been committed.
    /// </para>
    /// <para>
    /// The parser therefore accepts only <c>[0-9]+</c>, rejects an empty or
    /// overlong run rather than overflowing, and requires a positive major and
    /// a non-negative minor before any <see cref="Version"/> is constructed.
    /// </para>
    /// </remarks>
    public static bool TryGetFrameworkIdentity(
        string? tfm,
        out FrameworkIdentity identity)
    {
        identity = default;
        if (string.IsNullOrEmpty(tfm))
            return false;

        TfmFamily family = GetTfmFamily(tfm);
        switch (family)
        {
            case TfmFamily.NetModern:
                return TryDotted(tfm.AsSpan(3), family, out identity);

            case TfmFamily.NetCore:
                return TryDotted(tfm.AsSpan("netcoreapp".Length), family, out identity);

            case TfmFamily.NetStandard:
                return TryDotted(tfm.AsSpan("netstandard".Length), family, out identity);

            case TfmFamily.NetFramework:
                return TryPacked(tfm.AsSpan(3), out identity);

            default:
                return false;
        }

        static bool TryDotted(
            ReadOnlySpan<char> version,
            TfmFamily family,
            out FrameworkIdentity identity)
        {
            identity = default;
            int separator = version.IndexOf('.');
            if (separator <= 0
                || !TryReadAsciiDigits(version[..separator], out int major)
                || major <= 0
                || !TryReadAsciiDigits(version[(separator + 1)..], out int minor))
            {
                return false;
            }

            identity = new FrameworkIdentity(family, new Version(major, minor, 0));
            return true;
        }

        // net45, net461, net481: a packed decimal with no separators, where the
        // third digit is a patch rather than a second minor digit. Leading
        // zeros are noncanonical (net010 is not net10) and are refused so a
        // padded spelling cannot collapse onto another framework version.
        static bool TryPacked(
            ReadOnlySpan<char> version,
            out FrameworkIdentity identity)
        {
            identity = default;
            if (version.Length is < 2 or > 3
                || (version.Length > 1 && version[0] == '0')
                || !TryReadAsciiDigits(version, out int packed)
                || packed < 10)
            {
                return false;
            }

            (int major, int minor, int patch) = packed < 100
                ? (packed / 10, packed % 10, 0)
                : (packed / 100, packed / 10 % 10, packed % 10);
            identity = new FrameworkIdentity(
                TfmFamily.NetFramework,
                new Version(major, minor, patch));
            return true;
        }
    }

    /// <summary>
    /// Reads a run of ASCII digits as a non-negative number, or returns false.
    /// </summary>
    /// <remarks>
    /// No sign, no whitespace, no digit separator, no non-ASCII digit, and no
    /// culture. The length bound is what makes overflow unreachable rather than
    /// caught: nine digits cannot exceed <see cref="int.MaxValue"/>, and no
    /// framework version has nine.
    /// </remarks>
    private static bool TryReadAsciiDigits(ReadOnlySpan<char> value, out int number)
    {
        number = 0;
        if (value.Length is 0 or > 9)
            return false;

        // A multi-digit run with a leading zero is not a framework version
        // spelling this resolver accepts (net08.0 is not net8.0).
        if (value.Length > 1 && value[0] == '0')
            return false;

        foreach (char character in value)
        {
            if (!char.IsAsciiDigit(character))
                return false;

            number = (number * 10) + (character - '0');
        }

        return true;
    }

    /// <summary>
    /// Returns true when an asset built for <paramref name="candidateTfm"/> can
    /// be consumed by a project targeting <paramref name="targetTfm"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Version aware, and family relative: a .NET Standard candidate is
    /// admitted only up to the highest .NET Standard version the target
    /// actually implements, and every other pairing requires the same lineage
    /// with a candidate version no newer than the target's. There is no global
    /// "age" ordering across families, because none exists —
    /// <c>netstandard2.0</c> is older than <c>net472</c> in support terms and
    /// newer in <see cref="GetTfmPriority"/> terms, and comparing those numbers
    /// is what rejects a valid pairing.
    /// </para>
    /// <para>
    /// An unrecognized moniker on either side falls back to
    /// <see cref="IsTfmCompatible"/>, preserving the permissive family-level
    /// answer callers already depend on rather than inventing a version for a
    /// framework this resolver does not model.
    /// </para>
    /// </remarks>
    public static bool IsFrameworkCompatible(string candidateTfm, string targetTfm)
    {
        if (!TryGetFrameworkIdentity(candidateTfm, out FrameworkIdentity candidate)
            || !TryGetFrameworkIdentity(targetTfm, out FrameworkIdentity target))
        {
            return IsTfmCompatible(candidateTfm, targetTfm);
        }

        if (candidate.Family == TfmFamily.NetStandard)
        {
            return MaxNetStandard(target) is { } supported
                && candidate.Version <= supported;
        }

        return target.Family switch
        {
            // net5.0 is netcoreapp5.0 renamed, so the two share one lineage and
            // one version line.
            TfmFamily.NetModern => candidate.Family is TfmFamily.NetModern or TfmFamily.NetCore
                && candidate.Version <= target.Version,
            TfmFamily.NetCore => candidate.Family is TfmFamily.NetCore
                && candidate.Version <= target.Version,
            TfmFamily.NetFramework => candidate.Family is TfmFamily.NetFramework
                && candidate.Version <= target.Version,
            TfmFamily.NetStandard => candidate.Family is TfmFamily.NetStandard
                && candidate.Version <= target.Version,
            _ => false,
        };
    }

    /// <summary>
    /// Ranks how directly a compatible candidate serves a target: 2 for the
    /// target's own lineage, 1 for a .NET Standard fallback, 0 for a candidate
    /// that is not compatible at all.
    /// </summary>
    /// <remarks>
    /// This is the reduction preference, separate from compatibility: an asset
    /// built for the target's own framework line is preferred over a .NET
    /// Standard asset the target merely also implements, whatever their version
    /// numbers are.
    /// </remarks>
    public static int GetFrameworkFallbackRank(string candidateTfm, string targetTfm)
    {
        if (!IsFrameworkCompatible(candidateTfm, targetTfm))
            return 0;

        if (!TryGetFrameworkIdentity(candidateTfm, out FrameworkIdentity candidate)
            || !TryGetFrameworkIdentity(targetTfm, out FrameworkIdentity target))
        {
            return 1;
        }

        bool sameLineage = candidate.Family == target.Family
            || (target.Family is TfmFamily.NetModern
                && candidate.Family is TfmFamily.NetCore);
        return sameLineage ? 2 : 1;
    }

    /// <summary>
    /// The highest .NET Standard version <paramref name="target"/> implements,
    /// or null when it implements none.
    /// </summary>
    /// <remarks>
    /// The table is the published .NET Standard support matrix, held here so
    /// one owner answers the question for every consumer.
    /// </remarks>
    static Version? MaxNetStandard(FrameworkIdentity target) => target.Family switch
    {
        TfmFamily.NetStandard => target.Version,
        TfmFamily.NetModern => new Version(2, 1, 0),
        TfmFamily.NetCore => target.Version >= new Version(3, 0, 0)
            ? new Version(2, 1, 0)
            : target.Version >= new Version(2, 0, 0)
                ? new Version(2, 0, 0)
                : new Version(1, 6, 0),
        TfmFamily.NetFramework => target.Version >= new Version(4, 6, 1)
            ? new Version(2, 0, 0)
            : target.Version >= new Version(4, 6, 0)
                ? new Version(1, 3, 0)
                : target.Version >= new Version(4, 5, 1)
                    ? new Version(1, 2, 0)
                    : target.Version >= new Version(4, 5, 0)
                        ? new Version(1, 1, 0)
                        : null,
        _ => null,
    };

    private static string? FindByTfm(string extractedPath, string tfm)
    {
        // Check lib/{tfm} and tools/{tfm}
        foreach (string subdir in new[] { "lib", "tools" })
        {
            string dir = Path.Combine(extractedPath, subdir, tfm);

            if (Directory.Exists(dir))
            {
                return dir;
            }

            // Case-insensitive fallback
            string parent = Path.Combine(extractedPath, subdir);

            if (Directory.Exists(parent))
            {
                foreach (string candidate in Directory.GetDirectories(parent))
                {
                    if (string.Equals(Path.GetFileName(candidate), tfm, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private static string? FindHighestTfm(string extractedPath, string? targetTfm = null)
    {
        int maxPriority = targetTfm is not null ? GetTfmPriority(targetTfm) : int.MaxValue;
        string? bestPath = null;
        int bestPriority = -1;

        foreach (string subdir in new[] { "lib", "tools" })
        {
            string parent = Path.Combine(extractedPath, subdir);

            if (!Directory.Exists(parent))
            {
                continue;
            }

            foreach (string tfmDir in Directory.GetDirectories(parent))
            {
                string tfmName = Path.GetFileName(tfmDir);
                int priority = GetTfmPriority(tfmName);

                // When a target TFM is specified, enforce family compatibility
                if (targetTfm is not null && !IsTfmCompatible(tfmName, targetTfm))
                {
                    continue;
                }

                if (priority > bestPriority && priority <= maxPriority)
                {
                    bestPriority = priority;
                    bestPath = tfmDir;
                }
            }
        }

        return bestPath;
    }

    private static void CollectDlls(string baseDir, List<PackageDll> dlls)
    {
        foreach (string tfmDir in Directory.GetDirectories(baseDir))
        {
            string tfmName = Path.GetFileName(tfmDir);

            if (!IsTfmLike(tfmName))
            {
                continue;
            }

            foreach (string dll in Directory.GetFiles(tfmDir, "*.dll"))
            {
                dlls.Add(new PackageDll(dll, tfmName));
            }
        }
    }

    /// <summary>
    /// Extracts a TFM from a relative path like "lib/net8.0/Assembly.dll".
    /// Finds the first TFM-like path segment (after lib/ or tools/).
    /// </summary>
    public static string? ExtractTfmFromPath(string relativePath)
    {
        string[] parts = relativePath.Split('/', '\\');

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (IsTfmLike(parts[i])
                && i > 0
                && (parts[i - 1].Equals(
                        "lib",
                        StringComparison.OrdinalIgnoreCase)
                    || parts[i - 1].Equals(
                        "tools",
                        StringComparison.OrdinalIgnoreCase)
                    || parts[i - 1].Equals(
                        "ref",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return parts[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the framework asset folder from a relative package path like
    /// "lib/uap10.0/Assembly.dll", including frameworks this resolver cannot
    /// select as .NET TFMs.
    /// </summary>
    public static string? ExtractFrameworkFolderFromPath(
        string relativePath)
    {
        string[] parts = relativePath.Split('/', '\\');

        for (int i = 1; i < parts.Length - 1; i++)
        {
            if (parts[i].Length > 0
                && (parts[i - 1].Equals(
                        "lib",
                        StringComparison.OrdinalIgnoreCase)
                    || parts[i - 1].Equals(
                        "tools",
                        StringComparison.OrdinalIgnoreCase)
                    || parts[i - 1].Equals(
                        "ref",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return parts[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the normalized containing directory from a relative package
    /// asset path.
    /// </summary>
    public static string? ExtractAssetDirectoryFromPath(
        string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        int separator = normalized.LastIndexOf('/');
        return separator > 0 ? normalized[..separator] : null;
    }

    /// <summary>
    /// True when a package asset folder name is a culture tag, the convention
    /// that marks the containing directory as holding satellite resource
    /// assemblies (<c>lib/net8.0/de/Foo.resources.dll</c>). It is a name test
    /// only: it reads no filesystem and asserts nothing about the entries.
    /// </summary>
    public static bool IsCultureFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Equals("any", StringComparison.OrdinalIgnoreCase))
            return false;

        string[] parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        return IsLanguageSubtag(parts[0])
            && parts.Skip(1).All(IsCultureSubtag);

        static bool IsLanguageSubtag(string value) =>
            value.Length is 2 or 3 && value.All(char.IsAsciiLetter);

        static bool IsCultureSubtag(string value) =>
            value.Length is >= 2 and <= 8
            && value.All(static c => char.IsAsciiLetter(c) || char.IsAsciiDigit(c));
    }

    /// <summary>
    /// Checks if a string looks like a TFM (starts with "net" followed by a digit,
    /// or is a known TFM prefix like "netcoreapp" or "netstandard").
    /// </summary>
    public static bool IsTfmLike(string name) =>
        name.StartsWith("net", StringComparison.OrdinalIgnoreCase)
        && name.Length >= 4
        && (char.IsAsciiDigit(name[3])
            || name.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Represents the broad family of a Target Framework Moniker.
/// </summary>
public enum TfmFamily
{
    Unknown,
    /// <summary>.NET 5+ (net5.0, net6.0, net8.0, etc.)</summary>
    NetModern,
    /// <summary>.NET Core (netcoreapp2.1, netcoreapp3.1, etc.)</summary>
    NetCore,
    /// <summary>.NET Standard (netstandard1.0, netstandard2.0, etc.)</summary>
    NetStandard,
    /// <summary>.NET Framework (net45, net461, net481, etc.)</summary>
    NetFramework,
}
