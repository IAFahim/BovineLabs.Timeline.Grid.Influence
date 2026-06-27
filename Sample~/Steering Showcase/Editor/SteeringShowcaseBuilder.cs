using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Authoring;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using TargetSlot = BovineLabs.Reaction.Data.Core.Target;
using TargetsAuthoring = BovineLabs.Reaction.Authoring.Core.TargetsAuthoring;
using LifeCycleAuthoring = BovineLabs.Core.Authoring.LifeCycle.LifeCycleAuthoring;
using LinearPIDTrack = BovineLabs.Timeline.Physics.Authoring.PIDs.PhysicsLinearPIDTrack;
using LinearPIDClip = BovineLabs.Timeline.Physics.Authoring.PIDs.PhysicsLinearPIDClip;
using LinearMode = BovineLabs.Timeline.Physics.PidLinearTargetMode;
using AngularPIDTrack = BovineLabs.Timeline.Physics.Authoring.PIDs.PhysicsAngularPIDTrack;
using AngularPIDClip = BovineLabs.Timeline.Physics.Authoring.PIDs.PhysicsAngularPIDClip;
using AngularMode = BovineLabs.Timeline.Physics.PidAngularTargetMode;
using VelocityTrack = BovineLabs.Timeline.Physics.Authoring.PhysicsVelocityTrack;
using VelocityClip = BovineLabs.Timeline.Physics.Authoring.PhysicsVelocityClip;
using VelocityMode = BovineLabs.Timeline.Physics.Data.PhysicsVelocityMode;
using ClampTrack = BovineLabs.Timeline.Physics.Authoring.VelocityClamps.PhysicsVelocityClampTrack;
using ClampClip = BovineLabs.Timeline.Physics.Authoring.VelocityClamps.PhysicsVelocityClampClip;
using FlowTrack = BovineLabs.Timeline.Grid.Influence.Authoring.GridFlowSteeringTrack;
using FlowClip = BovineLabs.Timeline.Grid.Influence.Authoring.GridFlowSteeringClip;
using FlowBias = BovineLabs.Timeline.Grid.Influence.Data.Flows.FlowBias;
using InfluenceTrack = BovineLabs.Timeline.Grid.Influence.Authoring.GridInfluenceTrack;
using InfluenceClip = BovineLabs.Timeline.Grid.Influence.Authoring.GridInfluenceClip;
using CompositeTrack = BovineLabs.Timeline.Grid.Influence.Authoring.GridCompositeTrack;
using CompositeClip = BovineLabs.Timeline.Grid.Influence.Authoring.GridCompositeClip;
using FieldSchema = BovineLabs.Timeline.Grid.Influence.Authoring.GridFieldSchemaObject;
using StampSchema = BovineLabs.Timeline.Grid.Influence.Authoring.GridStampSchemaObject;
using CompositeSchema = BovineLabs.Timeline.Grid.Influence.Authoring.GridCompositeSchemaObject;
using Polarity = BovineLabs.Timeline.Grid.Influence.Authoring.Polarity;

// Steering-behaviours showcase: classic Reynolds-style steering composed from the
// SAME Timeline tracks the rest of the project uses. Two paradigms side by side:
//   PHYSICS PID  (dynamic bodies, real acceleration) -> Seek/Arrive, Pursue, Offset Pursuit, Evade, Look-At
//   GRID FIELD   (influence field + gradient slide)  -> Flee, Seek, Separation
// Reuses the existing Threat/Objective fields + Disc stamp + Mound composite (no new schemas).
public static class SteeringShowcaseBuilder
{
    private const string SampleFolder = "Assets/Samples/SteeringShowcase";
    private const string TimelineFolder = SampleFolder + "/Timelines";
    private const string MaterialFolder = SampleFolder + "/Materials";
    private const string ParentPath = SampleFolder + "/SteeringShowcase.unity";
    private const string SubPath = SampleFolder + "/SteeringShowcase_Sub.unity";

    private const string RequiredInSubScenePath = "Assets/Prefabs/Required In Subscene.prefab";
    private const string ThreatFieldPath = "Assets/Settings/Schemas/GridFields/Threat.asset";
    private const string ObjectiveFieldPath = "Assets/Settings/Schemas/GridFields/Objective.asset";
    private const string MoundPath = "Assets/Settings/Schemas/GridFields/Mound.asset";
    private const string DiscStampPath = "Assets/Settings/Schemas/GridStamps/Disc.asset";

    private static readonly Color PidColor = new Color(0.95f, 0.30f, 0.50f);
    private static readonly Color TargetColor = new Color(0.95f, 0.20f, 0.20f);
    private static readonly Color GridAgentColor = new Color(0.30f, 0.55f, 0.95f);
    private static readonly Color HazardColor = new Color(0.92f, 0.30f, 0.28f);
    private static readonly Color ObjectiveColor = new Color(0.35f, 0.85f, 0.45f);
    private static readonly Color PadColor = new Color(0.22f, 0.24f, 0.29f);
    private static readonly Color GridPadColor = new Color(0.18f, 0.20f, 0.26f);
    private static readonly Color BannerColor = new Color(0.06f, 0.08f, 0.12f);

    // PID columns (dynamic physics bodies on a collider pad).
    private const float SeekX = -46f;
    private const float PursueX = -36f;
    private const float OffsetX = -26f;
    private const float EvadeX = -16f;
    private const float LookAtX = -6f;
    // Grid columns (transform agents on a flat visual pad).
    private const float FleeX = 8f;
    private const float SeekFieldX = 20f;
    private const float SepX = 34f;

