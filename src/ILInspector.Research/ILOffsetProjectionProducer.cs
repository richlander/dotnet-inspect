using System.Collections.Immutable;
using System.Reflection.Metadata;
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
        if (request.Analysis is not null
            && request.AnalysisFailure is not null)
        {
            throw new ArgumentException(
                "IL-offset projection cannot receive both focused Analysis "
                    + "results and an Analysis failure.",
                nameof(request));
        }

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
        if (decoded
            && (uint)request.ILOffset > (uint)methodBody!.IL.Length)
        {
            return ILOffsetProjectionOutcome.Failed(
                ILOffsetProjectionFailureKind.InstructionUnavailable,
                $"IL offset 0x{request.ILOffset:X} is outside the decoded "
                + $"method body for token 0x{request.MethodToken:X}, whose "
                + $"terminal boundary is 0x{methodBody.IL.Length:X}.");
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
            && Includes(request, ILOffsetProjectionCapabilities.InstructionContext)
            && (!request.AllowNonBoundaryContextAbsence || !decoded))
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
            && Includes(request, ILOffsetProjectionCapabilities.CallsiteContext)
            && (!request.AllowNonBoundaryContextAbsence || !decoded))
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
            && Includes(request, ILOffsetProjectionCapabilities.ReturnAddressContext)
            && (!request.AllowNonBoundaryContextAbsence || !decoded))
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
            if (request.Analysis is not { } analysis)
            {
                var failureKind = wantsAllocation
                    ? ILOffsetProjectionFailureKind.AllocationAnalysisUnavailable
                    : wantsSafety
                        ? ILOffsetProjectionFailureKind.SafetyAnalysisUnavailable
                        : ILOffsetProjectionFailureKind.CostAnalysisUnavailable;
                return ILOffsetProjectionOutcome.Failed(
                    failureKind,
                    request.AnalysisFailure
                        ?? "IL-offset semantic analysis unavailable: "
                            + "no focused Analysis input was supplied.");
            }
            Guid sourceModuleVersionId = ReadModuleVersionId(context);
            if (sourceModuleVersionId
                != analysis.Receipt.ModuleIdentity.ModuleVersionId)
            {
                var failureKind = wantsAllocation
                    ? ILOffsetProjectionFailureKind.AllocationAnalysisUnavailable
                    : wantsSafety
                        ? ILOffsetProjectionFailureKind.SafetyAnalysisUnavailable
                        : ILOffsetProjectionFailureKind.CostAnalysisUnavailable;
                return ILOffsetProjectionOutcome.Failed(
                    failureKind,
                    "IL-offset semantic analysis unavailable: "
                        + "the source and Analysis input represent "
                        + "different module generations.");
            }
            if (wantsAllocation
                && !analysis.Allocations.WasRequested)
            {
                return ILOffsetProjectionOutcome.Failed(
                    ILOffsetProjectionFailureKind
                        .AllocationAnalysisUnavailable,
                    "IL-offset allocation analysis was not requested "
                        + "by the supplied execution.");
            }
            if (wantsSafety
                && !analysis.Safety.WasRequested)
            {
                return ILOffsetProjectionOutcome.Failed(
                    ILOffsetProjectionFailureKind
                        .SafetyAnalysisUnavailable,
                    "IL-offset safety analysis was not requested "
                        + "by the supplied execution.");
            }
            if (wantsCost
                && !analysis.CallGraph.WasRequested)
            {
                return ILOffsetProjectionOutcome.Failed(
                    ILOffsetProjectionFailureKind
                        .CostAnalysisUnavailable,
                    "IL-offset cost analysis was not requested "
                        + "by the supplied execution.");
            }
            if (wantsAllocation)
            {
                allocationContext = BuildAllocationContext(
                    analysis,
                    request.MethodToken,
                    request.ILOffset);
            }
            if (wantsSafety)
            {
                safetyContext = BuildSafetyContext(
                    analysis,
                    request.MethodToken,
                    request.ILOffset);
            }
            if (wantsCost)
            {
                costContext = BuildCostContext(
                    analysis,
                    request.MethodToken,
                    request.ILOffset);
            }
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

        string? url = source?.SourceUrl;
        if (url is not null)
        {
            if (request.BrowsableUrls)
                url = SourceLinkUrlPresentation.PreferRenderedUrl(url);
            url = new UriBuilder(url) { Fragment = $"L{source!.Line}" }.Uri.AbsoluteUri;
        }

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
            SourceChecksum = source?.Checksum,
            SourceChecksumAlgorithm = source?.ChecksumAlgorithm,
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

    static Guid ReadModuleVersionId(PdbContext context) =>
        context.InspectImage(
            static image =>
            {
                MetadataReader metadata =
                    image.GetMetadataReader();
                return metadata.GetGuid(
                    metadata.GetModuleDefinition().Mvid);
            });

    static List<ILOffsetAllocationContext> BuildAllocationContext(
        ILOffsetAnalysisInput analysis,
        int methodToken,
        int ilOffset)
        => Analysis.SemanticFactProjection.AllocationFacts(
                analysis.Allocations.Occurrences,
                methodToken,
                ilOffset)
            .Select(ToILOffsetAllocationContext)
            .ToList();

    static List<ILOffsetSafetyContext> BuildSafetyContext(
        ILOffsetAnalysisInput analysis,
        int methodToken,
        int ilOffset)
    {
        analysis.Safety.Occurrences.TryGetValue(
            methodToken,
            out var occurrences);
        analysis.UnsafeEvidenceByToken.TryGetValue(
            methodToken,
            out var evidence);
        return Analysis.SemanticFactProjection.SafetyFacts(
                evidence.IsDefault ? [] : evidence,
                occurrences.IsDefault ? [] : occurrences,
                ilOffset)
            .Select(ToILOffsetSafetyContext)
            .ToList();
    }

    static List<ILOffsetCostContext> BuildCostContext(
        ILOffsetAnalysisInput analysis,
        int methodToken,
        int ilOffset)
    {
        analysis.CallsByEvidenceMethod
            .TryGetValue(methodToken, out var calls);
        return Analysis.SemanticFactProjection.CostFacts(
                calls.IsDefault ? [] : calls,
                ilOffset: ilOffset)
            .Select(ToILOffsetCostContext)
            .ToList();
    }

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
