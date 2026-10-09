#:project ../src/ILInspector.Analysis/ILInspector.Analysis.csproj

// Regenerates the committed platform caller-unsafe projection
// (docs/design/platform-caller-unsafe-contracts.md) from one exact
// Microsoft.NETCore.App.Ref pack directory, for example
// .dotnet/packs/Microsoft.NETCore.App.Ref/11.0.0-rc.1.26425.128.

using System.Text;
using ILInspector.Analysis;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine(
        "Usage: dotnet run eng/generate-platform-caller-unsafe-contracts.cs -- "
        + "<Microsoft.NETCore.App.Ref/<version> directory> [output]");
    return 2;
}

var packRoot = new DirectoryInfo(Path.GetFullPath(args[0]));
if (!packRoot.Exists)
    throw new DirectoryNotFoundException(packRoot.FullName);
string packVersion = packRoot.Name;
string packId = packRoot.Parent?.Name
    ?? throw new InvalidDataException("The pack directory has no pack-id parent.");
if (packId != "Microsoft.NETCore.App.Ref")
    throw new InvalidDataException($"Expected a Microsoft.NETCore.App.Ref pack, found '{packId}'.");

string[] frameworks = Directory.GetDirectories(Path.Combine(packRoot.FullName, "ref"));
if (frameworks.Length != 1)
    throw new InvalidDataException($"Expected exactly one ref/<tfm> directory, found {frameworks.Length}.");

string text = PlatformCallerUnsafeProjectionBuilder.Build(
    packId,
    packVersion,
    Directory.EnumerateFiles(frameworks[0], "*.dll")
        .Order(StringComparer.Ordinal)
        .Select(path => (Path.GetFileName(path), File.ReadAllBytes(path))));

string outputPath = args.Length == 2
    ? Path.GetFullPath(args[1])
    : Path.Combine(FindRepositoryRoot(Directory.GetCurrentDirectory()), "src", "ILInspector.Analysis", "PlatformCallerUnsafeContracts.txt");
File.WriteAllText(outputPath, text, new UTF8Encoding(false));
Console.WriteLine($"Wrote {PlatformCallerUnsafeContracts.Parse(text).Count} entries from {packId} {packVersion} to {outputPath}.");
return 0;

static string FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null
        && !File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName
        ?? throw new DirectoryNotFoundException(
            "Run the generator from the dotnet-inspect repository.");
}
