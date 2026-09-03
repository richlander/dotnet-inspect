using System.Collections.Immutable;

using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Analysis;
using ILInspector.Findings;

namespace ILInspector.Research;

public sealed class ResearchAssemblyContext
{
    readonly Lazy<IReadOnlyDictionary<int, MethodSignals>> _signals;
    readonly Lazy<IReadOnlyDictionary<int, MethodLeverage>> _leverageByToken;
    readonly Lazy<IReadOnlyDictionary<int, IReadOnlyList<DirectCall>>> _callsByCaller;
    readonly Lazy<IReadOnlyDictionary<int, IReadOnlyList<UnsafeEvidence>>> _unsafeEvidenceByToken;

    ResearchAssemblyContext(LibraryBodyIndex index)
    {
        Index = index;
        _signals = new(() => index.GetMethodSignals());
        _leverageByToken = new(() => index.TopLeverage(int.MaxValue)
            .ToDictionary(entry => entry.Method.MetadataToken, entry => entry));
        _callsByCaller = new(() => index
            .GetDirectCallsByEvidenceMethod()
            .ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<DirectCall>)pair.Value));
        _unsafeEvidenceByToken = new(() => index.UnsafeEvidence
            .GroupBy(evidence => evidence.Member.MetadataToken)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<UnsafeEvidence>)group.ToArray()));
    }

    public LibraryBodyIndex Index { get; }
    public IReadOnlyDictionary<int, MethodSignals> Signals => _signals.Value;
    public IReadOnlyDictionary<int, MethodLeverage> LeverageByToken => _leverageByToken.Value;
    public IReadOnlyDictionary<int, IReadOnlyList<DirectCall>> CallsByCaller => _callsByCaller.Value;
    public IReadOnlyDictionary<int, IReadOnlyList<UnsafeEvidence>> UnsafeEvidenceByToken => _unsafeEvidenceByToken.Value;

    public ImmutableArray<Finding<DirectCall>> InspectCallSites(int methodToken)
    {
        if (!CallsByCaller.TryGetValue(methodToken, out var calls) || calls.Count == 0)
            return [];
        var subject = ResearchMemberIdentity.SubjectFromMethod(calls[0].Caller);
        return AnalysisFindings.InspectCallSites(
            calls,
            new FindingSubject(subject.Id, subject.Display));
    }

    public static ResearchAssemblyContext Create(LibraryBodyIndex index) =>
        new(index);
}

/// <summary>
/// Memoizes <see cref="ResearchAssemblyContext.Create"/> per <see cref="LibraryBodyIndex"/> instance.
/// The context's assembly-wide projections are lazy, and sharing the context
/// ensures each projection is computed at most once for an index when multiple
/// producers or member queries request it.
/// </summary>
static class ResearchAssemblyContextCache
{
    const int MaxCachedContexts = 8;
    static readonly object s_lock = new();
    static readonly Dictionary<LibraryBodyIndex, ResearchAssemblyContext> s_contexts = new();

    public static ResearchAssemblyContext ForIndex(LibraryBodyIndex index)
    {
        lock (s_lock)
        {
            if (s_contexts.TryGetValue(index, out var context))
                return context;
            if (s_contexts.Count >= MaxCachedContexts)
                s_contexts.Clear();
            context = ResearchAssemblyContext.Create(index);
            s_contexts[index] = context;
            return context;
        }
    }
}

public sealed record ResearchFactContext(
    MetadataSource Source,
    IrFunction Imported,
    ResearchAssemblyContext? Assembly = null,
    IReadOnlyList<DirectCall>? CallSites = null);

/// <summary>How much Analysis state one fact producer requires.</summary>
public enum ResearchAnalysisScope
{
    None,
    Member,
    Assembly,
}

