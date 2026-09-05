---------------------- MODULE BrowserScopeRetirement ----------------------
EXTENDS Naturals, FiniteSets, TLC

CONSTANTS ScopeLimit, Fault

ASSUME /\ ScopeLimit \in 1..2
       /\ Fault \in {"None", "EarlyUncharge", "LatePublish",
                     "UnprotectedReturn", "ForgetFailure", "NeverSettle"}

\* Equal display labels do not collapse generation or selection identity.
Keys == {<<"pkg@version/producer/default", 1, 1>>,
         <<"pkg@version/producer/default", 2, 1>>,
         <<"pkg@version/producer/default", 1, 2>>}
Entries == 1..3
Callers == 1..3
NoEntry == 0
NoKey == <<"None", 0, 0>>
Live == {"Pending", "Ready"}
Retained == Live \cup {"Retiring", "Failed"}
Terminal == {"Closed", "Failed"}
Stages == {"Unused", "Opening", "Waiting", "Returning", "Using", "Done"}

VARIABLES entries, callers, published, failures, clock, witnesses
vars == <<entries, callers, published, failures, clock, witnesses>>

EmptyEntry ==
    [phase |-> "Unused", key |-> NoKey, factory |-> "Running",
     charged |-> FALSE, retired |-> FALSE, touched |-> 0,
     sharedCancellation |-> FALSE]
EmptyCaller ==
    [stage |-> "Unused", key |-> NoKey, entry |-> NoEntry, holds |-> FALSE]

Holders(e) == {c \in Callers : callers[c].entry = e /\ callers[c].holds}
Charged == {e \in Entries : entries[e].charged}
Idle == {e \in Entries : entries[e].phase = "Ready" /\ Holders(e) = {}}
Unused == {e \in Entries : entries[e].phase = "Unused"}
Eligible(k) == {e \in Entries : entries[e].key = k /\ entries[e].phase \in Live}
Opening == {c \in Callers : callers[c].stage = "Opening"}

Init ==
    /\ entries = [e \in Entries |-> EmptyEntry]
    /\ callers = [c \in Callers |-> EmptyCaller]
    /\ published = [k \in Keys |-> NoEntry]
    /\ failures = {}
    /\ clock = 0
    /\ witnesses = {}

Request(c, k) ==
    /\ callers[c].stage = "Unused"
    /\ callers' = [callers EXCEPT ![c] =
        [@ EXCEPT !.stage = "Opening", !.key = k]]
    /\ UNCHANGED <<entries, published, failures, clock, witnesses>>

Admit(c) ==
    /\ c \in Opening
    /\ Eligible(callers[c].key) = {}
    /\ Cardinality(Charged) < ScopeLimit
    /\ Unused # {}
    /\ LET e == CHOOSE candidate \in Unused :
                    \A other \in Unused : candidate <= other
       IN /\ entries' = [entries EXCEPT ![e] =
                 [EmptyEntry EXCEPT !.phase = "Pending",
                  !.key = callers[c].key, !.charged = TRUE,
                  !.touched = clock + 1]]
          /\ callers' = [callers EXCEPT ![c] =
                 [@ EXCEPT !.stage = "Waiting", !.entry = e, !.holds = TRUE]]
          /\ witnesses' =
                 IF Cardinality({old \in Entries :
                        entries[old].phase # "Unused"}) >= ScopeLimit
                    /\ \E old \in Entries : entries[old].phase = "Closed"
                 THEN witnesses \cup {"ReusedAfterCleanup"}
                 ELSE witnesses
    /\ clock' = clock + 1
    /\ UNCHANGED <<published, failures>>

Join(c, e) ==
    /\ c \in Opening
    /\ e \in Eligible(callers[c].key)
    /\ callers' = [callers EXCEPT ![c] =
         [@ EXCEPT !.entry = e, !.holds = TRUE,
          !.stage = IF entries[e].phase = "Pending" THEN "Waiting" ELSE "Returning"]]
    /\ entries' = [entries EXCEPT ![e].touched = clock + 1]
    /\ clock' = clock + 1
    /\ UNCHANGED <<published, failures, witnesses>>

RetiredEntry(e) ==
    [entries[e] EXCEPT !.phase = "Retiring", !.retired = TRUE,
     !.charged = IF Fault = "EarlyUncharge" THEN FALSE ELSE @]

Unpublish(e) ==
    [k \in Keys |-> IF published[k] = e THEN NoEntry ELSE published[k]]

\* Explicit removal differs from capacity eviction: protected users keep
\* their lease, and a pending factory remains owned even after cancellation.
Remove(e) ==
    /\ entries[e].phase \in Live
    /\ entries' = [entries EXCEPT ![e] = RetiredEntry(e)]
    /\ published' = Unpublish(e)
    /\ UNCHANGED <<callers, failures, clock, witnesses>>

Evict(e) ==
    /\ Opening # {}
    /\ Cardinality(Charged) = ScopeLimit
    /\ e \in Idle
    /\ \A other \in Idle : entries[e].touched <= entries[other].touched
    /\ Remove(e)

Cancel(c) ==
    /\ callers[c].stage \in {"Opening", "Waiting", "Returning", "Using"}
    /\ LET e == callers[c].entry
           shared == e # NoEntry /\ Cardinality(Holders(e)) > 1
           abandon == e # NoEntry /\ entries[e].phase = "Pending"
                      /\ Holders(e) = {c}
       IN /\ callers' = [callers EXCEPT ![c] =
                 [@ EXCEPT !.stage = "Done", !.holds = FALSE]]
          /\ entries' =
                 IF abandon THEN [entries EXCEPT ![e] = RetiredEntry(e)]
                 ELSE IF shared
                      THEN [entries EXCEPT ![e].sharedCancellation = TRUE]
                      ELSE entries
          /\ published' = IF abandon THEN Unpublish(e) ELSE published
    /\ UNCHANGED <<failures, clock, witnesses>>

