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
    /// Modern .NET (net5.0+) accepts: NetModern (≤ target), NetCoreApp, NetStandard.
    /// .NET Framework accepts: NetFramework (≤ target), NetStandard.
    /// NetStandard is compatible with all families.
    /// </summary>
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
            if (IsTfmLike(parts[i]) &&
                i > 0 &&
                (parts[i - 1].Equals("lib", StringComparison.OrdinalIgnoreCase) ||
                 parts[i - 1].Equals("tools", StringComparison.OrdinalIgnoreCase) ||
                 parts[i - 1].Equals("ref", StringComparison.OrdinalIgnoreCase)))
            {
                return parts[i];
            }
        }

        return null;
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
