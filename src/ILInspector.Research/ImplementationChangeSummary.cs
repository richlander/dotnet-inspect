using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler.Pipeline;
using ILInspector.ILDiff;
using ILInspector.Metadata;

namespace ILInspector.Research;

public sealed record ImplementationBodyPair(
    int BeforeMethodToken,
    int AfterMethodToken);

public enum ImplementationBodyChangeKind
{
    Exact,
    Changed,
    Unavailable,
}

public sealed record ImplementationBodyChange(
    ImplementationBodyPair Pair,
    ImplementationBodyChangeKind Kind,
    string? Failure);

/// <summary>
/// Classifies already-corresponded physical method bodies without constructing
/// implementation diff rows or decompiling either endpoint.
/// </summary>
public static class ImplementationChangeSummary
{
    public static IReadOnlyList<ImplementationBodyChange> Compare(
        ResolvedAssemblyReference beforeAssembly,
        IAssemblyReferenceResolver beforeResolver,
        ResolvedAssemblyReference afterAssembly,
        IAssemblyReferenceResolver afterResolver,
        IReadOnlyList<ImplementationBodyPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(beforeAssembly);
        ArgumentNullException.ThrowIfNull(beforeResolver);
        ArgumentNullException.ThrowIfNull(afterAssembly);
        ArgumentNullException.ThrowIfNull(afterResolver);
        ArgumentNullException.ThrowIfNull(pairs);

        using MetadataSource before =
            MetadataSource.OpenWithoutSymbols(
                beforeAssembly,
                beforeResolver);
        using MetadataSource after =
            MetadataSource.OpenWithoutSymbols(
                afterAssembly,
                afterResolver);

        var results =
            new List<ImplementationBodyChange>(pairs.Count);
        foreach (ImplementationBodyPair pair in pairs)
            results.Add(Compare(before, after, pair));
        return results;
    }

    static ImplementationBodyChange Compare(
        MetadataSource before,
        MetadataSource after,
        ImplementationBodyPair pair)
    {
        if (!TryGetBody(
                before,
                pair.BeforeMethodToken,
                out MethodBodyBlock? beforeBody,
                out string? beforeFailure))
        {
            return new(
                pair,
                ImplementationBodyChangeKind.Unavailable,
                beforeFailure);
        }
        if (!TryGetBody(
                after,
                pair.AfterMethodToken,
                out MethodBodyBlock? afterBody,
                out string? afterFailure))
        {
            return new(
                pair,
                ImplementationBodyChangeKind.Unavailable,
                afterFailure);
        }

        if (beforeBody is null || afterBody is null)
        {
            return new(
                pair,
                beforeBody is null && afterBody is null
                    ? ImplementationBodyChangeKind.Exact
                    : ImplementationBodyChangeKind.Changed,
                Failure: null);
        }

        IlBodyChangeSummary summary = IlBodyDiff.CompareSummary(
            before.Reader,
            beforeBody,
            after.Reader,
            afterBody);
        return new(
            pair,
            summary.Outcome switch
            {
                IlBodyChangeSummaryOutcome.Exact =>
                    ImplementationBodyChangeKind.Exact,
                IlBodyChangeSummaryOutcome.Changed =>
                    ImplementationBodyChangeKind.Changed,
                IlBodyChangeSummaryOutcome.Unavailable =>
                    ImplementationBodyChangeKind.Unavailable,
                _ => throw new InvalidOperationException(
                    "Unknown IL body summary outcome."),
            },
            summary.Failure);
    }

    static bool TryGetBody(
        MetadataSource source,
        int token,
        out MethodBodyBlock? body,
        out string? failure)
    {
        EntityHandle handle;
        try
        {
            handle = MetadataTokens.EntityHandle(token);
        }
        catch (ArgumentException error)
        {
            body = null;
            failure = error.Message;
            return false;
        }
        if (handle.Kind != HandleKind.MethodDefinition)
        {
            body = null;
            failure = $"Token 0x{token:X8} is not a MethodDef.";
            return false;
        }

        try
        {
            MethodDefinition method =
                source.Reader.GetMethodDefinition(
                    (MethodDefinitionHandle)handle);
            body = method.RelativeVirtualAddress == 0
                ? null
                : source.Pe.GetMethodBody(
                    method.RelativeVirtualAddress);
            failure = null;
            return true;
        }
        catch (Exception error) when (
            error is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            body = null;
            failure = error.Message;
            return false;
        }
    }
}
