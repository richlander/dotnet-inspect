

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Tests for generic type name conversion from C# syntax to metadata format.
/// </summary>
public class GenericTypeNameTests
{
    [Theory]
    [InlineData("List<T>", "List`1")]
    [InlineData("Dictionary<TKey, TValue>", "Dictionary`2")]
    [InlineData("Action<T1, T2, T3>", "Action`3")]
    [InlineData("Func<T, TResult>", "Func`2")]
    [InlineData("Tuple<T1, T2, T3, T4>", "Tuple`4")]
    public void ConvertGenericTypeName_CSharpSyntax_ConvertsToBacktick(string input, string expected)
    {
        var result = GenericTypeNameConverter.Convert(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("List`1")]
    [InlineData("Dictionary`2")]
    [InlineData("String")]
    [InlineData("JsonSerializer")]
    public void ConvertGenericTypeName_AlreadyBacktickOrNonGeneric_ReturnsUnchanged(string input)
    {
        var result = GenericTypeNameConverter.Convert(input);

        Assert.Equal(input, result);
    }

    [Theory]
    [InlineData("Dictionary<string, List<int>>", "Dictionary`2")]
    [InlineData("Func<List<T>, Dictionary<K, V>>", "Func`2")]
    [InlineData("Action<Tuple<int, int>, string>", "Action`2")]
    public void ConvertGenericTypeName_NestedGenerics_CountsTopLevelOnly(string input, string expected)
    {
        var result = GenericTypeNameConverter.Convert(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("IEnumerable<T>", "IEnumerable`1")]
    [InlineData("ICollection<T>", "ICollection`1")]
    [InlineData("IList<T>", "IList`1")]
    [InlineData("IReadOnlyList<T>", "IReadOnlyList`1")]
    [InlineData("IReadOnlyDictionary<TKey, TValue>", "IReadOnlyDictionary`2")]
    public void ConvertGenericTypeName_CommonInterfaces_ConvertsCorrectly(string input, string expected)
    {
        var result = GenericTypeNameConverter.Convert(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Option<T>", "Option`1")]
    [InlineData("Result<T, TError>", "Result`2")]
    [InlineData("ValueTask<T>", "ValueTask`1")]
    [InlineData("Task<TResult>", "Task`1")]
    [InlineData("Span<T>", "Span`1")]
    [InlineData("Memory<T>", "Memory`1")]
    public void ConvertGenericTypeName_RealWorldTypes_ConvertsCorrectly(string input, string expected)
    {
        var result = GenericTypeNameConverter.Convert(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("List<>", "List`0")]  // Empty type params
    [InlineData("Bad<", "Bad<")]      // Malformed - no closing bracket
    [InlineData("Bad>", "Bad>")]      // Malformed - no opening bracket
    [InlineData(">Bad<", ">Bad<")]    // Malformed - wrong order
    public void ConvertGenericTypeName_EdgeCases_HandlesGracefully(string input, string expected)
    {
        var result = GenericTypeNameConverter.Convert(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConvertGenericTypeName_EmptyString_ReturnsEmpty()
    {
        var result = GenericTypeNameConverter.Convert("");

        Assert.Equal("", result);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<T>", "System.Collections.Generic.List`1")]
    [InlineData("System.Collections.Generic.Dictionary<TKey, TValue>", "System.Collections.Generic.Dictionary`2")]
    public void ConvertGenericTypeName_FullyQualifiedNames_ConvertsCorrectly(string input, string expected)
    {
        var result = GenericTypeNameConverter.Convert(input);

        Assert.Equal(expected, result);
    }
}
