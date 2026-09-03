using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ILInspector.Metadata;

namespace DependencyPolicy;

internal static class RepositoryGraphReader
{
    internal static RepositoryDependencyGraph Read(
        string repository,
        string solution,
        string configuration,
        string dotnetHost,
        DependencyPolicyDocument policy)
    {
        string graphPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-dependency-policy-{Guid.NewGuid():N}.json");
        try
        {
            GenerateRestoreGraph(
                repository,
                solution,
                configuration,
                dotnetHost,
                graphPath);
            return ParseRestoreGraph(
                repository,
                configuration,
                dotnetHost,
                graphPath,
                policy);
        }
        finally
        {
            File.Delete(graphPath);
        }
    }

    private static void GenerateRestoreGraph(
        string repository,
        string solution,
        string configuration,
        string dotnetHost,
        string graphPath)
    {
        ProcessResult result = RunDotnet(
            dotnetHost,
            repository,
            [
                "msbuild",
                solution,
                "-t:GenerateRestoreGraphFile",
                $"-p:RestoreGraphOutputPath={graphPath}",
                $"-p:Configuration={configuration}",
                "-p:MSBuildEnableWorkloadResolver=false",
                "-nologo",
                "-v:q",
            ]);
        if (result.TimedOut || result.ExitCode != 0)
        {
            throw new DependencyPolicyException(
                $"Could not evaluate Release dependency graph for "
                + $"'{solution}'."
                + $"{Environment.NewLine}stdout:{Environment.NewLine}"
                + result.StandardOutput
                + $"{Environment.NewLine}stderr:{Environment.NewLine}"
                + result.StandardError);
        }

        if (!File.Exists(graphPath))
        {
            throw new DependencyPolicyException(
                $"MSBuild did not produce dependency graph '{graphPath}'.");
        }
    }

