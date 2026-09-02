using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins the compiler gate that keeps inspected assemblies out of the runtime.
/// </summary>
public sealed class AssemblyLoadingPolicyTests
{
    [Fact]
    public void EveryShippedInspectionProductProjectIsAnalyzedForAssemblyLoading()
    {
        string root = CommandErrorOwnershipTests.RepositoryRoot();
        HashSet<string> projects = [];

        foreach (string productRoot in ProductRoots(root))
        {
            projects.UnionWith(CommandErrorOwnershipTests.ProjectClosure(productRoot));
        }

        List<string> uncovered = [];
        foreach (string project in projects.OrderBy(path => path, StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(root, project);
            ProjectEvaluation evaluation = Evaluate(project);

            if (evaluation.Properties.GetValueOrDefault(ProductMarker) != "true")
            {
                uncovered.Add($"{relative}: {ProductMarker} is not true.");
                continue;
            }

            if (!evaluation.Items["PackageReference"].Any(
                    item => item.GetValueOrDefault("Identity") == AnalyzerPackage
                        && (item.GetValueOrDefault("IncludeAssets") ?? string.Empty)
                            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Contains("analyzers", StringComparer.OrdinalIgnoreCase)))
            {
                uncovered.Add($"{relative}: does not consume the analyzer asset from {AnalyzerPackage}.");
            }

            string rules = Path.GetFullPath(Path.Combine(root, "eng", BannedSymbolsFile));
            if (!evaluation.Items["AdditionalFiles"].Any(
                    item => item.GetValueOrDefault("FullPath") is { Length: > 0 } fullPath
                        && Path.GetFullPath(fullPath).Equals(
                            rules,
                            StringComparison.OrdinalIgnoreCase)))
            {
                uncovered.Add(
                    $"{relative}: does not pass {BannedSymbolsFile} to the analyzer.");
            }

            string runAnalyzers = evaluation.Properties.GetValueOrDefault("RunAnalyzers", string.Empty);
            string runAnalyzersDuringBuild =
                evaluation.Properties.GetValueOrDefault("RunAnalyzersDuringBuild", string.Empty);
            if (runAnalyzers.Equals("false", StringComparison.OrdinalIgnoreCase)
                || (runAnalyzers.Length == 0
                    && runAnalyzersDuringBuild.Equals("false", StringComparison.OrdinalIgnoreCase)))
            {
                uncovered.Add($"{relative}: disables analyzers in the Release build.");
            }

            if (!ListProperty(evaluation, "WarningsAsErrors").Contains(
                    BannedApiRule,
                    StringComparer.OrdinalIgnoreCase))
            {
                uncovered.Add($"{relative}: does not escalate {BannedApiRule} to an error.");
            }

            if (ListProperty(evaluation, "WarningsNotAsErrors").Contains(
                    BannedApiRule,
                    StringComparer.OrdinalIgnoreCase)
                || ListProperty(evaluation, "NoWarn").Contains(
                    BannedApiRule,
                    StringComparer.OrdinalIgnoreCase))
            {
                uncovered.Add($"{relative}: suppresses or de-escalates {BannedApiRule}.");
            }
        }

        Assert.True(
            uncovered.Count == 0,
            "Every project in a shipped inspection product closure must compile with the "
                + $"assembly-loading policy enabled.{Environment.NewLine}"
                + string.Join(Environment.NewLine, uncovered));
    }

    [Fact]
    public void AssemblyLoadingBannedSymbolsNameEveryForbiddenRuntimeRoute()
    {
        string path = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "eng",
            BannedSymbolsFile);

        Assert.True(File.Exists(path), $"{path} is the rule; without it the analyzer is silent.");

        Assert.Equal(
            ForbiddenRuntimeSymbolIds().OrderBy(id => id, StringComparer.Ordinal),
            BannedSymbolIds(path).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void CompiledShippedToolAssembliesReferenceNoForbiddenRuntimeRoute()
    {
        string root = CommandErrorOwnershipTests.RepositoryRoot();
        HashSet<string> projects = [];

        foreach (string productRoot in ToolProductRoots(root))
        {
            projects.UnionWith(CommandErrorOwnershipTests.ProjectClosure(productRoot));
        }

        List<string> found = [];
        foreach (string project in projects.OrderBy(path => path, StringComparer.Ordinal))
        {
            string assembly = Evaluate(project).Properties.GetValueOrDefault("TargetPath", string.Empty);
            Assert.True(
                File.Exists(assembly),
                $"{Path.GetRelativePath(root, project)} is a shipped product project but {assembly} does not exist. "
                    + "Build the solution in Release before running this gate.");
            found.AddRange(ForbiddenRuntimeReferences(assembly));
        }

        Assert.Empty(found);
    }

    [Fact]
    public async Task AssemblyLoadingPolicyRejectsRepresentativeForbiddenRuntimeRoutes()
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
            ?? throw new InvalidOperationException("Failed to start the assembly-loading policy canary build.");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string output = await standardOutput + Environment.NewLine + await standardError;

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("error RS0030", output, StringComparison.Ordinal);

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
            Assert.Contains(symbol, output, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> ProductRoots(string root) =>
    [
        .. ToolProductRoots(root),
        Path.Combine(root, "prototypes", "inspect-web", "engine", "InspectWeb.Engine.csproj"),
    ];

    private static IEnumerable<string> ToolProductRoots(string root) =>
    [
        Path.Combine(root, "src", "dotnet-inspect", "dotnet-inspect.csproj"),
        Path.Combine(root, "src", "mdi", "mdi.csproj"),
        Path.Combine(root, "src", "runfaster", "runfaster.csproj"),
        Path.Combine(root, "src", "ts-jsexport", "ts-jsexport.csproj"),
    ];

    private static string[] ListProperty(ProjectEvaluation evaluation, string name) =>
        evaluation.Properties.GetValueOrDefault(name, string.Empty)
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);

    private static string[] BannedSymbolIds(string path) =>
    [
        .. File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith(';'))
            .Select(line => line.Split(';', 2)[0])
    ];

    private static IEnumerable<string> ForbiddenRuntimeSymbolIds()
    {
        INamedTypeSymbol assembly = RequiredFrameworkType("System.Reflection.Assembly");
        foreach (IMethodSymbol method in assembly.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method =>
                method.DeclaredAccessibility == Accessibility.Public
                && AssemblyLoadingMethodNames.Contains(method.Name)))
        {
            yield return method.GetDocumentationCommentId()!;
        }

        INamedTypeSymbol type = RequiredFrameworkType("System.Type");
        foreach (IMethodSymbol method in type.GetMembers("GetType")
            .OfType<IMethodSymbol>()
            .Where(method =>
                method.DeclaredAccessibility == Accessibility.Public
                && method.IsStatic
                && method.Parameters is [{ Type.SpecialType: SpecialType.System_String }, ..]))
        {
            yield return method.GetDocumentationCommentId()!;
        }

        yield return RequiredFrameworkType("System.Activator").GetDocumentationCommentId()!;
        yield return RequiredFrameworkType("System.AppDomain").GetDocumentationCommentId()!;
        yield return RequiredFrameworkType("System.Runtime.Loader.AssemblyLoadContext").GetDocumentationCommentId()!;
        yield return RequiredFrameworkType("System.Reflection.Emit.DynamicMethod")
            .ContainingNamespace.GetDocumentationCommentId()!;
    }

