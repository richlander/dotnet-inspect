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
/// JSON writer for find results. Serializes TypeFindResult with full fidelity.
/// Uses source-generated JSON context for AOT compatibility.
/// </summary>
public class FindJsonWriter : IResultWriter<TypeFindResult>
{
    private readonly bool _compact;

    public FindJsonWriter(bool compact = false)
    {
        _compact = compact;
    }

    public void Write(IReadOnlyList<TypeFindResult> results, WriterOptions options, TextWriter output)
    {
        var toSerialize = options.Limit.HasValue && results.Count > options.Limit.Value
            ? results.Take(options.Limit.Value).ToList()
            : results.ToList();

        var typeInfo = _compact
            ? TypeFindResultCompactJsonContext.Default.ListTypeFindResult
            : TypeFindResultJsonContext.Default.ListTypeFindResult;

        output.WriteLine(JsonSerializer.Serialize(toSerialize, typeInfo));
    }
}
