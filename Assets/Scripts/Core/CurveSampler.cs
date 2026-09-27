using System;
using System.Collections.Generic;

namespace ParamCurve
{
    /// <summary>曲线模式（控制点的语义由模式决定）。</summary>
    public enum CurveMode
    {
        /// <summary>二次 Bezier 链：控制点 = [曲线点, 手柄, 曲线点, 手柄, ...]，偶数下标在曲线上。</summary>
        QuadraticBezier,

        /// <summary>三次 Bezier 链：控制点 = [曲线点, 手柄, 手柄, 曲线点, ...]，下标 3k 在曲线上。</summary>
        CubicBezier,

        /// <summary>Hermite：控制点都在曲线上，切线数组平行存储（每个点一个切线向量）。</summary>
        Hermite,

        /// <summary>Catmull-Rom：控制点都是型值点，曲线穿过所有点（首尾用镜像虚拟点）。</summary>
        CatmullRom,

        /// <summary>uniform B 样条：控制点为逼近控制点（曲线不过中间点）。</summary>
        BSpline
    }

    /// <summary>
    /// 模型级采样器：把"控制点 + 模式"统一降维成三次 Bezier 段列表，
    /// 提供全局参数求值（u∈[0,1]）、一阶/二阶导、折线采样与弧长参数化。
    /// 纯 C#，不依赖 UnityEngine。
    /// </summary>
    public static class CurveSampler
    {
        /// <summary>
        /// 把任意模式的控制点转换为三次 Bezier 段列表（每段 4 个控制点）。
        /// 段与段首尾相接（Hermite / Catmull-Rom / B 样条天然连续；Bezier 链共享曲线点）。
        /// </summary>
        public static List<Vec3[]> ToBezierSegments(CurveMode mode, List<Vec3> points, List<Vec3> tangents)
        {
            List<Vec3[]> segments = new List<Vec3[]>();
            if (points == null || points.Count == 0)
                return segments;

            switch (mode)
            {
                case CurveMode.QuadraticBezier:
                    for (int k = 0; k + 2 < points.Count; k += 2)
                    {
                        Vec3 b0, b1, b2, b3;
                        CurveConversions.ElevateQuadraticToCubic(points[k], points[k + 1], points[k + 2],
                            out b0, out b1, out b2, out b3);
                        segments.Add(new Vec3[] { b0, b1, b2, b3 });
                    }
                    break;

                case CurveMode.CubicBezier:
                    for (int k = 0; k + 3 < points.Count; k += 3)
                        segments.Add(new Vec3[] { points[k], points[k + 1], points[k + 2], points[k + 3] });
                    break;

                case CurveMode.Hermite:
                    if (tangents != null && tangents.Count == points.Count)
                    {
                        for (int i = 0; i + 1 < points.Count; i++)
                        {
                            Vec3 b0, b1, b2, b3;
                            CurveConversions.HermiteToBezier(points[i], points[i + 1],
                                tangents[i], tangents[i + 1], out b0, out b1, out b2, out b3);
                            segments.Add(new Vec3[] { b0, b1, b2, b3 });
                        }
                    }
                    break;

                case CurveMode.CatmullRom:
                    {
                        int n = points.Count;
                        if (n < 2)
                            break;
                        for (int i = 0; i + 1 < n; i++)
                        {
                            // 窗口 (p0,p1,p2,p3)；首尾用镜像虚拟点，保证曲线穿过全部点
                            Vec3 p0 = i - 1 >= 0 ? points[i - 1] : points[0] * 2f - points[1];
                            Vec3 p1 = points[i];
                            Vec3 p2 = points[i + 1];
                            Vec3 p3 = i + 2 < n ? points[i + 2] : points[n - 1] * 2f - points[n - 2];

                            Vec3 b0, b1, b2, b3;
                            CurveConversions.CatmullRomToBezier(p0, p1, p2, p3,
                                out b0, out b1, out b2, out b3);
                            segments.Add(new Vec3[] { b0, b1, b2, b3 });
                        }
                    }
                    break;

                case CurveMode.BSpline:
                    for (int k = 0; k + 3 < points.Count; k++)
                    {
                        Vec3 b0, b1, b2, b3;
                        CurveConversions.BSplineToBezier(points[k], points[k + 1], points[k + 2], points[k + 3],
                            out b0, out b1, out b2, out b3);
                        segments.Add(new Vec3[] { b0, b1, b2, b3 });
                    }
                    break;
            }

            return segments;
        }

        // ------------------------------------------------------------------
        // 全局参数求值（u ∈ [0,1]，跨段）
        // ------------------------------------------------------------------

