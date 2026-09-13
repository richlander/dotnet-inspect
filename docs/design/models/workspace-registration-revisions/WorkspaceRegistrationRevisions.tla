-------------------- MODULE WorkspaceRegistrationRevisions --------------------
EXTENDS Naturals, Sequences, FiniteSets

CONSTANTS Profile, Fault, DefaultArray, NullEntry, None, Malformed

LibraryA == [arm |-> "Library",
             source |-> <<"System.Text.Json", "11.0.0-preview.7.26381.103">>]
LibraryB == [arm |-> "Library", source |-> <<"System.Text.Json", "10.0.0">>]
PrefixA == [arm |-> "Prefix", source |-> "Microsoft.Extensions."]
PrefixB == [arm |-> "Prefix", source |-> "microsoft.extensions."]
PrefixPlatform == [arm |-> "Prefix", source |-> "platform"]
EcoA == [arm |-> "Ecosystem", id |-> "platform",
         instance |-> "declaration-a", display |-> "Platform"]
EcoB == [arm |-> "Ecosystem", id |-> "platform",
         instance |-> "declaration-b", display |-> "Platform"]
EcoOther == [arm |-> "Ecosystem", id |-> "other",
             instance |-> "declaration-other", display |-> "Platform"]
Registrations ==
    {LibraryA, LibraryB, PrefixA, PrefixB, PrefixPlatform, EcoA, EcoB, EcoOther}
UnknownArm == [arm |-> "Unknown"]
Baseline == <<LibraryA, PrefixA, EcoA>>
Reordered == <<PrefixA, LibraryA, EcoA>>
Reissued == <<PrefixA, LibraryA, EcoB>>
Clients == {1, 2}
Scenarios ==
    {"Values", "Prefix", "Library", "ArmKeys", "Display",
     "Duplicates", "Malformed", "Expected", "Race", "Empty",
     "InitialDuplicate", "InitialInvalid"}

ASSUME /\ Profile \in Scenarios \cup {"All"}
       /\ Fault \in {"None", "IgnoreStale", "IgnoreForeign", "CloseAtRead",
                    "NoOpChurn", "CollapseEcosystem", "AllowDuplicates"}

DuplicateKey(r) ==
    <<r.arm, IF r.arm = "Ecosystem" THEN r.id ELSE r.source>>
WellFormed(s) ==
    IF s = DefaultArray THEN FALSE
    ELSE \A i \in 1..Len(s) : s[i] \in Registrations
Valid(s) ==
    IF ~WellFormed(s) THEN FALSE
    ELSE \A i, j \in 1..Len(s) :
        i # j => DuplicateKey(s[i]) # DuplicateKey(s[j])

InitialChoices(s) ==
    CASE s = "Empty" -> {<<>>}
      [] s = "Prefix" -> {<<PrefixA>>}
      [] s = "Library" -> {<<LibraryA>>}
      [] s = "ArmKeys" -> {<<PrefixPlatform, EcoA>>}
      [] s = "Display" -> {<<EcoA>>}
      [] s = "InitialDuplicate" ->
            {<<LibraryA, LibraryA>>, <<PrefixA, PrefixA>>, <<EcoA, EcoB>>}
      [] s = "InitialInvalid" -> {DefaultArray, <<NullEntry>>, <<UnknownArm>>}
      [] OTHER -> {Baseline}

Request(e, s) == [expected |-> e, desired |-> s]
Program(s, c) ==
    IF c = 2
    THEN IF s = "Race" THEN <<Request("Read", <<PrefixB>>)>> ELSE <<>>
    ELSE CASE s = "Values" ->
                  <<Request("Read", Baseline), Request("Read", Reordered),
                    Request("Read", Reissued), Request("Read", <<>>),
                    Request("Read", Baseline)>>
           [] s = "Prefix" ->
                  <<Request("Read", <<PrefixB>>), Request("Read", <<PrefixB>>)>>
           [] s = "Library" ->
                  <<Request("Read", <<LibraryB>>), Request("Read", <<LibraryB>>)>>
           [] s = "ArmKeys" -> <<Request("Read", <<PrefixPlatform, EcoA>>)>>
           [] s = "Display" -> <<Request("Read", <<EcoA, EcoOther>>)>>
           [] s = "Duplicates" ->
                  <<Request("Read", <<LibraryA, LibraryA>>),
                    Request("Read", <<PrefixA, PrefixA>>),
                    Request("Read", <<EcoA, EcoB>>)>>
           [] s = "Malformed" ->
                  <<Request("Read", DefaultArray), Request("Read", <<NullEntry>>),
                    Request("Read", <<UnknownArm>>)>>
           [] s = "Expected" ->
                  <<Request("Null", DefaultArray),
                    Request("Malformed", <<NullEntry>>),
                    Request("Foreign", <<>>),
                    Request("Foreign", DefaultArray),
                    Request("Read", <<>>),
                    Request("Initial", DefaultArray),
                    Request("Initial", <<>>)>>
           [] s = "Race" -> <<Request("Read", <<LibraryB>>)>>
           [] s = "Empty" ->
                  <<Request("Read", <<>>), Request("Read", <<LibraryA>>)>>
           [] OTHER -> <<>>

