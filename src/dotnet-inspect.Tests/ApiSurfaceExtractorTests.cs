using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Inspectors;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for API surface extraction, including signature formatting.
/// </summary>
public class ApiSurfaceExtractorTests
{
    [Fact]
    public void Extract_IncludesParameterNamesInMethodSignatures()
    {
        // Use the test assembly itself - we know its methods have parameter names
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        // Find a method with known parameters
        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);

        var method = testType.Members?.FirstOrDefault(m => m.Name == "MethodWithParameters");
        Assert.NotNull(method);

        // Verify parameter names are included, not just types
        Assert.Contains("int count", method.Signature);
        Assert.Contains("string name", method.Signature);
    }

    [Fact]
    public void Extract_HandlesMethodWithNoParameters()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);

        var method = testType.Members?.FirstOrDefault(m => m.Name == "MethodWithNoParameters");
        Assert.NotNull(method);

        Assert.Contains("()", method.Signature);
    }

    [Fact]
    public void Extract_HandlesGenericMethodParameters()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);

        var method = testType.Members?.FirstOrDefault(m => m.Name == "GenericMethod");
        Assert.NotNull(method);

        // Should have both the generic type and parameter name
        Assert.Contains("T item", method.Signature);
    }
}

/// <summary>
/// Sample class used for testing signature extraction.
/// </summary>
public class SampleClassForTesting
{
    public void MethodWithParameters(int count, string name) { }
    public void MethodWithNoParameters() { }
    public void GenericMethod<T>(T item) { }
}