    private const float BallY = 0.75f;

    private const uint CatGround = 1u << 0;
    private const uint CatBody = 1u << 1;
    private const uint CatTrigger = 1u << 3;

    private static readonly Vector3 CameraPos = new Vector3(0f, 24f, -52f);

    private static Scene activeSub;
    private static FieldSchema threat;
    private static FieldSchema objective;
    private static CompositeSchema mound;
    private static StampSchema disc;

    private enum BindKind { Body, Targets }

    private sealed class TrackBind
    {
        public string TrackName;
        public string BindName;
        public BindKind Kind;
    }

    private sealed class CellWire
    {
        public string DirectorName;
        public string TimelinePath;
        public string BindName;
        public BindKind DefaultKind = BindKind.Body;
        public List<TrackBind> Binds = new List<TrackBind>();
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

    [MenuItem("Showcase/Build Steering")]
    public static void Build()
    {
        Wires.Clear();
        Captions.Clear();
        MatCache.Clear();
        EnsureFolders();
        LoadSchemas();
        ResetAssets();

        var parent = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(parent, ParentPath);
        var sub = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(sub);
        activeSub = sub;

        BuildRequiredInSubScene();
        BuildPads();

        BuildSeekCell();
        BuildPursueCell();
        BuildOffsetPursuitCell();
        BuildEvadeCell();
        BuildLookAtCell();

        BuildFleeFieldCell();
        BuildSeekFieldCell();
        BuildSeparationCell();

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

        Debug.Log("SteeringShowcase: built at " + ParentPath + " | directors=" + Wires.Count +
                  " Threat.Id=" + (threat != null ? threat.Id.ToString() : "MISSING") +
                  " Objective.Id=" + (objective != null ? objective.Id.ToString() : "MISSING"));
    }

    // ============================================================
    //  PHYSICS PID behaviours (dynamic bodies, real acceleration)
    // ============================================================

    // SEEK / ARRIVE — LinearPID drives a force toward a fixed world point; PID damping
    // makes it ease in and settle (a well-tuned PID IS Arrive — strength/tuning = how hard it brakes).
    private static void BuildSeekCell()
    {
        var actor = MakeBall("Seek_Actor", new Vector3(SeekX - 3f, BallY + 0.5f, -3f), 0.5f, PidColor, 0f, true);
        var goal = MakeBall("Seek_Goal", new Vector3(SeekX + 2f, BallY + 0.5f, 3f), 0.35f, TargetColor, 0f, false);
        SetBodyFilter(goal, CatTrigger, 0u);

        var timeline = NewTimeline(TimelineFolder + "/Seek.playable");
        var pt = timeline.CreateTrack<LinearPIDTrack>(null, "Linear PID");
        var clip = AddClip<LinearPIDClip>(pt, 0.0, 6.0, "seek->point");
        var ca = (LinearPIDClip)clip.asset;
        ca.trackingTarget = TargetSlot.None;
        ca.targetMode = LinearMode.World;
        ca.targetOffset = new Vector3(SeekX + 2f, BallY + 0.5f, 3f);
        ca.strength = 1f;
        Dirty(clip.asset);

        var wire = MakeWire(timeline, "Seek", "Seek_Actor", BindKind.Body);
        FinishWire(timeline, wire, SeekX, "Seek -> Arrive", PidColor,
            "PhysicsLinearPIDClip (World mode) applies force toward a FIXED point (red marker). PID damping eases the body in and holds it = Seek that naturally Arrives. Strength/tuning decides how hard it brakes. Real accelerated motion on a dynamic body.");
    }

    // PURSUE — LinearPID TargetLocal chases a MOVING target (offset 0) forever.
    private static void BuildPursueCell()
    {
        var actor = MakeBall("Pursue_Actor", new Vector3(PursueX - 2f, BallY + 0.5f, -3f), 0.5f, PidColor, 0f, true);
        var target = MakeBall("Pursue_Target", new Vector3(PursueX, BallY + 0.5f, 0f), 0.4f, TargetColor, 0f, true);
        SetBodyFilter(target, CatTrigger, 0u);
        AddTargets("Pursue_Actor", "Pursue_Target");
        BuildOrbitTarget("Pursue", "Pursue_Target", new Vector3(PursueX, BallY + 0.5f, 0f));

        var timeline = NewTimeline(TimelineFolder + "/Pursue.playable");
        var pt = timeline.CreateTrack<LinearPIDTrack>(null, "Linear PID");
        var clip = AddClip<LinearPIDClip>(pt, 0.0, 6.0, "pursue");
        var ca = (LinearPIDClip)clip.asset;
        ca.trackingTarget = TargetSlot.Target;
        ca.targetMode = LinearMode.TargetLocal;
        ca.targetOffset = Vector3.zero;
        ca.strength = 1f;
        Dirty(clip.asset);

        var wire = MakeWire(timeline, "Pursue", "Pursue_Actor", BindKind.Body);
        FinishWire(timeline, wire, PursueX, "Pursue", PidColor,
            "PhysicsLinearPIDClip (TargetLocal, offset 0) forces the body toward an ORBITING target every frame -> it chases forever. trackingTarget=Target resolves the moving red ball via the Targets component.");
    }

