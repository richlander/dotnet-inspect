using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspector.Vulnerabilities;

internal static class ComponentDiscovery
{
    private static readonly string[] SupportedFilePatterns =
        ["*.deps.json", "*.runtimeconfig.json", "*.nuspec", "*.nupkg", "*.csproj", "*.fsproj", "*.vbproj", "*.dll", "*.exe"];

    private static readonly string[] ProjectFileExtensions = [".csproj", ".fsproj", ".vbproj"];

    internal static List<VulnerabilityComponent> DiscoverComponents(string[] inputs, bool recursive, out List<VulnerabilityRow> diagnostics)
    {
        var components = new List<VulnerabilityComponent>();
        diagnostics = [];

        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
            {
                var files = SupportedFilePatterns
                    .SelectMany(pattern => Directory.EnumerateFiles(input, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (files.Length == 0)
                {
                    diagnostics.Add(CouldNotInfer(input, "Directory did not contain a project file, .deps.json, .runtimeconfig.json, .nuspec, .nupkg, .dll, or .exe."));
                    continue;
                }

                // When a deps.json is present it authoritatively lists the managed package
                // closure, so prefer it and skip loose .dll/.exe probing (unreliable and noisy).
                var hasDepsJson = files.Any(static f => f.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase));
                var effectiveFiles = hasDepsJson
                    ? files.Where(static f =>
                        !f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        && !f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    : files;

                foreach (var file in effectiveFiles)
                    AddFileComponents(file, components, diagnostics);
                continue;
            }

            if (File.Exists(input))
            {
                AddFileComponents(input, components, diagnostics);
                continue;
            }

            if (TryParseComponentIdentity(input, out var component))
            {
                components.Add(component);
                continue;
            }

            diagnostics.Add(CouldNotInfer(input, "Expected an exact PackageId@Version, dotnet-runtime/dotnet-sdk/dotnet-aspnetcore@Version, binary, or component manifest."));
        }

        return components;
    }

    private static void AddFileComponents(string path, List<VulnerabilityComponent> components, List<VulnerabilityRow> diagnostics)
    {
        try
        {
            if (path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
            {
                AddDepsJsonComponents(path, components, diagnostics);
                return;
            }

            if (path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            {
                AddRuntimeConfigComponents(path, components, diagnostics);
                return;
            }

            if (ProjectFileExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                AddProjectComponents(path, components, diagnostics);
                return;
            }

            if (path.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            {
                AddNuspecComponents(path, NuspecParser.Parse(path), components, diagnostics);
                return;
            }

            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                AddBinaryComponents(path, components, diagnostics);
                return;
            }

            if (path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(path);
                var entry = archive.Entries.FirstOrDefault(static e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    diagnostics.Add(CouldNotInfer(path, "Package did not contain a .nuspec manifest."));
                    return;
                }

                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                AddNuspecComponents(path, NuspecParser.ParseContent(reader.ReadToEnd()), components, diagnostics);
                return;
            }

            diagnostics.Add(CouldNotInfer(path, "Unsupported input file type. Expected a project file, .deps.json, .runtimeconfig.json, .nuspec, .nupkg, .dll, or .exe."));
        }
        catch (Exception ex)
        {
            diagnostics.Add(CouldNotInfer(path, ex.Message));
        }
    }

    private static void AddDepsJsonComponents(string path, List<VulnerabilityComponent> components, List<VulnerabilityRow> diagnostics)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(CouldNotInfer(path, "deps.json did not contain a libraries object."));
            return;
        }

        var count = 0;
        foreach (var library in libraries.EnumerateObject())
        {
            if (!TrySplitPackageVersion(library.Name, out var name, out var version))
                continue;

            var libraryType = library.Value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;

            if (TryMapDepsLibraryToOfficialProduct(name, out var productName))
            {
                components.Add(new VulnerabilityComponent(
                    Name: productName,
                    Version: version,
                    Kind: VulnerabilityComponentKind.DotNetProduct,
                    SourceInputPath: path,
                    DisplayName: name,
                    Provenance: VulnerabilityProvenance.DepsJson));
                count++;
                continue;
            }

            if (!string.Equals(libraryType, "package", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            components.Add(new VulnerabilityComponent(
                Name: name,
                Version: version,
                Kind: VulnerabilityComponentKind.NuGetPackage,
                SourceInputPath: path,
                DisplayName: name,
                Provenance: VulnerabilityProvenance.DepsJson));
            count++;
        }

        if (count == 0)
            diagnostics.Add(CouldNotInfer(path, "deps.json did not contain exact package identities."));
    }

    private static void AddRuntimeConfigComponents(string path, List<VulnerabilityComponent> components, List<VulnerabilityRow> diagnostics)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("runtimeOptions", out var runtimeOptions)
            || runtimeOptions.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(CouldNotInfer(path, "runtimeconfig.json did not contain a runtimeOptions object."));
            return;
        }

        var count = 0;

        // Self-contained apps record the exact bundled framework versions; prefer those.
        if (runtimeOptions.TryGetProperty("includedFrameworks", out var included)
            && included.ValueKind == JsonValueKind.Array)
        {
            foreach (var framework in included.EnumerateArray())
                count += AddRuntimeConfigFramework(path, framework, components);
        }

        // Framework-dependent apps record the requested (minimum) framework version.
        if (count == 0)
        {
            if (runtimeOptions.TryGetProperty("framework", out var single)
                && single.ValueKind == JsonValueKind.Object)
            {
                count += AddRuntimeConfigFramework(path, single, components);
            }

            if (runtimeOptions.TryGetProperty("frameworks", out var multiple)
                && multiple.ValueKind == JsonValueKind.Array)
            {
                foreach (var framework in multiple.EnumerateArray())
                    count += AddRuntimeConfigFramework(path, framework, components);
            }
        }

        if (count == 0)
            diagnostics.Add(CouldNotInfer(path, "runtimeconfig.json did not reference a known .NET shared framework with an exact version."));
    }

    private static int AddRuntimeConfigFramework(string path, JsonElement framework, List<VulnerabilityComponent> components)
    {
        if (framework.ValueKind != JsonValueKind.Object
            || !framework.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
            || !framework.TryGetProperty("version", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.String)
        {
            return 0;
        }

        var frameworkName = nameElement.GetString() ?? "";
        if (!TryMapSharedFrameworkProduct(frameworkName, out var productName)
            || !TryNormalizeExactVersion(versionElement.GetString(), out var version))
        {
            return 0;
        }

        components.Add(new VulnerabilityComponent(
            Name: productName,
            Version: version,
            Kind: VulnerabilityComponentKind.DotNetProduct,
            SourceInputPath: path,
            DisplayName: frameworkName,
            Provenance: VulnerabilityProvenance.RuntimeConfig));
        return 1;
    }

    private static void AddProjectComponents(string path, List<VulnerabilityComponent> components, List<VulnerabilityRow> diagnostics)
    {
        var document = XDocument.Load(path);
        var references = document
            .Descendants()
            .Where(static e => e.Name.LocalName == "PackageReference")
            .ToList();

        if (references.Count == 0)
        {
            diagnostics.Add(CouldNotInfer(path, "Project did not contain any PackageReference items."));
            return;
        }

        Dictionary<string, string>? centralVersions = null;
        var count = 0;
        var skipped = false;

        foreach (var reference in references)
        {
            var id = (reference.Attribute("Include") ?? reference.Attribute("Update"))?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var rawVersion = reference.Attribute("VersionOverride")?.Value
                ?? reference.Attribute("Version")?.Value
                ?? reference.Elements().FirstOrDefault(static e => e.Name.LocalName == "Version")?.Value;

            // Central Package Management keeps versions in Directory.Packages.props.
            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                centralVersions ??= LoadCentralPackageVersions(path);
                centralVersions.TryGetValue(id, out rawVersion);
            }

            // MSBuild property references (e.g. $(Version)) need full evaluation; skip best-effort.
            if (string.IsNullOrWhiteSpace(rawVersion) || rawVersion.Contains("$("))
            {
                skipped = true;
                continue;
            }

            if (!TryNormalizeExactVersion(rawVersion, out var version))
            {
                skipped = true;
                continue;
            }

            components.Add(new VulnerabilityComponent(
                Name: id,
                Version: version,
                Kind: VulnerabilityComponentKind.NuGetPackage,
                SourceInputPath: path,
                DisplayName: id,
                Provenance: VulnerabilityProvenance.Project));
            count++;
        }

        if (count == 0)
        {
            diagnostics.Add(CouldNotInfer(path, skipped
                ? "Project PackageReference versions were floating, MSBuild properties, or unresolved central versions; none could be pinned to an exact version."
                : "Project did not contain exact PackageReference versions."));
        }
    }

    private static Dictionary<string, string> LoadCentralPackageVersions(string projectPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));

