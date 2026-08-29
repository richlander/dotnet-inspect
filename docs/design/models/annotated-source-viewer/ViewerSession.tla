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
  DualCsSibling,
  DualIl,
  IlOnlyIl,
  Node

Findings == {Dual, IlOnly, Unanchored}
Targets == {DualCs, DualCsSibling, DualIl, IlOnlyIl}
TargetPairs ==
  {<<DualCs, Dual>>, <<DualCsSibling, Dual>>, <<DualIl, Dual>>,
   <<IlOnlyIl, IlOnly>>}
CSharpTargets == {DualCs, DualCsSibling}
IlTargets == {DualIl, IlOnlyIl}
Nodes == {Node}

NoValue == "none"

ASSUME /\ Cardinality(Findings) = 3
       /\ Cardinality(Targets) = 4
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
            Cardinality(
              {target \in CSharpTargets :
                 <<target, finding>> \in TargetPairs}) = 2
       /\ \E finding \in Findings :
            ~\E target \in Targets :
                <<target, finding>> \in TargetPairs

Surfaces == {"Embedded", "Modal"}
Media == {"CSharp", "IL"}
SupportedMediaSets == {{"CSharp"}, Media}
PrimaryKinds == {"None", "Finding", "Node"}
OpenerKinds == {"None", "Chip", "Inspector"}
FocusKinds ==
  {"None", "Detail", "Explore", "ModalHeading", "Chip", "Inspector", "Node",
   "AnnotationSetControl", "AnnotationToggle", "MediumToggle",
   "CoordinateToggle"}
AnnotationSetControls == {"DefaultControl", "AllControl", "ClearControl"}
CoordinateControl == "CoordinatesControl"
ActionKinds ==
  {"Init", "OpenEmbeddedChip", "OpenModal", "DismissEscape",
   "DismissPointer", "OpenModalFinding", "SelectModalNode", "CloseDetail",
   "EmbeddedEscape", "SetDefault", "SetAll", "ClearAll",
   "ToggleAnnotation", "ToggleMedium", "ToggleCoordinates"}
ReportedStates == {"Default", "All", "Clear", "Custom"}
Values == Findings \cup Targets \cup Nodes \cup {NoValue}
FocusValues ==
  Values \cup Media \cup AnnotationSetControls \cup {CoordinateControl}
ControlValues ==
  Media \cup AnnotationSetControls \cup {CoordinateControl, NoValue}

FindingOf(target) ==
  CHOOSE finding \in Findings :
    <<target, finding>> \in TargetPairs

FindingTargets(finding) ==
  {target \in Targets : <<target, finding>> \in TargetPairs}

TargetMedium(target) ==
  IF target \in CSharpTargets THEN "CSharp" ELSE "IL"

AnnotatableFor(mediaSet) ==
  {finding \in Findings :
     \E target \in FindingTargets(finding) :
       TargetMedium(target) \in mediaSet}

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
AnnotationSetFocus(control) ==
  [kind |-> "AnnotationSetControl", value |-> control]
AnnotationToggleFocus(finding) ==
  [kind |-> "AnnotationToggle", value |-> finding]
MediumToggleFocus(medium) ==
  [kind |-> "MediumToggle", value |-> medium]
CoordinateToggleFocus ==
  [kind |-> "CoordinateToggle", value |-> CoordinateControl]