    // OFFSET PURSUIT — same, but hold a fixed offset in the target's local space (escort/flank).
    private static void BuildOffsetPursuitCell()
    {
        var actor = MakeBall("Offset_Actor", new Vector3(OffsetX - 2f, BallY + 0.5f, -3f), 0.5f, PidColor, 0f, true);
        var target = MakeBall("Offset_Target", new Vector3(OffsetX, BallY + 0.5f, 0f), 0.4f, TargetColor, 0f, true);
        SetBodyFilter(target, CatTrigger, 0u);
        AddTargets("Offset_Actor", "Offset_Target");
        BuildOrbitTarget("Offset", "Offset_Target", new Vector3(OffsetX, BallY + 0.5f, 0f));

        var timeline = NewTimeline(TimelineFolder + "/OffsetPursuit.playable");
        var pt = timeline.CreateTrack<LinearPIDTrack>(null, "Linear PID");
        var clip = AddClip<LinearPIDClip>(pt, 0.0, 6.0, "offset pursue");
        var ca = (LinearPIDClip)clip.asset;
        ca.trackingTarget = TargetSlot.Target;
        ca.targetMode = LinearMode.TargetLocal;
        ca.targetOffset = new Vector3(2.5f, 0f, 0f);
        ca.strength = 1f;
        Dirty(clip.asset);

        var wire = MakeWire(timeline, "Offset", "Offset_Actor", BindKind.Body);
        FinishWire(timeline, wire, OffsetX, "Offset Pursuit", PidColor,
            "PhysicsLinearPIDClip (TargetLocal, targetOffset=(2.5,0,0)) holds a fixed offset in the MOVING target's local frame -> the body escorts/flanks the orbiting target instead of landing on it.");
    }

    // EVADE / FLEE — LinearPID FleeFromTarget pushes away from a moving target.
    private static void BuildEvadeCell()
    {
        var actor = MakeBall("Evade_Actor", new Vector3(EvadeX, BallY + 0.5f, 0f), 0.5f, PidColor, 0f, true);
        var target = MakeBall("Evade_Target", new Vector3(EvadeX, BallY + 0.5f, -2f), 0.4f, TargetColor, 0f, true);
        SetBodyFilter(target, CatTrigger, 0u);
        AddTargets("Evade_Actor", "Evade_Target");
        BuildOrbitTarget("Evade", "Evade_Target", new Vector3(EvadeX, BallY + 0.5f, 0f));

        var home = new Vector3(EvadeX, BallY + 0.5f, 0f);
        var timeline = NewTimeline(TimelineFolder + "/Evade.playable");
        // FLEE: push away from the orbiting target.
        var pt = timeline.CreateTrack<LinearPIDTrack>(null, "Linear PID");
        var clip = AddClip<LinearPIDClip>(pt, 0.0, 6.0, "evade");
        var ca = (LinearPIDClip)clip.asset;
        ca.trackingTarget = TargetSlot.Target;
        ca.targetMode = LinearMode.FleeFromTarget;
        ca.targetOffset = Vector3.zero;
        ca.strength = 1f;
        Dirty(clip.asset);
        // LEASH: weak pull back to home so flee stays in frame (FleeFromTarget is otherwise unbounded).
        var lt = timeline.CreateTrack<LinearPIDTrack>(null, "Leash");
        var lc = AddClip<LinearPIDClip>(lt, 0.0, 6.0, "leash home");
        var la = (LinearPIDClip)lc.asset;
        la.trackingTarget = TargetSlot.None;
        la.targetMode = LinearMode.World;
        la.targetOffset = home;
        la.strength = 0.35f;
        Dirty(lc.asset);
        // CLAMP: hard speed cap so it can never rocket off.
        var ct = timeline.CreateTrack<ClampTrack>(null, "Clamp");
        var cc = AddClip<ClampClip>(ct, 0.0, 6.0, "clamp");
        var cca = (ClampClip)cc.asset;
        cca.maxLinearSpeed = 4f;
        cca.maxAngularSpeed = 5f;
        Dirty(cc.asset);

        var wire = MakeWire(timeline, "Evade", "Evade_Actor", BindKind.Body);
        wire.Binds.Add(new TrackBind { TrackName = "Linear PID", BindName = "Evade_Actor", Kind = BindKind.Body });
        wire.Binds.Add(new TrackBind { TrackName = "Leash", BindName = "Evade_Actor", Kind = BindKind.Body });
        wire.Binds.Add(new TrackBind { TrackName = "Clamp", BindName = "Evade_Actor", Kind = BindKind.Body });
        FinishWire(timeline, wire, EvadeX, "Evade / Flee", PidColor,
            "PhysicsLinearPIDClip (FleeFromTarget) forces the body AWAY from the orbiting target; a weak World-PID leash + VelocityClamp keep the dodge in frame (raw FleeFromTarget is unbounded). Positional flee, no velocity prediction.");
    }

