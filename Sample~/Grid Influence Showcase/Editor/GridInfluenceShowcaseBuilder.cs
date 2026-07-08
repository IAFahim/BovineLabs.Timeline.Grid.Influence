using BovineLabs.Core.Asset;
using System;
using System.Collections.Generic;
using TMPro;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using TargetsAuthoring = BovineLabs.Reaction.Authoring.Core.TargetsAuthoring;
using TargetSlot = BovineLabs.Reaction.Data.Core.Target;
using LifeCycleAuthoring = BovineLabs.Nerve.Authoring.LifeCycle.LifeCycleAuthoring;
using TimelineBeginAuthoring = BovineLabs.Timeline.Core.Authoring.TimelineBeginAuthoring;
using TimelineBeginMode = BovineLabs.Timeline.Core.Authoring.TimelineBeginMode;
using PositionTrack = BovineLabs.Timeline.Transform.Authoring.TransformPositionTrack;
using PositionClip = BovineLabs.Timeline.Transform.Authoring.PositionClip;
using PositionType = BovineLabs.Timeline.Transform.Authoring.PositionType;
using InfluenceTrack = BovineLabs.Timeline.Grid.Influence.Authoring.GridInfluenceTrack;
using InfluenceClip = BovineLabs.Timeline.Grid.Influence.Authoring.GridInfluenceClip;
using CompositeTrack = BovineLabs.Timeline.Grid.Influence.Authoring.GridCompositeTrack;
using CompositeClip = BovineLabs.Timeline.Grid.Influence.Authoring.GridCompositeClip;
using FlowTrack = BovineLabs.Timeline.Grid.Influence.Authoring.GridFlowSteeringTrack;
using FlowClip = BovineLabs.Timeline.Grid.Influence.Authoring.GridFlowSteeringClip;
using QueryTrack = BovineLabs.Timeline.Grid.Influence.Authoring.GridInfluenceQueryTrack;
using QueryClip = BovineLabs.Timeline.Grid.Influence.Authoring.GridInfluenceQueryClip;
using FieldSchema = BovineLabs.Timeline.Grid.Influence.Authoring.GridFieldSchemaObject;
using StampSchema = BovineLabs.Timeline.Grid.Influence.Authoring.GridStampSchemaObject;
using CompositeSchema = BovineLabs.Timeline.Grid.Influence.Authoring.GridCompositeSchemaObject;
using CompositeProfile = BovineLabs.Timeline.Grid.Influence.Authoring.CompositeProfile;
using GridSettings = BovineLabs.Timeline.Grid.Influence.Authoring.InfluenceGridSettingsAuthoring;
using Polarity = BovineLabs.Timeline.Grid.Influence.Authoring.Polarity;
using FalloffMode = BovineLabs.Timeline.Grid.Influence.Authoring.FalloffMode;
using GridFieldCategory = BovineLabs.Timeline.Grid.Influence.Authoring.GridFieldCategory;
using Quarter = BovineLabs.Timeline.Grid.Influence.Data.Quarter;
using ShapeKind = BovineLabs.Timeline.Grid.Influence.Data.ShapeKind;
using FlowBias = BovineLabs.Timeline.Grid.Influence.Data.Flows.FlowBias;

public static class GridInfluenceShowcaseBuilder
{
    private const string SampleFolder = "Assets/Samples/GridInfluenceShowcase";
    private const string TimelineFolder = SampleFolder + "/Timelines";
    private const string ParentPath = SampleFolder + "/GridInfluenceShowcase.unity";
    private const string SubPath = SampleFolder + "/GridInfluenceShowcase_Sub.unity";

    private const string RequiredInSubScenePath = "Assets/Prefabs/Required In Subscene.prefab";
    private const string SettingsPath = "Assets/Settings/Settings/InfluenceGridSettingsAuthoring.asset";
    private const string FieldFolder = "Assets/Settings/Schemas/GridFields";
    private const string StampFolder = "Assets/Settings/Schemas/GridStamps";
    private const string ThreatFieldPath = FieldFolder + "/Threat.asset";
    private const string ObjectiveFieldPath = FieldFolder + "/Objective.asset";
    private const string DiscStampPath = StampFolder + "/Disc.asset";
    private const string RingStampPath = StampFolder + "/Annulus.asset";
    private const string SectorStampPath = StampFolder + "/Sector.asset";
    private const string CompositePath = FieldFolder + "/Mound.asset";

    private static readonly Color WriteColor = new Color(0.20f, 0.80f, 0.80f);
    private static readonly Color CompositeColor = new Color(0.55f, 0.45f, 0.85f);
    private static readonly Color FlowColor = new Color(0.20f, 0.45f, 0.92f);
    private static readonly Color QueryColor = new Color(0.90f, 0.68f, 0.22f);
    private static readonly Color ShapeColor = new Color(0.85f, 0.30f, 0.35f);
    private static readonly Color HazardColor = new Color(0.92f, 0.30f, 0.28f);
    private static readonly Color AgentColor = new Color(0.85f, 0.88f, 0.95f);
    private static readonly Color PadColor = new Color(0.20f, 0.22f, 0.27f);
    private static readonly Color BannerColor = new Color(0.05f, 0.07f, 0.11f);

