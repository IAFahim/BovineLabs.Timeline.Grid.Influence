# TODO.md — BovineLabs.Timeline.Grid.Influence Full-Library Audit

## Executive Summary

The library is a chunked, sparse, integer influence-field system driven by DOTS Timeline clips: clips gather `Stamp`s → `FieldTickSystem` rasterizes them into per-chunk difference arrays → prefix sum resolves values → readers (query/steering/debug) sample last frame's field. The core math layer (Rasterizer, PrefixSum, InfluenceField, NativeFlatMap) is unusually well-tested with oracle/property tests and is largely trustworthy.

The biggest risks are **not in the math** — they are in the *system seams*:

1. **World/system-filter mismatch**: bootstrap/tick systems have no `WorldSystemFilter`, while every consumer system declares `Editor` support. In the edit-mode Editor world the registry singleton is never created and the entire feature is silently dead.
2. **Unbounded memory from misauthored stamps**: nothing caps how many chunks a single huge stamp can allocate. A typo'd radius can allocate gigabytes on the single-threaded prepare job.
3. **Frame-rate-dependent gameplay**: decay/spread are per-tick constants in a variable-rate group; diffusion speed and equilibrium field values change with FPS.
4. **A job-safety escape hatch**: `FieldRegistry` (nested native containers inside a `NativeArray` inside a singleton component) is passed into a `ScheduleParallel` job, and `NativeList.AsArray()` is called per-entity inside that job. Nothing in Unity's safety system can see these accesses; correctness rests entirely on an *undocumented* "every reader must touch the singleton RW" convention, plus a dead `WriterDependency` field that nobody assigns.
5. **A family of silent failures**: unregistered field keys, stamps dropped over the span budget, composite-vs-stamp precedence, small weights rounding to 0 during blends — all no-op without a message (only steering warns).

Everything else is medium/low: validation gaps, duplicated composite bake logic, a slot leak on frame wraparound, retention semantics silently doubling for double-buffered fields, and missing tests for the compaction and budget-overflow paths.

---

## System Inventory

| System / File | Responsibility |
|---|---|
| `InfluenceField` (+ `.DebugAccess`, `.flowaccess`) | Sparse chunked int field. Slot lifecycle (activate/evict/compact), difference-array scatter, prefix-sum resolve, optional decay/spread stencil. Owns its `JobHandle` chain. |
| `PrepareSlotsHelper` / `PrepareSlotsFrom{Map,Array}Job` | Frame advance, eviction (retention), compaction (every 60 frames), span budgeting, chunk activation, stamp extraction. |
| `Rasterizer` / `Shapes` / `ShapeRotation` / `ShapeScaling` | Shape → weighted-rect spans; bounds; span-count estimates; quarter rotation; inset. |
| `PrefixSum` | AVX2/Neon/scalar 2D inclusive scan per chunk. |
| `NativeFlatMap` | Custom open-addressing int2→int map (coord→slot), backward-shift delete. No safety handles. |
| `FieldRegistry` / `FieldRegistrySingleton` | Fixed-capacity array of `InfluenceFieldPair` (front/back fields, flow cache, config), key→slot map, `PendingStamps` multimap. Lives inside an `IComponentData`. |
| `FieldBootstrapSystem` / `FieldTickSystem` | Create registry from baked `GridFieldConfigData`; per-frame schedule of rasterize/resolve + stencil + buffer swap + stamp-map clear. |
| `GridInfluenceApplySystem` | Gathers stamps from active clips into `PendingStamps`, scaled by `ClipWeight`. |
| `GridInfluenceQuerySystem` | Samples value/gradient (+bilinear) into `InfluenceQueryResult` per active query clip; resets when inactive. |
| `GridFlowSteeringSystem` + `FlowField` | Per-field gradient cache resolve; moves bound `LocalTransform` along ±gradient. Warn-once for missing keys. |
| Authoring clips/tracks/schemas | `GridInfluenceClip`, `GridCompositeClip`, `GridFlowSteeringClip`, `GridInfluenceQueryClip`; `GridFieldSchemaObject`, `GridStampSchemaObject` (incl. painted canvas), `GridCompositeSchemaObject`, `InfluenceGridSettingsAuthoring`. |
| Debug | `InfluenceDebugSystem`, `FlowFieldDebugSystem`, `InfluenceQueryDebugSystem`; editor `InfluenceFieldMonitorWindow` + `InfluenceFieldSnapshot`. |
| Editor | Custom inspectors w/ footprint previews & paint canvas, clip tint editor, scene gizmo, preset library, `StampRasterizer`. |
| Tests | Oracle/property tests for rasterizer, prefix sum, field lifecycle, diffusion, flow, projection, primitives, reader APIs. |
| `StatelessFeatures.cs` | Example read-side helpers (Territory/Vision/Capture/Flow/Placement). |

