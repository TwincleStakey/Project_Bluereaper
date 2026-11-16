using System;
using BlueReaper.Data;
using BlueReaper.HitBox;
using BlueReaper.Stage;
using UnityEditor;
using UnityEngine;

namespace BlueReaper.Data.Editor
{
    internal static class AreaPatternPreview
    {
        internal static readonly Color SelectedColor = new Color(0.2f, 0.75f, 1f);
        internal static readonly Color OutlineColor = new Color(0.62f, 0.67f, 0.73f, 0.8f);

        internal static Transform FieldOrigin(StageField field)
        {
            if (!field) return null;
            using var properties = new SerializedObject(field);
            var layout = properties.FindProperty("fieldObject").objectReferenceValue as GameObject;
            return layout ? layout.transform : field.transform;
        }

        internal static AreaAttackShapeData GetShape(AreaAttackPatternData effect, int index) =>
            index > 0 ? effect.AdditionalShapes[index - 1] : new AreaAttackShapeData
            {
                Shape = effect.Shape, LocalCenter = effect.LocalCenter, LocalYaw = effect.LocalYaw,
                Height = effect.Height, Radius = effect.Radius, InnerRadius = effect.InnerRadius,
                RectangleSize = effect.RectangleSize, SectorAngle = effect.SectorAngle
            };

        internal static Vector3 Center(AreaAttackShapeData shape, Transform origin) =>
            origin ? origin.TransformPoint(shape.LocalCenter) : shape.LocalCenter;

        internal static Quaternion Facing(AreaAttackShapeData shape, Transform origin) =>
            Quaternion.Euler(0f, (origin ? origin.eulerAngles.y : 0f) + shape.LocalYaw, 0f);

        internal static Vector3[] Outline(AreaAttackShapeData shape, Vector3 center, Quaternion facing,
            float radius)
        {
            if (shape.Shape == AreaHitShape.Rectangle)
            {
                Vector3 right = facing * Vector3.right * (Mathf.Max(0.01f, shape.RectangleSize.x) * 0.5f);
                Vector3 forward = facing * Vector3.forward * (Mathf.Max(0.01f, shape.RectangleSize.y) * 0.5f);
                return new[] { center - right - forward, center + right - forward,
                    center + right + forward, center - right + forward, center - right - forward };
            }

            float angle = shape.Shape == AreaHitShape.Sector ? Mathf.Clamp(shape.SectorAngle, 0f, 360f) : 360f;
            bool sector = angle < 360f;
            int segments = Mathf.Max(1, Mathf.CeilToInt(angle / 5f));
            int offset = sector ? 1 : 0;
            var points = new Vector3[segments + 1 + offset * 2];
            if (sector) points[0] = points[points.Length - 1] = center;
            for (int i = 0; i <= segments; i++)
            {
                Quaternion rotation = facing * Quaternion.Euler(0f, angle * ((float)i / segments - 0.5f), 0f);
                points[i + offset] = center + rotation * Vector3.forward * radius;
            }
            return points;
        }

        internal static void DrawShape(AreaAttackShapeData shape, Transform origin, Color color, bool height,
            int pickControl = 0)
        {
            if (shape == null) return;
            var center = Center(shape, origin);
            var facing = Facing(shape, origin);
            float radius = Mathf.Max(0.01f, shape.Radius);
            Vector3[] outer = Outline(shape, center, facing, radius);
            Vector3[] inner = shape.Shape == AreaHitShape.Donut && shape.InnerRadius > 0f
                ? Outline(shape, center, facing, shape.InnerRadius) : null;

            if (Event.current.type == EventType.Layout && pickControl != 0)
            {
                float distance = HandleUtility.DistanceToPolyLine(outer);
                if (inner != null) distance = Mathf.Min(distance, HandleUtility.DistanceToPolyLine(inner));
                if (distance <= 8f) HandleUtility.AddControl(pickControl, distance);
            }
            if (Event.current.type != EventType.Repaint) return;
            using var scope = new Handles.DrawingScope(color);
            Handles.DrawAAPolyLine(2f, outer);
            if (inner != null) Handles.DrawAAPolyLine(2f, inner);
            if (!height) return;

            Vector3 half = Vector3.up * (Mathf.Max(0.01f, shape.Height) * 0.5f);
            DrawHeight(outer, half);
            if (inner != null) DrawHeight(inner, half);
        }

        private static void DrawHeight(Vector3[] outline, Vector3 half)
        {
            int stride = Mathf.Max(1, (outline.Length - 1) / 4);
            for (int i = 0; i < outline.Length; i++)
            {
                if (i > 0)
                {
                    Handles.DrawLine(outline[i - 1] - half, outline[i] - half);
                    Handles.DrawLine(outline[i - 1] + half, outline[i] + half);
                }
                if (i % stride == 0) Handles.DrawLine(outline[i] - half, outline[i] + half);
            }
        }

        internal static void FrameTop(Bounds bounds)
        {
            SceneView view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            view.in2DMode = false;
            view.orthographic = true;
            view.size = Mathf.Max(3f, bounds.extents.magnitude * 1.25f);
            view.LookAtDirect(bounds.center, Quaternion.Euler(90f, 0f, 0f));
            view.Repaint();
        }

        internal static Bounds BoundsOf(AreaAttackPatternData effect, Transform origin)
        {
            Bounds bounds = new Bounds(Center(GetShape(effect, 0), origin), Vector3.zero);
            for (int i = 0; i <= (effect.AdditionalShapes?.Length ?? 0); i++)
            {
                var shape = GetShape(effect, i);
                if (shape == null) continue;
                Vector3 center = Center(shape, origin);
                foreach (var point in Outline(shape, center, Facing(shape, origin), Mathf.Max(0.01f, shape.Radius)))
                    bounds.Encapsulate(point);
            }
            return bounds;
        }
    }
}
