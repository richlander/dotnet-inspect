----------------------- MODULE TypeForwardingResolution -----------------------
EXTENDS FiniteSets, Integers, Sequences, TLC

\* Owned by docs/design/type-forwarding-resolution.md.
\* Binding policy and single-image declaration probing supply typed inputs.
\* The model owns only the cross-assembly forwarding state machine.

CONSTANTS
    AssemblyA,
    AssemblyB,
    AssemblyC,
    NoOutcome,
    HopBudget,
    MutationMode

Assemblies == {AssemblyA, AssemblyB, AssemblyC}
Scopes == {"Any", "Platform"}
Phases == {"Probing", "Binding", "Opening", "Terminal"}
DeclarationKinds ==
    {"Defined", "Missing", "Forwarded", "Ambiguous", "Rejected",
     "ModuleExport"}
TerminalKinds ==
    {"Resolved", "NotFound", "UnboundBinding", "Unavailable",
     "Ambiguous", "Rejected"}
TerminalCauses ==
    {"Definition", "DeclarationMissing", "BindingMissing",
     "BindingUnavailable", "BindingAmbiguous", "DeclarationAmbiguous",
     "DeclarationRejected", "BindingRejected", "ModuleExport",
     "CandidateUnreadable", "CandidateInvalidImage",
     "CandidateResourceBudget", "Cycle", "HopBudget", "ForwarderShortcut"}

ASSUME
    /\ Cardinality(Assemblies) = 3
    /\ NoOutcome \notin TerminalKinds
    /\ NoOutcome \notin TerminalCauses
    /\ HopBudget \in 1..3
    /\ MutationMode \in
        {"Policy", "LooseScope", "PermitCycle", "ResolveForwarder",
         "CollapseBindingMiss", "ResolveAtStart", "AcceptInvalidImage"}

NoDuplicates(sequence) ==
    \A left, right \in 1..Len(sequence):
        left # right => sequence[left] # sequence[right]

Tighten(currentScope, requestedScope) ==
    IF currentScope = "Platform" \/ requestedScope = "Platform"
    THEN "Platform"
    ELSE "Any"

NextScope(currentScope, requestedScope) ==
    IF /\ MutationMode = "LooseScope"
       /\ currentScope = "Platform"
       /\ requestedScope = "Any"
    THEN "Any"
    ELSE Tighten(currentScope, requestedScope)

ExpectedKind(cause) ==
    CASE cause = "Definition" -> "Resolved"
      [] cause = "DeclarationMissing" -> "NotFound"
      [] cause = "BindingMissing" -> "UnboundBinding"
      [] cause = "BindingUnavailable" -> "Unavailable"
      [] cause \in {"BindingAmbiguous", "DeclarationAmbiguous"} ->
            "Ambiguous"
      [] cause \in
            {"DeclarationRejected", "BindingRejected", "ModuleExport",
             "CandidateUnreadable", "CandidateInvalidImage",
             "CandidateResourceBudget", "Cycle", "HopBudget",
             "ForwarderShortcut"} -> "Rejected"
      [] cause = NoOutcome -> NoOutcome

VARIABLES
    phase,
    current,
    path,
    initialScope,
    scope,
    hops,
    lastDeclaration,
    validated,
    terminalKind,
    terminalCause,
    terminalAssembly

vars ==
    <<phase, current, path, initialScope, scope, hops, lastDeclaration,
      validated, terminalKind, terminalCause, terminalAssembly>>

Init ==
    /\ phase = "Probing"
    /\ current = AssemblyA
    /\ path = <<AssemblyA>>
    /\ scope \in Scopes
    /\ initialScope = scope
    /\ hops = <<>>
    /\ lastDeclaration = NoOutcome
    /\ validated = TRUE
    /\ terminalKind = NoOutcome
    /\ terminalCause = NoOutcome
    /\ terminalAssembly = NoOutcome

FinishAt(assembly, kind, cause) ==
    /\ phase' = "Terminal"
    /\ terminalKind' = kind
    /\ terminalCause' = cause
    /\ terminalAssembly' = assembly
    /\ UNCHANGED
        <<current, path, initialScope, scope, hops, validated>>