## Dependency & Flow Map

Per frame (all inside `SimulationSystemGroup`):

```
TimelineSystemGroup:
  GridInfluenceApplySystem   — reads active clips, resolves Origin via EntityLinks,
                               writes Stamps into PendingStamps (keyed by registry slot)
  GridInfluenceQuerySystem   — reads *last tick's* Front field → InfluenceQueryResult
  GridFlowSteeringSystem     — resolves FlowField from Front, writes LocalTransform
SimulationSystemGroup (OrderLast):
  FieldTickSystem            — per field: [stencil from Front if decaying] →
                               (Prepare → Rasterize/Clear → Scatter → Resolve) on Back
                               (or Front if single-buffered) → Swap → ClearMapJob
```

Key invariants discovered (several are implicit and unenforced — see TODOs):
- **One-frame latency**: readers always see the previous tick's resolved field.
- **Cross-system job ordering is carried only by ECS chaining on `FieldRegistrySingleton` RW access** — if any future reader doesn't touch the singleton RW, there is no safety net (the containers are invisible to the job safety system).
- **A chunk is only non-stale if `lastWrittenBySlot[slot] == FrameId`**; `FieldReader`/`FlowReader` honor this, but the **stencil halo path does not** (TODO-05).
- Deactivation of decaying chunks only happens at exact zero; the frontier keeps any non-zero chunk alive.
- Double-buffered fields schedule each physical buffer every *other* tick (affects `FrameId`/retention semantics, TODO-13).

---

## Critical TODOs

### TODO-01: Cap per-frame chunk activation / stamp bounds to prevent OOM & main-thread hangs

**Priority:** Critical
**Certainty:** Confirmed
**Lens:** Designer Safety / Performance / Validation
**Files/Systems Involved:** `PrepareSlotsJob.cs` (`ProcessStamps`, `ActivateBounds`, `EnsureSlot`), `GridStampSchemaObject.OnValidate`, `GridInfluenceApplySystem`
**Problem:** `MaxSpansPerSchedule (1<<20)` caps *spans*, not *chunks*. A disc of radius 10,000 emits only ~20k spans (passes the budget) but its bounds activate ~1.5M chunks; `EnsureSlot` grows `Data` by `ElementsPerChunk` per chunk in a single-threaded job → multi-GB allocation / long stall. No authoring clamp exists on radii/sizes (`OnValidate` only clamps minimums).
**Evidence:** `ActivateBounds` iterates the full `ChunkRangeOf(bounds)` with no count limit; `EstimateSpanCount` for a disc is `2r+1`, so the span budget does not protect against area. `GridStampSchemaObject.OnValidate` has no maxima.
**Why It Matters:** One designer typo (or a scaled composite) hangs or OOMs the editor/build with no error message.
**Suggested Change:** (a) Add a per-schedule chunk-activation budget (e.g., `MaxChunksPerSchedule`) that skips the stamp and increments a "dropped" counter; (b) clamp shape extents in `GridStampSchemaObject.OnValidate` (e.g., radius/size ≤ 512 cells); (c) surface drops via debug counter (see TODO-22).
**Implementation Path:**
1. In `ProcessStamps`, before `ActivateBounds`, compute chunk count of the bounds; if over budget, zero the stamp's span capacity and `continue`, incrementing a dropped counter stored on the field.
2. Add maxima in `OnValidate` for all radii/rect sizes.
3. Expose the counters through `InfluenceFieldSnapshot` and a warning in the monitor window.
**Snippet/Pseudocode:**
```csharp
var chunks = ChunkMath.ChunkRangeOf(bounds, Spec.Log2);
long count = (long)(chunks.Max.x - chunks.Min.x + 1) * (chunks.Max.y - chunks.Min.y + 1);
if (count > MaxChunksPerSchedule - activated) { Dropped.Value++; continue; }
```
**How to Verify:** Unit test: schedule a `Disc(radius: 100_000)` stamp; assert field survives, `ActiveSlotCount <= MaxChunksPerSchedule`, dropped counter incremented.
**Tradeoffs:** Legit giant stamps get truncated; make the budget tunable and log once.
**Confidence:** High

