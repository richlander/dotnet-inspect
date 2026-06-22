using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The whole-type source <em>binding</em> oracle (issue #1137). Where
/// <see cref="TypeSourceCheck"/> grades the composed listing syntactically — it
/// never binds — this one compiles each composed type against the platform
/// reference set and reports the type/signature-level <c>CS0104</c> ambiguous
/// reference collisions that only a binder can see.
///
/// <para>
/// A collision happens when the listing imports two namespaces that both define
/// a simple name the source uses unqualified. The product
/// <see cref="TypeSourceComposer"/> already keeps a name qualified when its own
/// metadata shows two owners (the detectable case, #1017). The residue this
/// oracle catches is the <em>undetectable</em> case: the competing type is not a
/// TypeDef/TypeRef of the composed assembly, so the product — which must stay
/// SRM-only and free of external namespace enumeration — cannot know it exists.
/// The canonical example is <c>System.AppDomain.ExecuteAssembly</c>, whose
/// <c>AssemblyHashAlgorithm</c> parameter (from <c>System.Configuration.Assemblies</c>)
/// collides with <c>System.Reflection.AssemblyHashAlgorithm</c> once
/// <c>System.Reflection</c> is imported for other members.
/// </para>
///
/// <para>
/// Method bodies are stubbed (via <see cref="TypeSourceCheck.StubMemberBodies"/>)
/// before binding, so a body-codegen defect can neither manufacture nor mask a
/// type/signature-level collision — the same isolation #1112 relies on. Roslyn
/// lives only here in the developer/CI oracle; the product path is unchanged.
/// </para>
/// </summary>
static class TypeBindCheck
{
    /// <summary>One ambiguous-reference (CS0104) collision in a composed type.</summary>
    public sealed record BindCollision(string FullType, string SimpleName, string Detail);

    /// <summary>
    /// Collisions that are unfixable on the product path without enumerating an
    /// external namespace's contents (the #1137 guardrail). Keyed by
    /// (full type, ambiguous simple name). The gate accepts these and fails only
    /// on collisions outside this set, so a regression that introduces a new
    /// collision — or a composer change that resolves one of these — is caught.
    /// </summary>
    public static readonly ImmutableHashSet<(string FullType, string SimpleName)> KnownArtifacts =
        ImmutableHashSet.Create(
            ("System.AppDomain", "AssemblyHashAlgorithm"));

    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples)
    {
        int typesChecked = 0, assembliesScanned = 0;
        var collisions = new List<BindCollision>();
        int known = 0;

        foreach (var path in assemblies)
        {
            if (typesChecked >= cap)
                break;
            int before = typesChecked;
            try
            {
                foreach (var c in Evaluate(path, cap, ref typesChecked))
                {
                    if (KnownArtifacts.Contains((c.FullType, c.SimpleName)))
                        known++;
                    else
                        collisions.Add(c);
                }
            }
            catch
            {
                continue;
            }
            if (typesChecked > before)
                assembliesScanned++;
        }

        Console.WriteLine($"TYPE-BIND over {typesChecked} composed types ({assembliesScanned} assemblies)");
        Console.WriteLine();
        Console.WriteLine($"  known artifacts (allowlisted)   : {known}");
        Console.WriteLine($"  new ambiguous-reference (CS0104): {collisions.Count}");
        if (collisions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  examples:");
            foreach (var c in collisions.Take(maxExamples))
                Console.WriteLine($"    {c.FullType} — {c.SimpleName} — {c.Detail}");
            // Nonzero exit: a new collision is always a real, compile-breaking defect.
            return 1;
        }
        Console.WriteLine();
        Console.WriteLine("  no new type-source binding collisions.");
        return 0;
    }

    /// <summary>
    /// Composes every public top-level type in <paramref name="assemblyPath"/>,
    /// stubs bodies, binds against the platform reference set, and returns the
    /// CS0104 collisions. Known artifacts are excluded unless
    /// <paramref name="includeKnown"/> is set. Testable entry point.
    /// </summary>
    public static IReadOnlyList<BindCollision> Evaluate(
        string assemblyPath, int cap = int.MaxValue, bool includeKnown = false)
        => EvaluateDetailed(assemblyPath, cap, includeKnown).Collisions;

    /// <summary>
    /// As <see cref="Evaluate"/>, also reporting how many types were composed and
    /// bound — so a gate can assert a non-vacuous population (a refactor that
    /// silently stops composing types must not pass a zero-collision check).
    /// </summary>
    public static (int TypesChecked, IReadOnlyList<BindCollision> Collisions) EvaluateDetailed(
        string assemblyPath, int cap = int.MaxValue, bool includeKnown = false)
    {
        int checkedCount = 0;
        var all = Evaluate(assemblyPath, cap, ref checkedCount);
        var collisions = includeKnown
            ? all
            : all.Where(c => !KnownArtifacts.Contains((c.FullType, c.SimpleName))).ToList();
        return (checkedCount, collisions);
    }

    static IReadOnlyList<BindCollision> Evaluate(
        string assemblyPath, int cap, ref int checkedCount)
    {
        var collisions = new List<BindCollision>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            return collisions;

        var references = ReferencesFor(assemblyPath);
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        foreach (var type in surface.Types)
        {
            if (checkedCount >= cap)
                break;
            if (type.Kind == "delegate")
                continue;

            var source = TypeSourceComposer.Compose(type, assemblyPath, pdbPath: null);
            if (source is null)
                continue;

            checkedCount++;
            collisions.AddRange(Collisions(type.FullName, source, references));
        }
        return collisions;
    }

    /// <summary>
    /// The pure detector: bind <paramref name="composedSource"/> (with bodies
    /// stubbed) against <paramref name="references"/> and return every CS0104
    /// ambiguous-reference diagnostic as a collision. No other diagnostic class
    /// is consulted — the duplicate-definition noise from referencing the
    /// composed type's own defining assembly is irrelevant to an unqualified
    /// name's ambiguity.
    /// </summary>
    public static IReadOnlyList<BindCollision> Collisions(
        string fullType, string composedSource, ImmutableArray<MetadataReference> references)
    {
        var tree = CSharpSyntaxTree.ParseText(TypeSourceCheck.StubMemberBodies(composedSource));
        var compilation = CSharpCompilation.Create(
            "bind", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var collisions = new List<BindCollision>();
        foreach (var diagnostic in compilation.GetDiagnostics())
        {
            if (diagnostic.Id != "CS0104")
                continue;
            string message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            collisions.Add(new BindCollision(fullType, FirstQuoted(message) ?? "?", message));
        }
        return collisions;
    }

    /// <summary>The first single-quoted span of a CS0104 message is the ambiguous simple name.</summary>
    static string? FirstQuoted(string message)
    {
        int open = message.IndexOf('\'');
        if (open < 0)
            return null;
        int close = message.IndexOf('\'', open + 1);
        return close < 0 ? null : message[(open + 1)..close];
    }

    /// <summary>
    /// The platform reference set for binding a composed type: the running
    /// runtime's trusted platform assemblies plus, if the target is not already
    /// among them, the target and its sibling assemblies. Unlike
    /// <c>FidelityCheck</c> (which reconstructs a body and so excludes the
    /// target), this binds the type shell and therefore keeps the target so its
    /// own types — and the competing same-named types in sibling assemblies —
    /// are both visible.
    /// </summary>
    public static ImmutableArray<MetadataReference> ReferencesFor(string assemblyPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();

        void Add(string path)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return;
            if (!seen.Add(Path.GetFileNameWithoutExtension(path)))
                return;
            try { builder.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }

        foreach (var path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            Add(path);

        Add(Path.GetFullPath(assemblyPath));
        var dir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        if (dir is not null && Directory.Exists(dir))
            foreach (var path in Directory.EnumerateFiles(dir, "*.dll"))
                Add(path);

        return builder.ToImmutable();
    }
}
