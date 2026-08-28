-------------------------- MODULE ViewerSession --------------------------
(***************************************************************************)
(* Design model of one Annotated Source embedded reader and modal viewer.  *)
(*                                                                         *)
(* The model checks viewer-local selection, Finding detail, active versus  *)
(* rendered annotations, exact opener restoration, non-empty media, modal  *)
(* opening and dismissal, and layered Escape. Shell modal composition,     *)
(* browser history, navigation authority, and packet state are not modeled. *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals

CONSTANTS
  Dual,
  IlOnly,
  Unanchored,
  DualCs,
  DualIl,
  IlOnlyIl,
  Node

Findings == {Dual, IlOnly, Unanchored}
Targets == {DualCs, DualIl, IlOnlyIl}
TargetPairs ==
  {<<DualCs, Dual>>, <<DualIl, Dual>>, <<IlOnlyIl, IlOnly>>}
CSharpTargets == {DualCs}
IlTargets == {DualIl, IlOnlyIl}
Nodes == {Node}

NoValue == "none"

ASSUME /\ Cardinality(Findings) = 3
       /\ Cardinality(Targets) = 3
       /\ Cardinality(Nodes) = 1
       /\ NoValue \notin Findings \cup Targets \cup Nodes
       /\ TargetPairs \subseteq Targets \X Findings
       /\ \A target \in Targets :
            Cardinality(
              {finding \in Findings :
                 <<target, finding>> \in TargetPairs}) = 1
       /\ CSharpTargets \subseteq Targets
       /\ IlTargets \subseteq Targets
       /\ CSharpTargets \cup IlTargets = Targets
       /\ CSharpTargets \cap IlTargets = {}
       /\ CSharpTargets # {}
       /\ IlTargets # {}
       /\ \E finding \in Findings :
            /\ \E target \in CSharpTargets :
                 <<target, finding>> \in TargetPairs
            /\ \E target \in IlTargets :
                 <<target, finding>> \in TargetPairs
       /\ \E finding \in Findings :
            ~\E target \in Targets :
                <<target, finding>> \in TargetPairs

Surfaces == {"Embedded", "Modal"}
Media == {"CSharp", "IL"}
PrimaryKinds == {"None", "Finding", "Node"}
OpenerKinds == {"None", "Chip", "Inspector"}
FocusKinds ==
  {"None", "Detail", "Explore", "ModalHeading", "Chip", "Inspector", "Node"}
ActionKinds ==
  {"Init", "OpenEmbeddedChip", "OpenModal", "DismissEscape",
   "DismissPointer", "OpenModalFinding", "SelectModalNode", "CloseDetail",
   "EmbeddedEscape", "SetDefault", "SetAll", "ClearAll",
   "ToggleAnnotation", "ToggleMedium"}
ReportedStates == {"Default", "All", "Clear", "Custom"}
Values == Findings \cup Targets \cup Nodes \cup {NoValue}

FindingOf(target) ==
  CHOOSE finding \in Findings :
    <<target, finding>> \in TargetPairs

FindingTargets(finding) ==
  {target \in Targets : <<target, finding>> \in TargetPairs}

Annotatable ==
  {finding \in Findings : FindingTargets(finding) # {}}

NoPrimary == [kind |-> "None", value |-> NoValue]
FindingPrimary(finding) == [kind |-> "Finding", value |-> finding]
NodePrimary(node) == [kind |-> "Node", value |-> node]

NoDetail ==
  [finding |-> NoValue, opener |-> "None", target |-> NoValue]
ChipDetail(finding, target) ==
  [finding |-> finding, opener |-> "Chip", target |-> target]
InspectorDetail(finding) ==
  [finding |-> finding, opener |-> "Inspector", target |-> NoValue]

NoFocus == [kind |-> "None", value |-> NoValue]
DetailFocus(finding) == [kind |-> "Detail", value |-> finding]
ExploreFocus == [kind |-> "Explore", value |-> NoValue]
ModalHeadingFocus == [kind |-> "ModalHeading", value |-> NoValue]
ChipFocus(target) == [kind |-> "Chip", value |-> target]
InspectorFocus(finding) == [kind |-> "Inspector", value |-> finding]
NodeFocus(node) == [kind |-> "Node", value |-> node]

VisibleTargets(activeSet, shownMedia) ==
  {target \in Targets :
      /\ FindingOf(target) \in activeSet
      /\ IF target \in CSharpTargets
         THEN "CSharp" \in shownMedia
         ELSE "IL" \in shownMedia}

Reported(activeSet, defaultSet) ==
  IF activeSet = defaultSet
  THEN "Default"
  ELSE IF activeSet = Annotatable
       THEN "All"
       ELSE IF activeSet = {}
            THEN "Clear"
            ELSE "Custom"

Transferable(primary, defaultSet) ==
  /\ primary.kind = "Finding"
  /\ primary.value \in defaultSet
  /\ FindingTargets(primary.value) \cap CSharpTargets # {}

VARIABLES
  defaults,
  surface,
  embeddedPrimary,
  embeddedDetail,
  modalPrimary,
  modalDetail,
  active,
  visibleMedia,
  reported,
  focus,
  expectedFocus,
  eventPulse,
  lastAction,
  escapeLayered

vars ==
  <<defaults, surface, embeddedPrimary, embeddedDetail, modalPrimary,
    modalDetail, active, visibleMedia, reported, focus, expectedFocus,
    eventPulse, lastAction, escapeLayered>>

CurrentDetail ==
  IF surface = "Embedded" THEN embeddedDetail ELSE modalDetail

ExactChipAvailable(target, currentSurface, activeSet, shownMedia) ==
  IF currentSurface = "Embedded"
  THEN /\ target \in CSharpTargets
       /\ FindingOf(target) \in defaults
  ELSE target \in VisibleTargets(activeSet, shownMedia)

RestoredDetailFocus(detail, currentSurface, activeSet, shownMedia) ==
  IF detail.opener = "Chip"
     /\ ExactChipAvailable(detail.target, currentSurface,
                           activeSet, shownMedia)
  THEN ChipFocus(detail.target)
  ELSE InspectorFocus(detail.finding)

FocusIsValid ==
  CASE focus.kind = "None" -> focus = NoFocus
    [] focus.kind = "Detail" ->
         /\ CurrentDetail # NoDetail
         /\ focus.value = CurrentDetail.finding
    [] focus.kind = "Explore" ->
         /\ surface = "Embedded"
         /\ focus = ExploreFocus
    [] focus.kind = "ModalHeading" ->
         /\ surface = "Modal"
         /\ focus = ModalHeadingFocus
    [] focus.kind = "Chip" ->
         /\ focus.value \in Targets
         /\ ExactChipAvailable(focus.value, surface, active, visibleMedia)
    [] focus.kind = "Inspector" ->
         /\ surface = "Modal"
         /\ focus.value \in Findings
    [] focus.kind = "Node" ->
         /\ surface = "Modal"
         /\ focus.value \in Nodes
         /\ modalPrimary = NodePrimary(focus.value)
    [] OTHER -> FALSE

Init ==
  /\ defaults \in SUBSET Annotatable
  /\ surface = "Embedded"
  /\ embeddedPrimary = NoPrimary
  /\ embeddedDetail = NoDetail
  /\ modalPrimary = NoPrimary
  /\ modalDetail = NoDetail
  /\ active = defaults
  /\ visibleMedia = {"CSharp"}
  /\ reported = Reported(active, defaults)
  /\ focus = NoFocus
  /\ expectedFocus = NoFocus
  /\ eventPulse = FALSE
  /\ lastAction = "Init"
  /\ escapeLayered = TRUE

OpenEmbeddedChip(target) ==
  /\ surface = "Embedded"
  /\ target \in CSharpTargets
  /\ FindingOf(target) \in defaults
  /\ embeddedPrimary' = FindingPrimary(FindingOf(target))
  /\ embeddedDetail' = ChipDetail(FindingOf(target), target)
  /\ focus' = DetailFocus(FindingOf(target))
  /\ expectedFocus' = DetailFocus(FindingOf(target))
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "OpenEmbeddedChip"
  /\ UNCHANGED <<defaults, surface, modalPrimary, modalDetail, active,
                 visibleMedia, reported, escapeLayered>>

OpenModal ==
  /\ surface = "Embedded"
  /\ surface' = "Modal"
  /\ embeddedDetail' = NoDetail
  /\ modalPrimary' =
       IF Transferable(embeddedPrimary, defaults)
       THEN embeddedPrimary
       ELSE NoPrimary
  /\ modalDetail' = NoDetail
  /\ active' = defaults
  /\ visibleMedia' = {"CSharp"}
  /\ reported' = Reported(active', defaults)
  /\ focus' = ModalHeadingFocus
  /\ expectedFocus' = ModalHeadingFocus
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "OpenModal"
  /\ UNCHANGED <<defaults, embeddedPrimary, escapeLayered>>

DismissModal ==
  /\ surface = "Modal"
  /\ surface' = "Embedded"
  /\ embeddedPrimary' =
       IF Transferable(modalPrimary, defaults)
       THEN modalPrimary
       ELSE NoPrimary
  /\ embeddedDetail' = NoDetail
  /\ modalDetail' = NoDetail
  /\ active' = active
  /\ visibleMedia' = visibleMedia
  /\ reported' = reported
  /\ focus' = ExploreFocus
  /\ eventPulse' = ~eventPulse
  /\ UNCHANGED <<defaults, modalPrimary>>

DismissModalByEscape ==
  /\ surface = "Modal"
  /\ modalDetail = NoDetail
  /\ DismissModal
  /\ expectedFocus' = ExploreFocus
  /\ lastAction' = "DismissEscape"
  /\ escapeLayered' = (escapeLayered /\ modalDetail = NoDetail)

DismissModalByPointer ==
  /\ surface = "Modal"
  /\ DismissModal
  /\ expectedFocus' = ExploreFocus
  /\ lastAction' = "DismissPointer"
  /\ UNCHANGED escapeLayered

OpenModalFinding(finding, opener, target) ==
  /\ surface = "Modal"
  /\ finding \in Findings
  /\ opener \in {"Chip", "Inspector"}
  /\ IF opener = "Chip"
     THEN /\ target \in VisibleTargets(active, visibleMedia)
          /\ FindingOf(target) = finding
     ELSE target = NoValue
  /\ modalPrimary' = FindingPrimary(finding)
  /\ modalDetail' =
       IF opener = "Chip"
       THEN ChipDetail(finding, target)
       ELSE InspectorDetail(finding)
  /\ focus' = DetailFocus(finding)
  /\ expectedFocus' = DetailFocus(finding)
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "OpenModalFinding"
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail, active,
                 visibleMedia, reported, escapeLayered>>

SelectModalNode(node) ==
  /\ surface = "Modal"
  /\ node \in Nodes
  /\ modalPrimary' = NodePrimary(node)
  /\ modalDetail' = NoDetail
  /\ focus' = NodeFocus(node)
  /\ expectedFocus' = NodeFocus(node)
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "SelectModalNode"
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail, active,
                 visibleMedia, reported, escapeLayered>>

CloseCurrentDetail ==
  /\ CurrentDetail # NoDetail
  /\ LET restored ==
           RestoredDetailFocus(CurrentDetail, surface, active, visibleMedia)
     IN /\ IF surface = "Embedded"
           THEN /\ embeddedDetail' = NoDetail
                /\ modalDetail' = modalDetail
           ELSE /\ modalDetail' = NoDetail
                /\ embeddedDetail' = embeddedDetail
        /\ focus' = restored
        /\ expectedFocus' = restored
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "CloseDetail"
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, modalPrimary, active,
                 visibleMedia, reported, escapeLayered>>

EmbeddedEscapeFallsThrough ==
  /\ surface = "Embedded"
  /\ embeddedDetail = NoDetail
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 modalPrimary, modalDetail, active, visibleMedia, reported,
                 focus>>
  /\ expectedFocus' = focus
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "EmbeddedEscape"
  /\ UNCHANGED escapeLayered

SetDefault ==
  /\ surface = "Modal"
  /\ active' = defaults
  /\ reported' = Reported(active', defaults)
  /\ modalPrimary' = NoPrimary
  /\ modalDetail' = NoDetail
  /\ focus' = NoFocus
  /\ expectedFocus' = NoFocus
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "SetDefault"
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 visibleMedia, escapeLayered>>

