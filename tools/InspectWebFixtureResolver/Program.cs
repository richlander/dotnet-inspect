using DotnetInspector.Fixtures;

// Supported catalog->path bridge for out-of-process harnesses (issue #5576's
// artifact-backed package scope adoption gate). Given fixture IDs, it prints one
// "<id>\t<absolute-assembly-path>" line per ID, resolving each through
// FixtureCatalog.AssemblyPath so consumers never rediscover binaries by scanning
// build outputs. The catalog throws with a build hint when a fixture is not yet
// built; this resolver's build-only project references materialize the fixtures
// it is asked to resolve.

if (args.Length == 0)
{
    Console.Error.WriteLine(
        "usage: inspect-web-fixture-resolver <fixture-id> [<fixture-id> ...]");
    return 2;
}

try
{
    foreach (string id in args)
    {
        string path = FixtureCatalog.AssemblyPath(id);
        Console.Out.WriteLine($"{id}\t{path}");
    }
}
catch (Exception error) when (error is ArgumentException or FileNotFoundException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

return 0;
