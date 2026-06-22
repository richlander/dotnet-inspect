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

    public sealed record ValidityDiagnostic(string Id, string Message);

    public sealed record MethodResult(
        string TypeName,
        string MethodName,
        bool IsFull,
        ImmutableArray<ValidityDiagnostic> MalformedDiagnostics,
        bool SemanticChecked,
        ImmutableArray<ValidityDiagnostic> SemanticDiagnostics)
    {
        public string Id => $"{TypeName}::{MethodName}";
        public bool IsMalformed => MalformedDiagnostics.Length > 0;
        public bool HasSemanticDefect => SemanticDiagnostics.Length > 0;
    }

    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples, string? emitDefectsPath = null, string? diffDefectsPath = null, bool lowered = false)
    {
        var results = Evaluate(assemblies, cap, lowered);
        int total = results.Count;
        int fullTotal = results.Count(r => r.IsFull);
        int partialTotal = total - fullTotal;
        int fullMalformed = results.Count(r => r.IsFull && r.IsMalformed);
        int partialMalformed = results.Count(r => !r.IsFull && r.IsMalformed);
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
            semChecked, semDefect, defectCodes, malformedExamples, defectExamples);

        if (methodDefects is not null && emitDefectsPath is not null)
            EmitDefects(emitDefectsPath, methodDefects);
        if (methodDefects is not null && diffDefectsPath is not null)
            DiffDefects(diffDefectsPath, methodDefects);
        return 0;
    }

    internal static IReadOnlyList<MethodResult> Evaluate(string assemblyPath, int cap = int.MaxValue, bool lowered = false)
        => Evaluate([assemblyPath], cap, lowered);

    internal static IReadOnlyList<MethodResult> Evaluate(IReadOnlyList<string> assemblies, int cap = int.MaxValue, bool lowered = false)
    {
        var references = RuntimeReferences();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
            nullableContextOptions: NullableContextOptions.Disable)
            .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>())
            // Import internal members of the referenced runtime assemblies. When a
            // decompiled body legitimately calls an INTERNAL overload of the target
            // assembly (e.g. the internal `Span<T>(ref T, int)` ctor), the external
            // shell cannot access it; without the internal members imported, Roslyn
            // never sees the intended overload and mis-binds to a public sibling,
            // reporting a misleading CS1615/CS1620 (wrong ref/out keyword). Imported,
            // it recognizes the intended-but-inaccessible member and reports CS0122
            // instead — already filtered as visibility noise. The recovered keyword
            // is correct; the diagnostic was an artifact of the external shell.
            .WithMetadataImportOptions(MetadataImportOptions.Internal);

        int semChecked = 0;
        var results = new List<MethodResult>();
        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var path in assemblies)
        {
            MetadataSource source;
            try { source = MetadataSource.Open(path, context: metadata); }
            catch { continue; }
            using (source)
            {
                var productSignatures = ProductSignatureQueues(source.Pe);
                // Reconstruct generic-parameter `where` clauses so the shell binds
                // constrained generic calls the runtime accepts (no phantom CS0314).
                var constraints = ShellConstraints.Build(source);
                foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
                {
                    // Compiler-generated types/members (anonymous types, closures,
                    // local functions) carry metadata names that aren't valid C#
                    // identifiers (`<>f__`, `<This>j__TPar`); they are not
                    // user-facing decompile targets and would only pollute the
                    // shell with identifier-syntax errors. Skip them.
                    if (typeName.Contains('<') || methodName.Contains('<'))
                        continue;
                    var rendered = (lowered ? CSharpPrinter.PrintLowered(function) : CSharpPrinter.PrintRaised(function)).Output;
                    if (rendered is null)
                        continue;
                    bool full = function.Fidelity == DecompilationFidelity.Full;
                    string? productSignatureParameters = DequeueProductParameterList(productSignatures, typeName, methodName);
                    string? productParameterList = function.Signature.Parameters.Any(p => p.HasDefault)
                        ? productSignatureParameters
                        : null;

                    string shell = Shell(function, rendered, typeName, methodName, constraints, productParameterList);
                    var tree = CSharpSyntaxTree.ParseText(shell, parseOptions);
                    var malformed = ImmutableArray.CreateBuilder<ValidityDiagnostic>();
                    malformed.AddRange(SignatureDefaultDiagnostics(function, productParameterList));
                    malformed.AddRange(tree.GetDiagnostics().Where(IsError)
                        .Select(d => new ValidityDiagnostic(d.Id, d.GetMessage())));
                    malformed.AddRange(IllegalStatements(tree)
                        .Select(s => new ValidityDiagnostic("CS0201", "illegal statement: " + s.ToString().Trim())));

                    if (malformed.Count > 0)
                    {
                        results.Add(new MethodResult(typeName, methodName, full, malformed.ToImmutable(), SemanticChecked: false, []));
                        continue;
                    }

                    // Bind only Full, syntactically-valid methods (the set that
                    // claims to be good) up to the cap — binding is the slow part.
                    if (!full || semChecked >= cap)
                    {
                        results.Add(new MethodResult(typeName, methodName, full, [], SemanticChecked: false, []));
                        continue;
                    }
                    semChecked++;
                    var compilation = CSharpCompilation.Create("check", [tree], references, compileOptions);
                    var defects = compilation.GetDiagnostics()
                        .Where(IsError)
                        .Where(d => !BindingNoise.Contains(d.Id))
                        .Where(d => !IsShellArtifact(d))
                        .Where(d => !IsGenericArityCollisionNoise(d, tree, function))
                        .Where(d => !IsSimpleNameStaticTypeCollisionNoise(d, tree, function))
                        .Select(d => new ValidityDiagnostic(d.Id, d.GetMessage()))
                        .ToImmutableArray();
                    results.Add(new MethodResult(typeName, methodName, full, [], SemanticChecked: true, defects));
                }
            }
        }
        return results;
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
        Console.WriteLine($"Wrote per-method defects for {map.Count} methods to {path}");
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
        int semChecked, int semDefect, SortedDictionary<string, int> defectCodes,
        List<string> malformedExamples, List<string> defectExamples)
    {
        string Pct(int n, int d) => d == 0 ? "0" : $"{100.0 * n / d:F2}%";
        Console.WriteLine($"COMPILE-CHECK over {total} rendered methods ({fullTotal} Full, {partialTotal} Partial)");
        Console.WriteLine();
        Console.WriteLine("Syntactic validity (parse + statement legality — false-positive-free):");
        Console.WriteLine($"  Full malformed   : {fullMalformed} ({Pct(fullMalformed, fullTotal)} of Full) — the \"claimed good but won't parse\" set");
        Console.WriteLine($"  Partial malformed: {partialMalformed} ({Pct(partialMalformed, partialTotal)} of Partial) — expected: the diagnosed unsupported bits");
        Console.WriteLine();
        Console.WriteLine($"Semantic binding (Full + syntactically-valid, capped at {semChecked} bound):");
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

    static Dictionary<string, Queue<string>> ProductSignatureQueues(PEReader pe)
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

    static string? DequeueProductParameterList(Dictionary<string, Queue<string>> signatures, string typeName, string methodName)
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
    /// class, so the only <c>__Shell</c>-typed expression in scope is <c>this</c>
    /// — which in the real method is the declaring type. A diagnostic that names
    /// <c>__Shell</c> is therefore the shell mistyping <c>this</c> (the original
    /// source compiled, so the declaring-type form is valid), not a defect in the
    /// decompiled output. Filtered like the binding-visibility codes.
    /// </summary>
    internal static bool IsShellArtifact(Diagnostic diagnostic)
        => diagnostic.GetMessage().Contains("__Shell") || IsThisRefShellArtifact(diagnostic);

    static bool IsThisRefShellArtifact(Diagnostic diagnostic)
    {
        // `out this` / `ref this` is valid inside a struct instance method, but
        // the validity shell wraps every body in a reference-type __Shell method.
        // Roslyn therefore reports CS1605 against `this` even when the original
        // declaring type accepts the spelling.
        if (diagnostic.Id != "CS1605" || diagnostic.Location.SourceTree is not { } tree)
            return false;
        var lineSpan = diagnostic.Location.GetLineSpan();
        if (lineSpan.StartLinePosition.Line < 0)
            return false;
        var line = tree.GetText().Lines[lineSpan.StartLinePosition.Line].ToString();
        return line.Contains("this", StringComparison.Ordinal);
    }

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
    {
        if (diagnostic.Id is not ("CS0712" or "CS0721" or "CS0722" or "CS0723"))
            return false;
        if (diagnostic.Location.SourceTree != tree)
            return false;

        string? simpleName = SimpleNameAtDiagnosticLocation(diagnostic, tree)
            ?? SimpleNameFromDiagnosticMessage(diagnostic);
        if (simpleName is null)
            return false;

        return ShellNoise.ReferencesNonGenericTypeNamed(function, simpleName)
            && !ShellNoise.ReferencesGenericTypeNamed(function, simpleName);
    }

    static string? SimpleNameAtDiagnosticLocation(Diagnostic diagnostic, SyntaxTree tree)
    {
        var node = tree.GetRoot().FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        if (node.FirstAncestorOrSelf<IdentifierNameSyntax>() is { } name)
            return name.Identifier.ValueText;
        if (node.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>() is { Type: IdentifierNameSyntax objectType })
            return objectType.Identifier.ValueText;
        return null;
    }

    static string? SimpleNameFromDiagnosticMessage(Diagnostic diagnostic)
    {
        var message = diagnostic.GetMessage();
        int start = message.IndexOf('\'');
        if (start < 0)
            return null;
        int end = message.IndexOf('\'', start + 1);
        if (end <= start + 1)
            return null;
        var quoted = message[(start + 1)..end];
        if (quoted.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '.' or '+')))
            return null;
        return ShellNoise.SimpleName(quoted);
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
