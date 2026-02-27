using System.CommandLine;
using System.CommandLine.Help;
using DotnetInspector.Commands;
using DotnetInspector.Output;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the type, member, and deprecated api commands.
/// </summary>
public static class ApiCommandDefinitions
{
    /// <summary>
    /// Creates a deprecated hidden api command that shows a deprecation message.
    /// </summary>
    public static Command CreateDeprecatedApiCommand()
    {
        var deprecatedApiCommand = new Command("api", "Deprecated: Use 'type' or 'member' instead") { Hidden = true };
        deprecatedApiCommand.TreatUnmatchedTokensAsErrors = false;
        deprecatedApiCommand.SetAction(_ =>
        {
            Console.Error.WriteLine("The 'api' command is deprecated. Please use:");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  type   - Discover types in a package/library (terse, no docs by default)");
            Console.Error.WriteLine("  member - Inspect type members (docs by default)");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  dotnet-inspect type --package System.Text.Json");
            Console.Error.WriteLine("  dotnet-inspect member JsonSerializer --package System.Text.Json");
            Console.Error.WriteLine("  dotnet-inspect member -m JsonSerializer.Deserialize --package System.Text.Json");
            return 1;
        });
        return deprecatedApiCommand;
    }

    /// <summary>
    /// Creates the type command for fast type discovery (terse, no docs by default).
    /// </summary>
    public static Command CreateTypeCommand(SharedOptions opts)
    {
        var typeCommand = new Command(TypeCommand.Name, "Discover types in a package or library (terse output)");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Package and type pattern. When no --package/--library/--platform is given, first arg is the package.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var packageOption = new Option<string?>("--package") { Description = "Source: package (file, name, or name@version)" };
        var assemblyOption = new Option<string?>("--library") { Description = "Source: library path (local file, or relative within package)" };
        var platformOption = new Option<string?>("--platform") { Description = "Source: platform library (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Source: platform framework (runtime, aspnetcore, netstandard). @version for specific" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Source: select by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden (EditorBrowsable.Never) and obsolete members" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Filter types by glob pattern (e.g., *Json*, Progress*)" };
        typeFilterOption.Aliases.Add("--type");
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        var shapeOption = new Option<bool>("--shape") { Description = "Output type shape (inheritance, interfaces, members)" };
        shapeOption.Aliases.Add("--tree");
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter types with unsafe signatures (pointers)" };
        var memberOption = new Option<string[]>("-m")
        {
            Description = "Filter members by name or limit count (-m 5)",
            AllowMultipleArgumentsPerToken = true
        };
        memberOption.Aliases.Add("--member");
        var kindOption = new Option<string[]>("-k")
        {
            Description = "Filter by kind (class, struct, interface, enum, delegate, method, property, field, event, constructor)",
            AllowMultipleArgumentsPerToken = true
        };
        kindOption.Aliases.Add("--kind");

        typeCommand.Arguments.Add(argsArg);
        typeCommand.Options.Add(packageOption);
        typeCommand.Options.Add(assemblyOption);
        typeCommand.Options.Add(platformOption);
        typeCommand.Options.Add(frameworkOption);
        typeCommand.Options.Add(tfmOption);
        typeCommand.Options.Add(allOption);
        typeCommand.Options.Add(typeFilterOption);
        typeCommand.Options.Add(opts.Limit);
        typeCommand.Options.Add(opts.Json);
        typeCommand.Options.Add(compactOption);
        typeCommand.Options.Add(oneLineOption);
        typeCommand.Options.Add(noHeaderOption);
        typeCommand.Options.Add(shapeOption);
        typeCommand.Options.Add(unsafeOption);
        typeCommand.Options.Add(memberOption);
        typeCommand.Options.Add(kindOption);
        opts.AddSectionOptionsTo(typeCommand);
        typeCommand.Options.Add(opts.Markdown);
        opts.AddOutputOptionsTo(typeCommand);
        opts.AddNuGetOptionsTo(typeCommand);

        var commandArgs = new TypeOptionsParser.TypeCommandArgs(
            argsArg, packageOption, assemblyOption, platformOption, frameworkOption, tfmOption,
            allOption, typeFilterOption, compactOption, oneLineOption,
            noHeaderOption, shapeOption, unsafeOption, memberOption, kindOption);

        typeCommand.SetAction(async (parseResult, ct) =>
        {
            var result = await TypeOptionsParser.ParseAsync(parseResult, opts, commandArgs);

            switch (result)
            {
                case TypeOptionsParser.Discovery d:
                    var typeSchema = new MarkoutContext().GetSchemaInfo<CliApiSurface>();
                    SelectResolver.Discover(d.Select, d.Columns, d.Fields,
                        SectionRegistry.ApiTypeSections, typeSchema);
                    return 0;

                case TypeOptionsParser.ShowHelp:
                    new HelpAction().Invoke(parseResult);
                    return 0;

                case TypeOptionsParser.VersionError error:
                    Console.Error.WriteLine(error.Message);
                    return 1;

                case TypeOptionsParser.UnrecognizedOption error:
                    Console.Error.WriteLine($"Error: Unrecognized option '{error.Option}'.");
                    return 1;

                case TypeOptionsParser.Success success:
                    return await TypeCommand.ExecuteAsync(success.Options);

                default:
                    return 1;
            }
        });

        return typeCommand;
    }

