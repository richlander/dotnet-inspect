using System.Collections.Immutable;
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
public sealed class AsyncMethodCountProducer : MethodDefinitionProducer<bool, int, int>
{
    AsyncMethodCountProducer()
        : base("Experiment.AsyncMethodCount", version: 1, tier: 0, MethodDefinitionLayers.Declaration)
    {
    }

    public static AsyncMethodCountProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view) =>
        AsyncMethodScope.IsCounted(view.Reader, view.TypeDefinition, view.MethodDefinition);

    internal override int Seed() => 0;

    internal override int Accumulate(int accumulator, bool fact) => fact ? accumulator + 1 : accumulator;

    internal override int Complete(int accumulator, MethodDefinitionCompletionView completion) => accumulator;
}

/// <summary>Exists terminal with the same signature as unsafe presence (bool, bool).</summary>
public sealed class AsyncMethodPresenceProducer : MethodDefinitionProducer<bool, bool, bool>
{
    AsyncMethodPresenceProducer()
        : base("Experiment.AsyncMethodPresence", version: 1, tier: 0, MethodDefinitionLayers.Declaration)
    {
    }

    public static AsyncMethodPresenceProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view) =>
        AsyncMethodScope.IsCounted(view.Reader, view.TypeDefinition, view.MethodDefinition);

    internal override bool Seed() => false;

    internal override bool Accumulate(bool accumulator, bool fact) => accumulator | fact;

    internal override bool Complete(bool accumulator, MethodDefinitionCompletionView completion) => accumulator;

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

/// <summary>The async question as an open query: a per-method predicate; the type filter is a type scope.</summary>
public struct AsyncMethodPredicate : IMethodDefinitionPredicate
{
    public bool Test(scoped MethodDefinitionView view) =>
        AsyncMethodScope.IsCountedMethod(view.Reader, view.MethodDefinition);
}

public sealed class AsyncPredicateProducer : MethodDefinitionPredicateProducer<AsyncMethodPredicate>
{
    readonly bool _allowsKernel;

    AsyncPredicateProducer(string identity, bool allowsKernel)
        : base(identity, 1, 0, MethodDefinitionLayers.Declaration) =>
        _allowsKernel = allowsKernel;

    public static AsyncPredicateProducer Kernel { get; } = new("Experiment.AsyncPredicate.Kernel", true);
    public static AsyncPredicateProducer Interpreted { get; } = new("Experiment.AsyncPredicate.Interpreted", false);

    internal override bool AllowsKernel => _allowsKernel;
    internal override bool HasTypeScope => true;
    internal override bool TypeInScope(MetadataReader reader, TypeDefinition type) => AsyncMethodScope.IsCountedType(reader, type);
}

public static class AsyncClosedQueries
{
    static readonly Dictionary<(AsyncPredicateProducer, ProducerTerminal), WorkDescription> s_plans = new()
    {
        [(AsyncPredicateProducer.Kernel, ProducerTerminal.All)] = Plan(AsyncPredicateProducer.Kernel, ProducerTerminal.All),
        [(AsyncPredicateProducer.Kernel, ProducerTerminal.Exists)] = Plan(AsyncPredicateProducer.Kernel, ProducerTerminal.Exists),
        [(AsyncPredicateProducer.Interpreted, ProducerTerminal.All)] = Plan(AsyncPredicateProducer.Interpreted, ProducerTerminal.All),
        [(AsyncPredicateProducer.Interpreted, ProducerTerminal.Exists)] = Plan(AsyncPredicateProducer.Interpreted, ProducerTerminal.Exists),
    };

    static WorkDescription Plan(AsyncPredicateProducer producer, ProducerTerminal terminal) =>
        ProducerPlanner.Plan([new ProducerRequest(producer, terminal)]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException("The experiment request must plan.");

    public static int Count(bool kernel, string path, PEReader peReader) => Run(kernel, ProducerTerminal.All, path, peReader);

    public static bool Exists(bool kernel, string path, PEReader peReader) => Run(kernel, ProducerTerminal.Exists, path, peReader) > 0;

    static int Run(bool kernel, ProducerTerminal terminal, string path, PEReader peReader)
    {
        AsyncPredicateProducer producer = kernel ? AsyncPredicateProducer.Kernel : AsyncPredicateProducer.Interpreted;
        ProducerResult<int> result = MethodDefinitionExecution.Execute(s_plans[(producer, terminal)], path, peReader).ResultOf(producer);
        return result.HasValue ? result.Value : throw new InvalidDataException(result.Failure?.Message);
    }
}

/// <summary>The async question as a Rows open query: selected methods project to their MethodDef token.</summary>
public struct AsyncMethodRowProjection : IMethodDefinitionProjection<int>
{
    public bool TryProject(scoped MethodDefinitionView view, out int row)
    {
        row = view.Token;
        return AsyncMethodScope.IsCountedMethod(view.Reader, view.MethodDefinition);
    }
}

public sealed class AsyncRowsProducer : MethodDefinitionRowsProducer<AsyncMethodRowProjection, int>
{
    readonly bool _allowsKernel;

