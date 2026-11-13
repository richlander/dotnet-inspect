using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The three reader-facing populations behind the corpus sensor's headline
/// measures (#10191). Each measure is the fully raised share of one
/// population, so a method that is fully raised in the aggregate counts the
/// same way here; only the denominator changes. The method-count aggregate
/// is dominated by constructors and accessors, which hide whether the bodies a
/// reader opens render well. The populations are decided from metadata and
/// the imported (pre-pass) tree, never from the raised output, so a raise
/// cannot move a method between populations.
/// </summary>
internal static class HeadlinePopulations
{
    /// <summary>The imported CFG holds at least one conditional or switch branch.</summary>
    internal const string Branching = "branching";

    /// <summary>
    /// The method definition carries a classic async, iterator, or async-iterator
    /// state-machine attribute: the kickoff whose body a reader opens.
    /// </summary>
    internal const string StateMachine = "state-machine";

    /// <summary>
    /// The method is public or protected and every declaring type in its nesting
    /// chain is public or protected: a body reachable from the library's API.
    /// </summary>
    internal const string PublicApi = "public-api";

    internal static ImmutableArray<string>? Classify(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        IrFunction imported)
    {
        var method = reader.GetMethodDefinition(methodHandle);
        var populations = ImmutableArray.CreateBuilder<string>(3);
        if (HasConditionalBranch(imported))
            populations.Add(Branching);
        if (IsStateMachineKickoff(reader, method))
            populations.Add(StateMachine);
        if (IsPublicApi(reader, typeHandle, method))
            populations.Add(PublicApi);
        return populations.Count == 0 ? null : populations.ToImmutable();
    }

    internal static bool HasConditionalBranch(IrFunction imported)
    {
        foreach (var node in imported.Descendants)
        {
            if (node is ConditionalBranch or SwitchBranch)
                return true;
        }

        return false;
    }

    internal static bool IsStateMachineKickoff(MetadataReader reader, MethodDefinition method)
    {
        var attributes = method.GetCustomAttributes();
        return AttributeReader.HasAttribute(reader, attributes, KnownAttributeNames.AsyncStateMachineAttribute)
            || AttributeReader.HasAttribute(reader, attributes, KnownAttributeNames.IteratorStateMachineAttribute)
            || AttributeReader.HasAttribute(reader, attributes, KnownAttributeNames.AsyncIteratorStateMachineAttribute);
    }

    internal static bool IsPublicApi(MetadataReader reader, TypeDefinitionHandle typeHandle, MethodDefinition method)
    {
        if (!MetadataDeclarationQuery.IsPublicOrProtected(method))
            return false;

        var current = typeHandle;
        while (!current.IsNil)
        {
            var type = reader.GetTypeDefinition(current);
            bool visible = (type.Attributes & TypeAttributes.VisibilityMask) is
                TypeAttributes.Public
                or TypeAttributes.NestedPublic
                or TypeAttributes.NestedFamily
                or TypeAttributes.NestedFamORAssem;
            if (!visible)
                return false;
            current = type.GetDeclaringType();
        }

        return true;
    }

    /// <summary>
    /// Aggregates the three measures over the given rows. Returns null when no
    /// row carries population evidence, which is how a snapshot written before
    /// schema v8 reads.
    /// </summary>
    internal static HeadlinePopulationMetrics? Build(IEnumerable<CorpusMethodSnapshot> methods)
    {
        int branching = 0, branchingRaised = 0;
        int stateMachine = 0, stateMachineRaised = 0;
        int publicApi = 0, publicApiRaised = 0;
        bool anyEvidence = false;
        foreach (var method in methods)
        {
            if (method.Populations is not { Count: > 0 } populations)
                continue;

            anyEvidence = true;
            foreach (string population in populations)
            {
                switch (population)
                {
                    case Branching:
                        branching++;
                        if (method.FullyRaised)
                            branchingRaised++;
                        break;
                    case StateMachine:
                        stateMachine++;
                        if (method.FullyRaised)
                            stateMachineRaised++;
                        break;
                    case PublicApi:
                        publicApi++;
                        if (method.FullyRaised)
                            publicApiRaised++;
                        break;
                }
            }
        }

        return anyEvidence
            ? new HeadlinePopulationMetrics(
                PopulationRaisedMetrics.Of(branching, branchingRaised),
                PopulationRaisedMetrics.Of(stateMachine, stateMachineRaised),
                PopulationRaisedMetrics.Of(publicApi, publicApiRaised))
            : null;
    }
}

/// <summary>Fully raised count and share over one population.</summary>
internal sealed record PopulationRaisedMetrics(
    int Population,
    int FullyRaised,
    int FullyRaisedBasisPoints)
{
    public static PopulationRaisedMetrics Of(int population, int fullyRaised)
        => new(
            population,
            fullyRaised,
            population <= 0 ? 0 : (int)Math.Round(fullyRaised * 10_000.0 / population, MidpointRounding.AwayFromZero));
}

/// <summary>
/// The headline measures: fully raised over branching methods, over classic
/// state-machine kickoffs, and over public API bodies. See
/// <see cref="HeadlinePopulations"/>.
/// </summary>
internal sealed record HeadlinePopulationMetrics(
    PopulationRaisedMetrics Branching,
    PopulationRaisedMetrics StateMachine,
    PopulationRaisedMetrics PublicApi);