SetAll ==
  /\ surface = "Modal"
  /\ active' = Annotatable
  /\ reported' = Reported(active', defaults)
  /\ expectedFocus' = focus
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "SetAll"
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 modalPrimary, modalDetail, visibleMedia, focus,
                 escapeLayered>>

ClearAll ==
  /\ surface = "Modal"
  /\ active' = {}
  /\ reported' = Reported(active', defaults)
  /\ modalPrimary' = NoPrimary
  /\ modalDetail' = NoDetail
  /\ focus' = NoFocus
  /\ expectedFocus' = NoFocus
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "ClearAll"
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 visibleMedia, escapeLayered>>

ToggleAnnotation(finding) ==
  /\ surface = "Modal"
  /\ finding \in Annotatable
  /\ LET removing == finding \in active
         nextActive ==
           IF removing THEN active \ {finding} ELSE active \cup {finding}
         removesPrimary ==
           /\ removing
           /\ modalPrimary = FindingPrimary(finding)
         nextPrimary ==
           IF removesPrimary THEN NoPrimary ELSE modalPrimary
         nextDetail ==
           IF removesPrimary THEN NoDetail ELSE modalDetail
         nextFocus ==
           IF removesPrimary
           THEN InspectorFocus(finding)
           ELSE IF focus.kind = "Chip"
                   /\ focus.value \in FindingTargets(finding)
                   /\ finding \notin nextActive
                THEN NoFocus
                ELSE focus
     IN /\ active' = nextActive
        /\ reported' = Reported(nextActive, defaults)
        /\ modalPrimary' = nextPrimary
        /\ modalDetail' = nextDetail
        /\ focus' = nextFocus
        /\ expectedFocus' = nextFocus
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "ToggleAnnotation"
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 visibleMedia, escapeLayered>>

