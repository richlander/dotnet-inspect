using System.Collections.Immutable;

namespace ILInspector.Analysis;

internal static class RepeatedScanAnalysis
{
    internal static bool IsLinqMembershipScan(
        MemberRef member,
        out string operation)
    {
        operation = "";
        if (member.Kind == MemberKind.Unsupported
            || !IsEnumerableDefinition(member.DeclaringType)
            || member.ParameterTypes.Length < 2)
        {
            return false;
        }

        switch (member.Name)
        {
            case "Any":
            case "All":
            case "First":
            case "FirstOrDefault":
            case "Last":
            case "LastOrDefault":
            case "Single":
            case "SingleOrDefault":
            case "Count":
            case "LongCount":
            case "Contains":
                operation = member.Name;
                return true;
            default:
                return false;
        }
    }

    internal static bool IsLinqMaterializer(
        MemberRef member,
        out string operation)
    {
        operation = "";
        if (member.Kind == MemberKind.Unsupported
            || !IsEnumerableDefinition(member.DeclaringType)
            || member.ParameterTypes.Length != 1
            || member.TypeArguments.Length != 1)
        {
            return false;
        }

        switch (member.Name)
        {
            case "ToArray":
            case "ToList":
                operation = member.Name;
                return true;
            default:
                return false;
        }
    }

    internal static bool IsStringConcat(MemberRef member)
        => member.Kind != MemberKind.Unsupported
            && member.Name == "Concat"
            && FrameworkIdentity.IsCoreLibraryType(
                member.DeclaringType,
                "System",
                "String");

