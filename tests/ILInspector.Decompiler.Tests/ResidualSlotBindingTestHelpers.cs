using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Binds residual stack slots the way the product reaches the printer: every
/// nested lambda or local-function body through its own function scope (the
/// product runs the nested pipeline, including <see cref="ResidualSlotBindingPass"/>,
/// inside the raising passes before the nested node exists), then the enclosing
/// scope. Synthetic trees that construct <see cref="Lambda"/> or
/// <see cref="LocalFunctionStatement"/> nodes directly need this before
/// <see cref="CSharpPrinter.Print(IrFunction)"/>.
/// </summary>
static class ResidualSlotBindingTestHelpers
{
    public static IrFunction BindResidualSlots(this IrFunction function)
    {
        BindNested(function, function.Body);
        new ResidualSlotBindingPass().Run(function, PassContext.None);
        return function;
    }

    static void BindNested(IrFunction host, IrNode root)
    {
        foreach (var nested in DirectlyNested(root))
        {
            switch (nested)
            {
                case Lambda lambda:
                    BindNested(host, lambda.Body);
                    if (!HasSlots(lambda.Body))
                        break;
                    lambda.ReplaceWith(BindLambda(host, lambda));
                    break;
                case LocalFunctionStatement localFunction:
                    BindNested(host, localFunction.Body);
                    if (!HasSlots(localFunction.Body))
                        break;
                    localFunction.ReplaceWith(BindLocalFunction(host, localFunction));
                    break;
            }
        }
    }

    static bool HasSlots(IrNode body)
        => body.Descendants.Any(static node => node is StoreStackSlot or LoadStackSlot);

    static List<IrNode> DirectlyNested(IrNode root)
    {
        var nested = new List<IrNode>();
        Walk(root);
        return nested;

        void Walk(IrNode node)
        {
            foreach (var child in node.Children)
            {
                if (child is Lambda or LocalFunctionStatement)
                    nested.Add(child);
                else
                    Walk(child);
            }
        }
    }

    static Lambda BindLambda(IrFunction host, Lambda lambda)
    {
        var body = lambda.Body;
        var returnType = body.Descendants
            .OfType<Return>()
            .Select(static ret => ret.Value?.ResultType)
            .FirstOrDefault(static type => type is not null)
            ?? TypeRef.CoreLib("System", "Void");
        body.Detach();
        var nested = new IrFunction(
            "<lambda>",
            host.DeclaringType,
            new MethodSignature(returnType, lambda.Parameters, HasThis: false, GenericParameterCount: 0),
            lambda.Locals,
            body)
        {
            LocalNames = lambda.LocalNames,
            SynthesizedLocalNames = lambda.SynthesizedLocalNames,
            LocalDeclaredInNestedScope = lambda.LocalDeclaredInNestedScope,
            LocalDeclarationBindings = lambda.LocalDeclarationBindings,
            PdbLocalNameCandidates = lambda.PdbLocalNameCandidates,
            LocalNameImportCauses = lambda.LocalNameImportCauses,
            UsesUpdatedMemorySafetyRules = lambda.UsesUpdatedMemorySafetyRules,
            SkipLocalsInit = lambda.SkipLocalsInit,
        };
        nested.RestoreMaterializedStackSlotLocals(lambda.MaterializedStackSlotLocals);
        nested.CopyTypeFactsFrom(host);
        new ResidualSlotBindingPass().Run(nested, PassContext.None);
        body.Detach();
        return new Lambda(
            lambda.DelegateType,
            lambda.Parameters,
            nested.Locals,
            nested.LocalNames,
            lambda.UsesUpdatedMemorySafetyRules,
            lambda.SkipLocalsInit,
            body)
        {
            ReturnsVoid = lambda.ReturnsVoid,
            ParameterRefKinds = lambda.ParameterRefKinds,
            SynthesizedLocalNames = nested.SynthesizedLocalNames,
            LocalDeclaredInNestedScope = nested.LocalDeclaredInNestedScope,
            LocalDeclarationBindings = nested.LocalDeclarationBindings,
            PdbLocalNameCandidates = nested.PdbLocalNameCandidates,
            LocalNameImportCauses = nested.LocalNameImportCauses,
            MaterializedStackSlotLocals = nested.MaterializedStackSlotLocals,
            ResidualSlotBindings = nested.ResidualSlotBindings,
        };
    }

    static LocalFunctionStatement BindLocalFunction(IrFunction host, LocalFunctionStatement localFunction)
    {
        var body = localFunction.Body;
        body.Detach();
        var nested = new IrFunction(
            localFunction.Name,
            host.DeclaringType,
            new MethodSignature(localFunction.ReturnType, localFunction.Parameters, HasThis: false, GenericParameterCount: 0),
            localFunction.Locals,
            body)
        {
            LocalNames = localFunction.LocalNames,
            SynthesizedLocalNames = localFunction.SynthesizedLocalNames,
            LocalDeclaredInNestedScope = localFunction.LocalDeclaredInNestedScope,
            LocalDeclarationBindings = localFunction.LocalDeclarationBindings,
            PdbLocalNameCandidates = localFunction.PdbLocalNameCandidates,
            LocalNameImportCauses = localFunction.LocalNameImportCauses,
            UsesUpdatedMemorySafetyRules = localFunction.UsesUpdatedMemorySafetyRules,
            SkipLocalsInit = localFunction.SkipLocalsInit,
        };
        nested.RestoreMaterializedStackSlotLocals(localFunction.MaterializedStackSlotLocals);
        nested.CopyTypeFactsFrom(host);
        new ResidualSlotBindingPass().Run(nested, PassContext.None);
        body.Detach();
        return new LocalFunctionStatement(
            localFunction.Name,
            localFunction.ReturnType,
            localFunction.Parameters,
            localFunction.ParameterRefKinds,
            localFunction.IsStatic,
            nested.Locals,
            nested.LocalNames,
            localFunction.UsesUpdatedMemorySafetyRules,
            localFunction.SkipLocalsInit,
            body,
            localFunction.RequiresUnsafe)
        {
            SynthesizedLocalNames = nested.SynthesizedLocalNames,
            LocalDeclaredInNestedScope = nested.LocalDeclaredInNestedScope,
            LocalDeclarationBindings = nested.LocalDeclarationBindings,
            PdbLocalNameCandidates = nested.PdbLocalNameCandidates,
            LocalNameImportCauses = nested.LocalNameImportCauses,
            MaterializedStackSlotLocals = nested.MaterializedStackSlotLocals,
            ResidualSlotBindings = nested.ResidualSlotBindings,
        };
    }
}