ToggleMedium(medium) ==
  /\ surface = "Modal"
  /\ medium \in Media
  /\ medium \notin visibleMedia \/ Cardinality(visibleMedia) > 1
  /\ LET nextMedia ==
           IF medium \in visibleMedia
           THEN visibleMedia \ {medium}
           ELSE visibleMedia \cup {medium}
         nextFocus ==
           IF focus.kind = "Chip"
              /\ focus.value \notin VisibleTargets(active, nextMedia)
           THEN NoFocus
           ELSE focus
     IN /\ visibleMedia' = nextMedia
        /\ active' = active
        /\ reported' = reported
        /\ modalPrimary' = modalPrimary
        /\ modalDetail' = modalDetail
        /\ focus' = nextFocus
        /\ expectedFocus' = nextFocus
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "ToggleMedium"
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 escapeLayered>>

Next ==
  \/ \E target \in Targets : OpenEmbeddedChip(target)
  \/ OpenModal
  \/ DismissModalByEscape
  \/ DismissModalByPointer
  \/ \E finding \in Findings,
        opener \in {"Chip", "Inspector"},
        target \in Targets \cup {NoValue} :
       OpenModalFinding(finding, opener, target)
  \/ \E node \in Nodes : SelectModalNode(node)
  \/ CloseCurrentDetail
  \/ EmbeddedEscapeFallsThrough
  \/ SetDefault
  \/ SetAll
  \/ ClearAll
  \/ \E finding \in Annotatable : ToggleAnnotation(finding)
  \/ \E medium \in Media : ToggleMedium(medium)

