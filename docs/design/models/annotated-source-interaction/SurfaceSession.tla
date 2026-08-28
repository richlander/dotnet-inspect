------------------------- MODULE SurfaceSession -------------------------
(***************************************************************************)
(* Design model of one Annotated Source workspace entry.                   *)
(*                                                                         *)
(* The model checks embedded/full mode transfer, independent full state,   *)
(* Finding detail, active-versus-rendered annotations, layered Escape, and *)
(* focus fallback. It says nothing about browser history, source geometry, *)
(* packet encoding, declaration text, identity construction, or ARIA.      *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals

CONSTANTS
  Findings,
  Annotatable,
  CSharpTargets,
  IlTargets,
  Nodes

NoValue == "none"

ASSUME /\ Findings # {}
       /\ Nodes # {}
       /\ NoValue \notin Findings \cup Nodes
       /\ Annotatable \subseteq Findings
       /\ CSharpTargets \subseteq Annotatable
       /\ IlTargets \subseteq Annotatable
       /\ CSharpTargets # {}
       /\ IlTargets \ CSharpTargets # {}
       /\ Findings \ Annotatable # {}

Modes == {"Embedded", "Full"}
Media == {"CSharp", "IL"}
PrimaryKinds == {"None", "Finding", "Node"}
OpenerKinds == {"None", "Chip", "Inspector"}
FocusKinds == {"None", "Detail", "Explore", "Chip", "Inspector", "Node"}
ReportedStates == {"Default", "All", "Clear", "Custom"}
Values == Findings \cup Nodes \cup {NoValue}

NoPrimary == [kind |-> "None", value |-> NoValue]
FindingPrimary(f) == [kind |-> "Finding", value |-> f]
NodePrimary(n) == [kind |-> "Node", value |-> n]

NoDetail == [finding |-> NoValue, opener |-> "None"]
FindingDetail(f, opener) == [finding |-> f, opener |-> opener]

NoFocus == [kind |-> "None", value |-> NoValue]
DetailFocus(f) == [kind |-> "Detail", value |-> f]
ExploreFocus == [kind |-> "Explore", value |-> NoValue]
ChipFocus(f) == [kind |-> "Chip", value |-> f]
InspectorFocus(f) == [kind |-> "Inspector", value |-> f]
NodeFocus(n) == [kind |-> "Node", value |-> n]

Rendered(set, shownMedia) ==
  {f \in set :
      (f \in CSharpTargets /\ "CSharp" \in shownMedia)
      \/ (f \in IlTargets /\ "IL" \in shownMedia)}

Reported(set, defaultSet) ==
  IF set = defaultSet
  THEN "Default"
  ELSE IF set = Annotatable
       THEN "All"
       ELSE IF set = {}
            THEN "Clear"
            ELSE "Custom"

Transferable(primary, defaultSet) ==
  /\ primary.kind = "Finding"
  /\ primary.value \in defaultSet
  /\ primary.value \in CSharpTargets

VARIABLES
  defaults,
  mode,
  fullInitialized,
  embeddedPrimary,
  embeddedDetail,
  fullPrimary,
  fullDetail,
  active,
  visibleMedia,
  reported,
  focus,
  eventPulse,
  exploreWitness,
  exitWitness,
  embeddedEscapeWitness,
  mediaWitness,
  focusWitness

vars ==
  <<defaults, mode, fullInitialized, embeddedPrimary, embeddedDetail,
    fullPrimary, fullDetail, active, visibleMedia, reported, focus,
    eventPulse, exploreWitness, exitWitness, embeddedEscapeWitness,
    mediaWitness, focusWitness>>

CurrentDetail ==
  IF mode = "Embedded" THEN embeddedDetail ELSE fullDetail

ChipAvailable(f, currentMode, activeSet, shownMedia) ==
  IF currentMode = "Embedded"
  THEN f \in defaults /\ f \in CSharpTargets
  ELSE f \in Rendered(activeSet, shownMedia)

RestoredFocus(detail, currentMode, activeSet, shownMedia) ==
  IF detail.opener = "Chip"
     /\ ChipAvailable(detail.finding, currentMode, activeSet, shownMedia)
  THEN ChipFocus(detail.finding)
  ELSE InspectorFocus(detail.finding)

FocusIsValid ==
  CASE focus.kind = "None" -> focus = NoFocus
    [] focus.kind = "Detail" ->
         /\ CurrentDetail # NoDetail
         /\ focus.value = CurrentDetail.finding
    [] focus.kind = "Explore" ->
         /\ mode = "Embedded"
         /\ focus = ExploreFocus
    [] focus.kind = "Chip" ->
         /\ focus.value \in Findings
         /\ ChipAvailable(focus.value, mode, active, visibleMedia)
    [] focus.kind = "Inspector" ->
         /\ mode = "Full"
         /\ focus.value \in Findings
    [] focus.kind = "Node" ->
         /\ mode = "Full"
         /\ focus.value \in Nodes
         /\ fullPrimary = NodePrimary(focus.value)
    [] OTHER -> FALSE

Init ==
  /\ defaults \in SUBSET Annotatable
  /\ mode \in Modes
  /\ fullInitialized = (mode = "Full")
  /\ embeddedPrimary = NoPrimary
  /\ embeddedDetail = NoDetail
  /\ fullPrimary = NoPrimary
  /\ fullDetail = NoDetail
  /\ active = defaults
  /\ visibleMedia = {"CSharp"}
  /\ reported = Reported(active, defaults)
  /\ focus = NoFocus
  /\ eventPulse = FALSE
  /\ exploreWitness = TRUE
  /\ exitWitness = TRUE
  /\ embeddedEscapeWitness = TRUE
  /\ mediaWitness = TRUE
  /\ focusWitness = TRUE

OpenEmbeddedFinding(f) ==
  /\ mode = "Embedded"
  /\ f \in defaults
  /\ f \in CSharpTargets
  /\ embeddedPrimary' = FindingPrimary(f)
  /\ embeddedDetail' = FindingDetail(f, "Chip")
  /\ focus' = DetailFocus(f)
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<defaults, mode, fullInitialized, fullPrimary, fullDetail,
                 active, visibleMedia, reported, exploreWitness, exitWitness,
                 embeddedEscapeWitness, mediaWitness, focusWitness>>

OpenFullFinding(f, opener) ==
  /\ mode = "Full"
  /\ f \in Findings
  /\ opener \in {"Chip", "Inspector"}
  /\ opener = "Chip" => f \in Rendered(active, visibleMedia)
  /\ fullPrimary' = FindingPrimary(f)
  /\ fullDetail' = FindingDetail(f, opener)
  /\ focus' = DetailFocus(f)
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<defaults, mode, fullInitialized, embeddedPrimary,
                 embeddedDetail, active, visibleMedia, reported,
                 exploreWitness, exitWitness, embeddedEscapeWitness,
                 mediaWitness, focusWitness>>

SelectFullNode(n) ==
  /\ mode = "Full"
  /\ n \in Nodes
  /\ fullPrimary' = NodePrimary(n)
  /\ fullDetail' = NoDetail
  /\ focus' = NodeFocus(n)
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<defaults, mode, fullInitialized, embeddedPrimary,
                 embeddedDetail, active, visibleMedia, reported,
                 exploreWitness, exitWitness, embeddedEscapeWitness,
                 mediaWitness, focusWitness>>

Explore ==
  /\ mode = "Embedded"
  /\ LET first == ~fullInitialized
         nextPrimary == IF first THEN embeddedPrimary ELSE fullPrimary
         nextDetail == IF first THEN embeddedDetail ELSE fullDetail
     IN /\ mode' = "Full"
        /\ fullInitialized' = TRUE
        /\ fullPrimary' = nextPrimary
        /\ fullDetail' = nextDetail
        /\ active' = IF first THEN defaults ELSE active
        /\ visibleMedia' = IF first THEN {"CSharp"} ELSE visibleMedia
        /\ reported' = Reported(active', defaults)
        /\ focus' =
             IF nextDetail # NoDetail
             THEN DetailFocus(nextDetail.finding)
             ELSE NoFocus
        /\ exploreWitness' =
             /\ exploreWitness
             /\ mode' = "Full"
             /\ IF first
                THEN /\ fullPrimary' = embeddedPrimary
                     /\ fullDetail' = embeddedDetail
                     /\ active' = defaults
                     /\ visibleMedia' = {"CSharp"}
                ELSE /\ fullPrimary' = fullPrimary
                     /\ fullDetail' = fullDetail
                     /\ active' = active
                     /\ visibleMedia' = visibleMedia
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<defaults, embeddedPrimary, embeddedDetail, exitWitness,
                 embeddedEscapeWitness, mediaWitness, focusWitness>>

