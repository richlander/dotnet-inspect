using System.Collections.Concurrent;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Cheap, exhaustive validity-risk predicates. This is not the Roslyn validity
/// oracle; it is the coverage lane for known defect classes whose shape can be
/// recognized directly in raised IR without compiling a capped sample.
/// </summary>
internal static class ValidityPredicateScan
{
    const string ConditionalArmNumericJoinCast = "conditional-arm-numeric-join-cast";
    const string ConditionalTargetNumericCast = "conditional-target-numeric-cast";

    public static int Run(IReadOnlyList<string> assemblies, int maxExamples, int? workers, bool sequential)
    {
        var buckets = new ConcurrentDictionary<string, PredicateBucket>(StringComparer.Ordinal);
        int methods = 0;
        int passBugs = 0;
        using var metadata = CorpusMetadata.Create(assemblies);
        var options = new ParallelOptions { MaxDegreeOfParallelism = sequential ? 1 : (workers ?? Math.Max(1, Environment.ProcessorCount - 2)) };

        foreach (var assemblyPath in assemblies)
        {
            MetadataSource source;
            try { source = MetadataSource.Open(assemblyPath, context: metadata); }
            catch { continue; }
            using (source)
            {
                _ = source.ResolveShape(TypeRef.CoreLib("System", "Int32"));
                var items = IrImporter.ImportAssembly(source).ToList();
                Parallel.ForEach(items, options, item =>
                {
                    var (typeName, methodName, function) = item;
                    Interlocked.Increment(ref methods);
                    try
                    {
                        IrPasses.Run(function);
                    }
                    catch
                    {
                        Interlocked.Increment(ref passBugs);
                        return;
                    }

                    ScanFunction(source.AssemblyName, typeName, methodName, function, buckets, maxExamples);
                });
            }
        }

        Console.WriteLine($"VALIDITY PREDICATE SCAN over {methods} methods");
        Console.WriteLine("No compilation performed; counts are exhaustive IR-shape coverage for known validity-risk classes.");
        if (passBugs > 0)
            Console.WriteLine($"Pass bugs while scanning: {passBugs}");
        Console.WriteLine();
        Console.WriteLine("| Predicate | Count | Meaning |");
        Console.WriteLine("| --- | ---: | --- |");
        PrintBucket(buckets, ConditionalArmNumericJoinCast,
            "Conditional arm type needs an explicit numeric cast to the merged join type (#2302 class).");
        PrintBucket(buckets, ConditionalTargetNumericCast,
            "Conditional result type needs an explicit numeric cast at its typed sink (#2306 class).");

        foreach (var bucket in buckets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (bucket.Value.Examples.Count == 0)
                continue;
            Console.WriteLine();
            Console.WriteLine($"{bucket.Key} examples:");
            foreach (var example in bucket.Value.Examples.Order(StringComparer.Ordinal))
                Console.WriteLine($"  {example}");
        }

        return passBugs == 0 ? 0 : 1;
    }

    static void ScanFunction(
        string assemblyName,
        string typeName,
        string methodName,
        IrFunction function,
        ConcurrentDictionary<string, PredicateBucket> buckets,
        int maxExamples)
    {
        foreach (var conditional in function.Descendants.OfType<Conditional>())
        {
            if (conditional.MergedType is { } merged)
            {
                CountArm(conditional.WhenTrue, merged);
                CountArm(conditional.WhenFalse, merged);
            }
        }

        foreach (var sink in CoercionSinks.Enumerate(function))
        {
            if (sink.Value is not Conditional conditional || conditional.ResultType is not { } resultType)
                continue;
            if (resultType.Equals(sink.Target))
                continue;
            if (!CoercionRendering.CanSpellSlotCoercion(
                    resultType,
                    sink.Target,
                    function.TypeShapes,
                    function.EnumUnderlyingTypes))
                continue;
            Add(ConditionalTargetNumericCast, conditional, $"target {sink.Target.ToDisplayString()} from {resultType.ToDisplayString()}");
        }

        void CountArm(IrExpression arm, TypeRef merged)
        {
            if (arm.ResultType is not { } armType || !TypeFamilies.NeedsNumericCast(armType, merged))
                return;
            Add(ConditionalArmNumericJoinCast, arm, $"merged {merged.ToDisplayString()} from {armType.ToDisplayString()}");
        }

        void Add(string predicate, IrNode node, string detail)
        {
            var bucket = buckets.GetOrAdd(predicate, _ => new PredicateBucket(maxExamples));
            bucket.Add($"{assemblyName}!{typeName}::{methodName} IL_{Math.Max(0, node.SourceOffset):X4} — {detail}");
        }
    }

    static void PrintBucket(ConcurrentDictionary<string, PredicateBucket> buckets, string name, string meaning)
    {
        buckets.TryGetValue(name, out var bucket);
        Console.WriteLine($"| {name} | {bucket?.Count ?? 0} | {meaning} |");
    }

    sealed class PredicateBucket
    {
        readonly object _gate = new();
        readonly int _maxExamples;
        readonly List<string> _examples = [];

        public PredicateBucket(int maxExamples) => _maxExamples = Math.Max(0, maxExamples);

        public int Count;
        public IReadOnlyList<string> Examples
        {
            get
            {
                lock (_gate)
                    return _examples.ToArray();
            }
        }

        public void Add(string example)
        {
            Interlocked.Increment(ref Count);
            lock (_gate)
            {
                if (_examples.Count < _maxExamples)
                    _examples.Add(example);
            }
        }
    }
}
