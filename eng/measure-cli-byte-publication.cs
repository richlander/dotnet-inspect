#:project ../src/DotnetInspect.Cli/DotnetInspect.Cli.csproj
#:property IsPublishable=true
#:property PublishAot=true
#:property OptimizationPreference=Speed
#:property InvariantGlobalization=true
#:property DotnetInspectRuntimeFlavor=true

using System.CommandLine;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using DotnetInspect.Cli;
using DotnetInspector.Packages;

if (args is ["--identity"])
{
    Console.WriteLine(
        $"{VersionInfo.Version};{VersionInfo.Flavor}");
    return 0;
}

int samples = 20;
if (args.Length == 5)
{
    if (args[3] != "--samples"
        || !int.TryParse(
            args[4],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out samples)
        || samples < 1)
    {
        return Usage();
    }
}
else if (args.Length != 3)
{
    return Usage();
}

string assemblyPath = Path.GetFullPath(args[0]);
if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine(
        $"Assembly not found: {assemblyPath}");
    return 2;
}

string typeName = args[1];
string fallbackMember = args[2];

if (VersionInfo.Flavor != "NativeAOT")
{
    Console.Error.WriteLine(
        $"Expected NativeAOT; got {VersionInfo.Flavor}.");
    return 2;
}

Console.OutputEncoding =
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
NuGetCache.Initialize("dotnet-inspect");

string[] directArguments =
[
    "type",
    typeName,
    "--library",
    assemblyPath,
    "-S",
    "Type Info",
    "--tsv",
];
string[] fallbackArguments =
[
    "type",
    typeName,
    "--library",
    assemblyPath,
    "-m",
    fallbackMember,
    "-S",
    "Type Info",
    "--tsv",
];

for (int index = 0; index < 5; index++)
{
    await InvokeAsync(directArguments);
    await InvokeAsync(fallbackArguments);
}

var directAllocations = new List<long>(samples);
var fallbackAllocations = new List<long>(samples);
for (int index = 0; index < samples; index++)
{
    if ((index & 1) == 0)
    {
        directAllocations.Add(
            await MeasureAsync(directArguments));
        fallbackAllocations.Add(
            await MeasureAsync(fallbackArguments));
    }
    else
    {
        fallbackAllocations.Add(
            await MeasureAsync(fallbackArguments));
        directAllocations.Add(
            await MeasureAsync(directArguments));
    }
}

directAllocations.Sort();
fallbackAllocations.Sort();

Console.Error.WriteLine(
    string.Join(
        '\t',
        "identity",
        VersionInfo.Version,
        VersionInfo.Flavor,
        Convert.ToHexStringLower(
            SHA256.HashData(
                File.ReadAllBytes(assemblyPath)))));
Console.Error.WriteLine(
    "scenario\tsamples\tmedian_allocated_b\tp95_allocated_b");
WriteResult("direct", directAllocations);
WriteResult("fallback", fallbackAllocations);

return 0;

async Task<long> MeasureAsync(string[] commandArguments)
{
    long before = GC.GetTotalAllocatedBytes(precise: true);
    await InvokeAsync(commandArguments);
    return GC.GetTotalAllocatedBytes(precise: true) - before;
}

static async Task InvokeAsync(string[] commandArguments)
{
    RootCommand root = CommandLineBuilder.CreateRootCommand();
    ParseResult result = root.Parse(commandArguments);
    int exitCode = await CommandLineBuilder.InvokeWithLineWindowAsync(
        result,
        commandArguments);
    if (exitCode != 0)
    {
        throw new InvalidOperationException(
            $"Command exited {exitCode}: "
            + string.Join(' ', commandArguments));
    }
}

static void WriteResult(
    string scenario,
    List<long> allocations)
{
    Console.Error.WriteLine(
        string.Join(
            '\t',
            scenario,
            allocations.Count,
            Percentile(allocations, 0.50),
            Percentile(allocations, 0.95)));
}

static long Percentile(
    IReadOnlyList<long> sorted,
    double percentile)
{
    int index = Math.Min(
        sorted.Count - 1,
        (int)Math.Ceiling(sorted.Count * percentile) - 1);
    return sorted[index];
}

static int Usage()
{
    Console.Error.WriteLine(
        "Usage: measure-cli-byte-publication "
        + "<assembly> <type> <fallback-member> [--samples <count>]");
    return 2;
}
