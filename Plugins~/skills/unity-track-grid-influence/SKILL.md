---
name: unity-track-grid-influence
description: Master of the BovineLabs.Timeline.Grid.Influence track family — GridInfluenceTrack/GridCompositeTrack stamp integer weight shapes into a named 2D influence field, GridFlowSteeringTrack physically slides a body up/down the field's gradient, GridInfluenceQueryTrack samples value+gradient into a result component for AI/Reactions. Covers the GridFieldSchemaObject routing key, stamp shapes, Polarity/Falloff/Rotation, decay+diffusion field config, and Target/EntityLink origin routing. Portable to any project containing the package; worked example from vex-ee. Use when a designer asks to "paint a danger/threat/vision/objective field on the floor over time", "make this drift toward the safest/hottest cell", or "read how dangerous my spot is right now".
---

# Grid Influence track family specialist

## 1. SCOPE

You own four Timeline tracks from package **`BovineLabs.Timeline.Grid.Influence`**, ns
`BovineLabs.Timeline.Grid.Influence.Authoring` (asmdef of the same name). All four are sealed
`DOTSTrack`, all bind to **`TargetsAuthoring`** (the same component every Essence/Reaction object
carries), all hold one clip type whose `duration => 1.0` (seed only). They form a producer→consumer
pipeline over a shared **influence field** — a 2D integer grid projected onto a plane:

| Track (DisplayName) | Clip | clipCaps | Role |
|---|---|---|---|
| `GridInfluenceTrack` ("BovineLabs/Grid/Influence") | `GridInfluenceClip` | `Blending \| Looping` | **WRITE**: stamp shape(s) of integer weight into a field this frame |
| `GridCompositeTrack` ("BovineLabs/Grid/Composite") | `GridCompositeClip` | `Blending \| Looping` | **WRITE**: stamp a multi-layer falloff blob (a baked composite) |
| `GridFlowSteeringTrack` ("BovineLabs/Grid/Flow Steering") | `GridFlowSteeringClip` | `Blending \| Looping` | **MOVE**: slide the bound body along the field gradient |
| `GridInfluenceQueryTrack` ("BovineLabs/Grid/Influence Query") | `GridInfluenceQueryClip` | `Looping` only | **READ**: sample value+gradient at the origin into a result component |

A field is named by a **`GridFieldSchemaObject`** asset (carries an `Id`); every clip routes by
that schema, NOT by scene reference. Writers add weight, readers/steerers consume it — they only
meet if they name the **same Field schema**. The field grid, its cells, decay/diffusion, and the
plane it lives on are configured ONCE per project in `InfluenceGridSettingsAuthoring` (a Core
Settings asset) — that's environment, not your per-clip job; audit it, don't recreate it.

Authoring the `.playable`, wiring the SubScene director, choosing schemas/shapes is your job.
Building the Targets stage, EntityLink sources, and the field-settings asset are OTHER specialists'
domains (protocol §6: report a missing prerequisite, never improvise) — with the stage exception in
§2 of `unity-agent-protocol`.

Operate per `unity-timeline-track-authoring` (the SubScene open/SetActive/save/try-finally-restore
bracket, the unity-cli-only discovery preamble, `PRE|` capture, the undo-appendix structure, the
fresh-load verification protocol — all of it, do not restate). Behave per `unity-agent-protocol`;
use the editor per `unity-cli`. This skill keeps ONLY the grid family's unique facts.

## 2. PORTABLE SEMANTICS

True in ANY project containing the package. Names/ids below are vex-ee WORKED EXAMPLES (§5) —
**rediscover them here**; never assume a remembered Field id, schema name, or asset path.

### The field, end to end
1. **Project setup (environment).** `InfluenceGridSettingsAuthoring` (SettingsGroup "Grid") bakes
   one `InfluenceGridSettings` (`CellSize` world-units/cell, `PlaneNormal` the grid plane, default
   up; `StrideAlignment`) plus a `GridFieldConfigData` buffer — one row per Field schema listed in
   its `Fields[]`. At runtime `FieldBootstrapSystem` (InitializationSystemGroup) reads that buffer
   ONCE and builds a `FieldRegistry` keyed by each schema's `Id`. **A Field schema NOT listed in
   the settings' `Fields[]` has no registry slot → every clip naming it is a silent no-op**
   (`KeyToSlot.TryGetValue` misses → `return`). This is the family's #1 trap.
