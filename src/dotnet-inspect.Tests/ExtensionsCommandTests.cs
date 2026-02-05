using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for extension method discovery.
/// </summary>
public class ExtensionsCommandTests
{
    [Fact]
    public void Extract_DetectsExtensionMethods()
    {
        var assemblyPath = typeof(ExtensionsCommandTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        // Find our sample extension class
        var extClass = surface.Types.FirstOrDefault(t => t.Name == "SampleExtensions");
        Assert.NotNull(extClass);
        Assert.True(extClass.IsStatic);

        // Find extension methods
        var extMethod = extClass.Members?.FirstOrDefault(m => m.Name == "ToUpperCase" && m.IsExtension);
        Assert.NotNull(extMethod);
        Assert.True(extMethod.IsExtension);
        Assert.NotNull(extMethod.ExtendedType);
        Assert.Contains("string", extMethod.ExtendedType, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_DetectsGenericExtensionMethods()
    {
        var assemblyPath = typeof(ExtensionsCommandTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var extClass = surface.Types.FirstOrDefault(t => t.Name == "SampleExtensions");
        Assert.NotNull(extClass);

        // Find generic extension method
        var extMethod = extClass.Members?.FirstOrDefault(m => m.Name == "FirstOrNull");
        Assert.NotNull(extMethod);
        Assert.True(extMethod.IsExtension);
        Assert.Contains("IEnumerable", extMethod.ExtendedType);
    }

    [Fact]
    public void Extract_NonExtensionStaticMethodsAreNotMarkedAsExtension()
    {
        var assemblyPath = typeof(ExtensionsCommandTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var extClass = surface.Types.FirstOrDefault(t => t.Name == "SampleExtensions");
        Assert.NotNull(extClass);

        // Regular static method (not an extension)
        var staticMethod = extClass.Members?.FirstOrDefault(m => m.Name == "RegularStaticMethod");
        Assert.NotNull(staticMethod);
        Assert.False(staticMethod.IsExtension);
        Assert.Null(staticMethod.ExtendedType);
    }

    [Fact]
    public void Extract_InstanceMethodsAreNotMarkedAsExtension()
    {
        var assemblyPath = typeof(ExtensionsCommandTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        // Non-static class shouldn't have extension methods
        var normalClass = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(normalClass);
        
        var anyExtensions = normalClass.Members?.Any(m => m.IsExtension) ?? false;
        Assert.False(anyExtensions);
    }

    [Fact]
    public void MatchesTargetType_ExactMatch()
    {
        // Test exact matching logic
        Assert.True(MatchesTargetType("System.String", "String"));
        Assert.True(MatchesTargetType("String", "String"));
        Assert.True(MatchesTargetType("System.Net.Http.HttpClient", "HttpClient"));
    }

    [Fact]
    public void MatchesTargetType_GenericMatch()
    {
        Assert.True(MatchesTargetType("System.Collections.Generic.IEnumerable`1", "IEnumerable"));
        Assert.True(MatchesTargetType("IEnumerable`1", "IEnumerable"));
    }

    [Fact]
    public void MatchesTargetType_NoMatch()
    {
        Assert.False(MatchesTargetType("System.String", "Int32"));
        Assert.False(MatchesTargetType("HttpClient", "HttpContent"));
    }

    [Fact]
    public void NormalizeTypeName_StripsGenericArguments()
    {
        Assert.Equal("IEnumerable", NormalizeTypeName("IEnumerable<T>"));
        Assert.Equal("Dictionary", NormalizeTypeName("Dictionary<TKey, TValue>"));
        Assert.Equal("List", NormalizeTypeName("List<int>"));
    }

    [Fact]
    public void UnwrapAsyncType_UnwrapsTask()
    {
        Assert.Equal("HttpResponseMessage", UnwrapAsyncType("Task<HttpResponseMessage>"));
        Assert.Equal("String", UnwrapAsyncType("ValueTask<String>"));
        Assert.Equal("", UnwrapAsyncType("Task"));
        Assert.Equal("Int32", UnwrapAsyncType("Int32"));
    }

    // Helper methods that mirror the logic in ExtensionsCommand
    private static bool MatchesTargetType(string extendedType, string targetType)
    {
        var normalizedExtended = NormalizeTypeName(extendedType);
        var normalizedTarget = NormalizeTypeName(targetType);

        if (normalizedExtended.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalizedExtended.EndsWith("." + normalizedTarget, StringComparison.OrdinalIgnoreCase))
            return true;

        var extBase = normalizedExtended.Split('`')[0];
        var targetBase = normalizedTarget.Split('`')[0];
        if (extBase.Equals(targetBase, StringComparison.OrdinalIgnoreCase) ||
            extBase.EndsWith("." + targetBase, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string NormalizeTypeName(string typeName)
    {
        var angleIdx = typeName.IndexOf('<');
        if (angleIdx > 0) typeName = typeName.Substring(0, angleIdx);
        return typeName;
    }

    private static string UnwrapAsyncType(string type)
    {
        if (type.StartsWith("Task<") && type.EndsWith(">"))
            return type.Substring(5, type.Length - 6);
        if (type.StartsWith("ValueTask<") && type.EndsWith(">"))
            return type.Substring(10, type.Length - 11);
        if (type.StartsWith("Task") && !type.Contains("<"))
            return "";
        return type;
    }
}

/// <summary>
/// Sample extension class for testing extension method detection.
/// </summary>
public static class SampleExtensions
{
    /// <summary>
    /// Extension method on string.
    /// </summary>
    public static string ToUpperCase(this string value) => value.ToUpper();

    /// <summary>
    /// Extension method with multiple parameters.
    /// </summary>
    public static string Repeat(this string value, int count) => string.Concat(Enumerable.Repeat(value, count));

    /// <summary>
    /// Generic extension method on IEnumerable.
    /// </summary>
    public static T? FirstOrNull<T>(this IEnumerable<T> source) where T : class
        => source.FirstOrDefault();

    /// <summary>
    /// Extension method on a custom type.
    /// </summary>
    public static string GetInfo(this SampleTargetType target) => target.Name;

    /// <summary>
    /// Regular static method (NOT an extension).
    /// </summary>
    public static string RegularStaticMethod(string value) => value;
}

/// <summary>
/// Sample target type for extension method testing.
/// </summary>
public class SampleTargetType
{
    public string Name { get; set; } = "";
}
