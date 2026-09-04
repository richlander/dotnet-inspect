using System.Collections.ObjectModel;
using System.CommandLine;

namespace DotnetInspector.CommandLine;

internal sealed class CliRowSelectionRouteCandidate
{
    private readonly ReadOnlyCollection<string> _commandPrefix;

    public CliRowSelectionRouteCandidate(
        Command parserRoot,
        Command expectedCommand,
        IReadOnlyList<string> commandPrefix,
        CliRowSelectionOptionBindings bindings,
        CliRowSelectionCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(parserRoot);
        ArgumentNullException.ThrowIfNull(expectedCommand);
        ArgumentNullException.ThrowIfNull(commandPrefix);
        ArgumentNullException.ThrowIfNull(bindings);

        ParserRoot = parserRoot;
        ExpectedCommand = expectedCommand;
        _commandPrefix =
            Array.AsReadOnly(commandPrefix.ToArray());
        Bindings = bindings;
        Capabilities = capabilities;
    }

    public Command ParserRoot { get; }

    public Command ExpectedCommand { get; }

    public IReadOnlyList<string> CommandPrefix =>
        _commandPrefix;

    public CliRowSelectionOptionBindings Bindings { get; }

    public CliRowSelectionCapabilities Capabilities { get; }
}

internal enum CliRowSelectionRouteEnvelopeOutcome
{
    NoRequest,
    Deferred,
    ArgumentFailure,
    LoweringFailure,
    ExplicitCommandRequired,
    UnsupportedCapability,
    Success
}

internal sealed class CliRowSelectionRouteEnvelopeResult
{
    private readonly ReadOnlyCollection<int> _deferredPositions;

    private CliRowSelectionRouteEnvelopeResult(
        CliRowSelectionRouteEnvelopeOutcome outcome,
        IReadOnlyList<int>? deferredPositions = null,
        CliRowSelectionArgumentFailure? argumentFailure = null,
        CliRowSelectionFailure? failure = null,
        CliRowSelectionLoweringResult<string>? loweringResult = null,
        CliRowSelectionOccurrenceKind? requestKind = null,
        int? position = null)
    {
        Outcome = outcome;
        _deferredPositions =
            Array.AsReadOnly(
                deferredPositions?.ToArray()
                ?? []);
        ArgumentFailure = argumentFailure;
        Failure = failure;
        LoweringResult = loweringResult;
        RequestKind = requestKind;
        Position = position;
    }

    public CliRowSelectionRouteEnvelopeOutcome Outcome { get; }

    public IReadOnlyList<int> DeferredPositions =>
        _deferredPositions;

    public CliRowSelectionArgumentFailure? ArgumentFailure { get; }

    public CliRowSelectionFailure? Failure { get; }

    public CliRowSelectionLoweringResult<string>? LoweringResult { get; }

    public CliRowSelectionOccurrenceKind? RequestKind { get; }

    public int? Position { get; }

    public static CliRowSelectionRouteEnvelopeResult NoRequest() =>
        new(CliRowSelectionRouteEnvelopeOutcome.NoRequest);

    public static CliRowSelectionRouteEnvelopeResult Deferred(
        IReadOnlyList<int> positions) =>
        new(
            CliRowSelectionRouteEnvelopeOutcome.Deferred,
            deferredPositions: positions);

    public static CliRowSelectionRouteEnvelopeResult Failed(
        CliRowSelectionArgumentFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(
            CliRowSelectionRouteEnvelopeOutcome.ArgumentFailure,
            argumentFailure: failure,
            requestKind: failure.OccurrenceKind,
            position: failure.Position);
    }

    public static CliRowSelectionRouteEnvelopeResult Failed(
        CliRowSelectionLoweringResult<string> loweringResult)
    {
        ArgumentNullException.ThrowIfNull(loweringResult);
        ArgumentNullException.ThrowIfNull(loweringResult.Failure);
        return new(
            CliRowSelectionRouteEnvelopeOutcome.LoweringFailure,
            failure: loweringResult.Failure,
            loweringResult: loweringResult,
            requestKind: loweringResult.Failure.OccurrenceKind,
            position: loweringResult.Failure.Position);
    }

