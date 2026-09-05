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
    public void LaterCommonArgumentFailuresSurviveCandidateSpecificErrors()
    {
        CandidateFixture first =
            new("first", parentName: "scope", extraOptionName: "--only-in-first");
        CandidateFixture second = new("second");
        (string[] Suffix, CliRowSelectionOccurrenceKind Kind,
            CliRowSelectionArgumentFailureReason Reason)[] cases =
        [
            (["--rows"], CliRowSelectionOccurrenceKind.Rows,
                CliRowSelectionArgumentFailureReason.MissingValue),
            (["--head=true"], CliRowSelectionOccurrenceKind.Head,
                CliRowSelectionArgumentFailureReason.AttachedValueOnModifier),
            (["--rows", "--head=true"], CliRowSelectionOccurrenceKind.Rows,
                CliRowSelectionArgumentFailureReason.MissingValue),
            (["--head=true", "--rows"], CliRowSelectionOccurrenceKind.Head,
                CliRowSelectionArgumentFailureReason.AttachedValueOnModifier)
        ];
        foreach (var scenario in cases)
        {
            string[] arguments = ["Target", "-n", "--only-in-first", .. scenario.Suffix];
            CliRowSelectionRouteEnvelopeResult result = Evaluate(arguments, first, second);
            CliRowSelectionRouteEnvelopeResult reversed = Evaluate(arguments, second, first);

            Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.ArgumentFailure, result.Outcome);
            Assert.Equal(result.Outcome, reversed.Outcome);
            Assert.Equal(scenario.Reason, result.ArgumentFailure!.Reason);
            Assert.Equal(result.ArgumentFailure.Reason, reversed.ArgumentFailure!.Reason);
            Assert.Equal(scenario.Kind, result.RequestKind);
            Assert.Equal(result.RequestKind, reversed.RequestKind);
            Assert.Equal(3, result.Position);
            Assert.Equal(result.Position, reversed.Position);
        }
    }

    [Fact]
    public void LaterCommonLoweringFailuresSurviveCandidateSpecificErrors()
    {
        CandidateFixture first =
            new("first", extraOptionName: "--only-in-first");
        CandidateFixture second = new("second");
        (string[] Suffix, CliRowSelectionOccurrenceKind Kind, int Position,
            CliRowSelectionFailureReason Reason)[] cases =
        [
            (["--rows", "bad"], CliRowSelectionOccurrenceKind.Rows, 3,
                CliRowSelectionFailureReason.InvalidWindowForm),
            (["--head", "--tail"], CliRowSelectionOccurrenceKind.Tail, 4,
                CliRowSelectionFailureReason.ConflictingDirection),
            (["--rows", "1..2", "--rows", "2..3"], CliRowSelectionOccurrenceKind.Rows, 5,
                CliRowSelectionFailureReason.RepeatedGesture),
            (["--rows", "bad", "--head", "--tail"], CliRowSelectionOccurrenceKind.Rows, 3,
                CliRowSelectionFailureReason.InvalidWindowForm)
        ];
        foreach (var scenario in cases)
        {
            string[] arguments = ["Target", "-n", "--only-in-first", .. scenario.Suffix];
            CliRowSelectionRouteEnvelopeResult result = Evaluate(arguments, first, second);
            CliRowSelectionRouteEnvelopeResult reversed = Evaluate(arguments, second, first);

            Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.LoweringFailure, result.Outcome);
            Assert.Equal(result.Outcome, reversed.Outcome);
            Assert.Equal(scenario.Reason, result.Failure!.Reason);
            Assert.Equal(result.Failure.Reason, reversed.Failure!.Reason);
            Assert.Equal(scenario.Kind, result.RequestKind);
            Assert.Equal(result.RequestKind, reversed.RequestKind);
            Assert.Equal(scenario.Position, result.Position);
            Assert.Equal(result.Position, reversed.Position);
        }
    }

    [Fact]
    public void CandidateSpecificArityDoesNotTruncateLineUnitEvidence()
    {
        CandidateFixture ownsFollowingOption =
            new("owns-option", extraOptionName: "--only-in-first");
        CandidateFixture doesNotOwnFollowingOption =
            new("does-not-own-option");

        foreach (string modifier in new[] { "--lines", "--tail-lines" })
        {
            string[][] inputs =
            [
                ["Target", "-n", "--only-in-first", modifier],
                ["Target", modifier, "-n", "--only-in-first"],
                ["Target", "-n", "2", "--rows", "--only-in-first", modifier]
            ];
            foreach (string[] arguments in inputs)
            {
                CliRowSelectionRouteEnvelopeResult result =
                    Evaluate(arguments, ownsFollowingOption, doesNotOwnFollowingOption);
                CliRowSelectionRouteEnvelopeResult reversed =
                    Evaluate(arguments, doesNotOwnFollowingOption, ownsFollowingOption);

                Assert.Equal(
                    CliRowSelectionRouteEnvelopeOutcome.Deferred,
                    result.Outcome);
                Assert.Equal(result.Outcome, reversed.Outcome);
                Assert.Equal(result.DeferredPositions, reversed.DeferredPositions);
            }

            CliRowSelectionRouteEnvelopeResult determinate =
                Evaluate(
                    ["Target", "-n=2", "--only-in-first", modifier],
                    ownsFollowingOption,
                    doesNotOwnFollowingOption);
            Assert.Equal(
                CliRowSelectionRouteEnvelopeOutcome.Success,
                determinate.Outcome);
        }
    }

    [Fact]
    public void TruncatedOccurrencesCannotEstablishMissingCount()
    {
        (string Token, RowDeclarations Declaration,
            CliRowSelectionOccurrenceKind Kind)[] modifiers =
        [
            ("--lines", RowDeclarations.Lines, CliRowSelectionOccurrenceKind.Lines),
            ("--tail-lines", RowDeclarations.TailLines, CliRowSelectionOccurrenceKind.TailLines),
            ("--head", RowDeclarations.Head, CliRowSelectionOccurrenceKind.Head),
            ("--tail", RowDeclarations.Tail, CliRowSelectionOccurrenceKind.Tail)
        ];
        CandidateFixture ordinary = new("ordinary");
        CandidateFixture withoutCount =
            new("without-count", declarations: RowDeclarations.All & ~RowDeclarations.Limit);

        foreach (var modifier in modifiers)
        {
            CandidateFixture partialChild =
                new(
                    "partial-child",
                    childName: "Target",
                    childDeclarations: modifier.Declaration);
            (CandidateFixture Other, CliRowSelectionRouteEnvelopeOutcome Outcome)[] cases =
            [
                (withoutCount, CliRowSelectionRouteEnvelopeOutcome.ExplicitCommandRequired),
                (partialChild, CliRowSelectionRouteEnvelopeOutcome.Deferred)
            ];
            foreach (var candidate in cases)
            {
                foreach (string[] arguments in new string[][]
                {
                    ["Target", modifier.Token, "-n"],
                    ["Target", modifier.Token, "-n", "2"]
                })
                {
                    CliRowSelectionRouteEnvelopeResult result =
                        Evaluate(arguments, ordinary, candidate.Other);
                    CliRowSelectionRouteEnvelopeResult reversed =
                        Evaluate(arguments, candidate.Other, ordinary);

                    Assert.Equal(candidate.Outcome, result.Outcome);
                    Assert.Equal(result.Outcome, reversed.Outcome);
                    Assert.Equal(result.RequestKind, reversed.RequestKind);
                    Assert.Equal(result.Position, reversed.Position);
                    Assert.Equal(result.DeferredPositions, reversed.DeferredPositions);
                    if (candidate.Outcome
                        == CliRowSelectionRouteEnvelopeOutcome.ExplicitCommandRequired)
                    {
                        Assert.Equal(CliRowSelectionOccurrenceKind.Limit, result.RequestKind);
                        Assert.Equal(2, result.Position);
                    }
                    else
                    {
                        Assert.Equal([0], result.DeferredPositions);
                    }
                }
            }

            CandidateFixture completeChild =
                new(
                    "complete-child",
                    childName: "Target",
                    childDeclarations: modifier.Declaration | RowDeclarations.Limit);
            CliRowSelectionRouteEnvelopeResult commonArity =
                Evaluate(["Target", modifier.Token, "-n"], ordinary, completeChild);
            Assert.Equal(
                CliRowSelectionRouteEnvelopeOutcome.ArgumentFailure,
                commonArity.Outcome);
            Assert.Equal(
                CliRowSelectionArgumentFailureReason.MissingValue,
                commonArity.ArgumentFailure!.Reason);
            Assert.Equal(CliRowSelectionOccurrenceKind.Limit, commonArity.RequestKind);
            Assert.Equal(2, commonArity.Position);

            CliRowSelectionRouteEnvelopeResult realAbsence =
                Evaluate(["Target", modifier.Token], ordinary, withoutCount);
            Assert.Equal(
                CliRowSelectionRouteEnvelopeOutcome.LoweringFailure,
                realAbsence.Outcome);
            Assert.Equal(
                CliRowSelectionFailureReason.ModifierRequiresCount,
                realAbsence.Failure!.Reason);
            Assert.Equal(modifier.Kind, realAbsence.RequestKind);
            Assert.Equal(1, realAbsence.Position);

            CliRowSelectionRouteEnvelopeResult prefixValueFailure =
                Evaluate(
                    ["Target", "--rows", "bad", modifier.Token, "-n"],
                    ordinary,
                    withoutCount);
            Assert.Equal(
                CliRowSelectionRouteEnvelopeOutcome.LoweringFailure,
                prefixValueFailure.Outcome);
            Assert.Equal(
                CliRowSelectionFailureReason.InvalidWindowForm,
                prefixValueFailure.Failure!.Reason);
            Assert.Equal(1, prefixValueFailure.Position);
        }

        CliRowSelectionRouteEnvelopeResult prefixConflict =
            Evaluate(
                ["Target", "--head", "--tail", "-n"],
                ordinary,
                withoutCount);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.LoweringFailure,
            prefixConflict.Outcome);
        Assert.Equal(
            CliRowSelectionFailureReason.ConflictingDirection,
            prefixConflict.Failure!.Reason);
        Assert.Equal(2, prefixConflict.Position);
    }

    [Fact]
    public void UnexpectedChildSelectionCannotEstablishCountAbsence()
    {
        CandidateFixture first =
            new(
                "first",
                parentName: "scope",
                childName: "Target",
                childDeclarations: RowDeclarations.All & ~RowDeclarations.Limit);
        CandidateFixture second =
            new(
                "second",
                childName: "Target",
                childDeclarations: RowDeclarations.All & ~RowDeclarations.Limit);

        foreach (string modifier in new[] { "--lines", "--tail-lines", "--head", "--tail" })
        {
            (string[] Arguments, int ChildPosition)[] cases =
            [
                (["Target", modifier, "-n", "2"], 0),
                (["Target", modifier, "-n"], 0),
                (["Target", modifier, "-n=2"], 0),
                (["Target", modifier, "-2"], 0),
                (["-n", "2", "Target", modifier], 2)
            ];
            foreach (var scenario in cases)
            {
                CliRowSelectionRouteEnvelopeResult result =
                    Evaluate(scenario.Arguments, first, second);
                CliRowSelectionRouteEnvelopeResult reversed =
                    Evaluate(scenario.Arguments, second, first);

                Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.Deferred, result.Outcome);
                Assert.Equal(result.Outcome, reversed.Outcome);
                Assert.Equal([scenario.ChildPosition], result.DeferredPositions);
                Assert.Equal(result.DeferredPositions, reversed.DeferredPositions);
            }

            CliRowSelectionRouteEnvelopeResult commonValueFailure =
                Evaluate(["Target", "--rows", "bad", modifier, "-n", "2"], first, second);
            Assert.Equal(
                CliRowSelectionRouteEnvelopeOutcome.LoweringFailure,
                commonValueFailure.Outcome);
            Assert.Equal(
                CliRowSelectionFailureReason.InvalidWindowForm,
                commonValueFailure.Failure!.Reason);
            Assert.Equal(1, commonValueFailure.Position);

            CliRowSelectionRouteEnvelopeResult commonArityFailure =
                Evaluate(["Target", modifier, "--rows"], first, second);
            Assert.Equal(
                CliRowSelectionRouteEnvelopeOutcome.ArgumentFailure,
                commonArityFailure.Outcome);
            Assert.Equal(
                CliRowSelectionArgumentFailureReason.MissingValue,
                commonArityFailure.ArgumentFailure!.Reason);
            Assert.Equal(CliRowSelectionOccurrenceKind.Rows, commonArityFailure.RequestKind);
            Assert.Equal(2, commonArityFailure.Position);
        }

        CliRowSelectionRouteEnvelopeResult commonConflict =
            Evaluate(["Target", "--head", "--tail", "-n", "2"], first, second);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.LoweringFailure,
            commonConflict.Outcome);
        Assert.Equal(
            CliRowSelectionFailureReason.ConflictingDirection,
            commonConflict.Failure!.Reason);
        Assert.Equal(2, commonConflict.Position);
    }

    [Fact]
    public void InheritedCountScopePreservesMissingCountFailures()
    {
        CandidateFixture first =
            new("first", parentName: "scope", childName: "Target",
                childDeclarations: RowDeclarations.All);
        CandidateFixture second =
            new("second", childName: "Target", childDeclarations: RowDeclarations.All);
        (string Token, RowDeclarations Declaration,
            CliRowSelectionOccurrenceKind Kind)[] modifiers =
        [
            ("--lines", RowDeclarations.Lines, CliRowSelectionOccurrenceKind.Lines),
            ("--tail-lines", RowDeclarations.TailLines, CliRowSelectionOccurrenceKind.TailLines),
            ("--head", RowDeclarations.Head, CliRowSelectionOccurrenceKind.Head),
            ("--tail", RowDeclarations.Tail, CliRowSelectionOccurrenceKind.Tail)
        ];
        foreach (var modifier in modifiers)
        {
            CandidateFixture relevantScope =
                new("relevant-scope", childName: "Target",
                    childDeclarations: RowDeclarations.Limit | modifier.Declaration);
            foreach (CandidateFixture other in new[] { second, relevantScope })
            {
                CliRowSelectionRouteEnvelopeResult result =
                    Evaluate(["Target", modifier.Token], first, other);
                CliRowSelectionRouteEnvelopeResult reversed =
                    Evaluate(["Target", modifier.Token], other, first);

                Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.LoweringFailure, result.Outcome);
                Assert.Equal(result.Outcome, reversed.Outcome);
                Assert.Equal(CliRowSelectionFailureReason.ModifierRequiresCount, result.Failure!.Reason);
                Assert.Equal(result.Failure.Reason, reversed.Failure!.Reason);
                Assert.Equal(modifier.Kind, result.RequestKind);
                Assert.Equal(result.RequestKind, reversed.RequestKind);
                Assert.Equal(1, result.Position);
                Assert.Equal(result.Position, reversed.Position);

                CliRowSelectionRouteEnvelopeResult suppliedCount =
                    Evaluate(["Target", modifier.Token, "-n", "2"], first, other);
                Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.Deferred, suppliedCount.Outcome);
                Assert.Equal([0], suppliedCount.DeferredPositions);
            }
        }
    }

    [Fact]
    public void HiddenLineScopeDefersUnitDependentCapabilityChecks()
    {
        RowDeclarations inherited =
            RowDeclarations.All & ~RowDeclarations.Lines & ~RowDeclarations.TailLines;
        CandidateFixture first =
            new("first", capabilities: CliRowSelectionCapabilities.Lines,
                parentName: "scope", childName: "Target", childDeclarations: inherited);
        CandidateFixture second =
            new("second", capabilities: CliRowSelectionCapabilities.Lines,
                childName: "Target", childDeclarations: inherited);

        foreach (string modifier in new[] { "--lines", "--tail-lines" })
        {
            (string[] Arguments, int ChildPosition)[] cases =
            [
                (["Target", modifier, "-n", "2"], 0),
                (["Target", "-n", "2", modifier], 0),
                (["Target", modifier, "-n", "2", "--head"], 0),
                (["Target", modifier, "-n", "2", "--tail"], 0),
                (["Target", "-2", modifier], 0),
                (["-n", "2", "Target", modifier], 2)
            ];
            foreach (var scenario in cases)
            {
                CliRowSelectionRouteEnvelopeResult result =
                    Evaluate(scenario.Arguments, first, second);
                CliRowSelectionRouteEnvelopeResult reversed =
                    Evaluate(scenario.Arguments, second, first);

                Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.Deferred, result.Outcome);
                Assert.Equal(result.Outcome, reversed.Outcome);
                Assert.Equal([scenario.ChildPosition], result.DeferredPositions);
                Assert.Equal(result.DeferredPositions, reversed.DeferredPositions);
            }

            CliRowSelectionRouteEnvelopeResult commonValueFailure =
                Evaluate(["Target", modifier, "--rows", "bad", "-n", "2"], first, second);
            Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.LoweringFailure, commonValueFailure.Outcome);
            Assert.Equal(CliRowSelectionFailureReason.InvalidWindowForm, commonValueFailure.Failure!.Reason);
            Assert.Equal(2, commonValueFailure.Position);

            CandidateFixture unsupportedFirst =
                new("unsupported-first", capabilities: CliRowSelectionCapabilities.HeadTail,
                    childName: "Target", childDeclarations: inherited);
            CandidateFixture unsupportedSecond =
                new("unsupported-second", capabilities: CliRowSelectionCapabilities.HeadTail,
                    childName: "Target", childDeclarations: inherited);
            CliRowSelectionRouteEnvelopeResult commonUnsupported =
                Evaluate(["Target", modifier, "-n", "2"], unsupportedFirst, unsupportedSecond);
            Assert.Equal(
                CliRowSelectionRouteEnvelopeOutcome.UnsupportedCapability,
                commonUnsupported.Outcome);
            Assert.Equal(CliRowSelectionCapabilities.Lines, commonUnsupported.Failure!.MissingCapabilities);
            Assert.Equal(1, commonUnsupported.Position);

            CandidateFixture ordinaryFirst =
                new("ordinary-first", capabilities: CliRowSelectionCapabilities.Lines);
            CandidateFixture ordinarySecond =
                new("ordinary-second", capabilities: CliRowSelectionCapabilities.Lines);
            CliRowSelectionRouteEnvelopeResult ordinary =
                Evaluate(["Target", modifier, "-n", "2"], ordinaryFirst, ordinarySecond);
            Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.Success, ordinary.Outcome);
        }

        CliRowSelectionRouteEnvelopeResult semanticUnsupported =
            Evaluate(["Target", "-n", "2"], first, second);
        Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.UnsupportedCapability, semanticUnsupported.Outcome);
        Assert.Equal(CliRowSelectionCapabilities.HeadTail, semanticUnsupported.Failure!.MissingCapabilities);
        Assert.Equal(CliRowSelectionOccurrenceKind.Limit, semanticUnsupported.RequestKind);
        Assert.Equal(1, semanticUnsupported.Position);
    }

    [Fact]
    public void ChildShadowedRowOptionsKeepTheirParsedOwner()
    {
        string[] aliases =
            ["-n", "--rows", "--top", "--order-by", "--head", "--tail", "--lines", "--tail-lines"];
        foreach (string alias in aliases)
        {
            CandidateFixture first =
                new("first", capabilities: CliRowSelectionCapabilities.None,
                    parentName: "scope", childName: "Target",
                    childDeclarations: RowDeclarations.All, childOptionName: alias);
            CandidateFixture second =
                new("second", capabilities: CliRowSelectionCapabilities.None,
                    childName: "Target", childDeclarations: RowDeclarations.All,
                    childOptionName: alias);
            (string[] Arguments, bool HasValue)[] cases =
            [
                (["Target", alias, "payload"], true),
                (["Target", $"{alias}=payload"], true),
                (["Target", $"{alias}:payload"], true),
                (["Target", alias], false)
            ];
            foreach (var scenario in cases)
            {
                CliRowSelectionRouteEnvelopeResult result =
                    Evaluate(scenario.Arguments, first, second);
                CliRowSelectionRouteEnvelopeResult reversed =
                    Evaluate(scenario.Arguments, second, first);
                Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.Deferred, result.Outcome);
                Assert.Equal(result.Outcome, reversed.Outcome);
                Assert.Equal([0], result.DeferredPositions);
                Assert.Equal(result.DeferredPositions, reversed.DeferredPositions);

                foreach (CandidateFixture fixture in new[] { first, second })
                {
                    string[] explicitArguments =
                        [.. fixture.Candidate.CommandPrefix, .. scenario.Arguments];
                    CliRowSelectionArgumentResult inspected =
                        CliRowSelectionArgumentAdapter.InspectExplicit(
                            fixture.Candidate.ParserRoot, explicitArguments, fixture.Candidate.Bindings);
                    Assert.Empty(inspected.Occurrences);
                    Assert.Empty(inspected.ArgumentFailures);

                    CliRowSelectionArgumentResult lowered =
                        CliRowSelectionArgumentAdapter.LowerExplicit(
                            fixture.Candidate.ParserRoot, explicitArguments, fixture.Candidate.Bindings,
                            CliRowSelectionCapabilities.All);
                    Assert.Empty(lowered.Occurrences);
                    Assert.Empty(lowered.ArgumentFailures);
                    if (scenario.HasValue)
                    {
                        Assert.Empty(inspected.ParseErrors);
                        Assert.Equal("payload", inspected.ParseResult.GetValue(fixture.ChildOption!));
                        Assert.True(lowered.LoweringResult!.IsSuccess);
                    }
                    else
                    {
                        Assert.NotEmpty(inspected.ParseErrors);
                        Assert.True(lowered.HasParseErrors);
                        Assert.Null(lowered.LoweringResult);
                    }
                }
            }

            if (alias == "-n")
            {
                foreach (string shorthand in new[] { "-2", "-n2" })
                {
                    string[] arguments = ["Target", shorthand];
                    CliRowSelectionRouteEnvelopeResult result = Evaluate(arguments, first, second);
                    Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.Deferred, result.Outcome);
                    Assert.Equal([0], result.DeferredPositions);
                    string[] explicitArguments = [.. first.Candidate.CommandPrefix, .. arguments];
                    CliRowSelectionArgumentResult inspected =
                        CliRowSelectionArgumentAdapter.InspectExplicit(
                            first.Candidate.ParserRoot, explicitArguments, first.Candidate.Bindings);
                    Assert.Equal(explicitArguments, inspected.Arguments);
                    Assert.Empty(inspected.Occurrences);
                }
            }
        }
    }

    [Fact]
    public void ChildShadowedLinesCannotCreateCountAbsenceOrCapabilities()
    {
        foreach (string modifier in new[] { "--lines", "--tail-lines" })
        {
            CandidateFixture first =
                new("first", capabilities: CliRowSelectionCapabilities.Lines,
                    parentName: "scope", childName: "Target",
                    childDeclarations: RowDeclarations.All, childOptionName: modifier);
            CandidateFixture second =
                new("second", capabilities: CliRowSelectionCapabilities.Lines,
                    childName: "Target", childDeclarations: RowDeclarations.All,
                    childOptionName: modifier);
            (string[] Arguments, int ChildPosition)[] cases =
            [
                (["Target", modifier, "-n2"], 0),
                (["Target", modifier, "-2"], 0),
                (["Target", modifier, "payload", "-n2"], 0),
                (["Target", $"{modifier}=payload", "-n2"], 0),
                ([modifier, "-n", "2", "Target", modifier, "payload"], 3)
            ];
            foreach (var scenario in cases)
            {
                CliRowSelectionRouteEnvelopeResult result =
                    Evaluate(scenario.Arguments, first, second);
                CliRowSelectionRouteEnvelopeResult reversed =
                    Evaluate(scenario.Arguments, second, first);
                Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.Deferred, result.Outcome);
                Assert.Equal(result.Outcome, reversed.Outcome);
                Assert.Equal([scenario.ChildPosition], result.DeferredPositions);
                Assert.Equal(result.DeferredPositions, reversed.DeferredPositions);
            }

            CandidateFixture unshadowed =
                new("unshadowed", capabilities: CliRowSelectionCapabilities.Lines,
                    childName: "Target", childDeclarations: RowDeclarations.All);
            CliRowSelectionRouteEnvelopeResult neighbor =
                Evaluate(["Target", modifier, "-n2"], unshadowed, unshadowed);
            Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.Deferred, neighbor.Outcome);
            Assert.Equal([0], neighbor.DeferredPositions);
        }
    }

    [Fact]
    public void ChildShadowedCountCannotEstablishAbsence()
    {
        CandidateFixture first =
            new("first", childName: "Target", childDeclarations: RowDeclarations.All,
                childOptionName: "-n");
        CandidateFixture second =
            new("second", parentName: "scope", childName: "Target",
                childDeclarations: RowDeclarations.All, childOptionName: "-n");
        foreach (string modifier in new[] { "--lines", "--tail-lines", "--head", "--tail" })
        {
            (string[] Arguments, int ChildPosition)[] cases =
            [
                (["Target", modifier], 0),
                (["Target", modifier, "-n", "payload"], 0),
                (["-n", "2", "Target", modifier, "-n", "payload"], 2),
                (["-2", "Target", modifier, "-n", "payload"], 1),
                (["-n2", "Target", modifier, "-n", "payload"], 1)
            ];
            foreach (var scenario in cases)
            {
                CliRowSelectionRouteEnvelopeResult result =
                    Evaluate(scenario.Arguments, first, second);
                CliRowSelectionRouteEnvelopeResult reversed =
                    Evaluate(scenario.Arguments, second, first);
                Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.Deferred, result.Outcome);
                Assert.Equal(result.Outcome, reversed.Outcome);
                Assert.Equal([scenario.ChildPosition], result.DeferredPositions);
                Assert.Equal(result.DeferredPositions, reversed.DeferredPositions);
            }
        }
    }

    [Fact]
    public void ChildScopeOptionRecognitionDefersRowArguments()
    {
        foreach (string valueAlias in new[] { "-n", "--rows", "--top", "--order-by" })
        {
            foreach (bool childOnly in new[] { false, true })
            {
                CandidateFixture first =
                    new("first", parentName: "scope", childName: "Target",
                        childDeclarations: RowDeclarations.All,
                        extraOptionName: childOnly ? null : "--other",
                        childOptionName: childOnly ? "--other" : null);
                CandidateFixture second =
                    new("second", childName: "Target", childDeclarations: RowDeclarations.All,
                        extraOptionName: childOnly ? null : "--other",
                        childOptionName: childOnly ? "--other" : null);
                foreach (string following in new[] { "--other", "--other=payload", "--other:payload" })
                {
                    CliRowSelectionRouteEnvelopeResult result =
                        Evaluate(["Target", valueAlias, following], first, second);
                    CliRowSelectionRouteEnvelopeResult reversed =
                        Evaluate(["Target", valueAlias, following], second, first);
                    Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.Deferred, result.Outcome);
                    Assert.Equal(result.Outcome, reversed.Outcome);
                    Assert.Equal([0], result.DeferredPositions);
                    Assert.Equal(result.DeferredPositions, reversed.DeferredPositions);
                }

                CliRowSelectionRouteEnvelopeResult laterValue =
                    Evaluate(["Target", valueAlias, "--other", "--rows", "bad"], first, second);
                Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.LoweringFailure, laterValue.Outcome);
                Assert.Equal(CliRowSelectionFailureReason.InvalidWindowForm, laterValue.Failure!.Reason);
                Assert.Equal(3, laterValue.Position);

                CliRowSelectionRouteEnvelopeResult laterArity =
                    Evaluate(["Target", valueAlias, "--other", "--rows"], first, second);
                Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.ArgumentFailure, laterArity.Outcome);
                Assert.Equal(CliRowSelectionArgumentFailureReason.MissingValue, laterArity.ArgumentFailure!.Reason);
                Assert.Equal(CliRowSelectionOccurrenceKind.Rows, laterArity.RequestKind);
                Assert.Equal(3, laterArity.Position);

                CliRowSelectionRouteEnvelopeResult attached =
                    Evaluate(["Target", $"{valueAlias}=--other"], first, second);
                if (valueAlias == "--order-by")
                {
                    Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.Deferred, attached.Outcome);
                }
                else
                {
                    Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.LoweringFailure, attached.Outcome);
                    Assert.Equal(
                        valueAlias == "--rows"
                            ? CliRowSelectionFailureReason.InvalidWindowForm
                            : CliRowSelectionFailureReason.MalformedValue,
                        attached.Failure!.Reason);
                    Assert.Equal(1, attached.Position);
                }
            }
        }
    }

    [Fact]
    public void RowArityUsesTheTokenScope()
    {
        CandidateFixture first =
            new("first", childName: "Target", childDeclarations: RowDeclarations.All,
                extraOptionName: "--other");
        CandidateFixture second =
            new("second", childName: "Target", childDeclarations: RowDeclarations.All,
                extraOptionName: "--other");

        CliRowSelectionRouteEnvelopeResult absence =
            Evaluate(["Target", "--head", "-n", "--other"], first, second);
        Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.Deferred, absence.Outcome);
        Assert.Equal([0], absence.DeferredPositions);

        CliRowSelectionRouteEnvelopeResult prefix =
            Evaluate(["-n", "--other", "Target"], first, second);
        Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.ArgumentFailure, prefix.Outcome);
        Assert.Equal(CliRowSelectionArgumentFailureReason.MissingValue, prefix.ArgumentFailure!.Reason);
        Assert.Equal(0, prefix.Position);

        CliRowSelectionArgumentResult explicitChild =
            CliRowSelectionArgumentAdapter.LowerExplicit(
                first.Candidate.ParserRoot,
                [.. first.Candidate.CommandPrefix, "Target", "-n", "--other"],
                first.Candidate.Bindings,
                CliRowSelectionCapabilities.All);
        Assert.Empty(explicitChild.ArgumentFailures);
        Assert.Empty(explicitChild.ParseErrors);
        Assert.Equal(CliRowSelectionFailureReason.MalformedValue, explicitChild.LoweringResult!.Failure!.Reason);

        CandidateFixture recursiveFirst =
            new("recursive-first", childName: "Target", childDeclarations: RowDeclarations.All,
                extraOptionName: "--other", extraOptionRecursive: true);
        CandidateFixture recursiveSecond =
            new("recursive-second", childName: "Target", childDeclarations: RowDeclarations.All,
                extraOptionName: "--other", extraOptionRecursive: true);
        CliRowSelectionRouteEnvelopeResult commonArity =
            Evaluate(["Target", "-n", "--other"], recursiveFirst, recursiveSecond);
        Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.ArgumentFailure, commonArity.Outcome);
        Assert.Equal(CliRowSelectionArgumentFailureReason.MissingValue, commonArity.ArgumentFailure!.Reason);
        Assert.Equal(1, commonArity.Position);

        CliRowSelectionRouteEnvelopeResult commonValue =
            Evaluate(["Target", "-n", "--unknown"], first, second);
        Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.LoweringFailure, commonValue.Outcome);
        Assert.Equal(CliRowSelectionFailureReason.MalformedValue, commonValue.Failure!.Reason);
        Assert.Equal(1, commonValue.Position);
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
    public void MissingCanonicalAliasesRemainExplicitRequests()
    {
        static CandidateFixture Renamed(string name) =>
            new(
                name,
                limitName: "--limit",
                rowsName: "--window",
                topName: "--rank",
                orderByName: "--sort",
                headName: "--first",
                tailName: "--last",
                linesName: "--text",
                tailLinesName: "--last-text");

        CandidateFixture first = Renamed("first");
        CandidateFixture second = Renamed("second");
        CandidateFixture canonical = new("canonical");
        (string Token, string? Value, CliRowSelectionOccurrenceKind Kind)[] options =
        [
            ("-n", "2", CliRowSelectionOccurrenceKind.Limit),
            ("--rows", "1..2", CliRowSelectionOccurrenceKind.Rows),
            ("--top", "2", CliRowSelectionOccurrenceKind.Top),
            ("--order-by", "name", CliRowSelectionOccurrenceKind.OrderBy),
            ("--head", null, CliRowSelectionOccurrenceKind.Head),
            ("--tail", null, CliRowSelectionOccurrenceKind.Tail),
            ("--lines", null, CliRowSelectionOccurrenceKind.Lines),
            ("--tail-lines", null, CliRowSelectionOccurrenceKind.TailLines)
        ];
        foreach (var option in options)
        {
            string[][] forms = option.Value is { } value
                ?
                [
                    ["Target", option.Token, value],
                    ["Target", $"{option.Token}={value}"],
                    ["Target", $"{option.Token}:{value}"]
                ]
                :
                [
                    ["Target", option.Token],
                    ["Target", $"{option.Token}=true"],
                    ["Target", $"{option.Token}:true"]
                ];
            foreach (string[] arguments in forms)
            {
                CliRowSelectionRouteEnvelopeResult unsupported =
                    Evaluate(arguments, first, second);
                Assert.Equal(
                    CliRowSelectionRouteEnvelopeOutcome.UnsupportedCapability,
                    unsupported.Outcome);
                Assert.Equal(option.Kind, unsupported.RequestKind);
                Assert.Equal(1, unsupported.Position);

                CliRowSelectionRouteEnvelopeResult mixed =
                    Evaluate(arguments, canonical, first);
                CliRowSelectionRouteEnvelopeResult reversed =
                    Evaluate(arguments, first, canonical);
                Assert.Equal(
                    CliRowSelectionRouteEnvelopeOutcome.ExplicitCommandRequired,
                    mixed.Outcome);
                Assert.Equal(mixed.Outcome, reversed.Outcome);
                Assert.Equal(option.Kind, mixed.RequestKind);
                Assert.Equal(mixed.RequestKind, reversed.RequestKind);
                Assert.Equal(1, mixed.Position);
                Assert.Equal(mixed.Position, reversed.Position);
            }
        }

        CliRowSelectionRouteEnvelopeResult compact =
            Evaluate(["Target", "-n2"], first, second);
        Assert.Equal(CliRowSelectionRouteEnvelopeOutcome.UnsupportedCapability, compact.Outcome);
        Assert.Equal(CliRowSelectionOccurrenceKind.Limit, compact.RequestKind);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.NoRequest,
            Evaluate(["Target", "-2"], first, second).Outcome);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.NoRequest,
            Evaluate(["Target", "--", "-n", "2"], first, second).Outcome);
        Assert.Equal(
            CliRowSelectionRouteEnvelopeOutcome.Success,
            Evaluate(["Target", "--limit", "2", "--window", "1..2"], first, second).Outcome);
    }

    [Fact]
    public void CanonicalValueSpellingsRequireMatchingBindings()
    {
        (string Canonical, string Renamed, string Value,
            CliRowSelectionOccurrenceKind Kind)[] options =
        [
            ("--top", "--rank", "2", CliRowSelectionOccurrenceKind.Top),
            ("--order-by", "--sort", "name", CliRowSelectionOccurrenceKind.OrderBy)
        ];
        foreach (var option in options)
        {
            string[][] inputs =
            [
                ["Target", option.Canonical, option.Value],
                ["Target", $"{option.Canonical}={option.Value}"],
                ["Target", $"{option.Canonical}:{option.Value}"]
            ];
            foreach (string[] arguments in inputs)
            {
                CandidateFixture canonical = new("canonical");
                CandidateFixture renamed =
                    new("renamed", topName: "--rank", orderByName: "--sort");
                CandidateFixture alsoRenamed =
                    new("also-renamed", topName: "--rank", orderByName: "--sort");

                CliRowSelectionRouteEnvelopeResult result =
                    Evaluate(arguments, canonical, renamed);
                CliRowSelectionRouteEnvelopeResult reversed =
                    Evaluate(arguments, renamed, canonical);
                Assert.Equal(
                    CliRowSelectionRouteEnvelopeOutcome.ExplicitCommandRequired,
                    result.Outcome);
                Assert.Equal(result.Outcome, reversed.Outcome);
                Assert.Equal(option.Kind, result.RequestKind);
                Assert.Equal(result.RequestKind, reversed.RequestKind);
                Assert.Equal(1, result.Position);
                Assert.Equal(result.Position, reversed.Position);

                CliRowSelectionRouteEnvelopeResult unsupported =
                    Evaluate(arguments, renamed, alsoRenamed);
                Assert.Equal(
                    CliRowSelectionRouteEnvelopeOutcome.UnsupportedCapability,
                    unsupported.Outcome);
                Assert.Equal(option.Kind, unsupported.RequestKind);

                CliRowSelectionRouteEnvelopeResult custom =
                    Evaluate(
                        ["Target", option.Renamed, option.Value],
                        renamed,
                        alsoRenamed);
                Assert.Equal(
                    CliRowSelectionRouteEnvelopeOutcome.Success,
                    custom.Outcome);

                Option binding = option.Kind == CliRowSelectionOccurrenceKind.Top
                    ? renamed.Candidate.Bindings.Top!
                    : renamed.Candidate.Bindings.OrderBy!;
                binding.Aliases.Add(option.Canonical);
                CliRowSelectionRouteEnvelopeResult aliased =
                    Evaluate(arguments, canonical, renamed);
                Assert.Equal(
                    CliRowSelectionRouteEnvelopeOutcome.Success,
                    aliased.Outcome);
            }
        }
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
            string rowsName = "--rows",
            string topName = "--top",
            string orderByName = "--order-by",
            string limitName = "-n",
            string headName = "--head",
            string tailName = "--tail",
            string linesName = "--lines",
            string tailLinesName = "--tail-lines",
            string? childOptionName = null,
            bool extraOptionRecursive = false)
        {
            Option<string[]> limit =
                RowValueOption(limitName);
            Option<string[]> rows =
                RowValueOption(rowsName);
            Option<string[]>? top =
                omitTopOrderBindings
                    ? null
                    : RowValueOption(topName);
            Option<string[]>? orderBy =
                omitTopOrderBindings
                    ? null
                    : RowValueOption(orderByName);
            Option<bool> head =
                ModifierOption(headName);
            Option<bool> tail =
                ModifierOption(tailName);
            Option<bool> lines =
                ModifierOption(linesName);
            Option<bool> tailLines =
                ModifierOption(tailLinesName);
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
                Option<bool> extraOption = ModifierOption(extraOptionName);
                extraOption.Recursive = extraOptionRecursive;
                command.Options.Add(extraOption);
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
                if (childOptionName is not null)
                {
                    ChildOption = new Option<string?>(childOptionName)
                    {
                        Arity = ArgumentArity.ExactlyOne
                    };
                    child.Options.Add(ChildOption);
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

        public Option<string?>? ChildOption { get; }

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
