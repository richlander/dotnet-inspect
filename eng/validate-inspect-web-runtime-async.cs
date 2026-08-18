using System.Text;

if (args.Length != 1)
    throw new ArgumentException("Expected the published inspect-web _framework directory.");

string frameworkDirectory = Path.GetFullPath(args[0]);
if (!Directory.Exists(frameworkDirectory))
    throw new DirectoryNotFoundException(frameworkDirectory);

RunSelfTest();
Validate(frameworkDirectory);
Console.WriteLine("inspect-web runtime-async artifact gate passed.");

static void Validate(string frameworkDirectory)
{
    byte[] runtimeAsyncMarker = Encoding.UTF8.GetBytes("AsyncHelpers");
    foreach (string assemblyName in new[] { "InspectWeb.Engine", "NuGetFetch" })
    {
        string[] candidates = Directory.GetFiles(
            frameworkDirectory,
            $"{assemblyName}.*.wasm",
            SearchOption.TopDirectoryOnly);
        if (candidates.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected one published {assemblyName} Wasm assembly, found {candidates.Length}.");
        }

        byte[] assembly = File.ReadAllBytes(candidates[0]);
        if (assembly.AsSpan().IndexOf(runtimeAsyncMarker) >= 0)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(candidates[0])} contains runtime-async helpers, "
                + "which the Browser/Wasm host cannot execute.");
        }
    }
}

static void RunSelfTest()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"inspect-web-runtime-async-gate-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        File.WriteAllBytes(
            Path.Combine(directory, "InspectWeb.Engine.test.wasm"),
            "classic async"u8.ToArray());
        string nugetFetch = Path.Combine(directory, "NuGetFetch.test.wasm");
        File.WriteAllBytes(nugetFetch, "classic async"u8.ToArray());
        Validate(directory);

        File.WriteAllBytes(nugetFetch, "AsyncHelpers"u8.ToArray());
        try
        {
            Validate(directory);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains(
                "contains runtime-async helpers",
                StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            "The runtime-async artifact gate accepted its runtime-async canary.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}