    public static CliRowSelectionRouteEnvelopeResult
        ExplicitCommandRequired(
            CliRowSelectionOccurrenceKind? kind,
            int position) =>
        new(
            CliRowSelectionRouteEnvelopeOutcome
                .ExplicitCommandRequired,
            requestKind: kind,
            position: position);

    public static CliRowSelectionRouteEnvelopeResult
        Unsupported(
            CliRowSelectionOccurrenceKind kind,
            int position,
            CliRowSelectionCapabilities missingCapabilities)
    {
        var failure =
            new CliRowSelectionFailure(
                CliRowSelectionFailureReason.UnsupportedCapability,
                kind,
                position,
                missingCapabilities);
        return new(
            CliRowSelectionRouteEnvelopeOutcome
                .UnsupportedCapability,
            failure: failure,
            requestKind: kind,
            position: position);
    }

    public static CliRowSelectionRouteEnvelopeResult Success(
        CliRowSelectionLoweringResult<string> loweringResult)
    {
        ArgumentNullException.ThrowIfNull(loweringResult);
        if (!loweringResult.IsSuccess)
        {
            throw new ArgumentException(
                "A successful route envelope requires successful lowering.",
                nameof(loweringResult));
        }

        return new(
            CliRowSelectionRouteEnvelopeOutcome.Success,
            loweringResult: loweringResult);
    }
}

internal static class CliRowSelectionRouteEnvelope
{
    public static CliRowSelectionRouteEnvelopeResult Evaluate(
        string[] arguments,
        IReadOnlyList<CliRowSelectionRouteCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException(
                "At least one route candidate is required.",
                nameof(candidates));
        }

        CandidateObservation[] observations =
            candidates
                .Select(candidate =>
                    Observe(
                        arguments,
                        candidate))
                .ToArray();
        bool hasUnexpectedCommand =
            observations.Any(
                observation =>
                    !observation.ExpectedCommandSelected);

        int[] unexpectedCommandPositions =
            observations
                .Where(
                    observation =>
                        !observation.ExpectedCommandSelected)
                .Select(
                    observation =>
                        observation.UnexpectedCommandPosition)
                .Where(position => position is not null)
                .Select(position => position!.Value)
                .Distinct()
                .Order()
                .ToArray();
        // Common grammar failures remain reportable even when an unrelated
        // token must later be deferred to routing.
        CliRowSelectionArgumentFailure? commonArgumentFailure =
            CommonArgumentFailure(observations);
        if (commonArgumentFailure is not null)
        {
            return CliRowSelectionRouteEnvelopeResult.Failed(
                commonArgumentFailure);
        }

        CliRowSelectionLoweringResult<string>? commonLoweringFailure =
            CommonLoweringFailure(observations);
        if (commonLoweringFailure is not null)
        {
            return CliRowSelectionRouteEnvelopeResult.Failed(
                commonLoweringFailure);
        }

        RequestScan scan =
            ScanRequests(
                arguments,
                observations);