VisibleTargets(activeSet, shownMedia) ==
  {target \in Targets :
      /\ FindingOf(target) \in activeSet
      /\ IF target \in CSharpTargets
         THEN "CSharp" \in shownMedia
         ELSE "IL" \in shownMedia}

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
  supportedMedia,
  visibleMedia,
  coordinatesVisible,
  reported,
  focus,
  eventPulse,
  lastAction,
  escapeLayered,
  closedDetail,
  closedSurface,
  closedPrimary,
  closedActive,
  closedMedia,
  closedCoordinates,
  dismissedPrimary,
  openedFinding,
  openedOpener,
  openedTarget,
  openingEmbeddedPrimary,
  openingPriorActive,
  openingPriorMedia,
  openingPriorCoordinates,
  selectedNode,
  nodePriorActive,
  nodePriorMedia,
  nodePriorCoordinates,
  toggledFinding,
  togglePriorActive,
  togglePriorPrimary,
  togglePriorDetail,
  togglePriorMedia,
  togglePriorCoordinates,
  activatedControl,
  controlPriorActive,
  controlPriorPrimary,
  controlPriorDetail,
  controlPriorMedia,
  controlPriorCoordinates

vars ==
  <<defaults, surface, embeddedPrimary, embeddedDetail, modalPrimary,
    modalDetail, active, supportedMedia, visibleMedia, coordinatesVisible,
    reported, focus, eventPulse, lastAction, escapeLayered, closedDetail,
    closedSurface, closedPrimary, closedActive, closedMedia,
    closedCoordinates, dismissedPrimary, openedFinding, openedOpener,
    openedTarget, openingEmbeddedPrimary, openingPriorActive,
    openingPriorMedia, openingPriorCoordinates, selectedNode,
    nodePriorActive, nodePriorMedia, nodePriorCoordinates, toggledFinding,
    togglePriorActive, togglePriorPrimary, togglePriorDetail,
    togglePriorMedia, togglePriorCoordinates,
    activatedControl, controlPriorActive, controlPriorPrimary,
    controlPriorDetail, controlPriorMedia, controlPriorCoordinates>>

Annotatable ==
  AnnotatableFor(supportedMedia)

InspectorFindings ==
  Findings

Reported(activeSet, defaultSet) ==
  IF activeSet = defaultSet
  THEN "Default"
  ELSE IF activeSet = Annotatable
       THEN "All"
       ELSE IF activeSet = {}
            THEN "Clear"
            ELSE "Custom"

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
         /\ IF surface = "Embedded"
            THEN /\ focus.value \in CSharpTargets
                 /\ FindingOf(focus.value) \in defaults
            ELSE focus.value \in VisibleTargets(active, visibleMedia)
    [] focus.kind = "Inspector" ->
         /\ surface = "Modal"
         /\ focus.value \in Findings
    [] focus.kind = "Node" ->
         /\ surface = "Modal"
         /\ focus.value \in Nodes
         /\ modalPrimary = NodePrimary(focus.value)
    [] focus.kind = "AnnotationSetControl" ->
         /\ surface = "Modal"
         /\ focus.value \in AnnotationSetControls
    [] focus.kind = "AnnotationToggle" ->
         /\ surface = "Modal"
         /\ focus.value \in Annotatable
    [] focus.kind = "MediumToggle" ->
         /\ surface = "Modal"
         /\ focus.value \in supportedMedia
    [] focus.kind = "CoordinateToggle" ->
         /\ surface = "Modal"
         /\ focus = CoordinateToggleFocus
    [] OTHER -> FALSE

ClearCloseHistory ==
  /\ closedDetail' = NoDetail
  /\ closedSurface' = "Embedded"
  /\ closedPrimary' = NoPrimary
  /\ closedActive' = defaults
  /\ closedMedia' = {"CSharp"}
  /\ closedCoordinates' = FALSE

ClearDismissHistory ==
  dismissedPrimary' = NoPrimary

ClearOpenHistory ==
  /\ openedFinding' = NoValue
  /\ openedOpener' = "None"
  /\ openedTarget' = NoValue
  /\ openingEmbeddedPrimary' = NoPrimary
  /\ openingPriorActive' = defaults
  /\ openingPriorMedia' = {"CSharp"}
  /\ openingPriorCoordinates' = FALSE

ClearNodeHistory ==
  /\ selectedNode' = NoValue
  /\ nodePriorActive' = defaults
  /\ nodePriorMedia' = {"CSharp"}
  /\ nodePriorCoordinates' = FALSE

