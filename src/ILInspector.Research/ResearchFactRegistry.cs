using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Analysis;

namespace ILInspector.Research;

public sealed record ResearchAssemblyContext(
    LibraryBodyIndex Index,
    IReadOnlyDictionary<int, MethodSignals> Signals,
    IReadOnlyDictionary<int, MethodLeverage> LeverageByToken,
    IReadOnlyDictionary<int, IReadOnlyList<DirectCall>> CallsByCaller)
{
    public static ResearchAssemblyContext Create(LibraryBodyIndex index)
    {
        var leverage = index.TopLeverage(int.MaxValue)
            .ToDictionary(entry => entry.Method.MetadataToken, entry => entry);
        var callsByCaller = index.DirectCalls
            .GroupBy(call => call.Caller.MetadataToken)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<DirectCall>)group.ToArray());
        return new ResearchAssemblyContext(index, index.GetMethodSignals(), leverage, callsByCaller);
    }
}

public sealed record ResearchFactContext(
    MetadataSource Source,
    IrFunction Imported,
    ResearchAssemblyContext? Assembly = null);

public sealed record ResearchHeaderFact(
    AnnotationDescriptor Descriptor,
    string? Detail = null)
{
    public string Format()
        => string.IsNullOrEmpty(Detail)
            ? Descriptor.Id
            : $"{Descriptor.Id}({Detail})";
}

public interface IResearchFactProducer
{
    string Name { get; }
    IReadOnlyList<string> Produces { get; }
    IReadOnlyList<string> DependsOn { get; }
    IReadOnlyList<Annotation> Produce(ResearchFactContext context);
    IReadOnlyList<ResearchHeaderFact> ProduceHeaderFacts(ResearchFactContext context) => [];
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
        new UnsafetyOccurrenceFactProducer(),
        new CallSiteCostFactProducer(),
        new CallSiteSemanticsFactProducer(),
        new MethodHeaderLeverageFactProducer(),
        new DecompilerLifetimeFactProducer());

    public IReadOnlyList<Annotation> Collect(ResearchFactContext context)
        => [.. _producers.SelectMany(producer => producer.Produce(context)).OrderBy(fact => fact.SourceOffset).ThenBy(fact => fact.Descriptor.Id, StringComparer.Ordinal)];

    public IReadOnlyList<ResearchHeaderFact> CollectHeaderFacts(ResearchFactContext context)
        => [.. _producers.SelectMany(producer => producer.ProduceHeaderFacts(context)).OrderBy(fact => fact.Descriptor.Id, StringComparer.Ordinal)];

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

sealed class DecompilerLifetimeFactProducer : IResearchFactProducer
{
    public string Name => "decompiler-lifetime-facts";
    public IReadOnlyList<string> Produces { get; } = ["lifetime.*"];
    public IReadOnlyList<string> DependsOn => [];

    public IReadOnlyList<Annotation> Produce(ResearchFactContext context)
        => new Decompiler.Annotations.LifetimeClassifier().Classify(context.Imported);
}
