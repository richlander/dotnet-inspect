using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
            var json = JsonSerializer.Serialize(ToBodyDictionary(current), new JsonSerializerOptions { WriteIndented = true });
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

    static Dictionary<string, RenderedMethod> CollectRenders(
        IReadOnlyList<string> assemblies,
        int methodCap,
        int? workers,
        bool sequential)
    {
        var renders = new ConcurrentDictionary<string, RenderedMethod>(StringComparer.Ordinal);
        var options = new ParallelOptions { MaxDegreeOfParallelism = sequential ? 1 : (workers ?? Math.Max(1, Environment.ProcessorCount - 2)) };
        using var metadata = CorpusMetadata.Create(assemblies);

        foreach (var assemblyPath in assemblies)
        {
            var portablePath = CorpusSensor.PortablePath(assemblyPath);
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
            _ = source.ResolveShape(TypeRef.CoreLib("System", "Int32"));
            var constraints = ShellConstraints.Build(source);
            var productSignatures = ValidityCheck.ProductSignatureQueues(source.Pe);
            var stableSample = IrImporter.GetStableSampleCandidates(source, methodCap).ToList();

            Parallel.ForEach(stableSample, options, item =>
            {
                var typeName = item.TypeName;
                var methodName = item.MethodName;
                var function = item.Build(source);
                string? productSignatureParameters = ValidityCheck.DequeueProductParameterList(productSignatures, typeName, methodName);
                string? productParameterList = function.Signature.Parameters.Any(p => p.HasDefault)
                    ? productSignatureParameters
                    : null;
                
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
                        renders.TryAdd(key, new RenderedMethod(
                            typeName,
                            methodName,
                            rendered.Trim(),
                            function,
                            constraints,
                            productParameterList));
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

    static Dictionary<string, string> ToBodyDictionary(Dictionary<string, RenderedMethod> renders)
        => renders.ToDictionary(kv => kv.Key, kv => kv.Value.Body, StringComparer.Ordinal);

    internal static int Compare(Dictionary<string, string> baseline, Dictionary<string, RenderedMethod> current, int maxExamples)
    {
        int total = 0, changed = 0, added = 0, removed = 0;
        var byClass = new Dictionary<DiffClass, int> { [DiffClass.Structural] = 0, [DiffClass.ParenEquivalent] = 0, [DiffClass.Unparsed] = 0 };
        var bySemantic = new Dictionary<SemanticTransition, int>
        {
            [SemanticTransition.ValidToValid] = 0,
            [SemanticTransition.InvalidToValid] = 0,
            [SemanticTransition.ValidToInvalid] = 0,
            [SemanticTransition.InvalidToInvalid] = 0,
        };
        var diffs = new List<(string Key, string Before, string After, DiffClass Class, SemanticTransition Semantic)>();
        var semanticRegressions = new List<(string Key, string Before, string After, ValidityCheck.RenderedBodyResult BeforeValidity, ValidityCheck.RenderedBodyResult AfterValidity)>();
        var references = ValidityCheck.RuntimeReferences();
        var parseOptions = ValidityCheck.ParseOptions();
        var compileOptions = ValidityCheck.CompileOptions();

        foreach (var kvp in current)
        {
            total++;
            var sample = kvp.Value;
            if (!baseline.TryGetValue(kvp.Key, out var before))
            {
                added++;
            }
            else if (before != sample.Body)
            {
                changed++;
                var diffClass = Classify(before, sample.Body);
                byClass[diffClass]++;
                var beforeValidity = CheckSemantic(sample, before, references, parseOptions, compileOptions);
                var afterValidity = CheckSemantic(sample, sample.Body, references, parseOptions, compileOptions);
                var transition = SemanticTransitionOf(beforeValidity, afterValidity);
                bySemantic[transition]++;
                if (transition == SemanticTransition.ValidToInvalid)
                    semanticRegressions.Add((kvp.Key, before, sample.Body, beforeValidity, afterValidity));
                if (diffs.Count < maxExamples * 10) // Collect some for examples
                    diffs.Add((kvp.Key, before, sample.Body, diffClass, transition));
            }
        }

        foreach (var key in baseline.Keys)
        {
            if (!current.ContainsKey(key))
                removed++;
        }

        Console.WriteLine($"Render A/B Check: {total} methods evaluated");
        Console.WriteLine(changed == 0
            ? "Changed: 0"
            : $"Changed: {changed} (structural: {byClass[DiffClass.Structural]}, paren-equivalent: {byClass[DiffClass.ParenEquivalent]}, unparsed: {byClass[DiffClass.Unparsed]})");
        if (changed > 0)
        {
            Console.WriteLine(
                "Semantic: "
                + $"valid->valid: {bySemantic[SemanticTransition.ValidToValid]}, "
                + $"invalid->valid: {bySemantic[SemanticTransition.InvalidToValid]}, "
                + $"valid->invalid: {bySemantic[SemanticTransition.ValidToInvalid]}, "
                + $"invalid->invalid: {bySemantic[SemanticTransition.InvalidToInvalid]}");
        }
        Console.WriteLine($"Added:   {added}");
        Console.WriteLine($"Removed: {removed}");

        if (changed == 0)
        {
            Console.WriteLine("No regressions found.");
            return 0;
        }

        Console.WriteLine("\n==== Selected Regressions ====");
        int printed = 0;
        foreach (var diff in diffs.OrderBy(d => d.Class))
        {
            if (printed >= maxExamples)
                break;
            Console.WriteLine($"\nMethod: {diff.Key} [{DiffClassLabel(diff.Class)}, {SemanticTransitionLabel(diff.Semantic)}]");
            Console.WriteLine("--- Baseline ---");
            Console.WriteLine(diff.Before);
            Console.WriteLine("--- Current ---");
            Console.WriteLine(diff.After);
            printed++;
        }

        if (semanticRegressions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("==== Semantic Regressions (valid->invalid) ====");
            foreach (var regression in semanticRegressions.Take(maxExamples))
            {
                Console.WriteLine($"\nMethod: {regression.Key}");
                Console.WriteLine("Diagnostics:");
                foreach (var diagnostic in Diagnostics(regression.AfterValidity).Take(5))
                    Console.WriteLine($"  {diagnostic.Id}: {diagnostic.Message}");
                Console.WriteLine("--- Baseline ---");
                Console.WriteLine(regression.Before);
                Console.WriteLine("--- Current ---");
                Console.WriteLine(regression.After);
            }
        }

        return semanticRegressions.Count > 0 ? 2 : 1;
    }

    /// <summary>
    /// Diff classes for a changed render, ordered by severity — the example
    /// printer shows structural changes first. Structural: the two sides parse
    /// to different trees, a real spelling change. ParenEquivalent: identical
    /// trees modulo <see cref="ParenthesizedExpressionSyntax"/> — parenthesis
    /// placement only, the churn class the precedence model (#2376) is allowed
    /// to produce without changing meaning. Unparsed: at least one side has
    /// syntax errors, so no equivalence claim is made.
    /// </summary>
    enum DiffClass { Structural, Unparsed, ParenEquivalent }

    enum SemanticTransition { ValidToValid, InvalidToValid, ValidToInvalid, InvalidToInvalid }

    static string DiffClassLabel(DiffClass diffClass) => diffClass switch
    {
        DiffClass.Structural => "structural",
        DiffClass.ParenEquivalent => "paren-equivalent",
        _ => "unparsed",
    };

    static string SemanticTransitionLabel(SemanticTransition transition) => transition switch
    {
        SemanticTransition.ValidToValid => "semantic valid->valid",
        SemanticTransition.InvalidToValid => "semantic invalid->valid",
        SemanticTransition.ValidToInvalid => "semantic valid->invalid",
        _ => "semantic invalid->invalid",
    };

    static SemanticTransition SemanticTransitionOf(ValidityCheck.RenderedBodyResult before, ValidityCheck.RenderedBodyResult after)
        => (before.IsValid, after.IsValid) switch
        {
            (true, true) => SemanticTransition.ValidToValid,
            (false, true) => SemanticTransition.InvalidToValid,
            (true, false) => SemanticTransition.ValidToInvalid,
            _ => SemanticTransition.InvalidToInvalid,
        };

    static ValidityCheck.RenderedBodyResult CheckSemantic(
        RenderedMethod sample,
        string body,
        ImmutableArray<MetadataReference> references,
        CSharpParseOptions parseOptions,
        CSharpCompilationOptions compileOptions)
        => ValidityCheck.EvaluateRenderedBody(
            sample.Function,
            body,
            sample.TypeName,
            sample.MethodName,
            sample.Constraints,
            sample.ProductParameterList,
            references,
            parseOptions,
            compileOptions,
            bindSemantics: true);

    static IEnumerable<ValidityCheck.ValidityDiagnostic> Diagnostics(ValidityCheck.RenderedBodyResult result)
        => result.IsMalformed ? result.MalformedDiagnostics : result.SemanticDiagnostics;

    static DiffClass Classify(string before, string after)
    {
        var beforeRoot = ParseBody(before);
        var afterRoot = ParseBody(after);
        if (beforeRoot is null || afterRoot is null)
            return DiffClass.Unparsed;
        var stripper = new ParenStripper();
        return SyntaxFactory.AreEquivalent(stripper.Visit(beforeRoot), stripper.Visit(afterRoot), topLevel: false)
            ? DiffClass.ParenEquivalent
            : DiffClass.Structural;
    }

    /// <summary>
    /// Parses a rendered method body inside an async shell — `await` only
    /// lexes as a keyword in an async context, while yield, labels, and local
    /// functions are statement-legal anywhere. Parse-only: the gate is syntax
    /// diagnostics, never semantics. Null when the body does not parse.
    /// </summary>
    static SyntaxNode? ParseBody(string body)
    {
        var tree = CSharpSyntaxTree.ParseText($"class C {{ async void M() {{\n{body}\n}} }}");
        var root = tree.GetRoot();
        return tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error) ? null : root;
    }

    sealed class ParenStripper : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
            => Visit(node.Expression);
    }

    internal sealed record RenderedMethod(
        string TypeName,
        string MethodName,
        string Body,
        IrFunction Function,
        IReadOnlyDictionary<string, Dictionary<string, string>> Constraints,
        string? ProductParameterList);
}