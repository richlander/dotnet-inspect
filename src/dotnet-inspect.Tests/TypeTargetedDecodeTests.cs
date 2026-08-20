using System.Collections.Immutable;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Tests;

/// <summary>
/// Locks the type-targeted-decode invariant: opening a <see cref="Analysis.LibraryBodyIndex"/> with a
/// <c>bodyTypeScope</c> predicate restricted to one declaring type decodes bodies physically owned
/// by that type plus compiler-lifted bodies attributed to it, while producing per-method facts
/// (direct calls, unsafe evidence, unsafety occurrences, allocation occurrences) identical to the
/// full whole-assembly build for every physical method of that type. This is what lets the type
/// command render Unsafe Members / Called Types / Allocation-Safety-Cost facts for one source type,
/// including its private methods and attributed compiler-generated bodies, without decoding every
/// unrelated body in the assembly. Unlike a token <c>bodyScope</c> (member command), a
/// declaring-type predicate is required because type sections scan all evidence owned by the type,
/// not just its public API.
/// </summary>
public class TypeTargetedDecodeTests
{
    static string SelfPath => typeof(Analysis.LibraryBodyIndex).Assembly.Location;

    static string Facts<T>(IReadOnlyDictionary<int, ImmutableArray<T>> byToken, int token)
        => byToken.TryGetValue(token, out var v)
            ? string.Join(";", v.Select(x => x!.ToString()).OrderBy(s => s, StringComparer.Ordinal))
            : "";

    static Analysis.TypeRef LargestType(Analysis.LibraryBodyIndex full)
        => full.Methods
            .GroupBy(m => m.DeclaringType)
            .OrderByDescending(g => g.Count())
            .First().Key;

    [Fact]
    public void TypeTargetedBuild_MatchesFullBuild_ForEveryMethodOfTheType()
    {
        var full = Analysis.LibraryBodyIndex.Open(SelfPath);
        var fullCalls = full.GetDirectCallsByEvidenceMethod();
        var fullUnsafe = full.GetUnsafeEvidenceByMember();
        var fullUnsafety = full.GetUnsafetyOccurrences();
        var fullAlloc = full.GetAllocationOccurrences();

        var target = LargestType(full);
        var tokens = full.Methods.Where(m => m.DeclaringType.Equals(target)).Select(m => m.MetadataToken).ToArray();
        Assert.NotEmpty(tokens);

        var targeted = Analysis.LibraryBodyIndex.Open(SelfPath, bodyTypeScope: tr => tr.Equals(target));
        var tCalls = targeted.GetDirectCallsByEvidenceMethod();
        var tUnsafe = targeted.GetUnsafeEvidenceByMember();
        var tUnsafety = targeted.GetUnsafetyOccurrences();
        var tAlloc = targeted.GetAllocationOccurrences();

        foreach (var token in tokens)
        {
            Assert.Equal(Facts(fullCalls, token), Facts(tCalls, token));
            Assert.Equal(Facts(fullUnsafe, token), Facts(tUnsafe, token));
            Assert.Equal(Facts(fullUnsafety, token), Facts(tUnsafety, token));
            Assert.Equal(Facts(fullAlloc, token), Facts(tAlloc, token));
        }
    }

    [Fact]
    public void TypeTargetedBuild_PreservesDeclaredCallerForScopedLiftedBody()
    {
        var full = Analysis.LibraryBodyIndex.Open(SelfPath);
        Analysis.DirectCall attributed = full.DirectCalls.First(
            call => call.Caller != call.EvidenceMethod);
        Analysis.TypeRef evidenceType =
            attributed.EvidenceMethod.DeclaringType;

        var targeted = Analysis.LibraryBodyIndex.Open(
            SelfPath,
            bodyTypeScope: type => type.Equals(evidenceType));

        Assert.Contains(
            attributed,
            targeted.DirectCalls);
    }

    [Fact]
    public void TypeTargetedBuild_AdmitsOnlyScopedPhysicalOrDeclaredSourceBodies()
    {
        var full = Analysis.LibraryBodyIndex.Open(SelfPath);
        Analysis.TypeRef target = full.Methods
            .Select(method => method.DeclaringType)
            .Distinct()
            .Single(type =>
                type.Namespace == "ILInspector.Analysis"
                && type.Name == nameof(Analysis.CallGraphMemberResolver));
        Analysis.DirectCall lifted = full.DirectCalls.First(call =>
            call.Caller.DeclaringType.Equals(target)
            && !call.EvidenceMethod.DeclaringType.Equals(target));
        var physicalTokens = full.Methods
            .Where(method => method.DeclaringType.Equals(target))
            .Select(method => method.MetadataToken)
            .ToHashSet();

        var targeted = Analysis.LibraryBodyIndex.Open(
            SelfPath,
            bodyTypeScope: type => type.Equals(target));
        var callsByEvidence = targeted.DirectCalls
            .GroupBy(call => call.EvidenceMethod.MetadataToken)
            .ToArray();

        foreach (var calls in callsByEvidence)
        {
            Assert.True(
                physicalTokens.Contains(calls.Key)
                || calls.All(call =>
                    call.Caller.DeclaringType.Equals(target)));
        }

        Assert.Contains(
            callsByEvidence,
            calls => !physicalTokens.Contains(calls.Key));
        Assert.Contains(lifted, targeted.DirectCalls);
    }
}
