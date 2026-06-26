using System.Reflection;

using ILInspector.Analysis;

// `unsafe <assembly> [count]` — detect requires-unsafe methods under the new
// memory-safety model (CallerUnsafeMode) and rank them by direct callers.
if (args.Length > 0 && args[0] == "unsafe")
    return RunUnsafeReport(args);

var options = Parse(args);
if (options is null)
    return 1;

var index = LibraryBodyIndex.Open(options.AssemblyPath);
var matches = index.FindCalls(MemberPattern.Method(options.DeclaringType, options.MemberName));

Console.WriteLine($"Assembly: {options.AssemblyPath}");
Console.WriteLine($"Methods with IL: {index.Methods.Length:N0}");
Console.WriteLine($"Direct call edges: {index.DirectCalls.Length:N0}");
Console.WriteLine($"Diagnostics: {index.Diagnostics.Length:N0}");
Console.WriteLine($"Target: {options.DeclaringType}.{options.MemberName}");
Console.WriteLine($"Matches: {matches.Length:N0}");
Console.WriteLine();

foreach (var call in matches.OrderBy(c => c.Caller.DeclaringType.ToQualifiedDisplayString(), StringComparer.Ordinal)
             .ThenBy(c => c.Caller.Name, StringComparer.Ordinal)
             .ThenBy(c => c.ILOffset)
             .Take(options.Limit))
{
    Console.WriteLine($"{MethodDisplay(call.Caller)} IL_{call.ILOffset:X4} {call.Kind} -> {MemberDisplay(call.Callee)}");
}

if (matches.Length > options.Limit)
    Console.WriteLine($"... {matches.Length - options.Limit:N0} more matches");

foreach (var diagnostic in index.Diagnostics.Take(options.Limit))
    Console.Error.WriteLine($"diagnostic 0x{diagnostic.MethodToken:X8} {diagnostic.Method}: {diagnostic.Message}");

return 0;

static int RunUnsafeReport(string[] args)
{
    string assemblyPath = args.Length > 1 ? args[1] : Assembly.GetExecutingAssembly().Location;
    int count = 6;
    if (args.Length > 2 && (!int.TryParse(args[2], out count) || count <= 0))
    {
        Console.Error.WriteLine("Error: count must be a positive integer.");
        return 1;
    }
    if (!File.Exists(assemblyPath))
    {
        Console.Error.WriteLine($"Error: assembly not found: {assemblyPath}");
        return 1;
    }

    var index = LibraryBodyIndex.Open(assemblyPath);
    var modes = index.UnsafeModes;
    var top = index.TopUnsafeLeverage(count);

    Console.WriteLine($"Assembly: {assemblyPath}");
    Console.WriteLine($"Module opted into updated memory-safety rules: {index.MemorySafetyRulesEnabled}");
    Console.WriteLine($"Methods: {modes.Total:N0}");
    Console.WriteLine($"  None     (no requires-unsafe):              {modes.None:N0}");
    Console.WriteLine($"  Implicit (requires-unsafe, module not opted in): {modes.Implicit:N0}");
    Console.WriteLine($"  Explicit (requires-unsafe, module opted in):     {modes.Explicit:N0}");
    Console.WriteLine();
    Console.WriteLine($"Top {top.Length} requires-unsafe methods by direct callers:");
    Console.WriteLine();
    int rank = 1;
    foreach (var entry in top)
        Console.WriteLine($"{rank++}. {entry.DirectCallerCount,6} callers  [{entry.Mode}]  {MethodDisplay(entry.Method)}");

    var opaque = index.OpaqueUnsafeMethods();
    Console.WriteLine();
    Console.WriteLine($"Opaque-contract methods ({opaque.Length}): requires-unsafe with no pointer in the signature");
    Console.WriteLine("  (the obligation is visible only via the attribute / unsafe modifier, not the signature)");
    Console.WriteLine();
    foreach (var entry in opaque)
        Console.WriteLine($"  [{entry.Mode}]  {MethodDisplay(entry.Method)}");

    var hollow = index.HollowUnsafeMethods();
    Console.WriteLine();
    Console.WriteLine($"Hollow-unsafe methods ({hollow.Length}): requires-unsafe with no directly-visible unsafe operation");
    Console.WriteLine("  (an absence claim, never \"safe\" — an optimized-away pointer local can erase a real deref)");
    Console.WriteLine();
    foreach (var entry in hollow)
        Console.WriteLine($"  [{entry.Mode}]  {MethodDisplay(entry.Method)}");

    return 0;
}

static AppOptions? Parse(string[] args)
{
    string assemblyPath = Assembly.GetExecutingAssembly().Location;
    string target = "System.Console.WriteLine";
    int limit = 40;

    if (args.Length > 0 && args[0] is "-h" or "--help")
    {
        WriteUsage();
        return null;
    }

    if (args.Length > 0)
        assemblyPath = args[0];
    if (args.Length > 1)
        target = args[1];
    if (args.Length > 2 && (!int.TryParse(args[2], out limit) || limit <= 0))
    {
        Console.Error.WriteLine("Error: limit must be a positive integer.");
        WriteUsage();
        return null;
    }

    if (!File.Exists(assemblyPath))
    {
        Console.Error.WriteLine($"Error: assembly not found: {assemblyPath}");
        WriteUsage();
        return null;
    }

    if (!TryParseTarget(target, out var declaringType, out var memberName))
    {
        Console.Error.WriteLine($"Error: target must be Type.Member or Type::Member: {target}");
        WriteUsage();
        return null;
    }

    return new AppOptions(assemblyPath, declaringType, memberName, limit);
}

static bool TryParseTarget(string target, out string declaringType, out string memberName)
{
    int separator = target.LastIndexOf("::", StringComparison.Ordinal);
    int memberStart = separator >= 0 ? separator + 2 : -1;
    if (separator < 0)
    {
        separator = target.LastIndexOf('.');
        memberStart = separator + 1;
    }

    if (separator <= 0 || memberStart >= target.Length)
    {
        declaringType = "";
        memberName = "";
        return false;
    }

    declaringType = target[..separator];
    memberName = target[memberStart..];
    return true;
}

static string MethodDisplay(MethodIdentity method)
    => $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}({string.Join(", ", method.ParameterTypes.Select(p => p.ToQualifiedDisplayString()))})";

static string MemberDisplay(MemberRef member)
    => $"{member.DeclaringType.ToQualifiedDisplayString()}.{member.Name}({string.Join(", ", member.ParameterTypes.Select(p => p.ToQualifiedDisplayString()))})";

static void WriteUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  dotnet run --project src/ILInspector.Analysis.App -c Release");
    Console.Error.WriteLine("  dotnet run --project src/ILInspector.Analysis.App -c Release -- <assembly-path> [Type.Member|Type::Member] [limit]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Defaults: scan this app for System.Console.WriteLine, limit 40.");
}

sealed record AppOptions(string AssemblyPath, string DeclaringType, string MemberName, int Limit);
