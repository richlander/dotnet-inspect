using System.Diagnostics.CodeAnalysis;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace ILInspector.Research;

/// <summary>
/// Composes Metadata, Instructions, Analysis, and SourceLink evidence at one IL coordinate.
/// </summary>
public static class ILOffsetProjectionProducer
{
    public static ILOffsetProjectionOutcome Produce(ILOffsetProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);

        var context = request.Source.Context;
        if (!context.HasMetadata)
        {
            return ILOffsetProjectionOutcome.Failed(
                ILOffsetProjectionFailureKind.NoMetadata,
                "No metadata in library.");
        }

        var memberContext = context.ResolveMemberContext(request.MethodToken, request.ILOffset);
        if (memberContext is null)
        {
            return ILOffsetProjectionOutcome.Failed(
                ILOffsetProjectionFailureKind.MemberUnavailable,
                $"Could not resolve member context for token 0x{request.MethodToken:X}.",
                "The method token may be invalid or may not identify a MethodDef row.");
        }

        var bodySource = context.MethodBodies;
        var decoded = bodySource.TryRead(
            request.MethodToken,
            out var methodBody,
            out var decodeError);
        MethodInstructions? methodInstructions = null;
        if (decoded)
        {
            methodInstructions = MethodInstructions.Decode(methodBody!);
            if (!methodInstructions.IsComplete)
            {
                decodeError = $"Could not decode IL for token 0x{request.MethodToken:X}: "
                    + methodInstructions.Blocks.IncompleteReason;
                methodInstructions = null;
                decoded = false;
            }
        }

        ILOffsetInstructionContextInfo? instructionContext = null;
        string? instructionError = decodeError == $"Could not decode IL for token 0x{request.MethodToken:X}."
            ? $"Could not resolve instruction context for token 0x{request.MethodToken:X}+0x{request.ILOffset:X}."
            : decodeError;
        if (decoded)
        {
            instructionContext = InstructionContextResolver.ResolveInstructionContext(
                methodInstructions!,
                request.MethodToken,
                request.ILOffset,
                bodySource,
                out instructionError);
        }

        if (instructionContext is null
            && Includes(request, ILOffsetProjectionCapabilities.InstructionContext))
        {
            return ILOffsetProjectionOutcome.Failed(
                ILOffsetProjectionFailureKind.InstructionUnavailable,
                instructionError
                    ?? $"Could not resolve instruction context for token 0x{request.MethodToken:X}+0x{request.ILOffset:X}.");
        }

        var exceptionContext = context.ResolveExceptionContext(
            request.MethodToken,
            request.ILOffset,
            out var exceptionError);
        if (exceptionError is not null
            && Includes(request, ILOffsetProjectionCapabilities.ExceptionContext))
        {
            return ILOffsetProjectionOutcome.Failed(
                ILOffsetProjectionFailureKind.ExceptionUnavailable,
                exceptionError);
        }

        ILOffsetCallsiteContextInfo? callsiteContext = null;
        string? callsiteError = decodeError;
        if (decoded)
        {
            callsiteContext = InstructionContextResolver.ResolveCallsiteContext(
                methodInstructions!,
                request.MethodToken,
                request.ILOffset,
                bodySource,
                out callsiteError);
        }

        if (callsiteError is not null
            && Includes(request, ILOffsetProjectionCapabilities.CallsiteContext))
        {
            return ILOffsetProjectionOutcome.Failed(
                ILOffsetProjectionFailureKind.CallsiteUnavailable,
                callsiteError);
        }

        ILOffsetReturnAddressContextInfo? returnAddressContext = null;
        string? returnAddressError = decodeError;
        if (decoded)
        {
            returnAddressContext = InstructionContextResolver.ResolveReturnAddressContext(
                methodInstructions!,
                request.MethodToken,
                request.ILOffset,
                bodySource,
                out returnAddressError);
        }

        if (returnAddressError is not null
            && Includes(request, ILOffsetProjectionCapabilities.ReturnAddressContext))
        {
            return ILOffsetProjectionOutcome.Failed(
                ILOffsetProjectionFailureKind.ReturnAddressUnavailable,
                returnAddressError);
        }