    private const float WriteX = -34f;
    private const float CompositeX = -17f;
    private const float FlowX = 0f;
    private const float QueryX = 17f;
    private const float ShapeX = 34f;
    private const float RowStep = 9.0f;
    private const float ActorY = 1.0f;

    private static readonly Vector3 CameraPos = new Vector3(0f, 26f, -50f);

    private static Scene activeSub;
    private static FieldSchema threat;
    private static FieldSchema objective;
    private static StampSchema discStamp;
    private static StampSchema ringStamp;
    private static StampSchema sectorStamp;
    private static CompositeSchema mound;
    private static GridSettings settings;

    private sealed class CellWire
    {
        public string DirectorName;
        public string TimelinePath;
        public string TrackName;
        public string BindName;
        public bool BindTransform;
    }

    private static readonly List<CellWire> Wires = new List<CellWire>();

    private sealed class CaptionData
    {
        public string Title;
        public string Usage;
        public Vector3 CellPos;
        public Color Color;
    }

    private static readonly List<CaptionData> Captions = new List<CaptionData>();

    [MenuItem("Showcase/Build Grid Influence")]
    public static void Build()
    {
        Wires.Clear();
        Captions.Clear();

        EnsureFolders();
        EnsureSchemas();
        ResetAssets();

        var parent = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(parent, ParentPath);
        var sub = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(sub);
        activeSub = sub;

        BuildRequiredInSubScene();
        BuildPads();
        BuildWriteColumn();
        BuildCompositeColumn();
        BuildFlowColumn();
        BuildQueryColumn();
        BuildShapeColumn();

        EditorSceneManager.SaveScene(sub, SubPath);
        EditorSceneManager.SetActiveScene(parent);
        EditorSceneManager.CloseScene(sub, true);

        sub = EditorSceneManager.OpenScene(SubPath, OpenSceneMode.Additive);
        EditorSceneManager.SetActiveScene(sub);
        activeSub = sub;

        foreach (var w in Wires)
            WireCell(w);

        EditorSceneManager.MarkSceneDirty(sub);
        EditorSceneManager.SaveScene(sub);

        EditorSceneManager.SetActiveScene(parent);
        BuildParent();
        EditorSceneManager.SaveScene(parent);

        EditorSceneManager.CloseScene(sub, true);
        EditorSceneManager.OpenScene(ParentPath, OpenSceneMode.Single);

        Debug.Log("GridInfluenceShowcase: built at " + ParentPath + " directors=" + Wires.Count +
                  " | Threat.Id=" + threat.Id + " Objective.Id=" + objective.Id +
                  " Disc.Id=" + discStamp.Id + " Ring.Id=" + ringStamp.Id + " Sector.Id=" + sectorStamp.Id +
                  " CellSize=" + settings.CellSize);
    }

    // ============================================================
    //  WRITE column (cyan) — GridInfluenceClip stamps the Threat field.
    // ============================================================

    private static void BuildWriteColumn()
    {
        // Row 0 — Disc additive, moving hazard: footprint travels across cells.
        WriteCell(0, "Disc Additive (moving)",
            "GridInfluenceClip Field=Threat Stamp=Disc(BaseWeight=10,r=5) Polarity=Additive originTarget=Self. The RED hazard ORBITS via its own Position timeline, so every frame it re-stamps a +10 disc at a new cell of the world-space Threat field. Field weights are PLAIN integers (author 10 -> read 10).",
            c =>
            {
                c.Field = threat; c.Stamp = discStamp; c.Polarity = Polarity.Additive;
                c.WeightMultiplier = 1f; c.Category = GridFieldCategory.Threat;
            }, true);

        // Row 1 — Subtractive polarity (negates weights).
        WriteCell(1, "Subtractive Polarity",
            "Same Disc stamp but Polarity=Subtractive: the stamp weight is negated (-10) so it carves a NEGATIVE basin into the Threat field instead of a peak. Proves PolarityExtensions.Sign() (Additive=+1, Subtractive=-1).",
            c =>
            {
                c.Field = threat; c.Stamp = discStamp; c.Polarity = Polarity.Subtractive;
                c.WeightMultiplier = 1f; c.Category = GridFieldCategory.Threat;
            }, true);

        // Row 2 — WeightMultiplier scales weight.
        WriteCell(2, "WeightMultiplier x3",
            "WeightMultiplier=3 on a Disc(BaseWeight=10): the per-cell weight becomes round(10*3*clipWeight)=30. WeightMultiplier multiplies BaseWeight by ClipWeight before rasterizing (no x100 fixed-point - it is a literal integer scale).",
            c =>
            {
                c.Field = threat; c.Stamp = discStamp; c.Polarity = Polarity.Additive;
                c.WeightMultiplier = 3f; c.Category = GridFieldCategory.Threat;
            }, true);

        // Row 3 — ExtraStamps: multi-shape footprint (disc + ring) in one clip.
        WriteCell(3, "ExtraStamps (Disc+Ring)",
            "Stamp=Disc plus ExtraStamps=[Annulus]: a single clip rasterizes MULTIPLE shapes in one footprint. The primary Stamp goes through the builder; each ExtraStamp is appended to the InfluenceStampElement buffer and emitted at the same origin.",
            c =>
            {
                c.Field = threat; c.Stamp = discStamp; c.ExtraStamps = new[] { ringStamp };
                c.Polarity = Polarity.Additive; c.WeightMultiplier = 1f; c.Category = GridFieldCategory.Threat;
            }, true);

        // Row 4 — LocalOffset shifts the stamp from the origin.
        WriteCell(4, "LocalOffset + R90",
            "LocalOffset=(4,0,0) offsets the stamp 4 cells from the hazard origin, and Rotation=R90 rotates the (anisotropic Sector) footprint a quarter turn. Demonstrates Quarter rotation + LocalOffset translation of the rasterized shape.",
            c =>
            {
                c.Field = threat; c.Stamp = sectorStamp; c.LocalOffset = new Vector3(4f, 0f, 0f);
                c.Rotation = Quarter.R90; c.Polarity = Polarity.Additive; c.WeightMultiplier = 1f;
                c.Category = GridFieldCategory.Threat;
            }, true);
    }

