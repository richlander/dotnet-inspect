using System.Reflection;
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
/// The compiler owns the structural restriction:
/// <see cref="IrInvariants.Enabled"/> has a private setter, so a host cannot
/// lower either level by assignment, and the public-surface test holds the
/// process-wide configuration API to its documented shape. Runtime tests own
/// default behavior, environment precedence, and the consequence of a
/// disarmed suite.
/// </para>
/// </summary>
public sealed class IrInvariantsHostContractTests
{
    const string OptOutMethod = nameof(IrInvariants.DisableForShippedTool);

    const string EnvironmentVariable = "DOTNET_INSPECT_IR_INVARIANTS";

    /// <summary>The shipped host that deliberately declines validation on its hot path.</summary>
    const string ShippedToolEntryPoint = "src/DotnetInspect.Cli/Program.cs";

    /// <summary>
    /// Everything a host outside this assembly can reach. Instance members are
    /// included even though <see cref="IrInvariants"/> is static: dropping the
    /// <c>static</c> keyword is exactly how a public instance affordance would
    /// arrive, and the pin should fail loudly rather than stop applying.
    /// </summary>
    const BindingFlags PublicSurface =
        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

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
    /// line in a workflow <c>env:</c> block, with no
    /// <see cref="IrInvariants.DisableForShippedTool"/> call.
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
        Assert.True(IrInvariants.Enabled, WhyValidationIsOff());
    }

    /// <summary>
    /// Reports the cause that actually applies. A run can be disarmed two ways,
    /// and naming the wrong one is the same defect this test exists to fix: an
    /// operator told to unset a variable they never set has been pointed away
    /// from the code that declined for them.
    /// </summary>
    static string WhyValidationIsOff()
    {
        const string Consequence =
            " A disarmed run is not a passing run: every pipeline test in this assembly validates nothing.";

        if (EnvironmentRequest() is false)
        {
            string value = Environment.GetEnvironmentVariable(EnvironmentVariable) ?? string.Empty;
            return $"IR invariant validation is off because {EnvironmentVariable} was set to '{value}'. "
                + $"That request is honored on purpose.{Consequence} Unset the variable to restore coverage.";
        }

        return $"IR invariant validation is off with {EnvironmentVariable} unset, so it was declined in "
            + $"code — either the default flipped back to off or a host in this assembly called "
            + $"{OptOutMethod}(). The shipped use is {ShippedToolEntryPoint}.{Consequence}";
    }

    /// <summary>
    /// The structural half of the enforcement: neither level can be lowered by
    /// assignment, so no census, review habit, or naming convention has to catch
    /// that spelling. <see cref="IrInvariants.CheckSemantics"/> has no setter at
    /// all — since #3302 it is a computed projection of
    /// <see cref="IrInvariants.Enabled"/> — so it cannot be moved in either
    /// direction in-process, nor moved independently of the structural level.
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
    /// Every public member on this type changes what a host validates, so each
    /// one needs a host contract pinned here. Set equality, because the failure
    /// this catches is an <em>addition</em>: #3303 found
    /// <c>EnableSemanticChecks()</c> shipped with zero call sites and a doc
    /// naming a consumer — the corpus sweep — that
    /// <see cref="CorpusSweepGateTests"/> documents deliberately avoiding. An
    /// affordance no host uses is coverage-shaped silence: it reads as a
    /// supported way to move the level while nothing holds it to a contract,
    /// and it gives a future host a second process-wide control to drift on.
    /// Raising the semantic level has one spelling
    /// (<c>DOTNET_INSPECT_IR_INVARIANTS=full</c>) and threading it per call has
    /// another (<c>CheckInvariant(includeSemantics: true)</c>); neither needs a
    /// public mutator.
    /// <para>
    /// Members, not methods. A method is only one of the three ways to spell an
    /// entry point, and the other two are the ones with history:
    /// <c>public static bool Enabled { get; set; }</c> is exactly what this type
    /// carried before #3289 narrowed it, and a public static field is the same
    /// defect with less ceremony. Pinning only methods would let a future
    /// <c>ValidateEverything { get; set; }</c> reintroduce the original problem
    /// — a host moving validation with no census-visible call site — under a
    /// green test named for the whole surface. Property accessors are folded
    /// into the property they belong to so the pin reads as source, but nothing
    /// else is filtered: an operator or a field appears under its own name.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePublicSurfaceIsExactlyTheShippedToolOptOut()
    {
        var accessors = typeof(IrInvariants)
            .GetProperties(PublicSurface)
            .SelectMany(static property => property.GetAccessors(nonPublic: false))
            .Select(static accessor => accessor.Name)
            .ToHashSet(StringComparer.Ordinal);

        var surface = typeof(IrInvariants)
            .GetMembers(PublicSurface)
            .Where(member => !accessors.Contains(member.Name))
            .Select(static member => member.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { nameof(IrInvariants.CheckSemantics), OptOutMethod, nameof(IrInvariants.Enabled) },
            surface);
    }

    /// <summary>
    /// The semantic level is armed by default too (#3302), and by the same rule
    /// as <see cref="IrInvariants.Enabled"/>, so the two cannot drift apart. It
    /// was opt-in on the stated grounds that arming it suite-wide would
    /// false-positive on ~120 minimal fixtures; measured, it was five, and those
    /// five now declare the locals they use.
    /// </summary>
    [Fact]
    public void SemanticLevelIsOnByDefault_AndTracksTheStructuralLevel()
    {
        if (EnvironmentRequest() is false)
        {
            Assert.False(IrInvariants.CheckSemantics);
            return;
        }

        Assert.True(IrInvariants.CheckSemantics);
        Assert.Equal(IrInvariants.Enabled, IrInvariants.CheckSemantics);
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
}