        while (!string.IsNullOrEmpty(directory))
        {
            var propsPath = Path.Combine(directory, "Directory.Packages.props");
            if (File.Exists(propsPath))
            {
                try
                {
                    var document = XDocument.Load(propsPath);
                    foreach (var element in document.Descendants().Where(static e => e.Name.LocalName == "PackageVersion"))
                    {
                        var id = (element.Attribute("Include") ?? element.Attribute("Update"))?.Value?.Trim();
                        var version = element.Attribute("Version")?.Value
                            ?? element.Elements().FirstOrDefault(static e => e.Name.LocalName == "Version")?.Value;
                        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
                            result.TryAdd(id, version);
                    }
                }
                catch
                {
                    // Best-effort: ignore malformed props files.
                }

                break;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return result;
    }

    private static void AddNuspecComponents(string path, NuspecData nuspec, List<VulnerabilityComponent> components, List<VulnerabilityRow> diagnostics)
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(nuspec.PackageName)
            && TryNormalizeExactVersion(nuspec.Version, out var version))
        {
            components.Add(new VulnerabilityComponent(
                Name: nuspec.PackageName,
                Version: version,
                Kind: VulnerabilityComponentKind.NuGetPackage,
                SourceInputPath: path,
                DisplayName: nuspec.PackageName,
                Provenance: VulnerabilityProvenance.Nuspec));
            count++;
        }

