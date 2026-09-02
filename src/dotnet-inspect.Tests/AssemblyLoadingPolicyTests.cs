using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins the compiler gate that keeps inspected assemblies out of the runtime.
/// </summary>
public sealed class AssemblyLoadingPolicyTests
{
    [Fact]
    public void EveryShippedInspectionProductProjectReceivesAssemblyLoadingPolicy()
    {
        string root = CommandErrorOwnershipTests.RepositoryRoot();
        string rules = Path.GetFullPath(
            Path.Combine(root, "eng", BannedSymbolsFile));
        HashSet<string> projects = [];

        foreach (string productRoot in ProductRoots(root))
        {
            projects.UnionWith(
                CommandErrorOwnershipTests.ProjectClosure(productRoot));
        }

        List<string> uncovered = [];
        foreach (string project in projects.OrderBy(
            path => path,
            StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(root, project);
            ProjectEvaluation evaluation = Evaluate(project);

            if (evaluation.Properties.GetValueOrDefault(ProductMarker)
                != "true")
            {
                uncovered.Add($"{relative}: {ProductMarker} is not true.");
                continue;
            }

            if (!evaluation.Items["PackageReference"].Any(
                    item =>
                        item.GetValueOrDefault("Identity")
                            == AnalyzerPackage
                        && WarningCodes(
                                item.GetValueOrDefault("IncludeAssets")
                                    ?? string.Empty)
                            .Contains(
                                "analyzers",
                                StringComparer.OrdinalIgnoreCase)))
            {
                uncovered.Add(
                    $"{relative}: does not consume the analyzer asset from "
                    + $"{AnalyzerPackage}.");
            }

            if (!evaluation.Items["AdditionalFiles"].Any(
                    item =>
                        item.GetValueOrDefault("FullPath")
                            is { Length: > 0 } fullPath
                        && Path.GetFullPath(fullPath).Equals(
                            rules,
                            StringComparison.OrdinalIgnoreCase)))
            {
                uncovered.Add(
                    $"{relative}: does not receive {BannedSymbolsFile}.");
            }

            if (!WarningCodes(
                    evaluation.Properties.GetValueOrDefault(
                        "WarningsAsErrors",
                        string.Empty))
                .Contains(
                    BannedApiRule,
                    StringComparer.OrdinalIgnoreCase))
            {
                uncovered.Add(
                    $"{relative}: does not escalate {BannedApiRule} "
                    + "to an error.");
            }
        }

        Assert.True(
            uncovered.Count == 0,
            "Every project in a shipped inspection product closure must "
                + "receive the assembly-loading compiler policy."
                + Environment.NewLine
                + string.Join(Environment.NewLine, uncovered));
    }

    [Fact]
    public void AssemblyLoadingBannedSymbolsNameForbiddenRuntimeRoutes()
    {
        string path = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "eng",
            BannedSymbolsFile);

        Assert.True(
            File.Exists(path),
            $"{path} is the rule; without it the analyzer is silent.");

        Assert.Equal(
            [
                "M:System.Reflection.Assembly.CreateInstance(System.String)",
                "M:System.Reflection.Assembly.CreateInstance(System.String,System.Boolean)",
                "M:System.Reflection.Assembly.CreateInstance(System.String,System.Boolean,System.Reflection.BindingFlags,System.Reflection.Binder,System.Object[],System.Globalization.CultureInfo,System.Object[])",
                "M:System.Reflection.Assembly.Load(System.Byte[])",
                "M:System.Reflection.Assembly.Load(System.Byte[],System.Byte[])",
                "M:System.Reflection.Assembly.Load(System.Reflection.AssemblyName)",
                "M:System.Reflection.Assembly.Load(System.String)",
                "M:System.Reflection.Assembly.LoadFile(System.String)",
                "M:System.Reflection.Assembly.LoadFrom(System.String)",
                "M:System.Reflection.Assembly.LoadFrom(System.String,System.Byte[],System.Configuration.Assemblies.AssemblyHashAlgorithm)",
                "M:System.Reflection.Assembly.LoadModule(System.String,System.Byte[])",
                "M:System.Reflection.Assembly.LoadModule(System.String,System.Byte[],System.Byte[])",
                "M:System.Reflection.Assembly.LoadWithPartialName(System.String)",
                "M:System.Reflection.Assembly.UnsafeLoadFrom(System.String)",
                "M:System.Type.GetType(System.String)",
                "M:System.Type.GetType(System.String,System.Boolean)",
                "M:System.Type.GetType(System.String,System.Boolean,System.Boolean)",
                "M:System.Type.GetType(System.String,System.Func{System.Reflection.AssemblyName,System.Reflection.Assembly},System.Func{System.Reflection.Assembly,System.String,System.Boolean,System.Type})",
                "M:System.Type.GetType(System.String,System.Func{System.Reflection.AssemblyName,System.Reflection.Assembly},System.Func{System.Reflection.Assembly,System.String,System.Boolean,System.Type},System.Boolean)",
                "M:System.Type.GetType(System.String,System.Func{System.Reflection.AssemblyName,System.Reflection.Assembly},System.Func{System.Reflection.Assembly,System.String,System.Boolean,System.Type},System.Boolean,System.Boolean)",
                "N:System.Reflection.Emit",
                "T:System.Activator",
                "T:System.AppDomain",
                "T:System.Reflection.MetadataAssemblyResolver",
                "T:System.Reflection.MetadataLoadContext",
                "T:System.Reflection.PathAssemblyResolver",
                "T:System.Runtime.Loader.AssemblyLoadContext",
            ],
            BannedSymbolIds(path).OrderBy(
                id => id,
                StringComparer.Ordinal));
    }

