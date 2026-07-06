using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Slice-1 census behavior (#2439): the measurement-only ArrayPool lifecycle census partitions
/// recognized acquires into candidate and suppression buckets without touching the finding path
/// (the unchanged <see cref="LeakTriageAnalyzer"/> findings are pinned by
/// <see cref="LeakTriageAnalyzerTests"/>). Candidate buckets are asserted against the shared
/// <see cref="ArrayPoolLeakFixtures"/>; escape/incomplete suppression buckets need synthetic IL
/// because the release-build fixtures for those shapes never store the rented array to a local,
/// so they are not recognized as acquires at all.
/// </summary>
public sealed class ResourceLifecycleCensusTests
{
    [Fact]
    public void CandidateBuckets_MatchFixtureShapes()
    {
        Assert.Equal(
            ["exception-path-leak-candidate"],
            BucketsFor(nameof(ArrayPoolLeakFixtures.CorrectRentReturn)));

        Assert.Equal(
            ["exception-path-leak-candidate", "normal-path-leak-candidate"],
            BucketsFor(nameof(ArrayPoolLeakFixtures.RentNotReturnedOnSomePath)));

        Assert.Equal(
            ["exception-path-leak-candidate", "use-after-return-candidate"],
            BucketsFor(nameof(ArrayPoolLeakFixtures.UseAfterReturn)));

        Assert.Equal(
            ["double-return-candidate", "exception-path-leak-candidate"],
            BucketsFor(nameof(ArrayPoolLeakFixtures.DoubleReturn)));

        // Correlated multi-release: the census is deliberately raw (no predicate facts), so the
        // impossible (c && !c)-false path shows up as both a normal-path leak and a double return.
        // This is the pre-graduation over-count Slice 4 must refine, not a defect.
        Assert.Equal(
            ["double-return-candidate", "exception-path-leak-candidate", "normal-path-leak-candidate"],
            BucketsFor(nameof(ArrayPoolLeakFixtures.CorrelatedReturnOnAllPaths)));

        // Correct return on every branch, but no try/finally: only exception-unsafe.
        Assert.Equal(
            ["exception-path-leak-candidate"],
            BucketsFor(nameof(ArrayPoolLeakFixtures.ReturnOnAllPaths)));
    }

    [Fact]
    public void FinallyProtectedFixtures_ProduceNoFacts()
    {
        // A Return inside a covering finally/fault is safe on both normal and exception paths, so
        // it hits no bucket at all - including the nested finally/leave shape from #2380.
        Assert.Empty(BucketsFor(nameof(ArrayPoolLeakFixtures.TryFinallyReturn)));
        Assert.Empty(BucketsFor(nameof(ArrayPoolLeakFixtures.TryFinallyThrowReturn)));
        Assert.Empty(BucketsFor(nameof(ArrayPoolLeakFixtures.NestedFinallyLeaveReturn)));
    }

    [Fact]
    public void UnmodeledCall_SuppressedAsCrossMethod()
    {
        var result = CensusSynthetic([
            .. Call(TokenShared),        // IL_0000 ArrayPool<byte>.Shared
            0x1F, 0x10,                  // IL_0005 ldc.i4.s 16
            .. Callvirt(TokenRent),      // IL_0007 Rent
            0x0A,                        // IL_000C stloc.0
            0x06,                        // IL_000D ldloc.0
            .. Call(TokenConsume),       // IL_000E Consume(byte[])  (unmodeled)
            0x2A,                        // IL_0013 ret
        ]);

        var fact = Assert.Single(result.Facts);
        Assert.Equal("cross-method-suppressed", fact.Bucket);
        Assert.Equal(1, result.AcquiresObserved);
    }

    [Fact]
    public void FieldStore_SuppressedAsAliasOrField()
    {
        var result = CensusSynthetic([
            .. Call(TokenShared),        // IL_0000
            0x1F, 0x10,                  // IL_0005 ldc.i4.s 16
            .. Callvirt(TokenRent),      // IL_0007
            0x0A,                        // IL_000C stloc.0
            0x06,                        // IL_000D ldloc.0
            0x80, 0x01, 0x00, 0x00, 0x04, // IL_000E stsfld (field token)
            0x2A,                        // IL_0013 ret
        ]);

        var fact = Assert.Single(result.Facts);
        Assert.Equal("alias-or-field-suppressed", fact.Bucket);
    }

    [Fact]
    public void LocalAlias_SuppressedAsAliasOrField()
    {
        var result = CensusSynthetic([
            .. Call(TokenShared),        // IL_0000
            0x1F, 0x10,                  // IL_0005 ldc.i4.s 16
            .. Callvirt(TokenRent),      // IL_0007
            0x0A,                        // IL_000C stloc.0
            0x06,                        // IL_000D ldloc.0
            0x0B,                        // IL_000E stloc.1 (alias)
            0x2A,                        // IL_000F ret
        ]);

        var fact = Assert.Single(result.Facts);
        Assert.Equal("alias-or-field-suppressed", fact.Bucket);
    }

    [Fact]
    public void ReturnedArray_SuppressedAsOwnershipTransfer()
    {
        var result = CensusSynthetic([
            .. Call(TokenShared),        // IL_0000
            0x1F, 0x10,                  // IL_0005 ldc.i4.s 16
            .. Callvirt(TokenRent),      // IL_0007
            0x0A,                        // IL_000C stloc.0
            0x06,                        // IL_000D ldloc.0
            0x2A,                        // IL_000E ret (returns the rented array)
        ]);

        var fact = Assert.Single(result.Facts);
        Assert.Equal("ownership-transfer-suppressed", fact.Bucket);
    }

    [Fact]
    public void RentWithIncompleteCfg_SuppressedAsIncomplete()
    {
        // A br.s to an offset outside the method leaves an unmodeled external edge, so the CFG is
        // incomplete; a Rent is present, so the method is measured as incomplete rather than dropped.
        var result = CensusSynthetic([
            .. Call(TokenShared),        // IL_0000
            0x1F, 0x10,                  // IL_0005 ldc.i4.s 16
            .. Callvirt(TokenRent),      // IL_0007
            0x0A,                        // IL_000C stloc.0
            0x2B, 0x7F,                  // IL_000D br.s +127 (external target)
            0x2A,                        // IL_000F ret
        ]);

        var fact = Assert.Single(result.Facts);
        Assert.Equal("incomplete-cfg-or-rd-suppressed", fact.Bucket);
        Assert.Equal(0, result.AcquiresObserved);
    }

    [Fact]
    public void MethodWithoutRent_ProducesNothing()
    {
        var result = CensusSynthetic([0x2A]); // ret
        Assert.Empty(result.Facts);
        Assert.Equal(0, result.AcquiresObserved);
    }

    static ImmutableArray<string> BucketsFor(string methodName)
        => [.. ResourceLifecycleCensus.CensusAssembly(typeof(ArrayPoolLeakFixtures).Assembly.Location)
            .Facts
            .Where(f => f.Method.DeclaringType.Name == nameof(ArrayPoolLeakFixtures) && f.Method.Name == methodName)
            .Select(f => f.Bucket)
            .Distinct()
            .OrderBy(b => b, StringComparer.Ordinal)];

    const int TokenShared = 0x0A000001;
    const int TokenRent = 0x0A000002;
    const int TokenReturn = 0x0A000003;
    const int TokenConsume = 0x0A000004;

    static ResourceLifecycleCensusResult CensusSynthetic(byte[] il)
    {
        var byteArray = TypeRef.SzArray(TypeRef.CoreLib("System", "Byte"));
        var method = new MethodIdentity(
            "Fixture",
            Guid.Empty,
            TypeRef.Definition("Fixture", "Fixtures", "Synthetic"),
            "Synthetic",
            [],
            byteArray,
            0x06000001,
            IsStatic: true);
        return ResourceLifecycleCensus.CensusMethod(method, il, Array.Empty<ExceptionRegion>(), ResolveSyntheticMember);
    }

    static MemberRef ResolveSyntheticMember(int token)
    {
        var arrayPoolOfByte = TypeRef.GenericInstance(
            TypeRef.Definition("System.Buffers", "System.Buffers", "ArrayPool`1"),
            [TypeRef.CoreLib("System", "Byte")]);
        var byteArray = TypeRef.SzArray(TypeRef.CoreLib("System", "Byte"));

        return token switch
        {
            TokenShared => new MemberRef(arrayPoolOfByte, "get_Shared", [], arrayPoolOfByte, MemberKind.Method),
            TokenRent => new MemberRef(arrayPoolOfByte, "Rent", [TypeRef.CoreLib("System", "Int32")], byteArray, MemberKind.Method) { HasThis = true },
            TokenReturn => new MemberRef(arrayPoolOfByte, "Return", [byteArray], TypeRef.CoreLib("System", "Void"), MemberKind.Method) { HasThis = true },
            TokenConsume => new MemberRef(TypeRef.Definition("Fixture", "Fixtures", "Helper"), "Consume", [byteArray], TypeRef.CoreLib("System", "Void"), MemberKind.Method),
            _ => MemberRef.Unsupported($"unknown token 0x{token:X8}"),
        };
    }

    static byte[] Call(int token) => [0x28, .. BitConverter.GetBytes(token)];
    static byte[] Callvirt(int token) => [0x6F, .. BitConverter.GetBytes(token)];
}