        // Declaration and capability failures are compared in argv order so
        // naming a command cannot be suggested for a uniformly unsupported
        // earlier request.
        foreach (RequestToken request in scan.Requests)
        {
            if (request.MeaningsDiffer)
            {
                if (!hasUnexpectedCommand)
                {
                    return CliRowSelectionRouteEnvelopeResult
                        .ExplicitCommandRequired(
                            null,
                            request.Position);
                }

                continue;
            }

            if (request.Declared.All(
                    declared => !declared))
            {
                return CliRowSelectionRouteEnvelopeResult.Unsupported(
                    request.Kind,
                    request.Position,
                    CliRowSelectionLowerer.RequiredCapabilities(
                        request.Kind,
                        scan.LineSelection));
            }

            if (request.Declared.Any(
                    declared => !declared))
            {
                if (!hasUnexpectedCommand)
                {
                    return CliRowSelectionRouteEnvelopeResult
                        .ExplicitCommandRequired(
                            request.Kind,
                            request.Position);
                }

                continue;
            }

            if (scan.LineSelectionDeferred
                && request.Kind is
                    CliRowSelectionOccurrenceKind.Limit
                    or CliRowSelectionOccurrenceKind.Head
                    or CliRowSelectionOccurrenceKind.Tail)
            {
                continue;
            }

            CliRowSelectionCapabilities[] required =
                observations
                    .Select(
                        observation =>
                            CliRowSelectionLowerer
                                .RequiredCapabilities(
                                    request.Kind,
                                    observation.LineSelection))
                    .ToArray();
            bool[] supported =
                observations
                    .Select(
                        (observation, index) =>
                            (observation.Candidate.Capabilities
                                & required[index])
                            == required[index])
                    .ToArray();
            CliRowSelectionCapabilities commonRequired =
                required[0];
            bool commonMeaning =
                required.All(
                    candidateRequired =>
                        candidateRequired == commonRequired);
            if (!commonMeaning)
            {
                if (!hasUnexpectedCommand)
                {
                    return CliRowSelectionRouteEnvelopeResult
                        .ExplicitCommandRequired(
                            request.Kind,
                            request.Position);
                }

                continue;
            }

            if (supported.All(value => value))
            {
                continue;
            }

            if (supported.Any(value => value))
            {
                if (!hasUnexpectedCommand)
                {
                    return CliRowSelectionRouteEnvelopeResult
                        .ExplicitCommandRequired(
                            request.Kind,
                            request.Position);
                }

                continue;
            }

            return CliRowSelectionRouteEnvelopeResult.Unsupported(
                request.Kind,
                request.Position,
                commonRequired);
        }

        if (hasUnexpectedCommand
            || scan.DeferredPositions.Count > 0)
        {
            return CliRowSelectionRouteEnvelopeResult.Deferred(
                unexpectedCommandPositions
                    .Concat(scan.DeferredPositions)
                    .Distinct()
                    .Order()
                    .ToArray());
        }

        if (scan.Requests.Count == 0)
        {
            return CliRowSelectionRouteEnvelopeResult.NoRequest();
        }

        IReadOnlyList<CliRowSelectionOccurrence<string>>?
            commonOccurrences =
                CommonOccurrences(observations);
        if (commonOccurrences is null)
        {
            return CliRowSelectionRouteEnvelopeResult.Deferred(
                scan.Requests
                    .Select(request => request.Position)
                    .Distinct()
                    .Order()
                    .ToArray());
        }

        if (commonOccurrences.Count == 0)
        {
            return CliRowSelectionRouteEnvelopeResult.Deferred(
                scan.Requests
                    .Select(request => request.Position)
                    .Distinct()
                    .Order()
                    .ToArray());
        }

