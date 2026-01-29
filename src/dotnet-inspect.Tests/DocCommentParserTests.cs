using DotnetInspector.Inspectors;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for XML doc comment parsing, including sample reference extraction.
/// </summary>
public class DocCommentParserTests
{
    private readonly DocCommentParser _parser = new();

    [Fact]
    public void ExtractTypeDocComment_ParsesSandcastleCodeSource()
    {
        var source = """
            /// <summary>
            /// A sample class.
            /// </summary>
            /// <example>
            ///   <code lang="cs" source="../../samples/Demo.cs" region="BasicUsage" title="Basic usage example" />
            /// </example>
            public class MyClass { }
            """;

        var result = _parser.ExtractTypeDocComment(source, "MyClass");

        Assert.NotNull(result);
        Assert.Equal("A sample class.", result.Summary);
        Assert.NotNull(result.Samples);
        Assert.Single(result.Samples);

        var sample = result.Samples[0];
        Assert.Equal("../../samples/Demo.cs", sample.RelativePath);
        Assert.Equal("BasicUsage", sample.Region);
        Assert.Equal("Basic usage example", sample.Description);
    }

    [Fact]
    public void ExtractTypeDocComment_ParsesMultipleSandcastleSamples()
    {
        var source = """
            /// <summary>Test class.</summary>
            /// <example>
            ///   <code lang="cs" source="../samples/First.cs" region="One" title="First example" />
            ///   <code lang="cs" source="../samples/Second.cs" region="Two" title="Second example" />
            /// </example>
            public class TestClass { }
            """;

        var result = _parser.ExtractTypeDocComment(source, "TestClass");

        Assert.NotNull(result);
        Assert.NotNull(result.Samples);
        Assert.Equal(2, result.Samples.Count);

        Assert.Equal("../samples/First.cs", result.Samples[0].RelativePath);
        Assert.Equal("One", result.Samples[0].Region);

        Assert.Equal("../samples/Second.cs", result.Samples[1].RelativePath);
        Assert.Equal("Two", result.Samples[1].Region);
    }

    [Fact]
    public void ExtractTypeDocComment_ParsesSeealsoHref()
    {
        var source = """
            /// <summary>A documented type.</summary>
            /// <seealso href="../../samples/Usage.cs">Usage examples</seealso>
            public class DocumentedType { }
            """;

        var result = _parser.ExtractTypeDocComment(source, "DocumentedType");

        Assert.NotNull(result);
        Assert.NotNull(result.Samples);
        Assert.Single(result.Samples);

        var sample = result.Samples[0];
        Assert.Equal("../../samples/Usage.cs", sample.RelativePath);
        Assert.Equal("Usage examples", sample.Description);
        Assert.Null(sample.Region);
    }

    [Fact]
    public void ExtractTypeDocComment_ParsesSeealsoWithRegion()
    {
        var source = """
            /// <summary>Test.</summary>
            /// <seealso href="../samples/Demo.cs" region="QuickStart">Quick start guide</seealso>
            public class Test { }
            """;

        var result = _parser.ExtractTypeDocComment(source, "Test");

        Assert.NotNull(result);
        Assert.NotNull(result.Samples);
        Assert.Single(result.Samples);

        Assert.Equal("../samples/Demo.cs", result.Samples[0].RelativePath);
        Assert.Equal("QuickStart", result.Samples[0].Region);
        Assert.Equal("Quick start guide", result.Samples[0].Description);
    }

    [Fact]
    public void ExtractTypeDocComment_IgnoresHttpUrls()
    {
        var source = """
            /// <summary>A type with external links.</summary>
            /// <seealso href="https://example.com/docs">External docs</seealso>
            /// <seealso href="http://example.com/api">API reference</seealso>
            public class ExternalLinks { }
            """;

        var result = _parser.ExtractTypeDocComment(source, "ExternalLinks");

        Assert.NotNull(result);
        Assert.Null(result.Samples); // HTTP URLs should be ignored
    }

    [Fact]
    public void ExtractTypeDocComment_IgnoresNonCodeFiles()
    {
        var source = """
            /// <summary>A type.</summary>
            /// <seealso href="../docs/readme.md">Documentation</seealso>
            /// <seealso href="../images/diagram.png">Diagram</seealso>
            public class WithNonCodeLinks { }
            """;

        var result = _parser.ExtractTypeDocComment(source, "WithNonCodeLinks");

        Assert.NotNull(result);
        Assert.Null(result.Samples); // Non-code files should be ignored
    }

    [Fact]
    public void ExtractTypeDocComment_NormalizesBackslashes()
    {
        var source = """
            /// <summary>Test.</summary>
            /// <example>
            ///   <code lang="cs" source="..\samples\Demo.cs" region="Test" title="Test" />
            /// </example>
            public class BackslashTest { }
            """;

        var result = _parser.ExtractTypeDocComment(source, "BackslashTest");

        Assert.NotNull(result);
        Assert.NotNull(result.Samples);
        Assert.Single(result.Samples);
        Assert.Equal("../samples/Demo.cs", result.Samples[0].RelativePath);
    }

    [Fact]
    public void ExtractTypeDocComment_CombinesBothPatterns()
    {
        var source = """
            /// <summary>A comprehensive example.</summary>
            /// <example>
            ///   <code lang="cs" source="../../samples/Basic.cs" region="Usage" title="Basic usage" />
            /// </example>
            /// <seealso href="../../samples/Advanced.cs">Advanced examples</seealso>
            public class Combined { }
            """;

        var result = _parser.ExtractTypeDocComment(source, "Combined");

        Assert.NotNull(result);
        Assert.NotNull(result.Samples);
        Assert.Equal(2, result.Samples.Count);

        // Sandcastle-style sample
        Assert.Equal("../../samples/Basic.cs", result.Samples[0].RelativePath);
        Assert.Equal("Usage", result.Samples[0].Region);

        // Seealso-style sample
        Assert.Equal("../../samples/Advanced.cs", result.Samples[1].RelativePath);
        Assert.Null(result.Samples[1].Region);
    }

    [Fact]
    public void ExtractTypeDocComment_HandlesNoSamples()
    {
        var source = """
            /// <summary>A plain documented type with no samples.</summary>
            /// <remarks>This type has remarks but no sample links.</remarks>
            public class PlainType { }
            """;

        var result = _parser.ExtractTypeDocComment(source, "PlainType");

        Assert.NotNull(result);
        Assert.Equal("A plain documented type with no samples.", result.Summary);
        Assert.Null(result.Samples);
    }

    [Fact]
    public void ExtractTypeDocComment_SupportsFSharpFiles()
    {
        var source = """
            /// <summary>F# interop type.</summary>
            /// <seealso href="../samples/Demo.fs">F# example</seealso>
            /// <seealso href="../samples/Script.fsx">F# script</seealso>
            public class FSharpInterop { }
            """;

        var result = _parser.ExtractTypeDocComment(source, "FSharpInterop");

        Assert.NotNull(result);
        Assert.NotNull(result.Samples);
        Assert.Equal(2, result.Samples.Count);
        Assert.Equal("../samples/Demo.fs", result.Samples[0].RelativePath);
        Assert.Equal("../samples/Script.fsx", result.Samples[1].RelativePath);
    }
}
