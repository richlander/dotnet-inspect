/-!
# Covering paths and certified properties

`ProducerCapabilityPlanValidator` walks one satisfaction path: it starts from
the selected provision's offer, requires each covering edge's source to match
the current offer, replaces the offer with the edge's target, and finally
compares the offer with the requirement. `Offer.FromTarget` takes the target
offer's structural properties from the last edge alone.

The key `K` abstracts the scope, capability, completion, and outcome identities
that the validator compares by reference equality. `exact` abstracts
`ProducerCapabilityProperties.ExactCardinality`.

What a covering edge's `ExactCardinality` certifies is not pinned down by the
design. Two readings are stated below:

- **absolute**: the projected result has exact cardinality whenever its source
  realizes the edge's source key; and
- **preserving**: the projection preserves the exact cardinality of its source,
  matching the enum's documentation, "The result preserves exact source
  cardinality."

The validator's last-edge rule is sound under the absolute reading and unsound
under the preserving reading. A conjunctive rule is sound under both.
-/

namespace ProducerCapabilityDemand

namespace Coverage

variable {K V : Type} [DecidableEq K]

structure Provision (K : Type) where
  key : K
  exact : Bool

structure Edge (K : Type) where
  source : K
  target : K
  exact : Bool

structure Requirement (K : Type) where
  key : K
  needsExact : Bool

/-- An offer is a key plus whether exact cardinality is claimed. -/
abbrev Offer (K : Type) := K × Bool

/-- `ProducerCapabilityPlanValidator`: properties come from the last edge. -/
def walkLastEdge : Offer K → List (Edge K) → Option (Offer K)
  | o, [] => some o
  | o, e :: es => if o.1 = e.source then walkLastEdge (e.target, e.exact) es else none

/-- Alternative: a path claims exactness only if every link certifies it. -/
def walkConjunctive : Offer K → List (Edge K) → Option (Offer K)
  | o, [] => some o
  | o, e :: es =>
      if o.1 = e.source then walkConjunctive (e.target, o.2 && e.exact) es else none

def accepts (walk : Offer K → List (Edge K) → Option (Offer K))
    (p : Provision K) (path : List (Edge K)) (r : Requirement K) : Bool :=
  match walk (p.key, p.exact) path with
  | some (k, ex) => decide (k = r.key) && (!r.needsExact || ex)
  | none => false

/-- A producer's meaning for keys, cardinality, and covering projections. -/
structure Semantics (K V : Type) where
  realizes : K → V → Prop
  exactCard : V → Prop
  project : Edge K → V → V

def evalPath (S : Semantics K V) (v : V) (path : List (Edge K)) : V :=
  path.foldl (fun v e => S.project e v) v

def ProvisionSound (S : Semantics K V) (p : Provision K) (v : V) : Prop :=
  S.realizes p.key v ∧ (p.exact = true → S.exactCard v)

def EdgeSoundAbsolute (S : Semantics K V) (e : Edge K) : Prop :=
  ∀ v, S.realizes e.source v →
    S.realizes e.target (S.project e v) ∧
    (e.exact = true → S.exactCard (S.project e v))

def EdgeSoundPreserving (S : Semantics K V) (e : Edge K) : Prop :=
  ∀ v, S.realizes e.source v →
    S.realizes e.target (S.project e v) ∧
    (e.exact = true → S.exactCard v → S.exactCard (S.project e v))

def Satisfies (S : Semantics K V) (r : Requirement K) (v : V) : Prop :=
  S.realizes r.key v ∧ (r.needsExact = true → S.exactCard v)

omit [DecidableEq K] in
theorem absolute_implies_preserving {S : Semantics K V} {e : Edge K}
    (h : EdgeSoundAbsolute S e) : EdgeSoundPreserving S e := by
  intro v hv
  exact ⟨(h v hv).1, fun hx _ => (h v hv).2 hx⟩

