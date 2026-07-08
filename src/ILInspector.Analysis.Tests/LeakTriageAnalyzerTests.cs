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
    public void DetailedAnalysis_ReportsMeasurementCandidatesWithoutChangingFindings()
    {
        var result = FixtureResult();

        AssertSingleShape(result.Findings, nameof(ArrayPoolLeakFixtures.UseAfterReturn), "arraypool-use-after-return");
        AssertSingleShape(result.Findings, nameof(ArrayPoolLeakFixtures.RentNotReturnedOnSomePath), "arraypool-rent-not-returned");
        AssertSingleShape(result.Findings, nameof(ArrayPoolLeakFixtures.DoubleReturn), "arraypool-double-return");

        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.UseAfterReturn), "use-after-return-candidate");
        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.RentNotReturnedOnSomePath), "normal-path-leak-candidate");
        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.DoubleReturn), "double-return-candidate");
    }

    [Fact]
    public void CatchAllCleanup_SuppressesExceptionPathCandidate()
    {
        var result = FixtureResult();

        // A catch-all (`catch {}` or `catch (Exception)`) that returns the buffer protects the
        // exception path, so no exception-path-leak-candidate (the JsonDocument.Parse FP class).
        AssertNoCandidate(result, nameof(ArrayPoolLeakFixtures.RentCrossCallCatchAllReturn), "exception-path-leak-candidate");
        AssertNoCandidate(result, nameof(ArrayPoolLeakFixtures.RentCrossCallCatchExceptionReturn), "exception-path-leak-candidate");

        // Near miss: a typed catch does not cover every exception type, so it stays a candidate.
        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.RentCrossCallTypedCatchReturn), "exception-path-leak-candidate");

        // Near miss: a sibling typed catch precedes the catch-all and can handle an exception
        // without releasing, so the catch-all must not be credited - stays a candidate.
        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.RentCrossCallSiblingTypedThenCatchAllReturn), "exception-path-leak-candidate");

        // Near miss: an inner typed catch on a nested try intercepts and swallows before the outer
        // catch-all, so the catch-all must not be credited - stays a candidate.
        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.RentNestedInnerCatchSwallowThenCatchAll), "exception-path-leak-candidate");

        // The fix is measurement-only: it changes no finding.
        Assert.Empty(ForMethod(result.Findings, nameof(ArrayPoolLeakFixtures.RentCrossCallCatchAllReturn)));
        Assert.Empty(ForMethod(result.Findings, nameof(ArrayPoolLeakFixtures.RentCrossCallCatchExceptionReturn)));
        Assert.Empty(ForMethod(result.Findings, nameof(ArrayPoolLeakFixtures.RentCrossCallTypedCatchReturn)));
    }

    [Fact]
    public void AmbiguousArrayPoolFixtures_FailClosed()
    {
        var findings = FixtureFindings();

        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.CrossMethodReturn)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.FieldStoredArray)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.NonSharedPoolRent)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.ReturnsRentedArray)));
    }

    [Fact]
    public void DetailedAnalysis_BucketsSuppressedOwnershipShapes()
    {
        var crossMethod = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            .. Call(TokenUnknown),
            0x2A,
        ], []);
        var fieldStore = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            0x80, .. TokenBytes(TokenField),
            0x2A,
        ], []);
        var returned = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            0x2A,
        ], []);

        AssertCandidate(crossMethod, nameof(Synthetic), "cross-method-suppressed");
        AssertCandidate(fieldStore, nameof(Synthetic), "alias-or-field-suppressed");
        AssertCandidate(returned, nameof(Synthetic), "ownership-transfer-suppressed");

        var result = FixtureResult();
        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.NonSharedPoolRent), "ownership-transfer-suppressed");
    }

    [Fact]
    public void DetailedAnalysis_CrossMethodBeforeReturn_IsExceptionPathCandidate()
    {
        var result = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            .. Call(TokenUnknown),
            .. Call(TokenShared),
            0x06,
            .. Callvirt(TokenReturn),
            0x2A,
        ], []);

        Assert.Empty(result.Findings);
        AssertCandidate(result, nameof(Synthetic), "cross-method-suppressed");
        AssertCandidate(result, nameof(Synthetic), "exception-path-leak-candidate");
    }

    [Fact]
    public void DetailedAnalysis_FinallyProtectedCrossMethod_DoesNotAddExceptionPathCandidate()
    {
        var result = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),       // IL_0000 ArrayPool<byte>.Shared
            0x1F, 0x10,                // IL_0005 ldc.i4.s 16
            .. Callvirt(TokenRent),     // IL_0007 Rent
            0x0A,                       // IL_000C stloc.0
            0x06,                       // IL_000D ldloc.0
            .. Call(TokenUnknown),      // IL_000E call Unknown.Use
            0xDE, 0x0C,                 // IL_0013 leave.s IL_0021
            .. Call(TokenShared),       // IL_0015 ArrayPool<byte>.Shared
            0x06,                       // IL_001A ldloc.0
            .. Callvirt(TokenReturn),   // IL_001B Return
            0xDC,                       // IL_0020 endfinally
            0x2A,                       // IL_0021 ret
        ], [Region(ExceptionRegionKind.Finally, tryOffset: 13, tryLength: 8, handlerOffset: 21, handlerLength: 12)]);

        Assert.Empty(result.Findings);
        AssertCandidate(result, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(result, nameof(Synthetic), "exception-path-leak-candidate");
    }

    [Fact]
    public void DetailedAnalysis_NonThrowingSetupBoundary_DoesNotAddExceptionPathCandidate()
    {
        var keepAlive = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            .. Call(TokenKeepAlive),
            0x2A,
        ], []);
        var arrayCopy = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            0x06,
            0x17,
            .. Call(TokenArrayCopy),
            0x2A,
        ], []);
        var systemMemorySpanClear = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            0x16,
            .. Call(TokenSystemMemoryAsSpan),
            .. Call(TokenSystemMemorySpanClear),
            0x2A,
        ], []);
        var arrayClear = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            0x16,
            0x1F, 0x10,
            .. Call(TokenArrayClear),
            0x2A,
        ], []);
        var spanCopyTo = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            .. Call(TokenSpanImplicit),
            .. Call(TokenSpanCopyTo),
            0x2A,
        ], []);
        var stagedSpanCopyTo = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            .. Call(TokenSpanImplicit),
            0x0B,
            0x12, 0x01,
            0x07,
            .. Call(TokenSpanCopyTo),
            0x2A,
        ], []);

        Assert.Empty(keepAlive.Findings);
        AssertCandidate(keepAlive, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(keepAlive, nameof(Synthetic), "exception-path-leak-candidate");

        Assert.Empty(arrayCopy.Findings);
        AssertCandidate(arrayCopy, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(arrayCopy, nameof(Synthetic), "exception-path-leak-candidate");

        Assert.Empty(systemMemorySpanClear.Findings);
        AssertCandidate(systemMemorySpanClear, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(systemMemorySpanClear, nameof(Synthetic), "exception-path-leak-candidate");

        Assert.Empty(arrayClear.Findings);
        AssertCandidate(arrayClear, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(arrayClear, nameof(Synthetic), "exception-path-leak-candidate");

        Assert.Empty(spanCopyTo.Findings);
        AssertCandidate(spanCopyTo, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(spanCopyTo, nameof(Synthetic), "exception-path-leak-candidate");

        Assert.Empty(stagedSpanCopyTo.Findings);
        AssertCandidate(stagedSpanCopyTo, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(stagedSpanCopyTo, nameof(Synthetic), "exception-path-leak-candidate");
    }

    [Fact]
    public void DetailedAnalysis_ThrowingBoundaryAfterSetup_IsExceptionPathCandidate()
    {
        var result = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            .. Call(TokenKeepAlive),
            0x06,
            .. Call(TokenUnknown),
            .. Call(TokenShared),
            0x06,
            .. Callvirt(TokenReturn),
            0x2A,
        ], []);

        Assert.Empty(result.Findings);
        AssertCandidate(result, nameof(Synthetic), "cross-method-suppressed");
        AssertCandidate(result, nameof(Synthetic), "exception-path-leak-candidate");
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
    public void DetailedAnalysis_IncompleteDataflowWithoutRent_HasNoSuppressedBucket()
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

        var result = LeakTriageAnalyzer.AnalyzeMethodDetailed(
            method,
            externalBranch,
            Array.Empty<ExceptionRegion>(),
            _ => MemberRef.Unsupported("not used"));

        Assert.Empty(result.Findings);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void DetailedAnalysis_IncompleteDataflowWithRent_IsSuppressedBucket()
    {
        var result = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x2B, 0x7F,
            0x2A,
        ], []);

        Assert.Empty(result.Findings);
        AssertCandidate(result, nameof(Synthetic), "incomplete-cfg-or-rd-suppressed");
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

    [Fact]
    public void DetailedAnalysis_ExceptionPathLeak_IsSeparateCandidateBucket()
    {
        var result = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            .. Newobj(TokenUnknown),
            0x7A,
        ], []);

        AssertSingleShape(result.Findings, nameof(Synthetic), "arraypool-rent-not-returned");
        AssertCandidate(result, nameof(Synthetic), "exception-path-leak-candidate");
    }

    static LeakTriageResult FixtureResult()
    {
        var result = LeakTriageAnalyzer.AnalyzeAssemblyDetailed(typeof(ArrayPoolLeakFixtures).Assembly.Location);
        return result with
        {
            Findings = [.. result.Findings.Where(finding => finding.Method.DeclaringType.Name == nameof(ArrayPoolLeakFixtures))],
            Candidates = [.. result.Candidates.Where(candidate => candidate.Method.DeclaringType.Name == nameof(ArrayPoolLeakFixtures))],
        };
    }

    static ImmutableArray<LeakTriageFinding> FixtureFindings()
        => FixtureResult().Findings;

    const int TokenShared = 0x0A000001;
    const int TokenRent = 0x0A000002;
    const int TokenReturn = 0x0A000003;
    const int TokenUnknown = 0x0A000004;
    const int TokenField = 0x0A000005;
    const int TokenKeepAlive = 0x0A000006;
    const int TokenArrayCopy = 0x0A000007;
    const int TokenSystemMemoryAsSpan = 0x0A000008;
    const int TokenSystemMemorySpanClear = 0x0A000009;
    const int TokenArrayClear = 0x0A00000A;
    const int TokenSpanCopyTo = 0x0A00000B;
    const int TokenSpanImplicit = 0x0A00000C;

    static ImmutableArray<LeakTriageFinding> AnalyzeSynthetic(byte[] il, IReadOnlyCollection<ExceptionRegion> exceptionRegions)
        => AnalyzeSyntheticDetailed(il, exceptionRegions).Findings;

    static LeakTriageResult AnalyzeSyntheticDetailed(byte[] il, IReadOnlyCollection<ExceptionRegion> exceptionRegions)
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
        return LeakTriageAnalyzer.AnalyzeMethodDetailed(method, il, exceptionRegions, ResolveSyntheticMember);
    }

    static MemberRef ResolveSyntheticMember(int token)
    {
        var arrayPoolOfByte = TypeRef.GenericInstance(
            TypeRef.Definition("System.Buffers", "System.Buffers", "ArrayPool`1"),
            [TypeRef.CoreLib("System", "Byte")]);
        var byteArray = TypeRef.SzArray(TypeRef.CoreLib("System", "Byte"));
        var systemMemorySpanOfByte = TypeRef.GenericInstance(
            TypeRef.Definition("System.Memory", "System", "Span`1", trustedFrameworkAssembly: true),
            [TypeRef.CoreLib("System", "Byte")]);
        var coreLibSpanOfByte = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Span`1"),
            [TypeRef.CoreLib("System", "Byte")]);

        return token switch
        {
            TokenShared => new MemberRef(arrayPoolOfByte, "get_Shared", [], arrayPoolOfByte, MemberKind.Method),
            TokenRent => new MemberRef(arrayPoolOfByte, "Rent", [TypeRef.CoreLib("System", "Int32")], byteArray, MemberKind.Method) { HasThis = true },
            TokenReturn => new MemberRef(arrayPoolOfByte, "Return", [byteArray], TypeRef.CoreLib("System", "Void"), MemberKind.Method) { HasThis = true },
            TokenUnknown => new MemberRef(TypeRef.Definition("Fixture", "Fixtures", "Unknown"), "Use", [], TypeRef.CoreLib("System", "Void"), MemberKind.Method),
            TokenKeepAlive => new MemberRef(TypeRef.CoreLib("System", "GC"), "KeepAlive", [TypeRef.CoreLib("System", "Object")], TypeRef.CoreLib("System", "Void"), MemberKind.Method),
            TokenArrayCopy => new MemberRef(
                TypeRef.CoreLib("System", "Array"),
                "Copy",
                [TypeRef.CoreLib("System", "Array"), TypeRef.CoreLib("System", "Array"), TypeRef.CoreLib("System", "Int32")],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            TokenSystemMemoryAsSpan => new MemberRef(
                TypeRef.Definition("System.Memory", "System", "MemoryExtensions", trustedFrameworkAssembly: true),
                "AsSpan",
                [byteArray, TypeRef.CoreLib("System", "Int32")],
                systemMemorySpanOfByte,
                MemberKind.Method),
            TokenSystemMemorySpanClear => new MemberRef(
                systemMemorySpanOfByte,
                "Clear",
                [],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method)
            { HasThis = true },
            TokenArrayClear => new MemberRef(
                TypeRef.CoreLib("System", "Array"),
                "Clear",
                [TypeRef.CoreLib("System", "Array"), TypeRef.CoreLib("System", "Int32"), TypeRef.CoreLib("System", "Int32")],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            TokenSpanCopyTo => new MemberRef(
                coreLibSpanOfByte,
                "CopyTo",
                [coreLibSpanOfByte],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method)
            { HasThis = true },
            TokenSpanImplicit => new MemberRef(
                coreLibSpanOfByte,
                "op_Implicit",
                [byteArray],
                coreLibSpanOfByte,
                MemberKind.Method),
            _ => MemberRef.Unsupported($"unknown token 0x{token:X8}"),
        };
    }

    static byte[] Call(int token) => [0x28, .. TokenBytes(token)];
    static byte[] Callvirt(int token) => [0x6F, .. TokenBytes(token)];
    static byte[] Newobj(int token) => [0x73, .. TokenBytes(token)];
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

    static void AssertCandidate(LeakTriageResult result, string methodName, string shape)
    {
        var candidate = Assert.Single(result.Candidates.Where(candidate => candidate.Method.Name == methodName && candidate.Shape == shape));
        Assert.Equal(shape, candidate.Shape);
    }

    static void AssertNoCandidate(LeakTriageResult result, string methodName, string shape)
        => Assert.DoesNotContain(result.Candidates, candidate => candidate.Method.Name == methodName && candidate.Shape == shape);

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

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static byte[] ReturnsRentedArray()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        return buffer;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowAfterRent()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        throw new InvalidOperationException(buffer.Length.ToString());
    }

    // A cross-method throwing boundary returned on both the normal path and a catch-ALL cleanup
    // (`catch {}`) - correct code with no try/finally. The exception path is protected, so it must
    // NOT produce an exception-path-leak-candidate once catch-all cleanup is modeled (mirrors
    // System.Text.Json's JsonDocument.Parse idiom).
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentCrossCallCatchAllReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            Sink(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    // Same shape with `catch (Exception)`, which also runs for every managed exception type.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentCrossCallCatchExceptionReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            Sink(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
        catch (Exception)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    // Near miss: a TYPED catch only covers InvalidOperationException; another exception type from
    // Sink would bypass it and leak, so this MUST still be an exception-path-leak-candidate.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentCrossCallTypedCatchReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            Sink(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
        catch (InvalidOperationException)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    // Near miss: a sibling typed catch precedes the catch-all. An InvalidOperationException is
    // handled by the FIRST catch, which does NOT return, so the buffer leaks - the catch-all must
    // NOT be credited, and this MUST still be an exception-path-leak-candidate.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentCrossCallSiblingTypedThenCatchAllReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            Sink(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    // Near miss: an INNER typed catch on a nested try intercepts the boundary's exception first
    // and swallows it without releasing, so the outer catch-all never runs for that exception -
    // the array leaks. The outer catch-all must NOT be credited (reviewers, PR #2521).
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentNestedInnerCatchSwallowThenCatchAll()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            try
            {
                Sink(buffer);
            }
            catch (InvalidOperationException)
            {
                return;
            }
            ArrayPool<byte>.Shared.Return(buffer);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    static void ReturnHelper(byte[] buffer)
        => ArrayPool<byte>.Shared.Return(buffer);

    static int Sink(byte[] buffer) => buffer.Length;

    static void Consume(int value)
    {
        if (value == int.MinValue)
            throw new InvalidOperationException();
    }
}