Spec == Init /\ [][Next]_vars

TypeOK ==
  /\ defaults \in SUBSET Annotatable
  /\ surface \in Surfaces
  /\ embeddedPrimary \in [kind : PrimaryKinds, value : Values]
  /\ embeddedDetail \in
       [finding : Findings \cup {NoValue},
        opener  : OpenerKinds,
        target  : Targets \cup {NoValue}]
  /\ modalPrimary \in [kind : PrimaryKinds, value : Values]
  /\ modalDetail \in
       [finding : Findings \cup {NoValue},
        opener  : OpenerKinds,
        target  : Targets \cup {NoValue}]
  /\ active \in SUBSET Annotatable
  /\ visibleMedia \in SUBSET Media
  /\ reported \in ReportedStates
  /\ focus \in [kind : FocusKinds, value : Values]
  /\ expectedFocus \in [kind : FocusKinds, value : Values]
  /\ eventPulse \in BOOLEAN
  /\ lastAction \in ActionKinds
  /\ escapeLayered \in BOOLEAN

PrimaryShapes ==
  /\ (embeddedPrimary.kind = "None") = (embeddedPrimary = NoPrimary)
  /\ (embeddedPrimary.kind = "Finding" =>
        embeddedPrimary.value \in Findings)
  /\ embeddedPrimary.kind # "Node"
  /\ (modalPrimary.kind = "None") = (modalPrimary = NoPrimary)
  /\ (modalPrimary.kind = "Finding" => modalPrimary.value \in Findings)
  /\ (modalPrimary.kind = "Node" => modalPrimary.value \in Nodes)

