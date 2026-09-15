using DotnetInspector.Fixtures;

// Supported catalog->path bridge for out-of-process harnesses (issue #5576's
// artifact-backed package scope adoption gate). A fixture ID resolves its assembly;
// an optional ":asset" suffix selects a cataloged package or source asset.
// Each selection prints "<selection>\t<absolute-path>", without scanning build
// outputs. The catalog throws with a build hint when a fixture is not yet
// built; this resolver's build-only project references materialize the fixtures
// it is asked to resolve.

if (args.Length == 0)
{
    Console.Error.WriteLine(
        "usage: inspect-web-fixture-resolver <fixture-id>[:<asset>] [<fixture-id>[:<asset>] ...]");
    return 2;
}

try
{
    foreach (string id in args)
    {
        string[] selection = id.Split(':', 2);
        string path = selection.Length == 1
            ? FixtureCatalog.AssemblyPath(id)
            : FixtureCatalog.Get(selection[0]).AssetPath(selection[1]);
        Console.Out.WriteLine($"{id}\t{path}");
    }
}
catch (Exception error) when (error is ArgumentException or FileNotFoundException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

return 0;