theorem final_satisfies {S : Semantics K V} {r : Requirement K} {v : V}
    {k : K} {ex : Bool}
    (hv : S.realizes k v ∧ (ex = true → S.exactCard v))
    (hacc : (decide (k = r.key) && (!r.needsExact || ex)) = true) :
    Satisfies S r v := by
  simp only [Bool.and_eq_true, decide_eq_true_eq, Bool.or_eq_true,
    Bool.not_eq_true'] at hacc
  obtain ⟨rfl, hx⟩ := hacc
  refine ⟨hv.1, fun hn => ?_⟩
  cases hx with
  | inl h => simp [h] at hn
  | inr h => exact hv.2 h

theorem walkLastEdge_sound {S : Semantics K V}
    (path : List (Edge K)) (hs : ∀ e ∈ path, EdgeSoundAbsolute S e) :
    ∀ (o o' : Offer K) (v : V),
      S.realizes o.1 v → (o.2 = true → S.exactCard v) →
      walkLastEdge o path = some o' →
      S.realizes o'.1 (evalPath S v path) ∧
        (o'.2 = true → S.exactCard (evalPath S v path)) := by
  induction path with
  | nil =>
      intro o o' v hk hx hw
      simp only [walkLastEdge, Option.some.injEq] at hw
      subst hw
      exact ⟨hk, hx⟩
  | cons e es ih =>
      intro o o' v hk hx hw
      simp only [walkLastEdge] at hw
      split at hw
      · rename_i heq
        have he := hs e (List.mem_cons_self ..)
        have hv := he v (heq ▸ hk)
        exact ih (fun e' h => hs e' (List.mem_cons_of_mem _ h))
          (e.target, e.exact) o' (S.project e v) hv.1 hv.2 hw
      · cases hw

theorem walkConjunctive_sound {S : Semantics K V}
    (path : List (Edge K)) (hs : ∀ e ∈ path, EdgeSoundPreserving S e) :
    ∀ (o o' : Offer K) (v : V),
      S.realizes o.1 v → (o.2 = true → S.exactCard v) →
      walkConjunctive o path = some o' →
      S.realizes o'.1 (evalPath S v path) ∧
        (o'.2 = true → S.exactCard (evalPath S v path)) := by
  induction path with
  | nil =>
      intro o o' v hk hx hw
      simp only [walkConjunctive, Option.some.injEq] at hw
      subst hw
      exact ⟨hk, hx⟩
  | cons e es ih =>
      intro o o' v hk hx hw
      simp only [walkConjunctive] at hw
      split at hw
      · rename_i heq
        have he := hs e (List.mem_cons_self ..)
        have hv := he v (heq ▸ hk)
        refine ih (fun e' h => hs e' (List.mem_cons_of_mem _ h))
          (e.target, o.2 && e.exact) o' (S.project e v) hv.1 ?_ hw
        intro hboth
        simp only [Bool.and_eq_true] at hboth
        exact hv.2 hboth.2 (hx hboth.1)
      · cases hw

/-- The current rule is sound when edges certify exactness absolutely. -/
theorem lastEdge_sound_absolute {S : Semantics K V}
    {p : Provision K} {path : List (Edge K)} {r : Requirement K} {v : V}
    (hp : ProvisionSound S p v) (hs : ∀ e ∈ path, EdgeSoundAbsolute S e)
    (hacc : accepts walkLastEdge p path r = true) :
    Satisfies S r (evalPath S v path) := by
  unfold accepts at hacc
  split at hacc
  · rename_i k ex hw
    exact final_satisfies
      (walkLastEdge_sound path hs (p.key, p.exact) (k, ex) v hp.1 hp.2 hw) hacc
  · cases hacc

/-- The conjunctive rule is sound when edges only preserve exactness. -/
theorem conjunctive_sound_preserving {S : Semantics K V}
    {p : Provision K} {path : List (Edge K)} {r : Requirement K} {v : V}
    (hp : ProvisionSound S p v) (hs : ∀ e ∈ path, EdgeSoundPreserving S e)
    (hacc : accepts walkConjunctive p path r = true) :
    Satisfies S r (evalPath S v path) := by
  unfold accepts at hacc
  split at hacc
  · rename_i k ex hw
    exact final_satisfies
      (walkConjunctive_sound path hs (p.key, p.exact) (k, ex) v hp.1 hp.2 hw) hacc
  · cases hacc

/-- The conjunctive rule is also sound under the absolute reading. -/
theorem conjunctive_sound_absolute {S : Semantics K V}
    {p : Provision K} {path : List (Edge K)} {r : Requirement K} {v : V}
    (hp : ProvisionSound S p v) (hs : ∀ e ∈ path, EdgeSoundAbsolute S e)
    (hacc : accepts walkConjunctive p path r = true) :
    Satisfies S r (evalPath S v path) :=
  conjunctive_sound_preserving hp
    (fun e h => absolute_implies_preserving (hs e h)) hacc

/--
When the provision and every edge certify exactness, as in the Package Tree
and section-row adopters, both rules accept the same paths.
-/
theorem rules_agree_when_all_exact (path : List (Edge K))
    (hall : ∀ e ∈ path, e.exact = true) (k : K) :
    walkLastEdge (k, true) path = walkConjunctive (k, true) path := by
  induction path generalizing k with
  | nil => rfl
  | cons e es ih =>
      have he := hall e (List.mem_cons_self ..)
      simp only [walkLastEdge, walkConjunctive, Bool.true_and, he]
      split
      · exact ih (fun e' h => hall e' (List.mem_cons_of_mem _ h)) e.target
      · rfl

/-! ## Counterexample under the preserving reading -/

/-- Values are a key and an exact-cardinality flag. -/
def demo : Semantics Nat (Nat × Bool) where
  realizes k v := v.1 = k
  exactCard v := v.2 = true
  project e v := (e.target, v.2)

/-- Windowed Name rows: key `0`, no exact-cardinality claim. -/
def windowedNames : Provision Nat := ⟨0, false⟩

/-- Names → Count, certified to preserve its source's cardinality. -/
def namesToCount : Edge Nat := ⟨0, 1, true⟩

/-- A Count requirement that needs exact cardinality. -/
def exactCount : Requirement Nat := ⟨1, true⟩

theorem demo_edge_preserving : EdgeSoundPreserving demo namesToCount := by
  intro v hv
  exact ⟨rfl, fun _ h => h⟩

theorem demo_provision_sound : ProvisionSound demo windowedNames (0, false) :=
  ⟨rfl, fun h => by cases h⟩

theorem demo_lastEdge_accepts :
    accepts walkLastEdge windowedNames [namesToCount] exactCount = true := by
  decide

theorem demo_conjunctive_rejects :
    accepts walkConjunctive windowedNames [namesToCount] exactCount = false := by
  decide

/--
Under the preserving reading the validator accepts a Count path whose result
does not have exact cardinality.
-/
theorem lastEdge_unsound_preserving :
    ¬ Satisfies demo exactCount (evalPath demo (0, false) [namesToCount]) := by
  intro h
  exact Bool.false_ne_true (h.2 rfl)

end Coverage

end ProducerCapabilityDemand
