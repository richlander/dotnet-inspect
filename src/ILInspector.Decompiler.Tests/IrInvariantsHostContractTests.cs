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
    /// <para>
    /// The environment branch below covers precedence only — that the flag
    /// reports what the operator asked for. That an off request is <em>loud</em>
    /// rather than silent is a separate claim, held by
    /// <see cref="AnEnvironmentOffRequestDoesNotSilentlyDisarmTheSuite"/>.
    /// </para>
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
    /// The environment bypass, stated where someone reasoning about the host
    /// contract will look (#3303). It is the cheapest decline of them all — one
    /// line in a workflow <c>env:</c> block, no
    /// <see cref="IrInvariants.DisableForShippedTool"/> call — so the source
    /// census in <see cref="OnlyTheShippedToolEntryPointDeclinesValidation"/>
    /// structurally cannot see it.
    /// <para>
    /// The guarantee is not that the bypass is impossible: an operator who asks
    /// for off has asked for it, and the precedence rule honors that
    /// deliberately. The guarantee is that a run which takes it cannot come back
    /// green, so a CI job cannot quietly trade validation for wall-clock and
    /// look healthy doing it. That was already true before this test, but only
    /// as a side effect of one assertion inside
    /// <c>IrInvariantCheckTests.PipelineRunner_ThrowsWhenAPassCorruptsTheTree</c>,
    /// whose name gives a reader no reason to expect it. Asserting it here
    /// names the job, and reports the cause instead of a confusing "should be
    /// armed by default" when the run was disarmed on purpose.
    /// </para>
    /// </summary>
    [Fact]
    public void AnEnvironmentOffRequestDoesNotSilentlyDisarmTheSuite()
    {
        Assert.True(
            IrInvariants.Enabled,
            $"IR invariant validation is off for this run because {EnvironmentVariable} was set to "
                + $"'{Environment.GetEnvironmentVariable(EnvironmentVariable)}'. That request is honored "
                + "on purpose, but a disarmed run is not a passing run: every pipeline test below "
                + "validates nothing. Unset the variable to restore coverage.");
    }

    /// <summary>
    /// The structural half of the enforcement: neither level can be lowered by
    /// assignment, so no census, review habit, or naming convention has to catch
    /// that spelling. <see cref="IrInvariants.CheckSemantics"/> has no setter at
    /// all — the environment resolves it once at startup — so it cannot be moved
    /// in either direction in-process.
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
    /// Every public entry point on this type changes what a host validates, so
    /// each one needs a host contract pinned here. Set equality, because the
    /// failure this catches is an <em>addition</em>: #3303 found
    /// <c>EnableSemanticChecks()</c> shipped with zero call sites and a doc
    /// naming a consumer — the corpus sweep — that
    /// <see cref="CorpusSweepGateTests"/> documents deliberately avoiding. An
    /// affordance no host uses is the same coverage-shaped silence the census
    /// exists to remove: it reads as a supported way to move the level while
    /// nothing holds it to a contract, and it gives a future host a second
    /// spelling to drift on. Raising the semantic level has one spelling
    /// (<c>DOTNET_INSPECT_IR_INVARIANTS=full</c>) and threading it per call has
    /// another (<c>CheckInvariant(includeSemantics: true)</c>); neither needs a
    /// public mutator.
    /// </summary>
    [Fact]
    public void ThePublicSurfaceIsExactlyTheShippedToolOptOut()
    {
        var methods = typeof(IrInvariants)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName)
            .Select(static method => method.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { OptOutMethod }, methods);
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
    /// caught. Every preprocessor configuration of a file is matched, not just
    /// the one that survives parsing with no symbols defined, so a decline
    /// hidden behind <c>#if</c> is caught too. A new harness, sweep, or
    /// benchmark that quietly declines validation fails here instead of shipping
    /// silent non-coverage.
    /// <para>
    /// Residual gaps, stated rather than papered over. The cheapest decline is
    /// not a call site at all: a host or CI job that sets
    /// <c>DOTNET_INSPECT_IR_INVARIANTS=0</c> disarms validation with no
    /// <see cref="IrInvariants.DisableForShippedTool"/> anywhere, so no source
    /// census can see it. That one is covered by consequence rather than by
    /// scanning — such a run cannot come back green, asserted by
    /// <see cref="AnEnvironmentOffRequestDoesNotSilentlyDisarmTheSuite"/> and by
    /// <c>IrInvariantCheckTests.PipelineRunner_ThrowsWhenAPassCorruptsTheTree</c>.
    /// Of the spellings that <em>are</em> call sites, reflection onto the
    /// private setter or the method is beyond sound static analysis; the
    /// string-literal check catches the straightforward
    /// <c>GetMethod("DisableForShippedTool")</c> form but not a computed name.
    /// A <c>.cs</c> file living outside the repository and pulled in with
    /// <c>&lt;Compile Include="../../.."/&gt;</c> is outside the scan. Neither
    /// is a spelling anyone reaches by accident.
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

    /// <summary>
    /// Holds every configuration of a file to the same rule, not just the one
    /// that parses with no symbols defined. Roslyn parses without preprocessor
    /// symbols, so a decline spelled inside <c>#if DECLINE_VALIDATION</c> would
    /// otherwise sit in disabled trivia and never reach the matcher — while a
    /// host that defines that constant declines for real. Each disabled region
    /// is re-parsed and matched the same way, so <c>#if</c>, <c>#else</c>, and
    /// nested combinations are all covered without having to guess which
    /// symbols a host defines.
    /// </summary>
    static bool ReferencesOptOut(string text, string path)
    {
        Queue<string> configurations = new();
        configurations.Enqueue(text);

        while (configurations.Count > 0)
        {
            string configuration = configurations.Dequeue();
            var tree = CSharpSyntaxTree.ParseText(configuration, path: path, cancellationToken: TestContext.Current.CancellationToken);
            var root = tree.GetCompilationUnitRoot(TestContext.Current.CancellationToken);

            if (NamesOptOut(root))
                return true;

            foreach (var disabled in root.DescendantTrivia(descendIntoTrivia: true))
            {
                // A disabled region is a proper substring of the text it came
                // from — the directive lines that delimit it are lexed away — so
                // the length guard makes the walk provably terminating.
                if (disabled.IsKind(SyntaxKind.DisabledTextTrivia) && disabled.FullSpan.Length < configuration.Length)
                    configurations.Enqueue(disabled.ToFullString());
            }
        }

        return false;
    }

    static bool NamesOptOut(SyntaxNode root)
    {
        // nameof(...) names the method without reaching it, which is how this
        // test refers to it — but only when nameof is the operator. A file that
        // declares its own member called nameof turns that spelling back into a
        // real call, so the exclusion is withdrawn for that file.
        bool nameOfIsTheOperator = !DeclaresNameOf(root);

        // Any identifier reference, not just an invocation: a method group
        // assigned to a delegate reaches the opt-out just as well as a call.
        bool names = root.DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .Any(name => name.Identifier.ValueText == OptOutMethod
                && !(nameOfIsTheOperator && IsInsideNameOf(name)));

        // Error recovery in a re-parsed disabled region can leave the reference
        // as a skipped token rather than a name node, so those are read too.
        names |= root.DescendantTokens(descendIntoTrivia: true)
            .Any(token => token.IsKind(SyntaxKind.IdentifierToken)
                && token.ValueText == OptOutMethod
                && token.Parent is SkippedTokensTriviaSyntax);

        bool spellsItAsAString = root.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Any(literal => literal.IsKind(SyntaxKind.StringLiteralExpression)
                && literal.Token.ValueText == OptOutMethod);

        return names || spellsItAsAString;
    }

    /// <summary>
    /// <c>nameof</c> is a contextual keyword, so anything in scope may be named
    /// after it and <c>nameof(IrInvariants.DisableForShippedTool)</c> then
    /// compiles to a call that hands over the method group. A delegate-typed
    /// field, local, or parameter does this as well as a method does.
    /// </summary>
    static bool DeclaresNameOf(SyntaxNode root) =>
        root.DescendantNodes().Any(static node => NameOf(node) is "nameof");

    static string? NameOf(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        LocalFunctionStatementSyntax local => local.Identifier.ValueText,
        DelegateDeclarationSyntax @delegate => @delegate.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        VariableDeclaratorSyntax variable => variable.Identifier.ValueText,
        ParameterSyntax parameter => parameter.Identifier.ValueText,
        _ => null,
    };

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
