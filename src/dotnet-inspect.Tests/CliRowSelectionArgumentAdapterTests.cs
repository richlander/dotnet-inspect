using System.CommandLine;
using DotnetInspector.CommandLine;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

public sealed class CliRowSelectionArgumentAdapterTests
{
    [Fact]
    public void CliRowSelectionExplicitTokenOwnershipTests()
    {
        Fixture fixture = new();

        CliRowSelectionArgumentResult required =
            fixture.Lower(
                [
                    "demo",
                    "--required",
                    "-5"
                ]);
        Assert.Equal(
            [
                "demo",
                "--required",
                "-5"
            ],
            required.Arguments);
        Assert.Empty(required.Occurrences);
        Assert.Equal(
            "-5",
            required.ParseResult.GetValue(
                fixture.Required));

        CliRowSelectionArgumentResult optional =
            fixture.Success(
                [
                    "demo",
                    "--optional",
                    "-5"
                ]);
        Assert.Equal(
            [
                "demo",
                "--optional",
                "-n",
                "5"
            ],
            optional.Arguments);
        Assert.Null(
            optional.ParseResult.GetValue(
                fixture.Optional));
        Assert.Equal(
            5,
            Assert.Single(
                optional.LoweringResult!.Value!
                    .SemanticIntent.Operations)
                .Count);

        CliRowSelectionArgumentResult flag =
            fixture.Success(
                [
                    "demo",
                    "--flag",
                    "-5"
                ]);
        Assert.Equal(
            [
                "demo",
                "--flag",
                "-n",
                "5"
            ],
            flag.Arguments);

        CliRowSelectionArgumentResult inlineRequired =
            fixture.Lower(
                [
                    "demo",
                    "--required=-5"
                ]);
        Assert.Equal(
            [
                "demo",
                "--required=-5"
            ],
            inlineRequired.Arguments);
        Assert.Empty(inlineRequired.Occurrences);

        CliRowSelectionArgumentResult inlineOptional =
            fixture.Lower(
                [
                    "demo",
                    "--optional=-5"
                ]);
        Assert.Equal(
            [
                "demo",
                "--optional=-5"
            ],
            inlineOptional.Arguments);
        Assert.Empty(inlineOptional.Occurrences);

        CliRowSelectionArgumentResult repeatedValue =
            fixture.Success(
                [
                    "demo",
                    "--required=-5",
                    "-5"
                ]);
        Assert.Equal(
            [
                "demo",
                "--required=-5",
                "-n",
                "5"
            ],
            repeatedValue.Arguments);

        CliRowSelectionArgumentResult parentRequired =
            fixture.Success(
                [
                    "--root-required",
                    "-5",
                    "demo",
                    "-5"
                ]);
        Assert.Equal(
            [
                "--root-required",
                "-5",
                "demo",
                "-n",
                "5"
            ],
            parentRequired.Arguments);
        Assert.Equal(
            "-5",
            parentRequired.ParseResult.GetValue(
                fixture.RootRequired));

        CliRowSelectionArgumentResult recursiveParent =
            fixture.Success(
                [
                    "demo",
                    "--root-required",
                    "-5",
                    "-5"
                ]);
        Assert.Equal(
            [
                "demo",
                "--root-required",
                "-5",
                "-n",
                "5"
            ],
            recursiveParent.Arguments);

        CliRowSelectionArgumentResult requiredTail =
            fixture.Success(
                [
                    "demo",
                    "--required",
                    "--tail",
                    "-n",
                    "2"
                ]);
        Assert.Equal(
            RowSelectionStageKind.Head,
            Assert.Single(
                requiredTail.LoweringResult!.Value!
                    .SemanticIntent.Operations)
                .Kind);
        Assert.DoesNotContain(
            requiredTail.Occurrences,
            occurrence =>
                occurrence.Kind
                    == CliRowSelectionOccurrenceKind.Tail);

        CliRowSelectionArgumentResult requiredLines =
            fixture.Success(
                [
                    "demo",
                    "--required",
                    "--lines"
                ]);
        Assert.Empty(
            requiredLines.Occurrences);

        CliRowSelectionArgumentResult requiredRows =
            fixture.Success(
                [
                    "demo",
                    "--required",
                    "--rows",
                    "1..2"
                ]);
        Assert.Empty(
            requiredRows.Occurrences);
        Assert.Equal(
            ["1..2"],
            Assert.IsType<string[]>(
                requiredRows.ParseResult.GetValue(
                    fixture.Positionals)));

        CliRowSelectionArgumentResult requiredTop =
            fixture.Success(
                [
                    "demo",
                    "--required",
                    "--top",
                    "3"
                ]);
        Assert.Empty(
            requiredTop.Occurrences);

        CliRowSelectionArgumentResult requiredLimit =
            fixture.Success(
                [
                    "demo",
                    "--required",
                    "-n",
                    "2"
                ]);
        Assert.Empty(
            requiredLimit.Occurrences);

        CliRowSelectionArgumentResult compactRequired =
            fixture.Success(
                [
                    "demo",
                    "--required",
                    "-n2",
                    "-n",
                    "3"
                ]);
        Assert.Equal(
            "-n2",
            compactRequired.ParseResult.GetValue(
                fixture.Required));
        CliRowSelectionOccurrence<string> compactLimit =
            Assert.Single(
                compactRequired.Occurrences);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Limit,
            compactLimit.Kind);
        Assert.Equal(3, compactLimit.Position);
        Assert.Equal("3", compactLimit.Value);

