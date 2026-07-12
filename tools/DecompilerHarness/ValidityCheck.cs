using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The validity check (the fidelity check is <see cref="FidelityCheck"/>;
/// completeness is the <c>--gaps</c> floor). It compiles every decompiled body
/// to turn "valid C# by construction" — which the pipeline guarantees only for
/// crashes and silent-wrong, NOT for the rendered text — into a measured number.
///
/// Each body is wrapped in a method shell carrying its real signature (return
/// type, generic parameters, parameters) so locals, parameters, and type
/// parameters bind; the body is then:
///   1. parsed — a parse error is unambiguously the decompiler's fault;
///   2. checked for statement legality (CS0201) — a bare cast/expression
///      statement parses but is not valid, the localloc/RVA residue family;
///   3. bound against the runtime references — and the diagnostics bucketed by
///      code, with the member/type-visibility codes (the shell cannot see the
///      real class's fields/methods) called out as noise so the genuine
///      decompiler defects (type misuse, constrained-generic refs, ...) stand
///      out.
/// Reported split by fidelity: a Partial method is expected to carry invalid
/// fragments (the diagnosed unsupported bits); a Full method that fails to
/// compile is the real "claimed good but isn't" signal.
/// </summary>
static class ValidityCheck
{
    // Diagnostics that mean "the shell cannot see the real declaring type" —
    // unknown name/member/type/namespace, inaccessibility, overload resolution
    // against unresolved types — never decompiler defects, so they are filtered
    // as noise. Genuine misuse codes (CS0029/CS0266 conversions — the
    // constrained-generic family; CS0165 use-before-assignment; CS0136/CS0128
    // duplicate locals; CS0193 deref of non-pointer; CS1656/CS0131 bad assign)
    // are deliberately KEPT.
    //
    // CS1729 ("'T' does not contain a constructor that takes N arguments") is the
    // constructor analog of CS1501 (the method-arity/visibility code already
    // filtered above): a faithful `new T(args)` against a non-public ctor — every
    // case is an internal/private runtime ctor (Half, DateOnly, RuntimeTypeHandle,
    // ...) that binds inside its own assembly but is invisible through the public
    // reference surface the shell compiles against.
    // CS0704 ("cannot do member lookup in 'T' because it is a type parameter") is
    // the constraint-stripping analog: the shell declares each generic parameter
    // bare, so a faithful static-abstract-interface call through a type parameter
    // (`T.IsNegative(value)`, the C# spelling of `constrained. T; call`) cannot
    // bind without the real `where T : INumberBase<T>` constraint — every case is
    // an interface-constrained type parameter the external shell cannot see. The
    // call IS the correct spelling (it round-trips to the same constrained call);
    // the diagnostic is an artifact of the constraint-free shell.
    internal static readonly HashSet<string> BindingNoise =
    [
        "CS0103", "CS0117", "CS1061", "CS0246", "CS0234", "CS0122",
        "CS0119", "CS1955", "CS0021", "CS0070", "CS0118", "CS1501",
        "CS1502", "CS1503", "CS7036", "CS1929", "CS1928", "CS0411",
        "CS1929", "CS0428", "CS1955", "CS1729", "CS0704",
    ];

    internal const string StructuredEvidenceMissId = "VLD0001";

    static readonly HashSet<string> ShellTypeDiagnosticIds =
    [
        "CS0019", "CS0023", "CS0029", "CS0030", "CS0266",
        "CS1540", "CS1605", "CS8121", "CS8129",
    ];

    public sealed record ValidityDiagnostic(string Id, string Message);

    public sealed record MethodResult(
        string TypeName,
        string MethodName,
        string Signature,
        bool IsFull,
        ImmutableArray<ValidityDiagnostic> MalformedDiagnostics,
        bool SemanticChecked,
        ImmutableArray<ValidityDiagnostic> SemanticDiagnostics)
    {
        public string Id => $"{TypeName}::{MethodName}";
        public bool IsMalformed => MalformedDiagnostics.Length > 0;
        public bool HasSemanticDefect => SemanticDiagnostics.Length > 0;
    }

    internal sealed record RenderedBodyResult(
        ImmutableArray<ValidityDiagnostic> MalformedDiagnostics,
        bool SemanticChecked,
        ImmutableArray<ValidityDiagnostic> SemanticDiagnostics)
    {
        public bool IsMalformed => MalformedDiagnostics.Length > 0;
        public bool HasSemanticDefect => SemanticDiagnostics.Length > 0;
        public bool IsValid => !IsMalformed && SemanticChecked && !HasSemanticDefect;
    }

    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples, string? emitDefectsPath = null, string? diffDefectsPath = null, bool lowered = false)
    {
        var results = Evaluate(assemblies, cap, lowered);
        int total = results.Count;
        int fullTotal = results.Count(r => r.IsFull);
        int partialTotal = total - fullTotal;
        int fullMalformed = results.Count(r => r.IsFull && r.IsMalformed);
        int partialMalformed = results.Count(r => !r.IsFull && r.IsMalformed);
        int semanticEligible = results.Count(r => r.IsFull && !r.IsMalformed);
        int semChecked = results.Count(r => r.SemanticChecked);
        int semDefect = results.Count(r => r.HasSemanticDefect);
        var defectCodes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var diagnostic in results.SelectMany(r => r.SemanticDiagnostics))
            defectCodes[diagnostic.Id] = defectCodes.GetValueOrDefault(diagnostic.Id) + 1;
        var malformedExamples = results
            .Where(r => r.IsFull && r.IsMalformed)
            .Take(maxExamples)
            .Select(r => $"{r.Id}\n    {r.MalformedDiagnostics[0].Id}: {r.MalformedDiagnostics[0].Message}")
            .ToList();
        var defectExamples = results
            .Where(r => r.HasSemanticDefect)
            .Take(maxExamples)
            .Select(r => $"{r.Id}\n    {r.SemanticDiagnostics[0].Id}: {r.SemanticDiagnostics[0].Message}")
            .ToList();

        // When emitting or diffing, record the error-code set per Full method so a
        // before/after run can be compared method-by-method (which methods gained
        // or lost a given code) — the differential a raw aggregate count hides.
        var methodDefects = emitDefectsPath is not null || diffDefectsPath is not null
            ? new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal)
            : null;
        if (methodDefects is not null)
        {
            foreach (var result in results.Where(r => r.IsFull))
            {
                if (result.IsMalformed)
                    Record(methodDefects, result.Id, result.MalformedDiagnostics.Select(d => d.Id));
                else if (result.SemanticChecked)
                    Record(methodDefects, result.Id, result.SemanticDiagnostics.Select(d => d.Id));
            }
        }

        Report(total, fullTotal, partialTotal, fullMalformed, partialMalformed,
            semanticEligible, semChecked, cap, semDefect, defectCodes, malformedExamples, defectExamples);

        if (methodDefects is not null && emitDefectsPath is not null)
            EmitDefects(emitDefectsPath, methodDefects);
        if (methodDefects is not null && diffDefectsPath is not null)
            DiffDefects(diffDefectsPath, methodDefects);
        return 0;
    }

    internal static IReadOnlyList<MethodResult> Evaluate(string assemblyPath, int cap = int.MaxValue, bool lowered = false, bool importSiblingBodies = false, int? workers = null, bool sequential = false)
        => Evaluate([assemblyPath], cap, lowered, importSiblingBodies, workers, sequential);

    internal static IReadOnlyList<MethodResult> Evaluate(IReadOnlyList<string> assemblies, int cap = int.MaxValue, bool lowered = false, bool importSiblingBodies = false, int? workers = null, bool sequential = false)
    {
        var references = RuntimeReferences();
        var parseOptions = ParseOptions();
        var compileOptions = CompileOptions();

        int semChecked = 0;
        var results = new ConcurrentBag<MethodResult>();
        using var metadata = CorpusMetadata.Create(assemblies);
        var options = new ParallelOptions { MaxDegreeOfParallelism = sequential ? 1 : (workers ?? Math.Max(1, Environment.ProcessorCount - 2)) };

        foreach (var path in assemblies)
        {
            MetadataSource source;
            try { source = MetadataSource.Open(path, context: metadata); }
            catch { continue; }
            using (source)
            {
                // Pre-warm type maps.
                _ = source.ResolveShape(TypeRef.CoreLib("System", "Int32"));

                var productSignatures = ProductSignatureQueues(source.Pe);
                // Reconstruct generic-parameter `where` clauses so the shell binds
                // constrained generic calls the runtime accepts (no phantom CS0314).
                var constraints = ShellConstraints.Build(source);
                
                var candidates = new List<(string TypeName, string MethodName, IrFunction Function, string? ProductParameterList)>();
                foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
                {
                    // Compiler-generated types/members (anonymous types, closures,
                    // local functions) carry metadata names that aren't valid C#
                    // identifiers (`<>f__`, `<This>j__TPar`); they are not
                    // user-facing decompile targets and would only pollute the
                    // shell with identifier-syntax errors. Skip them.
                    if (typeName.Contains('<') || methodName.Contains('<'))
                        continue;

                    string? productSignatureParameters = DequeueProductParameterList(productSignatures, typeName, methodName);
                    string? productParameterList = function.Signature.Parameters.Any(p => p.HasDefault)
                        ? productSignatureParameters
                        : null;

                    candidates.Add((typeName, methodName, function, productParameterList));
                }

                var evaluationTargets = candidates;

                Parallel.ForEach(evaluationTargets, options, item =>
                {
                    var (typeName, methodName, function, productParameterList) = item;

                    Func<MethodRef, IrFunction?>? importMethodBody = importSiblingBodies
                        ? method => IrImporter.Import(source, method)
                        : null;
                    var rendered = (lowered
                        ? CSharpPrinter.PrintLowered(function, importMethodBody)
                        : CSharpPrinter.PrintRaised(function, importMethodBody)).Output;
                    if (rendered is null)
                        return;
                    bool full = function.Fidelity == DecompilationFidelity.Full;

                    var syntaxOnly = EvaluateRenderedBody(
                        function,
                        rendered,
                        typeName,
                        methodName,
                        constraints,
                        productParameterList,
                        references,
                        parseOptions,
                        compileOptions,
                        bindSemantics: false);

                    if (syntaxOnly.IsMalformed)
                    {
                        results.Add(new MethodResult(typeName, methodName, CorpusMethodIdentity.SignatureText(function.Signature), full, syntaxOnly.MalformedDiagnostics, SemanticChecked: false, []));
                        return; // parallel return
                    }

                    // Bind only Full, syntactically-valid methods (the set that
                    // claims to be good) up to the cap — binding is the slow part.
                    // To maintain deterministic sets while processing in parallel, we allow early evaluation
                    // threads to consume the cap. This is an accepted heuristic over exact sequential metadata order
                    // for the validity subset.
                    if (!full || Interlocked.Increment(ref semChecked) > cap)
                    {
                        if (full) Interlocked.Decrement(ref semChecked);
                        results.Add(new MethodResult(typeName, methodName, CorpusMethodIdentity.SignatureText(function.Signature), full, [], SemanticChecked: false, []));
                        return; // parallel return
                    }
                    var semantic = EvaluateRenderedBody(
                        function,
                        rendered,
                        typeName,
                        methodName,
                        constraints,
                        productParameterList,
                        references,
                        parseOptions,
                        compileOptions,
                        bindSemantics: true);
                    results.Add(new MethodResult(typeName, methodName, CorpusMethodIdentity.SignatureText(function.Signature), full, [], SemanticChecked: true, semantic.SemanticDiagnostics));
                });
            }
        }
        return results.OrderBy(r => r.TypeName, StringComparer.Ordinal)
            .ThenBy(r => r.MethodName, StringComparer.Ordinal)
            .ThenBy(r => r.Signature, StringComparer.Ordinal)
            .ToList();
    }

    internal static CSharpParseOptions ParseOptions()
        => new(LanguageVersion.Preview);

    internal static CSharpCompilationOptions CompileOptions()
        => new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Disable)
            .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>())
            // Import ALL members of the referenced runtime assemblies, including
            // private ones. When a decompiled body legitimately calls a non-public
            // overload of the target assembly (e.g. the internal `Span<T>(ref T, int)`
            // ctor, or the PRIVATE generic helper `Enum.ToString<TUnderlying, TStorage>`
            // that the public `Enum.ToString()` dispatches to), the external shell
            // cannot access it; without the member imported, Roslyn never sees the
            // intended overload and mis-binds to a public sibling, reporting a
            // misleading diagnostic that is purely a shell artifact — CS1615/CS1620
            // (wrong ref/out keyword) for an internal ctor, or CS0308 ("the non-generic
            // method 'Enum.ToString()' cannot be used with type arguments") for a
            // private generic overload, where the faithful `Enum.ToString<sbyte, byte>`
            // call binds to the public non-generic sibling. Importing All lets Roslyn
            // recognize the intended-but-inaccessible member (reporting CS0122, already
            // filtered as visibility noise) instead of these phantom binding errors;
            // the recovered call is correct and round-trips to the same member.
            .WithMetadataImportOptions(MetadataImportOptions.All);

    internal static RenderedBodyResult EvaluateRenderedBody(
        IrFunction function,
        string body,
        string typeName,
        string methodName,
        IReadOnlyDictionary<string, Dictionary<string, string>> constraints,
        string? productParameterList,
        ImmutableArray<MetadataReference> references,
        CSharpParseOptions parseOptions,
        CSharpCompilationOptions compileOptions,
        bool bindSemantics)
    {
        string shell = Shell(function, body, typeName, methodName, constraints, productParameterList);
        var tree = CSharpSyntaxTree.ParseText(shell, parseOptions);
        var malformed = ImmutableArray.CreateBuilder<ValidityDiagnostic>();
        malformed.AddRange(SignatureDefaultDiagnostics(function, productParameterList));
        malformed.AddRange(tree.GetDiagnostics().Where(IsError)
            .Select(d => new ValidityDiagnostic(d.Id, d.GetMessage())));
        malformed.AddRange(IllegalStatements(tree)
            .Select(s => new ValidityDiagnostic("CS0201", "illegal statement: " + s.ToString().Trim())));

        if (malformed.Count > 0 || !bindSemantics)
            return new RenderedBodyResult(malformed.ToImmutable(), SemanticChecked: false, []);

        var compilation = CSharpCompilation.Create("check", [tree], references, compileOptions);
        var semanticModel = compilation.GetSemanticModel(tree);
        var defects = ClassifySemanticDiagnostics(
            compilation.GetDiagnostics(),
            tree,
            function,
            semanticModel);
        return new RenderedBodyResult([], SemanticChecked: true, defects);
    }

    internal static ImmutableArray<ValidityDiagnostic> ClassifySemanticDiagnostics(
        IEnumerable<Diagnostic> diagnostics,
        SyntaxTree tree,
        IrFunction function,
        SemanticModel semanticModel)
    {
        var defects = ImmutableArray.CreateBuilder<ValidityDiagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            if (!IsError(diagnostic) || BindingNoise.Contains(diagnostic.Id))
                continue;

            var shellArtifact = ClassifyShellArtifact(diagnostic, tree, function, semanticModel);
            if (shellArtifact == EvidenceDisposition.Filter)
                continue;

            var staticTypeCollision = ClassifySimpleNameStaticTypeCollision(diagnostic, tree, function);
            if (staticTypeCollision == EvidenceDisposition.Filter)
                continue;

            if (IsGenericArityCollisionNoise(diagnostic, tree, function)
                || IsDeclaringTypeStaticPropertyCtorAssignmentNoise(
                    diagnostic,
                    tree,
                    function,
                    semanticModel))
            {
                continue;
            }

            defects.Add(new ValidityDiagnostic(diagnostic.Id, diagnostic.GetMessage()));
            if (shellArtifact == EvidenceDisposition.Unextracted
                || staticTypeCollision == EvidenceDisposition.Unextracted)
            {
                defects.Add(new ValidityDiagnostic(
                    StructuredEvidenceMissId,
                    $"structured validity evidence unavailable for {diagnostic.Id}"));
            }
        }

        return defects.ToImmutable();
    }

    static void Record(Dictionary<string, SortedSet<string>> map, string method, IEnumerable<string> codes)
    {
        if (!map.TryGetValue(method, out var set))
            map[method] = set = new SortedSet<string>(StringComparer.Ordinal);
        set.UnionWith(codes);
    }

    /// <summary>Writes one line per defective Full method: <c>Type::Method\tcode,code,…</c>.</summary>
    static void EmitDefects(string path, Dictionary<string, SortedSet<string>> map)
    {
        using var writer = new StreamWriter(path);
        foreach (var (method, codes) in map.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            writer.WriteLine($"{method}\t{string.Join(",", codes)}");
        Console.WriteLine();
        HarnessLog.Status($"Wrote per-method defects for {map.Count} methods to {path}");
    }

    /// <summary>
    /// Compares the current per-method defect map against a previously emitted
    /// baseline and prints, per code, the methods that GAINED it (regressions) and
    /// LOST it (improvements) — the differential a raw count cannot show.
    /// </summary>
    static void DiffDefects(string baselinePath, Dictionary<string, SortedSet<string>> current)
    {
        var baseline = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(baselinePath))
        {
            int tab = line.IndexOf('\t');
            if (tab < 0)
                continue;
            baseline[line[..tab]] = new SortedSet<string>(line[(tab + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        }

        var gained = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);  // code -> methods
        var lost = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        // Only methods checked in BOTH runs are comparable; a method present in one
        // file only was skipped by the cap there, and including it would invent a
        // spurious regression/improvement (the cap-boundary artifact).
        var comparable = current.Keys.Intersect(baseline.Keys).ToList();
        int onlyCurrent = current.Count - comparable.Count;
        int onlyBaseline = baseline.Count - comparable.Count;
        foreach (var method in comparable)
        {
            var now = current[method];
            var was = baseline[method];
            foreach (var code in now.Except(was))
                (gained.TryGetValue(code, out var g) ? g : gained[code] = []).Add(method);
            foreach (var code in was.Except(now))
                (lost.TryGetValue(code, out var l) ? l : lost[code] = []).Add(method);
        }

        Console.WriteLine();
        Console.WriteLine($"DEFECT DIFF vs {baselinePath} ({comparable.Count} methods checked in both; {onlyCurrent} only-current, {onlyBaseline} only-baseline excluded)");
        PrintDiffSide("REGRESSED (method gained the code)", gained);
        PrintDiffSide("IMPROVED (method lost the code)", lost);
    }

    static void PrintDiffSide(string title, SortedDictionary<string, List<string>> byCode)
    {
        Console.WriteLine();
        Console.WriteLine(title + ":");
        if (byCode.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }
        foreach (var (code, methods) in byCode.OrderByDescending(kv => kv.Value.Count))
        {
            Console.WriteLine($"  {code}: {methods.Count}");
            foreach (var method in methods.OrderBy(m => m, StringComparer.Ordinal))
                Console.WriteLine($"      {method}");
        }
    }

    static void Report(
        int total, int fullTotal, int partialTotal, int fullMalformed, int partialMalformed,
        int semanticEligible, int semChecked, int cap, int semDefect, SortedDictionary<string, int> defectCodes,
        List<string> malformedExamples, List<string> defectExamples)
    {
        string Pct(int n, int d) => d == 0 ? "0" : $"{100.0 * n / d:F2}%";
        string capText = cap == int.MaxValue ? "all" : cap.ToString();
        Console.WriteLine($"COMPILE-CHECK over {total} rendered methods ({fullTotal} Full, {partialTotal} Partial)");
        Console.WriteLine();
        Console.WriteLine("Syntactic validity (parse + statement legality — false-positive-free):");
        Console.WriteLine($"  Full malformed   : {fullMalformed} ({Pct(fullMalformed, fullTotal)} of Full) — the \"claimed good but won't parse\" set");
        Console.WriteLine($"  Partial malformed: {partialMalformed} ({Pct(partialMalformed, partialTotal)} of Partial) — expected: the diagnosed unsupported bits");
        Console.WriteLine();
        Console.WriteLine($"Semantic binding (Full + syntactically-valid): compiled {semChecked} of {semanticEligible} eligible methods (compile-cap {capText})");
        if (semChecked < semanticEligible)
            Console.WriteLine("  NOTE: semantic findings are per-sample, not corpus-wide; raise --compile-cap or use --compile-cap all for an exhaustive binding sweep.");
        Console.WriteLine($"  with a non-binding-noise error: {semDefect} ({Pct(semDefect, semChecked)})");
        if (defectCodes.Count > 0)
        {
            Console.WriteLine("  by code (binding-visibility codes already filtered out):");
            foreach (var (code, n) in defectCodes.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"    {code}: {n}");
        }
        if (malformedExamples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Malformed examples (Full):");
            foreach (var e in malformedExamples)
                Console.WriteLine($"  {e}");
        }
        if (defectExamples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Semantic-defect examples (Full):");
            foreach (var e in defectExamples)
                Console.WriteLine($"  {e}");
        }
    }

    internal static Dictionary<string, Queue<string>> ProductSignatureQueues(PEReader pe)
    {
        var result = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
        ApiSurface surface;
        try { surface = ApiSurfaceExtractor.Extract(pe, includeAll: true); }
        catch { return result; }
        foreach (var type in surface.Types)
            foreach (var member in type.Members)
            {
                if (member.Signature is not { Length: > 0 } signature)
                    continue;
                string key = $"{type.FullName}::{member.Name}";
                if (!result.TryGetValue(key, out var queue))
                    result[key] = queue = new Queue<string>();
                queue.Enqueue(signature);
            }
        return result;
    }

    internal static string? DequeueProductParameterList(Dictionary<string, Queue<string>> signatures, string typeName, string methodName)
    {
        string key = $"{typeName}::{methodName}";
        if (!signatures.TryGetValue(key, out var queue) || queue.Count == 0)
            return null;
        return ExtractParameterList(queue.Dequeue());
    }

    static string? ExtractParameterList(string signature)
    {
        int close = signature.LastIndexOf(')');
        if (close < 0)
            return null;
        int depth = 0;
        for (int i = close; i >= 0; i--)
        {
            char c = signature[i];
            if (c == ')')
                depth++;
            else if (c == '(' && --depth == 0)
                return signature[(i + 1)..close];
        }
        return null;
    }

    static IEnumerable<ValidityDiagnostic> SignatureDefaultDiagnostics(IrFunction function, string? productParameterList)
    {
        if (productParameterList is null)
            yield break;
        var productParameters = SplitParameters(productParameterList);
        if (productParameters.Count != function.Signature.Parameters.Length)
            yield break;
        for (int i = 0; i < productParameters.Count; i++)
        {
            if (function.Signature.Parameters[i].HasDefault
                && !productParameters[i].Contains("=", StringComparison.Ordinal))
            {
                yield return new ValidityDiagnostic(
                    "SIGDEFAULT",
                    $"optional parameter '{function.Signature.Parameters[i].Name}' rendered without a default value");
            }
        }
    }

    static IReadOnlyList<string> SplitParameters(string parameterList)
    {
        if (string.IsNullOrWhiteSpace(parameterList))
            return [];
        var result = new List<string>();
        int start = 0;
        int angle = 0, paren = 0, bracket = 0;
        bool inString = false, inChar = false, escape = false;
        for (int i = 0; i < parameterList.Length; i++)
        {
            char c = parameterList[i];
            if (escape)
            {
                escape = false;
                continue;
            }
            if (c == '\\' && (inString || inChar))
            {
                escape = true;
                continue;
            }
            if (c == '"' && !inChar)
            {
                inString = !inString;
                continue;
            }
            if (c == '\'' && !inString)
            {
                inChar = !inChar;
                continue;
            }
            if (inString || inChar)
                continue;
            switch (c)
            {
                case '<': angle++; break;
                case '>': if (angle > 0) angle--; break;
                case '(': paren++; break;
                case ')': if (paren > 0) paren--; break;
                case '[': bracket++; break;
                case ']': if (bracket > 0) bracket--; break;
                case ',' when angle == 0 && paren == 0 && bracket == 0:
                    result.Add(parameterList[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }
        result.Add(parameterList[start..].Trim());
        return result;
    }

    /// <summary>Wraps a body in a generic instance method on a class so locals, params, type params, and `this` all bind; member access on `this` becomes filtered binding noise.</summary>
    internal static string Shell(IrFunction function, string body, string typeName, string methodName,
        IReadOnlyDictionary<string, Dictionary<string, string>> constraints, string? productParameterList = null)
    {
        var generics = GenericParameterNames(function);
        string genericList = generics.Count > 0 ? "<" + string.Join(", ", generics) + ">" : "";
        string whereClauses = generics.Count > 0
            ? ShellConstraints.Clauses(constraints, typeName, methodName, function, generics)
            : "";
        string returnType = TypeText(function.Signature.ReturnType);
        string parameters = productParameterList ?? string.Join(", ", function.Signature.Parameters.Select(ParameterText));
        // A decompiled async method renders its `await` expressions faithfully, but
        // the original `async` modifier lives in metadata (the state machine), not in
        // the body. The shell must restore it or every awaiting body trips CS4032
        // ("'await' can only be used within an async method") — a shell artifact, not
        // a decompiler defect. The signature return type is already the awaitable
        // (Task/Task<T>/ValueTask, or void for async void), so `async` binds cleanly.
        // `unsafe` and `async` are mutually exclusive here: the blanket `unsafe`
        // modifier puts the whole body in an unsafe context, where `await` is illegal
        // (CS4004) — so an awaiting body takes `async` INSTEAD of `unsafe`. Async
        // method bodies do not carry pointer operations across awaits, so dropping the
        // unsafe context costs nothing.
        string modifier = function.Descendants.OfType<AwaitExpression>().Any() ? "async" : "unsafe";
        return $$"""
            #pragma warning disable
            using System;
            using System.Buffers;
            using System.Collections;
            using System.Collections.Generic;
            using System.Globalization;
            using System.Linq;
            using System.Numerics;
            using System.Reflection;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using System.Runtime.Intrinsics;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            class __Shell
            {
                {{modifier}} {{returnType}} __M{{genericList}}({{parameters}}){{whereClauses}}
                {
            {{body}}
                }
            }
            """;
    }

    static string ParameterText(Parameter parameter)
        => parameter.Type.Kind == TypeRefKind.ByRef
            ? $"ref {TypeText(parameter.Type.ElementType!)} {CSharpNaming.EscapeIdentifier(parameter.Name)}"
            : $"{TypeText(parameter.Type)} {CSharpNaming.EscapeIdentifier(parameter.Name)}";

    static string TypeText(TypeRef type)
    {
        // A bare definition with no namespace, or any shape the printer renders
        // by simple name, just needs to parse — unresolved names bucket as
        // binding noise. ByRef/Pointer in odd positions degrade to the element.
        string text = type.ToDisplayString();
        return string.IsNullOrWhiteSpace(text) || text.Contains('!') ? "object" : text;
    }

    static List<string> GenericParameterNames(IrFunction function)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        void Visit(TypeRef? type)
        {
            if (type is null)
                return;
            if (type.Kind is TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter
                && type.GenericParameterName.Length > 0)
            {
                names.Add(type.GenericParameterName);
            }
            Visit(type.ElementType);
            foreach (var argument in type.TypeArguments)
                Visit(argument);
        }

        Visit(function.Signature.ReturnType);
        foreach (var parameter in function.Signature.Parameters)
            Visit(parameter.Type);
        foreach (var node in function.Descendants)
        {
            foreach (var type in node.DirectTypes)
                Visit(type);
            if (node is IrExpression expression)
                Visit(expression.ResultType);
        }
        return names.ToList();
    }

    /// <summary>Expression statements whose expression is not a legal statement (the CS0201 rule), checkable without binding.</summary>
    internal static List<ExpressionStatementSyntax> IllegalStatements(SyntaxTree tree)
        => tree.GetRoot().DescendantNodes().OfType<ExpressionStatementSyntax>()
            .Where(s => !IsLegalStatementExpression(s.Expression))
            .ToList();

    static bool IsLegalStatementExpression(ExpressionSyntax expression) => expression switch
    {
        InvocationExpressionSyntax => true,
        ObjectCreationExpressionSyntax => true,
        AssignmentExpressionSyntax => true,
        AwaitExpressionSyntax => true,
        PostfixUnaryExpressionSyntax => true,
        PrefixUnaryExpressionSyntax p =>
            p.IsKind(SyntaxKind.PreIncrementExpression) || p.IsKind(SyntaxKind.PreDecrementExpression),
        ConditionalAccessExpressionSyntax c => IsLegalStatementExpression(c.WhenNotNull),
        ParenthesizedExpressionSyntax => false,
        _ => false,
    };

    internal static bool IsError(Diagnostic diagnostic) => diagnostic.Severity == DiagnosticSeverity.Error;

    /// <summary>
    /// The body is wrapped in an instance method on a synthetic <c>__Shell</c>
    /// class. Diagnostics caused by Roslyn binding <c>this</c> to that class, or
    /// by protected access that would be legal inside the real declaring type,
    /// are shell artifacts rather than decompiler defects.
    /// </summary>
    static EvidenceDisposition ClassifyShellArtifact(
        Diagnostic diagnostic,
        SyntaxTree tree,
        IrFunction function,
        SemanticModel semanticModel)
    {
        if (!ShellTypeDiagnosticIds.Contains(diagnostic.Id))
            return EvidenceDisposition.Keep;
        if (diagnostic.Location.SourceTree != tree)
            return EvidenceDisposition.Unextracted;

        var node = tree.GetRoot().FindNode(
            diagnostic.Location.SourceSpan,
            getInnermostNodeForTie: true);
        if (diagnostic.Id != "CS1540")
            return ClassifySyntheticShellThis(
                diagnostic.Id,
                node,
                function.DeclaringType,
                semanticModel);

        var memberAccess = node.FirstAncestorOrSelf<MemberAccessExpressionSyntax>()
            ?? node.DescendantNodesAndSelf()
                .OfType<MemberAccessExpressionSyntax>()
                .FirstOrDefault(candidate =>
                    diagnostic.Location.SourceSpan.IntersectsWith(candidate.Span));
        if (memberAccess is null)
            return EvidenceDisposition.Unextracted;

        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess.Name);
        var member = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (member is null)
            return EvidenceDisposition.Unextracted;
        return IsProtected(member)
            && ReceiverResolvesToDeclaringType(
                memberAccess.Expression,
                function.DeclaringType,
                semanticModel)
            ? EvidenceDisposition.Filter
            : EvidenceDisposition.Keep;
    }

    static EvidenceDisposition ClassifySyntheticShellThis(
        string diagnosticId,
        SyntaxNode node,
        TypeRef declaringType,
        SemanticModel semanticModel)
    {
        if (diagnosticId == "CS0019")
            return ClassifySyntheticShellBinary(node, declaringType, semanticModel);

        ExpressionSyntax? subject = diagnosticId switch
        {
            "CS0023" => node.FirstAncestorOrSelf<PrefixUnaryExpressionSyntax>()?.Operand,
            "CS0030" => node.FirstAncestorOrSelf<CastExpressionSyntax>()?.Expression,
            "CS8121" => node.FirstAncestorOrSelf<IsPatternExpressionSyntax>()?.Expression,
            "CS8129" => node.FirstAncestorOrSelf<AssignmentExpressionSyntax>()?.Right
                ?? ClosestExpression(node),
            _ => ClosestExpression(node),
        };
        if (subject is null)
            return EvidenceDisposition.Unextracted;

        ITypeSymbol? targetType = diagnosticId switch
        {
            "CS0030" => node.FirstAncestorOrSelf<CastExpressionSyntax>() is { } cast
                ? semanticModel.GetTypeInfo(cast.Type).Type
                : null,
            "CS0029" or "CS0266" => semanticModel.GetTypeInfo(subject).ConvertedType,
            _ => null,
        };
        bool shellArtifact = targetType is null
            ? IsDirectSyntheticShellValue(subject, semanticModel)
            : IsSyntheticShellConversionArtifact(
                subject,
                targetType,
                declaringType,
                semanticModel);
        return shellArtifact
            ? EvidenceDisposition.Filter
            : EvidenceDisposition.Keep;
    }

    static EvidenceDisposition ClassifySyntheticShellBinary(
        SyntaxNode node,
        TypeRef declaringType,
        SemanticModel semanticModel)
    {
        var binary = node.FirstAncestorOrSelf<BinaryExpressionSyntax>();
        if (binary is null)
            return EvidenceDisposition.Unextracted;

        var leftType = semanticModel.GetTypeInfo(binary.Left).Type;
        var rightType = semanticModel.GetTypeInfo(binary.Right).Type;
        bool shellArtifact = IsSyntheticShellValue(binary.Left, semanticModel)
                && (IsSyntheticShellType(leftType)
                    || TypesEquivalentAfterShellReplacement(leftType, rightType, declaringType))
            || IsSyntheticShellValue(binary.Right, semanticModel)
                && (IsSyntheticShellType(rightType)
                    || TypesEquivalentAfterShellReplacement(rightType, leftType, declaringType));
        return shellArtifact
            ? EvidenceDisposition.Filter
            : EvidenceDisposition.Keep;
    }

    static ExpressionSyntax? ClosestExpression(SyntaxNode node)
        => node.FirstAncestorOrSelf<ExpressionSyntax>()
            ?? node.DescendantNodesAndSelf()
                .OfType<ExpressionSyntax>()
                .OrderBy(candidate => candidate.Span.Length)
                .FirstOrDefault();

    static bool IsSyntheticShellValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
        => expression.DescendantNodesAndSelf()
            .OfType<ThisExpressionSyntax>()
            .Any(candidate => IsSyntheticShellThis(candidate, semanticModel))
        && ContainsSyntheticShellType(semanticModel.GetTypeInfo(expression).Type);

    static bool IsDirectSyntheticShellValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
        => expression.DescendantNodesAndSelf()
            .OfType<ThisExpressionSyntax>()
            .Any(candidate => IsSyntheticShellThis(candidate, semanticModel))
        && IsSyntheticShellType(semanticModel.GetTypeInfo(expression).Type);

    static bool IsSyntheticShellConversionArtifact(
        ExpressionSyntax expression,
        ITypeSymbol targetType,
        TypeRef declaringType,
        SemanticModel semanticModel)
    {
        if (!IsSyntheticShellValue(expression, semanticModel))
            return false;

        var sourceType = semanticModel.GetTypeInfo(expression).Type;
        return IsSyntheticShellType(sourceType)
            || TypesEquivalentAfterShellReplacement(sourceType, targetType, declaringType);
    }

    static bool ContainsSyntheticShellType(ITypeSymbol? type)
        => type switch
        {
            INamedTypeSymbol namedType => IsSyntheticShellType(namedType)
                || namedType.TypeArguments.Any(ContainsSyntheticShellType),
            IArrayTypeSymbol arrayType => ContainsSyntheticShellType(arrayType.ElementType),
            IPointerTypeSymbol pointerType => ContainsSyntheticShellType(pointerType.PointedAtType),
            _ => false,
        };

    static bool TypesEquivalentAfterShellReplacement(
        ITypeSymbol? left,
        ITypeSymbol? right,
        TypeRef declaringType)
    {
        if (left is null || right is null)
            return false;
        if (IsSyntheticShellType(left))
            return right is INamedTypeSymbol rightNamed
                && MetadataFullName(rightNamed) == MetadataFullName(declaringType);
        if (IsSyntheticShellType(right))
            return left is INamedTypeSymbol leftNamed
                && MetadataFullName(leftNamed) == MetadataFullName(declaringType);
        if (left is IArrayTypeSymbol leftArray && right is IArrayTypeSymbol rightArray)
        {
            return leftArray.Rank == rightArray.Rank
                && TypesEquivalentAfterShellReplacement(
                    leftArray.ElementType,
                    rightArray.ElementType,
                    declaringType);
        }
        if (left is IPointerTypeSymbol leftPointer && right is IPointerTypeSymbol rightPointer)
        {
            return TypesEquivalentAfterShellReplacement(
                leftPointer.PointedAtType,
                rightPointer.PointedAtType,
                declaringType);
        }
        if (left is INamedTypeSymbol leftNamedType
            && right is INamedTypeSymbol rightNamedType
            && SymbolEqualityComparer.Default.Equals(
                leftNamedType.OriginalDefinition,
                rightNamedType.OriginalDefinition)
            && leftNamedType.TypeArguments.Length == rightNamedType.TypeArguments.Length)
        {
            return leftNamedType.TypeArguments
                .Zip(rightNamedType.TypeArguments)
                .All(pair => TypesEquivalentAfterShellReplacement(
                    pair.First,
                    pair.Second,
                    declaringType));
        }

        return SymbolEqualityComparer.Default.Equals(left, right);
    }

    static bool IsSyntheticShellThis(
        ThisExpressionSyntax expression,
        SemanticModel semanticModel)
        => semanticModel.GetTypeInfo(expression).Type is INamedTypeSymbol type
        && IsSyntheticShellType(type);

    static bool IsSyntheticShellType(INamedTypeSymbol type)
        => type is
        {
            Name: "__Shell",
            ContainingType: null,
            ContainingNamespace.IsGlobalNamespace: true,
        };

    static bool IsSyntheticShellType(ITypeSymbol? type)
        => type is INamedTypeSymbol namedType && IsSyntheticShellType(namedType);

    static bool IsProtected(ISymbol? symbol)
        => symbol?.DeclaredAccessibility is
            Accessibility.Protected or
            Accessibility.ProtectedAndInternal or
            Accessibility.ProtectedOrInternal;

    /// <summary>
    /// CS0305 ("the generic type 'X&lt;T&gt;' requires N type arguments") on a
    /// <em>simple</em> identifier the decompiler wrote with no type-argument list,
    /// where the decompiler's own model holds <em>no</em> generic type of that
    /// simple name. That is the sibling-free-shell collision: a non-generic
    /// local/sibling type, or a member-access receiver, whose bare name happens to
    /// collide with a <c>using</c>-imported generic of the same name (e.g. the
    /// decompiler's own non-generic <c>Comparison</c> IR node vs
    /// <c>System.Comparison&lt;T&gt;</c>, or a <c>Lookup</c> property receiver vs
    /// <c>System.Linq.Lookup&lt;TKey, TElement&gt;</c>). In the real namespace the
    /// local symbol binds and shadows the import; only the sibling-free shell
    /// mis-resolves the bare name to the generic. Filtered like the other binding
    /// noise.
    /// <para>
    /// It is <em>not</em> filtered — so a real defect stays reported — when the model
    /// DOES reference a generic of that simple name. A genuinely mis-rendered generic
    /// can land on an <see cref="IdentifierNameSyntax"/> too (a dropped type-argument
    /// list, e.g. <c>List</c> where <c>List&lt;int&gt;</c> was meant): syntax kind
    /// alone cannot tell that apart from a collision, so the decompiler's type model
    /// is consulted (<see cref="ShellNoise.ReferencesGenericTypeNamed"/>). An explicit
    /// wrong-arity render is a <see cref="GenericNameSyntax"/> and is never matched here.
    /// </para>
    /// </summary>
    internal static bool IsGenericArityCollisionNoise(Diagnostic diagnostic, SyntaxTree tree, IrFunction function)
    {
        if (diagnostic.Id != "CS0305")
            return false;
        if (tree.GetRoot().FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
            is not IdentifierNameSyntax name)
            return false;
        // A bare identifier with a same-named generic in the model could be a real
        // dropped-type-argument render — keep it. Only filter when the model has no
        // generic of that name, i.e. the bare name is genuinely a non-generic type
        // (or non-type) that merely collides with an imported generic.
        return !ShellNoise.ReferencesGenericTypeNamed(function, name.Identifier.ValueText);
    }

    /// <summary>
    /// Static-type misuse diagnostics on a bare simple-name type that the
    /// decompiler model knows as a non-generic local/sibling type. In the real
    /// whole-type output, the current namespace type wins over blanket imports;
    /// only the standalone validity shell can mis-bind it to imported static types
    /// such as <c>System.Convert</c> or <c>System.Environment</c>.
    /// </summary>
    internal static bool IsSimpleNameStaticTypeCollisionNoise(Diagnostic diagnostic, SyntaxTree tree, IrFunction function)
        => ClassifySimpleNameStaticTypeCollision(diagnostic, tree, function)
            == EvidenceDisposition.Filter;

    static EvidenceDisposition ClassifySimpleNameStaticTypeCollision(
        Diagnostic diagnostic,
        SyntaxTree tree,
        IrFunction function)
    {
        if (diagnostic.Id is not ("CS0712" or "CS0721" or "CS0722" or "CS0723"))
            return EvidenceDisposition.Keep;
        if (diagnostic.Location.SourceTree != tree)
            return EvidenceDisposition.Unextracted;

        string? simpleName = SimpleNameAtDiagnosticLocation(diagnostic, tree);
        if (simpleName is null)
            return EvidenceDisposition.Unextracted;

        return ShellNoise.ReferencesNonGenericTypeNamed(function, simpleName)
            && !ShellNoise.ReferencesGenericTypeNamed(function, simpleName)
            ? EvidenceDisposition.Filter
            : EvidenceDisposition.Keep;
    }

    /// <summary>
    /// The shell wraps a static constructor body in <c>__Shell.__M</c>, so a
    /// same-full-name platform type can win over the reconstructed declaring type.
    /// A get-only auto-property assignment backed by an original static backing
    /// field store is legal inside the real declaring type's static constructor;
    /// CS0200 here means the shell resolved the receiver to the colliding external
    /// type instead.
    /// </summary>
    internal static bool IsDeclaringTypeStaticPropertyCtorAssignmentNoise(Diagnostic diagnostic, SyntaxTree tree, IrFunction function, SemanticModel semanticModel)
    {
        if (diagnostic.Id != "CS0200" || function.Name != ".cctor" || diagnostic.Location.SourceTree != tree)
            return false;

        var node = tree.GetRoot().FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        if (node.FirstAncestorOrSelf<MemberAccessExpressionSyntax>() is not { } memberAccess)
            return false;
        if (!IsAssignmentLeft(memberAccess))
            return false;
        if (!ReceiverResolvesToDeclaringType(memberAccess.Expression, function.DeclaringType, semanticModel))
            return false;

        string propertyName = memberAccess.Name.Identifier.ValueText;
        return function.Descendants.OfType<StoreField>().Any(store =>
            !store.HasInstance
            && store.Field.BackingPropertyName == propertyName
            && SameTypeDefinition(store.Field.DeclaringType, function.DeclaringType));
    }

    static bool IsAssignmentLeft(MemberAccessExpressionSyntax memberAccess)
    {
        var assignment = memberAccess.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
        return assignment is not null
            && assignment.Left.Span.Start <= memberAccess.Span.Start
            && memberAccess.Span.End <= assignment.Left.Span.End;
    }

    static bool ReceiverResolvesToDeclaringType(ExpressionSyntax receiver, TypeRef declaringType, SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(receiver);
        ISymbol? symbol = symbolInfo.Symbol
            ?? symbolInfo.CandidateSymbols.OfType<INamedTypeSymbol>().FirstOrDefault();
        if (symbol is IAliasSymbol alias)
            symbol = alias.Target;
        var namedType = symbol as INamedTypeSymbol
            ?? semanticModel.GetTypeInfo(receiver).Type as INamedTypeSymbol;
        if (namedType is null)
            return false;

        return MetadataFullName(namedType) == MetadataFullName(declaringType);
    }

    static string MetadataFullName(INamedTypeSymbol type)
    {
        var names = new Stack<string>();
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
            names.Push(current.Name);
        string nested = string.Join(".", names);
        string ns = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();
        return ns.Length == 0 ? nested : ns + "." + nested;
    }

    static string MetadataFullName(TypeRef type)
    {
        type = type.Kind == TypeRefKind.GenericInstance ? type.ElementType! : type;
        string name = string.Join(".", type.Name.Split('+').Select(ShellNoise.SimpleName));
        return type.Namespace.Length == 0 ? name : type.Namespace + "." + name;
    }

    static bool SameTypeDefinition(TypeRef left, TypeRef right)
    {
        left = left.Kind == TypeRefKind.GenericInstance ? left.ElementType! : left;
        right = right.Kind == TypeRefKind.GenericInstance ? right.ElementType! : right;
        return left.Kind == TypeRefKind.Definition
            && right.Kind == TypeRefKind.Definition
            && left.Assembly == right.Assembly
            && left.Namespace == right.Namespace
            && left.Name == right.Name;
    }

    static string? SimpleNameAtDiagnosticLocation(Diagnostic diagnostic, SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var span = diagnostic.Location.SourceSpan;
        var node = root.FindNode(span, getInnermostNodeForTie: true);
        TypeSyntax? type = diagnostic.Id switch
        {
            "CS0712" => node.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>()?.Type,
            "CS0721" => node.FirstAncestorOrSelf<ParameterSyntax>()?.Type,
            "CS0722" => node.FirstAncestorOrSelf<MethodDeclarationSyntax>()?.ReturnType,
            "CS0723" => node.FirstAncestorOrSelf<VariableDeclarationSyntax>()?.Type,
            _ => null,
        };
        if (type is not null)
            return RightmostSimpleName(type)?.Identifier.ValueText;

        var name = node.FirstAncestorOrSelf<SimpleNameSyntax>()
            ?? node.DescendantNodesAndSelf()
                .OfType<SimpleNameSyntax>()
                .Where(candidate =>
                    span.Start <= candidate.Span.Start
                    && candidate.Span.End <= span.End)
                .OrderBy(candidate => candidate.Span.Start)
                .LastOrDefault();
        return name?.Identifier.ValueText;
    }

    static SimpleNameSyntax? RightmostSimpleName(SyntaxNode node)
        => node.DescendantNodesAndSelf()
            .OfType<SimpleNameSyntax>()
            .OrderBy(candidate => candidate.Span.Start)
            .LastOrDefault();

    enum EvidenceDisposition
    {
        Keep,
        Filter,
        Unextracted,
    }

    internal static ImmutableArray<MetadataReference> RuntimeReferences()
    {
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var path in tpa)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            try { builder.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }
        return builder.ToImmutable();
    }
}
