using System.Buffers;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests;

public sealed class LeakTriageAnalyzerTests
{
    [Fact]
    public void CleanArrayPoolFixtures_ProduceZeroRows()
    {
        var findings = FixtureFindings();

        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.CorrectRentReturn)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.TryFinallyReturn)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.TryFinallyThrowReturn)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.ReturnOnAllPaths)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.CorrelatedReturnOnAllPaths)));
    }

    [Fact]
    public void MisuseArrayPoolFixtures_FireExactlyOnceEach()
    {
        var findings = FixtureFindings();

        AssertSingleShape(findings, nameof(ArrayPoolLeakFixtures.UseAfterReturn), "arraypool-use-after-return");
        AssertSingleShape(findings, nameof(ArrayPoolLeakFixtures.RentNotReturnedOnSomePath), "arraypool-rent-not-returned");
        AssertSingleShape(findings, nameof(ArrayPoolLeakFixtures.DoubleReturn), "arraypool-double-return");
    }

    [Fact]
    public void AmbiguousArrayPoolFixtures_FailClosed()
    {
        var findings = FixtureFindings();

        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.CrossMethodReturn)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.FieldStoredArray)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.NonSharedPoolRent)));
    }

    [Fact]
    public void IncompleteDataflow_FailsClosed()
    {
        byte[] externalBranch = [0x2B, 0x7F, 0x2A]; // br.s outside the method, then ret
        var method = new MethodIdentity(
            "Fixture",
            Guid.Empty,
            TypeRef.Definition("Fixture", "Fixtures", "Incomplete"),
            "Malformed",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);

        var findings = LeakTriageAnalyzer.AnalyzeMethod(
            method,
            externalBranch,
            Array.Empty<ExceptionRegion>(),
            _ => MemberRef.Unsupported("not used"));

        Assert.Empty(findings);
    }

    static ImmutableArray<LeakTriageFinding> FixtureFindings()
        => [.. LeakTriageAnalyzer.AnalyzeAssembly(typeof(ArrayPoolLeakFixtures).Assembly.Location)
            .Where(finding => finding.Method.DeclaringType.Name == nameof(ArrayPoolLeakFixtures))];

    static IEnumerable<LeakTriageFinding> ForMethod(ImmutableArray<LeakTriageFinding> findings, string methodName)
        => findings.Where(finding => finding.Method.Name == methodName);

    static void AssertSingleShape(ImmutableArray<LeakTriageFinding> findings, string methodName, string shape)
    {
        var finding = Assert.Single(ForMethod(findings, methodName));
        Assert.Equal(shape, finding.Shape);
    }
}

internal sealed class ArrayPoolLeakFixtures
{
    static byte[]? s_field;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CorrectRentReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        buffer[0] = 1;
        ArrayPool<byte>.Shared.Return(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void TryFinallyReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            buffer[0] = 1;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void TryFinallyThrowReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            throw new InvalidOperationException();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ReturnOnAllPaths(bool condition)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        if (condition)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            return;
        }

        ArrayPool<byte>.Shared.Return(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CorrelatedReturnOnAllPaths(bool condition)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        if (condition)
            ArrayPool<byte>.Shared.Return(buffer);
        if (!condition)
            ArrayPool<byte>.Shared.Return(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void UseAfterReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        ArrayPool<byte>.Shared.Return(buffer);
        buffer[0] = 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentNotReturnedOnSomePath(bool condition)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        if (condition)
            ArrayPool<byte>.Shared.Return(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void DoubleReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        ArrayPool<byte>.Shared.Return(buffer);
        ArrayPool<byte>.Shared.Return(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CrossMethodReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        ReturnHelper(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void FieldStoredArray()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        s_field = buffer;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NonSharedPoolRent(ArrayPool<byte> pool)
    {
        var buffer = pool.Rent(16);
        buffer[0] = 1;
    }

    static void ReturnHelper(byte[] buffer)
        => ArrayPool<byte>.Shared.Return(buffer);
}