LeaveFull ==
  /\ mode = "Full"
  /\ LET transferred ==
           IF Transferable(fullPrimary, defaults)
           THEN fullPrimary
           ELSE NoPrimary
     IN /\ mode' = "Embedded"
        /\ embeddedPrimary' = transferred
        /\ embeddedDetail' = NoDetail
        /\ fullPrimary' = fullPrimary
        /\ fullDetail' = NoDetail
        /\ active' = active
        /\ visibleMedia' = visibleMedia
        /\ reported' = reported
        /\ focus' = ExploreFocus
        /\ exitWitness' =
             /\ exitWitness
             /\ mode' = "Embedded"
             /\ embeddedPrimary' = transferred
             /\ embeddedDetail' = NoDetail
             /\ fullPrimary' = fullPrimary
             /\ fullDetail' = NoDetail
             /\ active' = active
             /\ visibleMedia' = visibleMedia
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<defaults, fullInitialized, exploreWitness,
                 embeddedEscapeWitness, mediaWitness, focusWitness>>

CloseCurrentDetail ==
  /\ CurrentDetail # NoDetail
  /\ LET restored ==
           RestoredFocus(CurrentDetail, mode, active, visibleMedia)
     IN /\ IF mode = "Embedded"
           THEN /\ embeddedDetail' = NoDetail
                /\ UNCHANGED fullDetail
           ELSE /\ fullDetail' = NoDetail
                /\ UNCHANGED embeddedDetail
        /\ focus' = restored
        /\ focusWitness' =
             /\ focusWitness
             /\ focus' = restored
             /\ IF CurrentDetail.opener = "Chip"
                   /\ ChipAvailable(CurrentDetail.finding, mode,
                                    active, visibleMedia)
                THEN focus' = ChipFocus(CurrentDetail.finding)
                ELSE focus' = InspectorFocus(CurrentDetail.finding)
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<defaults, mode, fullInitialized, embeddedPrimary,
                 fullPrimary, active, visibleMedia, reported, exploreWitness,
                 exitWitness, embeddedEscapeWitness, mediaWitness>>

