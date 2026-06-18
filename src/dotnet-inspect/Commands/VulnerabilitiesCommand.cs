using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DotnetInspector.Output;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Markout;
using NuGet.Versioning;

namespace DotnetInspector.Commands;

public sealed record VulnerabilityOptions
{
    public string[] Inputs { get; init; } = [];
    public bool Recursive { get; init; }
    public bool FailOnVulnerabilities { get; init; }
    public bool NoNuGet { get; init; }
    public bool NoDotNet { get; init; }
    public string DotNetReleaseIndex { get; init; } = VulnerabilitiesCommand.DefaultDotNetReleaseIndexUrl;
    public bool JsonOutput { get; init; }
    public bool OneLine { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool NoHeader { get; init; }
    public int? Rows { get; init; }
    public bool Verbose { get; init; }
}

public sealed record VulnerabilityReport(
    int Inputs,
    int Components,
    int Vulnerabilities,
    List<VulnerabilityRow> Results);

public sealed record VulnerabilityRow(
    string Source,
    string Status,
    string Input,
    string Component,
    string Version,
    string? Severity,
    string? Cve,
    string? Ghsa,
    string? AdvisoryUrl,
    string? AffectedRange,
    string? FixedVersion,
    string? Summary);

public static class VulnerabilitiesCommand
{
    public const string Name = "vulnerabilities";
    public const string DefaultDotNetReleaseIndexUrl = "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json";

    private static readonly string[] SupportedFilePatterns = ["*.deps.json", "*.nuspec", "*.nupkg", "*.dll", "*.exe"];