Finish(kind, cause) == FinishAt(current, kind, cause)

ProbeDefined ==
    /\ phase = "Probing"
    /\ lastDeclaration' = "Defined"
    /\ IF /\ MutationMode = "ResolveAtStart"
          /\ Len(path) > 1
       THEN FinishAt(path[1], "Resolved", "Definition")
       ELSE Finish("Resolved", "Definition")

ProbeMissing ==
    /\ phase = "Probing"
    /\ lastDeclaration' = "Missing"
    /\ Finish("NotFound", "DeclarationMissing")

ProbeAmbiguous ==
    /\ phase = "Probing"
    /\ lastDeclaration' = "Ambiguous"
    /\ Finish("Ambiguous", "DeclarationAmbiguous")

ProbeRejected ==
    /\ phase = "Probing"
    /\ lastDeclaration' = "Rejected"
    /\ Finish("Rejected", "DeclarationRejected")

ProbeModuleExport ==
    /\ phase = "Probing"
    /\ lastDeclaration' = "ModuleExport"
    /\ Finish("Rejected", "ModuleExport")

ProbeForwarded(requestedScope) ==
    /\ phase = "Probing"
    /\ requestedScope \in Scopes
    /\ lastDeclaration' = "Forwarded"
    /\ IF MutationMode = "ResolveForwarder"
       THEN Finish("Resolved", "ForwarderShortcut")
       ELSE
            LET effectiveScope == NextScope(scope, requestedScope)
                nextHops == Append(
                    hops,
                    [source |-> current, scope |-> effectiveScope])
            IN
                IF Len(hops) = HopBudget
                THEN
                    /\ phase' = "Terminal"
                    /\ scope' = effectiveScope
                    /\ hops' = nextHops
                    /\ terminalKind' = "Rejected"
                    /\ terminalCause' = "HopBudget"
                    /\ terminalAssembly' = current
                    /\ UNCHANGED
                        <<current, path, initialScope, validated>>
                ELSE
                    /\ phase' = "Binding"
                    /\ scope' = effectiveScope
                    /\ hops' = nextHops
                    /\ UNCHANGED
                        <<current, path, initialScope, validated,
                          terminalKind, terminalCause, terminalAssembly>>

BindSelected(nextAssembly) ==
    /\ phase = "Binding"
    /\ nextAssembly \in Assemblies
    /\ IF /\ nextAssembly \in {path[index] : index \in 1..Len(path)}
          /\ MutationMode # "PermitCycle"
       THEN
            /\ Finish("Rejected", "Cycle")
            /\ UNCHANGED lastDeclaration
       ELSE
            /\ phase' = "Opening"
            /\ current' = nextAssembly
            /\ path' = Append(path, nextAssembly)
            /\ validated' = FALSE
            /\ UNCHANGED
                <<initialScope, scope, hops, lastDeclaration,
                  terminalKind, terminalCause, terminalAssembly>>

BindMissing ==
    /\ phase = "Binding"
    /\ UNCHANGED lastDeclaration
    /\ IF MutationMode = "CollapseBindingMiss"
       THEN Finish("NotFound", "BindingMissing")
       ELSE Finish("UnboundBinding", "BindingMissing")

BindUnavailable ==
    /\ phase = "Binding"
    /\ UNCHANGED lastDeclaration
    /\ Finish("Unavailable", "BindingUnavailable")

BindAmbiguous ==
    /\ phase = "Binding"
    /\ UNCHANGED lastDeclaration
    /\ Finish("Ambiguous", "BindingAmbiguous")

BindRejected ==
    /\ phase = "Binding"
    /\ UNCHANGED lastDeclaration
    /\ Finish("Rejected", "BindingRejected")

OpenSucceeded ==
    /\ phase = "Opening"
    /\ phase' = "Probing"
    /\ validated' = TRUE
    /\ UNCHANGED
        <<current, path, initialScope, scope, hops, lastDeclaration,
          terminalKind, terminalCause, terminalAssembly>>

OpenUnreadable ==
    /\ phase = "Opening"
    /\ UNCHANGED lastDeclaration
    /\ Finish("Rejected", "CandidateUnreadable")

