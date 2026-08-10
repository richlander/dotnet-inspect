using System.Runtime.CompilerServices;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.Research;
using DotnetInspector.Models;
using DotnetInspector.Queries;

namespace DotnetInspector.Tests;

public sealed class LayeringTests
{
    [Fact]
    public void MetadataAndInstructions_DoNotReferenceEachOther()
    {
        Assert.DoesNotContain(
            typeof(AssemblyInspectionSession).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "ILInspector.Instructions");
        Assert.DoesNotContain(
            typeof(InstructionProducer).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "ILInspector.Metadata");
    }

    [Fact]
    public void CoreQueries_DoNotAcquireResearchOrDecompilerProjects()
    {
        string project = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "src",
            "DotnetInspector.Queries",
            "DotnetInspector.Queries.csproj");
        string[] closure = CommandErrorOwnershipTests.ProjectClosure(project)
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToArray();

        Assert.DoesNotContain("ILInspector.Research", closure);
        Assert.DoesNotContain("ILInspector.Decompiler", closure);
        Assert.Equal(
            "DotnetInspector.Queries",
            typeof(ApiComparisonQuery).Assembly.GetName().Name);
    }

    [Fact]
    public void ImplementationQuery_ReturnsResearchOwnedPresentationNeutralResult()
    {
        Assert.Equal(
            "ILInspector.Research",
            typeof(ImplementationDiffResult).Assembly.GetName().Name);
        Assert.DoesNotContain(
            typeof(ImplementationDiffResult).Assembly
                .GetReferencedAssemblies(),
            reference => reference.Name == "Markout");
    }

    [Fact]
    public void Metadata_FriendsOnlyTestAssemblies()
    {
        string[] friends = typeof(AssemblyInspectionSession).Assembly
            .GetCustomAttributes(typeof(InternalsVisibleToAttribute), inherit: false)
            .Cast<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName.Split(',')[0])
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected =
        [
            "DotnetInspector.MetadataRendering.Tests",
            "ILInspector.Metadata.Tests",
            "dotnet-inspect.Tests",
        ];

        Assert.Equal(expected, friends);
    }

    [Fact]
    public void Cli_DoesNotReferenceRawMetadataReaders()
    {
        using var stream = File.OpenRead(typeof(LibraryInspection).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var referencedTypes = reader.TypeReferences
            .Select(handle => reader.GetTypeReference(handle))
            .Select(type => $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}")
            .ToList();

        Assert.DoesNotContain(
            "System.Reflection.PortableExecutable.PEReader",
            referencedTypes);
        Assert.DoesNotContain(
            "System.Reflection.Metadata.MetadataReader",
            referencedTypes);
    }

    [Fact]
    public void BrowserDependencies_UsesProductQueriesAndCompileAssetSelection()
    {
        string engineSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "prototypes",
            "inspect-web",
            "engine",
            "Program.cs"));
        string browserSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "prototypes",
            "inspect-web",
            "src",
            "app.js"));

        Assert.Contains(
            ".Add(AssemblyReferencesQuery.Definition, AssemblyReferencesQuery.Execute)",
            engineSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "[AssemblyReferencesQuery.Definition]",
            engineSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "queryResults.Get(\n                                AssemblyReferencesQuery.Definition)",
            engineSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".AssemblyReferences",
            engineSource,
            StringComparison.Ordinal);
        Assert.Equal(
            4,
            CountOccurrences(
                engineSource,
                "PackageCompileAssetSelector.Select("));
        Assert.Contains(
            "selection.FindAsset(assemblyId)",
            engineSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ParseCompileAsset(",
            engineSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FrameworkPriority(",
            engineSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".FullName.StartsWith(prefix",
            engineSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".FullName.StartsWith($\"lib/",
            engineSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "return parts.Length == 3",
            engineSource,
            StringComparison.Ordinal);
        Assert.Equal(
            5,
            CountOccurrences(
                engineSource,
                "GetDirectPackageAssemblyEntries("));
        Assert.Equal(
            5,
            CountOccurrences(
                engineSource,
                "ReadSelectedPackageAssemblies("));
        Assert.Equal(
            3,
            CountOccurrences(
                engineSource,
                "ReadSelectedPackageAssembly("));
        Assert.Contains(
            "assemblyId: item.assemblyId",
            browserSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "assembly: item.assembly",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "string QueryId",
            engineSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "string MetadataId",
            engineSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "type.queryId ?? type.id",
            browserSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "compareFrameworks",
            browserSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "frameworkTier",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "TfmSelector.GetTfmPriority(group.Framework)",
            engineSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "graphTargetForSvgNode(callGraph, node)",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "target.kind === \"External\"",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "libraryKey(item).toLowerCase() === target.assembly.toLowerCase()",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "target.id === \"n0\"",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "(item.metadataId ?? item.queryId ?? item.id) === target.typeFullName",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "(overload.bodySelectors ?? []).some(body =>",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "body.selectorKey === target.selectorKey",
            browserSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "bodyTokens",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "group.overloads[index].graphSelectorKey === target.selectorKey",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "CallGraphMemberResolver.Resolve(",
            engineSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "CallGraphMemberResolver.CreateSelector(",
            engineSource,
            StringComparison.Ordinal);
        Assert.Contains(
            ".CreateBodySelectors(type, member)",
            engineSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "activatePackage(pkg",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "${escapeHtml(item)}</option>",
            browserSource,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(
                browserSource,
                "state.selectedBodyTarget = bodyTarget;"));
        Assert.Contains(
            "state.selectedBodyTarget = view.bodyTarget ?? null;",
            browserSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "paramNamesFromSig",
            browserSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "paramSig",
            browserSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "paramSig",
            engineSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "simpleTypeName(",
            browserSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SimpleTypeName(",
            engineSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ACCESS_ORDER",
            browserSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "function accessBucket",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "AccessibilityDescriptors(identifiedTypes)",
            engineSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "[`t${index}`, node.id]",
            browserSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "shortTypeName(node.displayName), node.id",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "memberRequestIsCurrent(signature)",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "!item.isRuntimePack",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "Workspace restore was incomplete:",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "navigationSeq !== state.navigationSeq",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "state.workspaceDependencies[key] = []",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "assembly?.StartsWith(\"Microsoft.Extensions\"",
            engineSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "resolveNodeLabel",
            browserSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "No direct lib/{targetFramework} implementation assemblies",
            engineSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserDeployment_OnlyRunsFromMainPush()
    {
        string workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "deploy-inspect-web.yml"));

        Assert.Contains("push:\n    branches:\n      - main", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("workflow_dispatch", workflow, StringComparison.Ordinal);
    }

    static int CountOccurrences(string value, string search)
    {
        int count = 0;
        for (int index = 0;
            (index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0;
            index += search.Length)
        {
            count++;
        }

        return count;
    }

    static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root containing dotnet-inspect.slnx.");
    }
}