    internal static bool IsInterfaceEnumeratorAllocation(MemberRef member)
    {
        if (member.Kind == MemberKind.Unsupported
            || member.Name != "GetEnumerator")
        {
            return false;
        }

        var returnType = member.ReturnType;
        var definition = returnType.Kind == TypeRefKind.GenericInstance
            ? returnType.ElementType ?? returnType
            : returnType;
        return FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Collections.Generic",
                "IEnumerator`1")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Collections",
                "IEnumerator");
    }

    internal static bool IsLinqLazyProducer(
        MemberRef member,
        out string operation)
    {
        operation = "";
        if (member.Kind == MemberKind.Unsupported
            || !IsEnumerableDefinition(member.DeclaringType))
        {
            return false;
        }

        switch (member.Name)
        {
            case "Where":
            case "Select":
            case "SelectMany":
            case "OfType":
            case "Cast":
                operation = member.Name;
                return true;
            default:
                return false;
        }
    }

    internal static ImmutableArray<OptimizationOpportunity> Collect(
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<OptimizationOpportunity> rawOpportunities,
        IReadOnlySet<int> suppressedMethodTokens,
        IReadOnlyDictionary<int, int> reachByToken)
    {
        var methodByToken =
            new Dictionary<int, MethodIdentity>(methods.Length);
        foreach (var method in methods)
            methodByToken[method.MetadataToken] = method;

        var scanningMethods = new Dictionary<int, string>();
        var inAssemblyCallees = new Dictionary<int, HashSet<int>>();
        var lazyReturning = new Dictionary<int, string>();
        var immediateLazyProducers =
            new Dictionary<(int MethodToken, int NextOffset), string>();
        foreach (var call in directCalls)
        {
            if (IsLinqMembershipScan(
                    call.Callee,
                    out var membershipOperation))
            {
                scanningMethods.TryAdd(
                    call.Caller.MetadataToken,
                    membershipOperation);
            }
            else if (IsLinqLazyProducer(
                    call.Callee,
                    out var lazyOperation))
            {
                if (lazyOperation is "Where" or "OfType"
                    && call.ReturnAddress is { } nextOffset)
                {
                    immediateLazyProducers.TryAdd(
                        (call.Caller.MetadataToken, nextOffset),
                        lazyOperation);
                }
                if (methodByToken.TryGetValue(
                        call.Caller.MetadataToken,
                        out var producer)
                    && ReturnsEnumerableSequence(producer.ReturnType))
                {
                    lazyReturning.TryAdd(
                        call.Caller.MetadataToken,
                        lazyOperation);
                }
            }
            else if (IsLinqParameterlessTerminal(
                    call.Callee,
                    out var terminalOperation)
                && immediateLazyProducers.TryGetValue(
                    (call.Caller.MetadataToken, call.ILOffset),
                    out var producerOperation))
            {
                scanningMethods.TryAdd(
                    call.Caller.MetadataToken,
                    $"{producerOperation}+{terminalOperation}");
            }

            if (methodByToken.ContainsKey(call.CalleeDefinitionToken))
            {
                if (!inAssemblyCallees.TryGetValue(
                        call.Caller.MetadataToken,
                        out var callees))
                {
                    inAssemblyCallees[call.Caller.MetadataToken] =
                        callees = [];
                }
                callees.Add(call.CalleeDefinitionToken);
            }
        }

        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (callerToken, callees) in inAssemblyCallees)
            {
                if (lazyReturning.ContainsKey(callerToken)
                    || !methodByToken.TryGetValue(
                        callerToken,
                        out var method)
                    || !ReturnsEnumerableSequence(method.ReturnType))
                {
                    continue;
                }

                foreach (var callee in callees)
                {
                    if (lazyReturning.TryGetValue(
                            callee,
                            out var operation))
                    {
                        lazyReturning[callerToken] = operation;
                        changed = true;
                        break;
                    }
                }
            }
        }

        foreach (var (token, operation) in lazyReturning)
            scanningMethods.TryAdd(token, operation);

        var methodMap = MethodDefinitionMap.Create(methods);
        var recursiveTraversalTokens = directCalls
            .Where(static call => call.Kind == CallKind.Call && call.InLoop)
            .Where(call => methodMap.Resolve(call) == call.Caller.MetadataToken)
            .Select(static call => call.Caller.MetadataToken)
            .ToHashSet();

        var inMethodScanLoopTokens = new HashSet<int>();
        foreach (var opportunity in rawOpportunities)
        {
            if (opportunity.Shape == "linq-scan-in-loop")
            {
                inMethodScanLoopTokens.Add(
                    opportunity.Method.MetadataToken);
            }
        }

        var opportunities =
            ImmutableArray.CreateBuilder<OptimizationOpportunity>();
        var emitted = new HashSet<int>();
        foreach (var call in directCalls)
        {
            if (!call.InLoop)
                continue;
            int calleeToken = call.CalleeDefinitionToken;
            if (!scanningMethods.TryGetValue(
                    calleeToken,
                    out var operation)
                || !methodByToken.TryGetValue(calleeToken, out var method)
                || suppressedMethodTokens.Contains(calleeToken)
                || inMethodScanLoopTokens.Contains(calleeToken)
                || call.Caller.MetadataToken == calleeToken
                || !emitted.Add(calleeToken))
            {
                continue;
            }

            opportunities.Add(new OptimizationOpportunity(
                method,
                "scan-method-in-loop-call",
                $"Linearly scans a sequence (Enumerable.{operation}); invoked inside a loop by {call.Caller.DeclaringType.ToQualifiedDisplayString()}::{call.Caller.Name}",
                "A method that linearly scans a sequence is called on every iteration of a caller's loop; precompute an index the caller can reuse, or hoist the scan out of the loop.",
                "low",
                true,
                null,
                "Quadratic only if the scanned sequence grows with the caller's loop; confirm the sequence is the loop-variant collection and not small/constant.",
                reachByToken.GetValueOrDefault(calleeToken)));
        }

        foreach (var call in directCalls)
        {
            int calleeToken = call.CalleeDefinitionToken;
            if (!recursiveTraversalTokens.Contains(call.Caller.MetadataToken)
                || !scanningMethods.TryGetValue(calleeToken, out var operation)
                || !methodByToken.TryGetValue(calleeToken, out var method)
                || suppressedMethodTokens.Contains(calleeToken)
                || inMethodScanLoopTokens.Contains(calleeToken)
                || call.Caller.MetadataToken == calleeToken
                || !emitted.Add(calleeToken))
            {
                continue;
            }

            opportunities.Add(new OptimizationOpportunity(
                method,
                "scan-method-in-recursive-traversal",
                $"Linearly scans a sequence (Enumerable.{operation}); invoked once per recursive traversal node by {call.Caller.DeclaringType.ToQualifiedDisplayString()}::{call.Caller.Name}",
                "If the scan source is shared across recursive calls, build an index before recursion and reuse it; otherwise keep the node-local scan.",
                "low",
                true,
                null,
                "Potentially superlinear only when each recursive step scans the same growing sequence; static analysis has not proved source identity.",
                reachByToken.GetValueOrDefault(calleeToken)));
        }

        return opportunities.ToImmutable();
    }

    static bool IsEnumerableDefinition(TypeRef type)
        => FrameworkIdentity.IsKnownFrameworkType(
            type,
            "System.Linq",
            "System.Linq",
            "Enumerable");

    static bool IsLinqParameterlessTerminal(
        MemberRef member,
        out string operation)
    {
        operation = "";
        if (member.Kind == MemberKind.Unsupported
            || !IsEnumerableDefinition(member.DeclaringType)
            || member.ParameterTypes.Length != 1)
        {
            return false;
        }

        switch (member.Name)
        {
            case "Any":
            case "First":
            case "FirstOrDefault":
            case "Last":
            case "LastOrDefault":
            case "Single":
            case "SingleOrDefault":
            case "Count":
            case "LongCount":
                operation = member.Name;
                return true;
            default:
                return false;
        }
    }

    static bool ReturnsEnumerableSequence(TypeRef returnType)
    {
        var definition = returnType.Kind == TypeRefKind.GenericInstance
            ? returnType.ElementType ?? returnType
            : returnType;
        if (definition.Kind == TypeRefKind.Unsupported)
            return false;
        return (definition.Namespace == "System.Collections.Generic"
                && definition.Name == "IEnumerable`1")
            || (definition.Namespace == "System.Collections"
                && definition.Name == "IEnumerable");
    }
}
