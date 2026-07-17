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
        if (Includes(request, ILOffsetProjectionCapabilities.AllocationContext))
        {
            allocationContext = BuildAllocationContext(
                context.AssemblyPath,
                request.MethodToken,
                request.ILOffset,
                instructionContext,
                request.Log);
        }
        if (Includes(request, ILOffsetProjectionCapabilities.SafetyContext))
            safetyContext = BuildSafetyContext(instructionContext);
        if (Includes(request, ILOffsetProjectionCapabilities.CostContext))
            costContext = BuildCostContext(instructionContext);

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

    static List<ILOffsetAllocationContext> BuildAllocationContext(
        string assemblyPath,
        int methodToken,
        int ilOffset,
        ILOffsetInstructionContextInfo? instruction,
        Action<string>? log)
    {
        try
        {
            var index = Analysis.LibraryBodyIndex.Open(assemblyPath);
            var facts = Analysis.SemanticFactProjection.AllocationFacts(
                    index.GetAllocationOccurrences(),
                    methodToken,
                    ilOffset)
                .Select(ToILOffsetAllocationContext)
                .ToList();
            if (facts.Count > 0)
                return facts;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or IOException
            or InvalidOperationException
            or ArgumentException
            or UnauthorizedAccessException)
        {
            log?.Invoke(
                $"IL-offset allocation analysis unavailable; using instruction fallback: "
                + $"{ex.GetType().Name}: {ex.Message}");
        }

        return BuildInstructionAllocationContext(instruction);
    }

    static List<ILOffsetAllocationContext> BuildInstructionAllocationContext(
        ILOffsetInstructionContextInfo? instruction)
    {
        if (instruction is null)
            return [];

        var opcode = instruction.Opcode;
        string? kind = opcode switch
        {
            "newarr" => "array",
            "box" => "box",
            "newobj" => "object",
            _ => null
        };
        if (kind is null)
            return [];

        return
        [
            new ILOffsetAllocationContext
            {
                ILOffset = FormatILOffset(instruction.ILOffset),
                AllocationKind = kind,
                AllocatedType = instruction.Operand,
                CountedAsHeap = opcode == "newobj" ? "Unknown" : "Yes",
                Frequency = "always",
                Escape = "unknown",
                InLoop = "Unknown",
                Evidence = opcode
            }
        ];
    }

    static List<ILOffsetSafetyContext> BuildSafetyContext(
        ILOffsetInstructionContextInfo? instruction)
    {
        if (instruction is null)
            return [];

        var opcode = instruction.Opcode;
        string? kind = opcode switch
        {
            "localloc" => "stackalloc",
            "calli" => "calli",
            "cpblk" or "initblk" => "unsafe block operation",
            "ldind.i1" or "ldind.u1" or "ldind.i2" or "ldind.u2" or "ldind.i4" or "ldind.u4"
                or "ldind.i8" or "ldind.i" or "ldind.r4" or "ldind.r8" or "ldind.ref" => "dereference",
            "stind.i1" or "stind.i2" or "stind.i4" or "stind.i8" or "stind.i"
                or "stind.r4" or "stind.r8" or "stind.ref" => "dereference",
            "call" or "callvirt" or "newobj" when IsUnsafeOperation(instruction.Operand) => "unsafe call",
            _ => null
        };
        if (kind is null)
            return [];

        return
        [
            new ILOffsetSafetyContext
            {
                ILOffset = FormatILOffset(instruction.ILOffset),
                SafetyKind = kind,
                Operation = instruction.Operand ?? opcode,
                Requirement = "requires unsafe",
                Evidence = opcode
            }
        ];
    }

    static bool IsUnsafeOperation(string? operand)
        => operand?.Contains("System.Runtime.CompilerServices.Unsafe::", StringComparison.Ordinal) == true
            || operand?.Contains("System.Runtime.CompilerServices.Unsafe.", StringComparison.Ordinal) == true
            || operand?.Contains("void*", StringComparison.Ordinal) == true;

    static List<ILOffsetCostContext> BuildCostContext(
        ILOffsetInstructionContextInfo? instruction)
    {
        if (instruction is null)
            return [];

        var opcode = instruction.Opcode;
        string? kind = opcode switch
        {
            "callvirt" => "virtual dispatch",
            "ldftn" or "ldvirtftn" => "delegate/function pointer",
            "calli" => "function pointer call",
            _ => null
        };
        if (kind is null)
            return [];

        return
        [
            new ILOffsetCostContext
            {
                ILOffset = FormatILOffset(instruction.ILOffset),
                CostKind = kind,
                Operation = instruction.Operand ?? opcode,
                InLoop = "Unknown",
                Evidence = opcode
            }
        ];
    }

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