### TODO-02: Add `WorldSystemFilter` to `FieldBootstrapSystem`/`FieldTickSystem` (feature is silently dead in the Editor world)

**Priority:** Critical
**Certainty:** Confirmed
**Lens:** Architecture / State
**Files/Systems Involved:** `Fields/RegistryRuntime.cs`
**Problem:** `GridInfluenceApplySystem`, `GridInfluenceQuerySystem`, `GridFlowSteeringSystem` and all three debug systems declare `WorldSystemFilterFlags... | Editor`. `FieldBootstrapSystem` and `FieldTickSystem` declare **no filter** (Default), so they are not created in the edit-mode Editor world. `RequireForUpdate<FieldRegistrySingleton>` then silently disables every consumer — the entire package no-ops in edit mode.
**Suggested Change:** Apply the identical `WorldSystemFilter` (LocalSimulation | ServerSimulation | ClientSimulation | Editor) to bootstrap + tick.
**How to Verify:** Open a subscene with an influence timeline in edit mode with `influencegizmo.draw-enabled 1`; chunks should draw.
**Confidence:** High

### TODO-03: Decay/diffusion is frame-rate dependent — normalize to time or move to fixed step

**Priority:** Critical
**Certainty:** Confirmed
**Lens:** Timing
**Files/Systems Involved:** `FieldTickSystem`, `InfluenceField.ResolveJob.ApplyStencil`, `IntegerMath.DecayKeep/Outflow`, `GridFieldSchemaObject` (`DecayPerMille`, `RetentionFrames`)
**Problem:** The stencil applies `DecayPerMille`/`SpreadDenominator` once per rendered frame (variable rate). At 144 FPS a field decays ~4.8× faster in real time than at 30 FPS; for continuously-stamped decaying fields the *equilibrium magnitude* scales with FPS — gameplay reads return different numbers on different machines. `RetentionFrames` is likewise wall-clock dependent.
**Suggested Change:** Preferred: fixed-step or config-gated accumulator (`while (acc >= tickInterval) RunOneTick()`, clamp sub-steps to 2, default off = current per-frame behavior). Do **not** scale integer `DecayPerMille` by dt (truncation breaks small values).
**How to Verify:** Run diffusion at simulated 30 Hz and 120 Hz wall-clock; equilibrium value at a probe cell must match.
**Confidence:** High (problem), Medium (remediation choice)

### TODO-04: Stop calling `NativeList.AsArray()`/`FieldRegistry` access inside the parallel query job; prebuild readers on the main thread

**Priority:** Critical
**Certainty:** Risk (potential safety exception / definite loss of safety coverage)
**Lens:** Architecture / Event / Performance
**Files/Systems Involved:** `GridInfluenceQuerySystem.SampleQueryJob`, `FieldRegistry`, `InfluenceField.AsReader`
**Problem:** `SampleQueryJob` (ScheduleParallel) carries `[ReadOnly] FieldRegistry Registry` — a `NativeArray<InfluenceFieldPair>` whose *elements* contain `NativeList`s/`NativeFlatMap`. The safety system only patches top-level containers, so (a) all inner accesses are invisible to the race detector, and (b) `Registry.Front(id).AsReader()` executes `NativeList.AsArray()` **per entity inside a parallel job**, which with collections checks enabled can throw and is wasted work per entity regardless.
**Suggested Change:** On the main thread, build `NativeArray<FieldReader> readersBySlot` (one `AsReader()` per field, after combining `Front.Dependency`) with a parallel `valid` array and pass into the job; index by `KeyToSlot[key]`.
**Snippet/Pseudocode:**
```csharp
var readers = CollectionHelper.CreateNativeArray<FieldReader>(reg.Count, state.WorldUpdateAllocator);
for (int i = 0; i < reg.Count; i++)
    if (reg.Slot(i).Front.IsCreated) { readers[i] = reg.Slot(i).Front.AsReader(); valid[i] = 1; }
```
**How to Verify:** Enable collections checks + leak detection, run the Grid Influence showcase; play-mode test with many query entities across 2 fields.
**Confidence:** High that the change is an improvement

