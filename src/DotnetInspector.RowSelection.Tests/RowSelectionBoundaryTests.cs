using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using DotnetInspector.RowSelection;
using DotnetInspector.RowSelectionConsumer;

namespace DotnetInspector.RowSelection.Tests;

public sealed class RowSelectionBoundaryTests
{
    [Fact]
    public void RowSelectionLanguageConsumerExercisesDeclaration()
    {
        LanguageObservation observation =
            LanguageConsumer.Inspect();
        Assert.Equal(
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Top,
            ],
            observation.Kinds);
        Assert.Equal(2, observation.HeadCount);
        Assert.Equal(3, observation.TailCount);
        Assert.Equal(4, observation.WindowStart);
        Assert.Equal(8, observation.WindowEnd);
        Assert.Equal(5, observation.TopCount);
        Assert.Equal("rank", observation.TopOrder);
        Assert.Equal(0, observation.EmptyCount);
        Assert.Equal(4, observation.PriorCount);
        Assert.Equal(5, observation.AppendedCount);
        Assert.Equal(
            typeof(InvalidOperationException),
            observation.WrongCountAccessorException);
        Assert.Equal(
            typeof(InvalidOperationException),
            observation.WrongWindowAccessorException);
        Assert.Equal(
            typeof(InvalidOperationException),
            observation.WrongOrderAccessorException);
    }

    [Fact]
    public void RowSelectionReferenceEvaluatorExercisesSurface()
    {
        ReferenceEvaluatorObservation observation =
            ReferenceEvaluatorConsumer.Evaluate();
        Assert.Equal([5, 6], observation.Values);
        Assert.Equal(
            [1, 2, 3],
            observation.RankedValues);
        Assert.Equal(
            [10, 20],
            observation.NamedKeys);
        Assert.Equal(
            [1, 2],
            observation.NamedValues[0]);
        Assert.Equal(
            [4, 5],
            observation.NamedValues[1]);
        Assert.False(observation.FailureIsSuccess);
        Assert.Equal(10, observation.FailureKey);
        Assert.Equal(2, observation.FailureStage);
        Assert.Equal(
            3,
            observation.FailureRequiredPosition);
        Assert.Equal(
            2,
            observation.FailureAvailableCount);
    }

    [Fact]
    public void RowSelectionHasOnlyFrameworkRuntimeDependencies()
    {
        string root = RepositoryRoot();
        string project = Path.Combine(
            root,
            "src",
            "DotnetInspector.RowSelection",
            "DotnetInspector.RowSelection.csproj");
        var document = XDocument.Load(project);
        Assert.DoesNotContain(
            document.Descendants(),
            element =>
                element.Name.LocalName
                is "ProjectReference"
                or "PackageReference");

        string assetsPath = Path.Combine(
            root,
            "artifacts",
            "obj",
            "DotnetInspector.RowSelection",
            "project.assets.json");
        Assert.True(
            File.Exists(assetsPath),
            $"Build or restore the product project before running this gate: {assetsPath}");
        using JsonDocument assets =
            JsonDocument.Parse(
                File.ReadAllText(assetsPath));
        List<string> productAssets =
            ProductCompileRuntimeAndNativeAssets(
                assets.RootElement);
        Assert.Empty(productAssets);

        string[] trustedPlatformAssemblies =
            ((string?)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")
                ?? string.Empty)
            .Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries);
        string runtimeDirectory =
            RuntimeEnvironment.GetRuntimeDirectory()
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
        var frameworkAssemblies =
            trustedPlatformAssemblies
                .Where(path =>
                    string.Equals(
                        Path.GetDirectoryName(path)?
                            .TrimEnd(
                                Path.DirectorySeparatorChar,
                                Path.AltDirectorySeparatorChar),
                        runtimeDirectory,
                        StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
        Assembly productAssembly =
            typeof(RowSelectionStage<>).Assembly;
        Assert.All(
            productAssembly.GetReferencedAssemblies(),
            reference =>
                Assert.Contains(
                    reference.Name,
                    frameworkAssemblies));
    }

    private static List<string>
        ProductCompileRuntimeAndNativeAssets(
            JsonElement assets)
    {
        var productAssets = new List<string>();
        foreach (JsonProperty target
            in assets.GetProperty("targets")
                .EnumerateObject())
        {
            foreach (JsonProperty library
                in target.Value.EnumerateObject())
            {
                string? type =
                    library.Value.TryGetProperty(
                        "type",
                        out JsonElement typeElement)
                        ? typeElement.GetString()
                        : null;
                if (type is "project")
                {
                    productAssets.Add(
                        $"{target.Name}:{library.Name}:project");
                }

                AddAssets(
                    productAssets,
                    target.Name,
                    library,
                    "compile");
                AddAssets(
                    productAssets,
                    target.Name,
                    library,
                    "runtime");
                AddAssets(
                    productAssets,
                    target.Name,
                    library,
                    "native");
                if (library.Value.TryGetProperty(
                        "runtimeTargets",
                        out JsonElement runtimeTargets))
                {
                    foreach (JsonProperty asset
                        in runtimeTargets.EnumerateObject())
                    {
                        if (asset.Name != "_._")
                        {
                            productAssets.Add(
                                $"{target.Name}:{library.Name}:runtimeTargets:{asset.Name}");
                        }
                    }
                }
            }
        }

        return productAssets;
    }

    private static void AddAssets(
        List<string> productAssets,
        string target,
        JsonProperty library,
        string group)
    {
        if (!library.Value.TryGetProperty(
                group,
                out JsonElement assets))
        {
            return;
        }

        foreach (JsonProperty asset
            in assets.EnumerateObject())
        {
            if (asset.Name != "_._")
            {
                productAssets.Add(
                    $"{target}:{library.Name}:{group}:{asset.Name}");
            }
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory =
                 new DirectoryInfo(
                     AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find the repository root.");
    }
}
