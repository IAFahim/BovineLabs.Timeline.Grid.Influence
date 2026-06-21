#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Editor
{
    internal sealed class InfluenceFieldMonitorWindow : EditorWindow
    {
        private const int DefaultMaxCapturedChunks = 512;

        private readonly List<World> _worlds = new(4);

        private bool _autoRefresh = true;
        private Vector2 _cellScroll;
        private Vector2 _chunkScroll;

        private Texture2D _chunkTexture;
        private string _error;
        private int _fieldIndex;

        private Vector2 _fieldScroll;
        private int _lastTextureHash;
        private int _manualAbsMax;
        private int _maxCapturedChunks = DefaultMaxCapturedChunks;
        private bool _normalizePerChunk;
        private int _selectedChunkIndex;
        private bool _showCellValues;
        private bool _showZeroChunks = true;

        private InfluenceFieldSnapshot _snapshot;

        private int _worldIndex;
        private string[] _worldNames = Array.Empty<string>();

        private void OnDisable()
        {
            DestroyPreviewTexture();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!string.IsNullOrEmpty(_error))
                EditorGUILayout.HelpBox(_error, MessageType.Warning);

            if (_snapshot == null)
            {
                EditorGUILayout.HelpBox("No snapshot captured yet.", MessageType.Info);
                return;
            }

            DrawOverview();
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawFieldList();
                DrawChunkList();
                DrawChunkPreview();
            }
        }

        private void OnInspectorUpdate()
        {
            if (!_autoRefresh)
                return;

            Refresh();
            Repaint();
        }

        [MenuItem("Window/BovineLabs/Grid Influence/Field Monitor")]
        private static void Open()
        {
            var window = GetWindow<InfluenceFieldMonitorWindow>();
            window.titleContent = new GUIContent("Influence Fields");
            window.Show();
        }

        private void RefreshWorldCache()
        {
            _worlds.Clear();

            foreach (var world in World.All)
                if (world != null && world.IsCreated)
                    _worlds.Add(world);

            if (_worldNames.Length != _worlds.Count)
                _worldNames = new string[_worlds.Count];

            for (var i = 0; i < _worlds.Count; i++)
                _worldNames[i] = _worlds[i].Name;

            _worldIndex = _worlds.Count > 0
                ? math.clamp(_worldIndex, 0, _worlds.Count - 1)
                : 0;
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    RefreshWorldCache();

                    if (_worlds.Count == 0)
                    {
                        EditorGUILayout.LabelField("World", "No DOTS worlds");
                    }
                    else
                    {
                        var nextWorld = EditorGUILayout.Popup("World", _worldIndex, _worldNames);

                        if (nextWorld != _worldIndex)
                        {
                            _worldIndex = nextWorld;
                            _fieldIndex = 0;
                            _selectedChunkIndex = 0;
                            Refresh();
                        }
                    }

                    _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto", EditorStyles.toolbarButton,
                        GUILayout.Width(55));

                    if (GUILayout.Button("Refresh", GUILayout.Width(80)))
                        Refresh();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _maxCapturedChunks = EditorGUILayout.IntSlider("Max Chunks", _maxCapturedChunks, 1, 4096);
                    _manualAbsMax = EditorGUILayout.IntField("Abs Max", _manualAbsMax, GUILayout.Width(180));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _showZeroChunks = GUILayout.Toggle(_showZeroChunks, "Show zero chunks", EditorStyles.miniButton);
                    _normalizePerChunk = GUILayout.Toggle(_normalizePerChunk, "Normalize preview per chunk",
                        EditorStyles.miniButton);
                    _showCellValues = GUILayout.Toggle(_showCellValues, "Show cell values", EditorStyles.miniButton);
                }
            }
        }

        private void DrawOverview()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Snapshot", EditorStyles.boldLabel);

                DrawReadOnly("World", _snapshot.WorldName);
                DrawReadOnly("Field", _snapshot.SelectedFieldName);
                DrawReadOnly("Field Slot", _snapshot.SelectedFieldSlot.ToString());
                DrawReadOnly("Frame", _snapshot.FrameId.ToString());
                DrawReadOnly("Cell Size", _snapshot.GridSettings.CellSize.ToString("0.###"));
                DrawReadOnly("Plane Normal", _snapshot.GridSettings.PlaneNormal.ToString());
                DrawReadOnly("Chunk Size", $"{_snapshot.Spec.ChunkSize} x {_snapshot.Spec.ChunkSize}");
                DrawReadOnly("Stride", _snapshot.Spec.Stride.ToString());
                DrawReadOnly("Double Buffered", _snapshot.IsDoubleBuffered ? "Yes" : "No");
                DrawReadOnly("Active Chunks", _snapshot.ActiveChunkCount.ToString());
                DrawReadOnly("Captured Chunks", _snapshot.CapturedChunkCount.ToString());
                DrawReadOnly("Non-Zero Cells", _snapshot.TotalNonZeroCells.ToString());
                DrawReadOnly("Min / Max", $"{_snapshot.MinValue} / {_snapshot.MaxValue}");
                DrawReadOnly("Sum", _snapshot.SumValue.ToString());
            }
        }

        private void DrawFieldList()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(320)))
            {
                EditorGUILayout.LabelField("Fields", EditorStyles.boldLabel);

                _fieldScroll = EditorGUILayout.BeginScrollView(_fieldScroll);

                for (var i = 0; i < _snapshot.Fields.Length; i++)
                {
                    var field = _snapshot.Fields[i];

                    using (new EditorGUILayout.VerticalScope(i == _fieldIndex ? "SelectionRect" : GUIStyle.none))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(field.DisplayName, EditorStyles.miniButtonLeft))
                            {
                                _fieldIndex = i;
                                _selectedChunkIndex = 0;
                                Refresh();
                            }

                            EditorGUILayout.LabelField(field.ActiveChunks.ToString(), GUILayout.Width(42));
                        }

                        EditorGUILayout.LabelField(
                            $"chunk {field.Spec.ChunkSize}, frame {field.FrameId}, allocated {field.AllocatedChunks}, approx {FormatBytes(field.ApproxDataBytes)}",
                            EditorStyles.miniLabel);
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawChunkList()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(360)))
            {
                EditorGUILayout.LabelField("Active Chunks", EditorStyles.boldLabel);

                _chunkScroll = EditorGUILayout.BeginScrollView(_chunkScroll);

                var visibleIndex = 0;

                for (var i = 0; i < _snapshot.Chunks.Length; i++)
                {
                    var chunk = _snapshot.Chunks[i];

                    if (!_showZeroChunks && chunk.NonZeroCells == 0)
                        continue;

                    using (new EditorGUILayout.HorizontalScope(i == _selectedChunkIndex
                               ? "SelectionRect"
                               : GUIStyle.none))
                    {
                        if (GUILayout.Button(
                                $"({chunk.Coord.x}, {chunk.Coord.y})",
                                EditorStyles.miniButtonLeft,
                                GUILayout.Width(100)))
                        {
                            _selectedChunkIndex = i;
                            RebuildPreviewTexture(true);
                        }

                        EditorGUILayout.LabelField($"nz {chunk.NonZeroCells}", GUILayout.Width(70));
                        EditorGUILayout.LabelField($"min {chunk.MinValue}", GUILayout.Width(70));
                        EditorGUILayout.LabelField($"max {chunk.MaxValue}", GUILayout.Width(70));
                    }

                    visibleIndex++;
                }

                if (visibleIndex == 0)
                    EditorGUILayout.HelpBox("No chunks visible with current filters.", MessageType.Info);

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawChunkPreview()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Chunk Preview", EditorStyles.boldLabel);

                if (_snapshot.Chunks.Length == 0)
                {
                    EditorGUILayout.HelpBox("Selected field has no captured active chunks.", MessageType.Info);
                    return;
                }

                _selectedChunkIndex = math.clamp(_selectedChunkIndex, 0, _snapshot.Chunks.Length - 1);
                var chunk = _snapshot.Chunks[_selectedChunkIndex];

                DrawReadOnly("Chunk Coord", $"({chunk.Coord.x}, {chunk.Coord.y})");
                DrawReadOnly("Cell Base", $"({chunk.CellBase.x}, {chunk.CellBase.y})");
                DrawReadOnly("Min / Max", $"{chunk.MinValue} / {chunk.MaxValue}");
                DrawReadOnly("Non-Zero", chunk.NonZeroCells.ToString());
                DrawReadOnly("Sum", chunk.SumValue.ToString());

                RebuildPreviewTexture(false);

                if (_chunkTexture != null)
                {
                    var size = math.min(position.width - 740, 420);
                    size = math.max(size, 160);

                    var rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                    EditorGUI.DrawPreviewTexture(rect, _chunkTexture, null, ScaleMode.ScaleToFit);
                }

                if (_showCellValues)
                    DrawCellValues(chunk);
            }
        }

        private void DrawCellValues(InfluenceFieldSnapshot.ChunkSnapshot chunk)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Cells", EditorStyles.boldLabel);

            _cellScroll = EditorGUILayout.BeginScrollView(_cellScroll, GUILayout.Height(220));

            var size = _snapshot.Spec.ChunkSize;

            for (var y = size - 1; y >= 0; y--)
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (var x = 0; x < size; x++)
                    {
                        var value = chunk.Cells[y * size + x];
                        GUILayout.Label(value.ToString(), EditorStyles.miniLabel, GUILayout.Width(38));
                    }
                }

            EditorGUILayout.EndScrollView();
        }

        private void Refresh()
        {
            RefreshWorldCache();

            if (_worlds.Count == 0)
            {
                _snapshot = null;
                _error = "No DOTS worlds exist.";
                DestroyPreviewTexture();
                return;
            }

            _worldIndex = math.clamp(_worldIndex, 0, _worlds.Count - 1);

            var world = _worlds[_worldIndex];

            if (world == null || !world.IsCreated)
            {
                _snapshot = null;
                _error = "The selected DOTS world is no longer valid.";
                DestroyPreviewTexture();
                return;
            }

            if (!InfluenceFieldSnapshot.TryCapture(
                    world,
                    _fieldIndex,
                    _maxCapturedChunks,
                    out var snapshot,
                    out var error))
            {
                _snapshot = null;
                _error = error;
                DestroyPreviewTexture();
                return;
            }

            _snapshot = snapshot;
            _error = string.Empty;

            _fieldIndex = math.clamp(_fieldIndex, 0, _snapshot.Fields.Length - 1);
            _selectedChunkIndex = math.clamp(
                _selectedChunkIndex,
                0,
                math.max(0, _snapshot.Chunks.Length - 1));

            RebuildPreviewTexture(true);
        }

        private void RebuildPreviewTexture(bool force)
        {
            if (_snapshot == null || _snapshot.Chunks.Length == 0)
            {
                DestroyPreviewTexture();
                return;
            }

            _selectedChunkIndex = math.clamp(_selectedChunkIndex, 0, _snapshot.Chunks.Length - 1);

            var chunk = _snapshot.Chunks[_selectedChunkIndex];
            var hash = HashTextureInputs(chunk);

            if (!force && _chunkTexture != null && hash == _lastTextureHash)
                return;

            _lastTextureHash = hash;

            var size = _snapshot.Spec.ChunkSize;
            EnsurePreviewTexture(size);

            var absMax = ComputeAbsMax(chunk);
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var value = chunk.Cells[y * size + x];
                pixels[y * size + x] = ColorForValue(value, absMax);
            }

            _chunkTexture.SetPixels32(pixels);
            _chunkTexture.Apply(false, false);
        }

        private void EnsurePreviewTexture(int size)
        {
            if (_chunkTexture != null && _chunkTexture.width == size && _chunkTexture.height == size)
                return;

            DestroyPreviewTexture();

            _chunkTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private int ComputeAbsMax(InfluenceFieldSnapshot.ChunkSnapshot chunk)
        {
            if (_manualAbsMax > 0)
                return _manualAbsMax;

            if (_normalizePerChunk)
                return math.max(1, math.max(math.abs(chunk.MinValue), math.abs(chunk.MaxValue)));

            return math.max(1, math.max(math.abs(_snapshot.MinValue), math.abs(_snapshot.MaxValue)));
        }

        private static Color32 ColorForValue(int value, int absMax)
        {
            if (value == 0)
                return new Color32(18, 18, 18, 255);

            var t = math.saturate(math.abs(value) / (float)absMax);
            var a = (byte)math.round(math.lerp(70, 255, t));

            if (value > 0)
            {
                var g = (byte)math.round(math.lerp(70, 255, t));
                return new Color32(30, g, 70, a);
            }

            var r = (byte)math.round(math.lerp(80, 255, t));
            return new Color32(r, 35, 35, a);
        }

        private int HashTextureInputs(InfluenceFieldSnapshot.ChunkSnapshot chunk)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _selectedChunkIndex;
                hash = hash * 31 + _manualAbsMax;
                hash = hash * 31 + (_normalizePerChunk ? 1 : 0);
                hash = hash * 31 + chunk.MinValue;
                hash = hash * 31 + chunk.MaxValue;
                hash = hash * 31 + chunk.NonZeroCells;
                hash = hash * 31 + _snapshot.FrameId.GetHashCode();
                return hash;
            }
        }

        private void DestroyPreviewTexture()
        {
            if (_chunkTexture == null)
                return;

            DestroyImmediate(_chunkTexture);
            _chunkTexture = null;
            _lastTextureHash = 0;
        }

        private static void DrawReadOnly(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(130));
                EditorGUILayout.SelectableLabel(value, EditorStyles.label,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";

            if (bytes < 1024 * 1024)
                return $"{bytes / 1024f:0.0} KB";

            return $"{bytes / (1024f * 1024f):0.0} MB";
        }
    }
}
#endif