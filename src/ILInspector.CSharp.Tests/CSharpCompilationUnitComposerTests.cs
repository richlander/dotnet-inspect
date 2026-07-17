using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpCompilationUnitComposerTests
{
    [Fact]
    public void ComposeEmitsPragmaAttributesUsingsAndTypes()
    {
        var type = new ApiType
        {
            Namespace = "Sample",
            Name = "Widget",
            Kind = "class",
        };

        var spec = new CSharpCompilationUnitSpec(
            AssemblyAttributes: ["System.Reflection.AssemblyMetadata(\"k\", \"v\")"],
            ModuleAttributes: ["System.Security.UnverifiableCode"],
            Usings: ["System.Collections.Generic", "System"],
            PrintRequests: [new CSharpTypePrintRequest(type)]);

        string source = CSharpCompilationUnitComposer.Compose(spec);

        Assert.StartsWith("#pragma warning disable", source);
        Assert.Contains("[assembly: System.Reflection.AssemblyMetadata(\"k\", \"v\")]", source);
        Assert.Contains("[module: System.Security.UnverifiableCode]", source);
        Assert.Contains("using System;", source);
        Assert.Contains("using System.Collections.Generic;", source);
        Assert.Contains("namespace Sample", source);
        Assert.Contains("class Widget", source);

        // Usings are ordinal-ordered: System before System.Collections.Generic.
        Assert.True(
            source.IndexOf("using System;", StringComparison.Ordinal)
                < source.IndexOf("using System.Collections.Generic;", StringComparison.Ordinal));
    }

    [Fact]
    public void ComposeEscapesAndDeduplicatesUsings()
    {
        var spec = new CSharpCompilationUnitSpec(
            AssemblyAttributes: [],
            ModuleAttributes: [],
            Usings: ["System", "System", "Some.namespace.Value"],
            PrintRequests: []);

        string source = CSharpCompilationUnitComposer.Compose(spec);

        // Reserved-keyword segment gets @-escaped, duplicates collapse.
        Assert.Contains("using Some.@namespace.Value;", source);
        Assert.Equal(1, CountOccurrences(source, "using System;"));
    }

    static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