ClearToggleHistory ==
  /\ toggledFinding' = NoValue
  /\ togglePriorActive' = defaults
  /\ togglePriorPrimary' = NoPrimary
  /\ togglePriorDetail' = NoDetail
  /\ togglePriorMedia' = {"CSharp"}
  /\ togglePriorCoordinates' = FALSE

ClearControlHistory ==
  /\ activatedControl' = NoValue
  /\ controlPriorActive' = defaults
  /\ controlPriorPrimary' = NoPrimary
  /\ controlPriorDetail' = NoDetail
  /\ controlPriorMedia' = {"CSharp"}
  /\ controlPriorCoordinates' = FALSE

RecordControlHistory(control) ==
  /\ activatedControl' = control
  /\ controlPriorActive' = active
  /\ controlPriorPrimary' = modalPrimary
  /\ controlPriorDetail' = modalDetail
  /\ controlPriorMedia' = visibleMedia
  /\ controlPriorCoordinates' = coordinatesVisible

ClearHistory ==
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory

Init ==
  /\ supportedMedia \in SupportedMediaSets
  /\ defaults \in SUBSET Annotatable
  /\ surface = "Embedded"
  /\ embeddedPrimary = NoPrimary
  /\ embeddedDetail = NoDetail
  /\ modalPrimary = NoPrimary
  /\ modalDetail = NoDetail
  /\ active = defaults
  /\ visibleMedia = {"CSharp"}
  /\ coordinatesVisible = FALSE
  /\ reported = Reported(active, defaults)
  /\ focus = NoFocus
  /\ eventPulse = FALSE
  /\ lastAction = "Init"
  /\ escapeLayered = TRUE
  /\ closedDetail = NoDetail
  /\ closedSurface = "Embedded"
  /\ closedPrimary = NoPrimary
  /\ closedActive = defaults
  /\ closedMedia = {"CSharp"}
  /\ closedCoordinates = FALSE
  /\ dismissedPrimary = NoPrimary
  /\ openedFinding = NoValue
  /\ openedOpener = "None"
  /\ openedTarget = NoValue
  /\ openingEmbeddedPrimary = NoPrimary
  /\ openingPriorActive = defaults
  /\ openingPriorMedia = {"CSharp"}
  /\ openingPriorCoordinates = FALSE
  /\ selectedNode = NoValue
  /\ nodePriorActive = defaults
  /\ nodePriorMedia = {"CSharp"}
  /\ nodePriorCoordinates = FALSE
  /\ toggledFinding = NoValue
  /\ togglePriorActive = defaults
  /\ togglePriorPrimary = NoPrimary
  /\ togglePriorDetail = NoDetail
  /\ togglePriorMedia = {"CSharp"}
  /\ togglePriorCoordinates = FALSE
  /\ activatedControl = NoValue
  /\ controlPriorActive = defaults
  /\ controlPriorPrimary = NoPrimary
  /\ controlPriorDetail = NoDetail
  /\ controlPriorMedia = {"CSharp"}
  /\ controlPriorCoordinates = FALSE

