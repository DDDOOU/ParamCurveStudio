namespace ParamCurve
{
    /// <summary>
    /// 曲线表示之间的精确转换（都是"局部段"级、保形转换）：
    ///  - Hermite ↔ Bezier（同一多项式，双向精确）
    ///  - Catmull-Rom 窗口 → Bezier 段（精确）
    ///  - uniform B 样条窗口 → Bezier 段（精确）
    ///  - 二次 Bezier → 三次 Bezier 升阶（精确，曲线形状不变）
    /// 更高层（控制点列表级）的组合转换见 CurveController（Unity 侧）与 CurveSampler。
    /// </summary>
    public static class CurveConversions
    {
        /// <summary>Hermite(p0,p1,t0,t1) → 三次 Bezier：b1=p0+t0/3, b2=p1-t1/3（同一多项式）。</summary>
        public static void HermiteToBezier(Vec3 p0, Vec3 p1, Vec3 t0, Vec3 t1,
            out Vec3 b0, out Vec3 b1, out Vec3 b2, out Vec3 b3)
        {
            b0 = p0;
            b1 = p0 + t0 / 3f;
            b2 = p1 - t1 / 3f;
            b3 = p1;
        }

        /// <summary>三次 Bezier(b0..b3) → Hermite：t0=3(b1-b0), t1=3(b3-b2)（同一多项式）。</summary>
        public static void BezierToHermite(Vec3 b0, Vec3 b1, Vec3 b2, Vec3 b3,
            out Vec3 p0, out Vec3 p1, out Vec3 t0, out Vec3 t1)
        {
            p0 = b0;
            p1 = b3;
            t0 = (b1 - b0) * 3f;
            t1 = (b3 - b2) * 3f;
        }

        /// <summary>Catmull-Rom 窗口 (p0,p1,p2,p3) 的段 [p1,p2] → 三次 Bezier（精确）。</summary>
        public static void CatmullRomToBezier(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3,
            out Vec3 b0, out Vec3 b1, out Vec3 b2, out Vec3 b3)
        {
            b0 = p1;
            b1 = p1 + (p2 - p0) / 6f;
            b2 = p2 - (p3 - p1) / 6f;
            b3 = p2;
        }

        /// <summary>uniform B 样条窗口 (q0..q3) → 三次 Bezier（精确）。</summary>
        public static void BSplineToBezier(Vec3 q0, Vec3 q1, Vec3 q2, Vec3 q3,
            out Vec3 b0, out Vec3 b1, out Vec3 b2, out Vec3 b3)
        {
            b0 = (q0 + q1 * 4f + q2) / 6f;
            b1 = (q1 * 2f + q2) / 3f;
            b2 = (q1 + q2 * 2f) / 3f;
            b3 = (q1 + q2 * 4f + q3) / 6f;
        }

        /// <summary>二次 Bezier(q0,q1,q2) 升阶为三次 Bezier（精确，形状不变）。</summary>
        public static void ElevateQuadraticToCubic(Vec3 q0, Vec3 q1, Vec3 q2,
            out Vec3 b0, out Vec3 b1, out Vec3 b2, out Vec3 b3)
        {
            b0 = q0;
            b1 = (q0 + q1 * 2f) / 3f;
            b2 = (q1 * 2f + q2) / 3f;
            b3 = q2;
        }
    }
}
