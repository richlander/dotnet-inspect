using DotnetInspector;

// Pre-process args for implicit package command
args = CommandLineBuilder.PreprocessArgs(args);

// Create and invoke command
var rootCommand = CommandLineBuilder.CreateRootCommand();
var result = rootCommand.Parse(args);
return await result.InvokeAsync();