    private static RepositoryDependencyGraph ParseRestoreGraph(
        string repository,
        string configuration,
        string dotnetHost,
        string graphPath,
        DependencyPolicyDocument policy)
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(graphPath));
        JsonElement projects = RequiredObject(
            document.RootElement,
            "projects",
            "restore graph");
        var drafts = new Dictionary<string, ProjectDraft>(
            StringComparer.Ordinal);

        foreach (JsonProperty projectProperty in projects.EnumerateObject())
        {
            string projectPath = CanonicalRepositoryPath(
                repository,
                projectProperty.Name);
            JsonElement project = projectProperty.Value;
            JsonElement restore = RequiredObject(
                project,
                "restore",
                projectPath);
            string projectName = Path.GetFileNameWithoutExtension(projectPath);
            string relativeProjectPath = Path.GetRelativePath(
                    repository,
                    projectPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var references = new HashSet<string>(StringComparer.Ordinal);

            JsonElement frameworks = RequiredObject(
                restore,
                "frameworks",
                projectPath);
            string[] targetFrameworks = frameworks
                .EnumerateObject()
                .Select(framework => framework.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            foreach (JsonProperty framework in frameworks.EnumerateObject())
            {
                if (!framework.Value.TryGetProperty(
                        "projectReferences",
                        out JsonElement projectReferences))
                {
                    continue;
                }

                if (projectReferences.ValueKind != JsonValueKind.Object)
                {
                    throw new DependencyPolicyException(
                        $"Restore graph projectReferences for '{projectPath}' "
                        + "must be an object.");
                }

                foreach (JsonProperty reference
                    in projectReferences.EnumerateObject())
                {
                    references.Add(CanonicalRepositoryPath(
                        repository,
                        reference.Name));
                }
            }

            if (!drafts.TryAdd(
                    projectPath,
                    new(
                        projectName,
                        projectPath,
                        relativeProjectPath,
                        references,
                        targetFrameworks)))
            {
                throw new DependencyPolicyException(
                    $"Restore graph contains duplicate project '{projectPath}'.");
            }
        }

        foreach (ProjectDraft draft in drafts.Values.Where(project =>
            policy.Rules.Any(rule =>
                rule.Graphs.Contains(DependencyGraphKind.Project)
                && DependencyPattern.Selects(
                    rule,
                    project.ProjectName,
                    project.RelativeProjectPath))))
        {
            draft.ReferencePaths.Clear();
            draft.ReferencePaths.UnionWith(
                QueryProjectReferences(
                    repository,
                    draft,
                    configuration,
                    dotnetHost));
        }

        var projectNamesByPath = drafts.Values.ToDictionary(
            draft => draft.ProjectPath,
            draft => draft.ProjectName,
            StringComparer.Ordinal);
        HashSet<string> assemblyProjectPaths = AssemblyProjectClosure(
            policy,
            drafts);
        var seenProjectNames = new HashSet<string>(StringComparer.Ordinal);
        var nodes = ImmutableArray.CreateBuilder<ProjectDependencyNode>(
            drafts.Count);

        foreach (ProjectDraft draft in drafts.Values
            .OrderBy(draft => draft.ProjectName, StringComparer.Ordinal))
        {
            if (!seenProjectNames.Add(draft.ProjectName))
            {
                throw new DependencyPolicyException(
                    $"Restore graph contains duplicate project name "
                    + $"'{draft.ProjectName}'.");
            }

            string[] projectReferences = draft.ReferencePaths
                .Select(path => projectNamesByPath.TryGetValue(
                    path,
                    out string? projectName)
                    ? projectName
                    : throw new DependencyPolicyException(
                        $"Project '{draft.ProjectName}' references '{path}', "
                        + "which is absent from the evaluated graph."))
                .Order(StringComparer.Ordinal)
                .ToArray();
            (string? assemblyName, ImmutableArray<string> assemblyReferences) =
                assemblyProjectPaths.Contains(draft.ProjectPath)
                    ? ReadAssembly(
                        draft,
                        configuration,
                        dotnetHost)
                    : (null, []);
            nodes.Add(new(
                draft.ProjectName,
                draft.RelativeProjectPath,
                [.. projectReferences],
                assemblyName,
                assemblyReferences));
        }

        ImmutableArray<ProjectDependencyNode> immutableNodes =
            nodes.ToImmutable();
        ImmutableHashSet<string> platformAssemblies =
            ReadPlatformAssemblies();
        return RepositoryDependencyGraph.Create(
            immutableNodes,
            platformAssemblies);
    }

    private static (
        string? AssemblyName,
        ImmutableArray<string> References) ReadAssembly(
            ProjectDraft project,
            string configuration,
            string dotnetHost)
    {
        string candidate = QueryTargetPath(
            project,
            configuration,
            dotnetHost);
        return ReadAssemblyOutput(project.ProjectName, candidate);
    }

    internal static (
        string AssemblyName,
        ImmutableArray<string> References) ReadAssemblyOutput(
        string projectName,
        string candidate)
    {
        if (!File.Exists(candidate))
        {
            throw new DependencyPolicyException(
                $"Release target output for '{projectName}' does not "
                + $"exist: '{candidate}'. Build the solution before running "
                + "dependency policy.");
        }

        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(candidate);
            if (!session.HasMetadata)
            {
                throw new DependencyPolicyException(
                    $"Built output '{candidate}' has no managed metadata.");
            }

            AssemblyIdentityNames identity = session.IdentityNames();
            return ValidateAssemblyIdentity(candidate, identity);
        }
        catch (BadImageFormatException exception)
        {
            throw new DependencyPolicyException(
                $"Could not inspect built output '{candidate}': "
                + exception.Message);
        }
    }

    internal static (
        string AssemblyName,
        ImmutableArray<string> References) ValidateAssemblyIdentity(
        string candidate,
        AssemblyIdentityNames identity)
    {
        if (identity.Name.Length == 0)
        {
            throw new DependencyPolicyException(
                $"Built output '{candidate}' is a module without an "
                + "assembly identity.");
        }

        if (!identity.ReferencesComplete)
        {
            throw new DependencyPolicyException(
                $"Could not decode every assembly reference in "
                + $"'{candidate}'.");
        }

        return (
            identity.Name,
            identity.ReferenceNames
                .Order(StringComparer.Ordinal)
                .ToImmutableArray());
    }

    private static string QueryTargetPath(
        ProjectDraft project,
        string configuration,
        string dotnetHost)
    {
        ProcessResult result = RunDotnet(
            dotnetHost,
            Path.GetDirectoryName(project.ProjectPath)!,
            [
                "msbuild",
                project.ProjectPath,
                "-getProperty:TargetPath",
                $"-p:Configuration={configuration}",
                "-p:MSBuildEnableWorkloadResolver=false",
                "-nologo",
                "-v:q",
            ]);
        if (result.TimedOut || result.ExitCode != 0)
        {
            throw new DependencyPolicyException(
                $"Could not evaluate TargetPath for "
                + $"'{project.ProjectPath}'."
                + $"{Environment.NewLine}stdout:{Environment.NewLine}"
                + result.StandardOutput
                + $"{Environment.NewLine}stderr:{Environment.NewLine}"
                + result.StandardError);
        }

        return NormalizeTargetPath(
            project.ProjectPath,
            result.StandardOutput);
    }

    internal static string NormalizeTargetPath(
        string projectPath,
        string standardOutput)
    {
        string targetPath = standardOutput.Trim();
        if (targetPath.Length == 0
            || targetPath.Contains('\r')
            || targetPath.Contains('\n'))
        {
            throw new DependencyPolicyException(
                $"MSBuild returned an invalid TargetPath for "
                + $"'{projectPath}': '{targetPath}'.");
        }

        return Path.IsPathRooted(targetPath)
            ? Path.GetFullPath(targetPath)
            : Path.GetFullPath(
                Path.Combine(
                    Path.GetDirectoryName(projectPath)!,
                    targetPath));
    }

    private static HashSet<string> QueryProjectReferences(
        string repository,
        ProjectDraft project,
        string configuration,
        string dotnetHost)
    {
        if (project.TargetFrameworks.Length == 0)
        {
            throw new DependencyPolicyException(
                $"Restore graph project '{project.ProjectName}' has no "
                + "target frameworks.");
        }

        var references = new HashSet<string>(StringComparer.Ordinal);
        foreach (string targetFramework in project.TargetFrameworks)
        {
            ProcessResult result = RunDotnet(
                dotnetHost,
                Path.GetDirectoryName(project.ProjectPath)!,
                [
                    "msbuild",
                    project.ProjectPath,
                    "-getItem:ProjectReference",
                    $"-p:Configuration={configuration}",
                    $"-p:TargetFramework={targetFramework}",
                    "-p:MSBuildEnableWorkloadResolver=false",
                    "-nologo",
                    "-v:q",
                ]);
            if (result.TimedOut || result.ExitCode != 0)
            {
                throw new DependencyPolicyException(
                    $"Could not evaluate ProjectReference items for "
                    + $"'{project.ProjectPath}' targeting "
                    + $"'{targetFramework}'."
                    + $"{Environment.NewLine}stdout:{Environment.NewLine}"
                    + result.StandardOutput
                    + $"{Environment.NewLine}stderr:{Environment.NewLine}"
                    + result.StandardError);
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(
                    result.StandardOutput);
                JsonElement items = RequiredObject(
                    document.RootElement,
                    "Items",
                    $"evaluated project '{project.ProjectPath}'");
                if (!items.TryGetProperty(
                        "ProjectReference",
                        out JsonElement projectReferences)
                    || projectReferences.ValueKind != JsonValueKind.Array)
                {
                    throw new DependencyPolicyException(
                        $"Evaluated ProjectReference items for "
                        + $"'{project.ProjectPath}' targeting "
                        + $"'{targetFramework}' must be an array.");
                }

                foreach (JsonElement reference
                    in projectReferences.EnumerateArray())
                {
                    if (reference.ValueKind != JsonValueKind.Object
                        || !reference.TryGetProperty(
                            "FullPath",
                            out JsonElement fullPath)
                        || fullPath.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(fullPath.GetString()))
                    {
                        throw new DependencyPolicyException(
                            $"Evaluated ProjectReference item for "
                            + $"'{project.ProjectPath}' targeting "
                            + $"'{targetFramework}' has no FullPath.");
                    }

                    references.Add(CanonicalRepositoryPath(
                        repository,
                        fullPath.GetString()!));
                }
            }
            catch (JsonException exception)
            {
                throw new DependencyPolicyException(
                    $"Could not decode evaluated ProjectReference items for "
                    + $"'{project.ProjectPath}' targeting "
                    + $"'{targetFramework}': {exception.Message}");
            }
        }

        return references;
    }

    private static ProcessResult RunDotnet(
        string dotnetHost,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo = new(dotnetHost)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = StartProcess(startInfo, dotnetHost);
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        bool timedOut = !process.WaitForExit(milliseconds: 120_000);
        if (timedOut)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        Task.WaitAll(outputTask, errorTask);
        return new(
            timedOut,
            process.ExitCode,
            outputTask.Result,
            errorTask.Result);
    }

    private static Process StartProcess(
        ProcessStartInfo startInfo,
        string dotnetHost)
    {
        try
        {
            return Process.Start(startInfo)
                ?? throw new DependencyPolicyException(
                    $"Could not start '{dotnetHost}'.");
        }
        catch (Win32Exception)
        {
            throw new DependencyPolicyException(
                $"Could not start '{dotnetHost}'.");
        }
        catch (ArgumentException)
        {
            throw new DependencyPolicyException(
                $"Could not start '{dotnetHost}'.");
        }
    }

    private static HashSet<string> AssemblyProjectClosure(
        DependencyPolicyDocument policy,
        IReadOnlyDictionary<string, ProjectDraft> projects)
    {
        var closure = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<string>(
            projects.Values
                .Where(project => policy.Rules.Any(rule =>
                    rule.Graphs.Contains(DependencyGraphKind.Assembly)
                    && DependencyPattern.Selects(
                        rule,
                        project.ProjectName,
                        project.RelativeProjectPath)))
                .Select(project => project.ProjectPath));

        while (frontier.TryDequeue(out string? projectPath))
        {
            if (!closure.Add(projectPath))
            {
                continue;
            }

            if (!projects.TryGetValue(
                    projectPath,
                    out ProjectDraft? project))
            {
                throw new DependencyPolicyException(
                    $"Assembly-policy project '{projectPath}' is absent "
                    + "from the evaluated graph.");
            }

            foreach (string referencePath in project.ReferencePaths)
            {
                frontier.Enqueue(referencePath);
            }
        }

        return closure;
    }

    private static ImmutableHashSet<string> ReadPlatformAssemblies()
    {
        string runtimeDirectory = Path.GetFullPath(
            RuntimeEnvironment.GetRuntimeDirectory());
        string runtimePrefix = runtimeDirectory.EndsWith(
            Path.DirectorySeparatorChar)
            ? runtimeDirectory
            : runtimeDirectory + Path.DirectorySeparatorChar;
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string? trustedAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(trustedAssemblies))
        {
            throw new DependencyPolicyException(
                "The current runtime did not expose trusted platform "
                + "assemblies.");
        }

        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.GetFullPath)
            .Where(path => path.StartsWith(
                runtimePrefix,
                pathComparison))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToImmutableHashSet(StringComparer.Ordinal);
    }

    private static string CanonicalRepositoryPath(
        string repository,
        string path)
    {
        string fullPath = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(repository, fullPath);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new DependencyPolicyException(
                $"Restore graph path is outside the repository: '{path}'.");
        }

        return fullPath;
    }

    private static JsonElement RequiredObject(
        JsonElement parent,
        string propertyName,
        string context)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Object)
        {
            throw new DependencyPolicyException(
                $"Expected object '{propertyName}' in {context}.");
        }

        return value;
    }

    private sealed record ProjectDraft(
        string ProjectName,
        string ProjectPath,
        string RelativeProjectPath,
        HashSet<string> ReferencePaths,
        string[] TargetFrameworks);

    private sealed record ProcessResult(
        bool TimedOut,
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