EmbeddedEscapeFallsThrough ==
  /\ mode = "Embedded"
  /\ embeddedDetail = NoDetail
  /\ UNCHANGED <<defaults, mode, fullInitialized, embeddedPrimary,
                 embeddedDetail, fullPrimary, fullDetail, active,
                 visibleMedia, reported, focus>>
  /\ eventPulse' = ~eventPulse
  /\ embeddedEscapeWitness' =
       /\ embeddedEscapeWitness
       /\ mode' = mode
       /\ embeddedPrimary' = embeddedPrimary
       /\ fullPrimary' = fullPrimary
       /\ fullDetail' = fullDetail
       /\ active' = active
       /\ visibleMedia' = visibleMedia
       /\ focus' = focus
  /\ UNCHANGED <<exploreWitness, exitWitness, mediaWitness, focusWitness>>

SetDefault ==
  /\ mode = "Full"
  /\ active' = defaults
  /\ reported' = Reported(active', defaults)
  /\ fullPrimary' = NoPrimary
  /\ fullDetail' = NoDetail
  /\ focus' = NoFocus
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<defaults, mode, fullInitialized, embeddedPrimary,
                 embeddedDetail, visibleMedia, exploreWitness, exitWitness,
                 embeddedEscapeWitness, mediaWitness, focusWitness>>

SetAll ==
  /\ mode = "Full"
  /\ active' = Annotatable
  /\ reported' = Reported(active', defaults)
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<defaults, mode, fullInitialized, embeddedPrimary,
                 embeddedDetail, fullPrimary, fullDetail, visibleMedia,
                 focus, exploreWitness, exitWitness, embeddedEscapeWitness,
                 mediaWitness, focusWitness>>

ClearAll ==
  /\ mode = "Full"
  /\ active' = {}
  /\ reported' = Reported(active', defaults)
  /\ fullPrimary' = NoPrimary
  /\ fullDetail' = NoDetail
  /\ focus' = NoFocus
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<defaults, mode, fullInitialized, embeddedPrimary,
                 embeddedDetail, visibleMedia, exploreWitness, exitWitness,
                 embeddedEscapeWitness, mediaWitness, focusWitness>>

ToggleAnnotation(f) ==
  /\ mode = "Full"
  /\ f \in Annotatable
  /\ LET removing == f \in active
         nextActive ==
           IF removing THEN active \ {f} ELSE active \cup {f}
         removesPrimary ==
           /\ removing
           /\ fullPrimary = FindingPrimary(f)
         nextPrimary ==
           IF removesPrimary THEN NoPrimary ELSE fullPrimary
         nextDetail ==
           IF removesPrimary THEN NoDetail ELSE fullDetail
         nextFocus ==
           IF removesPrimary
              \/ (focus = ChipFocus(f) /\ f \notin nextActive)
           THEN NoFocus
           ELSE focus
     IN /\ active' = nextActive
        /\ reported' = Reported(nextActive, defaults)
        /\ fullPrimary' = nextPrimary
        /\ fullDetail' = nextDetail
        /\ focus' = nextFocus
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<defaults, mode, fullInitialized, embeddedPrimary,
                 embeddedDetail, visibleMedia, exploreWitness, exitWitness,
                 embeddedEscapeWitness, mediaWitness, focusWitness>>