DetailShapes ==
  /\ (embeddedDetail = NoDetail)
     \/ /\ embeddedDetail.opener = "Chip"
        /\ embeddedDetail.target \in CSharpTargets
        /\ FindingOf(embeddedDetail.target) = embeddedDetail.finding
        /\ embeddedPrimary = FindingPrimary(embeddedDetail.finding)
  /\ (modalDetail = NoDetail)
     \/ /\ modalDetail.finding \in Findings
        /\ modalPrimary = FindingPrimary(modalDetail.finding)
        /\ IF modalDetail.opener = "Chip"
           THEN /\ modalDetail.target \in Targets
                /\ FindingOf(modalDetail.target) = modalDetail.finding
           ELSE /\ modalDetail.opener = "Inspector"
                /\ modalDetail.target = NoValue

EmbeddedStateIsConstrained ==
  /\ (embeddedPrimary = NoPrimary)
     \/ /\ embeddedPrimary.kind = "Finding"
        /\ embeddedPrimary.value \in defaults
        /\ FindingTargets(embeddedPrimary.value) \cap CSharpTargets # {}
  /\ (embeddedDetail = NoDetail)
     \/ embeddedDetail.finding \in defaults

ModalDetailExistsOnlyWhileOpen ==
  surface = "Embedded" => modalDetail = NoDetail

AtLeastOneMediumIsVisible ==
  visibleMedia # {}

ReportedStateIsDerived ==
  reported = Reported(active, defaults)

FocusMatchesTransition ==
  focus = expectedFocus

ModalOpeningIsFresh ==
  lastAction = "OpenModal" =>
    /\ surface = "Modal"
    /\ modalPrimary =
         IF Transferable(embeddedPrimary, defaults)
         THEN embeddedPrimary
         ELSE NoPrimary
    /\ modalDetail = NoDetail
    /\ active = defaults
    /\ visibleMedia = {"CSharp"}
    /\ focus = ModalHeadingFocus

ModalDismissalIsExact ==
  lastAction \in {"DismissEscape", "DismissPointer"} =>
    /\ surface = "Embedded"
    /\ embeddedPrimary =
         IF Transferable(modalPrimary, defaults)
         THEN modalPrimary
         ELSE NoPrimary
    /\ embeddedDetail = NoDetail
    /\ modalDetail = NoDetail
    /\ focus = ExploreFocus

EscapeCannotBypassDetail ==
  escapeLayered

=============================================================================
