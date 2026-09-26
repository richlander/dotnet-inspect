using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning.Experiments;

// Experiment only (exp/producer-planning-async-count): a cheap, declaration-only
// workload for measuring planning overhead against hand-rolled and NLinq-style
// oracles. Scope matches MethodClassificationScanner.Scan's async rows: public,
// non-accessor, non-P/Invoke methods on types whose name does not start with '<'.
public static class AsyncMethodScope
{
    public static bool IsCountedType(MetadataReader reader, TypeDefinition type) =>
        !reader.StringComparer.StartsWith(type.Name, "<");

    public static bool IsCountedMethod(MetadataReader reader, MethodDefinition method)
    {
        if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
            || (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
        {
            return false;
        }

        StringHandle name = method.Name;
        if (reader.StringComparer.StartsWith(name, "get_")
            || reader.StringComparer.StartsWith(name, "set_")
            || reader.StringComparer.StartsWith(name, "add_")
            || reader.StringComparer.StartsWith(name, "remove_"))
        {
            return false;
        }

        return MethodClassificationScanner.ClassifyAsyncMethod(reader, method) is not null;
    }

    public static bool IsCounted(MetadataReader reader, TypeDefinition type, MethodDefinition method) =>
        IsCountedType(reader, type) && IsCountedMethod(reader, method);
}

/// <summary>Count terminal: how many async methods.</summary>
public sealed class AsyncMethodCountProducer : MethodDefinitionProducer<bool, int>
{
    AsyncMethodCountProducer()
        : base("Experiment.AsyncMethodCount", version: 1, tier: 0, MethodDefinitionLayers.Declaration)
    {
    }

    public static AsyncMethodCountProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view) =>
        AsyncMethodScope.IsCounted(view.Reader, view.TypeDefinition, view.MethodDefinition);

    internal override int Complete(IReadOnlyList<bool> facts, MethodDefinitionCompletionView completion)
    {
        int count = 0;
        for (int i = 0; i < facts.Count; i++)
        {
            if (facts[i])
                count++;
        }

        return count;
    }
}

/// <summary>Exists terminal with the same signature as unsafe presence (bool, bool).</summary>
public sealed class AsyncMethodPresenceProducer : MethodDefinitionProducer<bool, bool>
{
    AsyncMethodPresenceProducer()
        : base("Experiment.AsyncMethodPresence", version: 1, tier: 0, MethodDefinitionLayers.Declaration)
    {
    }

    public static AsyncMethodPresenceProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view) =>
        AsyncMethodScope.IsCounted(view.Reader, view.TypeDefinition, view.MethodDefinition);

    internal override bool Complete(IReadOnlyList<bool> facts, MethodDefinitionCompletionView completion)
    {
        for (int i = 0; i < facts.Count; i++)
        {
            if (facts[i])
                return true;
        }

        return false;
    }

    internal override bool Settles(bool fact) => fact;
}

public static class AsyncMethodCount
{
    static readonly WorkDescription s_count = Plan(new(AsyncMethodCountProducer.Instance));
    static readonly WorkDescription s_presence = Plan(
        new(AsyncMethodPresenceProducer.Instance, ProducerTerminal.Exists));

    static WorkDescription Plan(ProducerRequest request) =>
        ProducerPlanner.Plan([request]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException("The experiment request must plan.");

    public static int Planned(string path, PEReader peReader)
    {
        ProducerResult<int> result = MethodDefinitionExecution.Execute(s_count, path, peReader)
            .ResultOf(AsyncMethodCountProducer.Instance);
        return result.HasValue ? result.Value : throw new InvalidDataException(result.Failure?.Message);
    }

    public static bool PlannedExists(string path, PEReader peReader)
    {
        ProducerResult<bool> result = MethodDefinitionExecution.Execute(s_presence, path, peReader)
            .ResultOf(AsyncMethodPresenceProducer.Instance);
        return result.HasValue ? result.Value : throw new InvalidDataException(result.Failure?.Message);
    }

    /// <summary>The optimal oracle: nested loops, the type filter hoisted out of the method loop.</summary>
    public static int HandRolled(PEReader peReader)
    {
        MetadataReader reader = peReader.GetMetadataReader();
        int count = 0;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (!AsyncMethodScope.IsCountedType(reader, type))
                continue;
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                if (AsyncMethodScope.IsCountedMethod(reader, reader.GetMethodDefinition(methodHandle)))
                    count++;
            }
        }

        return count;
    }

    /// <summary>The naive legacy oracle: classify rows for every method, then count with LINQ, as the CLI does.</summary>
    public static int LegacyRows(PEReader peReader) =>
        MethodClassificationScanner.Scan(peReader).Count(static method =>
            method.Classification is MethodClassification.RuntimeAsync
                or MethodClassification.StateMachineAsync);
}
