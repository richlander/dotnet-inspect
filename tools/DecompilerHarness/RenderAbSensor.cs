using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

internal static class RenderAbSensor
{
    const int BaselineVersion = 2;
    const int StructuralArtifactVersion = 1;
    static readonly ResearchFactRegistry s_emptyFactRegistry = new();

    public static int Run(
        IReadOnlyList<string> assemblies,
        string? diffPath,
        string? emitPath,
        int maxExamples,
        int methodCap,
        int? workers,
        bool sequential,
        string? structuralDiffDirectory = null)
    {
        Console.WriteLine($"Evaluating render A/B...");
        BaselineArtifact? comparisonBaseline = diffPath is null
            ? null
            : LoadBaseline(diffPath);
        if (diffPath is not null
            && comparisonBaseline is null
            && emitPath is null)
        {
            return 2;
        }

        var current = CollectRenders(
            assemblies,
            methodCap,
            workers,
            sequential,
            comparisonBaseline?.Methods,
            captureAllStructuralDocuments: emitPath is not null);
        
        if (emitPath is not null)
        {
            BaselineArtifact baseline;
            try
            {
                baseline = CreateBaseline(current);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(
                    $"Failed to create Render A/B baseline: {ex.Message}");
                return 2;
            }
            var json = JsonSerializer.Serialize(
                baseline,
                new JsonSerializerOptions());
            File.WriteAllText(emitPath, json);
            HarnessLog.Status($"Wrote baseline to {emitPath} ({current.Count} methods).");
            if (diffPath is null)
                return 0;
        }

        if (diffPath is not null)
        {
            return comparisonBaseline is null
                ? 2
                : Compare(
                    comparisonBaseline.Methods,
                    current,
                    maxExamples,
                    structuralDiffDirectory);
        }
        
        return 0;
    }

    internal static BaselineArtifact? LoadBaseline(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Render A/B baseline not found: {path}");
            return null;
        }
        
        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<BaselineArtifact>(
                json,
                new JsonSerializerOptions
                {
                    RespectRequiredConstructorParameters = true,
                });
            if (parsed is null)
                throw new JsonException("baseline is empty");
            if (parsed.Version != BaselineVersion)
            {
                throw new JsonException(
                    $"unsupported baseline version {parsed.Version}; expected {BaselineVersion}");
            }
            if (parsed.Methods is null)
                throw new JsonException("baseline methods are missing");
            if (parsed.Methods.Any(static pair =>
                    pair.Value.SourceDocument?.Source is null
                    || !MatchesStructuralProjection(
                        pair.Value.Body,
                        pair.Value.SourceDocument.Text.Trim())))
            {
                throw new JsonException(
                    "baseline methods must carry matching product structural documents with physical method provenance");
            }

