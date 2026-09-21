using System.CommandLine;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using CSharpText;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspect.Cli.Tests;

public partial class MatchDiscoveryTests
{
    /// <summary>
    /// The README tells same-image callers to confirm a candidate with the pairwise form. That
    /// instruction must stay scoped, because there is no cross-image confirmation to run.
    /// </summary>
    [Fact]
    public void Readme_DoesNotPromiseCrossImagePairwiseConfirmation()
    {
        string readme = Path.Combine(CommandErrorOwnershipTests.RepositoryRoot(), "README.md");
        string text = File.ReadAllText(readme);

        int section = text.IndexOf("### Structural matching", StringComparison.Ordinal);
        Assert.True(section >= 0, "README no longer has a Structural matching section.");
        int end = text.IndexOf("\n### ", section + 1, StringComparison.Ordinal);
        string structuralMatching = end < 0 ? text[section..] : text[section..end];

        Assert.DoesNotContain(
            "Confirm a candidate by re-running the pairwise form on the selected pair.",
            structuralMatching);
        Assert.Contains("Within one image, confirm a candidate", structuralMatching);
    }

    /// <summary>
    /// The range grammar was removed from the product, but the surviving promise of it sat in the
    /// command table, outside the Structural matching section the gate above reads. Scoping a
    /// documentation gate to one section is why that line shipped stale for a full round; this one
    /// reads the whole file.
    /// </summary>
    [Fact]
    public void Readme_DoesNotPromiseTheRemovedLibraryRangeGrammar()
    {
        string readme = Path.Combine(CommandErrorOwnershipTests.RepositoryRoot(), "README.md");
        string text = File.ReadAllText(readme);

        Assert.DoesNotContain("old.dll..new.dll", text);
        Assert.DoesNotContain("--library old", text);
    }

    // ---- Round 7 review findings: raw MethodDef token selectors ----

    /// <summary>
    /// A MethodDef token is a dense table row index, not an identity. Resolving one against a
    /// merged surface -- which includes type-forwarded types whose rows live in other images --
    /// binds it to whichever type collides first. Feeding the tool its own printed token back
    /// returned a confidently wrong member at exit 0, in both the pairwise and the seeded
    /// direction, for four consecutive rounds. A token now resolves only against the one image
    /// named by <c>--library</c>, and never silently against a forwarded one.
    /// </summary>
    [Fact]
    public async Task Pairwise_TokenOutsideTheNamedImage_FailsRatherThanNamingAnotherMember()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");
        Assert.True(File.Exists(facade), facade);

        // System.Runtime is a pure facade: it defines no method bodies, so no MethodDef row it
        // could be asked about is its own. Any answer other than a failure is a wrong answer.
        MatchOptions options = new()
        {
            LeftSelector = "0x06000001",
            RightSelector = "0x06000001",
            AssemblyPath = facade,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.DoesNotContain("Relation", error);
        Assert.Contains("is not a MethodDef row in", error);
        Assert.Contains("System.Runtime.dll", error);
        Assert.DoesNotContain("method handle is outside", error);
    }

    /// <summary>
    /// The seeded direction of the same defect: a token seed silently scoped discovery to whatever
    /// forwarded type its row collided with, then ranked candidates inside that unrelated type and
    /// reported success. Failing is the only honest outcome when the named image does not define
    /// the row.
    /// </summary>
    [Fact]
    public async Task Similar_TokenSeedOutsideTheNamedImage_DoesNotScopeToAForeignType()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");
        Assert.True(File.Exists(facade), facade);

        MatchOptions options = Seeded("0x06000001") with { AssemblyPath = facade };

        var (exitCode, output, error) = await RunAsync(options);