        List<ILOffsetAllocationContext>? allocationContext = null;
        List<ILOffsetSafetyContext>? safetyContext = null;
        List<ILOffsetCostContext>? costContext = null;
        bool wantsAllocation = Includes(request, ILOffsetProjectionCapabilities.AllocationContext);
        bool wantsSafety = Includes(request, ILOffsetProjectionCapabilities.SafetyContext);
        bool wantsCost = Includes(request, ILOffsetProjectionCapabilities.CostContext);
        if (wantsAllocation || wantsSafety || wantsCost)
        {
            // One shared, cached index acquisition for all three semantic contexts: opening the
            // library body index is expensive, and each context is just a different filtered
            // projection over the same Analysis evidence.
            if (!TryOpenAnalysisIndex(context.AssemblyPath, out var index, out var indexError))
            {
                var failureKind = wantsAllocation
                    ? ILOffsetProjectionFailureKind.AllocationAnalysisUnavailable
                    : wantsSafety
                        ? ILOffsetProjectionFailureKind.SafetyAnalysisUnavailable
                        : ILOffsetProjectionFailureKind.CostAnalysisUnavailable;
                return ILOffsetProjectionOutcome.Failed(failureKind, indexError);
            }
            if (wantsAllocation)
                allocationContext = BuildAllocationContext(index, request.MethodToken, request.ILOffset);
            if (wantsSafety)
                safetyContext = BuildSafetyContext(index, context.AssemblyPath, request.MethodToken, request.ILOffset);
            if (wantsCost)
                costContext = BuildCostContext(index, request.MethodToken, request.ILOffset);
        }

        SourceLinkResolver.ILOffsetSourceInfo? source = null;
        if (Includes(request, ILOffsetProjectionCapabilities.SourceLocation))
        {
            if (!request.Source.HasPdb)
            {
                return ILOffsetProjectionOutcome.Failed(
                    ILOffsetProjectionFailureKind.SourceUnavailable,
                    "No readable PDB found.");
            }

            source = request.Source.ResolveByILOffset(request.MethodToken, request.ILOffset);
            if (source is null)
            {
                return ILOffsetProjectionOutcome.Failed(
                    ILOffsetProjectionFailureKind.SourceUnavailable,
                    $"Could not resolve source location for token 0x{request.MethodToken:X}+0x{request.ILOffset:X}.",
                    "The method token may be invalid or the PDB may not contain sequence points for this method.");
            }
        }

        string? url = request.BrowsableUrls ? source?.GitHubBrowseUrl : source?.SourceUrl;
        if (url is not null)
            url += $"#L{source!.Line}";