    // LOOK-AT — AngularPID applies torque to face a moving target.
    private static void BuildLookAtCell()
    {
        var actor = MakePrimitive(PrimitiveType.Cube, "LookAt_Actor", new Vector3(LookAtX, BallY + 0.5f, 0f), new Vector3(0.45f, 0.45f, 1.6f), PidColor);
        ConfigureBody(actor, 0f, true, 0.2f);
        var nose = MakePrimitive(PrimitiveType.Sphere, "LookAt_Nose", new Vector3(LookAtX, BallY + 0.5f, 0.95f), new Vector3(0.5f, 0.5f, 0.4f), Color.white);
        nose.transform.SetParent(actor.transform, true);

        var target = MakeBall("LookAt_Target", new Vector3(LookAtX, BallY + 0.5f, 2f), 0.4f, TargetColor, 0f, true);
        SetBodyFilter(target, CatTrigger, 0u);
        AddTargets("LookAt_Actor", "LookAt_Target");
        BuildOrbitTarget("LookAt", "LookAt_Target", new Vector3(LookAtX, BallY + 0.5f, 1.6f));

        var home = new Vector3(LookAtX, BallY + 0.5f, 0f);
        var timeline = NewTimeline(TimelineFolder + "/LookAt.playable");
        var pt = timeline.CreateTrack<AngularPIDTrack>(null, "Angular PID");
        var clip = AddClip<AngularPIDClip>(pt, 0.0, 6.0, "look at");
        var ca = (AngularPIDClip)clip.asset;
        ca.trackingTarget = TargetSlot.Target;
        ca.targetMode = AngularMode.LookAtTarget;
        ca.strength = 1f;
        Dirty(clip.asset);
        // LEASH: hold position so it pivots in place (AngularPID only torques; a dynamic body otherwise drifts).
        var lt = timeline.CreateTrack<LinearPIDTrack>(null, "Leash");
        var lc = AddClip<LinearPIDClip>(lt, 0.0, 6.0, "hold");
        var la = (LinearPIDClip)lc.asset;
        la.trackingTarget = TargetSlot.None;
        la.targetMode = LinearMode.World;
        la.targetOffset = home;
        la.strength = 0.8f;
        Dirty(lc.asset);

        var wire = MakeWire(timeline, "LookAt", "LookAt_Actor", BindKind.Body);
        wire.Binds.Add(new TrackBind { TrackName = "Angular PID", BindName = "LookAt_Actor", Kind = BindKind.Body });
        wire.Binds.Add(new TrackBind { TrackName = "Leash", BindName = "LookAt_Actor", Kind = BindKind.Body });
        FinishWire(timeline, wire, LookAtX, "Look-At", PidColor,
            "PhysicsAngularPIDClip (LookAtTarget) applies TORQUE so the elongated body keeps turning its nose toward the orbiting target. The rotational counterpart of Seek; composes with a Linear PID for full pursuit.");
    }

    // ============================================================
    //  GRID FIELD behaviours (influence field + gradient slide)
    // ============================================================

    // FLEE — a static hazard stamps the Threat field; the agent slides DOWN the gradient.
    private static void BuildFleeFieldCell()
    {
        var hazard = MakeHazard("Flee_Hazard", new Vector3(FleeX, 1.0f, 2.5f), HazardColor);
        AttachWriter("Flee_HazardWrite", hazard, threat, disc, Polarity.Additive, 1f);

        var agent = MakeAgent("Flee_Agent", new Vector3(FleeX - 2f, 1.0f, -1.5f), GridAgentColor);

        var timeline = NewTimeline(TimelineFolder + "/FleeField.playable");
        var ft = timeline.CreateTrack<FlowTrack>(null, "Flow");
        var clip = AddClip<FlowClip>(ft, 0.0, 8.0, "descend");
        var ca = (FlowClip)clip.asset;
        ca.Field = threat;
        ca.Bias = FlowBias.Descend;
        ca.MaxSpeed = 1.2f;
        Dirty(clip.asset);

        var wire = MakeWire(timeline, "FleeField", "Flee_Agent", BindKind.Targets);
        FinishWire(timeline, wire, FleeX, "Flee (field)", GridAgentColor,
            "GridFlowSteeringClip (Bias=Descend) reads the Threat field GRADIENT and slides the agent DOWNHILL away from the red hazard's stamp. Constant MaxSpeed (gradient is normalized); writes LocalTransform directly.");
    }

    // SEEK (field) — a static source stamps a Mound into Objective; the agent climbs UP toward it.
    private static void BuildSeekFieldCell()
    {
        var src = MakeHazard("SeekField_Source", new Vector3(SeekFieldX, 1.0f, 2.5f), ObjectiveColor);
        AttachComposite("SeekField_SourceWrite", src, objective, mound);

        var agent = MakeAgent("SeekField_Agent", new Vector3(SeekFieldX - 2f, 1.0f, -2f), GridAgentColor);

        var timeline = NewTimeline(TimelineFolder + "/SeekField.playable");
        var ft = timeline.CreateTrack<FlowTrack>(null, "Flow");
        var clip = AddClip<FlowClip>(ft, 0.0, 8.0, "ascend");
        var ca = (FlowClip)clip.asset;
        ca.Field = objective;
        ca.Bias = FlowBias.Ascend;
        ca.MaxSpeed = 1.2f;
        Dirty(clip.asset);

        var wire = MakeWire(timeline, "SeekField", "SeekField_Agent", BindKind.Targets);
        FinishWire(timeline, wire, SeekFieldX, "Seek (field)", ObjectiveColor,
            "A green source stamps a soft Mound into the Objective field; GridFlowSteeringClip (Bias=Ascend) climbs the agent UP the gradient toward the objective hill. Context-steering toward a painted goal.");
    }