2. **Write.** While a `GridInfluenceClip`/`GridCompositeClip` is active, `GridInfluenceApplySystem`
   (TimelineSystemGroup, after component-animation) resolves the clip's **origin world position**
   (§"Origin routing"), projects it to a grid cell, and adds the clip's shape weights into that
   field's chunks — scaled by `ClipWeight` (the blend weight) and rounded. Weight 0 after scaling
   is skipped. Writers are **per-frame, additive, and transient**: each tick `FieldTickSystem`
   (end of SimulationSystemGroup) clears and re-rasterizes from that frame's pending stamps, so the
   field reflects only what's being stamped THIS frame plus (if decay is on) a faded copy of last
   frame. Stop stamping → the field empties (subject to decay/retention below).
3. **Decay + diffusion (optional, per field).** A field only retains/spreads if its schema has
   `DoubleBuffered = true` AND `DecayPerMille > 0`. Then each tick: `kept = value*(1000-DecayPerMille)/1000`
   (integer), a fraction `kept/SpreadDenominator` bleeds to each of 4 neighbours. So `DecayPerMille`
   = how fast the paint fades (0 = no decay; with `DoubleBuffered=false` the field is wiped to bare
   stamps every frame), `SpreadDenominator` = how tight the bleed (bigger = less spread). Without
   double-buffering there is no "heat that lingers"; a single stamp clip just shows while active.
4. **Read.** `GridFlowSteeringClip` (steer) and `GridInfluenceQueryClip` (sample) read the field's
   gradient at the bound body's cell via `FieldGradient.Ascent` (central difference of neighbour
   cell values).

### Per-clip facts
| Type | Facts (all verified vex-ee 2026-06, source-derived; no play mode) |
|---|---|
| `GridInfluenceClip` | Fields: **`Field`** (`GridFieldSchemaObject`, REQUIRED — null → `Debug.LogError` + skip), **`Stamp`** (`GridStampSchemaObject`, REQUIRED — null → error+skip), `ExtraStamps[]` (more stamps, same origin), `Composite` (optional `GridCompositeSchemaObject` blob, stamped alongside), `Polarity` (Additive/Subtractive — flips weight sign), `Rotation` (`Quarter` R0/R90/R180/R270, 90° steps), `Falloff` (None/Stepped), `FalloffSteps` (default 3), `FalloffSpacing` (default 2), `WeightMultiplier` (default 1, scales stamp weight), `LocalOffset` (Vector3, offset from origin before projection), `originTarget` (`Target`, default Owner), `originLink` (`EntityLinkSchema`, optional), `Category` (`GridFieldCategory` — **editor tint ONLY, no runtime effect**). |
| `GridCompositeClip` | A pure composite stamp: `Field` + `Composite` (both REQUIRED; `Composite.Base` must be non-null) + `Polarity` + `LocalOffset` + `originTarget`/`originLink`. No single Stamp, no Falloff/Rotation. Bakes the composite to a `CompositeShapeBlob` (cached by content hash) — concentric layers from `CompositeProfile` (Peak, Levels, a `DistanceToWeight` AnimationCurve, center→edge). Use it for a soft gradient mound; `GridInfluenceClip.Composite` is the same blob stamped together with a hard stamp. |
| `GridFlowSteeringClip` | `Field` (REQUIRED) + `Bias` (`FlowBias` Descend/Ascend) + `MaxSpeed` (default 1, units/sec) + `LocalOffset`. **No Target routing** — always steers the BOUND body's own `LocalTransform`. While active, moves the body `gradient_dir * MaxSpeed * dt * ClipWeight` per frame; Descend = downhill (flee high values), Ascend = uphill (seek high values). Pure planar slide on the field plane; ignores physics, collisions, the body's own movement. Zero gradient → no move. |
| `GridInfluenceQueryClip` | `Field` (REQUIRED) + `LocalOffset` + `originTarget`/`originLink`. **`Looping` only — no Blending** (it samples, it doesn't accumulate). Bake adds `InfluenceQueryData` + an empty **`InfluenceQueryResult`** to the clip entity. While active, `GridInfluenceQuerySystem` fills the result: `Value` (field value at the cell), `Direction` (`int2` ascent gradient), `Cell` (`int2`), `Valid` (1 if the field+origin resolved, else 0 and result is cleared). This is the bridge to AI/Reactions — another system reads `InfluenceQueryResult` off the clip entity. |

### Stamp shapes (`GridStampSchemaObject`)
`Kind` is a `ShapeKind`: **SolidRect, RectShell, Disc, Annulus, Capsule, Ellipse, RoundedRect,
ThickLine, Sector**. `BaseWeight` (int) is the per-cell weight inside the shape; each Kind reads its
own field group (Disc→`DiscCenter`/`DiscRadius`; Annulus→outer/inner radius; Sector→
`SectorFacingDegrees`/`SectorHalfAngleDegrees`; etc.). Stamps are integer raster shapes in CELL
units, not world meters — a `DiscRadius=5` with `CellSize=1` is a 5-metre disc; halve `CellSize` and
it's 2.5 m. `Stepped` falloff stamps `FalloffSteps+1` concentric **inset** rings (`spacing` cells
apart) for a layered plateau. `Rotation` rotates the shape in 90° quarters at bake.