    private static void WriteCell(int row, string title, string usage, Action<InfluenceClip> configure, bool moving)
    {
        var z = row * RowStep;
        var cell = "Write" + row;
        var hazard = MakeHazard(cell + "_Hazard", new Vector3(WriteX, ActorY, z), HazardColor, moving, 3.5f);

        var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
        var track = timeline.CreateTrack<InfluenceTrack>(null, "Influence");
        var clip = AddClip<InfluenceClip>(track, 0.0, 12.0, title);
        var asset = (InfluenceClip)clip.asset;
        asset.originTarget = TargetSlot.Self;
        configure(asset);
        Dirty(asset);

        FinishCell(timeline, cell, WriteX, z, title, usage, WriteColor, hazard.name, "Influence", false);
    }

    // ============================================================
    //  COMPOSITE column (purple) — GridCompositeClip writes a layered mound blob.
    // ============================================================

    private static void BuildCompositeColumn()
    {
        // Row 0 — Additive mound into Objective field.
        CompositeCell(0, "Composite Mound (Additive)",
            "GridCompositeClip Field=Objective Composite=Mound(Disc base + center-peaked profile) Polarity=Additive. Bakes a CompositeShapeBlob of concentric depth LAYERS (peak at center, falling to the edge) - a smooth gradient hill rather than a flat disc. Each layer is a separate weighted ring emitted at the origin.",
            Polarity.Additive);

        // Row 1 — Subtractive mound (inverted crater).
        CompositeCell(1, "Composite Mound (Subtractive)",
            "Same Mound composite, Polarity=Subtractive: every baked layer weight is negated, carving an inverted crater into the Objective field. Proves composite polarity sign is applied per-layer at bake (CompositeBaking.TryBuild).",
            Polarity.Subtractive);
    }

    private static void CompositeCell(int row, string title, string usage, Polarity polarity)
    {
        var z = row * RowStep;
        var cell = "Comp" + row;
        var hazard = MakeHazard(cell + "_Source", new Vector3(CompositeX, ActorY, z), new Color(0.55f, 0.40f, 0.90f), true, 3.2f);

        var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
        var track = timeline.CreateTrack<CompositeTrack>(null, "Composite");
        var clip = AddClip<CompositeClip>(track, 0.0, 12.0, title);
        var asset = (CompositeClip)clip.asset;
        asset.Field = objective;
        asset.Composite = mound;
        asset.Polarity = polarity;
        asset.originTarget = TargetSlot.Self;
        Dirty(asset);

        FinishCell(timeline, cell, CompositeX, z, title, usage, CompositeColor, hazard.name, "Composite", false);
    }

    // ============================================================
    //  FLOW STEERING column (blue) — GridFlowSteeringClip MOVES the bound body.
    //  Each cell has its OWN static hazard writing Threat + an agent that steers.
    // ============================================================

    private static void BuildFlowColumn()
    {
        // Row 0 — Descend: agent flows DOWN the threat gradient (away from hazard).
        FlowCell(0, "Flow Descend (flee)", FlowBias.Descend, 2.0f,
            "GridFlowSteeringClip Field=Threat Bias=Descend MaxSpeed=2. A static RED hazard continuously stamps a +10 Threat disc nearby; the BLUE agent reads the field GRADIENT and steers DOWN it (descend = flee the threat). Writes LocalTransform.Position DIRECTLY (ignores physics). VISIBLE MOTION: the agent slides away from the hazard each loop.");

        // Row 1 — Ascend: agent flows UP toward the hazard.
        FlowCell(1, "Flow Ascend (seek)", FlowBias.Ascend, 2.0f,
            "Bias=Ascend MaxSpeed=2: the agent climbs the gradient TOWARD the threat peak (seek). FlowBias.Sign() flips the gradient direction (Descend=-1, Ascend=+1). The agent drifts toward the hazard. NO Target routing - flow steering always moves the BOUND body itself.");
    }