    // SEPARATION — 4 agents in a clump, each stamps a repulsor into Threat + each Descends -> they spread apart.
    private static void BuildSeparationCell()
    {
        Vector3[] starts =
        {
            new Vector3(SepX - 0.8f, 1.0f, -0.8f),
            new Vector3(SepX + 0.8f, 1.0f, -0.6f),
            new Vector3(SepX - 0.5f, 1.0f, 0.9f),
            new Vector3(SepX + 0.9f, 1.0f, 0.7f),
        };

        for (var i = 0; i < starts.Length; i++)
        {
            var agentName = "Sep_Agent" + i;
            MakeAgent(agentName, starts[i], GridAgentColor);

            var timeline = NewTimeline(TimelineFolder + "/Separation_" + i + ".playable");
            // WRITE: stamp a repulsor at self into the shared Threat field.
            var it = timeline.CreateTrack<InfluenceTrack>(null, "Influence");
            var ic = AddClip<InfluenceClip>(it, 0.0, 8.0, "self repulsor");
            var ia = (InfluenceClip)ic.asset;
            ia.Field = threat;
            ia.Stamp = disc;
            ia.Polarity = Polarity.Additive;
            ia.WeightMultiplier = 1f;
            ia.originTarget = TargetSlot.Self;
            Dirty(ic.asset);
            // MOVE: descend the field -> flee the mutual high values (neighbours).
            var ft = timeline.CreateTrack<FlowTrack>(null, "Flow");
            var fc = AddClip<FlowClip>(ft, 0.0, 8.0, "descend");
            var fa = (FlowClip)fc.asset;
            fa.Field = threat;
            fa.Bias = FlowBias.Descend;
            fa.MaxSpeed = 1.5f;
            Dirty(fc.asset);

            var wire = MakeWire(timeline, "Sep_" + i, agentName, BindKind.Targets);
            wire.Binds.Add(new TrackBind { TrackName = "Influence", BindName = agentName, Kind = BindKind.Targets });
            wire.Binds.Add(new TrackBind { TrackName = "Flow", BindName = agentName, Kind = BindKind.Targets });
            FixDuration(timeline);
            Dirty(timeline);
            foreach (var tr in timeline.GetOutputTracks()) Dirty(tr);
            AssetDatabase.SaveAssets();
            MakeDirector(wire.DirectorName);
            Wires.Add(wire);
        }

        Captions.Add(new CaptionData
        {
            Title = "Separation",
            Usage = "Each of 4 agents STAMPS a Threat repulsor at itself AND Descends the same field -> every agent flees its neighbours' weight and the clump spreads apart. Emergent separation from one shared influence field (the classic influence-map use).",
            CellPos = new Vector3(SepX, 5.5f, 0f),
            Color = GridAgentColor,
        });
    }

    // ============================================================
    //  orbit companion (velocity-driven moving target for PID cells)
    // ============================================================

    private static void BuildOrbitTarget(string baseName, string targetName, Vector3 home)
    {
        var dirName = baseName + "_TgtDirector";
        var path = TimelineFolder + "/" + baseName + "_Target.playable";
        MakeDirector(dirName);
        var timeline = NewTimeline(path);
        var track = timeline.CreateTrack<VelocityTrack>(null, "Velocity");
        var a = AddVelocity(track, 0.0, 1.4, "right", new Vector3(2.0f, 0f, 0f));
        var b = AddVelocity(track, 1.4, 1.4, "back", new Vector3(0f, 0f, -2.0f));
        var c = AddVelocity(track, 2.8, 1.4, "left", new Vector3(-2.0f, 0f, 0f));
        var d = AddVelocity(track, 4.2, 1.4, "fwd", new Vector3(0f, 0f, 2.0f));
        Blend(a, b, c, d);
        FixDuration(timeline);
        Dirty(timeline, track);
        AssetDatabase.SaveAssets();
        Wires.Add(new CellWire { DirectorName = dirName, TimelinePath = path, BindName = targetName, DefaultKind = BindKind.Body });
    }

    // A continuously-stamping writer director attached to a static grid source.
    private static void AttachWriter(string dirName, GameObject source, FieldSchema field, StampSchema stamp, Polarity polarity, float weight)
    {
        MakeDirector(dirName);
        var timeline = NewTimeline(TimelineFolder + "/" + dirName + ".playable");
        var track = timeline.CreateTrack<InfluenceTrack>(null, "Influence");
        var clip = AddClip<InfluenceClip>(track, 0.0, 8.0, "stamp");
        var asset = (InfluenceClip)clip.asset;
        asset.Field = field;
        asset.Stamp = stamp;
        asset.Polarity = polarity;
        asset.WeightMultiplier = weight;
        asset.originTarget = TargetSlot.Self;
        Dirty(asset);
        FixDuration(timeline);
        Dirty(timeline);
        foreach (var tr in timeline.GetOutputTracks()) Dirty(tr);
        AssetDatabase.SaveAssets();
        Wires.Add(new CellWire { DirectorName = dirName, TimelinePath = AssetDatabase.GetAssetPath(timeline), BindName = source.name, DefaultKind = BindKind.Targets });
    }