        foreach (var dependency in nuspec.DependencyGroups?.SelectMany(group => group.Dependencies) ?? [])
        {
            if (string.IsNullOrWhiteSpace(dependency.Id)
                || !TryNormalizeExactVersion(dependency.Version, out var dependencyVersion))
            {
                continue;
            }

            components.Add(new VulnerabilityComponent(
                Name: dependency.Id,
                Version: dependencyVersion,
                Kind: VulnerabilityComponentKind.NuGetPackage,
                SourceInputPath: path,
                DisplayName: dependency.Id,
                Provenance: VulnerabilityProvenance.Nuspec));
            count++;
        }

        if (count == 0)
            diagnostics.Add(CouldNotInfer(path, "nuspec did not contain exact package identities."));
    }

    private static bool TryParseComponentIdentity(string input, out VulnerabilityComponent component)
    {
        component = default!;
        var at = input.LastIndexOf('@');
        if (at <= 0 || at == input.Length - 1)
            return false;

        var name = input[..at].Trim();
        var version = input[(at + 1)..].Trim();
        if (name.Length == 0 || !TryNormalizeExactVersion(version, out var normalizedVersion))
            return false;

        if (TryNormalizeOfficialProductName(name, out var productName))
        {
            component = new VulnerabilityComponent(
                Name: productName,
                Version: normalizedVersion,
                Kind: VulnerabilityComponentKind.DotNetProduct,
                SourceInputPath: input,
                DisplayName: productName,
                Provenance: VulnerabilityProvenance.Identity);
            return true;
        }

        component = new VulnerabilityComponent(
            Name: name,
            Version: normalizedVersion,
            Kind: VulnerabilityComponentKind.NuGetPackage,
            SourceInputPath: input,
            DisplayName: name,
            Provenance: VulnerabilityProvenance.Identity);
        return true;
    }

    private static void AddBinaryComponents(string path, List<VulnerabilityComponent> components, List<VulnerabilityRow> diagnostics)
    {
        if (TryInferOfficialProductFromPath(path, out var productName, out var productVersion, out var displayName))
        {
            components.Add(new VulnerabilityComponent(
                Name: productName,
                Version: productVersion,
                Kind: VulnerabilityComponentKind.DotNetProduct,
                SourceInputPath: path,
                DisplayName: displayName ?? productName,
                Provenance: VulnerabilityProvenance.Binary));
            return;
        }

        if (TryInferNuGetPackageFromPath(path, out var packageId, out var packageVersion))
        {
            components.Add(new VulnerabilityComponent(
                Name: packageId,
                Version: packageVersion,
                Kind: VulnerabilityComponentKind.NuGetPackage,
                SourceInputPath: path,
                DisplayName: packageId,
                Provenance: VulnerabilityProvenance.Binary));
            return;
        }

        string? assemblyName = null;
        string? assemblyVersion = null;
        try
        {
            var debugInfo = AssemblyInspector.InspectDll(path);
            assemblyName = debugInfo.AssemblyInfo?.AssemblyName;
            assemblyVersion = NormalizeInformationalVersion(debugInfo.AssemblyInfo?.InformationalVersion)
                ?? debugInfo.AssemblyInfo?.AssemblyVersion
                ?? debugInfo.AssemblyInfo?.FileVersion;
        }
        catch (Exception ex)
        {
            diagnostics.Add(CouldNotInfer(path, $"Binary could not be inspected: {ex.Message}"));
            return;
        }

        var summary = string.IsNullOrWhiteSpace(assemblyName)
            ? "Binary did not expose a package identity or official .NET component version."
            : $"Assembly {assemblyName} {assemblyVersion ?? ""} did not expose an exact NuGet package identity or official .NET component version.";
        diagnostics.Add(CouldNotInfer(path, summary.TrimEnd()));
    }

    private static bool TryInferOfficialProductFromPath(string path, out string productName, out string version, out string? displayName)
    {
        productName = "";
        version = "";
        displayName = null;

        var segments = path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < segments.Length; i++)
        {
            if (i + 2 < segments.Length
                && segments[i].Equals("shared", StringComparison.OrdinalIgnoreCase)
                && TryMapSharedFrameworkProduct(segments[i + 1], out productName)
                && TryNormalizeExactVersion(segments[i + 2], out version))
            {
                displayName = segments[i + 1];
                return true;
            }

            if (i + 1 < segments.Length
                && segments[i].Equals("sdk", StringComparison.OrdinalIgnoreCase)
                && TryNormalizeExactVersion(segments[i + 1], out version))
            {
                productName = "dotnet-sdk";
                displayName = "dotnet-sdk";
                return true;
            }

            if (i + 2 < segments.Length
                && segments[i].Equals("packs", StringComparison.OrdinalIgnoreCase)
                && TryMapPackProduct(segments[i + 1], out productName)
                && TryNormalizeExactVersion(segments[i + 2], out version))
            {
                displayName = segments[i + 1];
                return true;
            }
        }

        return false;
    }

    private static bool TryInferNuGetPackageFromPath(string path, out string packageId, out string version)
    {
        packageId = "";
        version = "";
        var segments = path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i + 2 < segments.Length; i++)
        {
            if (segments[i].Equals("packages", StringComparison.OrdinalIgnoreCase)
                && TryNormalizeExactVersion(segments[i + 2], out version))
            {
                packageId = segments[i + 1];
                return !string.IsNullOrWhiteSpace(packageId);
            }
        }

        return false;
    }

    private static bool TryMapDepsLibraryToOfficialProduct(string name, out string productName)
    {
        if (TryMapSharedFrameworkProduct(name, out productName))
            return true;

        if (TryMapPackProduct(name, out productName))
            return true;

        productName = "";
        return false;
    }

    private static bool TryMapSharedFrameworkProduct(string value, out string productName)
    {
        productName = value.Trim() switch
        {
            "Microsoft.NETCore.App" => "dotnet-runtime",
            "Microsoft.AspNetCore.App" => "dotnet-aspnetcore",
            _ => ""
        };

        return productName.Length > 0;
    }

    private static bool TryMapPackProduct(string value, out string productName)
    {
        var name = value.Trim();
        productName = name switch
        {
            "Microsoft.NETCore.App.Ref" => "dotnet-runtime",
            "Microsoft.AspNetCore.App.Ref" => "dotnet-aspnetcore",
            _ when name.StartsWith("Microsoft.NETCore.App.Runtime.", StringComparison.OrdinalIgnoreCase) => "dotnet-runtime",
            _ when name.StartsWith("runtimepack.Microsoft.NETCore.App.Runtime.", StringComparison.OrdinalIgnoreCase) => "dotnet-runtime",
            _ when name.StartsWith("Microsoft.AspNetCore.App.Runtime.", StringComparison.OrdinalIgnoreCase) => "dotnet-aspnetcore",
            _ when name.StartsWith("runtimepack.Microsoft.AspNetCore.App.Runtime.", StringComparison.OrdinalIgnoreCase) => "dotnet-aspnetcore",
            _ => ""
        };

        return productName.Length > 0;
    }

    private static bool TrySplitPackageVersion(string value, out string name, out string version)
    {
        name = "";
        version = "";
        var slash = value.LastIndexOf('/');
        if (slash <= 0 || slash == value.Length - 1)
            return false;

        name = value[..slash];
        return TryNormalizeExactVersion(value[(slash + 1)..], out version);
    }

    private static bool TryNormalizeExactVersion(string? value, out string version)
    {
        version = "";
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        if (NuGetVersion.TryParse(value, out var direct))
        {
            version = direct.ToNormalizedString();
            return true;
        }

        if (value.Length >= 3 && value[0] == '[' && value[^1] == ']' && !value.Contains(','))
            return TryNormalizeExactVersion(value[1..^1], out version);

        if (VersionRange.TryParse(value, out var range)
            && range.MinVersion != null
            && range.MaxVersion != null
            && range.IsMinInclusive
            && range.IsMaxInclusive
            && range.MinVersion == range.MaxVersion)
        {
            version = range.MinVersion.ToNormalizedString();
            return true;
        }

        return false;
    }

    private static string? NormalizeInformationalVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var plus = value.IndexOf('+');
        var candidate = plus >= 0 ? value[..plus] : value;
        return TryNormalizeExactVersion(candidate, out var version) ? version : null;
    }

    private static bool TryNormalizeOfficialProductName(string name, out string normalized)
    {
        normalized = name.Trim().ToLowerInvariant() switch
        {
            "runtime" or "dotnet-runtime" or "microsoft.netcore.app" => "dotnet-runtime",
            "sdk" or "dotnet-sdk" or "microsoft.net.sdk" => "dotnet-sdk",
            "aspnetcore" or "aspnetcore-runtime" or "dotnet-aspnetcore" or "microsoft.aspnetcore.app" => "dotnet-aspnetcore",
            _ => ""
        };

        return normalized.Length > 0;
    }

    private static VulnerabilityRow CouldNotInfer(string input, string summary) =>
        new(
            Source: "input",
            Status: "Package identity could not be inferred",
            Input: input,
            Component: "",
            Version: "",
            Severity: null,
            Cve: null,
            Ghsa: null,
            AdvisoryUrl: null,
            AffectedRange: null,
            FixedVersion: null,
            Summary: summary);

    internal static List<VulnerabilityComponent> DeduplicateComponents(List<VulnerabilityComponent> components)
    {
        var seen = new HashSet<ComponentKey>();
        List<VulnerabilityComponent> result = [];
        foreach (var component in components)
        {
            if (seen.Add(ComponentKey.From(component)))
                result.Add(component);
        }

        return result;
    }
}