### Origin routing (writers + query; NOT flow steering)
The clip binds to a `TargetsAuthoring`; the **origin** of the stamp/sample is chosen by
`originTarget` (a `Target`: None/Target/Owner/Source/Self/Custom) resolved through that entity's
`Targets` component, optionally hopped one more step through `originLink` (an `EntityLinkSchema`).
`None`/`Self` → the bound entity itself. Same enum + EntityLink mechanism as the EntityLinks track
family — see those skills; the trap is identical: a `Target` slot that resolves to `Entity.Null`
silently falls back to the bound entity (you stamp on the wrong thing, no error). `LocalOffset` is
applied in the origin's local space (rotated by its rotation) before projecting to a cell.

### What does NOT apply
- **No ×100 fixed-point.** Unlike Essence stats, field weights are PLAIN integers; the only scaling
  is `WeightMultiplier` (author-time) × `ClipWeight` (blend), rounded. Author 1, read 1.
- **No restore/undo of runtime field state.** The field is transient runtime data rebuilt every
  tick; there is nothing to "undo" at runtime — stop stamping and it fades. Undo scope is the
  AUTHORING artifacts only (the `.playable`, director wiring, any schema assets you created).
- **Flow steering has no Target routing and ignores physics.** It writes `LocalTransform.Position`
  directly. On a physics body this fights the physics step; prefer steering kinematic/transform-only
  agents, or feed a Query result into a force/PID track instead.

## 3. DISCOVERY RECIPES

Act only through `unity-cli exec`/`console`; never the filesystem; never play mode; Safe Loop on
every mutation. Discover names here — never assume the §5 worked example.

**3.1 Confirm the package (else report a missing prerequisite — protocol §6):**
```csharp
var t = System.Type.GetType("BovineLabs.Timeline.Grid.Influence.Authoring.GridInfluenceTrack, BovineLabs.Timeline.Grid.Influence.Authoring");
return t == null ? "MISSING_PREREQUISITE|GridInfluenceTrack not found - package BovineLabs.Timeline.Grid.Influence absent" : "OK|" + t.AssemblyQualifiedName + "|dataPath=" + UnityEngine.Application.dataPath;
```

**3.2 Scene + SubScene + director:** run the unity-cli First Command for the active scene/roots/
SubScene `.unity` paths; find `PlayableDirector`(s) inside the chosen SubScene (read-only additive
open, restore parent after); pick the single director carrying the project's timeline-reference
authoring component (else ask). Zero directors → protocol §6.

**3.3 Find the bind target by COMPONENT (`TargetsAuthoring`), never by name** — print per holder:
hierarchy path, scene.path, sibling `TargetsAuthoring` slots (Owner/Source/Target/Custom),
`EntityLinkSourceAuthoring` schemas present, has `LocalTransform`-baking (for a steered body).
Resolve by recorded hierarchy path, not bare `GameObject.Find`.

**3.4 Discover Field/Stamp/Composite schemas AND verify field registration (the §2 trap):**
```csharp
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:GridFieldSchemaObject")) {
    var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var f = UnityEditor.AssetDatabase.LoadAssetAtPath<BovineLabs.Timeline.Grid.Influence.Authoring.GridFieldSchemaObject>(p);
    UnityEngine.Debug.Log($"FIELD|{f.FieldName}|id={f.Id}|chunkPow={f.ChunkPower}|retention={f.RetentionFrames}|doubleBuffered={f.DoubleBuffered}|decayPerMille={f.DecayPerMille}|spreadDenom={f.SpreadDenominator}|{p}");
}
// then load the InfluenceGridSettingsAuthoring asset (FindAssets t:InfluenceGridSettingsAuthoring or the SettingsAuthoring holder) and print CellSize, PlaneNormal, and which Field schemas are in Fields[].
// CROSS-CHECK: a Field used by a clip MUST appear in settings.Fields[] or the clip is a silent no-op (trap §2.1). Same for Stamps[] visibility expectations.
```
Likewise `FindAssets("t:GridStampSchemaObject")` (print `Kind`, `BaseWeight`, the radius/size for
that Kind) and `t:GridCompositeSchemaObject` (print `Base`, `Profile.Peak`/`Levels`). **Ids drift
between projects — never assume a remembered Field id.** No matching schema for the designer's
intent → create one (a tracked authoring artifact, journal it) or report the gap.