\* Factory completion is an external event. A retiring factory cannot grant
\* a new result; its still-protected waiters receive failure and release.
Finish(e, result) ==
    /\ entries[e].phase \in {"Pending", "Retiring"}
    /\ entries[e].factory = "Running"
    /\ LET accept == entries[e].phase = "Pending" /\ result = "Succeeded"
           late == entries[e].phase = "Retiring"
           waiting == {c \in Callers :
                            callers[c].entry = e /\ callers[c].stage = "Waiting"}
       IN /\ entries' = [entries EXCEPT ![e] =
                 [IF accept THEN @ ELSE RetiredEntry(e)
                  EXCEPT !.factory = result,
                         !.phase = IF accept THEN "Ready" ELSE "Retiring"]]
          /\ callers' = [c \in Callers |->
                 IF c \in waiting THEN
                     [callers[c] EXCEPT
                      !.stage = IF accept THEN "Returning" ELSE "Done",
                      !.holds = accept /\ Fault # "UnprotectedReturn"]
                 ELSE callers[c]]
          /\ published' =
                 IF accept \/ (late /\ Fault = "LatePublish")
                 THEN [published EXCEPT ![entries[e].key] = e]
                 ELSE Unpublish(e)
          /\ witnesses' = witnesses
                 \cup (IF accept /\ waiting # {} /\ entries[e].sharedCancellation
                       THEN {"JoinedCancellation"} ELSE {})
                 \cup (IF late /\ Eligible(entries[e].key) # {}
                       THEN {"LateAfterReplacement"} ELSE {})
    /\ UNCHANGED <<failures, clock>>

Deliver(c) ==
    /\ callers[c].stage = "Returning"
    /\ callers' = [callers EXCEPT ![c].stage = "Using"]
    /\ UNCHANGED <<entries, published, failures, clock, witnesses>>

Release(c) ==
    /\ callers[c].stage = "Using"
    /\ callers' = [callers EXCEPT ![c] =
         [@ EXCEPT !.stage = "Done", !.holds = FALSE]]
    /\ UNCHANGED <<entries, published, failures, clock, witnesses>>