    private static void AttachComposite(string dirName, GameObject source, FieldSchema field, CompositeSchema composite)
    {
        MakeDirector(dirName);
        var timeline = NewTimeline(TimelineFolder + "/" + dirName + ".playable");
        var track = timeline.CreateTrack<CompositeTrack>(null, "Composite");
        var clip = AddClip<CompositeClip>(track, 0.0, 8.0, "mound");
        var asset = (CompositeClip)clip.asset;
        asset.Field = field;
        asset.Composite = composite;
        asset.Polarity = Polarity.Additive;
        asset.originTarget = TargetSlot.Self;
        Dirty(asset);
        FixDuration(timeline);
        Dirty(timeline);
        foreach (var tr in timeline.GetOutputTracks()) Dirty(tr);
        AssetDatabase.SaveAssets();
        Wires.Add(new CellWire { DirectorName = dirName, TimelinePath = AssetDatabase.GetAssetPath(timeline), BindName = source.name, DefaultKind = BindKind.Targets });
    }

    // ============================================================
    //  actor builders
    // ============================================================

    private static GameObject MakeBall(string name, Vector3 pos, float radius, Color color, float gravityFactor, bool dynamic)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = new Vector3(radius * 2f, radius * 2f, radius * 2f);
        Object.DestroyImmediate(go.GetComponent<UnityEngine.Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, color);

        var shape = go.AddComponent<PhysicsShapeAuthoring>();
        shape.SetSphere(new SphereGeometry { Center = float3.zero, Radius = 0.5f }, quaternion.identity);
        shape.OverrideRestitution = true;
        shape.Restitution = new PhysicsMaterialCoefficient { Value = 0.2f, CombineMode = Unity.Physics.Material.CombinePolicy.Maximum };
        shape.OverrideFriction = true;
        shape.Friction = new PhysicsMaterialCoefficient { Value = 0.4f, CombineMode = Unity.Physics.Material.CombinePolicy.GeometricMean };
        shape.OverrideBelongsTo = true;
        shape.BelongsTo = MakeTags(CatBody);
        shape.OverrideCollidesWith = true;
        shape.CollidesWith = MakeTags(CatGround | CatBody);

        var body = go.AddComponent<PhysicsBodyAuthoring>();
        body.MotionType = dynamic ? BodyMotionType.Dynamic : BodyMotionType.Static;
        body.Mass = 1f;
        body.GravityFactor = gravityFactor;
        body.LinearDamping = 0.05f;
        body.AngularDamping = 0.05f;

        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    private static void ConfigureBody(GameObject go, float gravityFactor, bool dynamic, float restitution)
    {
        var shape = go.AddComponent<PhysicsShapeAuthoring>();
        shape.SetBox(new BoxGeometry { Center = float3.zero, Size = new float3(1f, 1f, 1f), Orientation = quaternion.identity, BevelRadius = 0.02f });
        shape.OverrideRestitution = true;
        shape.Restitution = new PhysicsMaterialCoefficient { Value = restitution, CombineMode = Unity.Physics.Material.CombinePolicy.Maximum };
        shape.OverrideBelongsTo = true;
        shape.BelongsTo = MakeTags(CatBody);
        shape.OverrideCollidesWith = true;
        shape.CollidesWith = MakeTags(CatGround | CatBody);

        var body = go.AddComponent<PhysicsBodyAuthoring>();
        body.MotionType = dynamic ? BodyMotionType.Dynamic : BodyMotionType.Static;
        body.Mass = 1f;
        body.GravityFactor = gravityFactor;
        body.LinearDamping = 0.05f;
        body.AngularDamping = 0.05f;
    }

    private static void SetBodyFilter(GameObject go, uint belongsTo, uint collidesWith)
    {
        var shape = go.GetComponent<PhysicsShapeAuthoring>();
        shape.OverrideBelongsTo = true;
        shape.BelongsTo = MakeTags(belongsTo);
        shape.OverrideCollidesWith = true;
        shape.CollidesWith = MakeTags(collidesWith);
        EditorUtility.SetDirty(shape);
    }