OpenInvalidImage ==
    /\ phase = "Opening"
    /\ UNCHANGED lastDeclaration
    /\ IF MutationMode = "AcceptInvalidImage"
       THEN
            /\ phase' = "Probing"
            /\ validated' = FALSE
            /\ UNCHANGED
                <<current, path, initialScope, scope, hops, terminalKind,
                  terminalCause, terminalAssembly>>
       ELSE Finish("Rejected", "CandidateInvalidImage")

OpenResourceBudget ==
    /\ phase = "Opening"
    /\ UNCHANGED lastDeclaration
    /\ Finish("Rejected", "CandidateResourceBudget")

Advance ==
    \/ ProbeDefined
    \/ ProbeMissing
    \/ ProbeAmbiguous
    \/ ProbeRejected
    \/ ProbeModuleExport
    \/ \E requestedScope \in Scopes : ProbeForwarded(requestedScope)
    \/ \E nextAssembly \in Assemblies : BindSelected(nextAssembly)
    \/ BindMissing
    \/ BindUnavailable
    \/ BindAmbiguous
    \/ BindRejected
    \/ OpenSucceeded
    \/ OpenUnreadable
    \/ OpenInvalidImage
    \/ OpenResourceBudget

Next == Advance

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(Advance)

TypeOK ==
    /\ phase \in Phases
    /\ current \in Assemblies
    /\ path \in Seq(Assemblies)
    /\ Len(path) >= 1
    /\ initialScope \in Scopes
    /\ scope \in Scopes
    /\ hops \in Seq([source : Assemblies, scope : Scopes])
    /\ lastDeclaration \in DeclarationKinds \union {NoOutcome}
    /\ validated \in BOOLEAN
    /\ terminalKind \in TerminalKinds \union {NoOutcome}
    /\ terminalCause \in TerminalCauses \union {NoOutcome}
    /\ terminalAssembly \in Assemblies \union {NoOutcome}

CurrentMatchesPath ==
    current = path[Len(path)]

PhaseShapeIsCoherent ==
    /\ (phase = "Probing" => Len(path) = Len(hops) + 1)
    /\ (phase = "Binding" => Len(path) = Len(hops))
    /\ (phase = "Opening" => Len(path) = Len(hops) + 1)
    /\ (phase = "Terminal" =>
        Len(path) \in {Len(hops), Len(hops) + 1})

HopSourcesFollowSelectedPath ==
    \A index \in 1..Len(hops):
        hops[index].source = path[index]

CurrentScopeMatchesLastHop ==
    /\ (Len(hops) = 0 => scope = initialScope)
    /\ (Len(hops) > 0 => scope = hops[Len(hops)].scope)

ScopeNeverLoosens ==
    /\ ~(initialScope = "Platform"
         /\ Len(hops) > 0
         /\ hops[1].scope = "Any")
    /\ \A index \in 2..Len(hops):
        ~(hops[index - 1].scope = "Platform"
          /\ hops[index].scope = "Any")

SelectedPathHasNoCycle ==
    NoDuplicates(path)

HopBudgetIsObserved ==
    /\ Len(hops) <= HopBudget + 1
    /\ (Len(hops) > HopBudget =>
        /\ phase = "Terminal"
        /\ terminalCause = "HopBudget")

HopBudgetRetainsTerminalEvidence ==
    terminalCause = "HopBudget" =>
        /\ Len(hops) = HopBudget + 1
        /\ Len(path) = Len(hops)
        /\ hops[Len(hops)].source = current

TerminalOutcomeMatchesCause ==
    phase = "Terminal" =>
        terminalKind = ExpectedKind(terminalCause)

TerminalStateHasExactlyOneOutcome ==
    (phase = "Terminal")
        <=> /\ terminalKind \in TerminalKinds
            /\ terminalCause \in TerminalCauses
            /\ terminalAssembly \in Assemblies

ResolvedTerminalIsCurrent ==
    terminalKind = "Resolved" => terminalAssembly = current

ResolvedRequiresDefinedDeclaration ==
    terminalKind = "Resolved" => lastDeclaration = "Defined"

ResolvedRequiresValidatedCandidate ==
    terminalKind = "Resolved" => validated

ResolutionConverges == <>(phase = "Terminal")

=============================================================================
