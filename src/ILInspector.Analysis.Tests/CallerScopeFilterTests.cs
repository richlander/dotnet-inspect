using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// <see cref="CallerScopeFilter"/> decides, from assembly identity alone, whether an assembly is
/// worth opening during cross-assembly caller discovery. Its only correctness obligation is that it
/// never rules out an assembly the matcher would have matched, so these tests pin the boundary in
/// both directions and use the same real fixture assemblies the matcher tests use.
/// </summary>
public class CallerScopeFilterTests
{
    static AssemblyIdentityNames Identity(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        return AssemblyIdentityScanner.Scan(peReader);
    }

    static bool CouldContainCallerOf(string targetAssemblyPath, string candidateAssemblyPath)
    {
        var candidate = Identity(candidateAssemblyPath);
        return CallerScopeFilter.CouldContainCallerOf(
            Identity(targetAssemblyPath).Name, candidate.Name, candidate.ReferenceNames);
    }

    // The real caller fixture references the target, so it must survive the filter and be opened.
    // This is the assembly BuildCallerTree_WithScope_* proves does contribute callers.
    [Fact]
    public void ReferencingAssemblyIsKept()
    {
        Assert.True(CouldContainCallerOf(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath()));
    }

    // The lookalike declares its own Target.Api.Ping and never references the target assembly.
    // The matcher already excludes it (BuildCallerTree ignores its calls); the filter must reach
    // the same answer, which is what makes skipping it an optimization rather than a behavior change.
    [Fact]
    public void NonReferencingAssemblyIsRuledOut()
    {
        Assert.False(CouldContainCallerOf(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphLookalikeCaller.AssemblyPath()));
    }

    // The candidate is the target: its own TypeDefs are callees the matcher can match.
    [Fact]
    public void TargetAssemblyItselfIsKept()
    {
        string target = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        Assert.True(CouldContainCallerOf(target, target));
    }

    // The facade case, and the reason the filter canonicalizes instead of comparing raw names.
    // Callers reference System.Runtime; the target is defined in System.Private.CoreLib. A raw name
    // comparison would rule out essentially every caller of a corelib member.
    [Theory]
    [InlineData("System.Runtime")]
    [InlineData("mscorlib")]
    [InlineData("netstandard")]
    [InlineData("System.Runtime.Extensions")]
    public void CorelibFacadeReferenceIsKept(string facade)
    {
        Assert.True(CallerScopeFilter.CouldContainCallerOf(
            "System.Private.CoreLib", "Some.Consumer", [facade]));
    }

    [Fact]
    public void UnrelatedReferencesAreRuledOut()
    {
        Assert.False(CallerScopeFilter.CouldContainCallerOf(
            "Target", "Some.Consumer", ["System.Runtime", "Newtonsoft.Json"]));
    }

    // Undecidable inputs must fail open: a filter that cannot tell has to let the real matcher decide.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UnknownTargetFailsOpen(string? target)
    {
        Assert.True(CallerScopeFilter.CouldContainCallerOf(target, "Some.Consumer", ["Unrelated"]));
    }

    [Fact]
    public void UnknownCandidateReferencesFailOpen()
    {
        Assert.True(CallerScopeFilter.CouldContainCallerOf("Target", "Some.Consumer", null));
    }

    // A reference-free image (no AssemblyRef rows) that is not the target cannot call into it.
    [Fact]
    public void EmptyReferenceSetIsRuledOut()
    {
        Assert.False(CallerScopeFilter.CouldContainCallerOf("Target", "Some.Consumer", ImmutableArray<string>.Empty));
    }
}