        return ILOffsetProjectionOutcome.Success(new ILOffsetProjection
        {
            Method = source?.MethodName ?? memberContext.Member,
            Token = $"0x{request.MethodToken:X}",
            ILOffset = $"0x{request.ILOffset:X}",
            MatchedOffset = source is not null && source.MatchedOffset != request.ILOffset
                ? $"0x{source.MatchedOffset:X}"
                : null,
            File = source?.FilePath,
            Line = source?.Line,
            Url = url,
            MemberContext = new ILOffsetMemberContext
            {
                Assembly = memberContext.Assembly,
                Type = memberContext.Type,
                TypeKind = memberContext.TypeKind,
                Member = memberContext.Member,
                Signature = memberContext.Signature,
                MemberKind = memberContext.MemberKind,
                Visibility = memberContext.Visibility,
                Static = memberContext.Static ? "Yes" : "No",
                Async = memberContext.Async,
                MetadataToken = $"0x{memberContext.MetadataToken:X}",
                ILOffset = $"0x{memberContext.ILOffset:X}"
            },
            InstructionContext = instructionContext is null ? null : new ILOffsetInstructionContext
            {
                ILOffset = $"0x{instructionContext.ILOffset:X}",
                Boundary = instructionContext.Boundary,
                Opcode = instructionContext.Opcode,
                OperandKind = instructionContext.OperandKind,
                Operand = instructionContext.Operand,
                OperandToken = instructionContext.OperandToken,
                BranchTargets = instructionContext.BranchTargets,
                NextOffset = $"0x{instructionContext.NextOffset:X}",
                Length = instructionContext.Length,
                Block = instructionContext.Block,
                TerminatesBlock = instructionContext.TerminatesBlock ? "Yes" : "No",
                FallsThrough = instructionContext.FallsThrough ? "Yes" : "No"
            },
            ExceptionContext = exceptionContext
                .Select(item => new ILOffsetExceptionContext
                {
                    Region = item.Region,
                    Context = item.Context,
                    Clause = item.Clause,
                    TryRange = FormatILRange(item.TryStart, item.TryEnd),
                    HandlerRange = FormatILRange(item.HandlerStart, item.HandlerEnd),
                    FilterRange = item.FilterStart is { } start && item.FilterEnd is { } end
                        ? FormatILRange(start, end)
                        : null,
                    CaughtType = item.CaughtType
                })
                .ToList(),
            CallsiteContext = callsiteContext is null ? null : new ILOffsetCallsiteContext
            {
                CallOffset = FormatILOffset(callsiteContext.CallOffset),
                Opcode = callsiteContext.Opcode,
                CallKind = callsiteContext.CallKind,
                Callee = callsiteContext.Callee,
                OperandToken = callsiteContext.OperandToken,
                ReturnAddress = FormatILOffset(callsiteContext.ReturnAddress)
            },
            ReturnAddressContext = returnAddressContext is null ? null : new ILOffsetReturnAddressContext
            {
                ILOffset = FormatILOffset(returnAddressContext.ILOffset),
                CallOffset = FormatILOffset(returnAddressContext.CallOffset),
                Opcode = returnAddressContext.Opcode,
                CallKind = returnAddressContext.CallKind,
                Callee = returnAddressContext.Callee,
                OperandToken = returnAddressContext.OperandToken
            },
            AllocationContext = allocationContext,
            SafetyContext = safetyContext,
            CostContext = costContext
        });
    }

    static bool Includes(ILOffsetProjectionRequest request, ILOffsetProjectionCapabilities capability)
        => (request.Capabilities & capability) != 0;

    static string FormatILRange(int start, int end) => $"IL_{start:X4}..IL_{end:X4}";

    static string FormatILOffset(int offset) => $"IL_{offset:X4}";

    /// <summary>
    /// Analysis is the single source of truth for allocation, safety, and cost facts at an IL
    /// coordinate. Zero facts is a complete, verified answer; an acquisition failure is reported
    /// as a visible outcome failure, never silently replaced by an opcode-pattern guess. The
    /// index is opened once per request (via the shared cache) and reused across all three
    /// contexts instead of re-opening the assembly for each one.
    /// </summary>
    static bool TryOpenAnalysisIndex(
        string assemblyPath,
        [NotNullWhen(true)] out Analysis.LibraryBodyIndex? index,
        out string error)
    {
        try
        {
            index = AnalysisIndexCache.ForPath(assemblyPath);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or IOException
            or InvalidOperationException
            or ArgumentException
            or UnauthorizedAccessException)
        {
            index = null;
            error = $"IL-offset semantic analysis unavailable: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    static List<ILOffsetAllocationContext> BuildAllocationContext(
        Analysis.LibraryBodyIndex index,
        int methodToken,
        int ilOffset)
        => Analysis.SemanticFactProjection.AllocationFacts(
                index.GetAllocationOccurrences(),
                methodToken,
                ilOffset)
            .Select(ToILOffsetAllocationContext)
            .ToList();

    static List<ILOffsetSafetyContext> BuildSafetyContext(
        Analysis.LibraryBodyIndex index,
        string assemblyPath,
        int methodToken,
        int ilOffset)
    {
        var subject = new FindingSubject($"{assemblyPath}|{methodToken:X8}", $"0x{methodToken:X}");
        index.GetUnsafetyOccurrences().TryGetValue(methodToken, out var occurrences);
        index.GetUnsafeEvidenceByMember().TryGetValue(methodToken, out var evidence);
        return Analysis.SemanticFactProjection.SafetyFacts(
                Analysis.AnalysisFindings.InspectUnsafeEvidence(evidence.IsDefault ? [] : evidence, subject),
                Analysis.AnalysisFindings.InspectUnsafety(occurrences.IsDefault ? [] : occurrences, subject),
                ilOffset)
            .Select(ToILOffsetSafetyContext)
            .ToList();
    }

    static List<ILOffsetCostContext> BuildCostContext(
        Analysis.LibraryBodyIndex index,
        int methodToken,
        int ilOffset)
        => Analysis.SemanticFactProjection.CostFacts(
                index.GetDirectCallsByCaller(),
                methodToken,
                ilOffset)
            .Select(ToILOffsetCostContext)
            .ToList();

    static ILOffsetSafetyContext ToILOffsetSafetyContext(Analysis.SafetyFact fact)
        => new()
        {
            ILOffset = fact.ILOffset is { } offset ? FormatILOffset(offset) : null,
            SafetyKind = fact.SafetyKind,
            Operation = fact.Operation,
            Requirement = fact.Requirement,
            Evidence = fact.Evidence
        };

    static ILOffsetCostContext ToILOffsetCostContext(Analysis.CostFact fact)
        => new()
        {
            ILOffset = FormatILOffset(fact.ILOffset),
            CostKind = fact.CostKind,
            Operation = fact.Operation,
            InLoop = fact.InLoop ? "Yes" : "No",
            Evidence = fact.Evidence
        };

    static ILOffsetAllocationContext ToILOffsetAllocationContext(Analysis.AllocationFact fact)
        => new()
        {
            ILOffset = FormatILOffset(fact.ILOffset),
            AllocationKind = fact.AllocationKind,
            AllocatedType = fact.AllocatedType,
            CountedAsHeap = fact.CountedAsHeap ? "Yes" : "No",
            Frequency = fact.Frequency,
            Escape = fact.Escape,
            EscapeKind = fact.EscapeKind,
            EstimatedSizeBytes = fact.EstimatedSizeBytes,
            SizeTier = fact.SizeTier,
            InLoop = fact.InLoop ? "Yes" : "No",
            Path = fact.Path,
            PathConfidence = fact.PathConfidence,
            PostDominance = fact.PostDominance,
            Evidence = fact.Evidence,
            Multiplicity = fact.Multiplicity,
            ChurnedType = fact.ChurnedType
        };
}