ToggleMedium(medium) ==
  /\ mode = "Full"
  /\ medium \in Media
  /\ LET nextMedia ==
           IF medium \in visibleMedia
           THEN visibleMedia \ {medium}
           ELSE visibleMedia \cup {medium}
         nextFocus ==
           IF focus.kind = "Chip"
              /\ focus.value \notin Rendered(active, nextMedia)
           THEN NoFocus
           ELSE focus
     IN /\ visibleMedia' = nextMedia
        /\ active' = active
        /\ reported' = reported
        /\ fullPrimary' = fullPrimary
        /\ fullDetail' = fullDetail
        /\ focus' = nextFocus
        /\ mediaWitness' =
             /\ mediaWitness
             /\ active' = active
             /\ reported' = reported
             /\ fullPrimary' = fullPrimary
             /\ fullDetail' = fullDetail
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<defaults, mode, fullInitialized, embeddedPrimary,
                 embeddedDetail, exploreWitness, exitWitness,
                 embeddedEscapeWitness, focusWitness>>

Next ==
  \/ \E f \in Findings : OpenEmbeddedFinding(f)
  \/ \E f \in Findings, opener \in {"Chip", "Inspector"} :
       OpenFullFinding(f, opener)
  \/ \E n \in Nodes : SelectFullNode(n)
  \/ Explore
  \/ LeaveFull
  \/ CloseCurrentDetail
  \/ EmbeddedEscapeFallsThrough
  \/ SetDefault
  \/ SetAll
  \/ ClearAll
  \/ \E f \in Annotatable : ToggleAnnotation(f)
  \/ \E medium \in Media : ToggleMedium(medium)

Spec == Init /\ [][Next]_vars

TypeOK ==
  /\ defaults \in SUBSET Annotatable
  /\ mode \in Modes
  /\ fullInitialized \in BOOLEAN
  /\ embeddedPrimary \in [kind : PrimaryKinds, value : Values]
  /\ embeddedDetail \in [finding : Findings \cup {NoValue},
                          opener : OpenerKinds]
  /\ fullPrimary \in [kind : PrimaryKinds, value : Values]
  /\ fullDetail \in [finding : Findings \cup {NoValue},
                      opener : OpenerKinds]
  /\ active \in SUBSET Annotatable
  /\ visibleMedia \in SUBSET Media
  /\ reported \in ReportedStates
  /\ focus \in [kind : FocusKinds, value : Values]
  /\ eventPulse \in BOOLEAN
  /\ exploreWitness \in BOOLEAN
  /\ exitWitness \in BOOLEAN
  /\ embeddedEscapeWitness \in BOOLEAN
  /\ mediaWitness \in BOOLEAN
  /\ focusWitness \in BOOLEAN

SelectionShapes ==
  /\ (embeddedPrimary.kind = "None") = (embeddedPrimary = NoPrimary)
  /\ (embeddedPrimary.kind = "Finding" =>
        embeddedPrimary.value \in Findings)
  /\ embeddedPrimary.kind # "Node"
  /\ (fullPrimary.kind = "None") = (fullPrimary = NoPrimary)
  /\ (fullPrimary.kind = "Finding" => fullPrimary.value \in Findings)
  /\ (fullPrimary.kind = "Node" => fullPrimary.value \in Nodes)

DetailMatchesPrimary ==
  /\ (embeddedDetail = NoDetail)
     \/ embeddedPrimary = FindingPrimary(embeddedDetail.finding)
  /\ (fullDetail = NoDetail)
     \/ fullPrimary = FindingPrimary(fullDetail.finding)

EmbeddedStateIsConstrained ==
  /\ (embeddedPrimary = NoPrimary)
     \/ /\ embeddedPrimary.kind = "Finding"
        /\ embeddedPrimary.value \in defaults
        /\ embeddedPrimary.value \in CSharpTargets
  /\ (embeddedDetail = NoDetail)
     \/ /\ embeddedDetail.finding \in defaults
        /\ embeddedDetail.finding \in CSharpTargets
        /\ embeddedDetail.opener = "Chip"

FullDetailExistsOnlyInFullMode ==
  mode = "Embedded" => fullDetail = NoDetail

ReportedStateIsDerived ==
  reported = Reported(active, defaults)

ExplorePreservesOrInitializesExactly == exploreWitness

ExitTransfersAndClosesExactly == exitWitness

EmbeddedEscapeIsStateNeutral == embeddedEscapeWitness

MediaChangesAreOrthogonal == mediaWitness

DetailClosureRestoresValidFocus == focusWitness

=============================================================================