OpenEmbeddedChip(target) ==
  /\ surface = "Embedded"
  /\ target \in CSharpTargets
  /\ FindingOf(target) \in defaults
  /\ embeddedPrimary' = FindingPrimary(FindingOf(target))
  /\ embeddedDetail' = ChipDetail(FindingOf(target), target)
  /\ focus' = DetailFocus(FindingOf(target))
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "OpenEmbeddedChip"
  /\ openedFinding' = FindingOf(target)
  /\ openedOpener' = "Chip"
  /\ openedTarget' = target
  /\ openingEmbeddedPrimary' = NoPrimary
  /\ openingPriorActive' = active
  /\ openingPriorMedia' = visibleMedia
  /\ openingPriorCoordinates' = coordinatesVisible
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory
  /\ UNCHANGED <<defaults, surface, modalPrimary, modalDetail, active,
                 supportedMedia, visibleMedia, coordinatesVisible, reported,
                 escapeLayered>>

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
  /\ coordinatesVisible' = FALSE
  /\ reported' = Reported(active', defaults)
  /\ focus' = ModalHeadingFocus
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "OpenModal"
  /\ openedFinding' = NoValue
  /\ openedOpener' = "None"
  /\ openedTarget' = NoValue
  /\ openingEmbeddedPrimary' = embeddedPrimary
  /\ openingPriorActive' = active
  /\ openingPriorMedia' = visibleMedia
  /\ openingPriorCoordinates' = coordinatesVisible
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory
  /\ UNCHANGED <<defaults, embeddedPrimary, supportedMedia, escapeLayered>>

DismissModal ==
  /\ surface = "Modal"
  /\ surface' = "Embedded"
  /\ embeddedPrimary' =
       IF Transferable(modalPrimary, defaults)
       THEN modalPrimary
       ELSE NoPrimary
  /\ embeddedDetail' = NoDetail
  /\ modalPrimary' = NoPrimary
  /\ modalDetail' = NoDetail
  /\ active' = defaults
  /\ visibleMedia' = {"CSharp"}
  /\ coordinatesVisible' = FALSE
  /\ reported' = Reported(active', defaults)
  /\ focus' = ExploreFocus
  /\ eventPulse' = ~eventPulse
  /\ dismissedPrimary' = modalPrimary
  /\ ClearCloseHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory
  /\ UNCHANGED <<defaults, supportedMedia>>

DismissModalByEscape ==
  /\ surface = "Modal"
  /\ modalDetail = NoDetail
  /\ DismissModal
  /\ lastAction' = "DismissEscape"
  /\ escapeLayered' = (escapeLayered /\ modalDetail = NoDetail)

DismissModalByPointer ==
  /\ surface = "Modal"
  /\ DismissModal
  /\ lastAction' = "DismissPointer"
  /\ UNCHANGED escapeLayered

OpenModalFinding(finding, opener, target) ==
  /\ surface = "Modal"
  /\ finding \in Findings
  /\ opener \in {"Chip", "Inspector"}
  /\ IF opener = "Chip"
     THEN /\ target \in VisibleTargets(active, visibleMedia)
          /\ FindingOf(target) = finding
     ELSE /\ target = NoValue
          /\ finding \in InspectorFindings
  /\ modalPrimary' = FindingPrimary(finding)
  /\ modalDetail' =
       IF opener = "Chip"
       THEN ChipDetail(finding, target)
       ELSE InspectorDetail(finding)
  /\ focus' = DetailFocus(finding)
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "OpenModalFinding"
  /\ openedFinding' = finding
  /\ openedOpener' = opener
  /\ openedTarget' = target
  /\ openingEmbeddedPrimary' = NoPrimary
  /\ openingPriorActive' = active
  /\ openingPriorMedia' = visibleMedia
  /\ openingPriorCoordinates' = coordinatesVisible
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail, active,
                 supportedMedia, visibleMedia, coordinatesVisible, reported,
                 escapeLayered>>

SelectModalNode(node) ==
  /\ surface = "Modal"
  /\ node \in Nodes
  /\ modalPrimary' = NodePrimary(node)
  /\ modalDetail' = NoDetail
  /\ focus' = NodeFocus(node)
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "SelectModalNode"
  /\ selectedNode' = node
  /\ nodePriorActive' = active
  /\ nodePriorMedia' = visibleMedia
  /\ nodePriorCoordinates' = coordinatesVisible
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail, active,
                 supportedMedia, visibleMedia, coordinatesVisible, reported,
                 escapeLayered>>

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
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "CloseDetail"
  /\ closedDetail' = CurrentDetail
  /\ closedSurface' = surface
  /\ closedPrimary' =
       IF surface = "Embedded" THEN embeddedPrimary ELSE modalPrimary
  /\ closedActive' = active
  /\ closedMedia' = visibleMedia
  /\ closedCoordinates' = coordinatesVisible
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ ClearControlHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, modalPrimary, active,
                 supportedMedia, visibleMedia, coordinatesVisible, reported,
                 escapeLayered>>

