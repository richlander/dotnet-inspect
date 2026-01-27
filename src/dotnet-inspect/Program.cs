using DotnetInspector.Commands;

var (command, commandArgs) = CommandRouter.Route(args);
return await command.ExecuteAsync(commandArgs);
