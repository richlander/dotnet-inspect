------------------------ MODULE EntryRestoration ------------------------
(***************************************************************************)
(* Design model of Annotated Source state in browser history entries.      *)
(*                                                                         *)
(* The model checks replace-versus-push behavior, sticky mode with fresh   *)
(* destination-local state, forward-history truncation, and exact Back and *)
(* Forward restoration. Subject-navigation resolution and stale-result    *)
(* authority belong to Inspection Subject Navigation and are atomic here.  *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, Sequences

CONSTANTS
  Members,
  MaxEntries,
  MaxLocalRevision

ASSUME /\ Cardinality(Members) > 1
       /\ MaxEntries \in Nat \ {0, 1}
       /\ MaxLocalRevision \in Nat \ {0}

Modes == {"Embedded", "Full"}

Entry(id, member, mode, embeddedRevision, fullRevision) ==
  [ id               |-> id,
    member           |-> member,
    mode             |-> mode,
    embeddedRevision |-> embeddedRevision,
    fullRevision     |-> fullRevision ]

FreshEntry(id, member, mode) == Entry(id, member, mode, 0, 0)

WithMode(entry, nextMode) ==
  [entry EXCEPT !.mode = nextMode]

Edited(entry) ==
  IF entry.mode = "Embedded"
  THEN [entry EXCEPT !.embeddedRevision = @ + 1]
  ELSE [entry EXCEPT !.fullRevision = @ + 1]

VARIABLES
  entries,
  cursor,
  visible,
  nextId,
  eventPulse,
  localWitness,
  replaceWitness,
  navigationWitness,
  restorationWitness,
  failureWitness

vars ==
  <<entries, cursor, visible, nextId, eventPulse, localWitness,
    replaceWitness, navigationWitness, restorationWitness, failureWitness>>

Init ==
  /\ \E member \in Members, initialMode \in Modes :
       LET first == FreshEntry(1, member, initialMode)
       IN /\ entries = <<first>>
          /\ cursor = 1
          /\ visible = first
  /\ nextId = 2
  /\ eventPulse = FALSE
  /\ localWitness = TRUE
  /\ replaceWitness = TRUE
  /\ navigationWitness = TRUE
  /\ restorationWitness = TRUE
  /\ failureWitness = TRUE

EditCurrentEntry ==
  /\ IF visible.mode = "Embedded"
     THEN visible.embeddedRevision < MaxLocalRevision
     ELSE visible.fullRevision < MaxLocalRevision
  /\ LET updated == Edited(visible)
         updatedEntries == [entries EXCEPT ![cursor] = updated]
     IN /\ visible' = updated
        /\ entries' = updatedEntries
        /\ cursor' = cursor
        /\ nextId' = nextId
        /\ localWitness' =
             /\ localWitness
             /\ Len(entries') = Len(entries)
             /\ cursor' = cursor
             /\ visible'.id = visible.id
             /\ visible'.member = visible.member
             /\ visible'.mode = visible.mode
             /\ \A i \in DOMAIN entries :
                  i # cursor => entries'[i] = entries[i]
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<replaceWitness, navigationWitness, restorationWitness,
                 failureWitness>>

ReplaceCurrentMode(nextMode) ==
  /\ nextMode \in Modes
  /\ nextMode # visible.mode
  /\ LET updated == WithMode(visible, nextMode)
         updatedEntries == [entries EXCEPT ![cursor] = updated]
     IN /\ visible' = updated
        /\ entries' = updatedEntries
        /\ cursor' = cursor
        /\ nextId' = nextId
        /\ replaceWitness' =
             /\ replaceWitness
             /\ Len(entries') = Len(entries)
             /\ cursor' = cursor
             /\ visible'.id = visible.id
             /\ visible'.member = visible.member
             /\ visible'.embeddedRevision = visible.embeddedRevision
             /\ visible'.fullRevision = visible.fullRevision
             /\ \A i \in DOMAIN entries :
                  i # cursor => entries'[i] = entries[i]
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<localWitness, navigationWitness, restorationWitness,
                 failureWitness>>

NavigateToMember(member) ==
  /\ nextId <= MaxEntries
  /\ member \in Members \ {visible.member}
  /\ LET destination == FreshEntry(nextId, member, visible.mode)
         retainedPrefix == SubSeq(entries, 1, cursor)
         nextEntries == Append(retainedPrefix, destination)
     IN /\ entries' = nextEntries
        /\ cursor' = cursor + 1
        /\ visible' = destination
        /\ nextId' = nextId + 1
        /\ navigationWitness' =
             /\ navigationWitness
             /\ Len(entries') = cursor + 1
             /\ cursor' = cursor + 1
             /\ \A i \in 1..cursor : entries'[i] = entries[i]
             /\ visible'.id = nextId
             /\ visible'.member = member
             /\ visible'.mode = visible.mode
             /\ visible'.embeddedRevision = 0
             /\ visible'.fullRevision = 0
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<localWitness, replaceWitness, restorationWitness,
                 failureWitness>>

NavigateBack ==
  /\ cursor > 1
  /\ entries' = entries
  /\ cursor' = cursor - 1
  /\ visible' = entries[cursor - 1]
  /\ nextId' = nextId
  /\ restorationWitness' =
       /\ restorationWitness
       /\ visible' = entries'[cursor']
       /\ entries' = entries
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<localWitness, replaceWitness, navigationWitness,
                 failureWitness>>

NavigateForward ==
  /\ cursor < Len(entries)
  /\ entries' = entries
  /\ cursor' = cursor + 1
  /\ visible' = entries[cursor + 1]
  /\ nextId' = nextId
  /\ restorationWitness' =
       /\ restorationWitness
       /\ visible' = entries'[cursor']
       /\ entries' = entries
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<localWitness, replaceWitness, navigationWitness,
                 failureWitness>>

NavigationFails(member) ==
  /\ member \in Members \ {visible.member}
  /\ UNCHANGED <<entries, cursor, visible, nextId>>
  /\ eventPulse' = ~eventPulse
  /\ failureWitness' =
       /\ failureWitness
       /\ entries' = entries
       /\ cursor' = cursor
       /\ visible' = visible
       /\ nextId' = nextId
  /\ UNCHANGED <<localWitness, replaceWitness, navigationWitness,
                 restorationWitness>>

Next ==
  \/ EditCurrentEntry
  \/ \E nextMode \in Modes : ReplaceCurrentMode(nextMode)
  \/ \E member \in Members : NavigateToMember(member)
  \/ NavigateBack
  \/ NavigateForward
  \/ \E member \in Members : NavigationFails(member)

Spec == Init /\ [][Next]_vars

EntryType ==
  [ id               : 1..MaxEntries,
    member           : Members,
    mode             : Modes,
    embeddedRevision : 0..MaxLocalRevision,
    fullRevision     : 0..MaxLocalRevision ]

TypeOK ==
  /\ entries \in Seq(EntryType)
  /\ Len(entries) \in 1..MaxEntries
  /\ cursor \in 1..Len(entries)
  /\ visible \in EntryType
  /\ nextId \in 2..(MaxEntries + 1)
  /\ eventPulse \in BOOLEAN
  /\ localWitness \in BOOLEAN
  /\ replaceWitness \in BOOLEAN
  /\ navigationWitness \in BOOLEAN
  /\ restorationWitness \in BOOLEAN
  /\ failureWitness \in BOOLEAN

VisibleEntryIsCurrent ==
  visible = entries[cursor]

EntryIdsAreUnique ==
  /\ \A left, right \in DOMAIN entries :
       left # right => entries[left].id # entries[right].id
  /\ \A i \in DOMAIN entries : entries[i].id < nextId

LocalStateChangesDoNotNavigate == localWitness

ModeChangesReplaceCurrentEntry == replaceWitness

SuccessfulNavigationIsFreshAndSticky == navigationWitness

BackAndForwardRestoreExactEntries == restorationWitness

FailedNavigationRetainsHistory == failureWitness

=============================================================================
