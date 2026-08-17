using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.ControlFlow;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal interface ILibraryMethodAnalysisResolver :
    IMethodAllocationResolver,
    IOptimizationOpportunityResolver
{
}

internal interface ILibraryMethodAnalysisInfrastructure
{
    MetadataReader Reader { get; }

    PEReader PeReader { get; }

    string AssemblyName { get; }

    Guid Mvid { get; }

    GenericScope CreateScope(
        TypeDefinition typeDefinition,
        MethodDefinition methodDefinition);

    MethodIdentity CreateMethodIdentity(
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        GenericScope scope);

    ILibraryMethodAnalysisResolver CreateMethodAnalysisResolver(
        GenericScope scope,
        MethodIdentity caller,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions);

    IMethodCallResolver CreateCallResolver(
        GenericScope scope,
        MethodIdentity caller);

    MemberRef ResolveMethod(
        int token,
        GenericScope scope,
        MethodDefinitionHandle caller);

    string? CalliReturnDetail(
        int token,
        GenericScope scope);

    bool IsAllocatingValueTypeBox(
        int token,
        GenericScope scope);

    bool HasGeneratedCodeAttribute(
        CustomAttributeHandleCollection attributes);

    bool HasCompilerGeneratedAttribute(
        CustomAttributeHandleCollection attributes);

    bool TryResolveLiftedSourceOwner(
        MethodDefinitionHandle liftedHandle,
        MethodDefinition liftedMethod,
        MethodIdentity liftedIdentity,
        out MethodIdentity? sourceOwner,
        out bool sourceGenerated);

    bool DispatchCanTargetOverride(
        TypeDefinition declaringType,
        MethodDefinition method);
}

// Method-local output is merged by LibraryBodyAnalysisBuilder in metadata
// order. BuildCallTree_PreservesRecoverableBodyAnalysisFailure gates the
// partial call/evidence publication and diagnostic behavior.
internal sealed class LibraryMethodAnalysisResult
{
    public bool HasCaller;
    public MethodIdentity? Caller;
    public int Token;
    public CallerUnsafeMode Mode;
    public bool IsLeverage;
    public bool HasBody;
    public ImmutableArray<UnsafeEvidence> UnsafeEvidence;
    public ImmutableArray<DirectCall> Calls;
    public ImmutableArray<AllocationOccurrence> Allocations;
    public ImmutableArray<UnsafetyOccurrence> Unsafety;
    public ImmutableArray<OptimizationOpportunity> Opportunities;
    public bool Suppressed;
    public bool HasSignals;
    public BodySignals Signals;
    public LeakTriageResult? LeakTriage;
    public ArrayPoolOwnershipMethodEvidence? OwnershipFlow;
    public AnalysisDiagnostic? Diagnostic;
}

