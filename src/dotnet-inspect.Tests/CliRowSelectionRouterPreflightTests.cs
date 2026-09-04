using System.CommandLine;
using DotnetInspector.CommandLine;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

public sealed class CliRowSelectionRouterPreflightTests
{
    [Fact]
    public void CommonRequestLowersWithoutRouting()
    {
        CandidateFixture first = new("first");
        CandidateFixture second = new("second");

        CliRowSelectionRouteEnvelopeResult result =
            Evaluate(
                ["Target", "-5"],
                first,
                second);

        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.Success,
            result.Outcome);
        Assert.NotNull(result.LoweringResult);
        RowSelectionIntentOperation<string> operation =
            Assert.Single(
                result.LoweringResult!.Value!
                    .SemanticIntent.Operations);
        Assert.Equal(
            RowSelectionStageKind.Head,
            operation.Kind);
        Assert.Equal(5, operation.Count);

        CliRowSelectionRouteEnvelopeResult unrelatedUnknown =
            Evaluate(
                [
                    "Target",
                    "--unknown",
                    "-n",
                    "2"
                ],
                first,
                second);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.Success,
            unrelatedUnknown.Outcome);
        Assert.Equal(
            2,
            Assert.Single(
                unrelatedUnknown.LoweringResult!.Value!
                    .SemanticIntent.Operations)
                .Count);

        CliRowSelectionRouteEnvelopeResult noRequest =
            Evaluate(
                [
                    "Target",
                    "--unknown"
                ],
                first,
                second);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.NoRequest,
            noRequest.Outcome);

        CliRowSelectionRouteEnvelopeResult afterTerminator =
            Evaluate(
                [
                    "Target",
                    "--",
                    "-5"
                ],
                first,
                second);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.NoRequest,
            afterTerminator.Outcome);
    }

    [Fact]
    public void RequiredValueUnionDefersOnlyDependentDecisions()
    {
        CandidateFixture required =
            new(
                "required",
                requiredArity:
                    ArgumentArity.ExactlyOne);
        CandidateFixture optional =
            new(
                "optional",
                requiredArity:
                    ArgumentArity.ZeroOrOne);

        CliRowSelectionRouteEnvelopeResult deferred =
            Evaluate(
                [
                    "Target",
                    "--required",
                    "-5"
                ],
                required,
                optional);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.Deferred,
            deferred.Outcome);
        Assert.Equal(
            [2],
            deferred.DeferredPositions);

        CandidateFixture alsoRequired =
            new(
                "also-required",
                requiredArity:
                    ArgumentArity.ExactlyOne);
        CliRowSelectionRouteEnvelopeResult protectedValue =
            Evaluate(
                [
                    "Target",
                    "--required",
                    "-5"
                ],
                required,
                alsoRequired);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.NoRequest,
            protectedValue.Outcome);

        CliRowSelectionRouteEnvelopeResult commonEarlierFailure =
            Evaluate(
                [
                    "Target",
                    "--rows",
                    "bad",
                    "--required",
                    "-5"
                ],
                required,
                optional);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.LoweringFailure,
            commonEarlierFailure.Outcome);
        Assert.Equal(
            CliRowSelectionFailureReason.InvalidWindowForm,
            commonEarlierFailure.Failure!.Reason);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Rows,
            commonEarlierFailure.Failure.OccurrenceKind);
        Assert.Equal(
            1,
            commonEarlierFailure.Failure.Position);

        CandidateFixture noWindow =
            new(
                "no-window",
                capabilities:
                    CliRowSelectionCapabilities.None,
                requiredArity:
                    ArgumentArity.ExactlyOne);
        CandidateFixture noWindowOptional =
            new(
                "no-window-optional",
                capabilities:
                    CliRowSelectionCapabilities.None,
                requiredArity:
                    ArgumentArity.ZeroOrOne);
        CliRowSelectionRouteEnvelopeResult independentFailure =
            Evaluate(
                [
                    "Target",
                    "--rows",
                    "1..2",
                    "--required",
                    "-5"
                ],
                noWindow,
                noWindowOptional);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .UnsupportedCapability,
            independentFailure.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Rows,
            independentFailure.RequestKind);
    }

    [Fact]
    public void MixedDeclarationsPreserveExplicitOptionIdentity()
    {
        CandidateFixture declared = new("declared");
        CandidateFixture undeclared =
            new(
                "undeclared",
                declarations:
                    RowDeclarations.All
                    & ~RowDeclarations.Limit);

        CliRowSelectionRouteEnvelopeResult bare =
            Evaluate(
                [
                    "Target",
                    "-5"
                ],
                declared,
                undeclared);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.Deferred,
            bare.Outcome);
        Assert.Equal([1], bare.DeferredPositions);

        CliRowSelectionRouteEnvelopeResult explicitLimit =
            Evaluate(
                [
                    "Target",
                    "-n",
                    "5"
                ],
                declared,
                undeclared);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .ExplicitCommandRequired,
            explicitLimit.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Limit,
            explicitLimit.RequestKind);
        Assert.Equal(1, explicitLimit.Position);

        CliRowSelectionRouteEnvelopeResult attachedExplicitLimit =
            Evaluate(
                [
                    "Target",
                    "-n=5"
                ],
                declared,
                undeclared);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .ExplicitCommandRequired,
            attachedExplicitLimit.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Limit,
            attachedExplicitLimit.RequestKind);
        Assert.Equal(1, attachedExplicitLimit.Position);

        CandidateFixture alsoUndeclared =
            new(
                "also-undeclared",
                declarations:
                    RowDeclarations.All
                    & ~RowDeclarations.Limit);
        CliRowSelectionRouteEnvelopeResult unsupported =
            Evaluate(
                [
                    "Target",
                    "-n",
                    "5"
                ],
                undeclared,
                alsoUndeclared);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .UnsupportedCapability,
            unsupported.Outcome);
        Assert.Equal(
            CliRowSelectionCapabilities.HeadTail,
            unsupported.Failure!.MissingCapabilities);

        CliRowSelectionRouteEnvelopeResult attachedUnsupported =
            Evaluate(
                [
                    "Target",
                    "-n:5"
                ],
                undeclared,
                alsoUndeclared);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .UnsupportedCapability,
            attachedUnsupported.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Limit,
            attachedUnsupported.RequestKind);

        CliRowSelectionRouteEnvelopeResult unboundBare =
            Evaluate(
                [
                    "Target",
                    "-5"
                ],
                undeclared,
                alsoUndeclared);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.NoRequest,
            unboundBare.Outcome);
    }

    [Fact]
    public void CandidateCapabilitiesAreComparedPerRequest()
    {
        CandidateFixture window =
            new(
                "window",
                capabilities:
                    CliRowSelectionCapabilities.Window);
        CandidateFixture noWindow =
            new(
                "no-window",
                capabilities:
                    CliRowSelectionCapabilities.None);

        CliRowSelectionRouteEnvelopeResult mixed =
            Evaluate(
                [
                    "Target",
                    "--rows",
                    "1..2"
                ],
                window,
                noWindow);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .ExplicitCommandRequired,
            mixed.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Rows,
            mixed.RequestKind);
        Assert.Equal(1, mixed.Position);

        CandidateFixture alsoNoWindow =
            new(
                "also-no-window",
                capabilities:
                    CliRowSelectionCapabilities.None);
        CliRowSelectionRouteEnvelopeResult unsupported =
            Evaluate(
                [
                    "Target",
                    "--rows",
                    "1..2"
                ],
                noWindow,
                alsoNoWindow);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .UnsupportedCapability,
            unsupported.Outcome);
        Assert.Equal(
            CliRowSelectionCapabilities.Window,
            unsupported.Failure!.MissingCapabilities);

        CliRowSelectionRouteEnvelopeResult attachedUnsupportedWindow =
            Evaluate(
                [
                    "Target",
                    "--rows=1..2"
                ],
                noWindow,
                alsoNoWindow);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .UnsupportedCapability,
            attachedUnsupportedWindow.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Rows,
            attachedUnsupportedWindow.RequestKind);

        CliRowSelectionRouteEnvelopeResult attachedMixedWindow =
            Evaluate(
                [
                    "Target",
                    "--rows:1..2"
                ],
                window,
                noWindow);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .ExplicitCommandRequired,
            attachedMixedWindow.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Rows,
            attachedMixedWindow.RequestKind);

        CliRowSelectionRouteEnvelopeResult neighboringAttachedWindow =
            Evaluate(
                [
                    "Target",
                    "-5",
                    "--rows=2..4"
                ],
                new CandidateFixture(
                    "all-capabilities"),
                new CandidateFixture(
                    "headtail-only",
                    capabilities:
                        CliRowSelectionCapabilities.HeadTail));
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .ExplicitCommandRequired,
            neighboringAttachedWindow.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Rows,
            neighboringAttachedWindow.RequestKind);
        Assert.Equal(2, neighboringAttachedWindow.Position);

        CandidateFixture semanticOnly =
            new(
                "semantic-only",
                capabilities:
                    CliRowSelectionCapabilities.HeadTail);
        CandidateFixture lines =
            new(
                "lines",
                capabilities:
                    CliRowSelectionCapabilities.Lines);
        CliRowSelectionRouteEnvelopeResult lineUnit =
            Evaluate(
                [
                    "Target",
                    "-n",
                    "2",
                    "--lines"
                ],
                semanticOnly,
                lines);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .ExplicitCommandRequired,
            lineUnit.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Limit,
            lineUnit.RequestKind);
        Assert.Equal(1, lineUnit.Position);

        CandidateFixture mixedLineDeclaration =
            new(
                "mixed-line",
                capabilities:
                    CliRowSelectionCapabilities.Lines);
        CandidateFixture semanticWithLineCapability =
            new(
                "semantic-with-line-capability",
                declarations:
                    RowDeclarations.All
                    & ~RowDeclarations.Lines,
                capabilities:
                    CliRowSelectionCapabilities.Lines);
        CliRowSelectionRouteEnvelopeResult mixedLineMeaning =
            Evaluate(
                [
                    "Target",
                    "-n",
                    "2",
                    "--lines"
                ],
                mixedLineDeclaration,
                semanticWithLineCapability);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .ExplicitCommandRequired,
            mixedLineMeaning.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Limit,
            mixedLineMeaning.RequestKind);
        Assert.Equal(1, mixedLineMeaning.Position);

        CandidateFixture noLineDeclaration =
            new(
                "no-line-declaration",
                declarations:
                    RowDeclarations.All
                    & ~RowDeclarations.Lines,
                capabilities:
                    CliRowSelectionCapabilities.HeadTail);
        CandidateFixture alsoNoLineDeclaration =
            new(
                "also-no-line-declaration",
                declarations:
                    RowDeclarations.All
                    & ~RowDeclarations.Lines,
                capabilities:
                    CliRowSelectionCapabilities.HeadTail);
        CliRowSelectionRouteEnvelopeResult undeclaredLineUnit =
            Evaluate(
                [
                    "Target",
                    "-n",
                    "2",
                    "--lines"
                ],
                noLineDeclaration,
                alsoNoLineDeclaration);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .UnsupportedCapability,
            undeclaredLineUnit.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Lines,
            undeclaredLineUnit.RequestKind);
        Assert.Equal(3, undeclaredLineUnit.Position);

        CandidateFixture noOrder =
            new(
                "no-order",
                declarations:
                    RowDeclarations.All
                    & ~RowDeclarations.OrderBy
                    & ~RowDeclarations.Top);
        CandidateFixture topOnly =
            new(
                "top-only",
                declarations:
                    RowDeclarations.All
                    & ~RowDeclarations.OrderBy);
        CliRowSelectionRouteEnvelopeResult firstUnsupported =
            Evaluate(
                [
                    "Target",
                    "--order-by",
                    "name",
                    "--top",
                    "3"
                ],
                noOrder,
                topOnly);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .UnsupportedCapability,
            firstUnsupported.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.OrderBy,
            firstUnsupported.RequestKind);
        Assert.Equal(1, firstUnsupported.Position);

        CliRowSelectionRouteEnvelopeResult attachedTop =
            Evaluate(
                [
                    "Target",
                    "--top=3"
                ],
                new CandidateFixture(
                    "top-capable"),
                new CandidateFixture(
                    "not-top-capable",
                    capabilities:
                        CliRowSelectionCapabilities.None));
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .ExplicitCommandRequired,
            attachedTop.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Top,
            attachedTop.RequestKind);
    }

    [Fact]
    public void CommonFailuresUseOriginalImplicitArgvPositions()
    {
        CandidateFixture first =
            new(
                "first",
                parentName: "outer");
        CandidateFixture second =
            new(
                "second",
                parentName: "outer");

        CliRowSelectionRouteEnvelopeResult missing =
            Evaluate(
                [
                    "Target",
                    "--rows"
                ],
                first,
                second);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.ArgumentFailure,
            missing.Outcome);
        Assert.Equal(
            CliRowSelectionArgumentFailureReason.MissingValue,
            missing.ArgumentFailure!.Reason);
        Assert.Equal(1, missing.ArgumentFailure.Position);

        CliRowSelectionRouteEnvelopeResult repeated =
            Evaluate(
                [
                    "Target",
                    "-n",
                    "1",
                    "-2"
                ],
                first,
                second);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.LoweringFailure,
            repeated.Outcome);
        Assert.Equal(
            CliRowSelectionFailureReason.RepeatedGesture,
            repeated.Failure!.Reason);
        Assert.Equal(3, repeated.Failure.Position);
    }

    [Fact]
    public void UnexpectedChildSelectionDefersToRouting()
    {
        CandidateFixture ordinary =
            new("ordinary");
        CandidateFixture withTargetSubcommand =
            new(
                "with-child",
                childName: "Target",
                childDeclarations:
                    RowDeclarations.All);

        CliRowSelectionRouteEnvelopeResult result =
            Evaluate(
                [
                    "Target",
                    "-n",
                    "2"
                ],
                ordinary,
                withTargetSubcommand);

        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.Deferred,
            result.Outcome);
        Assert.Equal([0], result.DeferredPositions);

        CliRowSelectionRouteEnvelopeResult commonFailure =
            Evaluate(
                [
                    "Target",
                    "--rows",
                    "bad"
                ],
                ordinary,
                withTargetSubcommand);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.LoweringFailure,
            commonFailure.Outcome);
        Assert.Equal(
            CliRowSelectionFailureReason.InvalidWindowForm,
            commonFailure.Failure!.Reason);
        Assert.Equal(1, commonFailure.Failure.Position);

        CandidateFixture unsupportedOrdinary =
            new(
                "unsupported-ordinary",
                capabilities:
                    CliRowSelectionCapabilities.None);
        CandidateFixture unsupportedWithChild =
            new(
                "unsupported-with-child",
                capabilities:
                    CliRowSelectionCapabilities.None,
                childName: "Target",
                childDeclarations:
                    RowDeclarations.All);
        CliRowSelectionRouteEnvelopeResult commonUnsupported =
            Evaluate(
                [
                    "Target",
                    "--rows",
                    "1..2"
                ],
                unsupportedOrdinary,
                unsupportedWithChild);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .UnsupportedCapability,
            commonUnsupported.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Rows,
            commonUnsupported.RequestKind);
        Assert.Equal(1, commonUnsupported.Position);
    }

    [Fact]
    public void CandidateSpecificParserErrorsCannotManufactureACommonRequest()
    {
        CandidateFixture ownsFollowingOption =
            new(
                "owns-option",
                extraOptionName:
                    "--only-in-first");
        CandidateFixture doesNotOwnFollowingOption =
            new("does-not-own-option");
        CandidateFixture third =
            new("third");

        CliRowSelectionRouteEnvelopeResult result =
            Evaluate(
                [
                    "Target",
                    "-n",
                    "--only-in-first"
                ],
                ownsFollowingOption,
                doesNotOwnFollowingOption,
                third);

        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.Deferred,
            result.Outcome);
        Assert.Equal([1], result.DeferredPositions);

        CliRowSelectionRouteEnvelopeResult reordered =
            Evaluate(
                [
                    "Target",
                    "-n",
                    "--only-in-first"
                ],
                third,
                ownsFollowingOption,
                doesNotOwnFollowingOption);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.Deferred,
            reordered.Outcome);
        Assert.Equal([1], reordered.DeferredPositions);
    }

    [Fact]
    public void DeferredLineModifierCannotChangeEarlierCountMeaning()
    {
        CandidateFixture lineIsRequiredValue =
            new(
                "line-is-required-value",
                declarations:
                    RowDeclarations.All
                    & ~RowDeclarations.Lines,
                requiredArity:
                    ArgumentArity.ExactlyOne);
        CandidateFixture lineIsModifier =
            new(
                "line-is-modifier",
                requiredArity:
                    ArgumentArity.ZeroOrOne);

        CliRowSelectionRouteEnvelopeResult result =
            Evaluate(
                [
                    "Target",
                    "-n",
                    "2",
                    "--required",
                    "--lines"
                ],
                lineIsRequiredValue,
                lineIsModifier);

        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.Deferred,
            result.Outcome);
        Assert.Equal([4], result.DeferredPositions);
    }

    [Fact]
    public void MixedMeaningsDoNotSelectACandidateKind()
    {
        CandidateFixture topMeaning =
            new("top-meaning");
        CandidateFixture rowsMeaning =
            new(
                "rows-meaning",
                rowsName: "--top",
                omitTopOrderBindings: true);

        CliRowSelectionRouteEnvelopeResult result =
            Evaluate(
                [
                    "Target",
                    "--top",
                    "2"
                ],
                topMeaning,
                rowsMeaning);
        CliRowSelectionRouteEnvelopeResult reversed =
            Evaluate(
                [
                    "Target",
                    "--top",
                    "2"
                ],
                rowsMeaning,
                topMeaning);

        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .ExplicitCommandRequired,
            result.Outcome);
        Assert.Equal(result.Outcome, reversed.Outcome);
        Assert.Null(result.RequestKind);
        Assert.Null(reversed.RequestKind);
        Assert.Equal(1, result.Position);
        Assert.Equal(result.Position, reversed.Position);

        CandidateFixture undeclaredTopMeaning =
            new(
                "undeclared-top-meaning",
                declarations:
                    RowDeclarations.All
                    & ~RowDeclarations.Top);
        CandidateFixture undeclaredRowsMeaning =
            new(
                "undeclared-rows-meaning",
                declarations:
                    RowDeclarations.All
                    & ~RowDeclarations.Rows,
                rowsName: "--top",
                omitTopOrderBindings: true);
        CliRowSelectionRouteEnvelopeResult undeclared =
            Evaluate(
                [
                    "Target",
                    "--top",
                    "2"
                ],
                undeclaredTopMeaning,
                undeclaredRowsMeaning);
        CliRowSelectionRouteEnvelopeResult undeclaredReversed =
            Evaluate(
                [
                    "Target",
                    "--top",
                    "2"
                ],
                undeclaredRowsMeaning,
                undeclaredTopMeaning);

        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .ExplicitCommandRequired,
            undeclared.Outcome);
        Assert.Equal(
            undeclared.Outcome,
            undeclaredReversed.Outcome);
        Assert.Null(undeclared.RequestKind);
        Assert.Null(undeclaredReversed.RequestKind);
        Assert.Equal(1, undeclared.Position);
        Assert.Equal(
            undeclared.Position,
            undeclaredReversed.Position);
    }

    [Fact]
    public void CandidateBindingsMayOmitTopGrammar()
    {
        CandidateFixture windowOnly =
            new(
                "window-only",
                omitTopOrderBindings: true);

        CliRowSelectionRouteEnvelopeResult noRequest =
            Evaluate(
                ["Target"],
                windowOnly);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.NoRequest,
            noRequest.Outcome);

        CliRowSelectionRouteEnvelopeResult unsupported =
            Evaluate(
                [
                    "Target",
                    "--top",
                    "2"
                ],
                windowOnly);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome
                .UnsupportedCapability,
            unsupported.Outcome);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Top,
            unsupported.RequestKind);
    }

    private static CliRowSelectionRouteEnvelopeResult Evaluate(
        string[] arguments,
        params CandidateFixture[] candidates) =>
        CliRowSelectionRouteEnvelope.Evaluate(
            arguments,
            candidates
                .Select(candidate => candidate.Candidate)
                .ToArray());

    [Flags]
    private enum RowDeclarations
    {
        None = 0,
        Limit = 1,
        Rows = 2,
        Top = 4,
        OrderBy = 8,
        Head = 16,
        Tail = 32,
        Lines = 64,
        TailLines = 128,
        All =
            Limit
            | Rows
            | Top
            | OrderBy
            | Head
            | Tail
            | Lines
            | TailLines
    }

    private sealed class CandidateFixture
    {
        public CandidateFixture(
            string name,
            RowDeclarations declarations =
                RowDeclarations.All,
            CliRowSelectionCapabilities capabilities =
                CliRowSelectionCapabilities.All,
            ArgumentArity? requiredArity = null,
            string? parentName = null,
            string? childName = null,
            RowDeclarations childDeclarations =
                RowDeclarations.None,
            string? extraOptionName = null,
            bool omitTopOrderBindings = false,
            string rowsName = "--rows")
        {
            Option<string[]> limit =
                RowValueOption("-n");
            Option<string[]> rows =
                RowValueOption(rowsName);
            Option<string[]>? top =
                omitTopOrderBindings
                    ? null
                    : RowValueOption("--top");
            Option<string[]>? orderBy =
                omitTopOrderBindings
                    ? null
                    : RowValueOption("--order-by");
            Option<bool> head =
                ModifierOption("--head");
            Option<bool> tail =
                ModifierOption("--tail");
            Option<bool> lines =
                ModifierOption("--lines");
            Option<bool> tailLines =
                ModifierOption("--tail-lines");
            var required =
                new Option<string?>("--required")
                {
                    Arity =
                        requiredArity
                        ?? ArgumentArity.ZeroOrOne
                };
            var command =
                new Command(name);
            Add(
                command,
                declarations,
                RowDeclarations.Limit,
                limit);
            Add(
                command,
                declarations,
                RowDeclarations.Rows,
                rows);
            if (top is not null)
            {
                Add(
                    command,
                    declarations,
                    RowDeclarations.Top,
                    top);
            }
            if (orderBy is not null)
            {
                Add(
                    command,
                    declarations,
                    RowDeclarations.OrderBy,
                    orderBy);
            }
            Add(
                command,
                declarations,
                RowDeclarations.Head,
                head);
            Add(
                command,
                declarations,
                RowDeclarations.Tail,
                tail);
            Add(
                command,
                declarations,
                RowDeclarations.Lines,
                lines);
            Add(
                command,
                declarations,
                RowDeclarations.TailLines,
                tailLines);
            command.Options.Add(required);
            if (extraOptionName is not null)
            {
                command.Options.Add(
                    ModifierOption(
                        extraOptionName));
            }
            command.Arguments.Add(
                new Argument<string[]>("values")
                {
                    Arity =
                        ArgumentArity.ZeroOrMore
                });
            if (childName is not null)
            {
                var child =
                    new Command(childName);
                if ((childDeclarations
                        & RowDeclarations.Limit) != 0)
                {
                    limit.Recursive = true;
                }
                if ((childDeclarations
                        & RowDeclarations.Rows) != 0)
                {
                    rows.Recursive = true;
                }
                if ((childDeclarations
                        & RowDeclarations.Top) != 0)
                {
                    if (top is not null)
                    {
                        top.Recursive = true;
                    }
                }
                if ((childDeclarations
                        & RowDeclarations.OrderBy) != 0)
                {
                    if (orderBy is not null)
                    {
                        orderBy.Recursive = true;
                    }
                }
                if ((childDeclarations
                        & RowDeclarations.Head) != 0)
                {
                    head.Recursive = true;
                }
                if ((childDeclarations
                        & RowDeclarations.Tail) != 0)
                {
                    tail.Recursive = true;
                }
                if ((childDeclarations
                        & RowDeclarations.Lines) != 0)
                {
                    lines.Recursive = true;
                }
                if ((childDeclarations
                        & RowDeclarations.TailLines) != 0)
                {
                    tailLines.Recursive = true;
                }
                command.Subcommands.Add(child);
            }

            var root = new RootCommand();
            string[] prefix;
            if (parentName is null)
            {
                root.Subcommands.Add(command);
                prefix = [name];
            }
            else
            {
                var parent =
                    new Command(parentName)
                    {
                        command
                    };
                root.Subcommands.Add(parent);
                prefix =
                    [
                        parentName,
                        name
                    ];
            }

            Candidate =
                new(
                    root,
                    command,
                    prefix,
                    new(
                        limit,
                        rows,
                        top,
                        orderBy,
                        head,
                        tail,
                        lines,
                        tailLines),
                    capabilities);
        }

        public CliRowSelectionRouteCandidate Candidate { get; }

        private static void Add(
            Command command,
            RowDeclarations declarations,
            RowDeclarations declaration,
            Option option)
        {
            if ((declarations & declaration) != 0)
            {
                command.Options.Add(option);
            }
        }

        private static Option<string[]> RowValueOption(
            string name) =>
            new(name)
            {
                Arity =
                    ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = false
            };

        private static Option<bool> ModifierOption(
            string name) =>
            new(name)
            {
                Arity =
                    ArgumentArity.Zero
            };
    }
}
