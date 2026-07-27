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
/// gets for free, and declining it is a single explicit call site that this
/// census pins.
/// </summary>
public sealed class IrInvariantsHostContractTests
{
    const string OptOutMethod = "DisableForShippedTool";

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
    /// The semantic level stays opt-in: minimal hand-built fixtures reference
    /// local slots without populating <c>Locals</c>, so arming it suite-wide
    /// would false-positive on them. Corpus gates thread the level explicitly.
    /// </summary>
    [Fact]
    public void SemanticLevelStaysOptIn()
    {
        bool requestedFull = Environment.GetEnvironmentVariable(EnvironmentVariable) == "full";

        Assert.Equal(requestedFull, IrInvariants.CheckSemantics);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("full", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData("", null)]
    [InlineData("yes", null)]
    [InlineData(null, null)]
    public void EnvironmentValueMapsToAnExplicitRequest(string? value, bool? expected)
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
    /// The drift guard. Any write to <see cref="IrInvariants.Enabled"/> outside
    /// the declaring file — whether a direct assignment or the sanctioned
    /// <see cref="IrInvariants.DisableForShippedTool"/> call — is a host
    /// declining or overriding validation, and only the shipped CLI entry point
    /// may do that. A new harness, sweep, or benchmark that quietly turns the
    /// check off fails here instead of shipping silent non-coverage.
    /// </summary>
    [Fact]
    public void OnlyTheShippedToolEntryPointDeclinesValidation()
    {
        var sites = FindEnabledWriteSites().OrderBy(static s => s, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { ShippedToolEntryPoint }, sites);
    }

    static List<string> FindEnabledWriteSites()
    {
        string root = FindRepositoryRoot();
        string declaringFile = Path.Combine(
            root, "src", "ILInspector.Decompiler", "Pipeline", "Ir", "IrInvariants.cs");

        List<string> sites = [];
        foreach (string area in new[] { "src", "tools", "tests" })
        {
            string areaPath = Path.Combine(root, area);
            if (!Directory.Exists(areaPath))
                continue;

            foreach (string path in Directory.EnumerateFiles(areaPath, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(path) || PathsEqual(path, declaringFile))
                    continue;

                string text = File.ReadAllText(path);
                if (!text.Contains(nameof(IrInvariants), StringComparison.Ordinal)
                    && !text.Contains(OptOutMethod, StringComparison.Ordinal))
                {
                    continue;
                }

                if (WritesEnabled(text, path))
                    sites.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'));
            }
        }

        return sites;
    }

    static bool WritesEnabled(string text, string path)
    {
        var tree = CSharpSyntaxTree.ParseText(text, path: path, cancellationToken: TestContext.Current.CancellationToken);
        var root = tree.GetCompilationUnitRoot(TestContext.Current.CancellationToken);

        bool assigns = root.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => IsEnabledFlag(assignment.Left));

        bool callsOptOut = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => NameOf(invocation.Expression) == OptOutMethod);

        return assigns || callsOptOut;
    }

    static bool IsEnabledFlag(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax member
            && member.Name.Identifier.ValueText == nameof(IrInvariants.Enabled)
            && member.Expression.ToString().EndsWith(nameof(IrInvariants), StringComparison.Ordinal);

    static string? NameOf(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => null,
    };

    static bool IsBuildOutput(string path)
    {
        string separator = Path.DirectorySeparatorChar.ToString();
        return path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
            || path.Contains($"{separator}obj{separator}", StringComparison.Ordinal);
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
