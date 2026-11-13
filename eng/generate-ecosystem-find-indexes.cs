#:project ../src/DotnetInspect.Cli/DotnetInspect.Cli.csproj

using DotnetInspect.Cli.Tools;
using DotnetInspector.Packages;

if (args.Length is < 1 or > 3)
{
    Console.Error.WriteLine(
        "Usage: OUTPUT_DIRECTORY [TARGET_FRAMEWORK] [MAXIMUM_PACKAGES]");
    return 2;
}

var request = new EcosystemFindIndexGenerationRequest(
    args[0],
    args.Length >= 2 ? args[1] : "net10.0",
    args.Length >= 3 ? int.Parse(args[2]) : 25);
NuGetCache.Initialize("dotnet-inspect");
using var client = new HttpClient
{
    Timeout = TimeSpan.FromMinutes(10),
};
IReadOnlyList<EcosystemFindIndexGenerationResult> results =
    await EcosystemFindIndexGeneration.GenerateAsync(
        client,
        request,
        Console.Error.WriteLine);
foreach (EcosystemFindIndexGenerationResult result in results)
{
    Console.WriteLine(
        $"{result.Ecosystem}\t{result.Packages}\t{result.Libraries}\t"
        + $"{result.Symbols}\t{result.Gaps}\t{result.Path}");
}
return 0;