**3.5 Capture pre-state** (`PRE|`) per `unity-timeline-track-authoring` §3.5: the director's current
`playableAsset` path and every existing track binding (name/index/bound object), before mutating.

## 4. CANONICAL PATTERNS

One logical change per exec block; print `PRE|` before mutating; save inside the block; verify from
a fresh load (§7 + the shared verification protocol). Clip fields are camelCase-serialized — the
public field names above ARE the `SerializedObject` property paths (`Field`, `Stamp`, `Polarity`,
`WeightMultiplier`, `originTarget`, `Bias`, `MaxSpeed`, …). Schema references are object refs (set
`.objectReferenceValue` to the loaded asset). Build the asset/track exactly as the shared template
shows, then per pattern:

**P1 — Paint a danger/threat field (WRITE).** `GridInfluenceTrack` + one `GridInfluenceClip`:
set `Field` = the threat field schema, `Stamp` = a Disc (e.g. `BaseWeight=10`, radius covering the
hazard), `Polarity=Additive`, `originTarget=Self` (stamp at the bound enemy/hazard's own cell). For
a lingering heat-map, ensure that Field's schema has `DoubleBuffered=true` + `DecayPerMille>0` (e.g.
30 ≈ 3%/tick fade) so it spreads and fades instead of vanishing the instant the clip ends. Multiple
hazards stamping the same Field accumulate.

**P2 — Soft objective mound (WRITE, composite).** `GridCompositeTrack` + `GridCompositeClip`:
`Field` = an objective field, `Composite` = a schema whose `Base` is a Disc and `Profile` is a
center-peaked falloff (Peak high, `DistanceToWeight` linear-to-0). Stamps a smooth gradient hill the
AI can climb — pair with a Query or Ascend steering clip elsewhere.

