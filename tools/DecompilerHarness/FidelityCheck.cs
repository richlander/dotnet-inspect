using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

using CSharpText;
using DotnetInspector.Services;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The semantic-fidelity check (validity is <see cref="ValidityCheck"/>;
/// completeness is the <c>--gaps</c> floor).
/// It closes the loop named in docs/decompiler.md: decompile → recompile →
/// compare IL. A decompiled body that compiles and reads plausibly but recompiles
/// to a different contract body changed the measured program shape
/// (docs/decompiler-taste.md), invisible to the validity check.
///
/// Unlike <see cref="ValidityCheck"/>'s per-method <c>__Shell</c> — which cannot
/// see the declaring type's fields, so any <c>this.field</c> reference fails to
/// bind as noise — this recompiles each member inside a reconstructed shape of
/// its REAL declaring type: the type declaration, every field, every sibling and
/// nested member as a throwing stub, and the one target member's real decompiled
/// body. The C# analog of the IL round-trip suite's full-skeleton scaffold
/// (IlasmScaffold.BuildCompilationUnit). Fields in scope mean a dropped or
/// mis-bound field access surfaces as a body diff, not a compile error.
/// </summary>
static partial class FidelityCheck
{
    /// <summary>
    /// Maximum top-level source types one structured <see cref="Evaluate(string, Func{string, bool}?)"/>
    /// call may send through compile-back. <c>FidelityCheckSelectionGuardTests</c>
    /// gates both sides: the large test assembly is rejected even through an
    /// all-matching predicate, while a focused predicate over that assembly runs.
    /// </summary>
    internal const int MaxEvaluationTypeCount = 256;

    /// <summary>
    /// The compile-back fidelity contract: which differences the comparison treats as
    /// noise rather than defect. Persisted alongside corpus metrics, so a change to
    /// <see cref="ContractBodyDiffNormalization"/> must bump this — otherwise a baseline
    /// and a current run claim the same contract under different equality rules, and the
    /// corpus sensor compares numbers that were never comparable.
    /// </summary>
    /// <remarks>
    /// v2 (#3580) added generated-member ordinal correspondence. Roslyn's local-function
    /// and state-machine names embed an ordinal over the containing type's members, which
    /// necessarily differs once one side is a reconstructed skeleton, so v1 reported
    /// operand differences between identical bodies.
    /// <para>
    /// v3 (#3645) removes the older per-side synthesized-member rewrite. That rewrite
    /// masked real differences between overloads because uniqueness is not a property
    /// either side can establish alone. The correspondence now owns lambda methods and
    /// their cache fields as well as local functions and state machines, so the unsafe
    /// fallback is no longer needed.
    /// </para>
    /// <para>
    /// The flag set is the trigger this version is <em>gated</em> on, but it is not the
    /// whole of what it protects: the equality rules also include how
    /// <c>IlBodyDiff</c> renders an operand, and a renderer change moves them without
    /// moving any flag. Nothing detects that — the guard compares versions, and the
    /// version only moves when a human moves it. Treat a change to operand rendering as
    /// a contract change and reason about it explicitly here.
    /// </para>
    /// <para>
    /// #3491 is the first such change and deliberately does not bump: it added the
    /// signature this-attributes to the rendered text (<c>SignatureThisPrefix</c>), which
    /// only ever <em>appends</em> to an operand and so is monotone toward stricter — a
    /// body that compared unequal before cannot compare equal after. Measured on the
    /// pinned corpus, it moved nothing: <c>Area=Fidelity</c> is 68/0 before and after,
    /// because the corpus carries no <c>EXPLICITTHIS</c> signature and no instance
    /// function pointer. A baseline therefore stays comparable within one contract
    /// version. A renderer
    /// change that is not monotone, or that moves a corpus number, does not get this
    /// argument and must bump.
    /// </para>
    /// </remarks>
    internal const int CurrentContractVersion = 3;

    internal const IlBodyDiffNormalization ContractBodyDiffNormalization =
        IlBodyDiffNormalization.NormalizeVariableLayout
        | IlBodyDiffNormalization.NormalizeCurrentAssemblyScope
        | IlBodyDiffNormalization.NormalizePlatformAssemblyScope
        // Compile-back recompiles a reconstructed unit, whose member ordering
        // is the harness's rather than the original source file's, so Roslyn
        // renumbers the containing-method ordinal in every closure name it
        // synthesizes. That renumbering is not decompiler evidence (#3503).
        //
        // The correspondence folds only where the ordinal-free key is one-to-one on
        // both sides. That two-sided evidence is the entire contract: the retired
        // per-side rewrite could not distinguish overloads and masked real differences
        // (#3645), so contract v3 removes it rather than layering the two mechanisms.
        | IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals;

    const int MaxTransientEmptyEmitAttempts = 3;

    // The render path for one source: the lowered view, or the shipped raised
    // view with the cross-method import seam bound (so lambda raising can reach
    // a synthesized body in the same module). The lowered view carries no seam
    // yet, so a lambda there stays a delegate creation.
    // The default (byte-faithful) renderer threads no PrinterOptions, so every
    // corpus/gate path renders the shipped output. The optional options overload
    // is the opt-in-knob seam the byte-neutrality gate uses to render the raised
    // view with a single knob on and prove it recompiles to the same IL. Options
    // apply to the raised view (the view every opt-in knob targets); the lowered
    // view has no opt-in knobs, so it keeps the shipped renderer.
    static Func<IrFunction, DecompilerResult> Renderer(MetadataSource source, bool lowered)
        => Renderer(source, lowered, options: null);

    static Func<IrFunction, DecompilerResult> Renderer(MetadataSource source, bool lowered, PrinterOptions? options)
        => lowered
            ? CSharpPrinter.PrintLowered
            : function => CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method), options);

    public static int Run(
        IReadOnlyList<string> assemblies,
        int cap,
        int maxExamples,
        bool lowered = false,
        bool timings = false,
        int zeroSignalGuard = 0)
    {
        var phaseTimings = timings ? new FidelityPhaseTimings() : null;
        var zeroSignal = zeroSignalGuard > 0 ? new ZeroSignalGuard(zeroSignalGuard, cap) : null;
        // Release codegen so the recompiled stream is compared against the
        // optimization shape the BCL ships; the fixture assembly is built the
        // same way under -c Release.
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable);

        int total = 0, full = 0, exact = 0, contextFail = 0, recompileFail = 0;
        int opcodeDiff = 0, operandDiff = 0, fidelityUnavailable = 0;
        var opcodeDiffExamples = new List<string>();
        var operandDiffExamples = new List<string>();
        var fidelityUnavailableExamples = new List<string>();
        var recompileFailExamples = new List<string>();
        var contextFailExamples = new List<string>();
        var recompileFailCodes = new SortedDictionary<string, int>(StringComparer.Ordinal);

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var path in assemblies)
        {
            if (total >= cap || zeroSignal?.Stopped == true || zeroSignal?.ShouldRerunWithoutGuard == true)
                break;
            PEReader pe;
            try { pe = new PEReader(File.OpenRead(path)); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { continue; }
            using (pe)
            {
                if (!pe.HasMetadata)
                    continue;
                var featureOptions = CompilerFeatureOptions.Resolve(pe);
                var reader = pe.GetMetadataReader();
                MetadataSource source;
                try { source = MetadataSource.Open(path, context: metadata); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { continue; }
                RegisterSourceContext(source, metadata);
                var references = RuntimeReferences(path);
                using (source)
                {
                    var render = Renderer(source, lowered);
                    foreach (var typeHandle in reader.TypeDefinitions)
                    {
                        if (total >= cap || zeroSignal?.Stopped == true || zeroSignal?.ShouldRerunWithoutGuard == true)
                            break;
                        int effectiveCap = zeroSignal?.EffectiveCap(total) ?? cap;
                        RunType(reader, pe, source, typeHandle, references, featureOptions, compileOptions,
                            effectiveCap, maxExamples, render, ref total, ref full, ref exact, ref contextFail,
                            ref recompileFail, ref opcodeDiff, ref operandDiff, ref fidelityUnavailable,
                            opcodeDiffExamples, operandDiffExamples, fidelityUnavailableExamples, recompileFailExamples,
                            contextFailExamples, recompileFailCodes, phaseTimings);
                        zeroSignal?.Observe(total, exact, opcodeDiff + operandDiff, recompileFail, contextFail, recompileFailCodes);
                    }
                }

            }
        }

        if (zeroSignal?.ShouldRerunWithoutGuard == true)
        {
            zeroSignal.Report();
            return Run(assemblies, cap, maxExamples, lowered, timings, zeroSignalGuard: 0);
        }

        Report(total, full, exact, contextFail, recompileFail, opcodeDiff, operandDiff,
            fidelityUnavailable, recompileFailCodes, opcodeDiffExamples, operandDiffExamples,
            fidelityUnavailableExamples, recompileFailExamples, contextFailExamples, zeroSignal);
        phaseTimings?.Report();
        return 0;
    }

    internal static IReadOnlyList<CompileBackTarget> SelectReturnToSenderTargets(
        IReadOnlyList<string> assemblies,
        int cap,
        string? typeFilter = null)
        => SelectReturnToSenderTargetPlan(
            assemblies,
            cap,
            typeFilter).Targets;

    internal static ReturnToSenderTargetSelection SelectReturnToSenderTargetPlan(
        IReadOnlyList<string> assemblies,
        int cap,
        string? typeFilter = null,
        CSharpLanguageProfile? languageProfile = null)
    {
        if (cap <= 0)
            return new([], [], 0, 0, 0);

        var selected = new List<CompileBackTarget>(Math.Min(cap, 4096));
        var exclusions = new List<ReturnToSenderTargetExclusion>();
        int scannedBodyCount = 0;
        int declarationCandidateCount = 0;
        int eligibleCount = 0;
        CSharpLanguageProfile profile =
            languageProfile
            ?? new(CSharpLanguageVersion.Preview);
        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            int remaining = cap - selected.Count;
            if (remaining <= 0)
                break;

            using var source = MetadataSource.Open(assemblyPath, context: metadata);
            RegisterSourceContext(source, metadata);
            var reader = source.Reader;
            TargetApiEvidence targetApiEvidence =
                CreateTargetApiEvidence(source.Pe);
            using var declarations =
                new ReturnToSenderDeclarationSession(assemblyPath);
            var decisions =
                new Dictionary<int, ReturnToSenderCandidateDecision>();
            foreach (var candidate in IrImporter.GetStableSampleCandidates(
                         source,
                         remaining,
                         candidate =>
                         {
                             scannedBodyCount++;
                             ReturnToSenderCandidateDecision? decision =
                                 DecideStandaloneReturnToSenderCandidate(
                                     assemblyPath,
                                     reader,
                                     candidate,
                                     typeFilter,
                                     targetApiEvidence,
                                     declarations,
                                     profile,
                                     out bool declarationCandidate);
                             if (declarationCandidate)
                                 declarationCandidateCount++;
                             if (decision is null)
                                 return false;

                             int token = System.Reflection.Metadata.Ecma335
                                 .MetadataTokens.GetToken(
                                     candidate.MethodHandle);
                             decisions.Add(token, decision);
                             if (decision.Exclusion is { } exclusion)
                             {
                                 exclusions.Add(exclusion);
                                 return false;
                             }

                             eligibleCount++;
                             return true;
                         },
                         candidate =>
                         {
                             int token = System.Reflection.Metadata.Ecma335
                                 .MetadataTokens.GetToken(
                                     candidate.MethodHandle);
                             return decisions[token].StableIdentitySuffix;
                         }))
            {
                int token = System.Reflection.Metadata.Ecma335.MetadataTokens
                    .GetToken(candidate.MethodHandle);
                selected.Add(decisions[token].Target
                    ?? throw new InvalidOperationException(
                        "A selected standalone RTS target lost its declaration decision."));
            }
        }

        return new(
            selected.ToArray(),
            exclusions
                .OrderBy(exclusion => exclusion.AssemblyPath, StringComparer.Ordinal)
                .ThenBy(exclusion => exclusion.Type, StringComparer.Ordinal)
                .ThenBy(exclusion => exclusion.Method, StringComparer.Ordinal)
                .ThenBy(exclusion => exclusion.Signature, StringComparer.Ordinal)
                .ThenBy(exclusion => exclusion.Reason)
                .ThenBy(exclusion => exclusion.Overload)
                .ToArray(),
            scannedBodyCount,
            declarationCandidateCount,
            eligibleCount);
    }

    static ReturnToSenderCandidateDecision?
        DecideStandaloneReturnToSenderCandidate(
        string assemblyPath,
        MetadataReader reader,
        IrImporter.StableSampleCandidate candidate,
        string? typeFilter,
        TargetApiEvidence targetApiEvidence,
        ReturnToSenderDeclarationSession declarations,
        CSharpLanguageProfile languageProfile,
        out bool declarationCandidate)
    {
        declarationCandidate = false;
        var typeDef = reader.GetTypeDefinition(candidate.TypeDefHandle);
        if (!typeDef.GetDeclaringType().IsNil
            || ShapeOf(reader, typeDef) is not (TypeKind.Class or TypeKind.Struct)
            || (typeFilter is not null
                && !candidate.TypeName.Contains(typeFilter, StringComparison.Ordinal))
            || IsGeneratedType(reader, typeDef, candidate.TypeName))
        {
            return null;
        }

        var method = reader.GetMethodDefinition(candidate.MethodHandle);
        if (IsGeneratedMethod(
                reader,
                method,
                candidate.MethodName,
                allowEmbeddedAngleBrackets: true))
            return null;

        declarationCandidate = true;
        MetadataMethodAddress address =
            MetadataMethodAddress.Create(reader, candidate.MethodHandle);
        int token = System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(
            candidate.MethodHandle);
        string stableIdentitySuffix = "generic-arity:"
            + method.GetGenericParameters().Count.ToString(
                CultureInfo.InvariantCulture);
        MemberSignatureShapeResult signatureShape =
            MetadataMemberSignatureShape.Create(
                reader,
                candidate.MethodHandle);
        string? signature = signatureShape.Shape is { } shape
            ? MemberSignatureShapeCodec.Encode(shape)
            : null;

        ReturnToSenderTargetExclusion Exclude(
            ReturnToSenderTargetExclusionReason reason,
            ReturnToSenderDeclarationProducer? producer = null,
            CSharpDeclarationRepresentabilityResult? exactOutcome = null)
            => new(
                assemblyPath,
                candidate.TypeName,
                candidate.MethodName,
                candidate.Overload,
                signature,
                address,
                reason,
                producer,
                exactOutcome);

        bool hasApiEntry =
            targetApiEvidence.Index.TryGetValue(token, out var entry);
        bool mappedAccessor =
            targetApiEvidence.AccessorTokens.Contains(token);
        if (hasApiEntry
            && (mappedAccessor
                || entry.Member.MethodSemantics is not
                    ApiMethodSemanticsKind.None))
        {
            return new(
                null,
                Exclude(
                    !mappedAccessor
                        && entry.Member.MethodSemantics is null
                        ? ReturnToSenderTargetExclusionReason
                            .MethodSemanticsUnavailable
                        : ReturnToSenderTargetExclusionReason
                            .AccessorDeferred),
                stableIdentitySuffix);
        }

        ReturnToSenderDeclarationSelection declaration;
        CSharpDeclarationRepresentabilityResult? exactResult = null;
        CSharpMethodDeclarationPost? exactPost = null;
        bool inspectExactDeclaration =
            !hasApiEntry
            || entry.Member.Kind is
                "explicit-interface-implementation" or "operator";
        bool requiresExactDeclaration =
            hasApiEntry
            && entry.Member.Kind == "explicit-interface-implementation";
        if (inspectExactDeclaration)
        {
            exactPost = declarations.Capture(
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    candidate.TypeDefHandle),
                address);
            requiresExactDeclaration |=
                exactPost.Implementations is not
                    MetadataMethodImplementationResult.Absent;
        }

        if (requiresExactDeclaration)
        {
            exactPost ??= declarations.Capture(
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    candidate.TypeDefHandle),
                address);
            exactResult = CSharpDeclarationRepresentability.Decide(
                exactPost,
                languageProfile);
            switch (exactResult)
            {
                case CSharpDeclarationRepresentabilityResult.Representable
                    represented:
                    declaration =
                        new ReturnToSenderDeclarationSelection.ExactMethod(
                            represented.Request);
                    break;
                case CSharpDeclarationRepresentabilityResult.Unrepresentable:
                    return new(
                        null,
                        Exclude(
                            ReturnToSenderTargetExclusionReason
                                .ExactDeclarationUnrepresentable,
                            ReturnToSenderDeclarationProducer
                                .ExactMethodDeclaration,
                            exactResult),
                        stableIdentitySuffix);
                case CSharpDeclarationRepresentabilityResult.Unavailable:
                    return new(
                        null,
                        Exclude(
                            ReturnToSenderTargetExclusionReason
                                .ExactDeclarationUnavailable,
                            ReturnToSenderDeclarationProducer
                                .ExactMethodDeclaration,
                            exactResult),
                        stableIdentitySuffix);
                default:
                    throw new InvalidOperationException(
                        "Unknown C# declaration representability result.");
            }
        }
        else
        {
            if (!hasApiEntry)
            {
                return new(
                    null,
                    Exclude(
                        ReturnToSenderTargetExclusionReason
                            .ProductMemberUnavailable,
                        ReturnToSenderDeclarationProducer
                            .OrdinaryTypeArtifact,
                        exactResult),
                    stableIdentitySuffix);
            }

            if (!CSharpMemberArtifactEligibility.IsRepresentable(
                    entry.Type,
                    entry.Member))
            {
                return new(
                    null,
                    Exclude(
                        ReturnToSenderTargetExclusionReason
                            .OrdinaryDeclarationUnrepresentable,
                        ReturnToSenderDeclarationProducer
                            .OrdinaryTypeArtifact),
                    stableIdentitySuffix);
            }

            declaration =
                new ReturnToSenderDeclarationSelection.OrdinaryMethod();
        }

        if (signature is null)
        {
            return new(
                null,
                Exclude(
                    ReturnToSenderTargetExclusionReason
                        .CanonicalSignatureUnavailable,
                    declaration is
                        ReturnToSenderDeclarationSelection.ExactMethod
                            ? ReturnToSenderDeclarationProducer
                                .ExactMethodDeclaration
                            : ReturnToSenderDeclarationProducer
                                .OrdinaryTypeArtifact,
                    exactResult),
                stableIdentitySuffix);
        }

        return new(
            new CompileBackTarget(
                assemblyPath,
                candidate.TypeName,
                candidate.MethodName,
                candidate.Overload,
                signature,
                address,
                declaration),
            null,
            stableIdentitySuffix);
    }

    sealed class ReturnToSenderDeclarationSession : IDisposable
    {
        readonly string _assemblyPath;
        AssemblyInspectionSession? _assembly;
        MetadataOperationContext? _operation;
        MetadataDeclarationSession? _declarations;

        internal ReturnToSenderDeclarationSession(string assemblyPath)
            => _assemblyPath = assemblyPath;

        internal CSharpMethodDeclarationPost Capture(
            MetadataTypeDefinitionAddress type,
            MetadataMethodAddress method)
        {
            _assembly ??= AssemblyInspectionSession.Open(_assemblyPath);
            _operation ??= new(
                MetadataOperationPolicy.Unbounded);
            _declarations ??=
                _assembly.CreateDeclarationSession(_operation);
            return CSharpMethodDeclarationPost.Capture(
                _declarations,
                type,
                method);
        }

        public void Dispose()
        {
            _declarations?.Dispose();
            _operation?.Dispose();
            _assembly?.Dispose();
        }
    }

    public static async Task<int> RunMethodDelta(
        IReadOnlyList<string> assemblies,
        string deltaPath,
        int maxExamples,
        bool lowered = false)
    {
        var artifact = JsonSerializer.Deserialize<CorpusMethodDeltaArtifact>(
            File.ReadAllText(deltaPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Could not read corpus delta '{deltaPath}'.");

        var allTargets = artifact.ChangedMethods
            .Where(row => row.Current is not null)
            .Select(row => MethodTarget.From(row.Current!))
            .DistinctBy(TargetKey)
            .OrderBy(target => target.DisplayMethod, StringComparer.Ordinal)
            .ToArray();

        // Compiler-generated / synthesized members — regex source-generator output
        // (`<RegexGenerator_g>…`), lambda display classes, local-function frames,
        // the `<Module>` pseudo-type — are never recompiled by the fidelity
        // skeleton: their `<…>` names are not legal C# and CollectType/BuildUnit
        // skip them by design. Classify them up front so they report as an explicit
        // unsupported bucket instead of masquerading as a target-method-not-found
        // lookup bug.
        var supported = allTargets.Where(target => !IsSynthesizedTarget(target)).ToArray();

        var results = lowered
            ? EvaluateTargets(assemblies, supported, lowered: true).ToList()
            : (await EvaluateChangedMethodTargets(assemblies, supported)).ToList();
        foreach (var target in allTargets.Where(IsSynthesizedTarget))
            results.Add(new TargetedCompileBackResult(
                target,
                new CompileBackResult(
                    target.Type, target.Method, target.Overload, target.Signature,
                    CompileBackStatus.ContextFail,
                    "",
                    "",
                    "generated-member-unsupported",
                    lowered ? CaptureMode.WholeModule : CaptureMode.ProductArtifact,
                    lowered
                        ? "legacy whole-module (lowered)"
                        : "product-artifact RTS; compile-back-floor=false")));

        Console.WriteLine(
            $"Changed-method delta contracts: baseline v{artifact.BaselineFidelityContractVersion}, "
            + $"current v{artifact.CurrentFidelityContractVersion}; evaluating v{CurrentContractVersion}");
        Console.WriteLine(lowered
            ? "Changed-method engine: legacy whole-module (lowered)"
            : "Changed-method engine: product-artifact RTS (raised; compile-back-floor=false)");
        ReportTargeted(results, allTargets.Length, maxExamples);
        return 0;
    }

    /// <summary>
    /// A changed method the fidelity skeleton cannot recompile because its type or
    /// member is compiler-synthesized — an `<…>` name (source-generator output,
    /// display class, iterator, async state machine, local-function frame) or the
    /// `&lt;Module&gt;` pseudo-type. Reported as unsupported rather than a lookup miss.
    /// </summary>
    static bool IsSynthesizedTarget(MethodTarget target)
        => IsSynthesizedMember(target.Type, target.Method);

    /// <summary>
    /// A delta row whose type or member is compiler-synthesized — an `&lt;…&gt;`
    /// name (source-generator output, display class, iterator, async state
    /// machine, local-function frame) or the `&lt;Module&gt;` pseudo-type. The
    /// fidelity skeleton never recompiles these, so the targeted path reports them
    /// as unsupported instead of a lookup miss.
    /// </summary>
    internal static bool IsSynthesizedMember(string type, string method)
        => type.Contains('<') || method.Contains('<') || type == "<Module>";

    /// <summary>
    /// Every full type name the targeted delta path can collect compile-back
    /// entries for, nested types included (each threaded through its declaring
    /// types as <c>Outer.Inner</c>) — the identity surface a delta row's
    /// <c>Type</c> is matched against. Exposed for the nested-type lookup
    /// regression test; before the nested-aware fix this set held only top-level
    /// types, so any changed method on a nested type fell into the
    /// <c>target-method-not-found</c> bucket.
    /// </summary>
    /// <param name="typeFilter">
    /// Optional full-type-name predicate applied before any method is decompiled.
    /// Collecting a name still costs a full render of the type's methods, so a
    /// caller interested in a few types should say so rather than pay for every
    /// type in the assembly. Declining a type only removes it from the returned
    /// set; it never changes how a retained name is computed.
    /// </param>
    internal static IReadOnlyList<string> CollectibleFullTypeNames(string assemblyPath, Func<string, bool>? typeFilter = null)
    {
        var names = new List<string>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            return names;
        var reader = pe.GetMetadataReader();
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);
        RegisterSourceContext(source, metadata);
        var render = Renderer(source, lowered: false);
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (!typeDef.GetDeclaringType().IsNil)
                continue;
            foreach (var (fullType, _, _) in EnumerateTypeTree(reader, pe, source, typeHandle, render, typeFilter))
                names.Add(fullType);
        }
        return names;
    }

    /// <summary>The fidelity check outcome for one method.</summary>
    public enum CompileBackStatus
    {
        /// <summary>Recompiled to the same body under the compile-back fidelity contract — the goal.</summary>
        Exact,
        /// <summary>Rendered at Full fidelity but recompiled to a different stream (a defect).</summary>
        OpcodeDiff,
        /// <summary>Opcode names matched, but a contract-observable operand or branch target differed.</summary>
        OperandDiff,
        /// <summary>The product body comparison could not produce a verdict.</summary>
        FidelityUnavailable,
        /// <summary>Imported below Full fidelity, so an opcode diff is expected, not a defect.</summary>
        NotFull,
        /// <summary>The decompiled body did not recompile (e.g. an unbindable construct).</summary>
        RecompileFail,
        /// <summary>The type skeleton could not be emitted or the original/recompiled method was not found.</summary>
        ContextFail,
    }

    /// <summary>
    /// Which reconstruction produced a row — the provenance the segmented
    /// "safely-capturable" bands read (#1412). <see cref="WholeModule"/> bound (or
    /// failed) under the whole-module skeleton; <see cref="Cluster"/> bound under
    /// the reconstruction closure after the whole-module attempt failed (an
    /// unrelated-sibling rescue); <see cref="ClusterBailed"/> failed the
    /// whole-module attempt *and* the closure escalation — the principled
    /// not-safely-capturable signal.
    /// </summary>
    public enum CaptureMode
    {
        WholeModule,
        Cluster,
        ClusterBailed,
        ProductArtifact,
    }

    /// <summary>
    /// How the fidelity loop reconstructs each target (#1412). <see cref="Off"/> is
    /// whole-module only. <see cref="Escalate"/> runs the cheap whole-module
    /// compile first and escalates only the rows it could not check to the
    /// (per-method, iterative) closure path — the operational order, since a
    /// whole-module Exact never needs re-checking and only its failures can
    /// improve. <see cref="ForceAll"/> attempts the closure for every row first;
    /// it exercises the closure engine maximally and is the test seam.
    /// </summary>
    public enum ClusterMode
    {
        Off,
        Escalate,
        ForceAll,
    }

    /// <summary>One method's fidelity check result, with both opcode streams for diagnostics.</summary>
    public sealed record CompileBackResult(
        string Type, string Method, int Overload, string Signature, CompileBackStatus Status,
        string OriginalOpcodes, string RecompiledOpcodes, string? Detail,
        CaptureMode Capture = CaptureMode.WholeModule,
        string? CaptureDetail = null,
        IlBodyDiffResult? FidelityDiff = null,
        string? Annotated = null,
        bool UsedProductWholeMember = false);

    internal static CompileBackStatus ClassifyStatus(
        bool isFull,
        bool opcodesExact,
        IlBodyDiffResult? fidelityDiff)
    {
        if (!opcodesExact)
            return isFull ? CompileBackStatus.OpcodeDiff : CompileBackStatus.NotFull;
        if (fidelityDiff is null || !fidelityDiff.IsAvailable)
            return CompileBackStatus.FidelityUnavailable;
        if (fidelityDiff.Outcome == IlBodyDiffOutcome.Exact)
            return CompileBackStatus.Exact;
        return isFull ? CompileBackStatus.OperandDiff : CompileBackStatus.NotFull;
    }

    internal sealed record CompileBackTarget(
        string AssemblyPath,
        string Type,
        string Method,
        int Overload,
        string Signature,
        MetadataMethodAddress? Address = null,
        ReturnToSenderDeclarationSelection? Declaration = null);

    internal abstract record ReturnToSenderDeclarationSelection
    {
        private ReturnToSenderDeclarationSelection()
        {
        }

        internal sealed record OrdinaryMethod
            : ReturnToSenderDeclarationSelection;

        internal sealed record ExactMethod(
            CSharpAcceptedDeclarationRequest Request)
            : ReturnToSenderDeclarationSelection;
    }

    internal enum ReturnToSenderDeclarationProducer
    {
        OrdinaryTypeArtifact,
        ExactMethodDeclaration,
    }

    internal enum ReturnToSenderTargetExclusionReason
    {
        ProductMemberUnavailable,
        MethodSemanticsUnavailable,
        AccessorDeferred,
        OrdinaryDeclarationUnrepresentable,
        ExactDeclarationUnrepresentable,
        ExactDeclarationUnavailable,
        CanonicalSignatureUnavailable,
    }

    internal sealed record ReturnToSenderTargetExclusion(
        string AssemblyPath,
        string Type,
        string Method,
        int Overload,
        string? Signature,
        MetadataMethodAddress Address,
        ReturnToSenderTargetExclusionReason Reason,
        ReturnToSenderDeclarationProducer? Producer = null,
        CSharpDeclarationRepresentabilityResult? ExactOutcome = null);

    internal sealed record ReturnToSenderTargetSelection(
        IReadOnlyList<CompileBackTarget> Targets,
        IReadOnlyList<ReturnToSenderTargetExclusion> Exclusions,
        int ScannedBodyCount,
        int DeclarationCandidateCount,
        int EligibleCount);

    sealed record ReturnToSenderCandidateDecision(
        CompileBackTarget? Target,
        ReturnToSenderTargetExclusion? Exclusion,
        string StableIdentitySuffix);

    /// <summary>
    /// Runs the fidelity check loop over one assembly and returns a structured result
    /// per rendered method, without printing. This is the testable entry point the
    /// xunit gate uses to assert the green set stays contract-exact; <see cref="Run"/>
    /// is the console-reporting entry point. Shares all of the skeleton-emission and
    /// opcode-comparison machinery so the two paths can never drift.
    /// </summary>
    /// <param name="typeFilter">
    /// Optional predicate over the full type name, applied before any decompile or
    /// recompile work. A caller that wants a single fixture type out of a large
    /// assembly should pass it here rather than filtering the returned results: this
    /// overload otherwise renders and recompiles <em>every</em> type in the assembly,
    /// which for a test assembly is thousands of types of wasted work. Names match
    /// <see cref="CompileBackResult.Type"/>, so the predicate and a post-filter select
    /// the same rows. Filtering does not change how any individual result is computed,
    /// because the reconstruction skeleton is rebuilt from whole-module metadata inside
    /// the compile step.
    ///
    /// A supplied predicate that leaves no processable top-level class or struct
    /// throws instead of returning a vacuous green result. Selection is also
    /// limited to <see cref="MaxEvaluationTypeCount"/> processable types after
    /// filtering, so an all-matching predicate cannot disguise an unbounded sweep.
    /// </param>
    public static IReadOnlyList<CompileBackResult> Evaluate(string assemblyPath, Func<string, bool>? typeFilter = null)
        => Evaluate(assemblyPath, lowered: false, ClusterMode.Off, typeFilter, methodFilter: null);

    /// <summary>
    /// Runs the fidelity check only for methods admitted by
    /// <paramref name="methodFilter"/>. The selector receives metadata identity
    /// before per-method import, render, disassembly, or compile-back work. The
    /// selected method is still compiled in a whole-module skeleton.
    /// </summary>
    public static IReadOnlyList<CompileBackResult> Evaluate(
        string assemblyPath,
        Func<string, bool> typeFilter,
        Func<EvaluationMethod, bool> methodFilter)
    {
        ArgumentNullException.ThrowIfNull(typeFilter);
        ArgumentNullException.ThrowIfNull(methodFilter);
        return Evaluate(assemblyPath, lowered: false, ClusterMode.Off, typeFilter, methodFilter);
    }

    /// <summary>
    /// Runs the fidelity check roundtrip for a chosen view — the shipped raised
    /// view (<paramref name="lowered"/> false) or the lowered view (true), so
    /// each official C# view earns its own compiler→decompiler→compiler
    /// validation. The renderer is built here so the raised path can bind the
    /// cross-method import seam from the open source (lambda raising).
    /// </summary>
    public static IReadOnlyList<CompileBackResult> Evaluate(string assemblyPath, bool lowered, Func<string, bool>? typeFilter = null)
        => Evaluate(assemblyPath, lowered, ClusterMode.Off, typeFilter, methodFilter: null);

    /// <summary>
    /// Compatibility overload: <paramref name="cluster"/> true selects the
    /// operational <see cref="ClusterMode.Escalate"/> path (whole-module first,
    /// then escalate only its failures to the reconstruction closure).
    /// </summary>
    public static IReadOnlyList<CompileBackResult> Evaluate(string assemblyPath, bool lowered, bool cluster, Func<string, bool>? typeFilter = null)
        => Evaluate(assemblyPath, lowered, cluster ? ClusterMode.Escalate : ClusterMode.Off, typeFilter, methodFilter: null);

    /// <summary>
    /// Evaluates one assembly with the chosen reconstruction-closure (cluster)
    /// strategy (#1412). The test seam for the cluster path; the CB_CLUSTER
    /// environment variable selects <see cref="ClusterMode.Escalate"/> for the
    /// console path.
    /// </summary>
    public static IReadOnlyList<CompileBackResult> Evaluate(
        string assemblyPath,
        bool lowered,
        ClusterMode clusterMode,
        Func<string, bool>? typeFilter = null,
        Func<EvaluationMethod, bool>? methodFilter = null)
    {
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable);

        var results = new List<CompileBackResult>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
        {
            if (typeFilter is not null)
            {
                throw new ArgumentException(
                    $"The type filter selected no processable top-level class or struct because "
                    + $"'{assemblyPath}' does not contain managed metadata.",
                    nameof(typeFilter));
            }
            if (methodFilter is not null)
            {
                throw new ArgumentException(
                    $"The method filter selected no processable method because "
                    + $"'{assemblyPath}' does not contain managed metadata.",
                    nameof(methodFilter));
            }

            return results;
        }
        var featureOptions = CompilerFeatureOptions.Resolve(pe);
        var reader = pe.GetMetadataReader();
        var selectedTypes = SelectEvaluationTypes(reader, assemblyPath, typeFilter);
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);
        RegisterSourceContext(source, metadata);
        var render = Renderer(source, lowered);
        var references = RuntimeReferences(assemblyPath);

        foreach (var typeHandle in selectedTypes)
        {
            EvaluateType(
                reader,
                pe,
                source,
                typeHandle,
                references,
                featureOptions,
                compileOptions,
                render,
                results,
                clusterMode: clusterMode,
                methodFilter: methodFilter);
        }

        if (methodFilter is not null && results.Count == 0)
        {
            throw new ArgumentException(
                "The method filter selected no processable method after type filtering. "
                + "Match EvaluationMethod.Type, Method, and Overload against a method with a body.",
                nameof(methodFilter));
        }

        return results;
    }

    static IReadOnlyList<TypeDefinitionHandle> SelectEvaluationTypes(
        MetadataReader reader,
        string assemblyPath,
        Func<string, bool>? typeFilter)
    {
        var selected = new List<TypeDefinitionHandle>();

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (!typeDef.GetDeclaringType().IsNil
                || ShapeOf(reader, typeDef) is not (TypeKind.Class or TypeKind.Struct))
            {
                continue;
            }

            string fullType = reader.GetFullTypeName(typeDef);
            if (typeFilter is not null)
            {
                if (!typeFilter(fullType))
                    continue;
            }

            if (!IsGeneratedType(reader, typeDef, fullType))
                selected.Add(typeHandle);
        }

        if (typeFilter is not null && selected.Count == 0)
        {
            throw new ArgumentException(
                $"The type filter selected no processable top-level class or struct in '{assemblyPath}'. "
                + "Nested types are not independent broad-sweep roots; select their containing top-level type. "
                + "Metadata paths elsewhere use 'Outer.Inner', not reflection's nested-type '+' spelling.",
                nameof(typeFilter));
        }

        if (selected.Count > MaxEvaluationTypeCount)
        {
            throw new InvalidOperationException(
                $"FidelityCheck.Evaluate selected {selected.Count} processable top-level types from "
                + $"'{assemblyPath}', exceeding its budget of {MaxEvaluationTypeCount}. "
                + $"Pass a type filter that admits at most {MaxEvaluationTypeCount} types, or use the "
                + "bounded multi-assembly Evaluate overload for corpus sampling. "
                + "The budget applies after filtering, so an all-matching predicate does not bypass it.");
        }

        return selected;
    }

    public static IReadOnlyList<CompileBackResult> Evaluate(IReadOnlyList<string> assemblies, int perAssemblyCap, bool lowered, int? workers = null, bool sequential = false)
        => Evaluate(assemblies, perAssemblyCap, lowered, includeAllResults: false, workers, sequential);

    public static IReadOnlyList<CompileBackResult> Evaluate(IReadOnlyList<string> assemblies, int perAssemblyCap, bool lowered, bool includeAllResults, int? workers = null, bool sequential = false)
    {
        if (perAssemblyCap <= 0)
            return [];

        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable);

        var results = new List<CompileBackResult>();
        using var metadata = CorpusMetadata.Create(assemblies);
        var options = new ParallelOptions { MaxDegreeOfParallelism = sequential ? 1 : (workers ?? Math.Max(1, Environment.ProcessorCount - 2)) };

        foreach (var assemblyPath in assemblies)
        {
            var assemblyResults = new ConcurrentBag<CompileBackResult>();
            int attempts = 0;
            int attemptCap = Math.Max(perAssemblyCap * 10, 100);
            int successCount = 0;

            PEReader pe;
            try { pe = new PEReader(File.OpenRead(assemblyPath)); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { continue; }
            using (pe)
            {
                if (!pe.HasMetadata)
                    continue;
                var featureOptions = CompilerFeatureOptions.Resolve(pe);
                var reader = pe.GetMetadataReader();
                MetadataSource source;
                try { source = MetadataSource.Open(assemblyPath, context: metadata); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { continue; }
                RegisterSourceContext(source, metadata);
                using (source)
                {
                    // Pre-warm type maps.
                    _ = source.ResolveShape(TypeRef.CoreLib("System", "Int32"));
                    var references = RuntimeReferences(assemblyPath);

                    Parallel.ForEach(reader.TypeDefinitions, options, (typeHandle, state) =>
                    {
                        if (ShouldSkipBeforeSampleReservation(reader, typeHandle))
                            return;

                        if (Volatile.Read(ref successCount) >= perAssemblyCap)
                        {
                            state.Break();
                            return;
                        }

                        int currentAttempts = Volatile.Read(ref attempts);
                        if (currentAttempts >= attemptCap)
                        {
                            state.Break();
                            return;
                        }
                        
                        // Atomically reserve a block of attempts for this type
                        int reserved = Math.Min(8, attemptCap - currentAttempts);
                        if (Interlocked.Add(ref attempts, reserved) > attemptCap + reserved)
                            return;

                        // Each type compilation has its own render lambda so it's safe.
                        var render = Renderer(source, lowered);

                        var typeResults = new List<CompileBackResult>();
                        EvaluateType(reader, pe, source, typeHandle, references, featureOptions, compileOptions, render, typeResults, reserved);
                        
                        var selectableResults = includeAllResults ? typeResults : typeResults.Where(IsUsefulCorpusSample);
                        foreach (var res in selectableResults)
                        {
                            if (Interlocked.Increment(ref successCount) <= perAssemblyCap)
                            {
                                assemblyResults.Add(res);
                            }
                            else
                            {
                                Interlocked.Decrement(ref successCount);
                                break;
                            }
                        }
                    });
                }
            }
            results.AddRange(assemblyResults.OrderBy(r => r.Type, StringComparer.Ordinal)
                                            .ThenBy(r => r.Method, StringComparer.Ordinal)
                                            .ThenBy(r => r.Signature, StringComparer.Ordinal));
        }
        return results;
    }

    static bool ShouldSkipBeforeSampleReservation(MetadataReader reader, TypeDefinitionHandle typeHandle)
    {
        try
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (ShapeOf(reader, typeDef) is not (TypeKind.Class or TypeKind.Struct))
                return true;
            return IsGeneratedType(reader, typeDef, reader.GetFullTypeName(typeDef));
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return true;
        }
    }

    internal static bool IsUsefulCorpusSample(CompileBackResult result)
        => result.Status is CompileBackStatus.Exact
            or CompileBackStatus.OpcodeDiff
            or CompileBackStatus.OperandDiff
            or CompileBackStatus.FidelityUnavailable;

    sealed record MethodTarget(
        string Assembly,
        string AssemblyPath,
        string Type,
        string Method,
        int Overload,
        string Signature,
        string DisplayMethod)
    {
        public static MethodTarget From(CorpusMethodSnapshot method)
            => new(
                method.Assembly,
                method.AssemblyPath,
                method.Type,
                method.Method,
                method.Overload,
                method.Signature,
                method.DisplayMethod);
    }

    sealed record TargetedCompileBackResult(MethodTarget Target, CompileBackResult Result);

    sealed record ReferenceSet(ImmutableArray<MetadataReference> Metadata, SignatureSpellability Accessibility);

    sealed record CompilerReference(ResolvedAssemblyReference Reference, bool PlatformTrusted);

    sealed class CompilerReferenceResolver(IEnumerable<CompilerReference> references) : IAssemblyReferenceResolver
    {
        readonly IReadOnlyList<CompilerReference> _references = references.ToList();

        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        {
            foreach (var candidate in _references)
            {
                if (scope == AssemblyResolutionScope.Platform && !candidate.PlatformTrusted)
                    continue;

                var reference = candidate.Reference;
                if (!reference.Identity.Name.Equals(identity.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!CultureMatches(identity.Culture, reference.Identity.Culture))
                    continue;
                if (identity.PublicKeyToken is { Length: > 0 }
                    && !string.Equals(identity.PublicKeyToken, reference.Identity.PublicKeyToken, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (identity.Version is not null && reference.Identity.Version != identity.Version)
                    continue;
                return reference;
            }

            return null;
        }

        static bool CultureMatches(string? expected, string? actual)
        {
            if (string.IsNullOrEmpty(expected))
                return true;

            static string Normalize(string? culture)
                => string.IsNullOrEmpty(culture) || culture.Equals("neutral", StringComparison.OrdinalIgnoreCase)
                    ? ""
                    : culture;

            return Normalize(expected).Equals(Normalize(actual), StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class ZeroSignalGuard
    {
        readonly int _probeCount;
        readonly int _requestedCap;

        public ZeroSignalGuard(int probeCount, int requestedCap)
        {
            _probeCount = Math.Max(1, probeCount);
            _requestedCap = requestedCap;
        }

        public bool Stopped { get; private set; }
        public bool ProbeCompleted { get; private set; }
        public bool ShouldRerunWithoutGuard => ProbeCompleted && !Stopped;
        public int StopCount { get; private set; }
        public string? DominantBucket { get; private set; }
        public int DominantCount { get; private set; }

        public int EffectiveCap(int total)
            => ProbeCompleted || Stopped ? _requestedCap : Math.Min(_requestedCap, Math.Max(total, _probeCount));

        public void Observe(
            int total,
            int exact,
            int diffCount,
            int recompileFail,
            int contextFail,
            IReadOnlyDictionary<string, int> recompileFailCodes)
        {
            if (ProbeCompleted || Stopped || total < _probeCount)
                return;

            ProbeCompleted = true;
            if (exact + diffCount != 0 || recompileFail + contextFail != total)
                return;

            var dominant = recompileFailCodes
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .FirstOrDefault();
            string bucket = dominant.Key ?? "<unknown>";
            int count = dominant.Value;
            if (contextFail > count)
            {
                bucket = "context-fail";
                count = contextFail;
            }
            if (count * 10L < total * 9L)
                return;

            Stopped = true;
            StopCount = total;
            DominantBucket = bucket;
            DominantCount = count;
        }

        public void Report()
        {
            if (Stopped)
            {
                string pct = StopCount == 0 ? "0" : $"{100.0 * DominantCount / StopCount:F2}%";
                Console.WriteLine($"  zero-signal guard : stopped after {StopCount} of requested {_requestedCap}; no Exact/OpcodeDiff/OperandDiff rows; dominant {DominantBucket}: {DominantCount} ({pct})");
            }
            else if (ProbeCompleted)
            {
                Console.WriteLine($"  zero-signal guard : probe {_probeCount} found useful or mixed signal; continued to requested cap");
            }
            else
            {
                Console.WriteLine($"  zero-signal guard : probe {_probeCount} was not reached");
            }
        }
    }

    sealed class FidelityPhaseTimings
    {
        long _collectRenderTicks;
        long _skeletonEmitTicks;
        long _parseTicks;
        long _compilationCreateTicks;
        long _emitTicks;
        long _opcodeCompareTicks;

        public T MeasureCollectRender<T>(Func<T> action) => Measure(ref _collectRenderTicks, action);
        public T MeasureSkeletonEmit<T>(Func<T> action) => Measure(ref _skeletonEmitTicks, action);
        public T MeasureParse<T>(Func<T> action) => Measure(ref _parseTicks, action);
        public T MeasureCompilationCreate<T>(Func<T> action) => Measure(ref _compilationCreateTicks, action);
        public T MeasureEmit<T>(Func<T> action) => Measure(ref _emitTicks, action);
        public T MeasureOpcodeCompare<T>(Func<T> action) => Measure(ref _opcodeCompareTicks, action);

        static T Measure<T>(ref long ticks, Func<T> action)
        {
            long start = Stopwatch.GetTimestamp();
            try { return action(); }
            finally { ticks += Stopwatch.GetTimestamp() - start; }
        }

        public void Report()
        {
            static string Ms(long ticks) => $"{ticks * 1000.0 / Stopwatch.Frequency:F1} ms";

            Console.WriteLine();
            Console.WriteLine("Fidelity phase timings:");
            Console.WriteLine($"  collect/render/original IL : {Ms(_collectRenderTicks)}");
            Console.WriteLine($"  skeleton emit              : {Ms(_skeletonEmitTicks)}");
            Console.WriteLine($"  parse                      : {Ms(_parseTicks)}");
            Console.WriteLine($"  compilation create         : {Ms(_compilationCreateTicks)}");
            Console.WriteLine($"  emit                       : {Ms(_emitTicks)}");
            Console.WriteLine($"  opcode compare             : {Ms(_opcodeCompareTicks)}");
        }
    }

    static string TargetKey(MethodTarget target)
        => $"{target.AssemblyPath}!{target.Type}::{target.Method}#{target.Overload}{target.Signature}";

    internal static IReadOnlyList<CompileBackResult> EvaluateTargets(
        IReadOnlyList<string> assemblies,
        IReadOnlyList<CompileBackTarget> targets,
        bool lowered = false)
        => EvaluateTargets(assemblies, targets, lowered, options: null);

    /// <summary>
    /// As <see cref="EvaluateTargets(IReadOnlyList{string}, IReadOnlyList{CompileBackTarget}, bool)"/>,
    /// but renders the (raised) view with <paramref name="options"/> applied — the
    /// seam the byte-neutrality gate uses to compile-back a single opt-in knob's
    /// output and assert it recompiles to the same IL as the shipped default.
    /// </summary>
    internal static IReadOnlyList<CompileBackResult> EvaluateTargets(
        IReadOnlyList<string> assemblies,
        IReadOnlyList<CompileBackTarget> targets,
        bool lowered,
        PrinterOptions? options,
        bool readSymbols = true)
    {
        var methodTargets = targets
            .Select(target => new MethodTarget(
                Assembly: Path.GetFileNameWithoutExtension(target.AssemblyPath),
                AssemblyPath: PortablePath(target.AssemblyPath),
                Type: target.Type,
                Method: target.Method,
                Overload: target.Overload,
                Signature: target.Signature,
                DisplayMethod: $"{target.Type}::{target.Method}"))
            .ToArray();

        return EvaluateTargets(assemblies, methodTargets, lowered, options, readSymbols)
            .Select(row => row.Result)
            .ToArray();
    }

    static IReadOnlyList<TargetedCompileBackResult> EvaluateTargets(IReadOnlyList<string> assemblies, IReadOnlyList<MethodTarget> targets, bool lowered)
        => EvaluateTargets(assemblies, targets, lowered, options: null);

    internal static async Task<IReadOnlyList<CompileBackResult>> EvaluateChangedMethodTargetsForTesting(
        IReadOnlyList<string> assemblies,
        IReadOnlyList<CompileBackTarget> targets)
    {
        var methodTargets = targets
            .Select(target => new MethodTarget(
                Assembly: Path.GetFileNameWithoutExtension(target.AssemblyPath),
                AssemblyPath: PortablePath(target.AssemblyPath),
                Type: target.Type,
                Method: target.Method,
                Overload: target.Overload,
                Signature: target.Signature,
                DisplayMethod: $"{target.Type}::{target.Method}"))
            .ToArray();
        return (await EvaluateChangedMethodTargets(assemblies, methodTargets))
            .Select(row => row.Result)
            .ToArray();
    }

    static async Task<IReadOnlyList<TargetedCompileBackResult>> EvaluateChangedMethodTargets(
        IReadOnlyList<string> assemblies,
        IReadOnlyList<MethodTarget> targets)
    {
        if (targets.Count == 0)
            return [];

        using var metadata = CorpusMetadata.Create(assemblies);
        var pending = targets.ToDictionary(TargetKey, StringComparer.Ordinal);
        var rows = new List<TargetedCompileBackResult>();
        foreach (var assemblyPath in assemblies)
        {
            var portablePath = PortablePath(assemblyPath);
            var assemblyTargets = pending.Values
                .Where(target => string.Equals(target.AssemblyPath, portablePath, StringComparison.Ordinal))
                .OrderBy(target => target.DisplayMethod, StringComparer.Ordinal)
                .ToArray();
            if (assemblyTargets.Length == 0)
                continue;

            try
            {
                using var source = MetadataSource.Open(assemblyPath, context: metadata);
                RegisterSourceContext(source, metadata);
                var resolved = new List<(MethodTarget Target, CompileBackTarget Request)>();
                foreach (var target in assemblyTargets)
                {
                    if (ResolveChangedMethodAddress(source, target) is not { } address)
                    {
                        rows.Add(TargetUnavailable(target, "target-method-not-found"));
                        pending.Remove(TargetKey(target));
                        continue;
                    }

                    resolved.Add((
                        target,
                        new CompileBackTarget(
                            assemblyPath,
                            target.Type,
                            target.Method,
                            target.Overload,
                            target.Signature,
                            address)));
                }

                if (resolved.Count != 0)
                {
                    var evaluation = await ReturnToSenderFidelityEvaluator.EvaluateAsync(
                        assemblyPath,
                        resolved.Select(item => item.Request).ToArray(),
                        "product-artifact RTS; compile-back-floor=false",
                        CaptureMode.ProductArtifact);
                    if (evaluation.CompileBackFloorAppliedMethods != 0)
                    {
                        throw new InvalidOperationException(
                            $"Raised changed-method RTS applied the compile-back floor to "
                            + $"{evaluation.CompileBackFloorAppliedMethods} methods.");
                    }

                    for (int index = 0; index < resolved.Count; index++)
                    {
                        var item = resolved[index];
                        rows.Add(new TargetedCompileBackResult(item.Target, evaluation.Results[index]));
                        pending.Remove(TargetKey(item.Target));
                    }
                }
            }
            catch (Exception ex) when (
                ex is IOException or BadImageFormatException or InvalidOperationException or UnauthorizedAccessException)
            {
                foreach (var target in assemblyTargets.Where(target => pending.ContainsKey(TargetKey(target))))
                {
                    rows.Add(TargetUnavailable(
                        target,
                        $"return-to-sender-context-unavailable: {ex.Message}"));
                    pending.Remove(TargetKey(target));
                }
            }
        }

        foreach (var target in pending.Values.OrderBy(target => target.DisplayMethod, StringComparer.Ordinal))
            rows.Add(TargetUnavailable(target, "target-method-not-found"));

        var rowsByTarget = rows.ToDictionary(
            row => TargetKey(row.Target),
            StringComparer.Ordinal);
        return targets
            .Select(target => rowsByTarget[TargetKey(target)])
            .ToArray();
    }

    static MetadataMethodAddress? ResolveChangedMethodAddress(
        MetadataSource source,
        MethodTarget target)
    {
        var reader = source.Reader;
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (!string.Equals(reader.GetFullTypeName(typeDef), target.Type, StringComparison.Ordinal))
                continue;

            int overload = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                string methodName = reader.GetString(method.Name);
                if (!string.Equals(methodName, target.Method, StringComparison.Ordinal))
                    continue;

                if (overload++ != target.Overload)
                    continue;
                if (method.RelativeVirtualAddress == 0)
                    return null;

                var candidate = new IrImporter.StableSampleCandidate(
                    target.Type,
                    target.Method,
                    target.Overload,
                    typeHandle,
                    methodHandle);
                string liveSignature = CorpusMethodIdentity.SignatureText(candidate.Build(source).Signature);
                return string.Equals(liveSignature, target.Signature, StringComparison.Ordinal)
                    ? MetadataMethodAddress.Create(reader, methodHandle)
                    : null;
            }

            return null;
        }

        return null;
    }

    static TargetedCompileBackResult TargetUnavailable(MethodTarget target, string detail)
        => new(
            target,
            new CompileBackResult(
                target.Type,
                target.Method,
                target.Overload,
                target.Signature,
                CompileBackStatus.ContextFail,
                "",
                "",
                detail,
                CaptureMode.ProductArtifact,
                "product-artifact RTS; compile-back-floor=false"));

    static IReadOnlyList<TargetedCompileBackResult> EvaluateTargets(
        IReadOnlyList<string> assemblies,
        IReadOnlyList<MethodTarget> targets,
        bool lowered,
        PrinterOptions? options,
        bool readSymbols = true)
    {
        if (targets.Count == 0)
            return [];

        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable);

        using var metadata = CorpusMetadata.Create(assemblies);
        var pending = targets.ToDictionary(TargetKey, StringComparer.Ordinal);
        var rows = new List<TargetedCompileBackResult>();
        foreach (var assemblyPath in assemblies)
        {
            if (pending.Count == 0)
                break;
            PEReader pe;
            try { pe = new PEReader(File.OpenRead(assemblyPath)); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { continue; }
            using (pe)
            {
                if (!pe.HasMetadata)
                    continue;
                var featureOptions = CompilerFeatureOptions.Resolve(pe);
                var portablePath = PortablePath(assemblyPath);
                var reader = pe.GetMetadataReader();
                MetadataSource source;
                try
                {
                    source = readSymbols
                        ? MetadataSource.Open(assemblyPath, context: metadata)
                        : MetadataSource.OpenWithoutSymbols(assemblyPath, context: metadata);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { continue; }
                RegisterSourceContext(source, metadata);
                using (source)
                {
                    var assemblyTargets = pending.Values
                        .Where(target => string.Equals(target.AssemblyPath, portablePath, StringComparison.Ordinal))
                        .GroupBy(target => target.Type, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
                    if (assemblyTargets.Count == 0)
                        continue;

                    var render = Renderer(source, lowered, options);
                    var references = RuntimeReferences(assemblyPath, assemblies);
                    foreach (var typeHandle in reader.TypeDefinitions)
                    {
                        if (pending.Count == 0)
                            break;
                        var rootDef = reader.GetTypeDefinition(typeHandle);
                        if (!rootDef.GetDeclaringType().IsNil)
                            continue; // each top-level root walks its own nested tree once

                        foreach (var (fullType, entries, treeHandle) in EnumerateTypeTree(
                            reader,
                            pe,
                            source,
                            typeHandle,
                            render,
                            assemblyTargets.ContainsKey))
                        {
                            if (pending.Count == 0)
                                break;
                            if (entries.Count == 0 || !assemblyTargets.TryGetValue(fullType, out var typeTargets))
                                continue;

                            var typeTargetMap = typeTargets.ToDictionary(
                                target => $"{target.Method}{target.Signature}",
                                StringComparer.Ordinal);
                            var matched = entries
                                .Where(entry => typeTargetMap.ContainsKey($"{entry.Name}{entry.Signature}"))
                                .ToArray();
                            if (matched.Length == 0)
                                continue;

                            var typeResults = EvaluateGrouped(reader, pe, references, featureOptions, compileOptions, fullType, treeHandle, matched);
                            for (int i = 0; i < matched.Length && i < typeResults.Count; i++)
                            {
                                var entry = matched[i];
                                var target = typeTargetMap[$"{entry.Name}{entry.Signature}"];
                                rows.Add(new TargetedCompileBackResult(target, typeResults[i]));
                                pending.Remove(TargetKey(target));
                            }
                        }
                    }
                }
            }
        }

        foreach (var target in pending.Values.OrderBy(target => target.DisplayMethod, StringComparer.Ordinal))
        {
            rows.Add(new TargetedCompileBackResult(
                target,
                new CompileBackResult(
                    target.Type,
                    target.Method,
                    target.Overload,
                    target.Signature,
                    CompileBackStatus.ContextFail,
                    "",
                    "",
                    "target-method-not-found")));
        }

        return rows;
    }

    static void ReportTargeted(IReadOnlyList<TargetedCompileBackResult> rows, int targetCount, int maxExamples)
    {
        int exact = rows.Count(row => row.Result.Status == CompileBackStatus.Exact);
        int opcodeDiff = rows.Count(row => row.Result.Status == CompileBackStatus.OpcodeDiff);
        int operandDiff = rows.Count(row => row.Result.Status == CompileBackStatus.OperandDiff);
        int fidelityUnavailable = rows.Count(row => row.Result.Status == CompileBackStatus.FidelityUnavailable);
        int notFull = rows.Count(row => row.Result.Status == CompileBackStatus.NotFull);
        int recompileFail = rows.Count(row => row.Result.Status == CompileBackStatus.RecompileFail);
        int contextFail = rows.Count(row => row.Result.Status == CompileBackStatus.ContextFail);

        Console.WriteLine($"CHANGED-METHOD COMPILE-BACK over {targetCount} current changed methods ({rows.Count} attempted)");
        Console.WriteLine();
        Console.WriteLine($"  exact (contract v{CurrentContractVersion}): {exact}");
        Console.WriteLine($"  opcode diff (Full) : {opcodeDiff}");
        Console.WriteLine($"  operand diff (Full): {operandDiff}");
        Console.WriteLine($"  fidelity unavailable: {fidelityUnavailable}");
        Console.WriteLine($"  not Full           : {notFull}");
        Console.WriteLine($"  recompile fail     : {recompileFail}");
        Console.WriteLine($"  context fail       : {contextFail}");
        PrintCaptureBands(rows);
        PrintTargetFailureBuckets(rows, CompileBackStatus.RecompileFail, "  recompile-fail buckets:");
        PrintTargetRecompileCodes(rows);
        PrintTargetFailureBuckets(rows, CompileBackStatus.ContextFail, "  context-fail buckets:");
        PrintTargetExamples(rows, CompileBackStatus.OpcodeDiff, "Opcode-diff examples", maxExamples, includeOpcodes: true);
        PrintTargetExamples(rows, CompileBackStatus.OperandDiff, "Operand-diff examples", maxExamples, includeOpcodes: true);
        PrintTargetExamples(rows, CompileBackStatus.FidelityUnavailable, "Fidelity-unavailable examples", maxExamples, includeOpcodes: false);
        PrintTargetExamples(rows, CompileBackStatus.RecompileFail, "Recompile-fail examples", maxExamples, includeOpcodes: false);
        PrintTargetExamples(rows, CompileBackStatus.ContextFail, "Context-fail examples", maxExamples, includeOpcodes: false);
        PrintTargetExamples(rows, CompileBackStatus.NotFull, "Not-Full examples", maxExamples, includeOpcodes: false);
    }

    /// <summary>
    /// The segmented "safely-capturable" bands (#1412), printed only when a
    /// cluster mode produced provenance. A checkable row (Exact, OpcodeDiff, or OperandDiff)
    /// captured whole-module needs no closure; one captured under the closure is
    /// an unrelated-sibling rescue; a ClusterBailed row failed both the
    /// whole-module attempt and the closure escalation — the principled
    /// not-safely-capturable band, which must not be counted as passing.
    /// </summary>
    static void PrintCaptureBands(IReadOnlyList<TargetedCompileBackResult> rows)
    {
        static bool Checkable(CompileBackStatus s)
            => s is CompileBackStatus.Exact or CompileBackStatus.OpcodeDiff or CompileBackStatus.OperandDiff;
        int wholeModuleCheckable = rows.Count(row => row.Result.Capture == CaptureMode.WholeModule && Checkable(row.Result.Status));
        int clusterRescued = rows.Count(row => row.Result.Capture == CaptureMode.Cluster && Checkable(row.Result.Status));
        int notSafelyCapturable = rows.Count(row => row.Result.Capture == CaptureMode.ClusterBailed && !Checkable(row.Result.Status));
        if (clusterRescued == 0 && notSafelyCapturable == 0)
            return; // whole-module-only run — no cluster provenance to report

        Console.WriteLine("  safely-capturable bands:");
        Console.WriteLine($"    checkable (whole-module)   : {wholeModuleCheckable}");
        Console.WriteLine($"    checkable (cluster-rescued): {clusterRescued}");
        Console.WriteLine($"    not-safely-capturable      : {notSafelyCapturable}");
        foreach (var group in rows
                     .Where(row => row.Result.Capture == CaptureMode.ClusterBailed
                                   && row.Result.CaptureDetail?.Contains("-unextracted[", StringComparison.Ordinal) == true)
                     .GroupBy(row => row.Result.CaptureDetail!, StringComparer.Ordinal)
                     .OrderByDescending(group => group.Count())
                     .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"    {group.Key}: {group.Count()}");
        }
    }

    /// <summary>
    /// The compiler-diagnostic code histogram for the recompile-fail rows — the
    /// `compiler diagnostic` bucket is a catch-all, so this splits it by `CS####`
    /// so the dominant skeleton-emit defect is visible without re-grepping the
    /// examples. The first row of each code names a representative method.
    /// </summary>
    static void PrintTargetRecompileCodes(IReadOnlyList<TargetedCompileBackResult> rows)
    {
        var byCode = rows
            .Where(row => row.Result.Status == CompileBackStatus.RecompileFail)
            .GroupBy(row => DiagnosticCode(row.Result.Detail), StringComparer.Ordinal)
            .Select(group => new
            {
                Code = group.Key,
                Count = group.Count(),
                Example = group.First().Target.DisplayMethod,
            })
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Code, StringComparer.Ordinal)
            .ToArray();
        if (byCode.Length == 0)
            return;

        Console.WriteLine("  recompile-fail by code:");
        foreach (var entry in byCode)
            Console.WriteLine($"    {entry.Code}: {entry.Count} (e.g. {entry.Example})");
    }

    static void PrintTargetFailureBuckets(IReadOnlyList<TargetedCompileBackResult> rows, CompileBackStatus status, string title)
    {
        var buckets = rows
            .Where(row => row.Result.Status == status)
            .GroupBy(row => status == CompileBackStatus.ContextFail
                ? ClassifyContextFailure(row.Result.Detail)
                : ClassifyRecompileFailure(row.Result.Detail), StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Name = group.Key,
                Count = group.Count(),
                Examples = group.Select(row => row.Target.DisplayMethod).Take(3).ToArray(),
            })
            .OrderByDescending(bucket => bucket.Count)
            .ThenBy(bucket => bucket.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (buckets.Length == 0)
            return;

        Console.WriteLine(title);
        foreach (var bucket in buckets)
            Console.WriteLine($"    {bucket.Name}: {bucket.Count} (e.g. {string.Join(", ", bucket.Examples)})");
    }

    static void PrintTargetExamples(
        IReadOnlyList<TargetedCompileBackResult> rows,
        CompileBackStatus status,
        string title,
        int maxExamples,
        bool includeOpcodes)
    {
        var examples = rows.Where(row => row.Result.Status == status).Take(maxExamples).ToArray();
        if (examples.Length == 0)
            return;

        Console.WriteLine();
        Console.WriteLine($"{title}:");
        foreach (var row in examples)
        {
            Console.WriteLine($"  {row.Target.DisplayMethod}");
            if (!string.IsNullOrWhiteSpace(row.Result.Annotated))
            {
                foreach (var annotatedLine in row.Result.Annotated.Split('\n'))
                    Console.WriteLine($"    {annotatedLine}");
            }
            else if (!string.IsNullOrWhiteSpace(row.Result.Detail))
            {
                Console.WriteLine($"    {row.Result.Detail}");
            }
            if (includeOpcodes)
            {
                Console.WriteLine($"    orig : {row.Result.OriginalOpcodes}");
                Console.WriteLine($"    recmp: {row.Result.RecompiledOpcodes}");
            }
        }
    }

    /// <summary>One method ready to compile back: its decompiled body and the original opcode stream to match.</summary>
    sealed record Entry(
        MethodDefinitionHandle Handle, string Name, int Overload, string Signature, TargetBody Target,
        IReadOnlyList<(string Field, string Value)> FieldInits,
        string OrigText, IReadOnlyList<string> OrigOps, bool IsFull,
        string? CompileBackUnavailableReason = null);

    /// <summary>
    /// Stable metadata identity available before a method is imported, rendered,
    /// disassembled, or compiled back. The overload is the method-name ordinal in
    /// metadata order, matching <see cref="CompileBackResult.Overload"/>.
    /// </summary>
    internal readonly record struct EvaluationMethod(string Type, string Method, int Overload);

    /// <summary>
    /// Imports, renders, and disassembles every recompilable method of one type.
    /// Null when the type is not a class/struct we recompile. The render/IL work
    /// is independent of how the methods are later compiled (grouped or per-method).
    /// </summary>
    static (string FullType, List<Entry> Entries)? CollectType(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        Func<IrFunction, DecompilerResult> render, int maxEntries = int.MaxValue,
        Func<string, bool>? typeFilter = null,
        Func<EvaluationMethod, bool>? methodFilter = null)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        if (!typeDef.GetDeclaringType().IsNil)
            return null; // nested types are emitted by their enclosing type
        return CollectTypeEntries(reader, pe, source, typeHandle, typeDef, render, maxEntries, typeFilter, methodFilter);
    }

    /// <summary>
    /// Builds the entry list for one type, keyed by its full name (nested types
    /// thread their declaring types: <c>Outer.Inner</c>, matching how the corpus
    /// snapshot names them). Unlike <see cref="CollectType"/> this does not reject
    /// a nested type, so the targeted delta path can reach a changed method that
    /// lives on a nested type; the corpus sweep keeps rooting at top-level types
    /// through <see cref="CollectType"/>, so non-targeted behavior is unchanged.
    /// </summary>
    static (string FullType, List<Entry> Entries)? CollectTypeEntries(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        TypeDefinition typeDef, Func<IrFunction, DecompilerResult> render, int maxEntries = int.MaxValue,
        Func<string, bool>? typeFilter = null,
        Func<EvaluationMethod, bool>? methodFilter = null)
    {
        if (maxEntries <= 0)
            return null;
        if (ShapeOf(reader, typeDef) is not (TypeKind.Class or TypeKind.Struct))
            return null;

        string fullType = reader.GetFullTypeName(typeDef);

        // Declining here is what makes a single-type caller cheap: everything
        // above is metadata-only, while everything below renders and
        // disassembles every method on the type. A caller that wants one
        // fixture out of a large assembly must not pay for the rest.
        if (typeFilter is not null && !typeFilter(fullType))
            return null;

        if (IsGeneratedType(reader, typeDef, fullType))
            return null;

        var entries = new List<Entry>();
        var overloads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var mh in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(mh);
            string name = reader.GetString(method.Name);
            string key = $"{fullType}::{name}";
            int overload = overloads.GetValueOrDefault(key);
            overloads[key] = overload + 1;
            if (method.RelativeVirtualAddress == 0 || IsGeneratedMethod(reader, method, name))
                continue;
            if (methodFilter is not null && !methodFilter(new EvaluationMethod(fullType, name, overload)))
                continue;

            var function = IrImporter.Import(source, fullType, name, overload);
            if (function is null)
                continue;
            var original = MetadataInstructionProducer.Disassemble(
                pe,
                reader,
                method);
            if (original is null)
                continue;
            var origOps = original.Select(
                i => CanonicalOpcode(i.OpCodeName)).ToList();
            if (function.MemorySafetyMode
                is MemorySafetyModeDecision.Unavailable unavailable)
            {
                entries.Add(new Entry(
                    mh,
                    name,
                    overload,
                    CorpusMethodIdentity.SignatureText(function.Signature),
                    new TargetBody("", null, false),
                    [],
                    string.Join(" ", origOps),
                    origOps,
                    IsFull: false,
                    MemorySafetyModeDecision.DescribeUnavailable(
                        unavailable.Rules)));
                if (entries.Count >= maxEntries)
                    break;
                continue;
            }

            string? body;
            string? chain;
            IReadOnlyList<(string Field, string Value)> fieldInits;
            try { var printed = render(function); body = printed.Output; chain = printed.ConstructorChain; fieldInits = printed.FieldInitializers; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { continue; }
            if (body is null)
                continue;
            var requiredNamespaces = MemberBodyFacts.ReferencedNamespaces(function);
            PrimaryConstructorShape? primaryConstructor = PrimaryConstructorFromPrologue(
                reader, method, MemberBodyFacts.Constructor(function).PrimaryConstructorPrologue, body);
            bool requiresAsync = function.RequiresAsyncMethodContext;
            var wholeMember = TryRenderTargetMember(
                pe,
                source,
                mh,
                targeted: methodFilter is not null,
                isPrimaryConstructor: primaryConstructor is not null);
            entries.Add(new Entry(mh, name, overload, CorpusMethodIdentity.SignatureText(function.Signature), new TargetBody(body, chain, requiresAsync, primaryConstructor, requiredNamespaces, wholeMember?.Text, wholeMember?.Namespaces), fieldInits,
                string.Join(" ", origOps), origOps, function.Fidelity == DecompilationFidelity.Full));
            if (entries.Count >= maxEntries)
                break;
        }
        return (fullType, entries);
    }

    static bool IsGeneratedType(MetadataReader reader, TypeDefinition typeDef, string fullType)
        => fullType.Contains('<')
           || TypeFilters.IsCompilerGeneratedNested(reader.GetString(typeDef.Name))
           || AttributeReader.HasAttribute(reader, typeDef.GetCustomAttributes(), KnownAttributeNames.CompilerGeneratedAttribute)
           || AttributeReader.HasAttribute(reader, typeDef.GetCustomAttributes(), "System.CodeDom.Compiler.GeneratedCodeAttribute")
           || BaseTypeName(reader, typeDef.BaseType) == "System.Text.Json.Serialization.JsonSerializerContext";

    static bool IsGeneratedMethod(
        MetadataReader reader,
        MethodDefinition method,
        string name,
        bool allowEmbeddedAngleBrackets = false)
    {
        bool generatedName = allowEmbeddedAngleBrackets
            ? name.StartsWith('<')
            : name.Contains('<');
        if (generatedName
            || name.StartsWith("__", StringComparison.Ordinal)
            || AttributeReader.HasAttribute(reader, method.GetCustomAttributes(), "System.CodeDom.Compiler.GeneratedCodeAttribute"))
            return true;

        if (!AttributeReader.HasAttribute(reader, method.GetCustomAttributes(), KnownAttributeNames.CompilerGeneratedAttribute))
            return false;

        return !IsCompilerGeneratedAccessor(method, name);
    }

    static bool IsCompilerGeneratedAccessor(MethodDefinition method, string name)
        => (method.Attributes & MethodAttributes.SpecialName) != 0
           && (name.StartsWith("get_", StringComparison.Ordinal)
               || name.StartsWith("set_", StringComparison.Ordinal));

    static PrimaryConstructorShape? PrimaryConstructorFromPrologue(
        MetadataReader reader,
        MethodDefinition method,
        IReadOnlyList<PrimaryConstructorFieldStore>? prologue,
        string renderedBody)
    {
        if (reader.GetString(method.Name) != ".ctor"
            || method.Attributes.HasFlag(MethodAttributes.Static))
            return null;
        var declaringHandle = method.GetDeclaringType();
        var declaringType = reader.GetTypeDefinition(declaringHandle);
        if (CountInstanceConstructors(reader, declaringType) != 1
            || HasInAssemblyDerivedType(reader, declaringHandle))
            return null;
        // The IR-shape detection lives in the product extractor; a null prologue
        // means the body is not primary-constructor shaped.
        if (prologue is null)
            return null;

        var parameterNames = ParameterNames(reader, method);
        if (parameterNames.Count == 0)
            return null;

        var initializers = new List<(string Field, string Value)>();
        foreach (var fieldStore in prologue)
        {
            if (!parameterNames.TryGetValue(fieldStore.SourceArgumentIndex - 1, out string? parameterName))
                return null;

            string name = AutoPropertyNameForBackingField(reader, declaringType, fieldStore.FieldName)
                ?? fieldStore.FieldName;
            initializers.Add((name, parameterName));
        }

        if (initializers.Count == 0)
            return null;
        if (!RenderedBodyMatchesPrimaryConstructorInitializers(renderedBody, initializers))
            return null;

        string parameters = Parameters(reader, method, method.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForMethod(reader, declaringType, method)));
        return new PrimaryConstructorShape(parameters, initializers);
    }

    static int CountInstanceConstructors(MetadataReader reader, TypeDefinition typeDef)
    {
        int count = 0;
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) == ".ctor"
                && !method.Attributes.HasFlag(MethodAttributes.Static))
                count++;
        }
        return count;
    }

    static bool HasInAssemblyDerivedType(MetadataReader reader, TypeDefinitionHandle baseHandle)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            if (typeHandle == baseHandle)
                continue;
            var type = reader.GetTypeDefinition(typeHandle);
            if (type.BaseType.Kind == HandleKind.TypeDefinition
                && (TypeDefinitionHandle)type.BaseType == baseHandle)
                return true;
        }
        return false;
    }

    static bool RenderedBodyMatchesPrimaryConstructorInitializers(
        string renderedBody,
        IReadOnlyList<(string Field, string Value)> initializers)
    {
        var lines = renderedBody
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length != initializers.Count)
            return false;

        for (int i = 0; i < initializers.Count; i++)
        {
            var (field, value) = initializers[i];
            string fieldName = Identifier(field);
            string expectedBare = $"{fieldName} = {value};";
            string expectedThis = $"this.{fieldName} = {value};";
            if (lines[i] != expectedBare && lines[i] != expectedThis)
                return false;
        }
        return true;
    }

    static Dictionary<int, string> ParameterNames(MetadataReader reader, MethodDefinition method)
    {
        var names = new Dictionary<int, string>();
        foreach (var parameterHandle in method.GetParameters())
        {
            var parameter = reader.GetParameter(parameterHandle);
            if (parameter.SequenceNumber >= 1)
                names[parameter.SequenceNumber - 1] = Identifier(reader.GetString(parameter.Name));
        }
        return names;
    }

    /// <summary>
    /// Yields the entry list for a top-level type and every nested type beneath
    /// it, each under its own full name (and the handle to root its skeleton
    /// field-initializers). Used only by the targeted delta path so a changed
    /// method on a nested type can be found and attempted instead of falling into
    /// the <c>target-method-not-found</c> bucket.
    /// </summary>
    static IEnumerable<(string FullType, List<Entry> Entries, TypeDefinitionHandle Handle)> EnumerateTypeTree(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        Func<IrFunction, DecompilerResult> render, Func<string, bool>? typeFilter = null)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        if (CollectTypeEntries(reader, pe, source, typeHandle, typeDef, render, typeFilter: typeFilter) is { } collected)
            yield return (collected.FullType, collected.Entries, typeHandle);
        foreach (var nested in typeDef.GetNestedTypes())
            foreach (var result in EnumerateTypeTree(reader, pe, source, nested, render, typeFilter))
                yield return result;
    }

    static void EvaluateType(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        ReferenceSet references, CompilerFeatureOptions.Resolution featureOptions,
        CSharpCompilationOptions compileOptions, Func<IrFunction, DecompilerResult> render, List<CompileBackResult> results,
        int maxEntries = int.MaxValue, ClusterMode clusterMode = ClusterMode.Off,
        Func<string, bool>? typeFilter = null,
        Func<EvaluationMethod, bool>? methodFilter = null)
    {
        if (maxEntries <= 0)
            return;
        if (CollectType(reader, pe, source, typeHandle, render, maxEntries, typeFilter, methodFilter) is not var (fullType, entries) || entries.Count == 0)
            return;
        results.AddRange(EvaluateGrouped(reader, pe, references, featureOptions, compileOptions, fullType, typeHandle, entries, clusterMode));
    }

    /// <summary>
    /// Compiles a type's decompiled bodies together and compares each method's
    /// recompiled opcodes against its original — the speed win, since a sibling
    /// body never changes a method's emitted IL, so a clean type of N methods
    /// costs one compilation instead of N. A non-recompilable body poisons the
    /// whole compilation, so on failure each method falls back to its own
    /// single-method build — correct either way, but the grouping only pays off
    /// for a type whose every body recompiles (a curated fixture holder).
    /// </summary>
    static List<CompileBackResult> EvaluateGrouped(
        MetadataReader reader, PEReader pe, ReferenceSet references,
        CompilerFeatureOptions.Resolution featureOptions,
        CSharpCompilationOptions compileOptions,
        string fullType, TypeDefinitionHandle typeHandle, IReadOnlyList<Entry> entries,
        ClusterMode clusterMode = ClusterMode.Off, FidelityPhaseTimings? timings = null)
    {
        string? unavailableReason = featureOptions
            is CompilerFeatureOptions.Resolution.Unavailable unavailable
                ? unavailable.Reason
                : entries
                    .Select(entry => entry.CompileBackUnavailableReason)
                    .FirstOrDefault(reason => reason is not null);
        if (unavailableReason is not null)
        {
            return entries.Select(entry => new CompileBackResult(
                fullType,
                entry.Name,
                entry.Overload,
                entry.Signature,
                CompileBackStatus.FidelityUnavailable,
                entry.OrigText,
                "",
                "memory-safety-mode-unavailable: " + unavailableReason))
                .ToList();
        }

        var parseOptions =
            ((CompilerFeatureOptions.Resolution.Available)featureOptions)
            .Options;

        // CB_CLUSTER selects the operational escalation path for the console runs.
        if (clusterMode == ClusterMode.Off && Environment.GetEnvironmentVariable("CB_CLUSTER") is not null)
            clusterMode = ClusterMode.Escalate;

        // ForceAll: attempt the reconstruction closure for every row first — the
        // maximal exercise of the closure engine (the test seam, #1412). A row the
        // closure binds carries Cluster provenance; a bail falls back to the
        // whole-module per-method build (ClusterBailed), so the result is never
        // worse than whole-module.
        if (clusterMode == ClusterMode.ForceAll)
        {
            var (typeIndex, methodIndex, namespaceIndex, receiverTypes) = ClusterIndexes(reader);
            var forced = new List<CompileBackResult>(entries.Count);
            foreach (var e in entries)
            {
                var captured = CompileOneClustered(reader, pe, references, parseOptions, compileOptions, fullType, e, typeIndex, methodIndex, namespaceIndex, receiverTypes, timings, out var captureDetail);
                forced.Add(captured is not null
                    ? captured with { Capture = CaptureMode.Cluster, CaptureDetail = captureDetail }
                    : CompileOne(reader, pe, references, parseOptions, compileOptions, fullType, e, timings) with
                    {
                        Capture = CaptureMode.ClusterBailed,
                        CaptureDetail = captureDetail,
                    });
            }
            return forced;
        }

        // Whole-module reconstruction — the cheap, grouped common case. CB_NOGROUP
        // forces the per-method path (the A/B baseline for the speedup).
        var results = new List<CompileBackResult>();
        bool grouped = entries.Count > 1 && Environment.GetEnvironmentVariable("CB_NOGROUP") is null;
        if (!(grouped && TryCompileGroup(reader, pe, references, parseOptions, compileOptions, fullType, typeHandle, entries, results, timings)))
        {
            results.Clear();
            foreach (var e in entries)
                results.Add(CompileOne(reader, pe, references, parseOptions, compileOptions, fullType, e, timings));
        }

        // Escalation: reconstruct only the rows whole-module could not check —
        // every reconstructed type is the target's own transitive closure, so an
        // unrelated sibling's gap cannot poison an otherwise-faithful target. The
        // closure's whole-module fallback guarantees the escalated result is never
        // worse than the whole-module one, so the capture never regresses (#1412).
        // A row that fails both is ClusterBailed — the not-safely-capturable band.
        if (clusterMode == ClusterMode.Escalate)
        {
            var (typeIndex, methodIndex, namespaceIndex, receiverTypes) = ClusterIndexes(reader);
            for (int i = 0; i < results.Count && i < entries.Count; i++)
            {
                if (results[i].Status is not (CompileBackStatus.RecompileFail or CompileBackStatus.ContextFail))
                    continue;
                var captured = CompileOneClustered(reader, pe, references, parseOptions, compileOptions, fullType, entries[i], typeIndex, methodIndex, namespaceIndex, receiverTypes, timings, out var captureDetail);
                results[i] = captured is not null
                    ? captured with { Capture = CaptureMode.Cluster, CaptureDetail = captureDetail }
                    : results[i] with
                    {
                        Capture = CaptureMode.ClusterBailed,
                        CaptureDetail = captureDetail,
                    };
            }
        }
        return results;
    }

    /// <summary>Builds and compiles one grouped unit; on success appends a classified result per method and returns true.</summary>
    static bool TryCompileGroup(
        MetadataReader reader, PEReader pe, ReferenceSet references,
        CSharpParseOptions parseOptions, CSharpCompilationOptions compileOptions,
        string fullType, TypeDefinitionHandle typeHandle, IReadOnlyList<Entry> entries,
        List<CompileBackResult> results, FidelityPhaseTimings? timings)
    {
        var targets = new Dictionary<MethodDefinitionHandle, TargetBody>();
        foreach (var e in entries)
            targets[e.Handle] = e.Target;
        // Field initializers are identical across a type's constructors (C# declares
        // them once at the field), so any ctor entry's lifted inits serve the group.
        var fieldInits = entries.FirstOrDefault(e => e.Name is ".ctor" or ".cctor")?.FieldInits ?? [];

        BuiltUnit built;
        try { built = timings is null
            ? BuildUnit(reader, targets, fieldInits, typeHandle, references.Accessibility)
            : timings.MeasureSkeletonEmit(() => BuildUnit(reader, targets, fieldInits, typeHandle, references.Accessibility)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return false; }
        string unit = built.Source;

        var tree = timings is null
            ? CSharpSyntaxTree.ParseText(unit, parseOptions)
            : timings.MeasureParse(() => CSharpSyntaxTree.ParseText(unit, parseOptions));
        var comp = timings is null
            ? CSharpCompilation.Create("cb", [tree], references.Metadata, compileOptions)
            : timings.MeasureCompilationCreate(() => CSharpCompilation.Create("cb", [tree], references.Metadata, compileOptions));
        using var ms = new MemoryStream();
        var emit = timings is null
            ? EmitWithTransientEmptyOutputRetry(comp, ms)
            : timings.MeasureEmit(() => EmitWithTransientEmptyOutputRetry(comp, ms));
        if (!emit.Success)
            return false;

        ms.Position = 0;
        using var rpe = new PEReader(ms);
        var disassembled = timings is null
            ? DisassembleAndClassifyGroup(pe, reader, rpe, fullType, entries, built.ProductWholeMembers)
            : timings.MeasureOpcodeCompare(() => DisassembleAndClassifyGroup(pe, reader, rpe, fullType, entries, built.ProductWholeMembers));
        if (disassembled is null)
            return false;   // a method that compiled but cannot be found — fall to isolation
        results.AddRange(disassembled);
        return true;
    }

    static List<CompileBackResult>? DisassembleAndClassifyGroup(
        PEReader originalPe,
        MetadataReader originalReader,
        PEReader recompiledPe,
        string fullType,
        IReadOnlyList<Entry> entries,
        IReadOnlySet<MethodDefinitionHandle> productWholeMembers)
    {
        var disassembled = new List<CompileBackResult>(entries.Count);
        foreach (var e in entries)
        {
            var rOps = FindAndDisassemble(recompiledPe, fullType, e.Name, e.Overload)
                ?.Select(i => CanonicalOpcode(i.OpCodeName)).ToList();
            if (rOps is null)
                return null;
            var fidelityDiff = CompareCompileBackFidelity(
                originalPe,
                originalReader,
                e.Handle,
                recompiledPe,
                fullType,
                e.Name,
                e.Overload);
            disassembled.Add(Classify(
                fullType,
                e,
                rOps,
                fidelityDiff,
                productWholeMembers.Contains(e.Handle)));
        }
        return disassembled;
    }

    /// <summary>The per-method fallback: build a single-target unit and classify it. Authoritative when the grouped build fails.</summary>
    static CompileBackResult CompileOne(
        MetadataReader reader, PEReader pe, ReferenceSet references,
        CSharpParseOptions parseOptions, CSharpCompilationOptions compileOptions,
        string fullType, Entry e, FidelityPhaseTimings? timings)
    {
        BuiltUnit built;
        try { built = timings is null
            ? BuildUnit(reader, e.Handle, e.Target, e.FieldInits, references.Accessibility)
            : timings.MeasureSkeletonEmit(() => BuildUnit(reader, e.Handle, e.Target, e.FieldInits, references.Accessibility)); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(fullType, e.Name, e.Overload, e.Signature, CompileBackStatus.ContextFail, e.OrigText, "", "skeleton-emit");
        }
        string unit = built.Source;
        bool usedProductWholeMember = built.ProductWholeMembers.Contains(e.Handle);

        var tree = timings is null
            ? CSharpSyntaxTree.ParseText(unit, parseOptions)
            : timings.MeasureParse(() => CSharpSyntaxTree.ParseText(unit, parseOptions));
        var comp = timings is null
            ? CSharpCompilation.Create("cb", [tree], references.Metadata, compileOptions)
            : timings.MeasureCompilationCreate(() => CSharpCompilation.Create("cb", [tree], references.Metadata, compileOptions));
        using var ms = new MemoryStream();
        var emit = timings is null
            ? EmitWithTransientEmptyOutputRetry(comp, ms)
            : timings.MeasureEmit(() => EmitWithTransientEmptyOutputRetry(comp, ms));
        if (!emit.Success)
        {
            var err = emit.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            if (Environment.GetEnvironmentVariable("CB_DUMP") is not null && err is not null)
            {
                var safe = string.Concat($"{fullType}.{e.Name}".Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
                var path = Path.Combine(Path.GetTempPath(), $"cb-{safe}.cs");
                File.WriteAllText(path, unit);
                Console.Error.WriteLine($"{path}: {err}");
            }
            return new(
                fullType,
                e.Name,
                e.Overload,
                e.Signature,
                CompileBackStatus.RecompileFail,
                e.OrigText,
                "",
                FormatDiagnostic(err),
                Annotated: RenderAnnotatedFailure(unit, err),
                UsedProductWholeMember: usedProductWholeMember);
        }
        ms.Position = 0;
        using var rpe = new PEReader(ms);
        var rOps = timings is null
            ? FindAndDisassemble(rpe, fullType, e.Name, e.Overload)?.Select(i => CanonicalOpcode(i.OpCodeName)).ToList()
            : timings.MeasureOpcodeCompare(() => FindAndDisassemble(rpe, fullType, e.Name, e.Overload)?.Select(i => CanonicalOpcode(i.OpCodeName)).ToList());
        return rOps is null
            ? new(
                fullType,
                e.Name,
                e.Overload,
                e.Signature,
                CompileBackStatus.ContextFail,
                e.OrigText,
                "",
                "method-not-found",
                UsedProductWholeMember: usedProductWholeMember)
            : Classify(
                fullType,
                e,
                rOps,
                CompareCompileBackFidelity(pe, reader, e.Handle, rpe, fullType, e.Name, e.Overload),
                usedProductWholeMember);
    }

    // ---- Reconstruction-closure (cluster) capture (#1412, opt-in via CB_CLUSTER) ----

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MetadataReader, ClusterIndex> s_clusterIndexCache = new();

    sealed record ExtensionMethodRoot(
        TypeDefinitionHandle Root,
        string? ReceiverType,
        bool UniversalReceiver);

    readonly record struct ExtensionRootSelection(
        IReadOnlyList<TypeDefinitionHandle> Roots,
        bool UsedFallback,
        string? FallbackReason);

    sealed record ClusterIndex(
        Dictionary<string, List<TypeDefinitionHandle>> Types,
        Dictionary<string, List<ExtensionMethodRoot>> Methods,
        Dictionary<string, List<TypeDefinitionHandle>> Namespaces,
        Dictionary<string, List<TypeDefinitionHandle>> ReceiverTypes);

    /// <summary>
    /// Name -> top-level-root maps for the compile-driven cluster: type leaf
    /// names (to resolve a missing type), method names (to resolve a missing
    /// extension method to the static class that declares it), and namespace names
    /// (to resolve a missing namespace segment — a `CS0234` whose body reference
    /// lives in a sub-namespace the leaf-name index cannot name), plus full type
    /// names for receiver hierarchy traversal. Cached per reader.
    /// </summary>
    static (
        Dictionary<string, List<TypeDefinitionHandle>> Types,
        Dictionary<string, List<ExtensionMethodRoot>> Methods,
        Dictionary<string, List<TypeDefinitionHandle>> Namespaces,
        Dictionary<string, List<TypeDefinitionHandle>> ReceiverTypes) ClusterIndexes(MetadataReader reader)
    {
        if (s_clusterIndexCache.TryGetValue(reader, out var cached))
            return (cached.Types, cached.Methods, cached.Namespaces, cached.ReceiverTypes);

        var types = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        var methods = new Dictionary<string, List<ExtensionMethodRoot>>(StringComparer.Ordinal);
        var namespaces = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        var receiverTypes = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        static void Add(Dictionary<string, List<TypeDefinitionHandle>> index, string key, TypeDefinitionHandle root)
        {
            if (key.Length == 0)
                return;
            if (!index.TryGetValue(key, out var list))
                index[key] = list = [];
            if (!list.Contains(root))
                list.Add(root);
        }

        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var root = TopLevelRootOf(reader, handle);
            Add(types, NormalizeTypeName(reader.GetString(typeDef.Name)), root);
            Add(receiverTypes, NormalizeReceiverTypeName(reader.GetFullTypeName(typeDef)), handle);
            // A top-level type's namespace maps to its own root, so a missing
            // namespace segment can pull in every root declared directly in it.
            if (typeDef.GetDeclaringType().IsNil)
                Add(namespaces, reader.GetString(typeDef.Namespace), handle);
            // A method-level ExtensionAttribute is the precise signal; ordinary
            // same-name static methods cannot satisfy CS1061.
            if ((typeDef.Attributes & (TypeAttributes.Abstract | TypeAttributes.Sealed)) == (TypeAttributes.Abstract | TypeAttributes.Sealed))
            {
                foreach (var mh in typeDef.GetMethods())
                {
                    var method = reader.GetMethodDefinition(mh);
                    if (!AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes()))
                        continue;

                    string methodName = reader.GetString(method.Name);
                    if (!methods.TryGetValue(methodName, out var candidates))
                        methods[methodName] = candidates = [];
                    candidates.Add(CreateExtensionMethodRoot(reader, root, typeDef, method));
                }
            }
        }

        s_clusterIndexCache.Add(reader, new ClusterIndex(types, methods, namespaces, receiverTypes));
        return (types, methods, namespaces, receiverTypes);
    }

    static ExtensionMethodRoot CreateExtensionMethodRoot(
        MetadataReader reader,
        TypeDefinitionHandle root,
        TypeDefinition declaringType,
        MethodDefinition method)
    {
        try
        {
            var context = GenericContext.ForMethod(reader, declaringType, method);
            var signature = method.DecodeSignature(SignatureDecoder.Instance, context);
            if (signature.ParameterTypes.Length == 0)
                return new ExtensionMethodRoot(root, null, false);

            string receiver = signature.ParameterTypes[0];
            bool universal = context.MethodParameters.Contains(receiver, StringComparer.Ordinal);
            return new ExtensionMethodRoot(
                root,
                universal ? null : NormalizeReceiverTypeName(receiver),
                universal);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return new ExtensionMethodRoot(root, null, false);
        }
    }

    static ExtensionRootSelection SelectExtensionRoots(
        MetadataReader reader,
        IReadOnlyDictionary<string, List<ExtensionMethodRoot>> methodIndex,
        IReadOnlyDictionary<string, List<TypeDefinitionHandle>> receiverTypes,
        string methodName,
        string? receiverType,
        IReadOnlyList<string>? compatibleReceiverTypes,
        bool compatibleReceiverTypesComplete)
    {
        if (!methodIndex.TryGetValue(methodName, out var candidates))
            return new ExtensionRootSelection([], false, null);

        string? receiver = receiverType is null ? null : NormalizeReceiverTypeName(receiverType);
        HashSet<string>? semanticClosure = compatibleReceiverTypes?
            .Select(NormalizeReceiverTypeName)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string>? receiverClosure = semanticClosure is { Count: > 0 }
            ? semanticClosure
            : null;
        bool complete = receiverClosure is not null && compatibleReceiverTypesComplete;
        bool metadataClosureAvailable = false;
        if (!complete && receiver is not null)
        {
            var metadataClosure = BuildReceiverClosure(
                reader,
                receiverTypes,
                receiver,
                out bool metadataComplete);
            if (metadataClosure is not null)
            {
                metadataClosureAvailable = true;
                receiverClosure ??= [];
                receiverClosure.UnionWith(metadataClosure);
                complete = metadataComplete;
            }
        }
        var compatible = new List<TypeDefinitionHandle>();
        var unknown = new List<TypeDefinitionHandle>();

        foreach (var candidate in candidates)
        {
            if (candidate.UniversalReceiver
                || (candidate.ReceiverType is not null
                    && candidate.ReceiverType == receiver)
                || (candidate.ReceiverType is not null
                    && receiverClosure?.Contains(candidate.ReceiverType) == true))
            {
                AddDistinct(compatible, candidate.Root);
                continue;
            }

            if (receiverClosure is null
                || candidate.ReceiverType is null
                || (!complete
                    && (!metadataClosureAvailable
                        || !receiverTypes.ContainsKey(candidate.ReceiverType))))
            {
                AddDistinct(unknown, candidate.Root);
            }
        }

        if (unknown.Count == 0)
            return new ExtensionRootSelection(compatible, false, null);

        string reason = receiver is null
            ? "receiver type unavailable"
            : receiverClosure is null
                ? $"receiver metadata unavailable for {receiver}"
                : !complete
                    ? $"receiver hierarchy incomplete for {receiver}"
                    : $"extension receiver metadata unavailable for {methodName}";
        foreach (var root in unknown)
            AddDistinct(compatible, root);
        return new ExtensionRootSelection(compatible, true, reason);
    }

    static HashSet<string>? BuildReceiverClosure(
        MetadataReader reader,
        IReadOnlyDictionary<string, List<TypeDefinitionHandle>> receiverTypes,
        string receiver,
        out bool complete)
    {
        complete = false;
        if (!receiverTypes.TryGetValue(receiver, out var receiverHandles))
            return null;
        if (receiverHandles.Count != 1)
        {
            // Keep unexpected receiver-key collisions incomplete so extension roots
            // flow through visible fallback instead of first-wins exclusion.
            complete = false;
            return new HashSet<string>(StringComparer.Ordinal) { receiver };
        }

        bool hierarchyComplete = true;
        var closure = new HashSet<string>(StringComparer.Ordinal) { receiver };
        var pending = new Queue<TypeDefinitionHandle>();
        var visited = new HashSet<TypeDefinitionHandle>();
        pending.Enqueue(receiverHandles[0]);
        while (pending.TryDequeue(out var handle))
        {
            if (!visited.Add(handle))
                continue;

            var typeDef = reader.GetTypeDefinition(handle);
            if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
                closure.Add("System.Object");
            AddBase(typeDef.BaseType, GenericContext.ForType(reader, typeDef));
            foreach (var interfaceHandle in typeDef.GetInterfaceImplementations())
            {
                var implementation = reader.GetInterfaceImplementation(interfaceHandle);
                AddBase(implementation.Interface, GenericContext.ForType(reader, typeDef));
            }
        }

        complete = hierarchyComplete;
        return closure;

        void AddBase(EntityHandle handle, GenericContext context)
        {
            if (handle.IsNil)
                return;

            string? name;
            try
            {
                name = handle.Kind switch
                {
                    HandleKind.TypeDefinition => reader.GetFullTypeName(
                        reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
                    HandleKind.TypeReference => reader.GetFullTypeName(
                        reader.GetTypeReference((TypeReferenceHandle)handle)),
                    HandleKind.TypeSpecification => reader.GetTypeSpecification(
                        (TypeSpecificationHandle)handle).DecodeSignature(SignatureDecoder.Instance, context),
                    _ => null,
                };
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                name = null;
            }

            if (name is null)
            {
                hierarchyComplete = false;
                return;
            }

            string normalized = NormalizeReceiverTypeName(name);
            closure.Add(normalized);
            if (receiverTypes.TryGetValue(normalized, out var localHandles) && localHandles.Count == 1)
                pending.Enqueue(localHandles[0]);
            else if (normalized != "System.Object")
                hierarchyComplete = false;
        }
    }

    static void AddDistinct(List<TypeDefinitionHandle> roots, TypeDefinitionHandle root)
    {
        if (!roots.Contains(root))
            roots.Add(root);
    }

    internal static (IReadOnlyList<string> Roots, bool UsedFallback, string? FallbackReason)
        SelectExtensionRootsForTest(
            string assemblyPath,
            string methodName,
            string? receiverType,
            IReadOnlyList<string>? compatibleReceiverTypes = null,
            bool compatibleReceiverTypesComplete = false)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var (_, methodIndex, _, receiverTypes) = ClusterIndexes(reader);
        var selection = SelectExtensionRoots(
            reader,
            methodIndex,
            receiverTypes,
            methodName,
            receiverType,
            compatibleReceiverTypes,
            compatibleReceiverTypesComplete);
        return (
            selection.Roots
                .Select(handle => reader.GetFullTypeName(reader.GetTypeDefinition(handle)))
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray(),
            selection.UsedFallback,
            selection.FallbackReason);
    }

    /// <summary>
    /// Compile-driven reconstruction-closure capture: reconstruct only the target's
    /// own type, compile, and for every "type not found" the compiler reports that
    /// resolves to a target-assembly type, add that type's top-level root and
    /// recompile — letting the compiler compute the exact closure. A type the
    /// target never references (the unrelated-sibling poison behind the #1318
    /// plateau) is never named, so it is never pulled in. Bails when the closure
    /// stops growing (a genuine target-body/hierarchy gap) or exceeds a budget (an
    /// unsafe, unbounded closure).
    /// </summary>
    static CompileBackResult? CompileOneClustered(
        MetadataReader reader, PEReader pe, ReferenceSet references,
        CSharpParseOptions parseOptions, CSharpCompilationOptions compileOptions,
        string fullType, Entry e,
        IReadOnlyDictionary<string, List<TypeDefinitionHandle>> nameIndex,
        IReadOnlyDictionary<string, List<ExtensionMethodRoot>> methodIndex,
        IReadOnlyDictionary<string, List<TypeDefinitionHandle>> namespaceIndex,
        IReadOnlyDictionary<string, List<TypeDefinitionHandle>> receiverTypes,
        FidelityPhaseTimings? timings,
        out string? captureDetail)
    {
        captureDetail = null;
        const int maxRoots = 200;
        const int maxIterations = 80;
        var include = new HashSet<TypeDefinitionHandle>
        {
            TopLevelRootOf(reader, reader.GetMethodDefinition(e.Handle).GetDeclaringType()),
        };
        var extensionFallbacks = new HashSet<string>(StringComparer.Ordinal);
        Diagnostic? firstError = null;
        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            BuiltUnit built;
            try { built = timings is null
                ? BuildUnit(reader, e.Handle, e.Target, e.FieldInits, references.Accessibility, include)
                : timings.MeasureSkeletonEmit(() => BuildUnit(reader, e.Handle, e.Target, e.FieldInits, references.Accessibility, include)); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                captureDetail = "cluster-source-build-failed";
                return null; // fall back to the whole-module build
            }
            string unit = built.Source;
            bool usedProductWholeMember = built.ProductWholeMembers.Contains(e.Handle);

            var tree = timings is null
                ? CSharpSyntaxTree.ParseText(unit, parseOptions)
                : timings.MeasureParse(() => CSharpSyntaxTree.ParseText(unit, parseOptions));
            var comp = timings is null
                ? CSharpCompilation.Create("cb", [tree], references.Metadata, compileOptions)
                : timings.MeasureCompilationCreate(() => CSharpCompilation.Create("cb", [tree], references.Metadata, compileOptions));
            using var ms = new MemoryStream();
            var emit = timings is null
                ? EmitWithTransientEmptyOutputRetry(comp, ms)
                : timings.MeasureEmit(() => EmitWithTransientEmptyOutputRetry(comp, ms));
            if (emit.Success)
            {
                ms.Position = 0;
                using var rpe = new PEReader(ms);
                var rOps = timings is null
                    ? FindAndDisassemble(rpe, fullType, e.Name, e.Overload)?.Select(i => CanonicalOpcode(i.OpCodeName)).ToList()
                    : timings.MeasureOpcodeCompare(() => FindAndDisassemble(rpe, fullType, e.Name, e.Overload)?.Select(i => CanonicalOpcode(i.OpCodeName)).ToList());
                if (rOps is null)
                {
                    captureDetail = "cluster-method-not-found";
                    return null;
                }
                captureDetail = extensionFallbacks.Count == 0
                    ? null
                    : string.Join("; ", extensionFallbacks.OrderBy(static value => value, StringComparer.Ordinal));
                return Classify(
                    fullType,
                    e,
                    rOps,
                    CompareCompileBackFidelity(pe, reader, e.Handle, rpe, fullType, e.Name, e.Overload),
                    usedProductWholeMember);
            }

            var errors = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            firstError = errors.FirstOrDefault();
            bool grew = false;
            var unextractedDiagnosticIds = new HashSet<string>(StringComparer.Ordinal);
            var semanticModel = comp.GetSemanticModel(tree);
            foreach (var diagnostic in errors)
            {
                var reference = ClosureDiagnosticEvidence.Extract(diagnostic, semanticModel);
                if (reference is null)
                {
                    if (ClosureDiagnosticEvidence.Supports(diagnostic.Id))
                        unextractedDiagnosticIds.Add(diagnostic.Id);
                    continue;
                }

                // CS0246/CS0234: a missing type; CS0103: a missing name used as an
                // expression (a static helper class referenced by name). Both name
                // a target-assembly type the closure must add.
                if (diagnostic.Id is "CS0246" or "CS0234" or "CS0103" or "CS0122")
                {
                    string typeName = reference.ContainingType ?? reference.Name;
                    if (nameIndex.TryGetValue(NormalizeTypeName(typeName), out var roots))
                        foreach (var root in roots)
                            grew |= include.Add(root);
                    // CS0234 names a missing namespace SEGMENT ("'Serialization'
                    // does not exist in the namespace 'Newtonsoft.Json'") rather
                    // than a leaf type, so the leaf-name index cannot resolve it.
                    // Reconstruct the full namespace from syntax location evidence
                    // (parent + '.' + child) and pull in the roots declared
                    // directly in it; the compile-driven loop walks any deeper
                    // segment on the next iteration.
                    if (diagnostic.Id is "CS0234"
                        && reference.ContainingNamespace is { Length: > 0 } containingNamespace
                        && namespaceIndex.TryGetValue($"{containingNamespace}.{reference.Name}", out var nsRoots))
                        foreach (var root in nsRoots)
                            grew |= include.Add(root);
                }
                // CS1061: a missing member — most often an extension method whose
                // static declaring class is not yet in the closure. Add only
                // actual extension roots with a receiver-compatible shape.
                else if (diagnostic.Id is "CS1061")
                {
                    var selection = SelectExtensionRoots(
                        reader,
                        methodIndex,
                        receiverTypes,
                        NormalizeTypeName(reference.Name),
                        reference.ContainingType,
                        reference.CompatibleReceiverTypes,
                        reference.CompatibleReceiverTypesComplete);
                    foreach (var root in selection.Roots)
                        grew |= include.Add(root);
                    if (selection.UsedFallback)
                    {
                        extensionFallbacks.Add(
                            $"extension receiver fallback for {reference.Name}: {selection.FallbackReason}");
                    }
                }
                else if (diagnostic.Id is "CS0117"
                         && reference.ContainingType is { } containingType
                         && nameIndex.TryGetValue(NormalizeTypeName(containingType), out var roots))
                {
                    foreach (var root in roots)
                        grew |= include.Add(root);
                }
            }
            if (!grew || include.Count > maxRoots)
            {
                captureDetail = include.Count > maxRoots
                    ? "closure-root-budget"
                    : ClosureDiagnosticEvidence.FailureReason(
                        "closure-stalled",
                        unextractedDiagnosticIds);
                if (Environment.GetEnvironmentVariable("CB_CLUSTER_DUMP") is not null)
                    Console.Error.WriteLine($"BAIL {fullType}.{e.Name} roots={include.Count} grew={grew} reason={captureDetail}: {FormatDiagnostic(firstError)}");
                return null; // closure stopped growing or got too large — fall back
            }
        }
        captureDetail = "closure-iteration-budget";
        return null; // fall back to the whole-module build
    }

    internal readonly record struct EmitAttempt(bool Success, long OutputLength, ImmutableArray<Diagnostic> Diagnostics);

    internal static int TransientEmptyEmitAttemptCount(Func<int, EmitAttempt> emitAttempt)
    {
        for (int attempt = 1; ; attempt++)
        {
            var result = emitAttempt(attempt);
            if (result.Success || !IsTransientEmptyEmitFailure(result) || attempt >= MaxTransientEmptyEmitAttempts)
                return attempt;
        }
    }

    static EmitResult EmitWithTransientEmptyOutputRetry(CSharpCompilation compilation, MemoryStream output)
    {
        EmitResult? result = null;
        TransientEmptyEmitAttemptCount(_ =>
        {
            output.SetLength(0);
            output.Position = 0;
            result = compilation.Emit(output);
            return new EmitAttempt(result.Success, output.Length, result.Diagnostics);
        });
        return result ?? throw new InvalidOperationException("Compilation emit did not run.");
    }

    // Retry only the observed host/resource flake signature: Roslyn reports failure before
    // writing any PE bytes and without a compiler error. Real source/skeleton regressions
    // carry CS diagnostics and must fail on the first attempt.
    static bool IsTransientEmptyEmitFailure(EmitAttempt result)
        => !result.Success
           && result.OutputLength == 0
           && !result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    /// <summary>The top-level type a (possibly nested) type definition belongs to.</summary>
    static TypeDefinitionHandle TopLevelRootOf(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var declaring = reader.GetTypeDefinition(handle).GetDeclaringType();
        return declaring.IsNil ? handle : TopLevelRootOf(reader, declaring);
    }

    /// <summary>A type name with its trailing generic arguments and arity stripped to the leaf simple name.</summary>
    static string NormalizeTypeName(string name)
        => ClosureDiagnosticEvidence.NormalizeTypeName(name);

    static string NormalizeReceiverTypeName(string name)
    {
        string value = name.Trim();
        if (value.StartsWith("global::", StringComparison.Ordinal))
            value = value[8..];
        if (value.StartsWith("ref ", StringComparison.Ordinal))
            value = value[4..];

        var normalized = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (ch == '<')
            {
                int end = FindGenericListEnd(value, i);
                if (end > i)
                {
                    normalized.Append('`');
                    normalized.Append(GenericArity(value.AsSpan(i + 1, end - i - 1)));
                    i = end;
                    continue;
                }
            }
            if (ch == '`')
            {
                normalized.Append(ch);
                while (i + 1 < value.Length && char.IsAsciiDigit(value[i + 1]))
                    normalized.Append(value[++i]);
                continue;
            }
            if (ch == '>')
                continue;
            if (ch is '@' or ' ')
                continue;
            normalized.Append(ch == '+' ? '.' : ch);
        }

        return normalized.ToString() switch
        {
            "bool" => "System.Boolean",
            "byte" => "System.Byte",
            "sbyte" => "System.SByte",
            "short" => "System.Int16",
            "ushort" => "System.UInt16",
            "int" => "System.Int32",
            "uint" => "System.UInt32",
            "long" => "System.Int64",
            "ulong" => "System.UInt64",
            "char" => "System.Char",
            "float" => "System.Single",
            "double" => "System.Double",
            "string" => "System.String",
            "object" => "System.Object",
            "nint" => "System.IntPtr",
            "nuint" => "System.UIntPtr",
            var result => result,
        };
    }

    static int FindGenericListEnd(string value, int start)
    {
        int depth = 0;
        for (int i = start; i < value.Length; i++)
        {
            if (value[i] == '<')
            {
                depth++;
            }
            else if (value[i] == '>' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    static int GenericArity(ReadOnlySpan<char> arguments)
    {
        int arity = 1;
        int depth = 0;
        bool hasContent = false;
        foreach (char ch in arguments)
        {
            if (!char.IsWhiteSpace(ch))
                hasContent = true;

            switch (ch)
            {
                case '<':
                case '(':
                case '[':
                case '{':
                    depth++;
                    break;
                case '>':
                case ')':
                case ']':
                case '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arity++;
                    break;
            }
        }

        return hasContent ? arity : 0;
    }

    static CompileBackResult Classify(
        string fullType,
        Entry e,
        IReadOnlyList<string> rOps,
        IlBodyDiffResult fidelityDiff,
        bool usedProductWholeMember) =>
        new(fullType, e.Name, e.Overload, e.Signature,
            ClassifyStatus(e.IsFull, e.OrigOps.SequenceEqual(rOps), fidelityDiff),
            e.OrigText, string.Join(" ", rOps), fidelityDiff.Failure,
            FidelityDiff: fidelityDiff,
            UsedProductWholeMember: usedProductWholeMember);

    static string? FormatDiagnostic(Diagnostic? diagnostic)
    {
        if (diagnostic is null)
            return null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(diagnostic.Id))
            parts.Add(diagnostic.Id);
        var message = diagnostic.GetMessage();
        if (!string.IsNullOrWhiteSpace(message))
            parts.Add(message);
        return parts.Count == 0 ? null : string.Join(": ", parts);
    }

    /// <summary>
    /// Renders the failing emitted line with a caret underline under the
    /// diagnostic span — the compiler's own squiggle, and the "act on this"
    /// gesture for a recompile failure. Invisible/format runes on the line are
    /// revealed (e.g. U+200C becomes &lt;ZWNJ&gt;) so the caret points at
    /// something the reader can see, and the caret sits on a <c>//</c> comment so
    /// the whole block stays valid C# inside a code fence. Null when the
    /// diagnostic carries no in-source location (nothing to point at).
    /// When the failure matches an enumerated cause (see <see cref="ClassifyCause"/>),
    /// a <c>cause:</c> and paired <c>fix:</c> line follow on the caret gutter.
    /// </summary>
    internal static string? RenderAnnotatedFailure(string source, Diagnostic? diagnostic)
    {
        if (diagnostic is null || !diagnostic.Location.IsInSource)
            return null;

        var span = diagnostic.Location.GetLineSpan().Span;
        int lineIndex = span.Start.Line;
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        if (lineIndex < 0 || lineIndex >= lines.Length)
            return null;

        string raw = lines[lineIndex];

        // Reveal invisible runes, mapping each raw column to its revealed column
        // so the caret aligns under what the reader actually sees, not the raw
        // (possibly zero-width) span.
        var revealed = new StringBuilder();
        int[] map = new int[raw.Length + 1];
        for (int i = 0; i < raw.Length; i++)
        {
            map[i] = revealed.Length;
            revealed.Append(RevealRune(raw[i]));
        }
        map[raw.Length] = revealed.Length;

        int startCol = Math.Clamp(span.Start.Character, 0, raw.Length);
        int endCol = span.End.Line == lineIndex ? Math.Clamp(span.End.Character, 0, raw.Length) : raw.Length;
        int caretStart = map[startCol];
        int caretLen = Math.Max(1, map[endCol] - caretStart);

        string indent = raw[..(raw.Length - raw.AsSpan().TrimStart().Length)];
        int pad = Math.Max(1, caretStart - indent.Length - 2); // 2 == "//"

        var sb = new StringBuilder();
        sb.Append(revealed).Append('\n');
        sb.Append(indent).Append("//").Append(' ', pad).Append('^', caretLen)
          .Append(' ').Append(diagnostic.Id).Append(": ").Append(diagnostic.GetMessage());

        // Layer 3 (#3256): classify the failure into a cause + paired fix on the
        // same gutter as the caret, when the fix is derivable from the failing
        // token and holds against the compiler. Falls through silently otherwise.
        if (ClassifyCause(raw, startCol, endCol, diagnostic.Id) is { } classified)
        {
            sb.Append('\n').Append(indent).Append("//  cause: ").Append(classified.Cause);
            sb.Append('\n').Append(indent).Append("//  fix:   ").Append(classified.Fix);
        }
        return sb.ToString();
    }

    /// <summary>
    /// A classified recompile failure: a <c>cause</c> and a paired <c>fix</c>, and
    /// (for a concrete <c>emit A → B</c> fix) the raw <see cref="From"/>/<see cref="To"/>
    /// tokens so the claim is checkable — applying <c>From → To</c> to the emitted
    /// source and recompiling must clear the diagnostic. A policy fix (no exact
    /// source form) leaves both null.
    /// </summary>
    internal readonly record struct CauseFix(string Cause, string Fix, string? From, string? To);

    // Lexer/parser diagnostics: the failing rune broke *tokenization* (as opposed
    // to a binding error, where the token lexed fine but resolved wrong). IDs are
    // locale-stable, unlike GetMessage(), so gating on them keeps the classifier
    // free of message parsing.
    static readonly HashSet<string> LexErrorIds = new(StringComparer.Ordinal) { "CS1001", "CS1056" };

    /// <summary>
    /// Layer 3 (#3256): maps a recompile-failure span to an enumerated
    /// <c>(cause, fix)</c> pair, or null to fall through to the bare layer 1–2
    /// render. Honesty rule: classify only what holds against the shipped compiler.
    /// C# treats a Unicode format rune (Cf, e.g. U+200C) as an identifier-<em>part</em>
    /// but not an identifier-<em>start</em>, and it strips Cf when comparing
    /// identifiers and when emitting metadata names. So:
    /// <list type="bullet">
    /// <item>A Cf that broke <em>lexing</em> (leading Cf → a lex-error id) is
    /// <c>invisible-rune</c>: dropping it yields a legal identifier — a concrete,
    /// recompile-verifiable fix.</item>
    /// <item>A Cf that lexed but failed to <em>bind</em> is <c>unspeakable-name</c>:
    /// Roslyn strips Cf from emitted metadata, so a bound name carrying one came
    /// from a non-C# producer and no C# spelling reaches it (policy fix, no delta).</item>
    /// <item>An exact reserved keyword written bare is <c>keyword-escape</c>: the
    /// verbatim <c>@</c> is the fix.</item>
    /// </list>
    /// namespace-shadow and signature-drift need the ground-truth original for the
    /// specific failing token (not available here) and are intentionally omitted.
    /// </summary>
    internal static CauseFix? ClassifyCause(string lineText, int spanStart, int spanEnd, string diagnosticId)
    {
        spanStart = Math.Clamp(spanStart, 0, lineText.Length);
        spanEnd = Math.Clamp(spanEnd, spanStart, lineText.Length);
        string spanToken = lineText[spanStart..spanEnd];

        // The span often points at a single offending char (a leading Cf), not the
        // whole token, so widen it to the enclosing identifier for Cf reasoning.
        string identifier = EnclosingIdentifier(lineText, spanStart, spanEnd);
        bool hasFormatRune = identifier.EnumerateRunes()
            .Any(r => Rune.GetUnicodeCategory(r) == UnicodeCategory.Format);

        if (hasFormatRune)
        {
            string reveal = RevealToken(identifier);
            string stripped = string.Concat(identifier.EnumerateRunes()
                .Where(r => Rune.GetUnicodeCategory(r) != UnicodeCategory.Format)
                .Select(r => r.ToString()));

            // invisible-rune: the Cf broke lexing; dropping it lexes again.
            // IsValidIdentifier is a character-rules check only (identifier-start
            // plus identifier-parts) — it says nothing about keywords, and returns
            // true for 'class'. So when stripping leaves a keyword, dropping alone
            // would swap a lex error for a bare keyword (measurably worse); the
            // honest proposal composes both causes: drop the rune *and* escape.
            if (LexErrorIds.Contains(diagnosticId) && SyntaxFacts.IsValidIdentifier(stripped))
            {
                bool strippedIsKeyword = SyntaxFacts.GetKeywordKind(stripped) != SyntaxKind.None;
                string to = strippedIsKeyword ? "@" + stripped : stripped;
                string note = strippedIsKeyword
                    ? "   (drop the format-category rune, and escape the keyword it exposes)"
                    : "   (drop the format-category rune)";
                return new(
                    $"invisible-rune — a format-category rune (Cf) in '{reveal}' breaks the token; C# accepts Cf as an identifier-part but not an identifier-start.",
                    $"emit  {reveal} → {to}{note}",
                    identifier, to);
            }

            // Cf that lexed but failed to bind → unspeakable (non-C# producer).
            return new(
                $"unspeakable-name — '{reveal}' carries a format-category rune (Cf) that C# strips from identifiers, so no source spelling matches the metadata name.",
                "no exact source form — emit a speakable alias or exclude from round-trip.",
                null, null);
        }

        // keyword-escape: keyword recognition uses the raw lexeme, so this fires
        // only on an exact reserved keyword — 'class', not 'class\u200C'.
        if (SyntaxFacts.IsReservedKeyword(SyntaxFacts.GetKeywordKind(spanToken)))
        {
            return new(
                $"keyword-escape — emitted identifier '{spanToken}' is a C# keyword written bare.",
                $"emit  {spanToken} → @{spanToken}",
                spanToken, "@" + spanToken);
        }

        return null;
    }

    /// <summary>
    /// Widens a diagnostic span to the identifier that encloses it. C# identifier
    /// spans in recompile diagnostics frequently point at a single character (a
    /// leading Cf, or the <c>&lt;</c> of a generated name) rather than the whole
    /// token; the cause classifier needs the surrounding identifier to reason about
    /// it. Returns the span text unchanged when it is not inside an identifier.
    /// </summary>
    static string EnclosingIdentifier(string lineText, int spanStart, int spanEnd)
    {
        int start = spanStart, end = spanEnd;
        while (start > 0 && SyntaxFacts.IsIdentifierPartCharacter(lineText[start - 1]))
            start--;
        while (end < lineText.Length && SyntaxFacts.IsIdentifierPartCharacter(lineText[end]))
            end++;
        return lineText[start..end];
    }

    static string RevealToken(string token) =>
        string.Concat(token.EnumerateRunes().Select(RevealRune));

    /// <summary>
    /// Rune-aware sibling of the char-based caret-line reveal: substitutes a
    /// visible token for a format/control/non-spacing rune, handling non-BMP runes
    /// (whose surrogate halves the char overload would miss) so the <c>fix:</c>
    /// line never prints the very character it is meant to expose.
    /// </summary>
    static string RevealRune(Rune r)
    {
        if (r.Value == 0x200C)
            return "\u2039ZWNJ\u203A";
        if (r.Value == 0x200D)
            return "\u2039ZWJ\u203A";
        return Rune.GetUnicodeCategory(r)
            is UnicodeCategory.Format or UnicodeCategory.Control or UnicodeCategory.NonSpacingMark
            ? $"\u2039U+{r.Value:X4}\u203A"
            : r.ToString();
    }

    /// <summary>
    /// Substitutes a visible token for a rune that would otherwise be invisible
    /// or ambiguous in the rendered source — format characters (e.g. the U+200C
    /// zero-width non-joiner that silently breaks an identifier match), controls,
    /// and non-spacing combining marks. Everything else passes through unchanged.
    /// </summary>
    static string RevealRune(char c)
    {
        if (c == '\t')
            return "\t";
        if (c == '\u200C')
            return "\u2039ZWNJ\u203A";
        if (c == '\u200D')
            return "\u2039ZWJ\u203A";
        return CharUnicodeInfo.GetUnicodeCategory(c)
            is UnicodeCategory.Format or UnicodeCategory.Control or UnicodeCategory.NonSpacingMark
            ? $"\u2039U+{(int)c:X4}\u203A"
            : c.ToString();
    }

    static string DiagnosticCode(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "<unknown>";

        var prefix = detail.Trim();
        int separator = prefix.IndexOf(':');
        if (separator >= 0)
            prefix = prefix[..separator].Trim();
        return prefix.Length == 0 ? "<unknown>" : prefix;
    }

    static string PortablePath(string path)
    {
        var full = Path.GetFullPath(path).Replace('\\', '/');
        const string nugetMarker = "/.nuget/packages/";
        int nuget = full.IndexOf(nugetMarker, StringComparison.OrdinalIgnoreCase);
        if (nuget >= 0)
            return $"nuget:{full[(nuget + nugetMarker.Length)..]}";

        var cwd = Path.GetFullPath(Environment.CurrentDirectory).Replace('\\', '/').TrimEnd('/');
        if (full.StartsWith(cwd + "/", StringComparison.Ordinal))
            return full[(cwd.Length + 1)..];
        return Path.GetFileName(path);
    }

    static void RunType(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        ReferenceSet references, CompilerFeatureOptions.Resolution featureOptions,
        CSharpCompilationOptions compileOptions, int cap, int maxExamples,
        Func<IrFunction, DecompilerResult> render,
        ref int total, ref int full, ref int exact, ref int contextFail,
        ref int recompileFail, ref int opcodeDiff, ref int operandDiff, ref int fidelityUnavailable,
        List<string> opcodeDiffExamples, List<string> operandDiffExamples,
        List<string> fidelityUnavailableExamples,
        List<string> recompileFailExamples, List<string> contextFailExamples,
        SortedDictionary<string, int> recompileFailCodes,
        FidelityPhaseTimings? timings)
    {
        int remaining = cap - total;
        if (remaining <= 0)
            return;
        // CB_TYPE is the documented "focus one type" switch, so it has to be
        // applied before CollectType renders a type's methods -- focusing should
        // cost one type's work, not the whole assembly's. Tested against the same
        // full type name the discarding check below used, so the selected set is
        // unchanged.
        Func<string, bool>? typeFilter =
            Environment.GetEnvironmentVariable("CB_TYPE") is { } filter
                ? name => name.Contains(filter, StringComparison.Ordinal)
                : null;
        var collected = timings is null
            ? CollectType(reader, pe, source, typeHandle, render, remaining, typeFilter)
            : timings.MeasureCollectRender(() => CollectType(reader, pe, source, typeHandle, render, remaining, typeFilter));
        if (collected is not var (fullType, entries) || entries.Count == 0)
            return;

        var results = EvaluateGrouped(reader, pe, references, featureOptions, compileOptions, fullType, typeHandle, entries, timings: timings);
        for (int i = 0; i < results.Count; i++)
        {
            if (total >= cap)
                return;
            total++;
            var e = entries[i];
            var r = results[i];
            if (e.IsFull)
                full++;
            switch (r.Status)
            {
                case CompileBackStatus.Exact:
                    exact++;
                    break;
                case CompileBackStatus.OpcodeDiff:
                    opcodeDiff++;
                    if (opcodeDiffExamples.Count < maxExamples)
                        opcodeDiffExamples.Add($"{r.Type}::{r.Method}\n    orig : {r.OriginalOpcodes}\n    recmp: {r.RecompiledOpcodes}");
                    break;
                case CompileBackStatus.OperandDiff:
                    operandDiff++;
                    if (operandDiffExamples.Count < maxExamples)
                    {
                        string rows = string.Join(
                            Environment.NewLine,
                            r.FidelityDiff!.Rows.Take(4).Select(row => $"      {row.Message}"));
                        operandDiffExamples.Add($"{r.Type}::{r.Method}{Environment.NewLine}{rows}");
                    }
                    break;
                case CompileBackStatus.FidelityUnavailable:
                    fidelityUnavailable++;
                    if (fidelityUnavailableExamples.Count < maxExamples)
                        fidelityUnavailableExamples.Add(FormatFailureExample(r));
                    break;
                case CompileBackStatus.RecompileFail:
                    recompileFail++;
                    recompileFailCodes[DiagnosticCode(r.Detail)] = recompileFailCodes.GetValueOrDefault(DiagnosticCode(r.Detail)) + 1;
                    if (recompileFailExamples.Count < maxExamples)
                        recompileFailExamples.Add(FormatFailureExample(r));
                    break;
                case CompileBackStatus.ContextFail:
                    contextFail++;
                    if (contextFailExamples.Count < maxExamples)
                        contextFailExamples.Add(FormatFailureExample(r));
                    break;
            }
        }
    }

    static void Report(
        int total, int full, int exact, int contextFail, int recompileFail, int opcodeDiff,
        int operandDiff, int fidelityUnavailable,
        SortedDictionary<string, int> recompileFailCodes, List<string> opcodeDiffExamples,
        List<string> operandDiffExamples, List<string> fidelityUnavailableExamples,
        List<string> recompileFailExamples, List<string> contextFailExamples, ZeroSignalGuard? zeroSignal)
    {
        string Pct(int n, int d) => d == 0 ? "0" : $"{100.0 * n / d:F2}%";
        Console.WriteLine($"COMPILE-BACK over {total} rendered methods ({full} Full)");
        Console.WriteLine();
        Console.WriteLine($"  exact (contract v{CurrentContractVersion}): {exact} ({Pct(exact, total)}) — EH-blind");
        Console.WriteLine($"  opcode diff (Full) : {opcodeDiff} — recompiled to a different opcode stream");
        Console.WriteLine($"  operand diff (Full): {operandDiff} — opcode names matched; operand or target differed");
        Console.WriteLine($"  fidelity unavailable: {fidelityUnavailable} — body comparison produced no verdict");
        Console.WriteLine($"  context-build fail : {contextFail} — could not emit the type skeleton");
        Console.WriteLine($"  recompile fail     : {recompileFail} — skeleton + body did not compile");
        if (recompileFailCodes.Count > 0)
        {
            Console.WriteLine("  recompile-fail by code:");
            foreach (var (code, n) in recompileFailCodes.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"    {code}: {n}");
        }
        zeroSignal?.Report();
        if (opcodeDiffExamples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(ExampleHeading("Opcode-diff examples (Full)", opcodeDiffExamples.Count, opcodeDiff));
            foreach (var e in opcodeDiffExamples)
                Console.WriteLine($"  {e}");
        }
        if (operandDiffExamples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(ExampleHeading(
                "Operand-diff examples (Full; EH-blind)",
                operandDiffExamples.Count,
                operandDiff));
            foreach (var e in operandDiffExamples)
                Console.WriteLine($"  {e}");
        }
        if (fidelityUnavailableExamples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(ExampleHeading(
                "Fidelity-unavailable examples",
                fidelityUnavailableExamples.Count,
                fidelityUnavailable));
            foreach (var e in fidelityUnavailableExamples)
                Console.WriteLine($"  {e}");
        }
        if (recompileFailExamples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(ExampleHeading("Recompile-fail examples", recompileFailExamples.Count, recompileFail));
            foreach (var e in recompileFailExamples)
                Console.WriteLine($"  {e}");
        }
        if (contextFailExamples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(ExampleHeading("Context-fail examples", contextFailExamples.Count, contextFail));
            foreach (var e in contextFailExamples)
                Console.WriteLine($"  {e}");
        }
    }

    static string ExampleHeading(string title, int shown, int total)
        => shown == total ? $"{title} ({total}):" : $"{title} (showing {shown} of {total}):";

    static string FormatFailureExample(CompileBackResult result)
        => string.IsNullOrWhiteSpace(result.Detail)
            ? $"{result.Type}::{result.Method}"
            : $"{result.Type}::{result.Method}\n    {result.Detail}";

    internal static IReadOnlyDictionary<string, FailureBucketSummary> SummarizeFailures(
        IReadOnlyList<CompileBackResult> results, CompileBackStatus status)
    {
        var bucketCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var bucketExamples = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in results.Where(result => result.Status == status))
        {
            var bucket = ClassifyFailure(result);
            bucketCounts[bucket] = bucketCounts.GetValueOrDefault(bucket) + 1;
            if (!bucketExamples.TryGetValue(bucket, out var examples))
            {
                examples = [];
                bucketExamples[bucket] = examples;
            }
            if (examples.Count < 3)
                examples.Add($"{result.Type}::{result.Method}");
        }

        return bucketCounts.ToDictionary(
            kv => kv.Key,
            kv => new FailureBucketSummary(
                kv.Value,
                bucketExamples.GetValueOrDefault(kv.Key, []).ToImmutableArray()),
            StringComparer.OrdinalIgnoreCase);
    }

    internal sealed record FailureBucketSummary(int Count, ImmutableArray<string> Examples);

    static string ClassifyFailure(CompileBackResult result)
    {
        if (result.Status == CompileBackStatus.ContextFail)
            return ClassifyContextFailure(result.Detail);
        return ClassifyRecompileFailure(result.Detail);
    }

    static string ClassifyContextFailure(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "skeleton emission";
        if (detail.Contains("generated-member-unsupported", StringComparison.OrdinalIgnoreCase))
            return "generated/synthesized member (unsupported)";
        if (detail.Contains("target-method-not-found", StringComparison.OrdinalIgnoreCase))
            return "target method not found";
        if (detail.Contains("return-to-sender-target-unavailable", StringComparison.OrdinalIgnoreCase))
            return "return-to-sender target unavailable";
        if (detail.Contains("return-to-sender-context-unavailable", StringComparison.OrdinalIgnoreCase))
            return "return-to-sender context unavailable";
        if (detail.Contains("method", StringComparison.OrdinalIgnoreCase)
            && detail.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return "target method not found";
        if (detail.Contains("skeleton", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("emit", StringComparison.OrdinalIgnoreCase))
            return "skeleton emission";
        return "other context failure";
    }

    static string ClassifyRecompileFailure(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "compiler diagnostic";
        if (detail.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
            return "missing symbol";
        if (detail.Contains("inaccessible", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("protection level", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("less accessible", StringComparison.OrdinalIgnoreCase))
            return "accessibility";
        if (detail.Contains("constraint", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("where", StringComparison.OrdinalIgnoreCase))
            return "generic constraint";
        if (detail.Contains("syntax", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("expected", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("identifier", StringComparison.OrdinalIgnoreCase))
            return "syntax";
        if (detail.Contains("convert", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("implicit", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("explicit", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("cast", StringComparison.OrdinalIgnoreCase))
            return "conversion";
        return "compiler diagnostic";
    }

    static List<ILInstructionText>? FindAndDisassemble(PEReader pe, string fullType, string name, int overload)
    {
        if (FindMethodDefinition(pe, fullType, name, overload) is not { } found)
            return null;
        return MetadataInstructionProducer.Disassemble(pe, found.Reader, found.Method);
    }

    static (MetadataReader Reader, MethodDefinitionHandle Handle, MethodDefinition Method)? FindMethodDefinition(
        PEReader pe,
        string fullType,
        string name,
        int overload)
    {
        var reader = pe.GetMetadataReader();
        int seen = 0;
        foreach (var tdh in reader.TypeDefinitions)
        {
            var td = reader.GetTypeDefinition(tdh);
            // Nested-aware full name (Outer.Inner) so a recompiled nested-type
            // target re-resolves; top-level names are unchanged (ns.tn).
            string ft = reader.GetFullTypeName(td);
            if (ft != fullType)
                continue;
            // The IL names .ctor/.cctor become the type name / static-ctor in C#;
            // map back so the target re-resolves by its metadata name.
            string match = name is ".ctor" or ".cctor" ? name : name;
            foreach (var mh in td.GetMethods())
            {
                var m = reader.GetMethodDefinition(mh);
                string mn = reader.GetString(m.Name);
                if (mn != match)
                    continue;
                if (seen++ == overload)
                    return (reader, mh, m);
            }
        }
        return null;
    }

    static IlBodyDiffResult CompareCompileBackFidelity(
        PEReader originalPe,
        MetadataReader originalReader,
        MethodDefinitionHandle originalMethod,
        PEReader recompiledPe,
        string fullType,
        string methodName,
        int overload)
    {
        if (FindMethodDefinition(recompiledPe, fullType, methodName, overload) is not { } recompiled)
            return IlBodyDiffResult.NewBodyMissing("recompiled method not found");

        return IlAssemblyDiff.CompareMembers(
            originalPe,
            originalReader,
            originalMethod,
            recompiledPe,
            recompiled.Reader,
            recompiled.Handle,
            oldLabel: $"{fullType}::{methodName}",
            newLabel: $"{fullType}::{methodName}",
            normalization: ContractBodyDiffNormalization).Diff;
    }

    static string CanonicalOpcode(string op)
        => HarnessOpcode.Canonicalize(op);

    /// <summary>
    /// References for recompilation: the running runtime (TPA), every sibling
    /// assembly in the target's own directory (project deps, test framework, etc.),
    /// and package assets named by the target's deps.json, EXCLUDING the target
    /// assembly itself. We reconstruct the target's own types from metadata, so
    /// referencing the real DLL would duplicate them (ambiguous-reference errors);
    /// referencing its neighbours resolves cross-assembly types in the stubbed
    /// signatures.
    /// </summary>
    static ReferenceSet RuntimeReferences(string targetPath, IReadOnlyList<string>? corpusAssemblies = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedReferences = new List<CompilerReference>();
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();

        void Add(string path, AssemblyDependencyProvenance? provenance)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return;
            if (!File.Exists(path))
                return;
            if (!ManagedReferenceFilter.IsManagedAssembly(path))
                return;
            string simple = Path.GetFileNameWithoutExtension(path);
            if (seen.Contains(simple))
                return; // first definition wins (prefer TPA over a dir copy)
            try
            {
                var fullPath = Path.GetFullPath(path);
                var reference = MetadataReference.CreateFromFile(fullPath);
                if (seen.Add(simple))
                {
                    builder.Add(reference);
                    if (TryReadAssemblyIdentity(fullPath) is { } identity)
                        resolvedReferences.Add(new CompilerReference(
                            ResolvedAssemblyReference.Create(
                                identity,
                                fullPath,
                                () => File.OpenRead(fullPath),
                                AssemblyResolutionProvenance.Local(
                                    provenance?.ToString() ?? "CompilerReference")),
                            PlatformTrusted: provenance is AssemblyDependencyProvenance.TrustedPlatformAssembly
                                or AssemblyDependencyProvenance.SharedFramework));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (BadImageFormatException) { }
            catch (ArgumentException) { }
        }

        var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
        {
            CorpusAssemblyPaths = corpusAssemblies,
            ExcludeTargetAssembly = true,
        });
        AssemblyResolutionResult resolution = resolver.ResolveAll();
        if (!resolution.Diagnostics.IsEmpty)
        {
            throw new InvalidOperationException(
                "Compiler reference discovery failed in "
                + $"{resolution.Diagnostics.Length} enabled tier(s).");
        }
        foreach (var dependency in resolution.Items)
            Add(dependency.Path, dependency.Provenance);

        return new ReferenceSet(builder.ToImmutable(), new SignatureSpellability(new CompilerReferenceResolver(resolvedReferences)));
    }

    static AssemblyReferenceIdentity? TryReadAssemblyIdentity(string path)
    {
        try
        {
            var name = AssemblyName.GetAssemblyName(path);
            string? token = ToHex(name.GetPublicKeyToken());
            return new AssemblyReferenceIdentity(
                name.Name ?? Path.GetFileNameWithoutExtension(path),
                name.Version,
                name.CultureName,
                token);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static string? ToHex(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return null;

        var chars = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i * 2] = "0123456789abcdef"[bytes[i] >> 4];
            chars[i * 2 + 1] = "0123456789abcdef"[bytes[i] & 0xF];
        }
        return new string(chars);
    }

}