/// <summary>
/// Analysis acquisition required by a fact producer. The registry unions these
/// declarations before opening an index.
/// </summary>
public readonly record struct ResearchFactRequirements
{
    ResearchFactRequirements(
        ResearchAnalysisScope scope,
        LibraryBodyAnalysisFeatures features)
    {
        Scope = scope;
        Features = features;
    }

    public ResearchAnalysisScope Scope { get; }
    public LibraryBodyAnalysisFeatures Features { get; }

    public static ResearchFactRequirements None { get; } =
        new(ResearchAnalysisScope.None, LibraryBodyAnalysisFeatures.None);

    public static ResearchFactRequirements ForMember(
        LibraryBodyAnalysisFeatures features) =>
        Create(ResearchAnalysisScope.Member, features);

    public static ResearchFactRequirements ForAssembly(
        LibraryBodyAnalysisFeatures features) =>
        Create(ResearchAnalysisScope.Assembly, features);

    internal ResearchFactRequirements Union(
        ResearchFactRequirements other) =>
        Scope >= other.Scope
            ? new(Scope, Features | other.Features)
            : new(other.Scope, Features | other.Features);

    static ResearchFactRequirements Create(
        ResearchAnalysisScope scope,
        LibraryBodyAnalysisFeatures features)
    {
        if (features == LibraryBodyAnalysisFeatures.None)
            throw new ArgumentException("An Analysis-backed fact producer must request at least one feature.", nameof(features));
        if ((features & ~LibraryBodyAnalysisFeatures.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(features));
        return new(scope, features);
    }
}

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
    ResearchFactRequirements Requirements => ResearchFactRequirements.None;
    IReadOnlyList<Finding<IAnnotation>> Produce(ResearchFactContext context);
    IReadOnlyList<ResearchHeaderFact> ProduceHeaderFacts(ResearchFactContext context) => [];
}

/// <summary>
/// Ordered fact-producer registry for the offset-keyed research overlay.
/// Producers contribute projection-neutral facts; presenters render the merged set.
/// </summary>
public sealed class ResearchFactRegistry
{
    public const string CallRelationshipDescriptorId = "call.edge";

    readonly IReadOnlyList<IResearchFactProducer> _producers;

    public ResearchFactRegistry(params IResearchFactProducer[] producers)
    {
        _producers = Order(producers);
        Requirements = _producers.Aggregate(
            ResearchFactRequirements.None,
            static (requirements, producer) =>
                requirements.Union(producer.Requirements));
    }

    public IReadOnlyList<string> ProducerNames => [.. _producers.Select(producer => producer.Name)];
    public ResearchFactRequirements Requirements { get; }

    public static ResearchFactRegistry Default { get; } = new(
        new AllocationOccurrenceFactProducer(),
        new UnsafetyOccurrenceFactProducer(),
        new CallSiteCostFactProducer(),
        new CallSiteSemanticsFactProducer(),
        new MethodHeaderLeverageFactProducer(),
        new DecompilerLifetimeFactProducer());

    /// <summary>
    /// The body-local relationship dimension. Its call evidence is supplied by
    /// the graph session, so producing the document opens no second Analysis
    /// index. <c>Registry_UnionsProducerAnalysisRequirementsBeforeAcquisition</c>
    /// pins the profile's declaration, while
    /// <c>RequirementsNone_DoesNotResolveAnAssemblyContext</c> gates the
    /// declaration-to-acquisition wiring.
    /// </summary>
    public static ResearchFactRegistry CallRelationships { get; } = new(
        new DirectCallFactProducer());

    public FindingCensus<IAnnotation> CollectCensus(ResearchFactContext context)
    {
        Finding<IAnnotation>[] findings =
        [
            .. _producers.SelectMany(producer => producer.Produce(context)),
        ];
        if (findings.Any(finding => finding is null))
        {
            throw new InvalidOperationException(
                "Research fact producers must not return null Findings.");
        }

        return FindingCensus<IAnnotation>.Seal(
            findings
                .OrderBy(finding => finding.Payload.SourceOffset)
                .ThenBy(
                    finding => finding.Payload.Descriptor.Id,
                    StringComparer.Ordinal));
    }

    public IReadOnlyList<IAnnotation> Collect(ResearchFactContext context)
        => [.. CollectCensus(context).Findings.Select(finding => finding.Payload)];

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

    public IReadOnlyList<Finding<IAnnotation>> Produce(ResearchFactContext context)
    {
        var subject = ResearchFactFinding.Subject(context.Imported);
        return
        [
            .. new Decompiler.Annotations.LifetimeClassifier()
                .Classify(context.Imported)
                .Select((annotation, ordinal) =>
                    ResearchFactFinding.Create(
                        subject,
                        annotation,
                        new FindingKey(
                            $"{annotation.Descriptor.Id}|{annotation.SourceOffset:X8}"),
                        ordinal)),
        ];
    }
}