    // Transform-driven ECS-pure grid agent (capsule, no physics body) for flow steering.
    private static GameObject MakeAgent(string name, Vector3 pos, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = pos;
        Object.DestroyImmediate(go.GetComponent<UnityEngine.Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, color);
        go.AddComponent<LifeCycleAuthoring>();
        var targets = go.AddComponent<TargetsAuthoring>();
        targets.Owner = go; targets.Source = go; targets.Custom = go; targets.Target = go;
        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    // A static grid source cube (carries TargetsAuthoring; stamps a field from its own cell).
    private static GameObject MakeHazard(string name, Vector3 pos, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
        Object.DestroyImmediate(go.GetComponent<UnityEngine.Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, color);
        go.AddComponent<LifeCycleAuthoring>();
        var targets = go.AddComponent<TargetsAuthoring>();
        targets.Owner = go; targets.Source = go; targets.Custom = go; targets.Target = go;
        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    private static GameObject MakePrimitive(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<UnityEngine.Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, color);
        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    private static GameObject MakePad(string name, Vector3 pos, Vector3 size, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = size;
        Object.DestroyImmediate(go.GetComponent<UnityEngine.Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, collider ? PadColor : GridPadColor);

        if (collider)
        {
            var shape = go.AddComponent<PhysicsShapeAuthoring>();
            shape.SetBox(new BoxGeometry { Center = float3.zero, Size = new float3(1f, 1f, 1f), Orientation = quaternion.identity, BevelRadius = 0.02f });
            shape.OverrideRestitution = true;
            shape.Restitution = new PhysicsMaterialCoefficient { Value = 0f, CombineMode = Unity.Physics.Material.CombinePolicy.Maximum };
            shape.OverrideBelongsTo = true;
            shape.BelongsTo = MakeTags(CatGround);
            shape.OverrideCollidesWith = true;
            shape.CollidesWith = MakeTags(CatBody);
            var body = go.AddComponent<PhysicsBodyAuthoring>();
            body.MotionType = BodyMotionType.Static;
        }

        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    private static void BuildPads()
    {
        // Collider pad under the PID bodies (so dynamic balls rest on it).
        MakePad("Pid_Pad", new Vector3((SeekX + LookAtX) / 2f, 0.05f, 0f), new Vector3(52f, 0.12f, 14f), true);
        // Flat visual pad under the grid agents (no collider — agents are transform-only).
        MakePad("Grid_Pad", new Vector3((FleeX + SepX) / 2f, 0.05f, 0f), new Vector3(40f, 0.1f, 14f), false);
    }

    // ============================================================
    //  wire / clip plumbing
    // ============================================================

    private static CellWire MakeWire(TimelineAsset timeline, string baseName, string bindName, BindKind defaultKind)
    {
        return new CellWire
        {
            DirectorName = baseName + "_Director",
            TimelinePath = AssetDatabase.GetAssetPath(timeline),
            BindName = bindName,
            DefaultKind = defaultKind,
        };
    }

    private static void FinishWire(TimelineAsset timeline, CellWire wire, float x, string label, Color color, string usage)
    {
        FixDuration(timeline);
        Dirty(timeline);
        foreach (var tr in timeline.GetOutputTracks()) Dirty(tr);
        AssetDatabase.SaveAssets();
        MakeDirector(wire.DirectorName);
        Wires.Add(wire);
        Captions.Add(new CaptionData { Title = label, Usage = usage, CellPos = new Vector3(x, 5.5f, 0f), Color = color });
    }

    private static void WireCell(CellWire w)
    {
        var director = GameObject.Find(w.DirectorName).GetComponent<PlayableDirector>();
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(w.TimelinePath);
        director.playableAsset = timeline;

        foreach (var track in timeline.GetOutputTracks())
        {
            var bind = FindBind(w, track.name);
            var go = GameObject.Find(bind.BindName);
            Object value = bind.Kind == BindKind.Targets ? go.GetComponent<TargetsAuthoring>() : (Object)go.GetComponent<PhysicsBodyAuthoring>();
            director.SetGenericBinding(track, value);
        }

        EditorUtility.SetDirty(director);
    }

    private static TrackBind FindBind(CellWire w, string trackName)
    {
        foreach (var b in w.Binds)
            if (b.TrackName == trackName)
                return b;
        return new TrackBind { TrackName = trackName, BindName = w.BindName, Kind = w.DefaultKind };
    }

    private static void AddTargets(string actorName, string targetName)
    {
        var actor = GameObject.Find(actorName);
        var target = GameObject.Find(targetName);
        var targets = actor.GetComponent<TargetsAuthoring>();
        if (targets == null)
            targets = actor.AddComponent<TargetsAuthoring>();
        targets.Target = target;
        targets.Owner = actor;
        EditorUtility.SetDirty(targets);
    }

    private static TimelineClip AddVelocity(VelocityTrack t, double start, double dur, string name, Vector3 vel)
    {
        var c = AddClip<VelocityClip>(t, start, dur, name);
        var a = (VelocityClip)c.asset;
        a.mode = VelocityMode.SetContinuous;
        a.space = TargetSlot.None;
        a.linearVelocity = vel;
        Dirty(c.asset);
        return c;
    }

    private static PhysicsCategoryTags MakeTags(uint value) => new PhysicsCategoryTags { Value = value };

    private static void Blend(params TimelineClip[] clips)
    {
        foreach (var c in clips)
            c.blendInDuration = 0.4;
    }

    private static PlayableDirector MakeDirector(string name)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, activeSub);
        var director = go.AddComponent<PlayableDirector>();
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Loop;
        return director;
    }

    private static TimelineAsset NewTimeline(string path)
    {
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, path);
        return timeline;
    }

    private static TimelineClip AddClip<T>(TrackAsset track, double start, double duration, string name) where T : PlayableAsset
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
    //  schemas / scene scaffolding
    // ============================================================

    private static void LoadSchemas()
    {
        threat = AssetDatabase.LoadAssetAtPath<FieldSchema>(ThreatFieldPath);
        objective = AssetDatabase.LoadAssetAtPath<FieldSchema>(ObjectiveFieldPath);
        mound = AssetDatabase.LoadAssetAtPath<CompositeSchema>(MoundPath);
        disc = AssetDatabase.LoadAssetAtPath<StampSchema>(DiscStampPath);
        if (threat == null || objective == null || mound == null || disc == null)
            Debug.LogError("SteeringShowcase: missing grid schema asset(s) — run 'Showcase/Build Grid Influence' once first. " +
                           "threat=" + (threat != null) + " objective=" + (objective != null) + " mound=" + (mound != null) + " disc=" + (disc != null));
    }

    private static void BuildRequiredInSubScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RequiredInSubScenePath);
        if (prefab == null)
        {
            Debug.LogWarning("SteeringShowcase: '" + RequiredInSubScenePath + "' missing; runtime singletons/camera may be absent.");
            return;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "Required In Subscene";
        SceneManager.MoveGameObjectToScene(go, activeSub);
    }

