using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Commands;

internal static class ILOffsetSourceQuery
{
    public static async Task<(int ExitCode, ILOffsetResult? Result)> ResolveAsync(
        string dllPath,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        using var service = SourceLinkService.Open(dllPath, logger.Log);
        return await ResolveAsync(service, packageName, packageVersion, isPlatformAssembly, options, httpClient, logger, writeErrors: true);
    }

    internal static async Task<(int ExitCode, ILOffsetResult? Result, string? Error)> ResolveBatchAsync(
        SourceLinkService service,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        var (exitCode, result) = await ResolveAsync(service, packageName, packageVersion, isPlatformAssembly, options, httpClient, logger, writeErrors: false);
        return (exitCode, result, exitCode == 0 ? null : "could not resolve");
    }

    private static async Task<(int ExitCode, ILOffsetResult? Result)> ResolveAsync(
        SourceLinkService service,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger,
        bool writeErrors)
    {
        if (!TryParse(options.ILOffsetParameter!, out var methodToken, out var ilOffset))
        {
            WriteError(writeErrors, $"Error: Invalid --il-offset value '{options.ILOffsetParameter}'.");
            WriteError(writeErrors, "Expected format: 0x6000001+0x5 (method token + IL offset)");
            return (1, null);
        }

        var pdbContext = service.Context;

        if (!pdbContext.HasMetadata)
        {
            WriteError(writeErrors, "Error: No metadata in library.");
            return (1, null);
        }

        var memberContext = pdbContext.ResolveMemberContext(methodToken, ilOffset);
        if (memberContext == null)
        {
            WriteError(writeErrors, $"Error: Could not resolve member context for token 0x{methodToken:X}.");
            WriteError(writeErrors, "The method token may be invalid or may not identify a MethodDef row.");
            return (1, null);
        }

        var instructionContext = pdbContext.ResolveInstructionContext(methodToken, ilOffset, out var instructionError);
        if (instructionContext == null && RequiresInstructionContext(options))
        {
            WriteError(writeErrors, $"Error: {instructionError ?? $"Could not resolve instruction context for token 0x{methodToken:X}+0x{ilOffset:X}."}");
            return (1, null);
        }

        var exceptionContext = pdbContext.ResolveExceptionContext(methodToken, ilOffset, out var exceptionError);
        if (exceptionError is not null && RequiresExceptionContext(options))
        {
            WriteError(writeErrors, $"Error: {exceptionError}");
            return (1, null);
        }

        var callsiteContext = pdbContext.ResolveCallsiteContext(methodToken, ilOffset, out var callsiteError);
        if (callsiteError is not null && RequiresCallsiteContext(options))
        {
            WriteError(writeErrors, $"Error: {callsiteError}");
            return (1, null);
        }

        var returnAddressContext = pdbContext.ResolveReturnAddressContext(methodToken, ilOffset, out var returnAddressError);
        if (returnAddressError is not null && RequiresReturnAddressContext(options))
        {
            WriteError(writeErrors, $"Error: {returnAddressError}");
            return (1, null);
        }

        List<ILOffsetAllocationContext>? allocationContext = null;
        List<ILOffsetSafetyContext>? safetyContext = null;
        List<ILOffsetCostContext>? costContext = null;
        if (RequiresSemanticContext(options))
        {
            if (RequiresAllocationContext(options))
                allocationContext = BuildAllocationContext(pdbContext.AssemblyPath, methodToken, ilOffset, instructionContext);
            if (RequiresSafetyContext(options))
                safetyContext = BuildSafetyContext(instructionContext);
            if (RequiresCostContext(options))
                costContext = BuildCostContext(instructionContext);
        }

        SourceLinkResolver.ILOffsetSourceInfo? result = null;
        if (RequiresSourceLocation(options))
        {
            await SourceEnricher.AcquirePdbAsync(
                pdbContext,
                httpClient,
                packageName,
                packageVersion,
                isPlatformAssembly,
                logger.Log);

            if (!pdbContext.HasPdb)
            {
                if (writeErrors)
                    WritePdbWarning(pdbContext);
                return (1, null);
            }

            if (!service.HasSourceLink)
                logger.Log("Warning: No SourceLink information found. URLs will not be available.");

            result = service.ResolveByILOffset(methodToken, ilOffset);
            if (result == null)
            {
                WriteError(writeErrors, $"Error: Could not resolve source location for token 0x{methodToken:X}+0x{ilOffset:X}.");
                WriteError(writeErrors, "The method token may be invalid or the PDB may not contain sequence points for this method.");
                return (1, null);
            }
        }

        string? url = options.BrowsableUrls ? result?.GitHubBrowseUrl : result?.SourceUrl;
        if (url != null)
            url += $"#L{result!.Line}";

        var resolved = new ILOffsetResult
        {
            Method = result?.MethodName ?? memberContext.Member,
            Token = $"0x{methodToken:X}",
            ILOffset = $"0x{ilOffset:X}",
            MatchedOffset = result != null && result.MatchedOffset != ilOffset ? $"0x{result.MatchedOffset:X}" : null,
            File = result?.FilePath,
            Line = result?.Line,
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
                .Select(context => new ILOffsetExceptionContext
                {
                    Region = context.Region,
                    Context = context.Context,
                    Clause = context.Clause,
                    TryRange = FormatILRange(context.TryStart, context.TryEnd),
                    HandlerRange = FormatILRange(context.HandlerStart, context.HandlerEnd),
                    FilterRange = context.FilterStart is { } fs && context.FilterEnd is { } fe
                        ? FormatILRange(fs, fe)
                        : null,
                    CaughtType = context.CaughtType
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
        };

        return (0, resolved);
    }

    private static void WriteError(bool enabled, string message)
    {
        if (enabled)
            Console.Error.WriteLine(message);
    }

    private static bool RequiresSourceLocation(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.ILOffset) == true;

    private static bool RequiresInstructionContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.InstructionContext) == true;

    private static bool RequiresExceptionContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.ExceptionContext) == true;