EmbeddedEscapeFallsThrough ==
  /\ surface = "Embedded"
  /\ embeddedDetail = NoDetail
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 modalPrimary, modalDetail, active, supportedMedia, visibleMedia,
                 coordinatesVisible, reported, focus>>
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "EmbeddedEscape"
  /\ ClearHistory
  /\ escapeLayered' = (escapeLayered /\ embeddedDetail = NoDetail)

SetDefault ==
  /\ surface = "Modal"
  /\ active' = defaults
  /\ reported' = Reported(active', defaults)
  /\ modalPrimary' = NoPrimary
  /\ modalDetail' = NoDetail
  /\ visibleMedia' = visibleMedia
  /\ coordinatesVisible' = coordinatesVisible
  /\ focus' = AnnotationSetFocus("DefaultControl")
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "SetDefault"
  /\ RecordControlHistory("DefaultControl")
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 supportedMedia, escapeLayered>>

SetAll ==
  /\ surface = "Modal"
  /\ active' = Annotatable
  /\ reported' = Reported(active', defaults)
  /\ modalPrimary' = modalPrimary
  /\ modalDetail' = modalDetail
  /\ visibleMedia' = visibleMedia
  /\ coordinatesVisible' = coordinatesVisible
  /\ focus' = AnnotationSetFocus("AllControl")
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "SetAll"
  /\ RecordControlHistory("AllControl")
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 supportedMedia, escapeLayered>>

ClearAll ==
  /\ surface = "Modal"
  /\ active' = {}
  /\ reported' = Reported(active', defaults)
  /\ modalPrimary' = NoPrimary
  /\ modalDetail' = NoDetail
  /\ visibleMedia' = visibleMedia
  /\ coordinatesVisible' = coordinatesVisible
  /\ focus' = AnnotationSetFocus("ClearControl")
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "ClearAll"
  /\ RecordControlHistory("ClearControl")
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 supportedMedia, escapeLayered>>

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
     IN /\ active' = nextActive
        /\ reported' = Reported(nextActive, defaults)
        /\ modalPrimary' = nextPrimary
        /\ modalDetail' = nextDetail
        /\ focus' = AnnotationToggleFocus(finding)
        /\ toggledFinding' = finding
        /\ togglePriorActive' = active
        /\ togglePriorPrimary' = modalPrimary
        /\ togglePriorDetail' = modalDetail
        /\ togglePriorMedia' = visibleMedia
        /\ togglePriorCoordinates' = coordinatesVisible
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "ToggleAnnotation"
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearControlHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
                 supportedMedia, visibleMedia, coordinatesVisible,
                 escapeLayered>>

ToggleMedium(medium) ==
  /\ surface = "Modal"
  /\ medium \in supportedMedia
  /\ LET nextMedia ==
           IF medium \in visibleMedia
              /\ Cardinality(visibleMedia) = 1
           THEN visibleMedia
           ELSE IF medium \in visibleMedia
                THEN visibleMedia \ {medium}
                ELSE visibleMedia \cup {medium}
     IN /\ visibleMedia' = nextMedia
        /\ active' = active
        /\ coordinatesVisible' = coordinatesVisible
        /\ reported' = reported
        /\ modalPrimary' = modalPrimary
        /\ modalDetail' = modalDetail
        /\ focus' = MediumToggleFocus(medium)
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "ToggleMedium"
  /\ RecordControlHistory(medium)
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
          supportedMedia, escapeLayered>>