        CliRowSelectionArgumentResult prefixedOption =
            fixture.Success(
                [
                    "demo",
                    "-nologo",
                    "-n",
                    "2"
                ]);
        CliRowSelectionOccurrence<string> prefixedLimit =
            Assert.Single(
                prefixedOption.Occurrences);
        Assert.Equal(2, prefixedLimit.Position);
        Assert.Equal("2", prefixedLimit.Value);

        string responseFile =
            Path.GetTempFileName();
        try
        {
            File.WriteAllLines(
                responseFile,
                ["--head"]);
            CliRowSelectionArgumentResult literalResponseFile =
                fixture.Success(
                    [
                        "demo",
                        $"@{responseFile}",
                        "-n",
                        "4"
                    ]);
            Assert.Equal(
                [$"@{responseFile}"],
                Assert.IsType<string[]>(
                    literalResponseFile.ParseResult.GetValue(
                        fixture.Positionals)));
            CliRowSelectionOccurrence<string> responseLimit =
                Assert.Single(
                    literalResponseFile.Occurrences);
            Assert.Equal(2, responseLimit.Position);
            Assert.Equal("4", responseLimit.Value);
        }
        finally
        {
            File.Delete(responseFile);
        }

        CliRowSelectionArgumentResult requiredAttachedModifier =
            fixture.Lower(
                [
                    "demo",
                    "--required",
                    "--head=true"
                ]);
        Assert.Empty(
            requiredAttachedModifier.Occurrences);
        Assert.NotNull(
            requiredAttachedModifier.ArgumentFailure);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Head,
            requiredAttachedModifier.ArgumentFailure
                .OccurrenceKind);
        Assert.True(
            requiredAttachedModifier.HasParseErrors);

        CliRowSelectionArgumentResult positional =
            fixture.Lower(
                [
                    "demo",
                    "--",
                    "-5"
                ]);
        Assert.Equal(
            [
                "demo",
                "--",
                "-5"
            ],
            positional.Arguments);
        Assert.Empty(positional.Occurrences);
    }

    [Fact]
    public void CliRowSelectionExplicitBareShorthandTests()
    {
        Fixture fixture = new();

        CliRowSelectionArgumentResult ordinary =
            fixture.Success(
                [
                    "demo",
                    "-5"
                ]);
        Assert.Equal(
            [
                "demo",
                "-n",
                "5"
            ],
            ordinary.Arguments);
        Assert.Equal(
            RowSelectionStageKind.Head,
            Assert.Single(
                ordinary.LoweringResult!.Value!
                    .SemanticIntent.Operations)
                .Kind);

        CliRowSelectionFailure zero =
            fixture.LoweringFailure(
                [
                    "demo",
                    "-0"
                ]);
        Assert.Equal(
            CliRowSelectionFailureReason.NonPositiveValue,
            zero.Reason);
        Assert.Equal(1, zero.Position);

        CliRowSelectionFailure overflow =
            fixture.LoweringFailure(
                [
                    "demo",
                    "-99999999999999999999"
                ]);
        Assert.Equal(
            CliRowSelectionFailureReason.OverflowValue,
            overflow.Reason);
        Assert.Equal(1, overflow.Position);

        CliRowSelectionArgumentResult nonAscii =
            fixture.Lower(
                [
                    "demo",
                    "-١"
                ]);
        Assert.Equal(
            [
                "demo",
                "-١"
            ],
            nonAscii.Arguments);
        Assert.Empty(nonAscii.Occurrences);

        CliRowSelectionArgumentResult nonAsciiCompact =
            fixture.Lower(
                [
                    "demo",
                    "-n١"
                ]);
        Assert.Equal(
            [
                "demo",
                "-n١"
            ],
            nonAsciiCompact.Arguments);
        Assert.Empty(nonAsciiCompact.Occurrences);

        Fixture ancestorMany =
            new(
                includeParent: true,
                parentAllowsMany: true);
        CliRowSelectionArgumentResult parentBare =
            ancestorMany.Success(
                [
                    "outer",
                    "-5",
                    "demo"
                ]);
        Assert.Equal(
            [
                "outer",
                "-5",
                "demo"
            ],
            parentBare.Arguments);
        Assert.Equal(
            ["-5"],
            Assert.IsType<string[]>(
                parentBare.ParseResult.GetValue(
                    ancestorMany.ParentPositionals)));
        Assert.Empty(parentBare.Occurrences);

        Fixture ancestorOne =
            new(includeParent: true);
        CliRowSelectionArgumentResult parentCompact =
            ancestorOne.Success(
                [
                    "outer",
                    "-n5",
                    "demo"
                ]);
        Assert.Equal(
            [
                "outer",
                "-n5",
                "demo"
            ],
            parentCompact.Arguments);
        Assert.Equal(
            "-n5",
            parentCompact.ParseResult.GetValue(
                ancestorOne.ParentPositional));
        Assert.Empty(parentCompact.Occurrences);

        CliRowSelectionArgumentResult beforeCommand =
            fixture.Lower(
                [
                    "-5",
                    "demo"
                ]);
        Assert.Equal(
            [
                "-5",
                "demo"
            ],
            beforeCommand.Arguments);
        Assert.True(beforeCommand.HasParseErrors);
        Assert.Empty(beforeCommand.Occurrences);
        Assert.Null(beforeCommand.LoweringResult);

        Fixture rootOwned =
            new(includeRootLimit: true);
        CliRowSelectionArgumentResult beforeChild =
            rootOwned.Success(
                [
                    "-5",
                    "demo"
                ]);
        Assert.Equal(
            [
                "-n",
                "5",
                "demo"
            ],
            beforeChild.Arguments);
        Assert.Equal(
            0,
            Assert.Single(beforeChild.Occurrences)
                .Position);

        CliRowSelectionArgumentResult afterChild =
            rootOwned.Success(
                [
                    "demo",
                    "-5"
                ]);
        Assert.Equal(
            1,
            Assert.Single(afterChild.Occurrences)
                .Position);

        Fixture withoutShorthand =
            new(limitName: "--limit");
        CliRowSelectionArgumentResult unavailable =
            withoutShorthand.Lower(
                [
                    "demo",
                    "-5"
                ]);
        Assert.Equal(
            [
                "demo",
                "-5"
            ],
            unavailable.Arguments);
        Assert.Empty(
            unavailable.Occurrences);

        Fixture aliasedShorthand =
            new(
                limitName: "--limit",
                limitAlias: "-n");
        CliRowSelectionArgumentResult aliased =
            aliasedShorthand.Success(
                [
                    "demo",
                    "-5"
                ]);
        Assert.Equal(
            [
                "demo",
                "-n",
                "5"
            ],
            aliased.Arguments);

        CliRowSelectionFailure repeated =
            fixture.LoweringFailure(
                [
                    "demo",
                    "-5",
                    "-5"
                ]);
        CliRowSelectionArgumentResult repeatedResult =
            fixture.Lower(
                [
                    "demo",
                    "-5",
                    "-5"
                ]);
        Assert.Empty(
            repeatedResult.ParseResult.Errors);
        Assert.Empty(
            repeatedResult.ParseErrors);
        Assert.Equal(
            CliRowSelectionFailureReason.RepeatedGesture,
            repeated.Reason);
        Assert.Equal(2, repeated.Position);

        CliRowSelectionArgumentResult protectedSuffix =
            fixture.Success(
                [
                    "demo",
                    "-51",
                    "--required",
                    "-5"
                ]);
        Assert.Equal(
            "-5",
            protectedSuffix.ParseResult.GetValue(
                fixture.Required));
        CliRowSelectionOccurrence<string> protectedLimit =
            Assert.Single(
                protectedSuffix.Occurrences);
        Assert.Equal(1, protectedLimit.Position);
        Assert.Equal("51", protectedLimit.Value);
    }

    [Fact]
    public void CliRowSelectionExplicitOccurrencePositionTests()
    {
        Fixture fixture = new();

        CliRowSelectionArgumentResult result =
            fixture.Success(
                [
                    "demo",
                    "--rows=3..6",
                    "-n2",
                    "--lines",
                    "--top:4",
                    "--order-by",
                    "confidence"
                ]);

        Assert.Equal(
            [
                CliRowSelectionOccurrenceKind.Rows,
                CliRowSelectionOccurrenceKind.Limit,
                CliRowSelectionOccurrenceKind.Lines,
                CliRowSelectionOccurrenceKind.Top,
                CliRowSelectionOccurrenceKind.OrderBy
            ],
            result.Occurrences.Select(
                occurrence =>
                    occurrence.Kind));
        Assert.Equal(
            [
                1,
                2,
                3,
                4,
                5
            ],
            result.Occurrences.Select(
                occurrence =>
                    occurrence.Position));
        Assert.Equal(
            "3..6",
            result.Occurrences[0].Value);
        Assert.Equal(
            "2",
            result.Occurrences[1].Value);
        Assert.Equal(
            "4",
            result.Occurrences[3].Value);
        Assert.Equal(
            "confidence",
            result.Occurrences[4].OrderOperand);

        Assert.Equal(
            [
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Top
            ],
            result.LoweringResult!.Value!
                .SemanticIntent.Operations.Select(
                    operation =>
                        operation.Kind));
        Assert.Equal(
            "confidence",
            result.LoweringResult.Value
                .SemanticIntent.Operations[1]
                .RankingOrderOperand);
        Assert.NotNull(
            result.LoweringResult.Value.LineIntent);
        Assert.Equal(
            2,
            result.LoweringResult.Value.LineIntent.Count);
    }

    [Fact]
    public void CliRowSelectionExplicitParseFailureTests()
    {
        Fixture fixture = new(
            includePositionals: false);

        CliRowSelectionArgumentResult missingValue =
            fixture.Lower(
                [
                    "demo",
                    "-n"
                ]);
        Assert.True(missingValue.HasParseErrors);
        Assert.NotNull(
            missingValue.ArgumentFailure);
        Assert.Equal(
            CliRowSelectionArgumentFailureReason.MissingValue,
            missingValue.ArgumentFailure.Reason);
        Assert.Null(missingValue.LoweringResult);
        Assert.Empty(missingValue.Occurrences);

        CliRowSelectionArgumentResult optionInsteadOfValue =
            fixture.Lower(
                [
                    "demo",
                    "-n",
                    "--rows",
                    "1..2"
                ]);
        Assert.NotNull(
            optionInsteadOfValue.ArgumentFailure);
        Assert.Equal(
            CliRowSelectionArgumentFailureReason.MissingValue,
            optionInsteadOfValue.ArgumentFailure.Reason);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Limit,
            optionInsteadOfValue.ArgumentFailure
                .OccurrenceKind);
        Assert.Equal(
            1,
            optionInsteadOfValue.ArgumentFailure.Position);
        Assert.Null(
            optionInsteadOfValue.LoweringResult);

        CliRowSelectionArgumentResult unknown =
            fixture.Lower(
                [
                    "demo",
                    "--unknown"
                ]);
        Assert.True(unknown.HasParseErrors);
        Assert.Null(unknown.LoweringResult);

        CliRowSelectionArgumentResult bundled =
            fixture.Lower(
                [
                    "demo",
                    "-an2"
                ]);
        Assert.True(bundled.HasParseErrors);
        Assert.Empty(bundled.Occurrences);
        Assert.Null(bundled.LoweringResult);

        CliRowSelectionArgumentResult limitFirstBundle =
            fixture.Lower(
                [
                    "demo",
                    "-na2"
                ]);
        Assert.True(limitFirstBundle.HasParseErrors);
        Assert.Empty(limitFirstBundle.Occurrences);
        Assert.Null(limitFirstBundle.LoweringResult);

        CliRowSelectionFailure bundledLimitValue =
            fixture.LoweringFailure(
                [
                    "demo",
                    "-n",
                    "-an2"
                ]);
        Assert.Equal(
            CliRowSelectionFailureReason.MalformedValue,
            bundledLimitValue.Reason);

        CliRowSelectionArgumentResult compactInsteadOfValue =
            fixture.Lower(
                [
                    "demo",
                    "-n",
                    "-n2"
                ]);
        Assert.NotNull(
            compactInsteadOfValue.ArgumentFailure);
        Assert.Equal(
            CliRowSelectionArgumentFailureReason.MissingValue,
            compactInsteadOfValue.ArgumentFailure.Reason);
        Assert.Null(compactInsteadOfValue.LoweringResult);

        CliRowSelectionArgumentResult attachedModifier =
            fixture.Lower(
                [
                    "demo",
                    "-n",
                    "2",
                    "--head=true"
                ]);
        Assert.NotNull(
            attachedModifier.ArgumentFailure);
        Assert.Equal(
            CliRowSelectionArgumentFailureReason
                .AttachedValueOnModifier,
            attachedModifier.ArgumentFailure.Reason);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Head,
            attachedModifier.ArgumentFailure
                .OccurrenceKind);
        Assert.Equal(
            3,
            attachedModifier.ArgumentFailure.Position);
        Assert.Null(
            attachedModifier.LoweringResult);

        Fixture positionalFixture = new();
        CliRowSelectionArgumentResult separatedModifierValue =
            positionalFixture.Success(
                [
                    "demo",
                    "-n",
                    "2",
                    "--head",
                    "true"
                ]);
        Assert.Null(
            separatedModifierValue.ArgumentFailure);
        string[] positionals =
            Assert.IsType<string[]>(
                separatedModifierValue.ParseResult
                    .GetValue(
                        positionalFixture.Positionals));
        Assert.Equal(
            ["true"],
            positionals);

        CliRowSelectionFailure repeatedOrder =
            fixture.LoweringFailure(
                [
                    "demo",
                    "--order-by=--order-by",
                    "--order-by",
                    "confidence"
                ]);
        Assert.Equal(
            CliRowSelectionFailureReason.RepeatedGesture,
            repeatedOrder.Reason);
        Assert.Equal(2, repeatedOrder.Position);
    }

    [Fact]
    public void CliRowSelectionExplicitAdapterCompositionTests()
    {
        Fixture fixture = new(
            capabilities:
                CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines);

        CliRowSelectionArgumentResult result =
            fixture.Success(
                [
                    "demo",
                    "--rows",
                    "3..6",
                    "-2",
                    "--lines"
                ]);
        RowSelectionIntentOperation<string> window =
            Assert.Single(
                result.LoweringResult!.Value!
                    .SemanticIntent.Operations);
        Assert.Equal(
            RowSelectionStageKind.Window,
            window.Kind);
        Assert.Equal(3, window.Start);
        Assert.Equal(6, window.End);
        Assert.NotNull(
            result.LoweringResult.Value.LineIntent);
        Assert.Equal(
            CliLineSelectionDirection.Head,
            result.LoweringResult.Value
                .LineIntent.Direction);
        Assert.Equal(
            2,
            result.LoweringResult.Value
                .LineIntent.Count);
    }

    private sealed class Fixture
    {
        private readonly RootCommand _root;
        private readonly CliRowSelectionOptionBindings
            _bindings;
        private readonly CliRowSelectionCapabilities
            _capabilities;

        public Fixture(
            bool includePositionals = true,
            CliRowSelectionCapabilities capabilities =
                CliRowSelectionCapabilities.All,
            string limitName = "-n",
            string? limitAlias = null,
            bool includeParent = false,
            bool parentAllowsMany = false,
            bool includeRootLimit = false)
        {
            Limit =
                RowValueOption(
                    limitName,
                    limitAlias);
            Rows =
                RowValueOption("--rows");
            Top =
                RowValueOption("--top");
            OrderBy =
                RowValueOption("--order-by");
            Head =
                ModifierOption("--head");
            Tail =
                ModifierOption("--tail");
            Lines =
                ModifierOption("--lines");
            TailLines =
                ModifierOption("--tail-lines");
            Required =
                RequiredValueOption("--required");
            RootRequired =
                new("--root-required")
                {
                    Arity =
                        ArgumentArity.ExactlyOne,
                    Recursive = true
                };
            Optional =
                new("--optional")
                {
                    Arity =
                        ArgumentArity.ZeroOrOne
                };
            Flag =
                ModifierOption("--flag");
            NoLogo =
                ModifierOption("-nologo");
            ShortA =
                ModifierOption("-a");
            Positionals =
                new("values")
                {
                    Arity =
                        includePositionals
                            ? ArgumentArity.ZeroOrMore
                            : ArgumentArity.Zero
                };

            var command =
                new Command("demo")
                {
                    Limit,
                    Rows,
                    Top,
                    OrderBy,
                    Head,
                    Tail,
                    Lines,
                    TailLines,
                    Required,
                    Optional,
                    Flag,
                    NoLogo,
                    ShortA
                };
            if (includePositionals)
            {
                command.Arguments.Add(
                    Positionals);
            }

            ParentPositionals =
                new("parent-values")
                {
                    Arity = ArgumentArity.ZeroOrMore
                };
            ParentPositional =
                new("parent-value")
                {
                    Arity = ArgumentArity.ZeroOrOne
                };
            _root = new();
            if (includeParent)
            {
                var parent =
                    new Command("outer")
                    {
                        command
                    };
                if (parentAllowsMany)
                {
                    parent.Arguments.Add(
                        ParentPositionals);
                }
                else
                {
                    parent.Arguments.Add(
                        ParentPositional);
                }

                _root.Subcommands.Add(
                    parent);
            }
            else
            {
                _root.Subcommands.Add(
                    command);
            }

            _root.Options.Add(
                RootRequired);
            if (includeRootLimit)
            {
                Limit.Recursive = true;
                _root.Options.Add(
                    Limit);
            }

            _bindings =
                new(
                    Limit,
                    Rows,
                    Top,
                    OrderBy,
                    Head,
                    Tail,
                    Lines,
                    TailLines);
            _capabilities = capabilities;
        }

        public Option<string[]> Limit { get; }

        public Option<string[]> Rows { get; }

        public Option<string[]> Top { get; }

        public Option<string[]> OrderBy { get; }

        public Option<bool> Head { get; }

        public Option<bool> Tail { get; }

        public Option<bool> Lines { get; }

        public Option<bool> TailLines { get; }

        public Option<string?> Required { get; }

        public Option<string?> RootRequired { get; }

        public Option<string?> Optional { get; }

        public Option<bool> Flag { get; }

        public Option<bool> NoLogo { get; }

        public Option<bool> ShortA { get; }

        public Argument<string[]> Positionals { get; }

        public Argument<string[]> ParentPositionals { get; }

        public Argument<string?> ParentPositional { get; }

        public CliRowSelectionArgumentResult Lower(
            string[] arguments) =>
            CliRowSelectionArgumentAdapter
                .LowerExplicit(
                    _root,
                    arguments,
                    _bindings,
                    _capabilities);

        public CliRowSelectionArgumentResult Success(
            string[] arguments)
        {
            CliRowSelectionArgumentResult result =
                Lower(arguments);
            Assert.False(result.HasParseErrors);
            Assert.Null(result.ArgumentFailure);
            Assert.NotNull(result.LoweringResult);
            Assert.True(result.LoweringResult.IsSuccess);
            return result;
        }

        public CliRowSelectionFailure LoweringFailure(
            string[] arguments)
        {
            CliRowSelectionArgumentResult result =
                Lower(arguments);
            Assert.False(result.HasParseErrors);
            Assert.Null(result.ArgumentFailure);
            Assert.NotNull(result.LoweringResult);
            Assert.False(result.LoweringResult.IsSuccess);
            return Assert.IsType<
                CliRowSelectionFailure>(
                result.LoweringResult.Failure);
        }

        private static Option<string[]> RowValueOption(
            string name,
            string? alias = null)
        {
            Option<string[]> option =
                alias is null
                    ? new(name)
                    : new(name, alias);
            option.Arity =
                ArgumentArity.OneOrMore;
            option.AllowMultipleArgumentsPerToken =
                false;
            return option;
        }

        private static Option<string?> RequiredValueOption(
            string name) =>
            new(name)
            {
                Arity =
                    ArgumentArity.ExactlyOne
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