        // Discovery fails on its own terms here -- it cannot determine a declaring type for a row
        // the named image does not define. The defect was that it *could*: the row collided with a
        // forwarded type and silently scoped retrieval to it.
        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.DoesNotContain("SortedDictionary", error);
        Assert.DoesNotContain("method handle is outside", error);
    }

    /// <summary>
    /// An out-of-range row must produce the command's own typed selector error. Reaching metadata
    /// with an unchecked row surfaced a framework resource name (<c>Arg_ParamName_Name</c>) to the
    /// user instead.
    /// </summary>
    [Fact]
    public async Task Pairwise_TokenBeyondTheMethodDefTable_ReportsATypedSelectorError()
    {
        MatchOptions options = new()
        {
            LeftSelector = "0x06FFFFFF",
            RightSelector = "0x06FFFFFF",
            AssemblyPath = TestAssembly,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);

        // The command's own selector error, naming the token and the image -- not the Analysis
        // layer's ArgumentOutOfRangeException, whose message is a framework resource. On a
        // resource-stripped AOT host that message renders as the raw key "Arg_ParamName_Name".
        Assert.Contains("0x06FFFFFF", error);
        Assert.Contains("is not a MethodDef row in", error);
        Assert.DoesNotContain("method handle is outside", error);
        Assert.DoesNotContain("Arg_ParamName_Name", error);
    }

    /// <summary>
    /// The contract the token selector exists to serve: a token this run printed must address the
    /// same member when handed straight back. This is the round-trip that was silently broken.
    /// </summary>
    [Fact]
    public async Task Similar_PrintedToken_AddressesTheSameMemberWhenFedBack()
    {
        MatchOptions options = Seeded(SampleSeed) with { JsonOutput = true };

        var (exitCode, output, error) = await RunAsync(options);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);

        JsonElement document = Parse(output);
        JsonElement first = document.GetProperty("candidates").EnumerateArray().First();
        string token = first.GetProperty("token").GetString()!;
        string member = first.GetProperty("member").GetString()!;

        var (pairExit, pairOutput, pairError) = await RunAsync(new MatchOptions
        {
            LeftSelector = token,
            RightSelector = token,
            AssemblyPath = TestAssembly,
        });

        Assert.Equal(0, pairExit);
        Assert.Empty(pairError);
        Assert.Contains(member[..member.LastIndexOf('.')], pairOutput);
    }

    /// <summary>
    /// <c>--assembly-wide</c> with a forwarded seed searched the facade, which defines no bodies,
    /// and reported an empty ranking at exit 0 -- while the narrower default scope ranked real
    /// candidates. A widened scope must never return strictly less than the narrower one.
    /// </summary>
    [Fact]
    public async Task Similar_AssemblyWideWithForwardedSeed_SearchesTheDefiningImage()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");
        Assert.True(File.Exists(facade), facade);

        MatchOptions options = Seeded("System.String.IsNullOrEmpty") with
        {
            AssemblyPath = facade,
            AssemblyWide = true,
            JsonOutput = true,
        };

        var (exitCode, output, error) = await RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.NotEmpty(Candidates(Parse(output)));
    }

    /// <summary>
    /// The printed token is a promise scoped to the image the disclosure names. Feeding a token
    /// that discovery printed for a forwarded population back against the facade must fail: the
    /// facade does not define that row. Resolving the token through the merged surface instead --
    /// which carries forwarded types whose rows live elsewhere -- silently re-attributed it to the
    /// defining image and compared a member the caller never named, at exit 0.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Pairwise_TokenPrintedForAForwardedPopulation_IsNotHonoredAgainstTheFacade()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");
        Assert.True(File.Exists(facade), facade);

        MatchOptions seedOptions = Seeded("System.String.IsNullOrEmpty") with
        {
            AssemblyPath = facade,
            RightSelector = "System.String",
            JsonOutput = true,
        };

        var (seedExit, seedOutput, _) = await RunAsync(seedOptions);
        Assert.Equal(0, seedExit);

        JsonElement document = Parse(seedOutput);
        string definingImage = document.GetProperty("candidate_assembly").GetString()!;
        string token = document.GetProperty("candidates").EnumerateArray()
            .First().GetProperty("token").GetString()!;

        // Against the image the disclosure named, the promise holds.
        var (honored, _, honoredError) = await RunCliAsync(
            "match", token, token, "--library", definingImage);
        Assert.Equal(0, honored);
        Assert.Empty(honoredError);

        // Against the facade the caller typed, it must fail rather than name another member.
        var (rejected, rejectedOutput, rejectedError) = await RunCliAsync(
            "match", token, token, "--library", facade);
        Assert.Equal(1, rejected);
        Assert.Empty(rejectedOutput);
        Assert.Contains("is not a MethodDef row in", rejectedError);
    }

    /// <summary>
    /// <c>System.dll</c> is a multi-target facade that defines no method bodies of its own, so no
    /// MethodDef token can name one of its members. Resolving a token through the merged surface
    /// bound it to whichever forwarded type held a colliding row -- returning
    /// <c>Relation: Exact</c> at exit 0 for a member the caller never named. Every probed row must
    /// be rejected; a facade has no row to compare.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Pairwise_TokenAgainstAPureFacade_NeverBindsToAForwardedType()
    {
        string coreLibrary = typeof(string).Assembly.Location;
        string facade = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.dll");
        if (!File.Exists(facade))
            return;

        foreach (string token in new[] { "0x06000001", "0x06000169", "0x06000FFF" })
        {
            var (exitCode, output, error) = await RunCliAsync(
                "match", token, token, "--library", facade);

            Assert.Equal(1, exitCode);
            Assert.Empty(output);
            Assert.Contains("is not a MethodDef row in", error);
        }
    }

    /// <summary>
    /// The parse layer rejects a missing second selector before the pairwise body runs, so the
    /// guidance for "you supplied a discovery flag; add <c>--similar</c>" was reachable only when
    /// the caller had already supplied two selectors. The caller who wrote one selector and a
    /// discovery flag -- the one who most clearly meant discovery -- was told to add a selector.
    /// </summary>
    [Theory]
    [InlineData("--assembly-wide")]
    [InlineData("--top")]
    [InlineData("--max-results")]
    [InlineData("--max-methods")]
    public async Task Pairwise_OneSelectorWithADiscoveryFlag_PointsAtSimilar(string flag)
    {
        // Must run through the real parser: the check this pins lives in the command definition,
        // which rejects a missing second selector before MatchCommand.ExecuteAsync is ever called.
        string[] args = flag == "--assembly-wide"
            ? ["match", SampleSeed, "--library", TestAssembly, flag]
            : ["match", SampleSeed, "--library", TestAssembly, flag, "5"];

        var (exitCode, output, error) = await RunCliAsync(args);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains($"{flag} applies to discovery; add --similar.", error);
        Assert.DoesNotContain("requires two method selectors", error);
    }
}
