using System.Collections.Immutable;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
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

    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples, string? emitDefectsPath = null, string? diffDefectsPath = null, bool lowered = false)
    {
        // When emitting or diffing, record the error-code set per Full method so a
        // before/after run can be compared method-by-method (which methods gained
        // or lost a given code) — the differential a raw aggregate count hides.
        var methodDefects = emitDefectsPath is not null || diffDefectsPath is not null
            ? new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal)
            : null;
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

        int total = 0, fullTotal = 0, partialTotal = 0;
        int fullMalformed = 0, partialMalformed = 0;
        int semChecked = 0, semDefect = 0;
        var malformedExamples = new List<string>();
        var defectExamples = new List<string>();
        var defectCodes = new SortedDictionary<string, int>(StringComparer.Ordinal);

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var path in assemblies)
        {
            MetadataSource source;
            try { source = MetadataSource.Open(path, context: metadata); }
            catch { continue; }
            using (source)
            {
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
                    total++;
                    bool full = function.Fidelity == DecompilationFidelity.Full;
                    if (full) fullTotal++; else partialTotal++;

                    string shell = Shell(function, rendered, typeName, methodName, constraints);
                    var tree = CSharpSyntaxTree.ParseText(shell, parseOptions);
                    var syntaxErrors = tree.GetDiagnostics().Where(IsError).ToList();
                    var illegal = IllegalStatements(tree);

                    if (syntaxErrors.Count > 0 || illegal.Count > 0)
                    {
                        if (full) fullMalformed++; else partialMalformed++;
                        if (full && malformedExamples.Count < maxExamples)
                        {
                            string reason = syntaxErrors.Count > 0
                                ? syntaxErrors[0].Id + ": " + syntaxErrors[0].GetMessage()
                                : "CS0201 (illegal statement): " + illegal[0].ToString().Trim();
                            malformedExamples.Add($"{typeName}::{methodName}\n    {reason}");
                        }
                        if (full && methodDefects is not null)
                        {
                            var codes = syntaxErrors.Select(e => e.Id);
                            Record(methodDefects, $"{typeName}::{methodName}", illegal.Count > 0 ? codes.Append("CS0201") : codes);
                        }
                        continue;
                    }

                    // Bind only Full, syntactically-valid methods (the set that
                    // claims to be good) up to the cap — binding is the slow part.
                    if (!full || semChecked >= cap)
                        continue;
                    semChecked++;
                    var compilation = CSharpCompilation.Create("check", [tree], references, compileOptions);
                    var defects = compilation.GetDiagnostics()
                        .Where(IsError)
                        .Where(d => !BindingNoise.Contains(d.Id))
                        .Where(d => !IsShellArtifact(d))
                        .Where(d => !IsGenericArityCollisionNoise(d, tree, function))
                        .ToList();
                    // Record EVERY bound method (clean ones with no codes), so the
                    // diff can restrict to methods checked in BOTH runs — a method
                    // skipped by the cap in one run must not masquerade as "clean".
                    if (methodDefects is not null)
                        Record(methodDefects, $"{typeName}::{methodName}", defects.Select(d => d.Id));
                    if (defects.Count > 0)
                    {
                        semDefect++;
                        foreach (var d in defects)
                            defectCodes[d.Id] = defectCodes.GetValueOrDefault(d.Id) + 1;
                        if (defectExamples.Count < maxExamples)
                            defectExamples.Add($"{typeName}::{methodName}\n    {defects[0].Id}: {defects[0].GetMessage()}");
                    }
                }
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

    /// <summary>Wraps a body in a generic instance method on a class so locals, params, type params, and `this` all bind; member access on `this` becomes filtered binding noise.</summary>
    internal static string Shell(IrFunction function, string body, string typeName, string methodName,
        IReadOnlyDictionary<string, Dictionary<string, string>> constraints)
    {
        var generics = GenericParameterNames(function);
        string genericList = generics.Count > 0 ? "<" + string.Join(", ", generics) + ">" : "";
        string whereClauses = generics.Count > 0
            ? ShellConstraints.Clauses(constraints, typeName, methodName, function, generics)
            : "";
        string returnType = TypeText(function.Signature.ReturnType);
        string parameters = string.Join(", ", function.Signature.Parameters.Select(ParameterText));
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
                unsafe {{returnType}} __M{{genericList}}({{parameters}}){{whereClauses}}
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
    internal static bool IsShellArtifact(Diagnostic diagnostic) => diagnostic.GetMessage().Contains("__Shell");

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
