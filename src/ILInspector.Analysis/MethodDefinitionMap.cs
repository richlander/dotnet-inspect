using System.Collections.Immutable;
using System.Runtime.InteropServices;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Same-module callee resolution over an eager declared-method list. The
/// resolution itself is <see cref="SameModuleMethodResolver"/>'s.
/// </summary>
internal sealed class MethodDefinitionMap : SameModuleMethodResolver
{
    readonly HashSet<int> _methodTokens;
    readonly HashSet<int> _invalidMethodTokens;
    readonly Dictionary<(TypeRef DeclaringType, string Name), List<SameModuleMethodSignature>>
        _methodsByDeclaringTypeAndName;

    MethodDefinitionMap(
        HashSet<int> methodTokens,
        HashSet<int> invalidMethodTokens,
        Dictionary<(TypeRef DeclaringType, string Name), List<SameModuleMethodSignature>>
            methodsByDeclaringTypeAndName,
        SameImageSignatureComparer signatureComparer)
        : base(signatureComparer)
    {
        _methodTokens = methodTokens;
        _invalidMethodTokens = invalidMethodTokens;
        _methodsByDeclaringTypeAndName = methodsByDeclaringTypeAndName;
    }

    public static MethodDefinitionMap Create(
        ImmutableArray<MethodIdentity> methods,
        string? currentModuleName = null)
    {
        HashSet<int> methodTokens = [];
        HashSet<int> invalidMethodTokens = [];
        Dictionary<(TypeRef DeclaringType, string Name), List<SameModuleMethodSignature>>
            methodsByDeclaringTypeAndName = [];
        AssemblyReferenceIdentity? assembly = null;
        foreach (MethodIdentity method in methods)
        {
            if (assembly is null
                && Definition(method.DeclaringType).Resolution?.Origin
                    is TypeReferenceOrigin.CurrentAssembly
                    { Assembly: { } currentAssembly })
            {
                assembly = currentAssembly;
            }

            methodTokens.Add(method.MetadataToken);
            if (method.HasInvalidGenericParameterDeclaration)
                invalidMethodTokens.Add(method.MetadataToken);

            var key = (method.DeclaringType, method.Name);
            SameModuleMethodSignature signature =
                SameModuleMethodSignature.From(method);
            if (methodsByDeclaringTypeAndName.TryGetValue(
                    key,
                    out List<SameModuleMethodSignature>? candidates))
            {
                candidates.Add(signature);
            }
            else
            {
                methodsByDeclaringTypeAndName[key] = [signature];
            }
        }
        return new(
            methodTokens,
            invalidMethodTokens,
            methodsByDeclaringTypeAndName,
            new SameImageSignatureComparer(assembly, currentModuleName));
    }

    public override bool ContainsToken(int token)
        => _methodTokens.Contains(token);

    private protected override bool HasInvalidGenericDeclaration(int token)
        => _invalidMethodTokens.Contains(token);

    private protected override ReadOnlySpan<SameModuleMethodSignature> Candidates(
        TypeRef definition,
        string name)
        => _methodsByDeclaringTypeAndName.TryGetValue(
                (definition, name),
                out List<SameModuleMethodSignature>? candidates)
            ? CollectionsMarshal.AsSpan(candidates)
            : [];

    public bool CouldResolveToCurrentModule(DirectCall call)
        => call.Callee.Kind != MemberKind.Unsupported
            && CanResolveToCurrentModule(call.Callee.DeclaringType);

    public int Resolve(DirectCall call)
        => ResolveTyped(call).Token;

    /// <summary>
    /// Resolves a direct call to a current-module definition, retaining why an
    /// unresolved call failed. <see cref="Resolve(DirectCall)"/> is this outcome's token.
    /// </summary>
    public MethodResolution ResolveTyped(DirectCall call)
        => ResolveTyped(
            SameModuleCallerScope.From(call.EvidenceMethod),
            call.CalleeDefinitionToken,
            call.Callee);

    /// <summary>The Analysis-issued target of one physical direct call.</summary>
    public SameModuleCallTarget ResolveTarget(DirectCall call)
        => ResolveTarget(
            call.Kind,
            SameModuleCallerScope.From(call.EvidenceMethod),
            call.CalleeDefinitionToken,
            call.Callee);
}
