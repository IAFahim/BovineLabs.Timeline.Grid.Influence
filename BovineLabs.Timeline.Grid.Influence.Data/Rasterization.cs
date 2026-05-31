using Unity.Collections;
using Unity.Mathematics;

namespace Influence
{
    public static class Rasterizer
    {
        public static AlignedRect Bounds(InfluenceShape shape, int2 origin)
        {
            switch (shape.Kind)
            {
                case ShapeKind.SolidRect:
                {
                    int2 min = origin + shape.RectMin;
                    return new AlignedRect(min, min + shape.RectSize);
                }
                case ShapeKind.RectShell:
                {
                    int2 min = origin + shape.ShellMin;
                    return new AlignedRect(min, min + shape.ShellSize);
                }
                case ShapeKind.Disc:
                {
                    int2 center = origin + shape.DiscCenter;
                    int radius = shape.DiscRadius;
                    return new AlignedRect(center - new int2(radius, radius), center + new int2(radius + 1, radius + 1));
                }
                case ShapeKind.Annulus:
                {
                    int2 center = origin + shape.AnnulusCenter;
                    int radius = shape.AnnulusOuter;
                    return new AlignedRect(center - new int2(radius, radius), center + new int2(radius + 1, radius + 1));
                }
                case ShapeKind.Capsule:
                {
                    int2 a = origin + shape.CapsuleA;
                    int2 b = origin + shape.CapsuleB;
                    int radius = shape.CapsuleRadius;
                    int2 min = math.min(a, b) - new int2(radius, radius);
                    int2 max = math.max(a, b) + new int2(radius + 1, radius + 1);
                    return new AlignedRect(min, max);
                }
                default:
                    return new AlignedRect(origin, origin);
            }
        }

        public static void Emit(Stamp stamp, ref NativeList<WorldRect> output)
        {
            InfluenceShape shape = stamp.Shape;
            int2 origin = stamp.Origin;
            int weight = shape.Weight;

            switch (shape.Kind)
            {
                case ShapeKind.SolidRect:
                    EmitRect(ref output, origin + shape.RectMin, shape.RectSize, weight);
                    break;
                case ShapeKind.RectShell:
                    EmitShell(ref output, origin + shape.ShellMin, shape.ShellSize, shape.ShellThickness, weight);
                    break;
                case ShapeKind.Disc:
                    EmitDisc(ref output, origin + shape.DiscCenter, shape.DiscRadius, weight);
                    break;
                case ShapeKind.Annulus:
                    EmitDisc(ref output, origin + shape.AnnulusCenter, shape.AnnulusOuter, weight);
                    EmitDisc(ref output, origin + shape.AnnulusCenter, shape.AnnulusInner, -weight);
                    break;
                case ShapeKind.Capsule:
                    EmitCapsule(ref output, origin + shape.CapsuleA, origin + shape.CapsuleB, shape.CapsuleRadius, weight);
                    break;
            }
        }

        static void EmitRect(ref NativeList<WorldRect> output, int2 min, int2 size, int weight)
        {
            if (size.x <= 0 || size.y <= 0) return;
            output.Add(new WorldRect(new AlignedRect(min, min + size), weight));
        }

        static void EmitShell(ref NativeList<WorldRect> output, int2 min, int2 size, int thickness, int weight)
        {
            EmitRect(ref output, min, size, weight);
            int2 innerMin = min + new int2(thickness, thickness);
            int2 innerSize = size - new int2(2 * thickness, 2 * thickness);
            EmitRect(ref output, innerMin, innerSize, -weight);
        }

        static void EmitDisc(ref NativeList<WorldRect> output, int2 center, int radius, int weight)
        {
            if (radius < 0) return;
            int radiusSquared = radius * radius;
            for (int dy = -radius; dy <= radius; dy++)
            {
                int half = IntegerMath.FloorSqrt(radiusSquared - dy * dy);
                int y = center.y + dy;
                output.Add(new WorldRect(
                    new AlignedRect(new int2(center.x - half, y), new int2(center.x + half + 1, y + 1)),
                    weight));
            }
        }

        static void EmitCapsule(ref NativeList<WorldRect> output, int2 a, int2 b, int radius, int weight)
        {
            float2 endA = new float2(a.x, a.y);
            float2 endB = new float2(b.x, b.y);
            float2 axis = endB - endA;
            float axisLengthSquared = math.dot(axis, axis);
            if (axisLengthSquared <= 0f)
            {
                EmitDisc(ref output, a, radius, weight);
                return;
            }

            float r = radius;
            float2 normal = new float2(-axis.y, axis.x) * (math.rsqrt(axisLengthSquared) * r);
            float2 c0 = endA + normal;
            float2 c1 = endA - normal;
            float2 c2 = endB - normal;
            float2 c3 = endB + normal;

            int yMin = (int)math.floor(math.min(endA.y, endB.y) - r);
            int yMax = (int)math.ceil(math.max(endA.y, endB.y) + r);
            const float boundary = 1e-4f;

            for (int y = yMin; y <= yMax; y++)
            {
                float row = y;
                float lo = float.MaxValue;
                float hi = float.MinValue;
                bool covered = false;

                covered |= DiscRow(endA, r, row, ref lo, ref hi);
                covered |= DiscRow(endB, r, row, ref lo, ref hi);
                covered |= CoreRow(c0, c1, c2, c3, row, ref lo, ref hi);
                if (!covered) continue;

                int xStart = (int)math.ceil(lo - boundary);
                int xEnd = (int)math.floor(hi + boundary);
                if (xStart > xEnd) continue;

                output.Add(new WorldRect(
                    new AlignedRect(new int2(xStart, y), new int2(xEnd + 1, y + 1)),
                    weight));
            }
        }

        static bool DiscRow(float2 center, float radius, float row, ref float lo, ref float hi)
        {
            float dy = row - center.y;
            if (math.abs(dy) > radius) return false;
            float half = math.sqrt(radius * radius - dy * dy);
            lo = math.min(lo, center.x - half);
            hi = math.max(hi, center.x + half);
            return true;
        }

        static bool CoreRow(float2 p0, float2 p1, float2 p2, float2 p3, float row, ref float lo, ref float hi)
        {
            bool hit = false;
            hit |= EdgeCrossing(p0, p1, row, ref lo, ref hi);
            hit |= EdgeCrossing(p1, p2, row, ref lo, ref hi);
            hit |= EdgeCrossing(p2, p3, row, ref lo, ref hi);
            hit |= EdgeCrossing(p3, p0, row, ref lo, ref hi);
            return hit;
        }

        static bool EdgeCrossing(float2 p, float2 q, float row, ref float lo, ref float hi)
        {
            float y0 = p.y;
            float y1 = q.y;
            if (y0 == y1) return false;
            float t = (row - y0) / (y1 - y0);
            if (t < 0f || t > 1f) return false;
            float x = p.x + (q.x - p.x) * t;
            lo = math.min(lo, x);
            hi = math.max(hi, x);
            return true;
        }
    }
}