Revision(w, id, s) == [workspace |-> w, identity |-> id, registrations |-> s]
UnrelatedState ==
    [packageRevision |-> "package-initial", artifactEpoch |-> "artifact-initial",
     participants |-> {"admitted-content"}, acquisitions |-> 0, evictions |-> 0]

VARIABLES scenario, mode, initial, phase, history, pc, pending, observed,
          reads, attempts, unrelated
vars == <<scenario, mode, initial, phase, history, pc, pending, observed,
          reads, attempts, unrelated>>

Current == history[Len(history)]
Foreign == Revision("ForeignWorkspace", 0, initial)
Expected(c) ==
    CASE Program(scenario, c)[pc[c]].expected = "Null" -> None
      [] Program(scenario, c)[pc[c]].expected = "Malformed" -> Malformed
      [] Program(scenario, c)[pc[c]].expected = "Foreign" -> Foreign
      [] Program(scenario, c)[pc[c]].expected = "Initial" -> history[1]
      [] OTHER -> observed[c].revision
Issued(e) == e \in {history[i] : i \in 1..Len(history)} \cup {Foreign}

Init ==
    /\ scenario \in IF Profile = "All" THEN Scenarios ELSE {Profile}
    /\ mode \in {"Sync", "Async"}
    /\ initial \in InitialChoices(scenario)
    /\ phase = "Unconstructed"
    /\ history = <<>>
    /\ pc = [c \in Clients |-> 1]
    /\ pending = [c \in Clients |-> FALSE]
    /\ observed = [c \in Clients |-> None]
    /\ reads = <<>>
    /\ attempts = <<>>
    /\ unrelated = UnrelatedState

Construct ==
    /\ phase = "Unconstructed"
    /\ phase' = IF Valid(initial) THEN "Open" ELSE "ConstructionRejected"
    /\ history' = IF Valid(initial)
                  THEN <<Revision("Workspace", 0, initial)>> ELSE <<>>
    /\ UNCHANGED <<scenario, mode, initial, pc, pending, observed,
                   reads, attempts, unrelated>>

Read(c) ==
    /\ phase \in {"Open", "Closing", "Closed"}
    /\ pc[c] <= Len(Program(scenario, c))
    /\ ~pending[c]
    /\ LET result == [atPhase |-> phase, revision |-> Current,
                      outcome |-> IF phase = "Open" THEN "Current"
                                  ELSE "Unavailable"]
       IN /\ observed' = [observed EXCEPT ![c] = result]
          /\ reads' = Append(reads, result)
    /\ pending' = [pending EXCEPT ![c] = TRUE]
    /\ UNCHANGED <<scenario, mode, initial, phase, history, pc, attempts, unrelated>>

\* This mutation intentionally discards exact ecosystem instance correspondence.
Collapsed(s) == [i \in 1..Len(s) |-> DuplicateKey(s[i])]