ToggleCoordinates ==
  /\ surface = "Modal"
  /\ coordinatesVisible' = ~coordinatesVisible
  /\ active' = active
  /\ visibleMedia' = visibleMedia
  /\ reported' = reported
  /\ modalPrimary' = modalPrimary
  /\ modalDetail' = modalDetail
  /\ focus' = CoordinateToggleFocus
  /\ eventPulse' = ~eventPulse
  /\ lastAction' = "ToggleCoordinates"
  /\ RecordControlHistory(CoordinateControl)
  /\ ClearCloseHistory
  /\ ClearDismissHistory
  /\ ClearOpenHistory
  /\ ClearNodeHistory
  /\ ClearToggleHistory
  /\ UNCHANGED <<defaults, surface, embeddedPrimary, embeddedDetail,
          supportedMedia, escapeLayered>>

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
  \/ \E medium \in supportedMedia : ToggleMedium(medium)
  \/ ToggleCoordinates

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
  /\ supportedMedia \in SupportedMediaSets
  /\ visibleMedia \in SUBSET Media
  /\ visibleMedia \subseteq supportedMedia
  /\ coordinatesVisible \in BOOLEAN
  /\ reported \in ReportedStates
  /\ focus \in [kind : FocusKinds, value : FocusValues]
  /\ eventPulse \in BOOLEAN
  /\ lastAction \in ActionKinds
  /\ escapeLayered \in BOOLEAN
  /\ closedDetail \in
       [finding : Findings \cup {NoValue},
        opener  : OpenerKinds,
        target  : Targets \cup {NoValue}]
  /\ closedSurface \in Surfaces
  /\ closedPrimary \in [kind : PrimaryKinds, value : Values]
  /\ closedActive \in SUBSET Annotatable
  /\ closedMedia \in SUBSET Media
  /\ closedCoordinates \in BOOLEAN
  /\ dismissedPrimary \in [kind : PrimaryKinds, value : Values]
  /\ openedFinding \in Findings \cup {NoValue}
  /\ openedOpener \in OpenerKinds
  /\ openedTarget \in Targets \cup {NoValue}
  /\ openingEmbeddedPrimary \in [kind : PrimaryKinds, value : Values]
  /\ openingPriorActive \in SUBSET Annotatable
  /\ openingPriorMedia \in SUBSET Media
  /\ openingPriorCoordinates \in BOOLEAN
  /\ selectedNode \in Nodes \cup {NoValue}
  /\ nodePriorActive \in SUBSET Annotatable
  /\ nodePriorMedia \in SUBSET Media
  /\ nodePriorCoordinates \in BOOLEAN
  /\ toggledFinding \in Findings \cup {NoValue}
  /\ togglePriorActive \in SUBSET Annotatable
  /\ togglePriorPrimary \in [kind : PrimaryKinds, value : Values]
  /\ togglePriorDetail \in
       [finding : Findings \cup {NoValue},
        opener  : OpenerKinds,
        target  : Targets \cup {NoValue}]
  /\ togglePriorMedia \in SUBSET Media
  /\ togglePriorCoordinates \in BOOLEAN
  /\ activatedControl \in ControlValues
  /\ controlPriorActive \in SUBSET Annotatable
  /\ controlPriorPrimary \in [kind : PrimaryKinds, value : Values]
  /\ controlPriorDetail \in
       [finding : Findings \cup {NoValue},
        opener  : OpenerKinds,
        target  : Targets \cup {NoValue}]
  /\ controlPriorMedia \in SUBSET Media
  /\ controlPriorCoordinates \in BOOLEAN

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

EmbeddedDetailExistsOnlyWhileEmbedded ==
  surface = "Modal" => embeddedDetail = NoDetail

CoordinatesExistOnlyWhileModal ==
  surface = "Embedded" => ~coordinatesVisible

AtLeastOneMediumIsVisible ==
  /\ visibleMedia # {}
  /\ visibleMedia \subseteq supportedMedia