        CliRowSelectionLoweringResult<string> lowering =
            CliRowSelectionLowerer.Lower(
                commonOccurrences,
                CliRowSelectionCapabilities.All);
        return lowering.IsSuccess
            ? CliRowSelectionRouteEnvelopeResult.Success(
                lowering)
            : CliRowSelectionRouteEnvelopeResult.Failed(
                lowering);
    }

    private static CandidateObservation Observe(
        string[] arguments,
        CliRowSelectionRouteCandidate candidate)
    {
        string[] prefixedArguments =
            [
                .. candidate.CommandPrefix,
                .. arguments
            ];
        CliRowSelectionArgumentResult result =
            CliRowSelectionArgumentAdapter.InspectExplicit(
                candidate.ParserRoot,
                prefixedArguments,
                candidate.Bindings);
        CliRowSelectionArgumentResult declarationResult =
            CliRowSelectionArgumentAdapter.InspectExplicit(
                candidate.ParserRoot,
                candidate.CommandPrefix.ToArray(),
                candidate.Bindings);
        int prefixLength =
            candidate.CommandPrefix.Count;
        CliRowSelectionOccurrence<string>[] occurrences =
            result.Occurrences
                .Where(
                    occurrence =>
                        occurrence.Position >= prefixLength)
                .Select(
                    occurrence =>
                        Translate(
                            occurrence,
                            prefixLength))
                .ToArray();
        CliRowSelectionArgumentFailure? argumentFailure =
            result.ArgumentFailure is not null
                && result.ArgumentFailure.Position >= prefixLength
                ? new(
                    result.ArgumentFailure.Reason,
                    result.ArgumentFailure.OccurrenceKind,
                    result.ArgumentFailure.Position
                        - prefixLength)
                : null;
        int[] requiredValuePositions =
            result.RequiredValuePositions
                .Where(position => position >= prefixLength)
                .Select(position => position - prefixLength)
                .Distinct()
                .Order()
                .ToArray();
        int? unexpectedCommandPosition =
            !ReferenceEquals(
                result.ParseResult.CommandResult.Command,
                candidate.ExpectedCommand)
            && result.SelectedCommandPosition is int
                selectedCommandPosition
            && selectedCommandPosition >= prefixLength
                ? selectedCommandPosition - prefixLength
                : null;
        CliRowSelectionLoweringResult<string> loweringResult =
            CliRowSelectionLowerer.Lower(
                occurrences,
                CliRowSelectionCapabilities.All);
        bool[] declaredKinds =
            Enum.GetValues<CliRowSelectionOccurrenceKind>()
                .Select(
                    kind =>
                        CliRowSelectionArgumentAdapter.IsDeclared(
                            declarationResult.ParseResult,
                            candidate.Bindings,
                            kind))
                .ToArray();

        return new(
            candidate,
            result.ParseResult,
            ReferenceEquals(
                result.ParseResult.CommandResult.Command,
                candidate.ExpectedCommand),
            occurrences,
            argumentFailure,
            requiredValuePositions,
            unexpectedCommandPosition,
            loweringResult,
            declaredKinds);
    }

    private static CliRowSelectionArgumentFailure?
        CommonArgumentFailure(
            IReadOnlyList<CandidateObservation> observations)
    {
        CliRowSelectionArgumentFailure? first =
            observations[0].ArgumentFailure;
        if (first is null)
        {
            return null;
        }

        return observations.All(
            observation =>
                Same(
                    first,
                    observation.ArgumentFailure))
            ? first
            : null;
    }

    private static CliRowSelectionLoweringResult<string>?
        CommonLoweringFailure(
            IReadOnlyList<CandidateObservation> observations)
    {
        CliRowSelectionLoweringResult<string> first =
            observations[0].LoweringResult;
        if (first.IsSuccess)
        {
            return null;
        }

        return observations
            .Skip(1)
            .Select(observation => observation.LoweringResult)
            .All(
                result =>
                    !result.IsSuccess
                    && Same(
                        first.Failure!,
                        result.Failure!))
            ? first
            : null;
    }

    private static RequestScan ScanRequests(
        IReadOnlyList<string> arguments,
        IReadOnlyList<CandidateObservation> observations)
    {
        var requests =
            new List<RequestToken>();
        var deferredPositions =
            new SortedSet<int>();
        bool lineSelectionDeferred = false;

        for (int position = 0;
            position < arguments.Count;
            position++)
        {
            string token =
                arguments[position];
            if (token == "--")
            {
                break;
            }

            bool[] requiredClaims =
                observations
                    .Select(
                        observation =>
                            observation.RequiredValuePositions
                                .Contains(position))
                    .ToArray();
            bool bare =
                CliRowSelectionArgumentAdapter
                    .IsBareLimitShorthand(token);
            bool[] bareDeclared =
                observations
                    .Select(
                        observation =>
                            CliRowSelectionArgumentAdapter
                                .HasShortLimitAlias(
                                    observation.Candidate.Bindings)
                            && observation.IsDeclared(
                                    CliRowSelectionOccurrenceKind
                                        .Limit))
                    .ToArray();
            CliRowSelectionOccurrenceKind?[] meanings =
                observations
                    .Select(
                        observation =>
                            CliRowSelectionArgumentAdapter
                                .TryClassifyExplicitRowToken(
                                    token,
                                    observation.Candidate.Bindings,
                                    out CliRowSelectionOccurrenceKind
                                        kind)
                                ? (CliRowSelectionOccurrenceKind?)kind
                                : null)
                    .ToArray();
            bool potentialRequest =
                bare
                && bareDeclared.Any(
                    declared => declared)
                || meanings.Any(
                    meaning =>
                        meaning is not null);
            if (!potentialRequest
                || requiredClaims.All(claimed => claimed))
            {
                continue;
            }

            if (requiredClaims.Any(claimed => claimed))
            {
                lineSelectionDeferred |=
                    meanings.Any(
                        meaning =>
                            meaning is
                                CliRowSelectionOccurrenceKind.Lines
                                or CliRowSelectionOccurrenceKind
                                    .TailLines);
                deferredPositions.Add(position);
                continue;
            }

            if (bare)
            {
                if (bareDeclared.Any(value => !value))
                {
                    deferredPositions.Add(position);
                    continue;
                }

                requests.Add(
                    new(
                        position,
                        CliRowSelectionOccurrenceKind.Limit,
                        bareDeclared,
                        MeaningsDiffer: false));
                continue;
            }

            CliRowSelectionOccurrenceKind kind =
                meanings
                    .Where(meaning => meaning is not null)
                    .Select(meaning => meaning!.Value)
                    .First();
            bool meaningsDiffer =
                meanings.Any(meaning => meaning is null)
                || meanings
                    .Where(meaning => meaning is not null)
                    .Select(meaning => meaning!.Value)
                    .Distinct()
                    .Skip(1)
                    .Any();
            var declared =
                new bool[observations.Count];
            for (int index = 0;
                index < observations.Count;
                index++)
            {
                declared[index] =
                    meanings[index] is
                        CliRowSelectionOccurrenceKind
                            candidateKind
                    && CliRowSelectionArgumentAdapter
                        .TryClassifyExplicitRowToken(
                            token,
                            observations[index].Candidate.Bindings,
                            out _)
                    && observations[index].IsDeclared(
                        candidateKind);
            }

            requests.Add(
                new(
                    position,
                    kind,
                    declared,
                    meaningsDiffer));
        }

        bool lineSelection =
            requests.Any(
                request =>
                    request.Kind is
                        CliRowSelectionOccurrenceKind.Lines
                        or CliRowSelectionOccurrenceKind.TailLines
                    && !request.MeaningsDiffer
                    && request.Declared.All(
                        declared => declared));
        return new(
            requests,
            deferredPositions.ToArray(),
            lineSelection,
            lineSelectionDeferred);
    }

    private static IReadOnlyList<
        CliRowSelectionOccurrence<string>>? CommonOccurrences(
            IReadOnlyList<CandidateObservation> observations)
    {
        IReadOnlyList<CliRowSelectionOccurrence<string>> first =
            observations[0].Occurrences;
        return observations
            .Skip(1)
            .All(
                observation =>
                    Same(
                        first,
                        observation.Occurrences))
            ? first
            : null;
    }

    private static bool Same(
        CliRowSelectionArgumentFailure expected,
        CliRowSelectionArgumentFailure? actual) =>
        actual is not null
        && expected.Reason == actual.Reason
        && expected.OccurrenceKind == actual.OccurrenceKind
        && expected.Position == actual.Position;

    private static bool Same(
        CliRowSelectionFailure expected,
        CliRowSelectionFailure actual) =>
        expected.Reason == actual.Reason
        && expected.OccurrenceKind == actual.OccurrenceKind
        && expected.Position == actual.Position
        && expected.MissingCapabilities
            == actual.MissingCapabilities;

    private static bool Same(
        IReadOnlyList<CliRowSelectionOccurrence<string>> expected,
        IReadOnlyList<CliRowSelectionOccurrence<string>> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (int index = 0;
            index < expected.Count;
            index++)
        {
            if (!Same(
                    expected[index],
                    actual[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Same(
        CliRowSelectionOccurrence<string> expected,
        CliRowSelectionOccurrence<string> actual)
    {
        if (expected.Kind != actual.Kind
            || expected.Position != actual.Position)
        {
            return false;
        }

        return expected.Kind switch
        {
            CliRowSelectionOccurrenceKind.Limit
                or CliRowSelectionOccurrenceKind.Rows
                or CliRowSelectionOccurrenceKind.Top =>
                expected.Value.Equals(
                    actual.Value,
                    StringComparison.Ordinal),
            CliRowSelectionOccurrenceKind.OrderBy =>
                expected.OrderOperand.Equals(
                    actual.OrderOperand,
                    StringComparison.Ordinal),
            _ => true
        };
    }

    private static CliRowSelectionOccurrence<string> Translate(
        CliRowSelectionOccurrence<string> occurrence,
        int prefixLength)
    {
        int position =
            occurrence.Position - prefixLength;
        return occurrence.Kind switch
        {
            CliRowSelectionOccurrenceKind.Limit =>
                CliRowSelectionOccurrence<string>.Limit(
                    position,
                    occurrence.Value),
            CliRowSelectionOccurrenceKind.Rows =>
                CliRowSelectionOccurrence<string>.Rows(
                    position,
                    occurrence.Value),
            CliRowSelectionOccurrenceKind.Top =>
                CliRowSelectionOccurrence<string>.Top(
                    position,
                    occurrence.Value),
            CliRowSelectionOccurrenceKind.OrderBy =>
                CliRowSelectionOccurrence<string>.OrderBy(
                    position,
                    occurrence.OrderOperand),
            CliRowSelectionOccurrenceKind.Head =>
                CliRowSelectionOccurrence<string>.Head(
                    position),
            CliRowSelectionOccurrenceKind.Tail =>
                CliRowSelectionOccurrence<string>.Tail(
                    position),
            CliRowSelectionOccurrenceKind.Lines =>
                CliRowSelectionOccurrence<string>.Lines(
                    position),
            CliRowSelectionOccurrenceKind.TailLines =>
                CliRowSelectionOccurrence<string>.TailLines(
                    position),
            _ => throw new ArgumentOutOfRangeException(
                nameof(occurrence),
                occurrence.Kind,
                null)
        };
    }

    private sealed class CandidateObservation
    {
        public CandidateObservation(
            CliRowSelectionRouteCandidate candidate,
            ParseResult parseResult,
            bool expectedCommandSelected,
            IReadOnlyList<CliRowSelectionOccurrence<string>>
                occurrences,
            CliRowSelectionArgumentFailure? argumentFailure,
            IReadOnlyList<int> requiredValuePositions,
            int? unexpectedCommandPosition,
            CliRowSelectionLoweringResult<string> loweringResult,
            bool[] declaredKinds)
        {
            Candidate = candidate;
            ParseResult = parseResult;
            ExpectedCommandSelected = expectedCommandSelected;
            Occurrences = occurrences;
            ArgumentFailure = argumentFailure;
            RequiredValuePositions = requiredValuePositions;
            UnexpectedCommandPosition =
                unexpectedCommandPosition;
            LoweringResult = loweringResult;
            DeclaredKinds = declaredKinds;
        }

        public CliRowSelectionRouteCandidate Candidate { get; }

        public ParseResult ParseResult
        {
            get;
        }

        public bool ExpectedCommandSelected { get; }

        public IReadOnlyList<CliRowSelectionOccurrence<string>>
            Occurrences { get; }

        public CliRowSelectionArgumentFailure? ArgumentFailure
        {
            get;
        }

        public IReadOnlyList<int> RequiredValuePositions { get; }

        public int? UnexpectedCommandPosition { get; }

        public CliRowSelectionLoweringResult<string> LoweringResult
        {
            get;
        }

        private bool[] DeclaredKinds { get; }

        public bool LineSelection =>
            Occurrences.Any(
                occurrence =>
                    occurrence.Kind is
                        CliRowSelectionOccurrenceKind.Lines
                        or CliRowSelectionOccurrenceKind.TailLines);

        public bool IsDeclared(
            CliRowSelectionOccurrenceKind kind) =>
            DeclaredKinds[(int)kind];
    }

    private sealed record RequestToken(
        int Position,
        CliRowSelectionOccurrenceKind Kind,
        bool[] Declared,
        bool MeaningsDiffer);

    private sealed record RequestScan(
        IReadOnlyList<RequestToken> Requests,
        IReadOnlyList<int> DeferredPositions,
        bool LineSelection,
        bool LineSelectionDeferred);
}