    [Fact]
    public async Task AssemblyLoadingPolicyRejectsForbiddenRuntimeRoutes()
    {
        string project = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "tests",
            "InspectionProductAssemblyLoadCompileNegative",
            "InspectionProductAssemblyLoadCompileNegative.csproj");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--nologo");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start the assembly-loading policy canary build.");
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Task<string> standardOutput =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError =
            process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string output =
            await standardOutput
            + Environment.NewLine
            + await standardError;

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains(
            "error RS0030",
            output,
            StringComparison.Ordinal);

        foreach (string symbol in new string[]
        {
            "Assembly.Load(",
            "Assembly.LoadFile(",
            "Assembly.LoadFrom(",
            "Assembly.LoadModule(",
            "Assembly.UnsafeLoadFrom(",
            "Assembly.CreateInstance(",
            "Type.GetType(",
            "AssemblyLoadContext",
            "System.Reflection.Emit",
            "AppDomain",
            "Activator",
        })
        {
            Assert.Contains(
                symbol,
                output,
                StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> ProductRoots(string root) =>
    [
        Path.Combine(
            root,
            "src",
            "dotnet-inspect",
            "dotnet-inspect.csproj"),
        Path.Combine(root, "src", "mdi", "mdi.csproj"),
        Path.Combine(root, "src", "runfaster", "runfaster.csproj"),
        Path.Combine(root, "src", "ts-jsexport", "ts-jsexport.csproj"),
        Path.Combine(
            root,
            "prototypes",
            "inspect-web",
            "engine",
            "InspectWeb.Engine.csproj"),
    ];

    private static string[] WarningCodes(string value) =>
        value.Split(
            [';', ','],
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);

    private static string[] BannedSymbolIds(string path) =>
    [
        .. File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0
                && !line.StartsWith(';'))
            .Select(line => line.Split(';', 2)[0])
    ];

    private static ProjectEvaluation Evaluate(string project) =>
        Evaluations.GetOrAdd(project, static path =>
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    ArgumentList =
                    {
                        "msbuild",
                        path,
                        "-p:Configuration=Release",
                        $"-getProperty:{string.Join(',', Properties)}",
                        $"-getItem:{string.Join(',', Items)}",
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Could not evaluate the assembly-loading policy for "
                        + $"{path}.{Environment.NewLine}{output}"
                        + $"{Environment.NewLine}{error}");
            }

            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement root = document.RootElement;
            Dictionary<string, string> properties =
                Properties.ToDictionary(
                    name => name,
                    name => root.GetProperty("Properties")
                        .TryGetProperty(
                            name,
                            out JsonElement value)
                            ? value.GetString() ?? string.Empty
                            : string.Empty,
                    StringComparer.Ordinal);
            Dictionary<string,
                IReadOnlyList<Dictionary<string, string>>> items = [];
            foreach (string name in Items)
            {
                items[name] = root.GetProperty("Items")
                    .TryGetProperty(name, out JsonElement values)
                    ? values.EnumerateArray()
                        .Select(value => value.EnumerateObject()
                            .ToDictionary(
                                property => property.Name,
                                property =>
                                    property.Value.GetString()
                                    ?? string.Empty,
                                StringComparer.Ordinal))
                        .ToArray()
                    : [];
            }

            return new(properties, items);
        });

    private const string AnalyzerPackage =
        "Microsoft.CodeAnalysis.BannedApiAnalyzers";
    private const string BannedApiRule = "RS0030";
    private const string BannedSymbolsFile =
        "BannedSymbols.InspectionProduct.txt";
    private const string ProductMarker =
        "IsInspectionProductProject";

    private static readonly string[] Properties =
    [
        ProductMarker,
        "WarningsAsErrors",
    ];

    private static readonly string[] Items =
        ["PackageReference", "AdditionalFiles"];

    private static readonly ConcurrentDictionary<
        string,
        ProjectEvaluation> Evaluations =
        new(StringComparer.Ordinal);

    private sealed record ProjectEvaluation(
        Dictionary<string, string> Properties,
        Dictionary<string,
            IReadOnlyList<Dictionary<string, string>>> Items);
}