AnnotatableUniverseIsSupported ==
  \A finding \in Findings :
    (finding \in Annotatable) =
      (\E target \in FindingTargets(finding) :
         TargetMedium(target) \in supportedMedia)

InspectorInventoryIsComplete ==
  /\ InspectorFindings = Findings
  /\ Unanchored \in InspectorFindings

ReportedStateIsDerived ==
  CASE active = defaults ->
         reported = "Default"
    [] active # defaults /\ active = Annotatable ->
         reported = "All"
    [] active # defaults /\ active # Annotatable /\ active = {} ->
         reported = "Clear"
    [] OTHER ->
         reported = "Custom"

ModalAlwaysHasFocus ==
  surface = "Modal" => focus # NoFocus

DetailClosureOutcomeIsExact ==
  lastAction = "CloseDetail" =>
    LET exactChipWasAvailable ==
          /\ closedDetail.opener = "Chip"
          /\ IF closedSurface = "Embedded"
             THEN /\ closedDetail.target \in CSharpTargets
                  /\ FindingOf(closedDetail.target) \in defaults
             ELSE closedDetail.target \in
                    VisibleTargets(closedActive, closedMedia)
        expectedFocus ==
          IF exactChipWasAvailable
          THEN ChipFocus(closedDetail.target)
          ELSE InspectorFocus(closedDetail.finding)
    IN /\ surface = closedSurface
       /\ CurrentDetail = NoDetail
       /\ active = closedActive
       /\ visibleMedia = closedMedia
       /\ coordinatesVisible = closedCoordinates
       /\ IF closedSurface = "Embedded"
          THEN embeddedPrimary = closedPrimary
          ELSE modalPrimary = closedPrimary
       /\ focus = expectedFocus

FindingOpeningIsExact ==
  lastAction \in {"OpenEmbeddedChip", "OpenModalFinding"} =>
    IF lastAction = "OpenEmbeddedChip"
    THEN /\ surface = "Embedded"
         /\ openedOpener = "Chip"
         /\ embeddedPrimary = FindingPrimary(openedFinding)
         /\ embeddedDetail = ChipDetail(openedFinding, openedTarget)
         /\ active = openingPriorActive
         /\ visibleMedia = openingPriorMedia
         /\ coordinatesVisible = openingPriorCoordinates
         /\ focus = DetailFocus(openedFinding)
    ELSE /\ surface = "Modal"
         /\ modalPrimary = FindingPrimary(openedFinding)
         /\ modalDetail =
              IF openedOpener = "Chip"
              THEN ChipDetail(openedFinding, openedTarget)
              ELSE InspectorDetail(openedFinding)
         /\ active = openingPriorActive
         /\ visibleMedia = openingPriorMedia
         /\ coordinatesVisible = openingPriorCoordinates
         /\ focus = DetailFocus(openedFinding)

NodeSelectionOutcomeIsExact ==
  lastAction = "SelectModalNode" =>
    /\ surface = "Modal"
    /\ modalPrimary = NodePrimary(selectedNode)
    /\ modalDetail = NoDetail
    /\ active = nodePriorActive
    /\ visibleMedia = nodePriorMedia
    /\ coordinatesVisible = nodePriorCoordinates
    /\ focus = NodeFocus(selectedNode)

AnnotationToggleOutcomeIsExact ==
  lastAction = "ToggleAnnotation" =>
    LET wasActive == toggledFinding \in togglePriorActive
        expectedActive ==
          IF wasActive
          THEN togglePriorActive \ {toggledFinding}
          ELSE togglePriorActive \cup {toggledFinding}
        removedPrimary ==
          /\ wasActive
          /\ togglePriorPrimary = FindingPrimary(toggledFinding)
    IN /\ active = expectedActive
       /\ modalPrimary =
            IF removedPrimary THEN NoPrimary ELSE togglePriorPrimary
       /\ modalDetail =
            IF removedPrimary THEN NoDetail ELSE togglePriorDetail
       /\ visibleMedia = togglePriorMedia
       /\ coordinatesVisible = togglePriorCoordinates
       /\ focus = AnnotationToggleFocus(toggledFinding)

