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
            "Explain an exact product resource");
        var pathArgument = new Argument<string>("resource-path")
        {
            Description =
                "Exact product-resource path, such as "
                + "library/sections/reference-hierarchy",
        };
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

        command.Arguments.Add(pathArgument);
        command.Options.Add(depthOption);
        opts.AddFormatOptionTo(
            command,
            CliPresentationFormat.Json,
            CliPresentationFormat.Markdown,
            CliPresentationFormat.PlainText);
        command.Options.Add(outputPathOption);
        SharedOptions.AddOutputPathValidator(command, outputPathOption);

        command.SetAction(parseResult =>
            ResourceExplanationCommand.Execute(
                parseResult.GetValue(pathArgument)!,
                parseResult.GetValue(depthOption),
                opts.ResolveFormat(parseResult),
                parseResult.GetValue(outputPathOption)));
        return command;
    }
}
