using System.Diagnostics;
using DotnetInspector.Metadata;

// Minimal CLI: dotnet-inspect-find <pattern> --library <path> [--types-only] [--perf <N>]
var cliArgs = Environment.GetCommandLineArgs().AsSpan(1);

string? pattern = null;
string? libraryPath = null;
bool typesOnly = false;
int perfIterations = 0;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--library" or "-l" when i + 1 < args.Length:
            libraryPath = args[++i];
            break;
        case "--types-only":
            typesOnly = true;
            break;
        case "--perf" when i + 1 < args.Length:
            perfIterations = int.Parse(args[++i]);
            break;
        case { } s when !s.StartsWith('-') && pattern is null:
            pattern = s;
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            PrintUsage();
            return 1;
    }
}

if (libraryPath is null)
{
    Console.Error.WriteLine("Error: --library is required");
    PrintUsage();
    return 1;
}

if (!File.Exists(libraryPath))
{
    Console.Error.WriteLine($"Error: file not found: {libraryPath}");
    return 1;
}

pattern ??= "*";

// Perf-test mode: run in a tight loop for profiling
if (perfIterations > 0)
{
    return RunPerfTest(libraryPath, pattern, typesOnly, perfIterations);
}

// Normal find mode
var api = AssemblyReader.ExtractApiSurface(libraryPath, typesOnly: typesOnly);
if (api is null)
{
    Console.Error.WriteLine($"Error: could not read metadata from: {libraryPath}");
    return 1;
}

var assemblyName = Path.GetFileNameWithoutExtension(libraryPath);
int matches = 0;

foreach (var type in api.Types)
{
    if (!MatchesGlob(type.FullName, pattern) && !MatchesGlob(type.Name, pattern))
        continue;

    matches++;
    Console.WriteLine($"{type.Kind,-10} {type.FullName,-50} {assemblyName}");
}

Console.Error.WriteLine($"\n{matches} type(s) found");
return 0;

// --- helpers ---

static int RunPerfTest(string path, string pattern, bool typesOnly, int iterations)
{
    // Warmup
    var api = AssemblyReader.ExtractApiSurface(path, typesOnly: typesOnly);
    if (api is null) { Console.Error.WriteLine("Error: no metadata"); return 1; }

    Console.Error.WriteLine($"Assembly:   {Path.GetFileName(path)}");
    Console.Error.WriteLine($"Types:      {api.Types.Count}");
    Console.Error.WriteLine($"Iterations: {iterations}");
    Console.Error.WriteLine($"Mode:       {(typesOnly ? "types-only" : "full")}");
    Console.Error.WriteLine();

    var sw = Stopwatch.StartNew();
    int typeCount = 0;

    for (int i = 0; i < iterations; i++)
    {
        var surface = AssemblyReader.ExtractApiSurface(path, typesOnly: typesOnly);
        typeCount += surface!.Types.Count;
    }

    sw.Stop();
    double totalMs = sw.Elapsed.TotalMilliseconds;
    double perIter = totalMs / iterations;

    Console.Error.WriteLine($"Total:          {totalMs:F1}ms");
    Console.Error.WriteLine($"Per iteration:  {perIter:F3}ms");
    Console.Error.WriteLine($"Types processed: {typeCount}");
    return 0;
}

static bool MatchesGlob(string text, string pattern)
{
    if (pattern == "*") return true;

    // Simple glob: * matches any sequence
    var parts = pattern.Split('*');
    int pos = 0;
    for (int i = 0; i < parts.Length; i++)
    {
        if (parts[i].Length == 0) continue;
        int idx = text.IndexOf(parts[i], pos, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;
        if (i == 0 && idx != 0 && !pattern.StartsWith('*')) return false;
        pos = idx + parts[i].Length;
    }
    if (!pattern.EndsWith('*') && pos != text.Length) return false;
    return true;
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage: dotnet-inspect-find [pattern] --library <path> [--types-only] [--perf <N>]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  pattern          Type name or glob (default: *)");
    Console.Error.WriteLine("  --library, -l    Path to assembly DLL");
    Console.Error.WriteLine("  --types-only     Skip member extraction");
    Console.Error.WriteLine("  --perf <N>       Run N iterations for benchmarking");
}