            return parsed with
            {
                Methods = new Dictionary<string, BaselineMethod>(
                    parsed.Methods,
                    StringComparer.Ordinal),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Failed to load Render A/B baseline {path}: {ex.Message}. "
                + "Regenerate it with --emit-render-ab.");
            return null;
        }
    }

    static Dictionary<string, RenderedMethod> CollectRenders(
        IReadOnlyList<string> assemblies,
        int methodCap,
        int? workers,
        bool sequential,
        IReadOnlyDictionary<string, BaselineMethod>? comparisonBaseline,
        bool captureAllStructuralDocuments)
    {
        var renders = new ConcurrentDictionary<string, RenderedMethod>(StringComparer.Ordinal);
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
                DecompilerResult projection;
                try
                {
                    // PrintRaised runs IrPasses.Default itself — a preceding
                    // IrPasses.Run here double-piped every method, so the
                    // sensor measured a second-run pipeline the product never
                    // ships (the double run folded goto-region diamonds the
                    // single run leaves raw — found via slice F1 scoping).
                    projection = RenderProjection(source, function);
                }
                catch
                {
                    // Ignore compilation crashes in A/B
                    return;
                }

                var rendered = projection.Output;
                if (rendered is not null)
                {
                    string body = rendered.Trim();
                    string signature = CorpusMethodIdentity.SignatureText(function.Signature);
                    string key = $"{portablePath}!{typeName}::{methodName}{signature}";
                    bool captureStructuralDocument =
                        captureAllStructuralDocuments
                        || (comparisonBaseline is not null
                            && comparisonBaseline.TryGetValue(key, out var before)
                            && !StringComparer.Ordinal.Equals(before.Body, body));
                    AnnotatedSourceDocument? structuralDocument = null;
                    string? documentFailure = null;
                    if (captureStructuralDocument)
                    {
                        var documentProjection = ResearchViews.ProjectMember(
                            new ResearchViews.MemberProjectionRequest(
                                source,
                                typeName,
                                methodName,
                                Registry: s_emptyFactRegistry,
                                MethodToken: MetadataTokens.GetToken(item.MethodHandle),
                                SourceDocument: true));
                        structuralDocument = CreateStructuralDocument(
                            body,
                            documentProjection.SourceDocument,
                            documentProjection.SourceDocumentFailure,
                            out documentFailure);
                    }

                    renders.TryAdd(key, new RenderedMethod(
                        typeName,
                        methodName,
                        signature,
                        assemblyPath,
                        portablePath,
                        body,
                        ValidityCheck.MethodShellContext.Create(
                            function,
                            projection.RequiresUnsafeBodyModifier),
                        PrecomputedSemanticContext: null,
                        SourceDocument: structuralDocument,
                        SourceDocumentFailure: documentFailure));
                }
            });
        }
        return renders.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                      .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    internal static string? Render(MetadataSource source, IrFunction function)
        => RenderProjection(source, function).Output;

    static DecompilerResult RenderProjection(MetadataSource source, IrFunction function)
        => CSharpPrinter.PrintRaised(
            function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);

    static string DocumentFailure(DecompilerResult? failure)
        => failure is null
            ? "product annotated-source document was unavailable"
            : string.Join(
                "; ",
                failure.Diagnostics.Select(static diagnostic => diagnostic.ToString()));

    static AnnotatedSourceDocument? CreateStructuralDocument(
        string body,
        AnnotatedSourceDocument? sourceDocument,
        DecompilerResult? sourceDocumentFailure,
        out string? failure)
    {
        if (sourceDocument is null)
        {
            failure = DocumentFailure(sourceDocumentFailure);
            return null;
        }

        try
        {
            var structuralDocument = CSharpStructuralDiffDocument
                .Create(sourceDocument, sourceDocument)
                .Before;
            if (!MatchesStructuralProjection(
                    body,
                    structuralDocument.Text.Trim()))
            {
                failure =
                    "product structural projection does not match the Render A/B body";
                return null;
            }

            failure = null;
            return structuralDocument;
        }
        catch (Exception ex) when (ex is
            ArgumentException
            or InvalidOperationException
            or NotSupportedException)
        {
            failure = ex.Message;
            return null;
        }
    }

    internal static BaselineArtifact CreateBaseline(
        Dictionary<string, RenderedMethod> renders)
        => new(
            BaselineVersion,
            renders.ToDictionary(
                kv => kv.Key,
                kv => CreateBaselineMethod(kv.Key, kv.Value),
                StringComparer.Ordinal));

    static BaselineMethod CreateBaselineMethod(
        string key,
        RenderedMethod rendered)
    {
        if (rendered.SourceDocument?.Source is null)
        {
            throw new InvalidOperationException(
                $"{key}: {rendered.SourceDocumentFailure
                    ?? "product annotated-source document with physical method provenance was unavailable"}");
        }

        if (!MatchesStructuralProjection(
                rendered.Body,
                rendered.SourceDocument.Text.Trim()))
        {
            throw new InvalidOperationException(
                $"{key}: product structural projection does not match the Render A/B body");
        }

        return new BaselineMethod(
            rendered.Body,
            rendered.ShellContext,
            rendered.SourceDocument);
    }

    internal static int Compare(
        Dictionary<string, BaselineMethod> baseline,
        Dictionary<string, RenderedMethod> current,
        int maxExamples,
        string? structuralDiffDirectory = null)
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
        var changes = new List<RenderChange>();
        var semanticRegressions = new List<(string Key, string Before, string After, ValidityCheck.RenderedBodyResult BeforeValidity, ValidityCheck.RenderedBodyResult AfterValidity)>();
        var references = ValidityCheck.RuntimeReferences();
        var compileOptions = ValidityCheck.CompileOptions();
        var semanticContexts = new Dictionary<string, SemanticContext?>(StringComparer.Ordinal);
        var parseOptionsByAssembly = new Dictionary<string, CSharpParseOptions>(
            StringComparer.Ordinal);

        foreach (var kvp in current)
        {
            total++;
            var sample = kvp.Value;
            if (!baseline.TryGetValue(kvp.Key, out var before))
            {
                added++;
            }
            else if (before.Body != sample.Body)
            {
                changed++;
                var diffClass = Classify(before.Body, sample.Body);
                byClass[diffClass]++;
                if (!semanticContexts.TryGetValue(kvp.Key, out var context))
                {
                    context = sample.PrecomputedSemanticContext ?? BuildSemanticContext(sample);
                    semanticContexts[kvp.Key] = context;
                }
                if (!parseOptionsByAssembly.TryGetValue(
                    sample.AssemblyPath,
                    out var parseOptions))
                {
                    parseOptions =
                        CompilerFeatureOptions.ParseOptions(sample.AssemblyPath);
                    parseOptionsByAssembly[sample.AssemblyPath] = parseOptions;
                }
                var beforeValidity = CheckSemantic(
                    context,
                    before.ShellContext,
                    before.Body,
                    references,
                    parseOptions,
                    compileOptions);
                var afterValidity = CheckSemantic(
                    context,
                    sample.ShellContext,
                    sample.Body,
                    references,
                    parseOptions,
                    compileOptions);
                var transition = SemanticTransitionOf(beforeValidity, afterValidity);
                bySemantic[transition]++;
                if (transition == SemanticTransition.ValidToInvalid)
                    semanticRegressions.Add((kvp.Key, before.Body, sample.Body, beforeValidity, afterValidity));
                changes.Add(new RenderChange(
                    kvp.Key,
                    before,
                    sample,
                    diffClass,
                    transition));
            }
        }

        foreach (var key in baseline.Keys)
        {
            if (!current.ContainsKey(key))
                removed++;
        }

        Console.WriteLine("A: stored baseline; B: current product render.");
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

        var structuralChanges = changes
            .OrderBy(static change => change.Class)
            .ThenBy(static change => change.Key, StringComparer.Ordinal)
            .Select(CreateStructuralChange)
            .ToList();
        int structurallyComplete = structuralChanges.Count(
            static change => change.Document is not null
                && change.Document.ToComparison().IsCorrespondenceComplete);
        int structurallyPartial = structuralChanges.Count(
            static change => change.Document is not null
                && !change.Document.ToComparison().IsCorrespondenceComplete);
        int structurallyUnavailable = structuralChanges.Count(
            static change => change.Document is null);
        if (changed > 0)
        {
            Console.WriteLine(
                "Structural review: "
                + $"complete: {structurallyComplete}, "
                + $"partial: {structurallyPartial}, "
                + $"unavailable: {structurallyUnavailable}");
        }

        bool artifactWriteFailed = structuralDiffDirectory is not null
            && !WriteStructuralArtifacts(
                structuralDiffDirectory,
                structuralChanges);

        if (changed == 0)
        {
            Console.WriteLine("No render changes found.");
            return artifactWriteFailed ? 2 : 0;
        }

        Console.WriteLine("\n==== Selected Changes ====");
        foreach (var change in structuralChanges.Take(maxExamples))
        {
            Console.WriteLine(
                $"\nMethod: {change.Change.Key} "
                + $"[{DiffClassLabel(change.Change.Class)}, "
                + $"{SemanticTransitionLabel(change.Change.Semantic)}]");
            if (change.Document is not null)
            {
                Console.WriteLine(StructuralReview.RenderMarkdown(
                    change.Document.ToComparison()));
            }
            else
            {
                Console.WriteLine(
                    $"Structural review unavailable: {change.Failure}");
                Console.WriteLine("--- Baseline ---");
                Console.WriteLine(change.Change.Before.Body);
                Console.WriteLine("--- Current ---");
                Console.WriteLine(change.Change.Current.Body);
            }
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

        return semanticRegressions.Count > 0
            || structurallyUnavailable > 0
            || artifactWriteFailed
                ? 2
                : 1;
    }

    static StructuralChange CreateStructuralChange(RenderChange change)
    {
        if (change.Before.SourceDocument is null)
        {
            return new StructuralChange(
                change,
                Document: null,
                "baseline: product annotated-source document was unavailable");
        }
        if (change.Current.SourceDocument is null)
        {
            return new StructuralChange(
                change,
                Document: null,
                $"current: {change.Current.SourceDocumentFailure
                    ?? "product annotated-source document was unavailable"}");
        }

        try
        {
            var document = CSharpStructuralDiffDocument.Create(
                change.Before.SourceDocument,
                change.Current.SourceDocument);
            string structuralBefore = document.Before.Text.Trim();
            string structuralAfter = document.After.Text.Trim();
            if (!MatchesStructuralProjection(
                    structuralBefore,
                    change.Before.Body)
                || !MatchesStructuralProjection(
                    structuralAfter,
                    change.Current.Body))
            {
                return new StructuralChange(
                    change,
                    Document: null,
                    "product structural projection does not match the Render A/B bodies");
            }

            return new StructuralChange(change, document, Failure: null);
        }
        catch (Exception ex) when (ex is
            ArgumentException
            or InvalidOperationException
            or NotSupportedException)
        {
            return new StructuralChange(
                change,
                Document: null,
                ex.Message);
        }
    }

    static bool WriteStructuralArtifacts(
        string directory,
        IReadOnlyList<StructuralChange> changes)
    {
        try
        {
            if (Directory.Exists(directory)
                && Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Console.Error.WriteLine(
                    $"Render A/B structural artifact directory is not empty: {directory}");
                return false;
            }

            Directory.CreateDirectory(directory);
            var entries = new List<StructuralArtifactEntry>(changes.Count);
            int artifactIndex = 0;
            foreach (var change in changes)
            {
                string? artifact = null;
                if (change.Document is not null)
                {
                    artifact = $"{++artifactIndex:D4}.structural-diff.json";
                    File.WriteAllText(
                        Path.Combine(directory, artifact),
                        AnnotatedSourceJson.SerializeStructuralDiff(
                            change.Document));
                }

                entries.Add(new StructuralArtifactEntry(
                    change.Change.Key,
                    DiffClassLabel(change.Change.Class),
                    SemanticTransitionLabel(change.Change.Semantic),
                    change.Document?.ToComparison().IsCorrespondenceComplete,
                    artifact,
                    change.Failure));
            }

            File.WriteAllText(
                Path.Combine(directory, "manifest.json"),
                JsonSerializer.Serialize(
                    new StructuralArtifactManifest(
                        StructuralArtifactVersion,
                        entries),
                    new JsonSerializerOptions { WriteIndented = true }));
            HarnessLog.Status(
                $"Wrote {artifactIndex} Render A/B structural diff artifact(s) "
                + $"to {directory}.");
            return true;
        }
        catch (Exception ex) when (ex is
            IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            Console.Error.WriteLine(
                $"Failed to write Render A/B structural artifacts to {directory}: "
                + ex.Message);
            return false;
        }
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
        SemanticContext? context,
        ValidityCheck.MethodShellContext shellContext,
        string body,
        ImmutableArray<MetadataReference> references,
        CSharpParseOptions parseOptions,
        CSharpCompilationOptions compileOptions)
    {
        if (context is null)
        {
            return new ValidityCheck.RenderedBodyResult(
                [new ValidityCheck.ValidityDiagnostic("RENDERABCTX", "could not rebuild current method shell for semantic render A/B")],
                SemanticChecked: false,
                []);
        }

        bool bindSemantics = context.Function.Fidelity == DecompilationFidelity.Full
            && !context.TypeName.Contains('<', StringComparison.Ordinal)
            && !context.MethodName.Contains('<', StringComparison.Ordinal);
        return ValidityCheck.EvaluateRenderedBody(
            context.Function,
            body,
            context.TypeName,
            context.MethodName,
            context.Constraints,
            shellContext,
            context.ProductParameterList,
            references,
            parseOptions,
            compileOptions,
            bindSemantics);
    }

    static SemanticContext? BuildSemanticContext(RenderedMethod sample)
    {
        using var metadata = CorpusMetadata.Create([sample.AssemblyPath]);
        using var source = MetadataSource.Open(sample.AssemblyPath, context: metadata);
        _ = source.ResolveShape(TypeRef.CoreLib("System", "Int32"));
        var constraints = ShellConstraints.Build(source);
        var productSignatures = ValidityCheck.ProductSignatureQueues(source.Pe);

        foreach (var item in IrImporter.GetStableSampleCandidates(source, int.MaxValue))
        {
            var function = item.Build(source);
            string signature = CorpusMethodIdentity.SignatureText(function.Signature);
            string key = $"{sample.PortablePath}!{item.TypeName}::{item.MethodName}{signature}";
            string? productSignatureParameters = ValidityCheck.DequeueProductParameterList(productSignatures, item.TypeName, item.MethodName);
            string? productParameterList = function.Signature.Parameters.Any(p => p.HasDefault)
                ? productSignatureParameters
                : null;

            if (!StringComparer.Ordinal.Equals(key, sample.Key))
                continue;

            try
            {
                if (Render(source, function) is null)
                    return null;
            }
            catch
            {
                return null;
            }

            return new SemanticContext(
                item.TypeName,
                item.MethodName,
                function,
                constraints,
                productParameterList);
        }

        return null;
    }

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

    static bool MatchesStructuralProjection(string body, string structuralText)
    {
        if (StringComparer.Ordinal.Equals(body, structuralText))
            return true;

        var bodyRoot = ParseBody(body);
        var structuralRoot = ParseBody(structuralText);
        return bodyRoot is not null
            && structuralRoot is not null
            && SyntaxFactory.AreEquivalent(bodyRoot, structuralRoot, topLevel: false);
    }

    sealed class ParenStripper : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
            => Visit(node.Expression);
    }

    internal sealed record RenderedMethod(
        string TypeName,
        string MethodName,
        string Signature,
        string AssemblyPath,
        string PortablePath,
        string Body,
        ValidityCheck.MethodShellContext ShellContext,
        SemanticContext? PrecomputedSemanticContext = null,
        AnnotatedSourceDocument? SourceDocument = null,
        string? SourceDocumentFailure = null)
    {
        public string Key => $"{PortablePath}!{TypeName}::{MethodName}{Signature}";
    }

    internal sealed record SemanticContext(
        string TypeName,
        string MethodName,
        IrFunction Function,
        IReadOnlyDictionary<string, Dictionary<string, string>> Constraints,
        string? ProductParameterList);

    internal sealed record BaselineArtifact(
        int Version,
        Dictionary<string, BaselineMethod> Methods);

    internal sealed record BaselineMethod(
        string Body,
        ValidityCheck.MethodShellContext ShellContext,
        AnnotatedSourceDocument? SourceDocument = null);

    sealed record RenderChange(
        string Key,
        BaselineMethod Before,
        RenderedMethod Current,
        DiffClass Class,
        SemanticTransition Semantic);

    sealed record StructuralChange(
        RenderChange Change,
        CSharpStructuralDiffDocument? Document,
        string? Failure);

    sealed record StructuralArtifactManifest(
        int Version,
        IReadOnlyList<StructuralArtifactEntry> Methods);

    sealed record StructuralArtifactEntry(
        string Method,
        string TextChange,
        string SemanticTransition,
        bool? CorrespondenceComplete,
        string? Artifact,
        string? Failure);
}