\* This is the host-observed terminal close outcome, including group and
\* artifact cleanup failures, NOT Dispose() or task completion alone.
\* Lower-owner receipt and cleanup transitions are intentionally not copied.
Settle(e, result) ==
    /\ Fault # "NeverSettle"
    /\ entries[e].phase = "Retiring"
    /\ entries[e].factory # "Running"
    /\ Holders(e) = {}
    /\ entries' = [entries EXCEPT ![e] =
         [@ EXCEPT !.phase = IF result = "Succeeded" THEN "Closed" ELSE "Failed",
          !.charged = result = "Failed" /\ Fault # "ForgetFailure"]]
    /\ failures' = IF result = "Failed" THEN failures \cup {e} ELSE failures
    /\ witnesses' = IF result = "Failed"
                    THEN witnesses \cup {"CleanupFailure"} ELSE witnesses
    /\ UNCHANGED <<callers, published, clock>>

Refuse(c) ==
    /\ c \in Opening
    /\ Eligible(callers[c].key) = {}
    /\ Cardinality(Charged) = ScopeLimit
    /\ Idle = {}
    /\ ~\E e \in Entries : entries[e].phase = "Retiring"
    /\ callers' = [callers EXCEPT ![c].stage = "Done"]
    /\ UNCHANGED <<entries, published, failures, clock, witnesses>>

FinishAny(e) == \E result \in {"Succeeded", "Failed"} : Finish(e, result)
SettleAny(e) == \E result \in {"Succeeded", "Failed"} : Settle(e, result)

Next ==
    \/ \E c \in Callers, k \in Keys : Request(c, k)
    \/ \E c \in Callers : Admit(c) \/ Cancel(c) \/ Deliver(c) \/ Release(c) \/ Refuse(c)
    \/ \E c \in Callers, e \in Entries : Join(c, e)
    \/ \E e \in Entries : Remove(e) \/ Evict(e) \/ FinishAny(e) \/ SettleAny(e)

Spec == Init /\ [][Next]_vars
FairSpec ==
    /\ Spec
    /\ \A e \in Entries : WF_vars(FinishAny(e)) /\ WF_vars(SettleAny(e))
    /\ \A c \in Callers : WF_vars(Deliver(c)) /\ WF_vars(Release(c))

TypeOK ==
    /\ entries \in [Entries ->
         [phase : {"Unused", "Pending", "Ready", "Retiring", "Closed", "Failed"},
          key : Keys \cup {NoKey}, factory : {"Running", "Succeeded", "Failed"},
          charged : BOOLEAN, retired : BOOLEAN, touched : 0..3,
          sharedCancellation : BOOLEAN]]
    /\ callers \in [Callers ->
         [stage : Stages, key : Keys \cup {NoKey},
          entry : Entries \cup {NoEntry}, holds : BOOLEAN]]
    /\ published \in [Keys -> Entries \cup {NoEntry}]
    /\ failures \subseteq Entries
    /\ clock \in 0..3
    /\ witnesses \subseteq {"ReusedAfterCleanup", "JoinedCancellation",
                            "LateAfterReplacement", "CleanupFailure"}

CapacityBound ==
    /\ Cardinality(Charged) <= ScopeLimit
    /\ Cardinality({e \in Entries : entries[e].phase \in Retained}) <= ScopeLimit
ChargeCoversOwnership ==
    \A e \in Entries : entries[e].phase \in Retained => entries[e].charged
ExactSingleFlight ==
    \A k \in Keys : Cardinality(Eligible(k)) <= 1
PublicationIsCurrent ==
    \A k \in Keys : published[k] # NoEntry =>
        /\ entries[published[k]].phase = "Ready"
        /\ entries[published[k]].key = k
        /\ ~entries[published[k]].retired
ProtectedUse ==
    \A c \in Callers : callers[c].stage \in {"Waiting", "Returning", "Using"} =>
        /\ callers[c].holds
        /\ entries[callers[c].entry].charged
        /\ entries[callers[c].entry].phase \in
              (IF callers[c].stage = "Waiting"
               THEN {"Pending", "Retiring"} ELSE {"Ready", "Retiring"})
        /\ entries[callers[c].entry].key = callers[c].key
RetirementIsIrreversible ==
    \A e \in Entries : entries[e].retired =>
        entries[e].phase \in {"Retiring", "Closed", "Failed"}
FailureIsQuarantined ==
    \A e \in Entries : entries[e].phase = "Failed" =>
        entries[e].charged /\ e \in failures
Safety ==
    TypeOK /\ CapacityBound /\ ChargeCoversOwnership /\ ExactSingleFlight
    /\ PublicationIsCurrent /\ ProtectedUse /\ RetirementIsIrreversible
    /\ FailureIsQuarantined
RetirementTerminates ==
    \A e \in Entries : (entries[e].phase = "Retiring")
                       ~> (entries[e].phase \in Terminal)

NeverJoinedCancellation == "JoinedCancellation" \notin witnesses
NeverLateReplacement == "LateAfterReplacement" \notin witnesses
NeverCapacityReuse == "ReusedAfterCleanup" \notin witnesses
NeverCleanupFailure == "CleanupFailure" \notin witnesses
=============================================================================
