using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The host contract for <see cref="IrInvariants"/> (#3267). #3241 made the IR
/// invariant check run in Release; making it opt-in then moved the failure mode
/// rather than removing it, because a host that never armed the flag exercised
/// the pipeline broadly while validating nothing — coverage-shaped silence. The
/// default is now on, and these tests hold that shape: validation is what a host
/// gets for free, and declining it has exactly one form.
/// <para>
/// Enforcement is layered so the weaker layer never carries the claim alone.
/// The compiler owns it: <see cref="IrInvariants.Enabled"/> has a private
/// setter, so no host can spell the decline as an assignment (under a
/// <c>using static</c>, a namespace alias, or otherwise) — it would not build.
/// The census below then only has to pin the one call site of the one method
/// that remains, which it matches by method name rather than by receiver
/// spelling, so aliasing does not evade it either.
/// </para>
/// </summary>
public sealed class IrInvariantsHostContractTests
{
    const string OptOutMethod = nameof(IrInvariants.DisableForShippedTool);

    const string EnvironmentVariable = "DOTNET_INSPECT_IR_INVARIANTS";

    /// <summary>The one host allowed to decline validation, relative to the repo root.</summary>
    const string ShippedToolEntryPoint = "src/dotnet-inspect/Program.cs";

    static bool? EnvironmentRequest() =>
        IrInvariants.ParseRequest(Environment.GetEnvironmentVariable(EnvironmentVariable));

    /// <summary>
    /// This test project never arms the flag: it inherits the default. If the
    /// default is ever flipped back to off, this fails — and so does the
    /// end-to-end teeth test in <see cref="IrInvariantCheckTests"/>.
    /// </summary>
    [Fact]
    public void ValidationIsOnByDefault_WithoutTheHostArmingIt()
    {
        // An operator running the suite with DOTNET_INSPECT_IR_INVARIANTS=0 has
        // asked for the other answer; the precedence rule itself is covered by
        // EnvironmentRequestOutranksTheHostOptOut.
        if (EnvironmentRequest() is false)
        {
            Assert.False(IrInvariants.Enabled);
            return;
        }

        Assert.True(IrInvariants.Enabled);
    }

    /// <summary>
    /// The structural half of the enforcement: neither level can be lowered by
    /// assignment, so no census, review habit, or naming convention has to catch
    /// that spelling. <see cref="IrInvariants.CheckSemantics"/> can still be
    /// armed, through a method whose only direction is up.
    /// </summary>
    [Fact]
    public void NeitherLevelHasAPubliclyWritableSetter()
    {
        Assert.Null(PublicSetter(nameof(IrInvariants.Enabled)));
        Assert.Null(PublicSetter(nameof(IrInvariants.CheckSemantics)));

        static MethodInfo? PublicSetter(string name) =>
            typeof(IrInvariants)
                .GetProperty(name, BindingFlags.Public | BindingFlags.Static)!
                .GetSetMethod(nonPublic: false);
    }

    /// <summary>
    /// The semantic level stays opt-in: minimal hand-built fixtures reference
    /// local slots without populating <c>Locals</c>, so arming it suite-wide
    /// would false-positive on them. Corpus gates thread the level explicitly.
    /// </summary>
    [Fact]
    public void SemanticLevelStaysOptIn()
    {
        bool requestedFull = string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariable)?.Trim(),
            "full",
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(requestedFull, IrInvariants.CheckSemantics);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("full", true)]
    [InlineData("on", true)]
    [InlineData("yes", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData("no", false)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("bogus", null)]
    [InlineData(null, null)]
    public void EnvironmentValueMapsToAnExplicitRequest(string? value, bool? expected)
    {
        Assert.Equal(expected, IrInvariants.ParseRequest(value));
    }

    /// <summary>
    /// Case and surrounding whitespace must not silently swallow an off request.
    /// With the default inverted, a dropped "off" leaves the check armed against
    /// an operator who explicitly asked for it off — the failure is quiet and
    /// points the wrong way.
    /// </summary>
    [Theory]
    [InlineData("False", false)]
    [InlineData("OFF", false)]
    [InlineData(" 0 ", false)]
    [InlineData("True", true)]
    [InlineData("FULL", true)]
    [InlineData(" full\t", true)]
    public void EnvironmentValueIsTrimmedAndCaseInsensitive(string value, bool? expected)
    {
        Assert.Equal(expected, IrInvariants.ParseRequest(value));
    }

    /// <summary>
    /// Precedence, exercised as a pure rule so it needs no process isolation: a
    /// host that says nothing is validated; the shipped tool's opt-out turns it
    /// off; an explicit environment request outranks both, so an operator can
    /// arm the shipped tool for debugging without a rebuild.
    /// </summary>
    [Theory]
    [InlineData(null, false, true)]
    [InlineData(null, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void EnvironmentRequestOutranksTheHostOptOut(bool? environmentRequest, bool hostOptedOut, bool expected)
    {
        Assert.Equal(expected, IrInvariants.ResolveEnabled(environmentRequest, hostOptedOut));
    }

    /// <summary>
    /// The drift guard over what the compiler cannot express: which host may
    /// reach the opt-out. Matching is by identifier, not by receiver spelling or
    /// call shape, so an aliased <c>Inv.DisableForShippedTool()</c>, a bare call
    /// under <c>using static</c>, and a method group handed to a delegate
    /// (<c>Action a = IrInvariants.DisableForShippedTool; a();</c>) are all
    /// caught. A new harness, sweep, or benchmark that quietly declines
    /// validation fails here instead of shipping silent non-coverage.
    /// <para>
    /// Residual gap, stated rather than papered over: reflection onto the
    /// private setter or the method is beyond sound static analysis. It is not a
    /// spelling anyone reaches by accident, and the string-literal check below
    /// catches the straightforward <c>GetMethod("DisableForShippedTool")</c>
    /// form.
    /// </para>
    /// </summary>
    [Fact]
    public void OnlyTheShippedToolEntryPointDeclinesValidation()
    {
        var sites = FindOptOutSites().OrderBy(static s => s, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { ShippedToolEntryPoint }, sites);
    }

    static List<string> FindOptOutSites()
    {
        string root = FindRepositoryRoot();
        string declaringFile = Path.Combine(
            root, "src", "ILInspector.Decompiler", "Pipeline", "Ir", "IrInvariants.cs");

        // Scan the whole repository rather than a list of source roots: a new
        // top-level directory (benchmarks/, samples/, a future sweep tool) is
        // exactly the kind of new host this guard exists for, and it must not be
        // able to appear outside the scan.
        List<string> sites = [];
        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (IsExcluded(root, path) || PathsEqual(path, declaringFile))
                continue;

            string text = File.ReadAllText(path);
            if (!text.Contains(OptOutMethod, StringComparison.Ordinal))
                continue;

            if (ReferencesOptOut(text, path))
                sites.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'));
        }

        Assert.True(
            sites.Count > 0,
            $"Scanned no opt-out sites beneath {root}; the census would pass vacuously.");

        return sites;
    }

    static bool ReferencesOptOut(string text, string path)
    {
        var tree = CSharpSyntaxTree.ParseText(text, path: path, cancellationToken: TestContext.Current.CancellationToken);
        var root = tree.GetCompilationUnitRoot(TestContext.Current.CancellationToken);

        // Any identifier reference, not just an invocation: a method group
        // assigned to a delegate reaches the opt-out just as well as a call.
        // nameof(...) is excluded — it names the method without reaching it,
        // which is how this test refers to it.
        bool names = root.DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .Any(name => name.Identifier.ValueText == OptOutMethod && !IsInsideNameOf(name));

        bool spellsItAsAString = root.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Any(literal => literal.IsKind(SyntaxKind.StringLiteralExpression)
                && literal.Token.ValueText == OptOutMethod);

        return names || spellsItAsAString;
    }

    static bool IsInsideNameOf(SyntaxNode node) =>
        node.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });

    /// <summary>
    /// Build output and version control only. Anything else under the repo root
    /// is a candidate host, including directories that do not exist yet.
    /// </summary>
    static bool IsExcluded(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        string[] segments = relative.Split(Path.DirectorySeparatorChar);

        return segments.Any(static segment =>
            segment is "bin" or "obj" or ".git" or "artifacts" or "node_modules");
    }

    static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.Ordinal);

    static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing dotnet-inspect.slnx.");
    }
}
