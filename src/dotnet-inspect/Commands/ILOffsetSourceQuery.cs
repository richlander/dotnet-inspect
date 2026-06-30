using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Instructions;
using ILInspector.Metadata;
using Markout;
using Markout.Formatting;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Commands;

internal static class ILOffsetSourceQuery
{
    public static async Task<int> ExecuteAsync(
        string dllPath,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (!TryParse(options.ILOffset!, out var methodToken, out var ilOffset))
        {
            Console.Error.WriteLine($"Error: Invalid --il-offset format '{options.ILOffset}'.");
            Console.Error.WriteLine("Expected format: 0x6000001+0x5 (method token + IL offset)");
            return 1;
        }

        // Substrate-derived call-site naming. PDB-free: needs only the assembly's own metadata and IL,
        // so it works whether or not a PDB is available.
        var callSite = ResolveCallSite(dllPath, methodToken, ilOffset);

        using var service = SourceLinkService.Open(dllPath, logger.Log);
        var pdbContext = service.Context;

        if (!pdbContext.HasMetadata)
        {
            Console.Error.WriteLine("Error: No metadata in library.");
            return 1;
        }

        await SourceEnricher.AcquirePdbAsync(
            pdbContext,
            httpClient,
            packageName,
            packageVersion,
            isPlatformAssembly,
            logger.Log);

        // Source resolution requires a Portable PDB with sequence points; the call-site naming above does not.
        SourceLinkResolver.ILOffsetSourceInfo? result = null;
        if (pdbContext.HasPdb)
        {
            if (!service.HasSourceLink)
                logger.Log("Warning: No SourceLink information found. URLs will not be available.");
            result = service.ResolveByILOffset(methodToken, ilOffset);
        }

        // Nothing resolved at all (invalid token / no body and no PDB sequence points): preserve the hard error.
        if (callSite is null && result is null)
        {
            Console.Error.WriteLine($"Error: Could not resolve token 0x{methodToken:X}+0x{ilOffset:X}.");
            Console.Error.WriteLine("The method token may be invalid, the method may have no body, or the PDB may not contain sequence points for it.");
            return 1;
        }

        string? url = result is not null
            ? (options.BrowsableUrls ? result.GitHubBrowseUrl : result.SourceUrl)
            : null;
        if (url != null)
            url += $"#L{result!.Line}";

        string token = $"0x{methodToken:X}";
        string ilOffsetHex = $"0x{ilOffset:X}";
        string methodName = result?.MethodName ?? callSite?.MethodName ?? token;
        string? instruction = callSite?.Instruction;
        string? calling = callSite?.Callee is { } callee
            ? (callSite.IsReturnAddress ? $"{callee} (return address)" : callee)
            : null;
        string? matchedOffset = result is not null && result.MatchedOffset != ilOffset
            ? $"0x{result.MatchedOffset:X}"
            : null;

        if (options.JsonOutput)
        {
            var jsonResult = new ILOffsetResult
            {
                Method = methodName,
                Token = token,
                ILOffset = ilOffsetHex,
                MatchedOffset = result is not null ? $"0x{result.MatchedOffset:X}" : null,
                Instruction = instruction,
                Callee = callSite?.Callee,
                IsReturnAddress = callSite?.IsReturnAddress ?? false,
                File = result?.FilePath,
                Line = result?.Line,
                Url = url
            };
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonResult, SourceJsonContext.Default.ILOffsetResult));
            return 0;
        }

        bool showSections = options.Verbosity >= Verbosity.Minimal;
        bool showSource = options.Verbosity >= Verbosity.Normal && result is not null;

        var view = new SourceILOffsetView
        {
            Title = methodName,
            Token = showSections ? null : token,
            ILOffset = showSections ? null : ilOffsetHex,
            MatchedOffset = showSections ? null : matchedOffset,
            Calling = showSections ? null : calling,
            Offset = showSections
                ? new ILOffsetInfoSection
                {
                    Token = token,
                    ILOffset = ilOffsetHex,
                    MatchedOffset = matchedOffset,
                    Instruction = instruction,
                    Calling = calling,
                }
                : null,
            Location = showSource ? [new ILOffsetSourceRow(result!.FilePath, result.Line, url)] : null,
        };

        if (options.OneLine && !options.JsonOutput)
        {
            string oneLine = url
                ?? (result is not null ? $"{result.FilePath}:{result.Line}"
                    : calling is not null ? $"calling {calling}"
                    : $"{token}+{ilOffsetHex}");
            Console.WriteLine(oneLine);
            return 0;
        }

        var writerOpts = OutputFormatter.CreateProjectedWriterOptions(options.Columns, options.Fields);
        var formatter = options.PlainText ? (IMarkoutFormatter)new PlainTextFormatter() : new MarkdownFormatter();
        if (options.PlainText)
        {
            MarkoutSerializer.Serialize(view, Console.Out, formatter, SourceViewContext.Default, writerOpts);
        }
        else
        {
            OutputFormatter.WriteLimitedMarkdown(Console.Out,
                MarkoutSerializer.Serialize(view, SourceViewContext.Default, writerOpts), options.Rows);
        }

        // When source is unavailable, say why; the call-site naming above is still a useful metadata-only result.
        if (result is null)
        {
            string reason = !pdbContext.HasPdb
                ? (pdbContext.WindowsPdbDetected ? "Windows PDB format is not supported" : "no PDB available")
                : "no sequence point covers this offset";
            Console.WriteLine();
            Console.WriteLine($"Source unavailable: {reason} (metadata-only result).");
        }

        return 0;
    }

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

    /// <summary>
    /// Resolves the decoded instruction at <paramref name="ilOffset"/> in the given method, and—when that
    /// instruction (or the one whose return address it is) is a call—the resolved callee. PDB-free: uses only
    /// the assembly's own metadata + IL via the <c>ILInspector.Instructions</c> substrate. Returns null when the
    /// token is not a method definition or the body cannot be read/decoded.
    /// </summary>
    private static CallSiteInfo? ResolveCallSite(string dllPath, int methodToken, int ilOffset)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                return null;

            var reader = pe.GetMetadataReader();
            var handle = MetadataTokens.EntityHandle(methodToken);
            if (handle.Kind != HandleKind.MethodDefinition)
                return null;

            string methodName = ILTokenResolver.ResolveMethod(reader, methodToken);

            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            if (method.RelativeVirtualAddress == 0)
                return new CallSiteInfo(methodName, null, null, IsReturnAddress: false);

            var body = pe.GetMethodBody(method.RelativeVirtualAddress);
            var instructions = MethodInstructions.Decode(body);

            // The instruction starting exactly at the offset; else the instruction whose return address is the
            // offset (call sites are commonly reported as the address of the following instruction).
            DecodedInstruction? at = instructions.InstructionAt(ilOffset);
            DecodedInstruction? call = at is not null && IsCall(at.OpCode) ? at : null;
            bool isReturnAddress = false;
            if (call is null)
            {
                DecodedInstruction? prev = instructions.Instructions.FirstOrDefault(i => i.NextOffset == ilOffset);
                if (prev is not null && IsCall(prev.OpCode))
                {
                    call = prev;
                    isReturnAddress = true;
                }
            }

            if (call is not null)
            {
                string callee = call.OpCode == ILOpCode.Calli
                    ? "(indirect call site)"
                    : ILTokenResolver.ResolveMethod(reader, (int)call.OperandValue);
                return new CallSiteInfo(methodName, OpName(call.OpCode), callee, isReturnAddress);
            }

            return at is not null
                ? new CallSiteInfo(methodName, OpName(at.OpCode), null, IsReturnAddress: false)
                : new CallSiteInfo(methodName, null, null, IsReturnAddress: false);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsCall(ILOpCode op) =>
        op is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Calli;

    private static string OpName(ILOpCode op) =>
        op.ToString().Replace('_', '.').ToLowerInvariant();
}

/// <summary>Substrate-derived call-site facts for an IL offset (PDB-free).</summary>
internal sealed record CallSiteInfo(string? MethodName, string? Instruction, string? Callee, bool IsReturnAddress);

public class ILOffsetResult
{
    public string? Method { get; init; }
    public string? Token { get; init; }
    public string? ILOffset { get; init; }
    public string? MatchedOffset { get; init; }
    public string? Instruction { get; init; }
    public string? Callee { get; init; }
    public bool IsReturnAddress { get; init; }
    public string? File { get; init; }
    public int? Line { get; init; }
    public string? Url { get; init; }
}