/// <summary>
/// Runs the ordered topic producers for one method while the assembly builder
/// retains scheduling, primary-image lifetime, and result aggregation. The
/// primary metadata resolver owns metadata-dependent judgments and adapters.
/// </summary>
internal sealed class LibraryMethodAnalysisRunner(
    ILibraryMethodAnalysisInfrastructure infrastructure)
{
    readonly ILibraryMethodAnalysisInfrastructure _infrastructure =
        infrastructure;

    internal LibraryMethodAnalysisResult Analyze(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        bool typeSourceGenerated,
        MethodDefinitionHandle methodHandle,
        LibraryBodyAnalysisPlan plan)
    {
        bool includeMethodEvidence = plan.Includes(
            LibraryBodyAnalysisFeatures.MethodEvidence);
        bool includeAllocations = plan.Includes(
            LibraryBodyAnalysisFeatures.Allocations);
        bool includeOpportunities = plan.Includes(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities);
        bool includeLeakTriage = plan.Includes(
            LibraryBodyAnalysisFeatures.LeakTriage);
        bool includeOwnershipFlow = plan.Includes(
            LibraryBodyAnalysisFeatures.OwnershipFlow);
        IReadOnlySet<int>? bodyScope = plan.MethodScope;
        Func<TypeRef, bool>? bodyTypeScope = plan.TypeScope;
        if (!includeMethodEvidence)
        {
            return includeLeakTriage
                ? AnalyzeLeakTriageMethod(
                    typeHandle,
                    typeDefinition,
                    methodHandle)
                : new LibraryMethodAnalysisResult();
        }

        var result = new LibraryMethodAnalysisResult();
        var evidence =
            ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var calls =
            ImmutableArray.CreateBuilder<DirectCall>();
        MetadataReader reader = _infrastructure.Reader;
        LeakTriageFailureKind leakFailureKind =
            LeakTriageFailureKind.MethodMetadata;
        try
        {
            var methodDefinition =
                reader.GetMethodDefinition(methodHandle);
            var scope = _infrastructure.CreateScope(
                typeDefinition,
                methodDefinition);
            var caller = _infrastructure.CreateMethodIdentity(
                typeHandle,
                methodHandle,
                methodDefinition,
                scope);
            result.HasCaller = true;
            result.Caller = caller;
            result.Token = caller.MetadataToken;
            // Tally the unsafe mode for every method, including bodiless
            // extern/abstract members (P/Invokes are a major source).
            result.Mode = caller.CallerUnsafeMode;
            var declarationSafety =
                MethodSafetyAnalysis.InspectDeclaration(
                    caller,
                    evidence);
            bool hasUnsafeApiMember =
                declarationSafety.HasUnsafeApiMember;
            bool hasUnsafeSignature =
                declarationSafety.HasUnsafeSignature;
            if (caller.CallerUnsafeMode != CallerUnsafeMode.None
                || hasUnsafeApiMember)
            {
                result.IsLeverage = true;
            }
            if (methodDefinition.RelativeVirtualAddress == 0
                || !HasManagedIlBody(
                    methodDefinition.ImplAttributes))
                return result;

            result.HasBody = true;
            // Scoped builds decode only selected method bodies; every other method is still
            // indexed as an identity (above) but its body is not decoded/scanned. MethodScope
            // selects by method token; TypeScope selects by declaring type. Reverse/aggregate
            // sections leave both scopes null.
            if (bodyScope is not null
                && !bodyScope.Contains(caller.MetadataToken))
            {
                return result;
            }
            if (bodyTypeScope is not null
                && !bodyTypeScope(caller.DeclaringType))
            {
                return result;
            }
            leakFailureKind =
                LeakTriageFailureKind.BodyAcquisition;
            var body = _infrastructure.PeReader.GetMethodBody(
                methodDefinition.RelativeVirtualAddress);
            var il = body.GetILBytes() ?? [];
            if (includeLeakTriage)
            {
                if (!SignatureBlobGuard.IsSafeToDecode(
                    reader,
                    methodDefinition.Signature,
                    SignatureBlobGuard.Kind.Method))
                {
                    result.LeakTriage =
                        LeakTriageAnalyzer.Failed(
                            caller.MetadataToken,
                            LeakTriageFailureKind.MethodMetadata,
                            "SignatureLimit");
                }
                else
                {
                    result.LeakTriage =
                        LeakTriageAnalyzer.AnalyzeMethodDetailed(
                            LeakTriageAnalyzer
                                .CreateAssemblyScanMethodIdentity(
                                    caller),
                            il,
                            body.ExceptionRegions,
                            token => _infrastructure.ResolveMethod(
                                token,
                                scope,
                                methodHandle),
                            token =>
                                ArrayPoolExceptionPathAnalyzer.ResolveCatchTypeRef(
                                    reader,
                                    MetadataTokens.EntityHandle(token),
                                    scope));
                }
            }
            var methodInstructions =
                DecodeBody(
                    il,
                    body.ExceptionRegions);
            var loopRegions =
                CollectLoopRegions(methodInstructions);
            var localTypes =
                DecodeLocalTypes(
                    body,
                    scope);
            var context = new MethodBodyAnalysisContext(
                caller,
                methodInstructions,
                body.ExceptionRegions,
                loopRegions,
                localTypes);
            // Build allocation's Layer-1 indexes before other topic producers,
            // then keep every result and query bound to this exact context.
            var allocationFacts =
                MethodAllocationFacts.Create(context);
            var methodAnalysisResolver =
                _infrastructure.CreateMethodAnalysisResolver(
                    scope,
                    caller,
                    il,
                    body.ExceptionRegions);
            var localSafety =
                MethodSafetyAnalysis.InspectLocals(
                    context,
                    evidence);
            bool hasUnsafeLocals =
                localSafety.HasUnsafeLocals;
            // Discover allocation occurrences once. The main allocation output
            // needs escape classification, while Performance Triage's
            // optimization-opportunity pass reuses the same discovered
            // occurrences.
            if (includeAllocations)
                allocationFacts.Collect(methodAnalysisResolver);
            result.Allocations =
                allocationFacts.ClassifiedOccurrences;
            result.Unsafety =
                MethodSafetyAnalysis.CollectOccurrences(
                    context,
                    token => _infrastructure.CalliReturnDetail(
                        token,
                        scope));
            var methodAttributes =
                methodDefinition.GetCustomAttributes();
            if (includeOpportunities)
            {
                bool sourceFunction =
                    CompilerGeneratedNames.IsLocalFunctionOrLambda(
                        caller.Name);
                MethodIdentity? sourceOwner = null;
                bool sourceOwnerGenerated = false;
                bool hasSourceOwner = sourceFunction
                    && _infrastructure.TryResolveLiftedSourceOwner(
                        methodHandle,
                        methodDefinition,
                        caller,
                        out sourceOwner,
                        out sourceOwnerGenerated);
                bool sourceGenerated =
                    _infrastructure.HasGeneratedCodeAttribute(
                        methodAttributes)
                    || hasSourceOwner && sourceOwnerGenerated;
                bool compilerGenerated =
                    _infrastructure.HasCompilerGeneratedAttribute(
                        methodAttributes)
                    || sourceFunction;
                if (!typeSourceGenerated
                    && !sourceGenerated
                    && !compilerGenerated
                    && !IsBlazorRenderMethod(caller))
                {
                    result.Opportunities =
                        OptimizationOpportunityAnalysis.Collect(
                            allocationFacts,
                            methodAnalysisResolver);
                }
                else
                {
                    if (!typeSourceGenerated
                        && !sourceGenerated
                        && compilerGenerated
                        && hasSourceOwner
                        && sourceOwner is not null
                        && !IsBlazorRenderMethod(caller)
                        && !IsBlazorRenderMethod(sourceOwner))
                    {
                        result.Opportunities =
                        [
                            .. OptimizationOpportunityAnalysis.Collect(
                                allocationFacts,
                                methodAnalysisResolver)
                            .Where(static opportunity =>
                                opportunity.Shape
                                    == "generic-parameter-object-box")
                            .Select(opportunity => opportunity with
                            {
                                SourceOwner = sourceOwner,
                            }),
                        ];
                    }
                    result.Suppressed = true;
                }
            }
            var signals = BodySignalAnalysis.Collect(
                context,
                token => _infrastructure
                    .IsAllocatingValueTypeBox(
                        token,
                        scope));
            if (signals.Newarr > 0
                || signals.Throws > 0
                || signals.Catches > 0
                || signals.Finallys > 0
                || signals.Boxes > 0)
            {
                result.Signals = signals;
                result.HasSignals = true;
            }
            MethodCallAnalysis.Collect(
                context,
                _infrastructure.CreateCallResolver(
                    scope,
                    caller),
                offset => allocationFacts.MultiplicityAt(offset),
                calls,
                evidence,
                includeIndirectOpcodes:
                    hasUnsafeApiMember
                    || hasUnsafeSignature
                    || hasUnsafeLocals);
            if (includeOwnershipFlow)
            {
                result.OwnershipFlow =
                    ArrayPoolOwnershipFlow.Analyze(
                        context,
                        calls.ToImmutable());
            }
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            result.Diagnostic = new AnalysisDiagnostic(
                MetadataTokens.GetToken(methodHandle),
                MethodLabel(
                    typeHandle,
                    methodHandle),
                $"{ex.GetType().Name}: {ex.Message}");
            if (includeLeakTriage
                && result.LeakTriage is null)
            {
                result.LeakTriage =
                    LeakTriageAnalyzer.Failed(
                        MetadataTokens.GetToken(methodHandle),
                        leakFailureKind,
                        ex.GetType().Name);
            }
        }
        finally
        {
            // Runs on every exit path so method-local evidence and calls
            // emitted before a recoverable failure remain visible.
            result.UnsafeEvidence = evidence.ToImmutable();
            result.Calls = calls.ToImmutable();
        }
        return result;
    }

    LibraryMethodAnalysisResult AnalyzeLeakTriageMethod(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        MethodDefinitionHandle methodHandle)
    {
        var result = new LibraryMethodAnalysisResult();
        MetadataReader reader = _infrastructure.Reader;
        LeakTriageFailureKind leakFailureKind =
            LeakTriageFailureKind.MethodMetadata;
        try
        {
            var methodDefinition =
                reader.GetMethodDefinition(methodHandle);
            if (!HasManagedIlBody(
                    methodDefinition.ImplAttributes))
                return result;
            if (methodDefinition.RelativeVirtualAddress == 0)
                return result;

            var scope = _infrastructure.CreateScope(
                typeDefinition,
                methodDefinition);
            if (!SignatureBlobGuard.IsSafeToDecode(
                    reader,
                    methodDefinition.Signature,
                    SignatureBlobGuard.Kind.Method))
            {
                result.LeakTriage =
                    LeakTriageAnalyzer.Failed(
                        MetadataTokens.GetToken(methodHandle),
                        LeakTriageFailureKind.MethodMetadata,
                        "SignatureLimit");
                return result;
            }

            var signature =
                methodDefinition.DecodeSignature(
                    TypeRefDecoder.Instance,
                    scope);
            var method = new MethodIdentity(
                _infrastructure.AssemblyName,
                _infrastructure.Mvid,
                TypeRefDecoder.Instance.GetTypeFromDefinition(
                    reader,
                    typeHandle,
                    0),
                reader.GetString(methodDefinition.Name),
                signature.ParameterTypes,
                signature.ReturnType,
                MetadataTokens.GetToken(methodHandle),
                (methodDefinition.Attributes
                    & MethodAttributes.Static) != 0)
            {
                SignatureHeader = signature.Header.RawValue,
                RequiredParameterCount =
                    signature.RequiredParameterCount,
                IsVirtualDispatchOpen =
                    _infrastructure.DispatchCanTargetOverride(
                        typeDefinition,
                        methodDefinition),
            };

            leakFailureKind =
                LeakTriageFailureKind.BodyAcquisition;
            var body =
                _infrastructure.PeReader.GetMethodBody(
                    methodDefinition.RelativeVirtualAddress);
            result.LeakTriage =
                LeakTriageAnalyzer.AnalyzeMethodDetailed(
                    method,
                    body.GetILBytes() ?? [],
                    body.ExceptionRegions,
                    token => _infrastructure.ResolveMethod(
                        token,
                        scope,
                        methodHandle),
                    token =>
                        ArrayPoolExceptionPathAnalyzer.ResolveCatchTypeRef(
                            reader,
                            MetadataTokens.EntityHandle(token),
                            scope));
        }
        catch (Exception ex)
            when (LeakTriageAnalyzer.IsRecoverable(ex))
        {
            result.LeakTriage =
                LeakTriageAnalyzer.Failed(
                    MetadataTokens.GetToken(methodHandle),
                    leakFailureKind,
                    ex.GetType().Name);
        }

        return result;
    }

    internal static bool HasManagedIlBody(
        MethodImplAttributes attributes)
        => (attributes
                & MethodImplAttributes.CodeTypeMask)
            == MethodImplAttributes.IL
            && (attributes
                & MethodImplAttributes.ManagedMask)
            == MethodImplAttributes.Managed;

    internal static MethodInstructions DecodeBody(
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        // The substrate decode contract is BadImageFormatException for
        // malformed IL. Do not use MethodInstructions.Decode: its fail-closed
        // contract would hide the throw from the recoverable-method gate.
        var instructions = InstructionDecoder.Decode(il);
        return new MethodInstructions(
            instructions,
            BlockGraph.Build(
                il.Length,
                instructions,
                exceptionRegions));
    }

    static IReadOnlyList<(int Start, int End)> CollectLoopRegions(
        MethodInstructions body)
    {
        var regions = new List<(int Start, int End)>();
        var blockGraph = body.Blocks;
        foreach (var instruction in body.Instructions)
        {
            if (instruction.OpCode == ILOpCode.Switch)
                continue;
            int sourceBlock =
                blockGraph.BlockIndexAt(instruction.Offset);
            foreach (int target in instruction.BranchTargets)
            {
                if (target >= instruction.Offset)
                    continue;
                int targetBlock =
                    blockGraph.BlockIndexAt(target);
                if (sourceBlock >= 0
                    && targetBlock >= 0
                    && blockGraph.Blocks[sourceBlock]
                        .Edges.Successors.Contains(targetBlock))
                {
                    regions.Add(
                        (target, instruction.Offset));
                }
            }
        }
        return regions;
    }

    ImmutableArray<TypeRef> DecodeLocalTypes(
        MethodBodyBlock body,
        GenericScope scope)
    {
        if (body.LocalSignature.IsNil)
            return [];
        MetadataReader reader = _infrastructure.Reader;
        var signature =
            reader.GetStandaloneSignature(body.LocalSignature);
        if (!SignatureBlobGuard.IsSafeToDecode(
                reader,
                signature.Signature,
                SignatureBlobGuard.Kind.LocalVariables))
        {
            return [];
        }
        return signature.DecodeLocalSignature(
            TypeRefDecoder.Instance,
            scope);
    }

    // Razor-generated render methods lack generated-code attributes. Trust-gate
    // RenderTreeBuilder identity (#1708) so lookalikes do not suppress findings.
    static bool IsBlazorRenderMethod(
        MethodIdentity caller)
    {
        foreach (var parameter in caller.ParameterTypes)
        {
            if (FrameworkIdentity.IsKnownFrameworkType(
                    parameter,
                    "Microsoft.AspNetCore.Components",
                    "Microsoft.AspNetCore.Components.Rendering",
                    "RenderTreeBuilder"))
            {
                return true;
            }
        }
        return false;
    }

    string MethodLabel(
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle)
    {
        try
        {
            MetadataReader reader = _infrastructure.Reader;
            var typeDefinition =
                reader.GetTypeDefinition(typeHandle);
            string ns =
                reader.GetString(typeDefinition.Namespace);
            string typeName =
                reader.GetString(typeDefinition.Name);
            string methodName =
                reader.GetString(
                    reader.GetMethodDefinition(
                        methodHandle).Name);
            string fullTypeName =
                ns.Length == 0
                    ? typeName
                    : $"{ns}.{typeName}";
            return $"{fullTypeName}::{methodName}";
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            return
                $"0x{MetadataTokens.GetToken(methodHandle):X8}";
        }
    }

    internal static bool IsRecoverableMethodFailure(
        Exception ex) =>
        ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or ArgumentOutOfRangeException
            or IndexOutOfRangeException;
}
