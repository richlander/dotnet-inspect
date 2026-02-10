using System.Reflection.PortableExecutable;
using DotnetInspector.Commands;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;

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
    public void FindAllExtensions_ReturnsAllExtensionsFromAssembly()
    {
        var assemblyPath = typeof(ExtensionsCommandTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);

        var extensions = ExtensionMethodScanner.FindAllExtensions(stream, includeAll: true).ToList();

        // Should find our sample extensions (ToUpperCase, Repeat, FirstOrNull, GetInfo)
        Assert.True(extensions.Count >= 4);
        Assert.Contains(extensions, e => e.MethodName == "ToUpperCase");
        Assert.Contains(extensions, e => e.MethodName == "FirstOrNull");
        Assert.Contains(extensions, e => e.MethodName == "GetInfo");

        // Each result should have an extended type
        Assert.All(extensions, e => Assert.False(string.IsNullOrEmpty(e.ExtendedType)));
    }

    [Fact]
    public void FindAllExtensions_FindsCSharp14ExtensionBlockMembers()
    {
        // MetadataReaderExtensions uses C# 14 extension blocks:
        //   extension(MetadataReader reader) { GetFullTypeName (3 overloads), GetGenericParameterName }
        //   extension(TypeDefinition typeDef) { IsPublic (property) }
        var testDir = Path.GetDirectoryName(typeof(ExtensionsCommandTests).Assembly.Location)!;
        var targetPath = Path.Combine(testDir, "DotnetInspector.Metadata.dll");
        Assert.True(File.Exists(targetPath), $"DotnetInspector.Metadata.dll not found at {targetPath}");

        using var stream = File.OpenRead(targetPath);
        var extensions = ExtensionMethodScanner.FindAllExtensions(stream).ToList();

        // C# 14 extension methods on MetadataReader
        var readerExtensions = extensions.Where(e =>
            e.ExtendedType.Contains("MetadataReader") &&
            !e.ExtendedType.Contains("TypeDefinition")).ToList();
        Assert.Contains(readerExtensions, e => e.MethodName == "GetFullTypeName");
        Assert.Equal(3, readerExtensions.Count(e => e.MethodName == "GetFullTypeName"));
        Assert.Contains(readerExtensions, e => e.MethodName == "GetGenericParameterName");

        // C# 14 extension property on TypeDefinition
        var typeDefExtensions = extensions.Where(e =>
            e.ExtendedType.Contains("TypeDefinition")).ToList();
        Assert.Contains(typeDefExtensions, e => e.MethodName == "IsPublic" && e.Kind == "property");
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

    [Fact]
    public void FormatResults_NoDuplicateRowsForOverloads()
    {
        // Overloads with different signatures should be collapsed into a single row.
        var results = CreateOverloadResults();
        var collapsed = ExtensionsCommand.CollapseOverloads(results);

        var output = ExtensionsOutputFormatter.FormatResults("IEnumerable<T>", collapsed);
        var tableRows = output.Split('\n')
            .Where(l => l.StartsWith("| ") && !l.StartsWith("| -") && !l.Contains("Name"))
            .ToList();

        // Should produce a single row, not 3 duplicate rows
        Assert.Single(tableRows);
        Assert.Contains("3 overloads", tableRows[0]);
    }

    [Fact]
    public void CollapseOverloads_PreservesAllSignatures()
    {
        var results = CreateOverloadResults();
        var collapsed = ExtensionsCommand.CollapseOverloads(results);

        Assert.Single(collapsed);
        var entry = collapsed[0];
        Assert.Equal(3, entry.Overloads);
        Assert.NotNull(entry.Signatures);
        Assert.Equal(3, entry.Signatures!.Count);
        Assert.Null(entry.Signature); // single signature cleared when multiple exist
    }

    [Fact]
    public void CollapseOverloads_SingleMethod_NoOverloadFields()
    {
        var results = new List<ExtensionMethodResult>
        {
            new()
            {
                MethodName = "First",
                Kind = "method",
                ExtensionClass = "System.Linq.Enumerable",
                Assembly = "System.Linq",
                Signature = "TSource First(this IEnumerable<TSource> source)",
                Source = "runtime",
                SourceVersion = "10.0.0"
            }
        };

        var collapsed = ExtensionsCommand.CollapseOverloads(results);

        Assert.Single(collapsed);
        var entry = collapsed[0];
        Assert.Null(entry.Overloads);
        Assert.Null(entry.Signatures);
        Assert.Equal("TSource First(this IEnumerable<TSource> source)", entry.Signature);
    }

    private static List<ExtensionMethodResult> CreateOverloadResults() =>
    [
        new()
        {
            MethodName = "ToDictionary",
            Kind = "method",
            ExtensionClass = "System.Linq.Enumerable",
            Assembly = "System.Linq",
            Signature = "Dictionary<TKey, TSource> ToDictionary(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)",
            Source = "runtime",
            SourceVersion = "10.0.0"
        },
        new()
        {
            MethodName = "ToDictionary",
            Kind = "method",
            ExtensionClass = "System.Linq.Enumerable",
            Assembly = "System.Linq",
            Signature = "Dictionary<TKey, TValue> ToDictionary(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> elementSelector)",
            Source = "runtime",
            SourceVersion = "10.0.0"
        },
        new()
        {
            MethodName = "ToDictionary",
            Kind = "method",
            ExtensionClass = "System.Linq.Enumerable",
            Assembly = "System.Linq",
            Signature = "Dictionary<TKey, TValue> ToDictionary(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> elementSelector, IEqualityComparer<TKey> comparer)",
            Source = "runtime",
            SourceVersion = "10.0.0"
        }
    ];

    [Fact]
    public void FormatResults_QuietMode_ShowsCountsTable()
    {
        var results = new List<ExtensionMethodResult>
        {
            new() { MethodName = "PostAsJson", Kind = "method", ExtensionClass = "JsonExtensions", Assembly = "System.Net.Http.Json" },
            new() { MethodName = "GetFromJson", Kind = "method", ExtensionClass = "JsonExtensions", Assembly = "System.Net.Http.Json" },
            new() { MethodName = "CopyToAsync", Kind = "method", ExtensionClass = "StreamExt", Assembly = "System.IO", ReachablePath = ".GetStreamAsync()", ReachableFromType = "Stream" },
        };

        var output = ExtensionsOutputFormatter.FormatResults("HttpClient", results, Verbosity.Quiet);

        // Should have a counts table, not the full extension tables
        Assert.Contains("| Type ", output);
        Assert.Contains("| Extensions ", output);
        Assert.Contains("| HttpClient | 2 |", output);
        Assert.Contains("| Stream | 1 | .GetStreamAsync() |", output);
        // Should NOT have the detailed columns
        Assert.DoesNotContain("| Name ", output);
        Assert.DoesNotContain("| Class ", output);
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
