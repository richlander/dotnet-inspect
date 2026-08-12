using System.Text.Json;
using DotnetInspector.Models;

namespace DotnetInspector.Output;

/// <summary>
/// Options for result writers.
/// </summary>
public record WriterOptions
{
    public int? Limit { get; init; }
}

/// <summary>
/// Interface for writers that output typed results.
/// </summary>
public interface IResultWriter<T>
{
    void Write(IReadOnlyList<T> results, WriterOptions options, TextWriter output);
}

/// <summary>
/// JSON document writer for find results.
/// Uses source-generated JSON context for AOT compatibility.
/// </summary>
public class FindJsonWriter : IResultWriter<TypeFindResult>
{
    public void Write(IReadOnlyList<TypeFindResult> results, WriterOptions options, TextWriter output)
        => output.WriteLine(
            JsonSerializer.Serialize(
                results.ToArray(),
                TypeFindResultJsonContext.Default.TypeFindResultArray));
}

/// <summary>
/// JSON document writer for member-search results.
/// Uses source-generated JSON context for AOT compatibility.
/// </summary>
public class MemberFindJsonWriter : IResultWriter<MemberFindResult>
{
    public void Write(IReadOnlyList<MemberFindResult> results, WriterOptions options, TextWriter output)
        => output.WriteLine(
            JsonSerializer.Serialize(
                results.ToArray(),
                MemberFindResultJsonContext.Default.MemberFindResultArray));
}