    private static INamedTypeSymbol RequiredFrameworkType(string metadataName) =>
        FrameworkCompilation.GetTypeByMetadataName(metadataName)
        ?? throw new InvalidOperationException(
            $"The selected framework does not define {metadataName}; the policy census cannot be evaluated.");

    private static IEnumerable<string> ForbiddenRuntimeReferences(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader pe = new(stream);
        MetadataReader reader = pe.GetMetadataReader();
        string assemblyName = Path.GetFileName(assemblyPath);

        foreach (TypeReferenceHandle handle in reader.TypeReferences)
        {
            TypeReference type = reader.GetTypeReference(handle);
            string namespaceName = reader.GetString(type.Namespace);
            string typeName = reader.GetString(type.Name);

            if ((namespaceName == "System" && typeName is "Activator" or "AppDomain")
                || (namespaceName == "System.Runtime.Loader" && typeName == "AssemblyLoadContext")
                || namespaceName == "System.Reflection.Emit"
                || namespaceName.StartsWith("System.Reflection.Emit.", StringComparison.Ordinal))
            {
                yield return $"{assemblyName}: T:{namespaceName}.{typeName}";
            }
        }

        foreach (MemberReferenceHandle handle in reader.MemberReferences)
        {
            MemberReference member = reader.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            TypeReference parent = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
            string namespaceName = reader.GetString(parent.Namespace);
            string typeName = reader.GetString(parent.Name);
            string memberName = reader.GetString(member.Name);

            if ((namespaceName == "System.Reflection"
                    && typeName == "Assembly"
                    && AssemblyLoadingMethodNames.Contains(memberName))
                || (namespaceName == "System"
                    && typeName == "Type"
                    && memberName == "GetType"))
            {
                yield return $"{assemblyName}: M:{namespaceName}.{typeName}.{memberName}";
            }
        }
    }

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
                    $"Could not evaluate the assembly-loading policy for {path}."
                        + $"{Environment.NewLine}{output}{Environment.NewLine}{error}");
            }

            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement root = document.RootElement;
            Dictionary<string, string> properties = Properties.ToDictionary(
                name => name,
                name => root.GetProperty("Properties").TryGetProperty(name, out JsonElement value)
                    ? value.GetString() ?? string.Empty
                    : string.Empty,
                StringComparer.Ordinal);
            Dictionary<string, IReadOnlyList<Dictionary<string, string>>> items = [];
            foreach (string name in Items)
            {
                items[name] = root.GetProperty("Items").TryGetProperty(name, out JsonElement values)
                    ? values.EnumerateArray()
                        .Select(value => value.EnumerateObject()
                            .ToDictionary(
                                property => property.Name,
                                property => property.Value.GetString() ?? string.Empty,
                                StringComparer.Ordinal))
                        .ToArray()
                    : [];
            }

            return new(properties, items);
        });

    private const string AnalyzerPackage = "Microsoft.CodeAnalysis.BannedApiAnalyzers";
    private const string BannedApiRule = "RS0030";
    private const string BannedSymbolsFile = "BannedSymbols.InspectionProduct.txt";
    private const string ProductMarker = "IsInspectionProductProject";

    private static readonly string[] Properties =
    [
        ProductMarker,
        "RunAnalyzers",
        "RunAnalyzersDuringBuild",
        "TargetPath",
        "WarningsAsErrors",
        "WarningsNotAsErrors",
        "NoWarn",
    ];

    private static readonly string[] Items = ["PackageReference", "AdditionalFiles"];

    private static readonly ConcurrentDictionary<string, ProjectEvaluation> Evaluations =
        new(StringComparer.Ordinal);

    private static readonly HashSet<string> AssemblyLoadingMethodNames =
        new(StringComparer.Ordinal)
        {
            "CreateInstance",
            "Load",
            "LoadFile",
            "LoadFrom",
            "LoadModule",
            "LoadWithPartialName",
            "UnsafeLoadFrom",
        };

    private static CSharpCompilation FrameworkCompilation { get; } =
        CSharpCompilation.Create(
            "AssemblyLoadingPolicyFramework",
            references:
            [
                .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                    .Split(Path.PathSeparator)
                    .Select(path => MetadataReference.CreateFromFile(path)),
            ]);

    private sealed record ProjectEvaluation(
        Dictionary<string, string> Properties,
        Dictionary<string, IReadOnlyList<Dictionary<string, string>>> Items);
}