**P3 — Drift to safety / hunt the hotspot (MOVE).** `GridFlowSteeringTrack` + `GridFlowSteeringClip`
on the AGENT (the bound body IS the thing that moves): `Field` = the danger field, `Bias=Descend`,
`MaxSpeed` = its flee speed → the body slides downhill away from danger every frame the clip is
active. `Bias=Ascend` makes it home in on the highest-value cell (chase the objective mound from
P2). Remember: it edits `LocalTransform` directly — use on transform-driven agents, not bodies you
also drive with physics force clips (they'll fight).

**P4 — "How dangerous is my spot?" (READ).** `GridInfluenceQueryTrack` + `GridInfluenceQueryClip`,
`Field` = the field to sample, `originTarget=Self`. While active it writes `InfluenceQueryResult`
(`Value`, `Direction`, `Cell`, `Valid`) onto the clip entity for a Reaction/AI system to consume
(e.g. a Reaction that spawns an escape when `Value` crosses a threshold). This clip is the seam
between the spatial field and the TRA/Reaction world — you produce the reading; the consuming
Reaction is another specialist's job.

Combine on one timeline: a writer track painting the field + a steering OR query track reading it,
all naming the SAME Field schema. Overlap two writer clips (Blending caps) for a crossfaded blend of
two threat shapes; the query/steer clips don't blend (Query is Looping-only).

## 5. WORKED EXAMPLE (vex-ee) — rediscover, never assume

- Package present at `/home/i/GitHub/vex-ee/Packages/BovineLabs.Timeline.Grid.Influence/`. Authoring
  ns `BovineLabs.Timeline.Grid.Influence.Authoring`; data ns `…Data` / `…Data.Flows`.
- Verified type facts (source reads): all four tracks sealed `DOTSTrack`, `TrackBindingType =
  TargetsAuthoring`; DisplayNames `BovineLabs/Grid/{Influence, Composite, Flow Steering, Influence
  Query}`. Clip caps: Influence/Composite/Flow = `Blending|Looping`, Query = `Looping`. All
  `duration => 1.0`. Enums: `Polarity{Additive,Subtractive}`, `FlowBias{Descend,Ascend}`,
  `FalloffMode{None,Stepped}`, `Quarter{R0,R90,R180,R270}`, `ShapeKind{SolidRect,RectShell,Disc,
  Annulus,Capsule,Ellipse,RoundedRect,ThickLine,Sector}`, `GridFieldCategory{Generic,Threat,Vision,
  Territory,Objective,Flow}` (editor tint only).
- `GridFieldSchemaObject` fields: `FieldName`, `ChunkPower(1..8,def4)`, `RetentionFrames(def300)`,
  `DoubleBuffered`, `DecayPerMille(0..1000)`, `SpreadDenominator(min1,def5)`; `Id` is auto-assigned
  via `IUID`/`AutoRef` into `InfluenceGridSettingsAuthoring.Fields`. Stamp via `AutoRef` into
  `.Stamps`. `InfluenceGridSettingsAuthoring`: `CellSize(def1)`, `PlaneNormal(def up)`,
  `StrideAlignment(min8)`.
- Runtime systems (all `TimelineSystemGroup`): `GridInfluenceApplySystem` (writer, after
  component-animation), `GridFlowSteeringSystem` + `GridInfluenceQuerySystem` (both after Apply);
  field lifecycle in `Fields/RegistryRuntime.cs` — `FieldBootstrapSystem`
  (InitializationSystemGroup, builds registry from `GridFieldConfigData` once),
  `FieldTickSystem` (SimulationSystemGroup OrderLast, rasterize+decay+swap). Decay math
  `IntegerMath.DecayKeep = value*(1000-decayPerMille)/1000`; diffusion gated on
  `DoubleBuffered && DecayPerMille>0`.
- Debug aids present: `InfluenceDebugSystem`, `Flowfielddebugsystem`, `Influencequerydebugsystem`,
  and an editor `InfluenceFieldMonitorWindow` — use these to OBSERVE field values in the editor
  (a designer can watch the heat-map) rather than guessing.
- No Grid timeline assets were found pre-built in `Assets/Training/` at authoring time — when you
  build the first one, record CellSize/PlaneNormal and the chosen Field id in your memory card.

## 6. UNDO APPENDIX

Runtime field state is transient (rebuilt each tick — nothing to restore). Undo scope = AUTHORING
artifacts, per the `unity-timeline-track-authoring` undo structure. Inventory for one §4 run:
1. Created `.playable` (TimelineAsset + grid track + clip sub-assets — `DeleteAsset` removes all).
2. Possibly-created folder(s) (only if `PRE|folderExisted=false`).
3. Mutated `director.playableAsset` (CAPTURED pre value).
4. Added generic binding for the new track (SubScene file; CAPTURED prior table).
5. Any **Field/Stamp/Composite schema asset you created** + its registration into
   `InfluenceGridSettingsAuthoring.Fields[]`/`.Stamps[]` (if you added one — that edits the settings
   asset; capture the prior array and journal it).
ORDER: restore director FIRST, then delete the `.playable`, then revert the settings-asset array
edit, then delete any schema asset, then other captured values (an asset deleted while referenced
leaves a dangling `{fileID: 0}`). Use the UNDO-1/2/3/4 templates from
`unity-timeline-track-authoring`, substituting `<TRACK_TYPE>` = the grid track type,
`<BIND_TARGET>` = the `TargetsAuthoring` holder; add an UNDO step for the settings/schema edits when
present, then the fresh-load verification (§7).

## 7. VERIFICATION PROTOCOL

Run the shared fresh-load protocol from `unity-timeline-track-authoring`, plus these family checks:
1. **Fresh-load asset dump** (separate exec block): load the `.playable`, dump each track/clip —
   name, start/duration, caps, `Field`(id), `Stamp`/`Composite`(name), `Polarity`, `Bias`/`MaxSpeed`
   (flow), `originTarget`/`originLink`, `WeightMultiplier`, `Falloff`/`Rotation`. Expect Query
   caps = `Looping` (no Blending); the others `Looping|Blending`.
2. **Field-registration cross-check (the trap, §2.1):** the `Field` schema each clip names MUST be
   present in `InfluenceGridSettingsAuthoring.Fields[]`. A clip naming an unlisted field is a SILENT
   no-op — there is NO console error. Print the membership explicitly; do not infer it from a clean
   console.
3. **Required-schema check:** Influence clips need a non-null `Field` AND `Stamp`; Composite clips
   need `Field` + `Composite.Base`; Flow/Query need `Field`. A missing one logs
   `Debug.LogError` at BAKE and the clip is skipped — surface any such error against the baseline.
4. **Binding from a RELOADED SubScene:** expect `BIND|<i>|<trackName> (<GridTrackType>) ->
   <holder> (TargetsAuthoring)`; prior entries intact.
5. **Parent-scene restore** (sceneCount=1, active, not dirty) and `unity-cli console --filter error`
   clean vs the project baseline. **Silence is NOT evidence** — the silent-no-op traps (unlisted
   field, null Target falling back to self) produce a clean console; prove the field membership and
   the resolved origin explicitly.