    AsyncRowsProducer(string identity, bool allowsKernel)
        : base(identity, 1, 0, MethodDefinitionLayers.Declaration) =>
        _allowsKernel = allowsKernel;

    public static AsyncRowsProducer Kernel { get; } = new("Experiment.AsyncRows.Kernel", true);
    public static AsyncRowsProducer Interpreted { get; } = new("Experiment.AsyncRows.Interpreted", false);

    internal override bool AllowsKernel => _allowsKernel;
    internal override bool HasTypeScope => true;
    internal override bool TypeInScope(MetadataReader reader, TypeDefinition type) => AsyncMethodScope.IsCountedType(reader, type);
}

public static class AsyncClosedQueriesK2
{
    public const int Threshold = 5;

    static readonly WorkDescription s_rowsKernel = Plan(new ProducerRequest(AsyncRowsProducer.Kernel));
    static readonly WorkDescription s_rowsInterpreted = Plan(new ProducerRequest(AsyncRowsProducer.Interpreted));
    static readonly WorkDescription s_atLeastKernel = Plan(new ProducerRequest(AsyncPredicateProducer.Kernel, ProducerTerminal.Exists, Threshold));
    static readonly WorkDescription s_atLeastInterpreted = Plan(new ProducerRequest(AsyncPredicateProducer.Interpreted, ProducerTerminal.Exists, Threshold));

    static WorkDescription Plan(ProducerRequest request) =>
        ProducerPlanner.Plan([request]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException("The experiment request must plan.");

    public static ImmutableArray<int> Rows(bool kernel, string path, PEReader peReader)
    {
        AsyncRowsProducer producer = kernel ? AsyncRowsProducer.Kernel : AsyncRowsProducer.Interpreted;
        ProducerResult<ImmutableArray<int>> result = MethodDefinitionExecution
            .Execute(kernel ? s_rowsKernel : s_rowsInterpreted, path, peReader)
            .ResultOf(producer);
        return result.HasValue ? result.Value : throw new InvalidDataException(result.Failure?.Message);
    }

    public static bool AtLeast(bool kernel, string path, PEReader peReader)
    {
        AsyncPredicateProducer producer = kernel ? AsyncPredicateProducer.Kernel : AsyncPredicateProducer.Interpreted;
        ProducerResult<int> result = MethodDefinitionExecution
            .Execute(kernel ? s_atLeastKernel : s_atLeastInterpreted, path, peReader)
            .ResultOf(producer);
        return result.HasValue ? result.Value >= Threshold : throw new InvalidDataException(result.Failure?.Message);
    }

    /// <summary>The optimal Rows oracle: hoisted type filter, tokens in unit order.</summary>
    public static ImmutableArray<int> HandRows(PEReader peReader)
    {
        MetadataReader reader = peReader.GetMetadataReader();
        ImmutableArray<int>.Builder rows = ImmutableArray.CreateBuilder<int>();
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (!AsyncMethodScope.IsCountedType(reader, type))
                continue;
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                if (AsyncMethodScope.IsCountedMethod(reader, reader.GetMethodDefinition(methodHandle)))
                    rows.Add(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(methodHandle));
            }
        }

        return rows.DrainToImmutable();
    }

    /// <summary>The optimal Count-at-least oracle: hoisted, returns at the threshold.</summary>
    public static bool HandAtLeast(PEReader peReader)
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
                if (AsyncMethodScope.IsCountedMethod(reader, reader.GetMethodDefinition(methodHandle))
                    && ++count >= Threshold)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The naive legacy Rows path: classify every row, then filter the async ones.</summary>
    public static int LegacyRowCount(PEReader peReader) =>
        MethodClassificationScanner.Scan(peReader).Count(static method =>
            method.Classification is MethodClassification.RuntimeAsync
                or MethodClassification.StateMachineAsync);
}