    private static bool RequiresCallsiteContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.CallsiteContext) == true;

    private static bool RequiresReturnAddressContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.ReturnAddressContext) == true;

    private static bool RequiresAllocationContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.AllocationContext) == true;

    private static bool RequiresSafetyContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.SafetyContext) == true;

    private static bool RequiresCostContext(LibraryOptions options)
        => options.IncludeSections?.Contains(SectionNames.CostContext) == true;

    private static bool RequiresSemanticContext(LibraryOptions options)
        => RequiresAllocationContext(options) || RequiresSafetyContext(options) || RequiresCostContext(options);

    private static string FormatILRange(int start, int end) => $"IL_{start:X4}..IL_{end:X4}";

    private static string FormatILOffset(int offset) => $"IL_{offset:X4}";

    private static List<ILOffsetAllocationContext> BuildAllocationContext(
        string assemblyPath,
        int methodToken,
        int ilOffset,
        ILOffsetInstructionContextInfo? instruction)
    {
        try
        {
            var index = Analysis.LibraryBodyIndex.Open(assemblyPath);
            var facts = Analysis.SemanticFactProjection
                .AllocationFacts(index.GetAllocationOccurrences(), methodToken, ilOffset)
                .Select(ToILOffsetAllocationContext)
                .ToList();
            if (facts.Count > 0)
                return facts;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
        }

        return BuildInstructionAllocationContext(instruction);
    }

    private static List<ILOffsetAllocationContext> BuildInstructionAllocationContext(ILOffsetInstructionContextInfo? instruction)
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

    private static List<ILOffsetSafetyContext> BuildSafetyContext(ILOffsetInstructionContextInfo? instruction)
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

    private static bool IsUnsafeOperation(string? operand)
        => operand?.Contains("System.Runtime.CompilerServices.Unsafe::", StringComparison.Ordinal) == true
            || operand?.Contains("System.Runtime.CompilerServices.Unsafe.", StringComparison.Ordinal) == true
            || operand?.Contains("void*", StringComparison.Ordinal) == true;

    private static List<ILOffsetCostContext> BuildCostContext(ILOffsetInstructionContextInfo? instruction)
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

    private static ILOffsetAllocationContext ToILOffsetAllocationContext(Analysis.AllocationFact fact)
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
            Evidence = fact.Evidence
        };

    private static ILOffsetSafetyContext ToILOffsetSafetyContext(Analysis.SafetyFact fact)
        => new()
        {
            ILOffset = fact.ILOffset is { } offset ? FormatILOffset(offset) : null,
            SafetyKind = fact.SafetyKind,
            Operation = fact.Operation,
            Requirement = fact.Requirement,
            Evidence = fact.Evidence
        };

    private static ILOffsetCostContext ToILOffsetCostContext(Analysis.CostFact fact)
        => new()
        {
            ILOffset = FormatILOffset(fact.ILOffset),
            CostKind = fact.CostKind,
            Operation = fact.Operation,
            InLoop = fact.InLoop ? "Yes" : "No",
            Evidence = fact.Evidence
        };

    public static bool TryParse(string value, out int methodToken, out int ilOffset)
    {
        methodToken = 0;
        ilOffset = 0;

        var plusIndex = value.IndexOf('+');
        if (plusIndex <= 0 || plusIndex >= value.Length - 1)
            return false;

        var tokenPart = value[..plusIndex];
        var offsetPart = value[(plusIndex + 1)..];

        if (!TryParseHexInt(tokenPart, out methodToken))
            return false;
        if (!TryParseHexInt(offsetPart, out ilOffset))
            return false;

        // MethodDef tokens have table id 0x06 in the high byte.
        return (methodToken & unchecked((int)0xFF000000)) == 0x06000000;
    }

    private static bool TryParseHexInt(string value, out int result)
    {
        result = 0;
        if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return false;
        return int.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out result);
    }

    private static void WritePdbWarning(PdbContext pdbContext)
    {
        Console.Error.WriteLine();
        if (pdbContext.WindowsPdbDetected)
        {
            Console.Error.WriteLine("Error: PDB is Windows format (not supported).");
            Console.Error.WriteLine("       Only Portable PDBs are supported.");
        }
        else
        {
            Console.Error.WriteLine("Error: No readable PDB found.");
        }
        Console.Error.WriteLine("       Use 'library <target> -S \"SourceLink Availability\"' for full source reachability.");
        Console.Error.WriteLine();
    }

}