    public static async Task<int> ExecuteAsync(VulnerabilityOptions options)
    {
        if (options.Inputs.Length == 0)
        {
            Console.Error.WriteLine("Error: one or more package, component, file, or directory inputs are required.");
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  dotnet-inspect vulnerabilities Newtonsoft.Json@13.0.3");
            Console.Error.WriteLine("  dotnet-inspect vulnerabilities app.deps.json");
            Console.Error.WriteLine("  dotnet-inspect vulnerabilities dotnet-runtime@8.0.24 dotnet-sdk@8.0.111");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var components = DiscoverComponents(options.Inputs, options.Recursive, out var diagnostics);
        components = DeduplicateComponents(components);

        List<VulnerabilityRow> rows = [.. diagnostics];
        var vulnerableKeys = new HashSet<ComponentKey>();
        var nonTerminalKeys = new HashSet<ComponentKey>();

        if (!options.NoNuGet)
            await AddNuGetRowsAsync(components, rows, vulnerableKeys, nonTerminalKeys, context).ConfigureAwait(false);

        if (!options.NoDotNet)
        {
            var provider = new DotNetSecurityDisclosureProvider(context.HttpClient, options.DotNetReleaseIndex, context.Logger.Log);
            await provider.AddRowsAsync(components, rows, vulnerableKeys, nonTerminalKeys).ConfigureAwait(false);
        }

        foreach (var component in components)
        {
            var key = ComponentKey.From(component);
            if (!vulnerableKeys.Contains(key) && !nonTerminalKeys.Contains(key))
            {
                rows.Add(new VulnerabilityRow(
                    Source: options.NoNuGet ? ".NET" : options.NoDotNet ? "NuGet" : "all",
                    Status: "No known vulnerabilities for exact version",
                    Input: component.SourceInputPath,
                    Component: component.DisplayName,
                    Version: component.Version,
                    Severity: null,
                    Cve: null,
                    Ghsa: null,
                    AdvisoryUrl: null,
                    AffectedRange: null,
                    FixedVersion: null,
                    Summary: null));
            }
        }

        rows = rows
            .OrderByDescending(row => string.Equals(row.Status, "Vulnerable", StringComparison.OrdinalIgnoreCase))
            .ThenBy(row => row.Input, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Component, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Cve, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Ghsa, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var report = new VulnerabilityReport(
            Inputs: options.Inputs.Length,
            Components: components.Count,
            Vulnerabilities: rows.Count(row => string.Equals(row.Status, "Vulnerable", StringComparison.OrdinalIgnoreCase)),
            Results: rows);

        WriteReport(report, options);

        return options.FailOnVulnerabilities && report.Vulnerabilities > 0 ? 2 : 0;
    }

    private static async Task AddNuGetRowsAsync(
        List<VulnerabilityComponent> components,
        List<VulnerabilityRow> rows,
        HashSet<ComponentKey> vulnerableKeys,
        HashSet<ComponentKey> nonTerminalKeys,
        CommandContext context)
    {
        foreach (var component in components.Where(static c => c.Kind == VulnerabilityComponentKind.NuGetPackage))
        {
            var key = ComponentKey.From(component);
            var vulnerabilities = await PackageMetadataService.GetPackageVulnerabilitiesAsync(
                context.HttpClient,
                component.Name,
                component.Version,
                context.Logger.Log).ConfigureAwait(false);

            if (vulnerabilities.Count > 0)
            {
                vulnerableKeys.Add(key);
                foreach (var vulnerability in vulnerabilities)
                {
                    rows.Add(new VulnerabilityRow(
                        Source: "NuGet",
                        Status: "Vulnerable",
                        Input: component.SourceInputPath,
                        Component: component.Name,
                        Version: component.Version,
                        Severity: vulnerability.Severity,
                        Cve: vulnerability.CveId,
                        Ghsa: vulnerability.GhsaId,
                        AdvisoryUrl: vulnerability.AdvisoryUrl,
                        AffectedRange: component.Version,
                        FixedVersion: null,
                        Summary: vulnerability.Summary));
                }

                continue;
            }

            var status = await PackageMetadataService.GetPackageVersionStatusAsync(
                context.HttpClient,
                component.Name,
                component.Version,
                context.Logger.Log).ConfigureAwait(false);

            if (status == PackageMetadataService.PackageVersionStatus.NotFound)
            {
                nonTerminalKeys.Add(key);
                rows.Add(new VulnerabilityRow(
                    Source: "NuGet",
                    Status: "Package version not found",
                    Input: component.SourceInputPath,
                    Component: component.Name,
                    Version: component.Version,
                    Severity: null,
                    Cve: null,
                    Ghsa: null,
                    AdvisoryUrl: null,
                    AffectedRange: null,
                    FixedVersion: null,
                    Summary: "Package/version was not found on nuget.org."));
            }
        }
    }

    private static List<VulnerabilityComponent> DiscoverComponents(string[] inputs, bool recursive, out List<VulnerabilityRow> diagnostics)
    {
        var components = new List<VulnerabilityComponent>();
        diagnostics = [];

        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
            {
                var files = SupportedFilePatterns
                    .SelectMany(pattern => Directory.EnumerateFiles(input, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (files.Length == 0)
                {
                    diagnostics.Add(CouldNotInfer(input, "Directory did not contain .deps.json, .nuspec, .nupkg, .dll, or .exe files."));
                    continue;
                }

                foreach (var file in files)
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

            diagnostics.Add(CouldNotInfer(path, "Unsupported input file type."));
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
                    DisplayName: name));
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
                DisplayName: name));
            count++;
        }

        if (count == 0)
            diagnostics.Add(CouldNotInfer(path, "deps.json did not contain exact package identities."));
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
                DisplayName: nuspec.PackageName));
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
                DisplayName: dependency.Id));
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
                DisplayName: productName);
            return true;
        }

        component = new VulnerabilityComponent(
            Name: name,
            Version: normalizedVersion,
            Kind: VulnerabilityComponentKind.NuGetPackage,
            SourceInputPath: input,
            DisplayName: name);
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
                DisplayName: displayName ?? productName));
            return;
        }

        if (TryInferNuGetPackageFromPath(path, out var packageId, out var packageVersion))
        {
            components.Add(new VulnerabilityComponent(
                Name: packageId,
                Version: packageVersion,
                Kind: VulnerabilityComponentKind.NuGetPackage,
                SourceInputPath: path,
                DisplayName: packageId));
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

    private static List<VulnerabilityComponent> DeduplicateComponents(List<VulnerabilityComponent> components)
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

    private static void WriteReport(VulnerabilityReport report, VulnerabilityOptions options)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, VulnerabilitiesJsonContext.Default.VulnerabilityReport));
            return;
        }

        if (options.OneLine)
        {
            OutputFormatter.WriteTable(Console.Out, !options.NoHeader, (writer, formatter) =>
            {
                var markoutWriter = new MarkoutWriter(writer, formatter, OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl));
                markoutWriter.WriteTable(Headers, StableHeaders, ToCells(report.Results));
                markoutWriter.Flush();
            });
            return;
        }

        Console.WriteLine(OutputFormatter.ApplyRowLimit(RenderMarkdown(report), options.Rows));
    }

    private static string RenderMarkdown(VulnerabilityReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Vulnerabilities");
        builder.AppendLine();
        builder.AppendLine($"Inputs: {report.Inputs} | Components: {report.Components} | Vulnerabilities: {report.Vulnerabilities}");
        builder.AppendLine();
        builder.AppendLine("## Results");
        builder.AppendLine();
        builder.AppendLine("| Source | Status | Input | Component | Version | Severity | CVE | GHSA | Advisory URL | Affected Range | Fixed Version | Summary |");
        builder.AppendLine("| ------ | ------ | ----- | --------- | ------- | -------- | --- | ---- | ------------ | -------------- | ------------- | ------- |");
        foreach (var row in report.Results)
        {
            builder.Append("| ");
            builder.Append(MarkdownCell(row.Source));
            builder.Append(" | ");
            builder.Append(MarkdownCell(row.Status));
            builder.Append(" | ");
            builder.Append(MarkdownCell(row.Input));
            builder.Append(" | ");
            builder.Append(MarkdownCell(row.Component));
            builder.Append(" | ");
            builder.Append(MarkdownCell(row.Version));
            builder.Append(" | ");
            builder.Append(MarkdownCell(row.Severity));
            builder.Append(" | ");
            builder.Append(MarkdownCell(row.Cve));
            builder.Append(" | ");
            builder.Append(MarkdownCell(row.Ghsa));
            builder.Append(" | ");
            builder.Append(MarkdownCell(row.AdvisoryUrl));
            builder.Append(" | ");
            builder.Append(MarkdownCell(row.AffectedRange));
            builder.Append(" | ");
            builder.Append(MarkdownCell(row.FixedVersion));
            builder.Append(" | ");
            builder.Append(MarkdownCell(row.Summary));
            builder.AppendLine(" |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string MarkdownCell(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');

    private static string[][] ToCells(List<VulnerabilityRow> rows) =>
        rows.Select(row => new[]
        {
            row.Source,
            row.Status,
            row.Input,
            row.Component,
            row.Version,
            row.Severity ?? "",
            row.Cve ?? "",
            row.Ghsa ?? "",
            row.AdvisoryUrl ?? "",
            row.AffectedRange ?? "",
            row.FixedVersion ?? "",
            row.Summary ?? ""
        }).ToArray();

    private static readonly string[] Headers =
    [
        "Source",
        "Status",
        "Input",
        "Component",
        "Version",
        "Severity",
        "CVE",
        "GHSA",
        "Advisory URL",
        "Affected Range",
        "Fixed Version",
        "Summary"
    ];

    private static readonly string[] StableHeaders =
    [
        "source",
        "status",
        "input",
        "component",
        "version",
        "severity",
        "cve",
        "ghsa",
        "advisory_url",
        "affected_range",
        "fixed_version",
        "summary"
    ];

    private sealed record VulnerabilityComponent(string Name, string Version, string Kind, string SourceInputPath, string DisplayName);

    private static class VulnerabilityComponentKind
    {
        public const string NuGetPackage = "nuget-package";
        public const string DotNetProduct = "dotnet-product";
    }

    private sealed record ComponentKey(string Kind, string Name, string Version)
    {
        public static ComponentKey From(VulnerabilityComponent component) =>
            new(component.Kind, component.Name.ToLowerInvariant(), component.Version.ToLowerInvariant());
    }

    private sealed class DotNetSecurityDisclosureProvider(HttpClient client, string rootIndexUrl, Action<string>? log)
    {
        private readonly Dictionary<string, JsonDocument?> _jsonCache = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string>? _majorLinks;

        public async Task AddRowsAsync(
            List<VulnerabilityComponent> components,
            List<VulnerabilityRow> rows,
            HashSet<ComponentKey> vulnerableKeys,
            HashSet<ComponentKey> nonTerminalKeys)
        {
            var candidates = components
                .Where(ShouldQueryDotNet)
                .Select(component => (Component: component, Major: GetDotNetMajorVersion(component.Version)))
                .Where(static item => item.Major != null)
                .GroupBy(static item => item.Major!, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (candidates.Length == 0)
                return;

            var majorLinks = await GetMajorLinksAsync().ConfigureAwait(false);
            foreach (var group in candidates)
            {
                if (!majorLinks.TryGetValue(group.Key, out var majorUrl))
                {
                    AddOfficialNotFoundRows(group.Select(static item => item.Component), rows, nonTerminalKeys);
                    continue;
                }

                var majorDocument = await FetchJsonAsync(majorUrl).ConfigureAwait(false);
                if (majorDocument == null)
                    continue;

                var majorIndex = MajorReleaseIndex.Parse(majorDocument.RootElement);

                foreach (var item in group)
                {
                    var component = item.Component;
                    var baseline = majorIndex.FindBaseline(component);
                    if (baseline == null)
                    {
                        if (component.Kind == VulnerabilityComponentKind.DotNetProduct)
                        {
                            var key = ComponentKey.From(component);
                            nonTerminalKeys.Add(key);
                            rows.Add(new VulnerabilityRow(
                                Source: ".NET",
                                Status: "Component version not found",
                                Input: component.SourceInputPath,
                                Component: component.DisplayName,
                                Version: component.Version,
                                Severity: null,
                                Cve: null,
                                Ghsa: null,
                                AdvisoryUrl: null,
                                AffectedRange: null,
                                FixedVersion: null,
                                Summary: "Component/version could not be mapped to a release-index patch baseline."));
                        }

                        continue;
                    }

                    var cveLinks = majorIndex.GetSecurityCveLinksAfter(baseline);
                    foreach (var cveLink in cveLinks)
                    {
                        var cveDocument = await FetchJsonAsync(cveLink).ConfigureAwait(false);
                        if (cveDocument == null)
                            continue;

                        var cve = OfficialCveDocument.Parse(cveDocument.RootElement);
                        AddOfficialRows(component, cve, rows, vulnerableKeys);
                    }
                }
            }
        }

        private static void AddOfficialNotFoundRows(
            IEnumerable<VulnerabilityComponent> components,
            List<VulnerabilityRow> rows,
            HashSet<ComponentKey> nonTerminalKeys)
        {
            foreach (var component in components.Where(static component => component.Kind == VulnerabilityComponentKind.DotNetProduct))
            {
                var key = ComponentKey.From(component);
                nonTerminalKeys.Add(key);
                rows.Add(new VulnerabilityRow(
                    Source: ".NET",
                    Status: "Component version not found",
                    Input: component.SourceInputPath,
                    Component: component.DisplayName,
                    Version: component.Version,
                    Severity: null,
                    Cve: null,
                    Ghsa: null,
                    AdvisoryUrl: null,
                    AffectedRange: null,
                    FixedVersion: null,
                    Summary: "Component major/version was not found in the official .NET release index."));
            }
        }

        private static bool ShouldQueryDotNet(VulnerabilityComponent component)
        {
            if (component.Kind == VulnerabilityComponentKind.DotNetProduct)
                return true;

            return component.Kind == VulnerabilityComponentKind.NuGetPackage
                && (component.Name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
                    || component.Name.StartsWith("System.", StringComparison.OrdinalIgnoreCase));
        }

        private static void AddOfficialRows(
            VulnerabilityComponent component,
            OfficialCveDocument document,
            List<VulnerabilityRow> rows,
            HashSet<ComponentKey> vulnerableKeys)
        {
            var affected = component.Kind == VulnerabilityComponentKind.DotNetProduct
                ? document.Products.Where(product => product.Name.Equals(component.Name, StringComparison.OrdinalIgnoreCase))
                : document.Packages.Where(package => package.Name.Equals(component.Name, StringComparison.OrdinalIgnoreCase));

            foreach (var affectedComponent in affected)
            {
                if (!IsVersionInInclusiveRange(component.Version, affectedComponent.MinVulnerable, affectedComponent.MaxVulnerable))
                    continue;

                document.Disclosures.TryGetValue(affectedComponent.CveId, out var disclosure);
                vulnerableKeys.Add(ComponentKey.From(component));
                rows.Add(new VulnerabilityRow(
                    Source: ".NET",
                    Status: "Vulnerable",
                    Input: component.SourceInputPath,
                    Component: component.DisplayName,
                    Version: component.Version,
                    Severity: disclosure?.Severity,
                    Cve: affectedComponent.CveId,
                    Ghsa: null,
                    AdvisoryUrl: disclosure?.AdvisoryUrl,
                    AffectedRange: $"{affectedComponent.MinVulnerable}..{affectedComponent.MaxVulnerable}",
                    FixedVersion: affectedComponent.Fixed,
                    Summary: disclosure?.Problem));
            }
        }

        private async Task<Dictionary<string, string>> GetMajorLinksAsync()
        {
            if (_majorLinks != null)
                return _majorLinks;

            var root = await FetchJsonAsync(rootIndexUrl).ConfigureAwait(false);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root?.RootElement.TryGetProperty("_embedded", out var embedded) == true
                && embedded.TryGetProperty("releases", out var releases)
                && releases.ValueKind == JsonValueKind.Array)
            {
                foreach (var release in releases.EnumerateArray())
                {
                    if (release.TryGetProperty("version", out var version)
                        && TryGetLink(release, "self", out var href))
                    {
                        var versionText = version.GetString();
                        if (!string.IsNullOrWhiteSpace(versionText))
                            result[versionText] = href;
                    }
                }
            }

            _majorLinks = result;
            return result;
        }

        private async Task<JsonDocument?> FetchJsonAsync(string url)
        {
            if (_jsonCache.TryGetValue(url, out var cached))
                return cached;

            try
            {
                log?.Invoke($"Fetching .NET release-index data: {url}");
                string json;
                if (File.Exists(url))
                {
                    json = await File.ReadAllTextAsync(url).ConfigureAwait(false);
                }
                else
                {
                    json = await client.GetStringAsync(url).ConfigureAwait(false);
                }

                var document = JsonDocument.Parse(json);
                _jsonCache[url] = document;
                return document;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Error fetching .NET release-index data: {ex.Message}");
                _jsonCache[url] = null;
                return null;
            }
        }

        private static bool TryGetLink(JsonElement element, string relation, out string href)
        {
            href = "";
            if (element.TryGetProperty("_links", out var links)
                && links.TryGetProperty(relation, out var link)
                && link.TryGetProperty("href", out var hrefElement))
            {
                href = hrefElement.GetString() ?? "";
                return href.Length > 0;
            }

            return false;
        }

        private static string? GetString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

        private static bool IsVersionInInclusiveRange(string version, string min, string max)
        {
            if (!NuGetVersion.TryParse(version, out var parsed)
                || !NuGetVersion.TryParse(min, out var minVersion)
                || !NuGetVersion.TryParse(max, out var maxVersion))
            {
                return false;
            }

            return parsed >= minVersion && parsed <= maxVersion;
        }

        private static string? GetDotNetMajorVersion(string version)
        {
            if (!NuGetVersion.TryParse(version, out var parsed))
                return null;

            return $"{parsed.Major}.{parsed.Minor}";
        }

        private sealed class MajorReleaseIndex
        {
            private readonly List<MajorPatch> _patches;

            private MajorReleaseIndex(List<MajorPatch> patches)
            {
                _patches = patches
                    .OrderBy(static patch => patch.Version)
                    .ToList();
            }

            public static MajorReleaseIndex Parse(JsonElement major)
            {
                List<MajorPatch> patchesList = [];
                if (major.TryGetProperty("_embedded", out var embedded)
                    && embedded.TryGetProperty("patches", out var patches)
                    && patches.ValueKind == JsonValueKind.Array)
                {
                    foreach (var patch in patches.EnumerateArray())
                    {
                        var versionText = GetString(patch, "version");
                        if (string.IsNullOrWhiteSpace(versionText)
                            || !NuGetVersion.TryParse(versionText, out var version))
                        {
                            continue;
                        }

                        DateTimeOffset? date = null;
                        var dateText = GetString(patch, "date");
                        if (!string.IsNullOrWhiteSpace(dateText)
                            && DateTimeOffset.TryParse(dateText, out var parsedDate))
                        {
                            date = parsedDate;
                        }

                        var sdkVersionText = GetString(patch, "sdk_version");
                        NuGetVersion? sdkVersion = null;
                        if (!string.IsNullOrWhiteSpace(sdkVersionText))
                            NuGetVersion.TryParse(sdkVersionText, out sdkVersion);

                        patchesList.Add(new MajorPatch(
                            Version: version,
                            VersionText: versionText,
                            Date: date,
                            Security: patch.TryGetProperty("security", out var security) && security.ValueKind == JsonValueKind.True,
                            CveLink: TryGetLink(patch, "cve-json", out var cveLink) ? cveLink : null,
                            SdkVersion: sdkVersion,
                            SdkVersionText: sdkVersionText));
                    }
                }

                return new MajorReleaseIndex(patchesList);
            }

            public MajorPatch? FindBaseline(VulnerabilityComponent component)
            {
                if (!NuGetVersion.TryParse(component.Version, out var componentVersion))
                    return null;

                if (component.Kind == VulnerabilityComponentKind.DotNetProduct)
                {
                    if (component.Name.Equals("dotnet-sdk", StringComparison.OrdinalIgnoreCase))
                    {
                        return _patches.LastOrDefault(patch => patch.SdkVersion == componentVersion);
                    }

                    return _patches.LastOrDefault(patch => patch.Version == componentVersion);
                }

                return _patches.LastOrDefault(patch => patch.Version == componentVersion);
            }

            public List<string> GetSecurityCveLinksAfter(MajorPatch baseline)
            {
                return _patches
                    .Where(patch => patch.Security && patch.CveLink != null && IsAfterBaseline(patch, baseline))
                    .Select(static patch => patch.CveLink!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            private static bool IsAfterBaseline(MajorPatch patch, MajorPatch baseline)
            {
                if (patch.Date != null && baseline.Date != null)
                    return patch.Date > baseline.Date;

                return patch.Version > baseline.Version;
            }
        }

        private sealed record MajorPatch(
            NuGetVersion Version,
            string VersionText,
            DateTimeOffset? Date,
            bool Security,
            string? CveLink,
            NuGetVersion? SdkVersion,
            string? SdkVersionText);
    }

    private sealed class OfficialCveDocument
    {
        public Dictionary<string, OfficialDisclosure> Disclosures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<OfficialAffectedComponent> Products { get; } = [];
        public List<OfficialAffectedComponent> Packages { get; } = [];

        public static OfficialCveDocument Parse(JsonElement root)
        {
            var document = new OfficialCveDocument();
            if (root.TryGetProperty("disclosures", out var disclosures) && disclosures.ValueKind == JsonValueKind.Array)
            {
                foreach (var disclosure in disclosures.EnumerateArray())
                {
                    var id = GetString(disclosure, "id");
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    var severity = "";
                    if (disclosure.TryGetProperty("cvss", out var cvss))
                        severity = GetString(cvss, "severity") ?? "";
                    if (string.IsNullOrWhiteSpace(severity)
                        && disclosure.TryGetProperty("cna", out var cna))
                    {
                        severity = GetString(cna, "severity") ?? "";
                    }

                    var advisoryUrl = "";
                    if (disclosure.TryGetProperty("references", out var references)
                        && references.ValueKind == JsonValueKind.Array)
                    {
                        advisoryUrl = references.EnumerateArray()
                            .Select(static reference => reference.ValueKind == JsonValueKind.String ? reference.GetString() : null)
                            .FirstOrDefault(static reference => !string.IsNullOrWhiteSpace(reference)) ?? "";
                    }

                    document.Disclosures[id] = new OfficialDisclosure(
                        CveId: id,
                        Problem: GetString(disclosure, "problem") ?? GetString(disclosure, "title"),
                        Severity: severity,
                        AdvisoryUrl: string.IsNullOrWhiteSpace(advisoryUrl) ? null : advisoryUrl);
                }
            }

            AddAffectedComponents(root, "products", document.Products);
            AddAffectedComponents(root, "packages", document.Packages);
            return document;
        }

        private static void AddAffectedComponents(JsonElement root, string propertyName, List<OfficialAffectedComponent> destination)
        {
            if (!root.TryGetProperty(propertyName, out var items) || items.ValueKind != JsonValueKind.Array)
                return;

            foreach (var item in items.EnumerateArray())
            {
                var cveId = GetString(item, "cve_id");
                var name = GetString(item, "name");
                var min = GetString(item, "min_vulnerable");
                var max = GetString(item, "max_vulnerable");
                var fixedVersion = GetString(item, "fixed");
                if (string.IsNullOrWhiteSpace(cveId)
                    || string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(min)
                    || string.IsNullOrWhiteSpace(max))
                {
                    continue;
                }

                destination.Add(new OfficialAffectedComponent(
                    CveId: cveId,
                    Name: name,
                    MinVulnerable: min,
                    MaxVulnerable: max,
                    Fixed: fixedVersion));
            }
        }

        private static string? GetString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private sealed record OfficialDisclosure(string CveId, string? Problem, string? Severity, string? AdvisoryUrl);
    private sealed record OfficialAffectedComponent(string CveId, string Name, string MinVulnerable, string MaxVulnerable, string? Fixed);
}
