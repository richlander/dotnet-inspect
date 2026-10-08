using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

public static class ResourceExplanationCommandDefinitions
{
    public static Command CreateExplainCommand(SharedOptions opts)
    {
        var command = new Command(
            ResourceExplanationCommand.Name,
            "Explain an exact product resource or search installed capabilities");
        var operandArgument = new Argument<string>("resource-or-search")
        {
            Description =
                "Exact product-resource path, such as "
                + "library/sections/reference-hierarchy or "
                + "package-query/query/facets/library-literal; otherwise "
                + "bounded capability-search text such as literal",
        };
        var projectionArgument = new Argument<string?>("projection")
        {
            Description = ".contract selects the complete self-contained explanation contract",
            Arity = ArgumentArity.ZeroOrOne,
        };
        projectionArgument.Validators.Add(result =>
        {
            string? projection = result.GetValue(projectionArgument);
            if (projection is not null && projection != ".contract")
                result.AddError("The supported exact explanation projection is .contract.");
        });
        var depthOption = new Option<int>("--depth")
        {
            Description =
                "Related-resource expansion depth (default: 0, maximum: 8)",
            DefaultValueFactory = static _ => 0,
        };
        var outputPathOption = SharedOptions.CreateOutputPathOption();

        depthOption.Validators.Add(result =>
        {
            if (result.GetValue(depthOption) is < 0 or > 8)
                result.AddError("--depth must be between 0 and 8.");
        });

        command.Arguments.Add(operandArgument);
        command.Arguments.Add(projectionArgument);
        command.Options.Add(depthOption);
        command.Options.Add(opts.Json);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        opts.AddTableOptionsTo(command);
        opts.AddEnvelopeOptionTo(command);
        command.Options.Add(opts.Limit);
        command.Options.Add(outputPathOption);
        SharedOptions.AddOutputPathValidator(command, outputPathOption);

        command.SetAction(parseResult =>
            ResourceExplanationCommand.Execute(
                parseResult.GetValue(operandArgument)!,
                parseResult.GetValue(depthOption),
                parseResult.GetResult(depthOption) is { Implicit: false },
                parseResult.GetValue(opts.Limit),
                opts.ResolveFormat(parseResult),
                parseResult.GetValue(opts.Envelope),
                parseResult.GetValue(opts.NoHeaders),
                parseResult.GetValue(outputPathOption),
                parseResult.GetValue(projectionArgument) == ".contract"));
        return command;
    }
}