    private static readonly Dictionary<string, UnityEngine.Material> MatCache = new Dictionary<string, UnityEngine.Material>();

    // Materials MUST be saved as ASSETS, not scene-embedded `new Material()`. A closed-subscene
    // EntityScene bake can only reference assets; an embedded material bakes to a NULL object
    // reference -> NullReferenceException in AsyncLoadSceneOperation.ScheduleSceneRead on stream.
    private static UnityEngine.Material MakeMaterial(string name, Color color)
    {
        var key = ColorUtility.ToHtmlStringRGB(color);
        if (MatCache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        var path = MaterialFolder + "/Mat_" + key + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
        if (mat == null)
        {
            mat = new UnityEngine.Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.shader = shader;
        }

        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(mat);
        MatCache[key] = mat;
        return mat;
    }

    private static void BuildParent()
    {
        RenderSettings.fog = false;

        MakeBanner("Title_Banner", new Vector3(0f, 21.5f, 0f), new Vector3(86f, 4.2f, 0.1f));
        MakeWorldLabel("Title", "STEERING BEHAVIOURS — composed from Timeline tracks", new Vector3(0f, 22.1f, -0.4f), 86f, Color.white, 5.5f, TextAlignmentOptions.Center);
        MakeWorldLabel("Subtitle",
            "LEFT = PHYSICS PID (real acceleration on dynamic bodies)   ·   RIGHT = GRID INFLUENCE FIELD (gradient slide)   ·   every behaviour is just tracks + clips",
            new Vector3(0f, 20.2f, -0.4f), 86f, new Color(0.85f, 0.92f, 1f), 2.2f, TextAlignmentOptions.Center);

        foreach (var cap in Captions)
            MakeCaption(cap.Title, cap.Usage, cap.CellPos, cap.Color);

        MakeBanner("Usage_Banner", new Vector3(0f, 0.7f, -9.5f), new Vector3(88f, 2.4f, 0.1f));
        MakeWorldLabel("Usage",
            "PID cells: dynamic PhysicsBody + PhysicsLinearPID/AngularPID toward a Target (orbiting red ball) — Seek/Arrive, Pursue, Offset Pursuit, Evade, Look-At. GRID cells: transform agents on a named influence field — Flee (descend Threat), Seek (ascend an Objective mound), Separation (4 agents each stamp + descend the shared field). Reuses existing Threat/Objective fields. Not shown (need a query->force bridge / velocity field): predictive Pursue, Velocity Match.",
            new Vector3(0f, 0.7f, -9.8f), 86f, new Color(0.96f, 0.97f, 1f), 1.5f, TextAlignmentOptions.Center);

        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubPath);
        if (sceneAsset == null)
        {
            Debug.LogError("SteeringShowcase: sub-scene asset missing at " + SubPath);
            return;
        }

        var subSceneGo = new GameObject("Showcase SubScene");
        var subScene = subSceneGo.AddComponent<SubScene>();
        subScene.SceneAsset = sceneAsset;
        subScene.AutoLoadScene = true;
        EditorUtility.SetDirty(subScene);
    }

    private static void MakeCaption(string title, string usage, Vector3 cellPos, Color color)
    {
        var z = cellPos.z;
        var y = 5.5f;
        MakeBanner("CapBanner_" + title, new Vector3(cellPos.x, y, z + 0.06f), new Vector3(9.2f, 3.0f, 0.05f));
        MakeWorldLabel("Cap_" + title, "<b>" + title + "</b>", new Vector3(cellPos.x, y + 1.0f, z), 9.0f, color, 2.2f, TextAlignmentOptions.Center);
        MakeWorldLabel("Use_" + title, usage, new Vector3(cellPos.x, y - 0.45f, z), 9.0f, new Color(0.95f, 0.96f, 1f), 0.95f, TextAlignmentOptions.Center);
    }

    private static void MakeBanner(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<UnityEngine.Collider>());
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(pos - CameraPos, Vector3.up);
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, BannerColor);
    }

    private static void MakeWorldLabel(string name, string text, Vector3 pos, float width, Color color, float fontSize, TextAlignmentOptions alignment)
    {
        // Pull the label toward the camera so it clears the TILTED banner plane (the banner faces a
        // camera that is above+in-front, so text coplanar with the banner-centre falls behind it).
        var faced = pos + Vector3.Normalize(CameraPos - pos) * 0.4f;
        var holder = new GameObject(name);
        holder.transform.position = faced;
        holder.transform.rotation = Quaternion.LookRotation(faced - CameraPos, Vector3.up);

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
            AssetDatabase.CreateFolder("Assets/Samples", "SteeringShowcase");
        if (!AssetDatabase.IsValidFolder(TimelineFolder))
            AssetDatabase.CreateFolder(SampleFolder, "Timelines");
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder(SampleFolder, "Materials");
    }

    private static void ResetAssets()
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(TimelineFolder) != null)
            foreach (var guid in AssetDatabase.FindAssets("t:TimelineAsset", new[] { TimelineFolder }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));

        foreach (var p in new[] { ParentPath, SubPath })
            if (AssetDatabase.LoadAssetAtPath<Object>(p) != null)
                AssetDatabase.DeleteAsset(p);
    }

    private static void Dirty(params Object[] objects)
    {
        foreach (var o in objects)
            EditorUtility.SetDirty(o);
    }
}