ControlOutcomeIsExact ==
  lastAction \in
    {"SetDefault", "SetAll", "ClearAll",
     "ToggleMedium", "ToggleCoordinates"} =>
    CASE lastAction = "SetDefault" ->
           /\ activatedControl = "DefaultControl"
           /\ active = defaults
           /\ modalPrimary = NoPrimary
           /\ modalDetail = NoDetail
           /\ visibleMedia = controlPriorMedia
           /\ coordinatesVisible = controlPriorCoordinates
           /\ focus = AnnotationSetFocus("DefaultControl")
      [] lastAction = "SetAll" ->
           /\ activatedControl = "AllControl"
           /\ active = Annotatable
           /\ modalPrimary = controlPriorPrimary
           /\ modalDetail = controlPriorDetail
           /\ visibleMedia = controlPriorMedia
           /\ coordinatesVisible = controlPriorCoordinates
           /\ focus = AnnotationSetFocus("AllControl")
      [] lastAction = "ClearAll" ->
           /\ activatedControl = "ClearControl"
           /\ active = {}
           /\ modalPrimary = NoPrimary
           /\ modalDetail = NoDetail
           /\ visibleMedia = controlPriorMedia
           /\ coordinatesVisible = controlPriorCoordinates
           /\ focus = AnnotationSetFocus("ClearControl")
      [] lastAction = "ToggleMedium" ->
           LET medium == activatedControl
               expectedMedia ==
                 IF medium \in controlPriorMedia
                    /\ Cardinality(controlPriorMedia) = 1
                 THEN controlPriorMedia
                 ELSE IF medium \in controlPriorMedia
                      THEN controlPriorMedia \ {medium}
                      ELSE controlPriorMedia \cup {medium}
           IN /\ medium \in supportedMedia
              /\ active = controlPriorActive
              /\ modalPrimary = controlPriorPrimary
              /\ modalDetail = controlPriorDetail
              /\ visibleMedia = expectedMedia
              /\ coordinatesVisible = controlPriorCoordinates
              /\ focus = MediumToggleFocus(medium)
      [] lastAction = "ToggleCoordinates" ->
           /\ activatedControl = CoordinateControl
           /\ active = controlPriorActive
           /\ modalPrimary = controlPriorPrimary
           /\ modalDetail = controlPriorDetail
           /\ visibleMedia = controlPriorMedia
           /\ coordinatesVisible = ~controlPriorCoordinates
           /\ focus = CoordinateToggleFocus

ModalOpeningIsFresh ==
  lastAction = "OpenModal" =>
    /\ surface = "Modal"
    /\ modalPrimary =
         IF Transferable(openingEmbeddedPrimary, defaults)
         THEN openingEmbeddedPrimary
         ELSE NoPrimary
    /\ modalDetail = NoDetail
    /\ embeddedDetail = NoDetail
    /\ active = defaults
    /\ visibleMedia = {"CSharp"}
    /\ coordinatesVisible = FALSE
    /\ focus = ModalHeadingFocus

ModalDismissalIsExact ==
  lastAction \in {"DismissEscape", "DismissPointer"} =>
    /\ surface = "Embedded"
    /\ embeddedPrimary =
         IF Transferable(dismissedPrimary, defaults)
         THEN dismissedPrimary
         ELSE NoPrimary
    /\ embeddedDetail = NoDetail
    /\ modalPrimary = NoPrimary
    /\ modalDetail = NoDetail
    /\ active = defaults
    /\ visibleMedia = {"CSharp"}
    /\ coordinatesVisible = FALSE
    /\ focus = ExploreFocus

EscapeCannotBypassDetail ==
  escapeLayered

=============================================================================
