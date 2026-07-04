using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

internal static class RenderAbSensor
{
    public static int Run(
        IReadOnlyList<string> assemblies,
        string? diffPath,
        string? emitPath,
        int maxExamples,
        int methodCap,
        int? workers,
        bool sequential)
    {
        Console.WriteLine($"Evaluating render A/B...");
        var current = CollectRenders(assemblies, methodCap, workers, sequential);
        
        if (emitPath is not null)
        {
            var json = JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(emitPath, json);
            Console.WriteLine($"Wrote baseline to {emitPath} ({current.Count} methods).");
            if (diffPath is null)
                return 0;
        }

        if (diffPath is not null)
        {
            var baseline = LoadBaseline(diffPath);
            return Compare(baseline, current, maxExamples);
        }
        
        return 0;
    }

    static Dictionary<string, string> LoadBaseline(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        
        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to load baseline {path}: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    static Dictionary<string, string> CollectRenders(
        IReadOnlyList<string> assemblies,
        int methodCap,
        int? workers,
        bool sequential)
    {
        var renders = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var options = new ParallelOptions { MaxDegreeOfParallelism = sequential ? 1 : (workers ?? Math.Max(1, Environment.ProcessorCount - 2)) };
        using var metadata = CorpusMetadata.Create(assemblies);

        foreach (var assemblyPath in assemblies)
        {
            var portablePath = CorpusSensor.PortablePath(assemblyPath);
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
            _ = source.ResolveShape(TypeRef.CoreLib("System", "Int32"));
            var stableSample = IrImporter.GetStableSampleCandidates(source, methodCap).ToList();

            Parallel.ForEach(stableSample, options, item =>
            {
                var typeName = item.TypeName;
                var methodName = item.MethodName;
                var function = item.Build(source);
                
                try
                {
                    // PrintRaised runs IrPasses.Default itself — a preceding
                    // IrPasses.Run here double-piped every method, so the
                    // sensor measured a second-run pipeline the product never
                    // ships (the double run folded goto-region diamonds the
                    // single run leaves raw — found via slice F1 scoping).
                    var rendered = CSharpPrinter.PrintRaised(function).Output;
                    if (rendered is not null)
                    {
                        string signature = CorpusMethodIdentity.SignatureText(function.Signature);
                        string key = $"{portablePath}!{typeName}::{methodName}{signature}";
                        renders.TryAdd(key, rendered.Trim());
                    }
                }
                catch
                {
                    // Ignore compilation crashes in A/B
                }
            });
        }
        return renders.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                      .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    static int Compare(Dictionary<string, string> baseline, Dictionary<string, string> current, int maxExamples)
    {
        int total = 0, changed = 0, added = 0, removed = 0;
        var diffs = new List<(string Key, string Before, string After)>();

        foreach (var kvp in current)
        {
            total++;
            if (!baseline.TryGetValue(kvp.Key, out var before))
            {
                added++;
            }
            else if (before != kvp.Value)
            {
                changed++;
                if (diffs.Count < maxExamples * 10) // Collect some for examples
                    diffs.Add((kvp.Key, before, kvp.Value));
            }
        }

        foreach (var key in baseline.Keys)
        {
            if (!current.ContainsKey(key))
                removed++;
        }

        Console.WriteLine($"Render A/B Check: {total} methods evaluated");
        Console.WriteLine($"Changed: {changed}");
        Console.WriteLine($"Added:   {added}");
        Console.WriteLine($"Removed: {removed}");

        if (changed == 0)
        {
            Console.WriteLine("No regressions found.");
            return 0;
        }

        Console.WriteLine("\n==== Selected Regressions ====");
        int printed = 0;
        foreach (var diff in diffs)
        {
            if (printed >= maxExamples)
                break;
            Console.WriteLine($"\nMethod: {diff.Key}");
            Console.WriteLine("--- Baseline ---");
            Console.WriteLine(diff.Before);
            Console.WriteLine("--- Current ---");
            Console.WriteLine(diff.After);
            printed++;
        }

        return 1;
    }
}