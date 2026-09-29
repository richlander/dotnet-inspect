using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

internal sealed class AnalysisLibraryBodyUseProducer
    : MethodDefinitionProducer<
        AnalysisLibraryBodyUseProducer.VisitFact,
        AnalysisLibraryBodyUseProducer.Accumulator,
        AnalysisLibraryBodyUseProducer.Result>
{
    readonly AnalysisLibraryBodyUseLimits _limits;
    readonly CancellationToken _cancellationToken;

    internal AnalysisLibraryBodyUseProducer(
        AnalysisLibraryBodyUseLimits limits,
        CancellationToken cancellationToken)
        : base(
            "AnalysisLibraryBodyUse",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body
                | MethodDefinitionLayers.ModuleLookup,
            parameters:
                $"instructions={limits.MaximumInstructionsPerBody};"
                + $"occurrences={limits.MaximumOccurrences}")
    {
        _limits = limits;
        _cancellationToken = cancellationToken;
    }

    internal override VisitFact Visit(
        scoped MethodDefinitionView view)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (!view.HasManagedBody)
            return new(null);

        try
        {
            BodyTypeUseMethodFact fact =
                view.Lookup.AnalyzeBodyTypeUses(
                    view.TypeHandle,
                    view.TypeDefinition,
                    view.MethodHandle,
                    view.MethodDefinition,
                    view.GetBody(),
                    _limits.MaximumInstructionsPerBody,
                    _limits.MaximumOccurrences);
            return new(fact);
        }
        catch (Exception exception)
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(exception))
        {
            return new(
                BodyTypeUseMethodFact.Unavailable(
                    view.TypeHandle,
                    view.Token,
                    ProducerFailure.Describe(exception)));
        }
    }

    internal override Accumulator Seed() =>
        new(_limits.MaximumOccurrences);

    internal override Accumulator Accumulate(
        Accumulator accumulator,
        VisitFact fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override Result Complete(
        Accumulator accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator.Complete();

    internal sealed record VisitFact(
        BodyTypeUseMethodFact? Body);

    internal sealed class Accumulator(int maximumOccurrences)
    {
        readonly List<BodyTypeUseOccurrence> _occurrences = [];
        readonly List<BodyTypeUseMethodFact> _bodies = [];
        readonly List<AnalysisLibraryBodyUseDiagnostic> _diagnostics = [];
        int _bodiesConsidered;
        int _bodiesExamined;
        int _bodiesPhysicalOnly;
        int _bodiesUnavailable;
        int _bodiesLimited;
        int _operandsConsidered;
        int _operandsExamined;
        int _operandsUnavailable;
        int _operandsLimited;

        internal void Add(VisitFact visit)
        {
            if (visit.Body is not { } body)
                return;

            _bodiesConsidered++;
            _operandsConsidered += body.OperandsConsidered;
            _operandsExamined += body.OperandsExamined;
            _operandsUnavailable += body.OperandsUnavailable;
            _diagnostics.AddRange(body.Diagnostics);
            _bodies.Add(body);

            if (body.Limited)
            {
                _bodiesLimited++;
                _operandsLimited += Math.Max(
                    0,
                    body.OperandsConsidered
                        - body.OperandsExamined
                        - body.OperandsUnavailable);
                return;
            }

            bool unavailable = body.Diagnostics.Any(
                static diagnostic =>
                    diagnostic.Kind
                        == AnalysisLibraryBodyUseDiagnosticKind
                            .MalformedBody);
            if (unavailable)
            {
                _bodiesUnavailable++;
                return;
            }

            long attempted = checked(
                (long)_occurrences.Count
                    + body.Occurrences.Length);
            if (attempted > maximumOccurrences)
            {
                _bodiesLimited++;
                _diagnostics.Add(
                    new(
                        AnalysisLibraryBodyUseDiagnosticKind.Limit,
                        body.PhysicalMethodToken,
                        null,
                        "The Library body-use occurrence limit was exceeded.",
                        maximumOccurrences,
                        attempted));
                return;
            }

            if (body.Fidelity
                == AnalysisLibraryBodyUseFidelity.PhysicalOnly)
            {
                _bodiesPhysicalOnly++;
            }
            else
            {
                _bodiesExamined++;
                _occurrences.AddRange(body.Occurrences);
            }
        }

        internal Result Complete() =>
            new(
                [.. _occurrences],
                [.. _bodies],
                [.. _diagnostics],
                new(
                    _bodiesConsidered,
                    _bodiesExamined,
                    _bodiesPhysicalOnly,
                    _bodiesUnavailable,
                    _bodiesLimited,
                    _operandsConsidered,
                    _operandsExamined,
                    _operandsUnavailable,
                    _operandsLimited));
    }

    internal sealed record Result(
        ImmutableArray<BodyTypeUseOccurrence> Occurrences,
        ImmutableArray<BodyTypeUseMethodFact> Bodies,
        ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics,
        AnalysisLibraryBodyUseCoverage Coverage);
}
