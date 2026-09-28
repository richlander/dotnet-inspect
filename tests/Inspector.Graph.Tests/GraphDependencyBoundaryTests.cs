using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Inspector.Graph.Tests;

public sealed class GraphDependencyBoundaryTests
{
    [Fact]
    public void ProductionProjectHasNoProjectReferenceClosure()
    {
        string root = FindRepoRoot();
        string project = Path.Combine(
            root,
            "src",
            "Inspector.Graph",
            "Inspector.Graph.csproj");

        IReadOnlySet<string> closure = ProjectReferenceClosure(project);

        Assert.Equal(
            [Path.GetFullPath(project)],
            closure.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void DirectConsumerReferencesOnlyGraph()
    {
        string root = FindRepoRoot();
        string project = Path.Combine(
            root,
            "tests",
            "Inspector.Graph.Consumer",
            "Inspector.Graph.Consumer.csproj");

        Assert.Equal(
            [
                Path.Combine(
                    root,
                    "src",
                    "Inspector.Graph",
                    "Inspector.Graph.csproj"),
            ],
            ReadEvaluatedProjectReferences(project));
    }

    [Fact]
    public void CompiledGraphReferencesOnlyRuntimeAssemblies()
    {
        HashSet<string> runtimeAssemblies = ((string?)
                AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new InvalidOperationException(
                    "The runtime did not publish its trusted platform assemblies."))
            .Split(Path.PathSeparator)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assembly graph = typeof(GraphDocument<
            Subject,
            Relationship,
            Receipt,
            Characteristic,
            Limit,
            Failure>).Assembly;

        string[] nonRuntimeReferences =
        [
            .. graph.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name =>
                    name is not null
                    && !runtimeAssemblies.Contains(name))
                .Select(static name => name!),
        ];

        Assert.Empty(nonRuntimeReferences);
    }

    private static IReadOnlySet<string> ProjectReferenceClosure(
        string rootProject)
    {
        var closure = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(Path.GetFullPath(rootProject));
        while (pending.TryDequeue(out string? project))
        {
            if (!closure.Add(project))
                continue;

            foreach (string reference
                in ReadEvaluatedProjectReferences(project))
            {
                pending.Enqueue(reference);
            }
        }
        return closure;
    }

    private static IReadOnlyList<string> ReadEvaluatedProjectReferences(
        string project)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add("msbuild");
        process.StartInfo.ArgumentList.Add(project);
        process.StartInfo.ArgumentList.Add("-getItem:ProjectReference");
        process.StartInfo.ArgumentList.Add("-p:Configuration=Release");
        process.StartInfo.ArgumentList.Add("-nologo");
        process.StartInfo.ArgumentList.Add("-v:q");

        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        bool timedOut = !process.WaitForExit(milliseconds: 30_000);
        if (timedOut)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        Task.WaitAll(output, error);
        if (timedOut || process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not evaluate ProjectReference for {project}."
                + $"{Environment.NewLine}{output.Result}"
                + $"{Environment.NewLine}{error.Result}");
        }

        using JsonDocument document = JsonDocument.Parse(output.Result);
        return
        [
            .. document.RootElement
                .GetProperty("Items")
                .GetProperty("ProjectReference")
                .EnumerateArray()
                .Select(reference => Path.GetFullPath(
                    reference.GetProperty("FullPath").GetString()
                    ?? throw new InvalidOperationException(
                        "Evaluated ProjectReference did not include FullPath.")))
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory =
            new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(
                Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the repository root.");
    }
}
