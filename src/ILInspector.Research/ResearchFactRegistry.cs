using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Research;

public sealed record ResearchFactContext(MetadataSource Source, IrFunction Imported);

public interface IResearchFactProducer
{
    string Name { get; }
    IReadOnlyList<string> Produces { get; }
    IReadOnlyList<string> DependsOn { get; }
    IReadOnlyList<Annotation> Produce(ResearchFactContext context);
}

/// <summary>
/// Ordered fact-producer registry for the offset-keyed research overlay.
/// Producers contribute projection-neutral facts; presenters render the merged set.
/// </summary>
public sealed class ResearchFactRegistry
{
    readonly IReadOnlyList<IResearchFactProducer> _producers;

    public ResearchFactRegistry(params IResearchFactProducer[] producers)
        => _producers = Order(producers);

    public IReadOnlyList<string> ProducerNames => [.. _producers.Select(producer => producer.Name)];

    public static ResearchFactRegistry Default { get; } = new(
        new AllocationOccurrenceFactProducer(),
        new DecompilerHiddenFactProducer());

    public IReadOnlyList<Annotation> Collect(ResearchFactContext context)
        => [.. _producers.SelectMany(producer => producer.Produce(context)).OrderBy(fact => fact.SourceOffset).ThenBy(fact => fact.Descriptor.Id, StringComparer.Ordinal)];

    static IReadOnlyList<IResearchFactProducer> Order(IReadOnlyList<IResearchFactProducer> producers)
    {
        var byName = producers.ToDictionary(producer => producer.Name, StringComparer.Ordinal);
        var result = new List<IResearchFactProducer>(producers.Count);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var producer in producers)
            Visit(producer);
        return result;

        void Visit(IResearchFactProducer producer)
        {
            if (visited.Contains(producer.Name))
                return;
            if (!visiting.Add(producer.Name))
                throw new InvalidOperationException($"Research fact producer dependency cycle at {producer.Name}");
            foreach (var dependency in producer.DependsOn)
                if (byName.TryGetValue(dependency, out var dependencyProducer))
                    Visit(dependencyProducer);
            visiting.Remove(producer.Name);
            visited.Add(producer.Name);
            result.Add(producer);
        }
    }
}

sealed class DecompilerHiddenFactProducer : IResearchFactProducer
{
    public string Name => "decompiler-hidden-facts";
    public IReadOnlyList<string> Produces { get; } = ["unsafe.*", "lifetime.*"];
    public IReadOnlyList<string> DependsOn => [];

    public IReadOnlyList<Annotation> Produce(ResearchFactContext context)
        => Decompiler.Annotations.AnnotationEngine.Default.ClassifyImported(context.Imported);
}