    private static void FlowCell(int row, string title, FlowBias bias, float maxSpeed, string usage)
    {
        var z = row * RowStep;
        var cell = "Flow" + row;

        // Static hazard writing Threat so the agent has a gradient to follow.
        var hazard = MakeHazard(cell + "_Hazard", new Vector3(FlowX, ActorY, z + 2.5f), HazardColor, false, 0f);
        AttachThreatWriter(hazard, cell + "_HazardWrite", discStamp, 3f);

        // Agent that steers; offset from the hazard so the gradient is non-zero.
        var agent = MakeAgent(cell + "_Agent", new Vector3(FlowX - 2.0f, ActorY, z - 2.0f), AgentColor);

        var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
        var track = timeline.CreateTrack<FlowTrack>(null, "Flow");
        var clip = AddClip<FlowClip>(track, 0.0, 12.0, title);
        var asset = (FlowClip)clip.asset;
        asset.Field = threat;
        asset.Bias = bias;
        asset.MaxSpeed = maxSpeed;
        Dirty(asset);

        FinishCell(timeline, cell, FlowX, z, title, usage, FlowColor, agent.name, "Flow", false);
    }

    // ============================================================
    //  QUERY column (amber) — GridInfluenceQueryClip READS value + gradient.
    // ============================================================

    private static void BuildQueryColumn()
    {
        // Row 0 — Self query of a moving-hazard field: Value rises/falls as hazard passes.
        QueryCell(0, "Query Value+Gradient",
            "GridInfluenceQueryClip Field=Threat originTarget=Self. A RED hazard ORBITS through the agent's cell writing +10 Threat; the agent's query bakes InfluenceQueryData + an empty InfluenceQueryResult{Value,Direction(int2),Cell(int2),Valid}. At runtime GridInfluenceQuerySystem fills Value=field at the agent cell, Direction=ascent gradient, Cell=int2 grid coord, Valid=1. The seam to AI/Reactions - NUMERIC proof the field is readable.",
            new Vector3(0f, 0f, 0f));

        // Row 1 — Query with LocalOffset (samples a cell ahead of the agent).
        QueryCell(1, "Query @ LocalOffset",
            "Same query with LocalOffset=(0,0,3): the read cell is offset 3 cells along +Z from the agent, so it samples a DIFFERENT location of the Threat field. result.Cell reflects the offset cell, not the agent's own cell. Demonstrates probing the field ahead of an actor.",
            new Vector3(0f, 0f, 3f));
    }

    private static void QueryCell(int row, string title, string usage, Vector3 localOffset)
    {
        var z = row * RowStep;
        var cell = "Query" + row;

        // Orbiting hazard whose disc sweeps over the agent's cell -> query value varies.
        var hazard = MakeHazard(cell + "_Hazard", new Vector3(QueryX, ActorY, z), HazardColor, true, 3.0f);
        AttachThreatWriter(hazard, cell + "_HazardWrite", discStamp, 3f);

        var agent = MakeAgent(cell + "_Agent", new Vector3(QueryX, ActorY, z), QueryColor);

        var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
        var track = timeline.CreateTrack<QueryTrack>(null, "Query");
        var clip = AddClip<QueryClip>(track, 0.0, 12.0, title);
        var asset = (QueryClip)clip.asset;
        asset.Field = threat;
        asset.originTarget = TargetSlot.Self;
        asset.LocalOffset = localOffset;
        Dirty(asset);

        FinishCell(timeline, cell, QueryX, z, title, usage, QueryColor, agent.name, "Query", false);
    }

    // ============================================================
    //  SHAPE column (red) — exhaustive Stamp ShapeKinds + Falloff modes.
    // ============================================================

    private static void BuildShapeColumn()
    {
        // Row 0 — Annulus (ring) stamp.
        ShapeCell(0, "Annulus (ring)",
            "Stamp Kind=Annulus (outer r=5, inner r=3): a weighted ring footprint - non-zero only between the two radii. Exercises InfluenceShape.Annulus rasterization.",
            c => { c.Field = threat; c.Stamp = ringStamp; c.Falloff = FalloffMode.None; });

        // Row 1 — Sector (cone) stamp.
        ShapeCell(1, "Sector (cone)",
            "Stamp Kind=Sector (r=6, facing 90deg, half-angle 30deg): a directional cone footprint (vision/threat arc). Built from two boundary rays via InfluenceShape.Sector.",
            c => { c.Field = threat; c.Stamp = sectorStamp; c.Falloff = FalloffMode.None; });

        // Row 2 — Falloff Stepped: concentric stepped rings around the stamp.
        ShapeCell(2, "Falloff Stepped",
            "Disc stamp with Falloff=Stepped, FalloffSteps=3, FalloffSpacing=2: the rasterizer adds 3 progressively weaker concentric step-rings spaced 2 cells apart around the core - a discrete distance falloff instead of a hard edge.",
            c =>
            {
                c.Field = threat; c.Stamp = discStamp; c.Falloff = FalloffMode.Stepped;
                c.FalloffSteps = 3; c.FalloffSpacing = 2;
            });
    }