        public static Vec3 Evaluate(List<Vec3[]> segments, float u)
        {
            if (segments == null || segments.Count == 0)
                return Vec3.Zero;

            Vec3[] seg;
            float t;
            GetSegmentAndT(segments, u, out seg, out t);
            return CurveMath.Bezier(seg[0], seg[1], seg[2], seg[3], t);
        }

        public static Vec3 Derivative(List<Vec3[]> segments, float u)
        {
            if (segments == null || segments.Count == 0)
                return Vec3.Zero;
            Vec3[] seg;
            float t;
            GetSegmentAndT(segments, u, out seg, out t);
            return CurveMath.BezierDerivative(seg[0], seg[1], seg[2], seg[3], t);
        }

        public static Vec3 SecondDerivative(List<Vec3[]> segments, float u)
        {
            if (segments == null || segments.Count == 0)
                return Vec3.Zero;
            Vec3[] seg;
            float t;
            GetSegmentAndT(segments, u, out seg, out t);
            return CurveMath.BezierSecondDerivative(seg[0], seg[1], seg[2], seg[3], t);
        }

        /// <summary>把全局参数 u 拆成 (段下标, 段内参数 t)。</summary>
        public static void GetSegmentAndT(List<Vec3[]> segments, float u, out Vec3[] seg, out float t)
        {
            if (u <= 0f)
            {
                seg = segments[0];
                t = 0f;
                return;
            }
            if (u >= 1f)
            {
                seg = segments[segments.Count - 1];
                t = 1f;
                return;
            }

            float su = u * segments.Count;
            int idx = (int)Math.Floor(su);
            if (idx >= segments.Count)
                idx = segments.Count - 1;
            seg = segments[idx];
            t = su - idx;
        }

        // ------------------------------------------------------------------
        // 折线采样（渲染用）
        // ------------------------------------------------------------------

        /// <summary>把曲线采样为连续折线；段间端点不重复。</summary>
        public static List<Vec3> SamplePolyline(List<Vec3[]> segments, int samplesPerSegment)
        {
            List<Vec3> poly = new List<Vec3>();
            if (segments == null || segments.Count == 0)
                return poly;

            int n = samplesPerSegment < 2 ? 2 : samplesPerSegment;
            bool first = true;
            for (int s = 0; s < segments.Count; s++)
            {
                Vec3[] seg = segments[s];
                for (int i = 0; i < n; i++)
                {
                    if (!first && i == 0)
                        continue; // 段间共享端点，跳过重复
                    float t = (float)i / (n - 1);
                    poly.Add(CurveMath.Bezier(seg[0], seg[1], seg[2], seg[3], t));
                }
                first = false;
            }
            return poly;
        }

        // ------------------------------------------------------------------
        // 弧长参数化
        // ------------------------------------------------------------------

        /// <summary>
        /// 弧长参数化表：把"沿曲线走过的长度比例 s∈[0,1]"映射回全局参数 u，
        /// 实现沿曲线匀速运动/等距采样。
        /// </summary>
        public sealed class ArcLengthTable
        {
            private readonly List<float> us = new List<float>();
            private readonly List<float> lens = new List<float>();
            private float total;

            public float Total
            {
                get { return total; }
            }

            public void Build(List<Vec3[]> segments, int samplesPerSegment)
            {
                us.Clear();
                lens.Clear();
                total = 0f;

                if (segments == null || segments.Count == 0)
                    return;

                int n = samplesPerSegment < 8 ? 8 : samplesPerSegment;
                int steps = segments.Count * n;

                us.Add(0f);
                lens.Add(0f);
                Vec3 prev = CurveSampler.Evaluate(segments, 0f);

                for (int i = 1; i <= steps; i++)
                {
                    float u = (float)i / steps;
                    Vec3 cur = CurveSampler.Evaluate(segments, u);
                    total += (cur - prev).Length;
                    us.Add(u);
                    lens.Add(total);
                    prev = cur;
                }
            }

            /// <summary>s ∈ [0,1]（占总长的比例）→ 全局参数 u（二分 + 线性插值）。</summary>
            public float Uniform(float s)
            {
                if (us.Count == 0)
                    return 0f;
                if (s <= 0f)
                    return us[0];
                if (s >= 1f)
                    return us[us.Count - 1];

                float target = s * total;
                int lo = 0;
                int hi = lens.Count - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (lens[mid] < target)
                        lo = mid;
                    else
                        hi = mid;
                }

                float l0 = lens[lo];
                float l1 = lens[hi];
                float f = (l1 - l0) > 1e-9f ? (target - l0) / (l1 - l0) : 0f;
                return us[lo] + (us[hi] - us[lo]) * f;
            }
        }
    }
}
