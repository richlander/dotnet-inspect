using System.Text;
using System.Text.Json;

string command = string.Join(" ", args.Take(3));
switch (command)
{
    case "sdk install 11":
        Console.WriteLine("fixture install status");
        return Environment.GetEnvironmentVariable("LAUNCHER_FIXTURE_INSTALL") == "fail" ? 23 : 0;

    case "dotnet -- --list-sdks":
        Console.WriteLine(Environment.GetEnvironmentVariable("LAUNCHER_FIXTURE_SDKS") ?? "11.0.100 [fixture]");
        return 0;

    case "dotnet -- --version":
        Console.WriteLine(Environment.GetEnvironmentVariable("LAUNCHER_FIXTURE_VERSION") ?? "11.0.100");
        return int.TryParse(
            Environment.GetEnvironmentVariable("LAUNCHER_FIXTURE_VERSION_EXIT"),
            out int versionExit)
            ? versionExit
            : 0;
}

if (args is ["dotnet", "--", ..])
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Args = args[2..],
        Stdin = await Console.In.ReadToEndAsync()
    }));
    byte[] standardError = Encoding.UTF8.GetBytes(
        Environment.GetEnvironmentVariable("LAUNCHER_FIXTURE_STDERR") ?? "");
    await Console.OpenStandardError().WriteAsync(standardError);
    return int.TryParse(
        Environment.GetEnvironmentVariable("LAUNCHER_FIXTURE_EXIT"),
        out int commandExit)
        ? commandExit
        : 0;
}

return 64;
