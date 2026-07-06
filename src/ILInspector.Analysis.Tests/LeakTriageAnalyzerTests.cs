using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
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
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.NestedFinallyLeaveReturn)));
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

    [Fact]
    public void FaultHandlerReturn_DoesNotSatisfyNormalLeavePath()
    {
        var findings = AnalyzeSynthetic([
            .. Call(TokenShared),       // IL_0000 ArrayPool<byte>.Shared
            0x1F, 0x10,                // IL_0005 ldc.i4.s 16
            .. Callvirt(TokenRent),     // IL_0007 Rent
            0x0A,                       // IL_000C stloc.0
            0xDE, 0x0C,                 // IL_000D leave.s IL_001B
            .. Call(TokenShared),       // IL_000F ArrayPool<byte>.Shared
            0x06,                       // IL_0014 ldloc.0
            .. Callvirt(TokenReturn),   // IL_0015 Return
            0xDC,                       // IL_001A endfinally
            0x2A,                       // IL_001B ret
        ], [Region(ExceptionRegionKind.Fault, tryOffset: 13, tryLength: 2, handlerOffset: 15, handlerLength: 12)]);

        AssertSingleShape(findings, nameof(Synthetic), "arraypool-rent-not-returned");
    }

    static ImmutableArray<LeakTriageFinding> FixtureFindings()
        => [.. LeakTriageAnalyzer.AnalyzeAssembly(typeof(ArrayPoolLeakFixtures).Assembly.Location)
            .Where(finding => finding.Method.DeclaringType.Name == nameof(ArrayPoolLeakFixtures))];

    const int TokenShared = 0x0A000001;
    const int TokenRent = 0x0A000002;
    const int TokenReturn = 0x0A000003;

    static ImmutableArray<LeakTriageFinding> AnalyzeSynthetic(byte[] il, IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        var method = new MethodIdentity(
            "Fixture",
            Guid.Empty,
            TypeRef.Definition("Fixture", "Fixtures", nameof(Synthetic)),
            nameof(Synthetic),
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);
        return LeakTriageAnalyzer.AnalyzeMethod(method, il, exceptionRegions, ResolveSyntheticMember);
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
            _ => MemberRef.Unsupported($"unknown token 0x{token:X8}"),
        };
    }

    static byte[] Call(int token) => [0x28, .. TokenBytes(token)];
    static byte[] Callvirt(int token) => [0x6F, .. TokenBytes(token)];
    static byte[] TokenBytes(int token) => BitConverter.GetBytes(token);

    static readonly ConstructorInfo s_exceptionRegionConstructor =
        typeof(ExceptionRegion).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(ExceptionRegionKind), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int)],
            modifiers: null)
        ?? throw new InvalidOperationException("ExceptionRegion constructor not found.");

    static ExceptionRegion Region(
        ExceptionRegionKind kind,
        int tryOffset,
        int tryLength,
        int handlerOffset,
        int handlerLength,
        int filterOffset = 0)
        => (ExceptionRegion)s_exceptionRegionConstructor.Invoke([kind, tryOffset, tryLength, handlerOffset, handlerLength, filterOffset]);

    static IEnumerable<LeakTriageFinding> ForMethod(ImmutableArray<LeakTriageFinding> findings, string methodName)
        => findings.Where(finding => finding.Method.Name == methodName);

    static void AssertSingleShape(ImmutableArray<LeakTriageFinding> findings, string methodName, string shape)
    {
        var finding = Assert.Single(ForMethod(findings, methodName));
        Assert.Equal(shape, finding.Shape);
    }
}

internal static class Synthetic
{
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
    public static void NestedFinallyLeaveReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            try
            {
                goto Done;
            }
            finally
            {
                Consume(1);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

    Done:
        return;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentThenCallBeforeTryFinally()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        Consume(1); // throwing call in the gap before the protecting try - an exception here skips the finally
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

    static void Consume(int value)
    {
        if (value == int.MinValue)
            throw new InvalidOperationException();
    }
}
