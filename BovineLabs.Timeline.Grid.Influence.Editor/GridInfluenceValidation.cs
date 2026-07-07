using System.Collections.Generic;
using System.Text;
using BovineLabs.Core.ObjectManagement;
using BovineLabs.Timeline.Grid.Influence.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using UnityEditor;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Editor
{
    public static class GridInfluenceValidation
    {
        public enum Severity
        {
            Error,
            Warning,
        }

        public readonly struct Issue
        {
            public readonly Severity Severity;
            public readonly string Message;
            public readonly UnityEngine.Object Context;

            public Issue(Severity severity, string message, UnityEngine.Object context)
            {
                Severity = severity;
                Message = message;
                Context = context;
            }
        }

        [MenuItem("BovineLabs/Grid/Validate Grid Influence Setup")]
        public static void ValidateMenu()
        {
            var settingsAssets = FindAllSettings();
            if (settingsAssets.Length == 0)
            {
                Debug.LogWarning(
                    "Grid Influence validation: no InfluenceGridSettingsAuthoring asset found. Create one and list your Fields/Stamps, or clips will silently no-op.");
                return;
            }

            var issues = new List<Issue>();
            foreach (var settings in settingsAssets)
                Validate(settings, issues);

            Report(issues, "Grid Influence validation");
        }

        public static InfluenceGridSettingsAuthoring[] FindAllSettings()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(InfluenceGridSettingsAuthoring));
            var list = new List<InfluenceGridSettingsAuthoring>(guids.Length);
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<InfluenceGridSettingsAuthoring>(path);
                if (settings != null)
                    list.Add(settings);
            }

            return list.ToArray();
        }

        public static InfluenceGridSettingsAuthoring FindFirstSettings()
        {
            var all = FindAllSettings();
            return all.Length > 0 ? all[0] : null;
        }

        public static void Validate(InfluenceGridSettingsAuthoring settings, List<Issue> issues)
        {
            if (settings == null)
            {
                issues.Add(new Issue(Severity.Error, "Settings asset is null.", null));
                return;
            }

            var registered = new HashSet<GridFieldSchemaObject>();
            var keys = new Dictionary<ushort, GridFieldSchemaObject>();

            if (settings.Fields != null)
                foreach (var field in settings.Fields)
                {
                    if (field == null)
                    {
                        issues.Add(new Issue(Severity.Error, $"'{settings.name}'.Fields has a null entry.", settings));
                        continue;
                    }

                    registered.Add(field);

                    var raw = ((IUID)field).ID;
                    if (raw == 0)
                        issues.Add(new Issue(Severity.Error,
                            $"Field '{field.name}' has id 0 (unassigned AutoRef); clips will not route to it.", field));
                    else if (raw > ushort.MaxValue)
                        issues.Add(new Issue(Severity.Error,
                            $"Field '{field.name}' id {raw} exceeds 65535 and truncates to key {field.Id}.", field));

                    if (Encoding.UTF8.GetByteCount(field.FieldName ?? string.Empty) > 61)
                        issues.Add(new Issue(Severity.Error,
                            $"Field '{field.name}' FieldName '{field.FieldName}' exceeds the 61-byte FixedString64Bytes limit and will be truncated.",
                            field));

                    if (keys.TryGetValue(field.Id, out var other))
                        issues.Add(new Issue(Severity.Error,
                            $"Fields '{field.name}' and '{other.name}' share key {field.Id}; one silently shadows the other.",
                            field));
                    else
                        keys[field.Id] = field;
                }

            if (settings.Stamps != null)
                foreach (var stamp in settings.Stamps)
                    if (stamp == null)
                        issues.Add(new Issue(Severity.Warning, $"'{settings.name}'.Stamps has a null entry.", settings));

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(GridFieldSchemaObject)))
            {
                var field = AssetDatabase.LoadAssetAtPath<GridFieldSchemaObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (field != null && !registered.Contains(field))
                    issues.Add(new Issue(Severity.Warning,
                        $"Field '{field.name}' is not listed in '{settings.name}'.Fields; clips using it will silently no-op.",
                        field));
            }

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(GridCompositeSchemaObject)))
            {
                var composite =
                    AssetDatabase.LoadAssetAtPath<GridCompositeSchemaObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (composite == null)
                    continue;

                if (composite.Base == null)
                    issues.Add(new Issue(Severity.Warning,
                        $"Composite '{composite.name}' has no Base shape; it will bake nothing.", composite));
                else if (composite.Base.Kind == ShapeKind.Painted)
                    issues.Add(new Issue(Severity.Warning,
                        $"Composite '{composite.name}' has a Painted Base; Painted stamps have no composite form and will be skipped.",
                        composite));
            }
        }

        public static (int Errors, int Warnings) Summarize(List<Issue> issues)
        {
            var errors = 0;
            var warnings = 0;
            foreach (var issue in issues)
                if (issue.Severity == Severity.Error)
                    errors++;
                else
                    warnings++;

            return (errors, warnings);
        }

        public static void Report(List<Issue> issues, string label)
        {
            foreach (var issue in issues)
                if (issue.Severity == Severity.Error)
                    Debug.LogError(label + ": " + issue.Message, issue.Context);
                else
                    Debug.LogWarning(label + ": " + issue.Message, issue.Context);

            var (errors, warnings) = Summarize(issues);
            if (errors == 0 && warnings == 0)
                Debug.Log(label + ": OK (0 errors / 0 warnings).");
            else
                Debug.Log($"{label}: {errors} error(s) / {warnings} warning(s).");
        }

        // TODO (TODO-20): also run these rules from an IPreprocessBuildWithReport hook so builds fail hard on
        // duplicate/unassigned keys. Left out here to avoid duplicate logging and build-order coupling.
    }
}