---

## High Priority TODOs

### TODO-05: Validate chunk freshness in the diffusion stencil (self + halo) to prevent stale-value re-injection

**Priority:** High — **Certainty:** Strongly Likely (edge), Confirmed (missing check) — **Lens:** State / Timing
**Files:** `InfluenceField.ResolveJob.ApplyStencil/FillHaloColumn/FillHaloRow`, `PrepareSlotsHelper.NeedsActivationEdge`, `StencilConfig`, `FieldTickSystem`
**Problem:** The stencil reads front-buffer chunk data via `StencilSlotByCoord.TryGetValue` with **no `lastWrittenBySlot == frontFrameId` check** (unlike `FieldReader`). Inactive front chunks are *usually* zero, but a chunk that reaches exactly 0 in one buffer (e.g., additive+subtractive stamps cancel) while its other-buffer copy still holds the previous frame's non-zero data is never cleared; two ticks later that stale data is read as halo/self source. The invariant "stale ⇒ zero" is load-bearing but unstated and untested.
**Suggested Change:** Add `NativeArray<uint> LastWrittenBySlot; uint FrameId;` to `StencilConfig` (front's values). In `ApplyStencil`/`FillHalo*`/`NeedsActivationEdge`, treat `lastWritten != FrameId` as zero. Populate in `FieldTickSystem` and the test harness `StencilOf`.
**Verify:** New test: stamp +1000, one diffusion step, then exact canceling stamp so a chunk hits 0 while sibling buffer is non-zero; step twice more, assert no inflow from the dead chunk.

### TODO-06: Make span/chunk-budget stamp drops loud and deterministic

**Priority:** High — **Certainty:** Confirmed (silent drop); Strongly Likely (nondeterministic order) — **Lens:** Validation / Debugging / Determinism
**Files:** `PrepareSlotsHelper.ProcessStamps`, `PrepareSlotsFromMapJob`, `GridInfluenceApplySystem`
**Problem:** Stamps over the `MaxSpansPerSchedule` budget get capacity 0 and are silently skipped. Which stamps are skipped depends on multimap extraction order (parallel-writer insertion order) → the drop set can differ run to run.
**Suggested Change:** (a) Count drops into a per-field counter, warn-once, surface in monitor; (b) sort `ExtractedStamps` (origin, kind, weight) before `ProcessStamps` so only the drop decision needs stable order (scatter is already commutative via `Interlocked.Add`).
**Verify:** Submit stamps exceeding `1<<20` estimate; assert dropped counter > 0 and the *same* stamps drop across 10 runs.

### TODO-07: Warn (once) on unregistered `FieldKey` in Apply and Query systems, matching Steering

**Priority:** High — **Certainty:** Confirmed — **Lens:** Validation / Designer Safety
**Files:** `GridInfluenceApplySystem.GatherStampsJob`, `GridInfluenceQuerySystem.SampleQueryJob`, `GridFlowSteeringSystem` (reference impl)
**Problem:** A clip whose field schema isn't in `InfluenceGridSettingsAuthoring.Fields` hits `KeyToSlot.TryGetValue == false` and returns silently. Only steering has `_warnedMissing`. This is *the* most likely designer mistake.
**Suggested Change:** Shared warn-once pre-pass (collect distinct keys, diff against `KeyToSlot`, warn per key) reused by all three systems; pair with editor validation (TODO-20).

### TODO-08: Fix `GridInfluenceClip` option precedence: composite silently disables `Stamp`, ignores `Rotation`/`Falloff`; null-`Base` composite silently falls back

**Priority:** High — **Certainty:** Confirmed — **Lens:** Designer Safety / Validation
**Files:** `GridInfluenceClip.Bake/TryBuildComposite/HasSchemas`, `GridInfluenceClipGizmo`, `GridInfluenceExpansion`
**Problem:** When `Composite` (with `Base`) is assigned: the primary `Stamp` is not collected, and `Rotation`/`Falloff`/`FalloffSteps/Spacing` are never applied to composite layers — no message. If `Composite != null` but `Base == null`, the composite is silently ignored. The scene gizmo previews only stamps, so editor preview and baked result diverge exactly here.
**Suggested Change:** (a) Bake-time warnings; (b) either apply `.Rotated(Rotation)` per composite layer or grey the fields out; (c) extend the gizmo to draw composite layers (reuse `GridCompositeSchemaObjectEditor.CollectLayers`).

### TODO-09: Reject/handle `Painted` base shapes consistently in all composite build paths

**Priority:** High — **Certainty:** Confirmed — **Lens:** Validation
**Files:** `GridCompositeClip.TryBakeBlob`, `GridCompositeSchemaObject.TryBuild` (missing check), `GridInfluenceClip.TryBuildComposite` (has the check)
**Problem:** `BuildShape` for Painted returns a `SolidRect` over the whole canvas, so a painted base silently bakes as a solid-rect mound in two of the three paths.
**Suggested Change:** Centralize composite building (TODO-14) with the Painted rejection + warning in one place; also warn in the schema editor.

### TODO-10: Formalize the cross-system dependency contract; remove or implement `WriterDependency`

**Priority:** High — **Certainty:** Confirmed (dead field); Risk (future race) — **Lens:** Architecture / Event
**Files:** `InfluenceFieldPair.WriterDependency`, `FieldTickSystem`, `GridFlowSteeringSystem`, debug systems, `InfluenceFieldSnapshot`
**Problem:** `WriterDependency` is combined/completed/cleared in four places but **never assigned** by any producer. The *actual* contract — "every system reading/writing field data must access `FieldRegistrySingleton` via `GetSingletonRW` AND `PublishDependency` its job" — is written down nowhere; violations produce silent races, not exceptions.
**Suggested Change:** Minimum: XML-doc the contract on `FieldRegistrySingleton`/`WriterDependency`/`StatelessFeatures` (main-thread, post-Complete only). Better (follow-up): `AcquireReader/PublishRead` API on `FieldRegistry`; migrate consumers; delete dead field.

### TODO-11: Small stamp weights round to 0 under clip-weight blending → footprint pop-in

**Priority:** High — **Certainty:** Confirmed — **Lens:** Designer Safety / Timing
**Files:** `InfluenceShape.TryScaleWeight`, `GridInfluenceApplySystem.Emit`, `GridStampSchemaObject.BaseWeight`
**Problem:** `round(Weight * clipWeight)` discards zero → `BaseWeight = 1` yields **nothing** until blend weight ≥ ~0.5; blended-in clips pop instead of fading; the whole stamp vanishes below the threshold.
**Suggested Change:** Editor validation/HelpBox when `BaseWeight × WeightMultiplier < ~8`; document integer quantization prominently.

### TODO-12: `InfluenceDebugSystem._stampQuery` is narrower than `DrawStampsJob`'s signature

**Priority:** High — **Certainty:** Confirmed — **Lens:** Debugging / Validation
**Files:** `InfluenceDebugSystem.OnCreate/OnUpdate`, `DrawStampsJob.Execute`
**Problem:** Query lacks `InfluenceStampElement` but `Execute` takes the buffer. Works today only because both bakers always pair them; fragile / version-dependent failure.
**Suggested Change:** Add `InfluenceStampElement` to the query builder.

### TODO-13: `RetentionFrames` (and per-buffer `FrameId`) advance at half rate for double-buffered fields

**Priority:** High — **Certainty:** Confirmed — **Lens:** Timing / Validation
**Files:** `FieldTickSystem` (swap), `InfluenceField.NewHelper`, `PrepareSlotsHelper` eviction, `GridFieldSchemaObject.RetentionFrames`
**Problem:** Each physical buffer is scheduled every *other* tick, so its `FrameId` advances once per two game frames. Eviction and the 60-frame compaction cadence run at half the configured rate for double-buffered fields. "RetentionFrames = 300" means ~300 frames on single-buffered but ~600 on double-buffered.
**Suggested Change:** Drive `FrameId` from an externally supplied tick counter passed into `Schedule` (backward-compatible overload) so both buffers share one timeline; or document + halve at `Register`.
**Verify:** Double-buffered field, RetentionFrames=4; count game ticks until slot freed; assert 4 (not 8).

---

## Medium Priority TODOs

### TODO-14: Consolidate the three composite bake paths; fix empty-blob registration in `GridCompositeClip`
**Certainty:** Confirmed — `GridCompositeClip.TryBakeBlob/ContentHash` (custom hash + registers blob *before* checking `Layers.Length > 0` → empty blob stays registered under the hash) vs `GridInfluenceClip.TryBuildComposite` (`AddBlobAsset` content dedup, disposes on failure) vs `GridCompositeSchemaObject.TryBuild` (raw). One `CompositeBaking.TryBuild(schema, polarity, weightMultiplier, rotation, out blob)` used by all three; validate before registration; prefer `AddBlobAsset`; delete `ContentHash`.

### TODO-15: `GridFieldPresetLibrary` creates schemas that are never registered in settings (silent no-op fields)
**Certainty:** Confirmed (no settings wiring); Risk (AutoRef id assignment for `AssetDatabase.CreateAsset`) — After creation, load the settings asset and merge (reuse showcase `MergeFields/MergeStamps` logic); assert non-zero, non-duplicate `Id`s and log a summary.

### TODO-16: Frame-wraparound reset leaks slots (never returned to `FreeSlots`) and stale `SlotByCoord` entries
**Certainty:** Confirmed — In `PrepareSlotsHelper.Execute` ResetFrame branch, slots are zeroed but neither freed nor unmapped; retention pass skips them (`!= 0` guard); compaction rebuild gated on `FreeSlots.Length > 0`. Fix: in the reset branch, mirror the eviction body (`FreeSlots.Add(i); SlotByCoord.Remove(CoordBySlot[i]);`).

### TODO-17: Idle-cost & scheduling perf pass
**Certainty:** Confirmed — (a) Tick schedules the full pipeline for idle fields (no stamps, no active chunks, no stencil) — early-out; (b) Apply's exact `requiredCapacity` main-thread chunk walk every frame — use estimate, exact only when resize indicated; (c) Steering schedules one full-query `SteerJob` per field sequentially (O(fields × clips), serialized) — one job with `NativeArray<FlowReader>` indexed by slot; (d) Query per-entity `AsReader` — covered by TODO-04.

### TODO-18: Test the compaction path (`FrameId % 60`)
**Certainty:** Confirmed (no coverage) — `CompactionPreservesAllMappingsAndValues`: allocate ~40 chunks, evict half via short retention, drive `FrameId` to a multiple of 60 (`OverrideFrameId`), schedule, assert every surviving coord reads its exact pre-compaction values, lists shrank, no `SlotByCoord` entry points past the new length. Fuzz across chunk powers.

### TODO-19: Unit-test and harden `NativeFlatMap`
**Certainty:** Confirmed (no coverage) — Property test vs `Dictionary<int2,int>`: random Add/Remove/TryGetValue including forced hash clustering; assert across `Grow`. Add debug-only guard against full-table infinite loop in `Remove`; doc comment "single writer; readers must be dependency-ordered".

### TODO-20: Editor-time validation suite ("Validate Grid Influence Setup")
**Certainty:** Confirmed (gaps) — Checks: duplicate `ushort` keys across field schemas; `id == 0` (unassigned AutoRef); `id > 65535` truncation; `FieldName` > FixedString64 bytes; null array entries; clip references field not in settings; `Composite.Base == null`; Painted composite base. Menu item + inspector HelpBoxes + build preprocessor.

### TODO-21: Monitor window syncs *all* field jobs every inspector update
**Certainty:** Confirmed — `InfluenceFieldSnapshot.TryCapture` calls `Complete()` on every pair ~10×/s with Auto on. Complete only the *selected* field; capture-rate throttle (2 Hz default); "Pause capture" toggle.

### TODO-22: Add runtime observability counters (dropped stamps, activated chunks, map pressure)
**Certainty:** Confirmed (missing) — Per-field `FieldFrameStats { StampsIn, StampsDropped, ChunksActivated, ChunksEvicted }` written by Prepare; surfaced via snapshot/monitor; warn-once when `StampsDropped > 0`.

### TODO-23: Move `CompositeProfile` out of the Data assembly; delete dead `FalloffProfile`; fix file/namespace/casing drift
**Certainty:** Confirmed — `Data/Shapes/Compositeprofile.cs` has namespace `...Authoring` and drags `AnimationCurve` (managed) into the runtime Data assembly; move to Authoring (move the .meta too). `Data/Shapes/FalloffCurve.cs` (`FalloffProfile`) is referenced nowhere — delete (verify first). Normalize file casing (`Gridfieldcategory.cs`, `Gridinfluencequeryclip.cs`, …) — optional, keep .meta GUIDs.

### TODO-24: Link-resolved origin without `LocalToWorld` → silent no-op
**Certainty:** Confirmed (path), Risk (frequency) — Bake adds `TransformUsageFlags.Dynamic` to the binding only; when `originLink` resolves to a different entity lacking `LocalToWorld`, gather/query early-out silently. Debug-build warn-once + docs.

---

## Low Priority TODOs

- **TODO-25** *(Risk)* Paint canvas / preview vertical orientation audit — verify a 1-cell painted stamp appears on the correct world side in inspector preview vs scene gizmo; if flipped, fix the preview, not the data.
- **TODO-26** `GridCompositeSchemaObject` implements `IUID` but has no `[AutoRef]` and its `Id` is never consumed — register or drop (or document).
- **TODO-27** README: one-frame read latency; **integer weights (no ×100 fixed point — contrast with Essence stats)**; per-tick decay semantics; deactivation-at-exact-zero; `Direction`/`DirectionSmooth` are un-normalized 2-cell central differences; Sector supports ≤180° wedges only at the `InfluenceShape.Sector` API level.
- **TODO-28** Showcase builders: `GameObject.Find` wiring fragile; `GridInfluenceShowcaseBuilder.MakeMaterial` leaks un-saved materials into the scene (reuse `SteeringShowcaseBuilder`'s cached asset approach).
- **TODO-29** `FieldTickSystem.pair.PendingStencil` written then cleared within one update — make it a local.
- **TODO-30** `IsChunkVisible`/`RectVisible` AABB code duplicated 3× across debug systems — extract `GridDebugCulling`.
- **TODO-31** `GridInfluenceQuerySystem.ResetQueryJob` schedules every frame even with zero query entities — gate on cached query emptiness.
- **TODO-32** `FieldConfig.Name` assignment from `string` can throw/truncate for long names at bake — clamp in `InfluenceGridSettingsAuthoring.Bake`.

---

## Designer Safety TODOs
- Warn-once on unregistered field keys everywhere (TODO-07) + edit-time membership check (TODO-20).
- Clamp stamp/shape maxima in `OnValidate`; chunk-activation budget with visible drop counter (TODO-01, TODO-22).
- `GridInfluenceClip` inspector: grey-out/warn ignored fields when Composite set; warn Composite-without-Base; warn low `BaseWeight×Multiplier` with blends (TODO-08, TODO-11).
- Painted-base composite rejection everywhere (TODO-09). Preset library registration (TODO-15). Composite gizmo parity (TODO-08c). Retention/decay tooltips per buffering mode (TODO-13, TODO-03).

## Validation & Guard TODOs
- Editor: duplicate keys, id==0, id>65535, name length, null entries, clip→settings membership, Composite.Base null, Painted base (TODO-20, TODO-09).
- Build-time: same validation as build preprocessor (TODO-20).
- Runtime: chunk budget + counters (TODO-01, TODO-22); stencil freshness (TODO-05); warn-once missing key (TODO-07); warn-once origin-without-LocalToWorld (TODO-24); `NativeFlatMap.Remove` full-table assert (TODO-19).

## Timing / Physics / Animation TODOs
- Fixed-step / accumulator diffusion; retention in real ticks (TODO-03).
- Shared tick counter across double buffers (TODO-13).
- Document one-frame stamp→read latency; blend-weight quantization pop-in (TODO-11).

## Architecture TODOs
- Dependency contract as code/docs; dead `WriterDependency` (TODO-10).
- Prebuilt reader arrays instead of registry-in-job (TODO-04).
- Single composite bake path (TODO-14). Data-assembly purity (TODO-23).
- `StatelessFeatures` documented as main-thread/post-Complete (TODO-10).

## Debugging / Tooling TODOs
- Frame stats counters in snapshot + monitor (TODO-22). Monitor selected-field-only sync + throttle (TODO-21).
- Composite gizmo preview (TODO-08c); shared culling helper (TODO-30). Validation menu (TODO-20).

## Testing TODOs
1. **CompactionPreservesMappingsAndValues** (TODO-18)
2. **NativeFlatMap property test vs Dictionary** (TODO-19)
3. **SpanBudgetOverflow_DropsDeterministicallyAndCounts** (TODO-06)
4. **HugeStampRespectsChunkBudget** (TODO-01)
5. **StaleHaloDoesNotReinject** (TODO-05)
6. **WraparoundReclaimsSlots** (TODO-16)
7. **DoubleBufferedRetentionMatchesConfig** (TODO-13)
8. **ECS pipeline integration** (Apply → Tick → Query result next frame; reset on ClipActive disable)
9. **FixedStep diffusion equivalence at 30/120 Hz** (TODO-03)
10. **Editor validation test** — duplicate-key pair produces a report (TODO-20)

## Suggested Architecture Direction

**Current weakness:** the data layer is a self-contained, well-tested kernel, but the orchestration layer glues it to ECS through invisible conventions — nested containers the safety system can't see, a dead `WriterDependency`, ordering guaranteed only by everyone happening to `GetSingletonRW` the same component, and consumer systems reconstructing readers ad hoc inside jobs.

**Desired boundaries:** `InfluenceField` (kernel) owns chunk memory + its handle chain — unchanged. `FieldRegistry` (broker) becomes the only place that hands out access (`AcquireReader`/`PublishRead`). `FieldTickSystem` (pump) is the sole writer/scheduler; fixed-step; shared tick counter; idle early-out. Validation runs the same rule list at edit-time (inspectors + menu), build-time (preprocessor, fail hard), and runtime (warn-once + counters — never silent). Migration order: TODO-02 → 04/17 → 05 → 03 → 20 → 14; each step independently shippable and test-guarded.

## Final Ranked TODO List

1. TODO-01 Chunk-activation budget + shape clamps — Critical
2. TODO-02 WorldSystemFilter on bootstrap/tick — Critical
3. TODO-03 Fixed-step / time-normalized decay & retention — Critical
4. TODO-04 Prebuilt FieldReader array in query job — Critical
5. TODO-05 Frame-validated stencil self/halo reads — High
6. TODO-07 Warn-once unregistered field keys — High
7. TODO-08 GridInfluenceClip composite precedence + rotation/gizmo parity — High
8. TODO-06 Loud + deterministic span-budget drops — High
9. TODO-10 Dependency contract; dead WriterDependency — High
10. TODO-11 Blend-weight quantization guidance/validation — High
11. TODO-12 InfluenceStampElement in debug stamp query — High
12. TODO-13 Unified FrameId/retention for double-buffered fields — High
13. TODO-09 Painted-base rejection everywhere — High
14. TODO-20 Editor + build-time validation suite — Medium
15. TODO-14 Consolidate composite bake; empty-blob fix — Medium
16. TODO-18 Compaction tests — Medium
17. TODO-22 Runtime observability counters — Medium
18. TODO-17 Idle/scheduling perf pass — Medium
19. TODO-19 NativeFlatMap tests + hardening — Medium
20. TODO-15 Preset library settings registration — Medium
21. TODO-21 Monitor window sync throttling — Medium
22. TODO-24 Origin-without-LocalToWorld warn-once — Medium
23. TODO-16 Wraparound slot reclamation — Medium
24. TODO-23 CompositeProfile move; dead FalloffProfile; casing — Medium
25. TODO-27 Runtime-model README/docs — Low
26. TODO-25 Paint canvas orientation audit — Low
27. TODO-30 Shared debug culling helper — Low
28. TODO-26 GridCompositeSchemaObject IUID decision — Low
29. TODO-29 PendingStencil as local — Low
30. TODO-31 Reset-job empty gate — Low
31. TODO-32 FieldName FixedString clamp — Low
32. TODO-28 Showcase builder robustness/material leak — Low