    private static void ShapeCell(int row, string title, string usage, Action<InfluenceClip> configure)
    {
        var z = row * RowStep;
        var cell = "Shape" + row;
        var hazard = MakeHazard(cell + "_Stamp", new Vector3(ShapeX, ActorY, z), ShapeColor, false, 0f);

        var timeline = NewTimeline(TimelineFolder + "/" + cell + ".playable");
        var track = timeline.CreateTrack<InfluenceTrack>(null, "Influence");
        var clip = AddClip<InfluenceClip>(track, 0.0, 12.0, title);
        var asset = (InfluenceClip)clip.asset;
        asset.originTarget = TargetSlot.Self;
        asset.Polarity = Polarity.Additive;
        asset.WeightMultiplier = 1f;
        asset.Category = GridFieldCategory.Threat;
        configure(asset);
        Dirty(asset);

        FinishCell(timeline, cell, ShapeX, z, title, usage, ShapeColor, hazard.name, "Influence", false);
    }

    // ============================================================
    //  actor builders
    // ============================================================

    // A ECS-pure hazard cube; binds TargetsAuthoring; optionally orbits.
    private static GameObject MakeHazard(string name, Vector3 pos, Color color, bool moving, float reach)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, color);
        go.AddComponent<LifeCycleAuthoring>();

        var targets = go.AddComponent<TargetsAuthoring>();
        targets.Owner = go; targets.Source = go; targets.Custom = go; targets.Target = go;

        SceneManager.MoveGameObjectToScene(go, activeSub);

        if (moving && reach > 0f)
            DriveOrbit(name, go, pos, reach);

        return go;
    }

    // A transform-driven ECS-pure agent (capsule) for flow steering / query.
    private static GameObject MakeAgent(string name, Vector3 pos, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = pos;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, color);
        go.AddComponent<LifeCycleAuthoring>();

        var targets = go.AddComponent<TargetsAuthoring>();
        targets.Owner = go; targets.Source = go; targets.Custom = go; targets.Target = go;

        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    // Adds a continuously-stamping Threat writer director to a hazard, so a field
    // exists for flow/query cells whose hazard does not itself carry the write clip.
    private static void AttachThreatWriter(GameObject hazard, string dirName, StampSchema stamp, float reach)
    {
        MakeDirector(dirName);
        var timeline = NewTimeline(TimelineFolder + "/" + dirName + ".playable");
        var track = timeline.CreateTrack<InfluenceTrack>(null, "Influence");
        var clip = AddClip<InfluenceClip>(track, 0.0, 12.0, "Threat stamp");
        var asset = (InfluenceClip)clip.asset;
        asset.Field = threat;
        asset.Stamp = stamp;
        asset.Polarity = Polarity.Additive;
        asset.WeightMultiplier = 1f;
        asset.originTarget = TargetSlot.Self;
        asset.Category = GridFieldCategory.Threat;
        Dirty(asset);
        FixDuration(timeline);
        Dirty(timeline);
        foreach (var tr in timeline.GetOutputTracks()) Dirty(tr);
        AssetDatabase.SaveAssets();

        Wires.Add(new CellWire
        {
            DirectorName = dirName,
            TimelinePath = AssetDatabase.GetAssetPath(timeline),
            TrackName = "Influence",
            BindName = hazard.name,
            BindTransform = false,
        });
    }

    // Sweeps a body along an open path so its stamp footprint travels each loop.
    private static void DriveOrbit(string name, GameObject go, Vector3 home, float reach)
    {
        var dirName = name + "_OrbitDir";
        var timelinePath = TimelineFolder + "/" + name + "_Orbit.playable";
        MakeDirector(dirName);

        var timeline = NewTimeline(timelinePath);
        var track = timeline.CreateTrack<PositionTrack>(null, "Position");
        track.ResetPositionOnDeactivate = true;

        var a = AddWorldPos(track, 0.0, 3.0, "+X", home + new Vector3(reach, 0f, 0f));
        var b = AddWorldPos(track, 3.0, 3.0, "+Z", home + new Vector3(0f, 0f, reach));
        var d = AddWorldPos(track, 6.0, 3.0, "-X", home + new Vector3(-reach, 0f, 0f));
        var e = AddWorldPos(track, 9.0, 3.0, "home", home);
        a.blendInDuration = 0.6; b.blendInDuration = 0.6; d.blendInDuration = 0.6; e.blendInDuration = 0.6;

        FixDuration(timeline);
        Dirty(timeline, track);
        AssetDatabase.SaveAssets();

        Wires.Add(new CellWire
        {
            DirectorName = dirName,
            TimelinePath = timelinePath,
            TrackName = "Position",
            BindName = go.name,
            BindTransform = true,
        });
    }

    private static TimelineClip AddWorldPos(PositionTrack t, double start, double dur, string name, Vector3 world)
    {
        var c = AddClip<PositionClip>(t, start, dur, name);
        var a = (PositionClip)c.asset;
        a.Type = PositionType.World;
        a.Position = world;
        Dirty(c.asset);
        return c;
    }

    // ============================================================
    //  wire / caption plumbing
    // ============================================================

    private static void FinishCell(TimelineAsset timeline, string cell, float x, float z,
        string label, string usage, Color color, string bindName, string trackName, bool bindTransform)
    {
        FixDuration(timeline);
        Dirty(timeline);
        foreach (var tr in timeline.GetOutputTracks()) Dirty(tr);
        AssetDatabase.SaveAssets();

        var dirName = cell + "_Director";
        MakeDirector(dirName);
        Wires.Add(new CellWire
        {
            DirectorName = dirName,
            TimelinePath = AssetDatabase.GetAssetPath(timeline),
            TrackName = trackName,
            BindName = bindName,
            BindTransform = bindTransform,
        });
        Captions.Add(new CaptionData { Title = label, Usage = usage, CellPos = new Vector3(x, 5.0f, z), Color = color });
    }

    private static void WireCell(CellWire w)
    {
        var director = GameObject.Find(w.DirectorName).GetComponent<PlayableDirector>();
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(w.TimelinePath);
        director.playableAsset = timeline;

        foreach (var track in timeline.GetOutputTracks())
        {
            if (track.name != w.TrackName) continue;
            var actor = GameObject.Find(w.BindName);
            if (w.BindTransform)
                director.SetGenericBinding(track, actor.transform);
            else
                director.SetGenericBinding(track, actor.GetComponent<TargetsAuthoring>());
        }

        EditorUtility.SetDirty(director);
    }

    private static PlayableDirector MakeDirector(string name)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, activeSub);
        var director = go.AddComponent<PlayableDirector>();
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Loop;
        var begin = go.AddComponent<TimelineBeginAuthoring>();
        begin.Mode = TimelineBeginMode.OnLoad;
        begin.DelaySeconds = 0f;
        return director;
    }

    private static TimelineAsset NewTimeline(string path)
    {
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, path);
        return timeline;
    }

    private static TimelineClip AddClip<T>(TrackAsset track, double start, double duration, string name)
        where T : PlayableAsset
    {
        var clip = track.CreateClip<T>();
        clip.start = start;
        clip.duration = duration;
        clip.displayName = name;
        return clip;
    }

    private static void FixDuration(TimelineAsset timeline)
    {
        var end = 0.0;
        foreach (var track in timeline.GetOutputTracks())
            foreach (var clip in track.GetClips())
            {
                var clipEnd = clip.start + clip.duration;
                if (clipEnd > end) end = clipEnd;
            }

        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = end;
    }

    // ============================================================
    //  schema asset creation
    // ============================================================

    private static void EnsureSchemas()
    {
        threat = LoadOrCreateField(ThreatFieldPath, "Threat", true, 30);
        objective = LoadOrCreateField(ObjectiveFieldPath, "Objective", false, 0);

        discStamp = LoadOrCreateStamp(DiscStampPath, s =>
        {
            s.Kind = ShapeKind.Disc; s.BaseWeight = 10; s.DiscCenter = Vector2Int.zero; s.DiscRadius = 5;
        });
        ringStamp = LoadOrCreateStamp(RingStampPath, s =>
        {
            s.Kind = ShapeKind.Annulus; s.BaseWeight = 8; s.AnnulusCenter = Vector2Int.zero;
            s.AnnulusOuterRadius = 5; s.AnnulusInnerRadius = 3;
        });
        sectorStamp = LoadOrCreateStamp(SectorStampPath, s =>
        {
            s.Kind = ShapeKind.Sector; s.BaseWeight = 8; s.SectorCenter = Vector2Int.zero;
            s.SectorRadius = 6; s.SectorFacingDegrees = 90f; s.SectorHalfAngleDegrees = 30f;
        });

        mound = AssetDatabase.LoadAssetAtPath<CompositeSchema>(CompositePath);
        if (mound == null)
        {
            mound = ScriptableObject.CreateInstance<CompositeSchema>();
            AssetDatabase.CreateAsset(mound, CompositePath);
        }
        mound.Base = discStamp;
        mound.Profile = new CompositeProfile
        {
            Peak = 10,
            Levels = 6,
            DistanceToWeight = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f),
        };
        EditorUtility.SetDirty(mound);

        settings = AssetDatabase.LoadAssetAtPath<GridSettings>(SettingsPath);
        if (settings != null)
        {
            settings.CellSize = 1f;
            settings.PlaneNormal = Vector3.up;
            settings.StrideAlignment = 8;
            settings.Fields = MergeFields(settings.Fields, threat, objective);
            settings.Stamps = MergeStamps(settings.Stamps, discStamp, ringStamp, sectorStamp);
            EditorUtility.SetDirty(settings);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        // Reload to pick up AutoRef/IUID id assignments.
        threat = AssetDatabase.LoadAssetAtPath<FieldSchema>(ThreatFieldPath);
        objective = AssetDatabase.LoadAssetAtPath<FieldSchema>(ObjectiveFieldPath);
        discStamp = AssetDatabase.LoadAssetAtPath<StampSchema>(DiscStampPath);
        ringStamp = AssetDatabase.LoadAssetAtPath<StampSchema>(RingStampPath);
        sectorStamp = AssetDatabase.LoadAssetAtPath<StampSchema>(SectorStampPath);
        mound = AssetDatabase.LoadAssetAtPath<CompositeSchema>(CompositePath);
        settings = AssetDatabase.LoadAssetAtPath<GridSettings>(SettingsPath);

        // Force-ensure both Threat and Objective ARE in settings.Fields (the #1 trap).
        if (settings != null)
        {
            settings.Fields = MergeFields(settings.Fields, threat, objective);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }

    private static FieldSchema LoadOrCreateField(string path, string fieldName, bool doubleBuffered, int decayPerMille)
    {
        var f = AssetDatabase.LoadAssetAtPath<FieldSchema>(path);
        if (f == null)
        {
            f = ScriptableObject.CreateInstance<FieldSchema>();
            AssetDatabase.CreateAsset(f, path);
        }
        f.FieldName = fieldName;
        f.ChunkPower = 4;
        f.RetentionFrames = 300;
        f.DoubleBuffered = doubleBuffered;
        f.DecayPerMille = decayPerMille;
        f.SpreadDenominator = 5;
        EditorUtility.SetDirty(f);
        return f;
    }

    private static StampSchema LoadOrCreateStamp(string path, Action<StampSchema> configure)
    {
        var s = AssetDatabase.LoadAssetAtPath<StampSchema>(path);
        if (s == null)
        {
            s = ScriptableObject.CreateInstance<StampSchema>();
            AssetDatabase.CreateAsset(s, path);
        }
        configure(s);
        EditorUtility.SetDirty(s);
        return s;
    }

    private static FieldSchema[] MergeFields(FieldSchema[] existing, params FieldSchema[] add)
    {
        var list = new List<FieldSchema>();
        if (existing != null)
            foreach (var e in existing)
                if (e != null && !list.Contains(e)) list.Add(e);
        foreach (var a in add)
            if (a != null && !list.Contains(a)) list.Add(a);
        return list.ToArray();
    }

    private static StampSchema[] MergeStamps(StampSchema[] existing, params StampSchema[] add)
    {
        var list = new List<StampSchema>();
        if (existing != null)
            foreach (var e in existing)
                if (e != null && !list.Contains(e)) list.Add(e);
        foreach (var a in add)
            if (a != null && !list.Contains(a)) list.Add(a);
        return list.ToArray();
    }

    // ============================================================
    //  primitives / parent scene
    // ============================================================

    private static GameObject MakePad(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = size;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, PadColor);
        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    private static void BuildRequiredInSubScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RequiredInSubScenePath);
        if (prefab == null)
        {
            Debug.LogWarning("GridInfluenceShowcase: '" + RequiredInSubScenePath + "' missing; runtime singletons may be absent.");
            return;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "Required In Subscene";
        SceneManager.MoveGameObjectToScene(go, activeSub);
    }

    private static void BuildPads()
    {
        float[] xs = { WriteX, CompositeX, FlowX, QueryX, ShapeX };
        string[] names = { "Write", "Composite", "Flow", "Query", "Shape" };
        var zCenter = RowStep * 2f;
        for (var i = 0; i < xs.Length; i++)
            MakePad(names[i] + "_Pad", new Vector3(xs[i], 0.05f, zCenter), new Vector3(13.0f, 0.12f, RowStep * 5f + 4f));
    }

    private static Material MakeMaterial(string name, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name + "_Mat" };
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        return mat;
    }

    private static void BuildParent()
    {
        FrameCamera();
        RenderSettings.fog = false;

        MakeBanner("Title_Banner", new Vector3(0f, 23.0f, 0f), new Vector3(80f, 4.0f, 0.1f));
        MakeWorldLabel("Title", "GRID INFLUENCE TIMELINE GRID — 4 TRACKS over a shared 2D influence field",
            new Vector3(0f, 23.4f, -0.4f), 80f, Color.white, 4.4f, TextAlignmentOptions.Center);
        MakeWorldLabel("Subtitle",
            "WRITE stamp · COMPOSITE blob · FLOW STEERING (moves the body) · INFLUENCE QUERY (reads value+gradient)   ·   com.bovinelabs.timeline.grid.influence",
            new Vector3(0f, 21.7f, -0.4f), 80f, new Color(0.85f, 0.92f, 1f), 1.9f, TextAlignmentOptions.Center);

        MakeColumnHeader("Write_Header", "WRITE (Influence)", WriteX, WriteColor);
        MakeColumnHeader("Comp_Header", "COMPOSITE", CompositeX, CompositeColor);
        MakeColumnHeader("Flow_Header", "FLOW STEERING", FlowX, FlowColor);
        MakeColumnHeader("Query_Header", "INFLUENCE QUERY", QueryX, QueryColor);
        MakeColumnHeader("Shape_Header", "STAMP SHAPES / FALLOFF", ShapeX, ShapeColor);

        foreach (var cap in Captions)
            MakeCaption(cap.Title, cap.Usage, cap.CellPos, cap.Color);

        MakeBanner("Usage_Banner", new Vector3(0f, 0.7f, -11.5f), new Vector3(86f, 2.8f, 0.1f));
        MakeWorldLabel("Usage",
            "All four DOTSTracks bind TargetsAuthoring on an ECS-pure body. The grid is a world-space 2D integer field (PlaneNormal=up -> XZ plane, CellSize=1). Prereq: InfluenceGridSettingsAuthoring lists BOTH Threat & Objective fields (a field NOT listed = silent no-op). WRITE/SHAPE = a RED hazard stamps the Threat field (value=author integer, no x100). COMPOSITE = a layered mound blob into Objective. FLOW STEERING reads the field gradient and moves the BLUE agent DIRECTLY (descend=flee, ascend=seek) = VISIBLE motion. QUERY bakes InfluenceQueryResult{Value,Direction,Cell,Valid} = NUMERIC read seam. FixedLength + Loop.",
            new Vector3(0f, 0.7f, -11.8f), 84f, new Color(0.96f, 0.97f, 1f), 1.4f, TextAlignmentOptions.Center);

        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubPath);
        if (sceneAsset == null)
        {
            Debug.LogError("GridInfluenceShowcase: sub-scene asset missing at " + SubPath);
            return;
        }

        var subSceneGo = new GameObject("Showcase SubScene");
        var subScene = subSceneGo.AddComponent<SubScene>();
        subScene.SceneAsset = sceneAsset;
        subScene.AutoLoadScene = true;
        EditorUtility.SetDirty(subScene);
    }

    private static void MakeColumnHeader(string name, string text, float x, Color color)
    {
        var pos = new Vector3(x, 7.0f, -6.5f);
        MakeBanner(name + "_Banner", pos + new Vector3(0f, 0f, 0.08f), new Vector3(12.6f, 1.6f, 0.1f));
        MakeWorldLabel(name, "<b>" + text + "</b>", pos, 12.4f, color, 2.6f, TextAlignmentOptions.Center);
    }

    private static void MakeCaption(string title, string usage, Vector3 cellPos, Color color)
    {
        var z = cellPos.z;
        var y = 6.6f;
        MakeBanner("CapBanner_" + title + "_" + z, new Vector3(cellPos.x, y, z + 0.06f), new Vector3(12.4f, 2.8f, 0.05f));
        MakeWorldLabel("Cap_" + title + "_" + z, "<b>" + title + "</b>", new Vector3(cellPos.x, y + 0.8f, z), 12.2f, color, 2.0f, TextAlignmentOptions.Center);
        MakeWorldLabel("Use_" + title + "_" + z, usage, new Vector3(cellPos.x, y - 0.55f, z), 12.2f, new Color(0.95f, 0.96f, 1f), 1.0f, TextAlignmentOptions.Center);
    }

    private static void FrameCamera()
    {
        var required = GameObject.Find("Required In Scene");
        if (required == null) return;
        var camTransform = required.transform.Find("Main Camera");
        if (camTransform == null) return;
        camTransform.position = CameraPos;
        camTransform.rotation = Quaternion.Euler(24f, 0f, 0f);
        var cam = camTransform.GetComponent<Camera>();
        if (cam != null)
        {
            cam.fieldOfView = 64f;
            cam.farClipPlane = 600f;
            EditorUtility.SetDirty(cam);
        }

        EditorUtility.SetDirty(camTransform);
    }

    private static void MakeBanner(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(pos - CameraPos, Vector3.up);
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, BannerColor);
    }

    private static void MakeWorldLabel(string name, string text, Vector3 pos, float width, Color color, float fontSize, TextAlignmentOptions alignment)
    {
        var holder = new GameObject(name);
        holder.transform.position = pos;
        holder.transform.rotation = Quaternion.LookRotation(pos - CameraPos, Vector3.up);

        var go = new GameObject("Text");
        go.transform.SetParent(holder.transform, false);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.rectTransform.sizeDelta = new Vector2(width, 4f);
        tmp.rectTransform.localPosition = Vector3.zero;
        tmp.fontStyle = FontStyles.Bold;
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Samples"))
            AssetDatabase.CreateFolder("Assets", "Samples");
        if (!AssetDatabase.IsValidFolder(SampleFolder))
            AssetDatabase.CreateFolder("Assets/Samples", "GridInfluenceShowcase");
        if (!AssetDatabase.IsValidFolder(TimelineFolder))
            AssetDatabase.CreateFolder(SampleFolder, "Timelines");
        if (!AssetDatabase.IsValidFolder("Assets/Settings/Schemas/GridFields"))
            AssetDatabase.CreateFolder("Assets/Settings/Schemas", "GridFields");
        if (!AssetDatabase.IsValidFolder("Assets/Settings/Schemas/GridStamps"))
            AssetDatabase.CreateFolder("Assets/Settings/Schemas", "GridStamps");
    }

    private static void ResetAssets()
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TimelineFolder) != null)
            foreach (var guid in AssetDatabase.FindAssets("t:TimelineAsset", new[] { TimelineFolder }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));

        foreach (var p in new[] { ParentPath, SubPath })
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p) != null)
                AssetDatabase.DeleteAsset(p);
    }

    private static void Dirty(params UnityEngine.Object[] objects)
    {
        foreach (var o in objects)
            EditorUtility.SetDirty(o);
    }
}
