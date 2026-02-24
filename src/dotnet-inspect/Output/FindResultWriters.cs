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
/// JSONL writer for find results. One compact JSON object per line (streaming-friendly).
/// Uses source-generated JSON context for AOT compatibility.
/// </summary>
public class FindJsonWriter : IResultWriter<TypeFindResult>
{
    public void Write(IReadOnlyList<TypeFindResult> results, WriterOptions options, TextWriter output)
    {
        foreach (var result in results)
        {
            output.WriteLine(JsonSerializer.Serialize(result, TypeFindResultJsonlContext.Default.TypeFindResult));
        }
    }
}
