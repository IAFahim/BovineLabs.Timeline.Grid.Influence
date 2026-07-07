# BovineLabs Timeline Grid Influence

BovineLabs Timeline Grid Influence adds grid influence-focused Timeline tracks for DOTS projects built on top of BovineLabs Timeline Core.

## Package name

`com.bovinelabs.timeline.grid.influence`

## Runtime model & gotchas

The field is a chunked, sparse, **integer** grid. Timeline clips gather `Stamp`s, `FieldTickSystem` rasterizes them into per-chunk difference arrays, a prefix sum resolves values, and readers (query / steering / debug) sample the field. A few behaviours are load-bearing and easy to trip over:

- **One-frame stamp→read latency.** `GridInfluenceApplySystem` (stamps), `GridInfluenceQuerySystem` (reads) and `GridFlowSteeringSystem` (steer) all run *before* `FieldTickSystem` resolves the field. Readers therefore see the **previous** tick's resolved field; a stamp emitted this frame is not visible to a query until next frame.
- **Weights are plain integers — no ×100 fixed-point.** Unlike Essence stats (which store `1.0` as `100`), an influence `BaseWeight` of `1` is literally `1`. Clip-weight blending computes `round(Weight × clipWeight)`, so small weights **round to 0**: a `BaseWeight` of `1` contributes nothing until the blend weight reaches ~0.5, making footprints pop in instead of fading. Prefer `BaseWeight ≥ 8–10` so blended clips ramp smoothly.
- **Decay / spread are per-tick, not per-second.** The diffusion stencil is applied once per resolved tick, so on a variable-rate group the diffusion speed and the equilibrium magnitude of a continuously-stamped decaying field change with frame rate. Reason in ticks. Unless a fixed-tick option is configured, two machines at different FPS will read different numbers.
- **`RetentionFrames` is counted in ticks** (not wall-clock). A chunk is evicted once it has gone un-written for `RetentionFrames` ticks; `uint.MaxValue` keeps chunks forever. Because a double-buffered field schedules each physical buffer every *other* tick, retention and the 60-tick compaction cadence effectively run at half rate for double-buffered fields.
- **Chunks deactivate only at exact zero.** A decaying chunk stays resident until every cell reaches `0`; any non-zero frontier keeps the chunk (and its neighbours, via the stencil halo) alive.
- **Fields must be registered.** A clip whose field schema is not listed in `InfluenceGridSettingsAuthoring.Fields` resolves no slot and **silently no-ops** (now warned). This is the most common authoring mistake.
- **`Direction` / `DirectionSmooth` are un-normalized 2-cell central differences** (`∂/∂x`, `∂/∂y` sampled over ±1 cell). Their magnitude tracks the local gradient — they are *not* unit vectors; normalize yourself if you need a heading.
- **Sector wedges are ≤180° at the API level.** `InfluenceShape.Sector` is defined by two half-plane directions and covers at most a half-disc; build a wider cone from multiple sectors.
- **Painted stamps have no composite form.** A `Painted` base inside a composite is rejected / falls back to a solid rect; keep painted canvases to single stamps.
- **Compaction preserves mappings.** Every 60 ticks (when free slots exist) live chunks are relocated down and the slot arrays shrink; coord→value mappings survive the move.

### Debug toolbox

- **Gizmos:** the `influencegizmo.*` config vars — `draw-enabled`, `draw-grid`, `draw-values`, `draw-value-text`, `draw-stamps`, `draw-flow`, `draw-query`, `cull-to-camera`, the `*-color` and `*-stride` knobs.
- **Field Monitor window:** `Window/BovineLabs/Grid Influence/Field Monitor` — per-field summaries plus a per-chunk value preview, throttled sampling and a pause toggle.
- **Preset library & setup validation** live under the same Grid Influence menu for seeding field/stamp schemas and checking a scene's influence wiring.

## Threading contract

Field data lives in nested native containers (`NativeList`, `NativeFlatMap`) held inside `FieldRegistrySingleton` — the job safety system only sees the top-level component, **not** these inner containers, so it cannot detect races on field data. Correctness rests on convention:

- **Any system that reads or writes field data must access `FieldRegistrySingleton` via `GetSingletonRW`.** That RW access is the *only* thing carrying cross-system job ordering; a reader that skips it races silently.
- **Readers must combine `InfluenceField.Dependency`** into their job's input dependencies and publish their own handle back so later writers wait on them.
- **Main-thread readers** (`StatelessFeatures`, the monitor window) must `Complete` the field first, then read post-completion.
- **`NativeFlatMap` is a single-writer container with no safety handles.** Its `Add`/`Remove`/`Grow` must not overlap any reader, and `AsReadOnly` readers must be ordered after the writer's job by explicit `JobHandle` dependency.
