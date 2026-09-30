using ILInspector.AnalysisHarness;

if (args.Length is < 1 or > 4)
{
    return Usage();
}

string assemblyPath = args[0];
int iterations = 30;
bool json = false;
for (int index = 1; index < args.Length; index++)
{
    if (args[index] == "--json")
    {
        json = true;
        continue;
    }
    if (args[index] == "--iterations"
        && index + 1 < args.Length
        && int.TryParse(args[++index], out int value))
    {
        iterations = value;
        continue;
    }
    return Usage();
}

return ImplementationMetricPerformance.Run(
    assemblyPath,
    iterations,
    json);

static int Usage()
{
    Console.Error.WriteLine(
        "Usage: analysis-harness <System.Private.CoreLib.dll> "
        + "[--iterations N] [--json]");
    return 2;
}