    /// <summary>
    /// Creates the member command for deep member inspection (docs on by default).
    /// </summary>
    public static Command CreateMemberCommand(SharedOptions opts)
    {
        var memberCommand = new Command(MemberCommand.Name, "Inspect type members (docs on by default)");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Package, type name, and member filter. When no --package/--library/--platform is given, first arg is the package.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var packageOption = new Option<string?>("--package") { Description = "Source: package (file, name, or name@version)" };
        var assemblyOption = new Option<string?>("--library") { Description = "Source: library path (local file, or relative within package)" };
        var platformOption = new Option<string?>("--platform") { Description = "Source: platform library (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Source: platform framework (runtime, aspnetcore, netstandard). @version for specific" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Source: select by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden (EditorBrowsable.Never) and obsolete members" };
        var memberOption = new Option<string[]>("-m")
        {
            Description = "Filter members by name (supports globs, Type.Member dotted syntax)",
            AllowMultipleArgumentsPerToken = true
        };
        memberOption.Aliases.Add("--member");
        var ctorOption = new Option<bool>("--ctor") { Description = "Filter members to constructors (shorthand for -m .ctor)" };
        var docsOption = new Option<bool>("--docs") { Description = "Include XML doc comments (on by default, use --no-docs to suppress)" };
        var noDocsOption = new Option<bool>("--no-docs") { Description = "Suppress XML doc comments" };
        var useLocalDocsOption = new Option<bool>("--use-local-docs") { Description = "Include XML docs from local packs directory (offline)" };
        var samplesOption = new Option<bool>("--samples") { Description = "Include code samples from source" };
        var browsableUrlsOption = new Option<bool>("--browsable-urls") { Description = "Use /blob/ URLs for browser viewing (default: /raw/ for LLM consumption)" };
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter members to unsafe signatures (pointers)" };
        var indexOption = new Option<int?>("--index") { Description = "Select member overload by index (or use Name:N shorthand)" };
        var paramsOption = new Option<string>("--params") { Description = "Select member overload by parameter types (comma-separated)" };
        var ofOption = new Option<string>("-of") { Description = "Select member overload by first parameter type" };
        var selectOption = new Option<bool>("--select") { Description = "Show member overload index (Name:N) column" };

        memberCommand.Arguments.Add(argsArg);
        memberCommand.Options.Add(packageOption);
        memberCommand.Options.Add(assemblyOption);
        memberCommand.Options.Add(platformOption);
        memberCommand.Options.Add(frameworkOption);
        memberCommand.Options.Add(tfmOption);
        memberCommand.Options.Add(allOption);
        memberCommand.Options.Add(memberOption);
        memberCommand.Options.Add(ctorOption);
        memberCommand.Options.Add(opts.Limit);
        memberCommand.Options.Add(docsOption);
        memberCommand.Options.Add(noDocsOption);
        memberCommand.Options.Add(useLocalDocsOption);
        memberCommand.Options.Add(samplesOption);
        memberCommand.Options.Add(browsableUrlsOption);
        memberCommand.Options.Add(opts.Json);
        memberCommand.Options.Add(compactOption);
        memberCommand.Options.Add(oneLineOption);
        memberCommand.Options.Add(noHeaderOption);
        memberCommand.Options.Add(unsafeOption);
        memberCommand.Options.Add(indexOption);
        memberCommand.Options.Add(paramsOption);
        memberCommand.Options.Add(ofOption);
        memberCommand.Options.Add(selectOption);
        opts.AddSectionOptionsTo(memberCommand);
        memberCommand.Options.Add(opts.Markdown);
        opts.AddOutputOptionsTo(memberCommand);
        opts.AddNuGetOptionsTo(memberCommand);

        var commandArgs = new MemberOptionsParser.MemberCommandArgs(
            argsArg, packageOption, assemblyOption, platformOption, frameworkOption, tfmOption,
            allOption, memberOption, ctorOption, docsOption, noDocsOption, useLocalDocsOption,
            samplesOption, browsableUrlsOption, compactOption, oneLineOption, noHeaderOption,
            unsafeOption, indexOption, paramsOption, ofOption, selectOption);

        memberCommand.SetAction(async (parseResult, ct) =>
        {
            var result = await MemberOptionsParser.ParseAsync(parseResult, opts, commandArgs);

            switch (result)
            {
                case MemberOptionsParser.Discovery d:
                    var memberSchema = new MarkoutContext().GetSchemaInfo<TypeView>();
                    SelectResolver.Discover(d.Select, d.Columns, d.Fields,
                        SectionRegistry.ApiMemberSections, memberSchema);
                    return 0;

                case MemberOptionsParser.ShowHelp:
                    new HelpAction().Invoke(parseResult);
                    return 0;

                case MemberOptionsParser.VersionError error:
                    Console.Error.WriteLine(error.Message);
                    return 1;

                case MemberOptionsParser.UnrecognizedOption error:
                    Console.Error.WriteLine($"Error: Unrecognized option '{error.Option}'.");
                    return 1;

                case MemberOptionsParser.Success success:
                    return await MemberCommand.ExecuteAsync(success.Options);

                default:
                    return 1;
            }
        });

        return memberCommand;
    }
}
