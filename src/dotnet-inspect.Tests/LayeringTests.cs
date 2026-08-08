using System.Runtime.CompilerServices;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Instructions;
using ILInspector.Metadata;
using DotnetInspector.Models;

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
            2,
            CountOccurrences(
                engineSource,
                "PackageCompileAssetSelector.Select(content, packageId, targetFramework)"));
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
            "StartsWith($\"lib/{package.Framework}/\"",
            engineSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "return parts.Length == 3",
            engineSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "assemblyId: item.assemblyId",
            browserSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "assembly: item.assembly",
            browserSource,
            StringComparison.Ordinal);
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
