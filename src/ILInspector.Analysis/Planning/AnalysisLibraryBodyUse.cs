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
            version: 4,
            tier: 0,
            MethodDefinitionLayers.Body
                | MethodDefinitionLayers.ModuleLookup,
            parameters:
                $"instructions={limits.MaximumInstructionsPerBody};"
                + $"occurrences={limits.MaximumOccurrences};"
                + $"methodSignatureBytes="
                + limits.MaximumMethodSignatureBytes)
    {
        _limits = limits;
        _cancellationToken = cancellationToken;
    }

    internal override bool HasTypeScope => true;

    internal override bool TypeInScope(
        MetadataReader reader,
        TypeDefinition type) =>
        IsTypeInPopulation(reader, type);

    internal static bool IsTypeInPopulation(
        MetadataReader reader,
        TypeDefinition type) =>
        !type.GetDeclaringType().IsNil
        || !(reader.StringComparer.Equals(type.Namespace, "")
            && reader.StringComparer.Equals(type.Name, "<Module>"));

    internal override VisitFact Visit(
        scoped MethodDefinitionView view)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (!view.HasManagedBody)
            return new(null, view.Terminal);

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
                    _limits.MaximumOccurrences,
                    _limits.MaximumMethodSignatureBytes,
                    _cancellationToken);
            return new(fact, view.Terminal);
        }
        catch (Exception exception)
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(exception))
        {
            return new(
                BodyTypeUseMethodFact.Unavailable(
                    view.TypeHandle,
                    view.Token,
                    ProducerFailure.Describe(exception)),
                view.Terminal);
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
        accumulator.Complete(
            completion.Terminal == ProducerTerminal.Rows);

    internal override bool Settles(VisitFact fact) =>
        IsSettling(fact, _limits.MaximumOccurrences);

    internal sealed record VisitFact(
        BodyTypeUseMethodFact? Body,
        ProducerTerminal Terminal);

    internal sealed class Accumulator(int maximumOccurrences)
    {
        List<BodyTypeUseOccurrence>? _occurrences;
        List<BodyTypeUsePhysicalFact>? _bodies;
        readonly List<AnalysisLibraryBodyUseDiagnostic> _diagnostics = [];
        int _occurrenceCount;
        int _bodiesConsidered;
        int _bodiesExamined;
        int _bodiesPhysicalOnly;
        int _bodiesUnavailable;
        int _bodiesLimited;
        int _operandsConsidered;
        int _operandsExamined;
        int _operandsUnavailable;
        int _operandsLimited;
        bool _occurrenceLimitReported;

        internal void Add(VisitFact visit)
        {
            if (visit.Body is not { } body)
                return;

            bool retainRows = visit.Terminal == ProducerTerminal.Rows;
            if (retainRows)
            {
                _occurrences ??= [];
                _bodies ??= [];
            }
            _bodiesConsidered++;
            _operandsConsidered += body.OperandsConsidered;
            _operandsExamined += body.OperandsExamined;
            _operandsUnavailable += body.OperandsUnavailable;
            _diagnostics.AddRange(body.Diagnostics);
            if (retainRows)
            {
                _bodies!.Add(
                    new(
                        body.PhysicalType,
                        body.PhysicalMethodToken,
                        body.Fidelity));
            }

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
                (long)_occurrenceCount
                    + body.Occurrences.Length);
            if (attempted > maximumOccurrences)
            {
                _bodiesLimited++;
                if (!_occurrenceLimitReported)
                {
                    _diagnostics.Add(
                        new(
                            AnalysisLibraryBodyUseDiagnosticKind.Limit,
                            body.PhysicalMethodToken,
                            null,
                            "The Library body-use occurrence limit was exceeded.",
                            maximumOccurrences,
                            attempted));
                    _occurrenceLimitReported = true;
                }
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
                _occurrenceCount += body.Occurrences.Length;
                if (retainRows)
                    _occurrences!.AddRange(body.Occurrences);
            }
        }

        internal Result Complete(bool retainRows) =>
            new(
                _occurrenceCount,
                retainRows
                    ? [.. _occurrences ?? []]
                    : default,
                retainRows
                    ? [.. _bodies ?? []]
                    : default,
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

    internal static bool IsSettling(
        VisitFact visit,
        int maximumOccurrences) =>
        visit.Body is { } body
        && !body.Limited
        && body.Fidelity
            == AnalysisLibraryBodyUseFidelity.LogicalOwner
        && !body.Diagnostics.Any(
            static diagnostic =>
                diagnostic.Kind
                    == AnalysisLibraryBodyUseDiagnosticKind
                        .MalformedBody)
        && body.Occurrences.Length > 0
        && body.Occurrences.Length <= maximumOccurrences;

    internal sealed record Result(
        int OccurrenceCount,
        ImmutableArray<BodyTypeUseOccurrence> Occurrences,
        ImmutableArray<BodyTypeUsePhysicalFact> Bodies,
        ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics,
        AnalysisLibraryBodyUseCoverage Coverage);
}

internal readonly record struct BodyTypeUsePhysicalFact(
    TypeDefinitionHandle PhysicalType,
    int PhysicalMethodToken,
    AnalysisLibraryBodyUseFidelity Fidelity);
