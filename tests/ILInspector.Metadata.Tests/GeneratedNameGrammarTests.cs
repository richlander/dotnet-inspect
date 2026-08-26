using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The shared compiler-generated name/shape grammar (#4692), extracted after
/// Analysis, the decompiler, and Metadata independently re-derived the same
/// shapes and drifted. This is shape parsing only; trust policy (attribute
/// gating, declared-owner requirements, API-surface visibility) is layered on
/// top by each consumer and is out of scope here.
/// </summary>
public class GeneratedNameGrammarTests
{
    [Theory]
    [InlineData("<>c__DisplayClass6_2", true)]
    [InlineData("<GetRegisteredTypes>d__10", true)]
    [InlineData("__StaticArrayInitTypeSize=16", true)]
    [InlineData("Outer", false)]
    [InlineData("ApiOutputFormatter", false)]
    public void IsGeneratedName_matches_leading_reserved_prefixes(string name, bool expected)
        => Assert.Equal(expected, GeneratedNameGrammar.IsGeneratedName(name));

    [Theory]
    [InlineData("Outer+<M>d__0", "<M>d__0")]
    [InlineData("Outer+Middle+<>c__DisplayClass0_0", "<>c__DisplayClass0_0")]
    [InlineData("Outer", "Outer")]
    [InlineData("", "")]
    public void LeafSegment_strips_everything_up_to_the_last_plus(string name, string expected)
        => Assert.Equal(expected, GeneratedNameGrammar.LeafSegment(name));

    [Theory]
    [InlineData("<>c__DisplayClass0_0", true)]
    [InlineData("<>c", false)] // the non-capturing lambda holder, not a display class
    [InlineData("<M>d__0", false)]
    [InlineData("Outer", false)]
    public void IsDisplayClassLeaf_requires_the_display_class_prefix(string leaf, bool expected)
        => Assert.Equal(expected, GeneratedNameGrammar.IsDisplayClassLeaf(leaf));

    [Theory]
    [InlineData("<M>d__0", true)]
    [InlineData("<M>d__0`1", true)] // generic iterator/async state machine
    [InlineData("<>c__DisplayClass0_0", false)]
    [InlineData("Outer", false)]
    public void IsStateMachineLeaf_requires_the_state_machine_infix(string leaf, bool expected)
        => Assert.Equal(expected, GeneratedNameGrammar.IsStateMachineLeaf(leaf));

    [Theory]
    [InlineData("<M>g__Helper|0_0", true)]
    [InlineData("M", false)]
    [InlineData("<M>b__0_0", false)]
    public void IsLocalFunctionMethodName_requires_the_local_function_infix(string name, bool expected)
        => Assert.Equal(expected, GeneratedNameGrammar.IsLocalFunctionMethodName(name));

    [Theory]
    [InlineData("<M>b__0_0", true)]
    [InlineData("M", false)]
    [InlineData("<M>g__Helper|0_0", false)]
    public void IsLambdaMethodName_requires_the_lambda_infix(string name, bool expected)
        => Assert.Equal(expected, GeneratedNameGrammar.IsLambdaMethodName(name));

    [Theory]
    // A leading '<' plus the matching infix is required together; either alone is not enough.
    [InlineData("<M>g__Helper|0_0", true)]
    [InlineData("M_g__Helper|0_0", false)] // carries the infix, but not the leading '<'
    [InlineData("<M>b__0_0", false)] // leading '<', but the lambda infix, not local-function
    public void IsSynthesizedLocalFunctionName_requires_both_marks(string name, bool expected)
        => Assert.Equal(expected, GeneratedNameGrammar.IsSynthesizedLocalFunctionName(name));

    [Theory]
    [InlineData("<M>b__0_0", true)]
    [InlineData("M_b__0_0", false)]
    [InlineData("<M>g__Helper|0_0", false)]
    public void IsSynthesizedLambdaMethodName_requires_both_marks(string name, bool expected)
        => Assert.Equal(expected, GeneratedNameGrammar.IsSynthesizedLambdaMethodName(name));

    [Theory]
    [InlineData("<>1__state", true)]
    [InlineData("<>2__current", true)]
    [InlineData("<i>5__2", true)]
    [InlineData("<>9__0_0", true)]
    [InlineData("count", false)]
    [InlineData("", false)]
    public void IsGeneratedFieldName_matches_any_leading_angle_bracket(string name, bool expected)
        => Assert.Equal(expected, GeneratedNameGrammar.IsGeneratedFieldName(name));

    [Theory]
    // A hoisted local carries a single '<' plus the hoist infix.
    [InlineData("<i>5__2", true)]
    [InlineData("<text>5__1", true)]
    // Pure state-machine plumbing wears the '<>' double prefix, not a hoist.
    [InlineData("<>1__state", false)]
    [InlineData("<>2__current", false)]
    [InlineData("<>9__0_0", false)]
    // A single-angle generated name without the hoist infix is not a hoisted local.
    [InlineData("<M>b__0_0", false)]
    [InlineData("<M>d__0", false)]
    [InlineData("count", false)]
    public void IsHoistedLocalFieldName_requires_single_angle_prefix_and_hoist_infix(string name, bool expected)
        => Assert.Equal(expected, GeneratedNameGrammar.IsHoistedLocalFieldName(name));
}