Replace(c) ==
    /\ phase \in {"Open", "Closing", "Closed"}
    /\ pending[c]
    /\ LET e == Expected(c)
           desired == Program(scenario, c)[pc[c]].desired
           available == IF Fault = "CloseAtRead"
                        THEN observed[c].atPhase = "Open" ELSE phase = "Open"
           equal == IF Fault = "CollapseEcosystem" /\ Valid(desired)
                    THEN Collapsed(desired) = Collapsed(Current.registrations)
                    ELSE desired = Current.registrations
           outcome ==
               IF ~available THEN "Unavailable"
               ELSE IF ~Issued(e) THEN "RejectedExpected"
               ELSE IF e.workspace # "Workspace" /\ Fault # "IgnoreForeign"
                    THEN "RejectedForeign"
               ELSE IF e.identity # Current.identity /\ Fault # "IgnoreStale"
                    THEN "RejectedStale"
               ELSE IF ~WellFormed(desired)
                       \/ (~Valid(desired) /\ Fault # "AllowDuplicates")
                    THEN "RejectedCandidate"
               ELSE IF equal /\ Fault # "NoOpChurn" THEN "NoEffect"
               ELSE "Committed"
           after == IF outcome = "Committed"
                    THEN Revision("Workspace", Len(history), desired) ELSE Current
           receipt == [before |-> Current, after |-> after, expected |-> e,
                       desired |-> desired, atPhase |-> phase, outcome |-> outcome]
       IN /\ history' = IF outcome = "Committed" THEN Append(history, after)
                        ELSE history
          /\ attempts' = Append(attempts, receipt)
    /\ pc' = [pc EXCEPT ![c] = @ + 1]
    /\ pending' = [pending EXCEPT ![c] = FALSE]
    /\ UNCHANGED <<scenario, mode, initial, phase, observed, reads, unrelated>>

\* Environmental availability only: no resource-release protocol is modeled.
BeginClose ==
    /\ phase = "Open"
    /\ phase' = "Closing"
    /\ UNCHANGED <<scenario, mode, initial, history, pc, pending, observed,
                   reads, attempts, unrelated>>
FinishClose ==
    /\ phase = "Closing"
    /\ phase' = "Closed"
    /\ UNCHANGED <<scenario, mode, initial, history, pc, pending, observed,
                   reads, attempts, unrelated>>

Next == Construct \/ BeginClose \/ FinishClose
        \/ (\E c \in Clients : Read(c) \/ Replace(c))
Spec == Init /\ [][Next]_vars

TypeOK ==
    /\ scenario \in Scenarios
    /\ mode \in {"Sync", "Async"}
    /\ phase \in {"Unconstructed", "ConstructionRejected", "Open", "Closing", "Closed"}
    /\ Len(history) <= 6
    /\ pc \in [Clients -> 1..8]
    /\ pending \in [Clients -> BOOLEAN]
    /\ \A i \in 1..Len(history) :
        /\ history[i].workspace = "Workspace"
        /\ history[i].identity \in 0..5
        /\ Valid(history[i].registrations)

ConstructionIsComplete ==
    /\ (phase \in {"Unconstructed", "ConstructionRejected"}) = (history = <<>>)
    /\ (phase = "ConstructionRejected" => ~Valid(initial))
    /\ (Len(history) > 0 =>
        /\ Valid(initial)
        /\ history[1] = Revision("Workspace", 0, initial))

ReadCurrency ==
    \A n \in 1..Len(reads) :
        LET r == reads[n] IN
        /\ r.revision \in {history[i] : i \in 1..Len(history)}
        /\ r.outcome = IF r.atPhase = "Open" THEN "Current" ELSE "Unavailable"

RevisionAssociation ==
    \A i, j \in 1..Len(history) :
        history[i].identity = history[j].identity => i = j

RequiredOutcome(a) ==
    IF a.atPhase # "Open" THEN "Unavailable"
    ELSE IF a.expected \in {None, Malformed} THEN "RejectedExpected"
    ELSE IF a.expected.workspace # a.before.workspace THEN "RejectedForeign"
    ELSE IF a.expected.identity # a.before.identity THEN "RejectedStale"
    ELSE IF ~Valid(a.desired) THEN "RejectedCandidate"
    ELSE IF a.desired = a.before.registrations THEN "NoEffect"
    ELSE "Committed"

RequestResults ==
    \A n \in 1..Len(attempts) :
        attempts[n].outcome = RequiredOutcome(attempts[n])

AtomicPublication ==
    \A n \in 1..Len(attempts) :
        LET a == attempts[n] IN
        IF a.outcome = "Committed"
        THEN /\ a.after.workspace = a.before.workspace
             /\ a.after.identity # a.before.identity
             /\ a.after.registrations = a.desired
        ELSE a.after = a.before

RetainedSnapshots ==
    \A n \in 1..Len(attempts) :
        /\ attempts[n].before \in {history[i] : i \in 1..Len(history)}
        /\ attempts[n].after \in {history[i] : i \in 1..Len(history)}

SingleWinnerPerExpectedRevision ==
    \A i, j \in 1..Len(attempts) :
        (i # j /\ attempts[i].outcome = "Committed"
               /\ attempts[j].outcome = "Committed") =>
            <<attempts[i].expected.workspace, attempts[i].expected.identity>> #
            <<attempts[j].expected.workspace, attempts[j].expected.identity>>

RegistrationOnly == unrelated = UnrelatedState
Safety == TypeOK /\ ConstructionIsComplete /\ ReadCurrency /\ RevisionAssociation
          /\ RequestResults /\ AtomicPublication /\ RetainedSnapshots
          /\ SingleWinnerPerExpectedRevision /\ RegistrationOnly
=============================================================================
