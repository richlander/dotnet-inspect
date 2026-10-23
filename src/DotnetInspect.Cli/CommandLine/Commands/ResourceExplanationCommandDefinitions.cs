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
            "Explain an exact product resource, an extracted component, or search installed capabilities; "
            + "with no operand, list the subject kinds, operations, and "
            + "references to start from");
        var operandArgument = new Argument<string?>("resource-or-search")
        {
            Description =
                "Exact product-resource path, such as "
                + "library/sections/reference-hierarchy or "
                + "package-query/query/facets/library-literal; otherwise "
                + "bounded capability-search text such as literal. Omit it "
                + "to explain the root",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var projectionArgument = new Argument<string?>("projection")
        {
            Description = ".data selects a self-contained reading dataset; .hal selects HAL resource data and navigation; .contract selects declarations",
            Arity = ArgumentArity.ZeroOrOne,
        };
        projectionArgument.Validators.Add(result =>
        {
            string? projection = result.GetValue(projectionArgument);
            if (projection is not null && projection is not (".contract" or ".data" or ".hal"))
                result.AddError("Supported exact explanation projections are .contract, .data, and .hal.");
        });
        var depthOption = new Option<int>("--depth")
        {
            Description =
                "Related-resource expansion depth (default: 0, maximum: 8)",
            DefaultValueFactory = static _ => 0,
        };
        var outputPathOption = SharedOptions.CreateOutputPathOption();
        var componentOption = new Option<string?>("--component")
        {
            Description = "Explain an extracted Workspace Package declaration from a UTF-8 file; - reads redirected stdin",
        };
        var parentPacketOption = new Option<string?>("--parent-packet")
        {
            Description = "Qualify --component with its canonical encoded parent Workspace packet, without restoring it",
        };
        var valuesOption = new Option<string?>("-Q")
        {
            Description = "Narrow vocabulary .data/.hal to exact native value IDs (comma/semicolon-separated; bare: all)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        valuesOption.Aliases.Add("--query-help");

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
        command.Options.Add(valuesOption);
        command.Options.Add(componentOption);
        command.Options.Add(parentPacketOption);
        SharedOptions.AddOutputPathValidator(command, outputPathOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            // A lone projection selects the root, or explicit component input.
            string? operand = parseResult.GetValue(operandArgument);
            string? projection = parseResult.GetValue(projectionArgument);
            if (projection is null
                && operand is ".contract" or ".data" or ".hal")
            {
                projection = operand;
                operand = null;
            }
            string? component = parseResult.GetValue(componentOption);
            string? parentPacket = parseResult.GetValue(parentPacketOption);
            OutputFormat format = opts.ResolveFormat(
                parseResult,
                operand is null && projection is null && component is null
                    ? OutputFormat.Tsv : OutputFormat.Markdown);
            if (component is not null || parentPacket is not null)
            {
                if (string.IsNullOrWhiteSpace(component) || operand is not null)
                {
                    CommandError.Write("--component requires a file or - and cannot accompany a resource/search operand; --parent-packet requires --component.");
                    return 1;
                }
                if (parseResult.GetResult(valuesOption) is { Implicit: false }
                    || parseResult.GetValue(opts.Limit) is not null
                    || parseResult.GetValue(opts.Envelope)
                    || format is not (OutputFormat.Markdown or OutputFormat.PlainText or OutputFormat.Json))
                {
                    CommandError.Write("Component explanation supports Markdown, plain text and JSON; -n, -Q, --envelope and table/row formats do not apply.");
                    return 1;
                }
                if (projection is not null && format != OutputFormat.Json)
                {
                    CommandError.Write("Component explanation projections require --json.");
                    return 1;
                }
                if (projection is ".data" or ".hal"
                    && parseResult.GetResult(depthOption) is { Implicit: false })
                {
                    CommandError.Write("Selected data closes over its reading dataset; --depth does not apply.");
                    return 1;
                }
                return await ResourceExplanationCommand.ExplainComponentAsync(
                    component, parentPacket, parseResult.GetValue(depthOption),
                    format, parseResult.GetValue(outputPathOption), projection,
                    cancellationToken);
            }
            string[]? valueIds = SharedOptions.ParseProjectionList(parseResult, valuesOption);
            if (parseResult.GetValue(valuesOption) is not null && valueIds is not { Length: > 0 })
            {
                CommandError.Write("-Q requires at least one exact vocabulary value identity when a value is supplied.");
                return 1;
            }
            return ResourceExplanationCommand.Execute(
                operand,
                parseResult.GetValue(depthOption),
                parseResult.GetResult(depthOption) is { Implicit: false },
                parseResult.GetValue(opts.Limit),
                // Bare explain is the agent's starting point, so its default
                // reading is the routing table as TSV.
                format,
                parseResult.GetValue(opts.Envelope),
                parseResult.GetValue(opts.NoHeaders),
                parseResult.GetValue(outputPathOption),
                projection == ".contract",
                projection is ".data" or ".hal" ? projection : null,
                valueIds);
        });
        return command;
    }
}
