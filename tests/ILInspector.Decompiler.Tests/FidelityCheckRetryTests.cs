using System.Collections.Immutable;

using ILInspector.DecompilerHarness;

using Microsoft.CodeAnalysis;

namespace ILInspector.Decompiler.Tests;

public class FidelityCheckRetryTests
{
    [Fact]
    public void TransientEmptyEmit_RetriesAndStopsOnSuccess()
    {
        var attempts = FidelityCheck.TransientEmptyEmitAttemptCount(attempt =>
            attempt == 1
                ? new FidelityCheck.EmitAttempt(false, 0, ImmutableArray<Diagnostic>.Empty)
                : new FidelityCheck.EmitAttempt(true, 128, ImmutableArray<Diagnostic>.Empty));

        Assert.Equal(2, attempts);
    }

    [Fact]
    public void CompilerErrorEmit_DoesNotRetry()
    {
        var attempts = FidelityCheck.TransientEmptyEmitAttemptCount(_ =>
            new FidelityCheck.EmitAttempt(false, 0, ImmutableArray.Create(ErrorDiagnostic("CS1002"))));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void PersistentTransientEmptyEmit_IsBounded()
    {
        var attempts = FidelityCheck.TransientEmptyEmitAttemptCount(_ =>
            new FidelityCheck.EmitAttempt(false, 0, ImmutableArray<Diagnostic>.Empty));

        Assert.Equal(3, attempts);
    }

    static Diagnostic ErrorDiagnostic(string id)
        => Diagnostic.Create(
            new DiagnosticDescriptor(
                id,
                "compile error",
                "compile error",
                "test",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true),
            Location.None);
}
