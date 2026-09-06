// Aliases for the harness's fully applied protocol types. Alias directives do
// not see other usings, so every target is spelled in full.
global using ToyCandidate = DotnetInspector.SourceDelegation.DelegationCandidate<
    DotnetInspector.SourceDelegation.Tests.ToyMember,
    DotnetInspector.SourceDelegation.Tests.ToyInput,
    DotnetInspector.SourceDelegation.Tests.ToyOperation,
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyCause = DotnetInspector.SourceDelegation.DelegationCause<
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyContext = DotnetInspector.SourceDelegation.CompletionContext<
    DotnetInspector.SourceDelegation.Tests.ToyMember,
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyCountOffer = DotnetInspector.SourceDelegation.CountMemberOffer<
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyCounts = DotnetInspector.SourceDelegation.ExactCountResult<
    DotnetInspector.SourceDelegation.Tests.ToyMember,
    DotnetInspector.SourceDelegation.Tests.ToyRow,
    DotnetInspector.SourceDelegation.Tests.ToyInput,
    DotnetInspector.SourceDelegation.Tests.ToyOperation,
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyEvidence = DotnetInspector.SourceDelegation.CompletionEvidence<
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyGroup = DotnetInspector.SourceDelegation.DelegationGroup<
    DotnetInspector.SourceDelegation.Tests.ToyMember>;
global using ToyHandoff = DotnetInspector.SourceDelegation.RowHandoffResult<
    DotnetInspector.SourceDelegation.Tests.ToyMember,
    DotnetInspector.SourceDelegation.Tests.ToyRow,
    DotnetInspector.SourceDelegation.Tests.ToyInput,
    DotnetInspector.SourceDelegation.Tests.ToyOperation,
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyNotSatisfied = DotnetInspector.SourceDelegation.NotSatisfiedResult<
    DotnetInspector.SourceDelegation.Tests.ToyMember,
    DotnetInspector.SourceDelegation.Tests.ToyRow,
    DotnetInspector.SourceDelegation.Tests.ToyInput,
    DotnetInspector.SourceDelegation.Tests.ToyOperation,
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyOutcome = DotnetInspector.SourceDelegation.SourceDelegationOutcome<
    DotnetInspector.SourceDelegation.Tests.ToyMember,
    DotnetInspector.SourceDelegation.Tests.ToyRow,
    DotnetInspector.SourceDelegation.Tests.ToyInput,
    DotnetInspector.SourceDelegation.Tests.ToyOperation,
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyReply = DotnetInspector.SourceDelegation.SourceDelegationReply<
    DotnetInspector.SourceDelegation.Tests.ToyMember,
    DotnetInspector.SourceDelegation.Tests.ToyRow,
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyRowOffer = DotnetInspector.SourceDelegation.RowMemberOffer<
    DotnetInspector.SourceDelegation.Tests.ToyRow,
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyRowOutcome = DotnetInspector.SourceDelegation.RowMemberOutcome<
    DotnetInspector.SourceDelegation.Tests.ToyMember,
    DotnetInspector.SourceDelegation.Tests.ToyRow,
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyRowValues = DotnetInspector.SourceDelegation.RowValuesOutcome<
    DotnetInspector.SourceDelegation.Tests.ToyMember,
    DotnetInspector.SourceDelegation.Tests.ToyRow,
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
global using ToyUnavailable = DotnetInspector.SourceDelegation.UnavailableOutcome<
    DotnetInspector.SourceDelegation.Tests.ToyMember,
    DotnetInspector.SourceDelegation.Tests.ToyRow,
    DotnetInspector.SourceDelegation.Tests.ToyDisposition,
    DotnetInspector.SourceDelegation.Tests.ToyWitness>;
