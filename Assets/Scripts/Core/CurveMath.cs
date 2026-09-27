using System;

namespace ParamCurve
{
    /// <summary>
    /// 参数曲线求值 / 导数核心（纯 C#）。
    /// 覆盖：二次与三次 Bezier、三次 Hermite、Catmull-Rom 段、uniform 三次 B 样条段。
    /// 所有函数都是"局部段"级操作，段与段之间如何拼接见 CurveSampler。
    /// </summary>
    public static class CurveMath
    {
        // ------------------------------------------------------------------
        // 三次 Bezier
        // ------------------------------------------------------------------

        /// <summary>B(t) = (1-t)^3 p0 + 3(1-t)^2 t p1 + 3(1-t) t^2 p2 + t^3 p3, t∈[0,1]</summary>
        public static Vec3 Bezier(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, float t)
        {
            float u = 1f - t;
            float u2 = u * u;
            float u3 = u2 * u;
            float t2 = t * t;
            float t3 = t2 * t;
            return p0 * u3 + p1 * (3f * u2 * t) + p2 * (3f * u * t2) + p3 * t3;
        }

        /// <summary>一阶导：3(1-t)^2(p1-p0) + 6(1-t)t(p2-p1) + 3t^2(p3-p2)</summary>
        public static Vec3 BezierDerivative(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, float t)
        {
            float u = 1f - t;
            return (p1 - p0) * (3f * u * u) + (p2 - p1) * (6f * u * t) + (p3 - p2) * (3f * t * t);
        }

        /// <summary>二阶导：6(1-t)(p2-2p1+p0) + 6t(p3-2p2+p1)</summary>
        public static Vec3 BezierSecondDerivative(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, float t)
        {
            float u = 1f - t;
            return (p2 - p1 * 2f + p0) * (6f * u) + (p3 - p2 * 2f + p1) * (6f * t);
        }

        // ------------------------------------------------------------------
        // 二次 Bezier
        // ------------------------------------------------------------------

        /// <summary>B(t) = (1-t)^2 q0 + 2(1-t)t q1 + t^2 q2</summary>
        public static Vec3 QuadraticBezier(Vec3 q0, Vec3 q1, Vec3 q2, float t)
        {
            float u = 1f - t;
            return q0 * (u * u) + q1 * (2f * u * t) + q2 * (t * t);
        }

        // ------------------------------------------------------------------
        // 三次 Hermite
        // ------------------------------------------------------------------

        /// <summary>
        /// H(t) = h00 p0 + h10 t0 + h01 p1 + h11 t1
        /// h00=2t^3-3t^2+1, h10=t^3-2t^2+t, h01=-2t^3+3t^2, h11=t^3-t^2
        /// p0/p1 为端点，t0/t1 为端点切线（一阶导向量）。
        /// </summary>
        public static Vec3 Hermite(Vec3 p0, Vec3 p1, Vec3 t0, Vec3 t1, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;
            return p0 * h00 + t0 * h10 + p1 * h01 + t1 * h11;
        }

        public static Vec3 HermiteDerivative(Vec3 p0, Vec3 p1, Vec3 t0, Vec3 t1, float t)
        {
            float t2 = t * t;
            float h00d = 6f * t2 - 6f * t;
            float h10d = 3f * t2 - 4f * t + 1f;
            float h01d = -6f * t2 + 6f * t;
            float h11d = 3f * t2 - 2f * t;
            return p0 * h00d + t0 * h10d + p1 * h01d + t1 * h11d;
        }

        public static Vec3 HermiteSecondDerivative(Vec3 p0, Vec3 p1, Vec3 t0, Vec3 t1, float t)
        {
            float h00dd = 12f * t - 6f;
            float h10dd = 6f * t - 4f;
            float h01dd = -12f * t + 6f;
            float h11dd = 6f * t - 2f;
            return p0 * h00dd + t0 * h10dd + p1 * h01dd + t1 * h11dd;
        }

        // ------------------------------------------------------------------
        // Catmull-Rom 段
        // ------------------------------------------------------------------

        /// <summary>
        /// 窗口 (p0,p1,p2,p3) 上的段 [p1,p2]：
        /// 切线 t1=(p2-p0)/2, t2=(p3-p1)/2，等价于以这两条切线做 Hermite。
        /// </summary>
        public static Vec3 CatmullRom(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, float t)
        {
            return Hermite(p1, p2, (p2 - p0) / 2f, (p3 - p1) / 2f, t);
        }

        // ------------------------------------------------------------------
        // uniform 三次 B 样条段
        // ------------------------------------------------------------------

        /// <summary>
        /// 控制点 (q0,q1,q2,q3) 的 uniform 三次 B 样条段：
        /// B(t) = ( (1-t)^3 q0 + (3t^3-6t^2+4) q1 + (-3t^3+3t^2+3t+1) q2 + t^3 q3 ) / 6
        /// 注意：B 样条是逼近曲线，一般不过控制点（B(0)=(q0+4q1+q2)/6）。
        /// </summary>
        public static Vec3 BSpline(Vec3 q0, Vec3 q1, Vec3 q2, Vec3 q3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            float u = 1f - t;
            float b0 = u * u * u / 6f;
            float b1 = (3f * t3 - 6f * t2 + 4f) / 6f;
            float b2 = (-3f * t3 + 3f * t2 + 3f * t + 1f) / 6f;
            float b3 = t3 / 6f;
            return q0 * b0 + q1 * b1 + q2 * b2 + q3 * b3;
        }
    }
}
