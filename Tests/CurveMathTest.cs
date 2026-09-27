// ============================================================================
// 离线验证：参数曲线数学核心（不依赖 Unity，用 csc 直接编译运行）。
//   csc /nologo /out:CurveMathTest.exe CurveMathTest.cs Vec3.cs CurveMath.cs CurveConversions.cs CurveSampler.cs
// 覆盖：端点插值、已知中点值、Hermite↔Bezier 互转、CR/B样条→Bezier 等价、
//       二次升阶等价、段间连续性（C0/C1/C2）、弧长参数化单调性。
// ============================================================================
using System;
using System.Collections.Generic;
using ParamCurve;

class CurveMathTest
{
    private static int failures;

    static int Main()
    {
        Console.WriteLine("== 参数曲线数学核心测试 ==");

        // ---------- 基础求值 ----------
        {
            Vec3 q0 = new Vec3(0, 0, 0), q1 = new Vec3(2, 0, 0), q2 = new Vec3(4, 0, 0);
            Vec3 m = CurveMath.QuadraticBezier(q0, q1, q2, 0.5f);
            CheckClose("二次Bezier中点 = (q0+2q1+q2)/4", m.X, 2f, 1e-5f);
        }
        {
            Vec3 p0 = new Vec3(0, 0, 0), p1 = new Vec3(1, 2, 0), p2 = new Vec3(3, 2, 0), p3 = new Vec3(4, 0, 0);
            Vec3 m = CurveMath.Bezier(p0, p1, p2, p3, 0.5f);
            CheckClose("三次Bezier中点 x = (p0+3p1+3p2+p3)/8", m.X, 2f, 1e-5f);
            CheckClose("三次Bezier中点 y = 1.5", m.Y, 1.5f, 1e-5f);
            Check("三次Bezier端点 B(0)=p0", Near(CurveMath.Bezier(p0, p1, p2, p3, 0f), p0));
            Check("三次Bezier端点 B(1)=p3", Near(CurveMath.Bezier(p0, p1, p2, p3, 1f), p3));
        }

        // ---------- Hermite ↔ Bezier 互转（同一多项式） ----------
        {
            Vec3 p0 = new Vec3(0, 0, 0), p1 = new Vec3(3, 1, 0);
            Vec3 t0 = new Vec3(1, 2, 0), t1 = new Vec3(0, 1, 0);

            Vec3 b0, b1, b2, b3;
            CurveConversions.HermiteToBezier(p0, p1, t0, t1, out b0, out b1, out b2, out b3);

            // b1=p0+t0/3, b2=p1-t1/3
            CheckClose("Hermite→Bezier b1", b1.X, 1f / 3f, 1e-5f);
            CheckClose("Hermite→Bezier b2", b2.Y, 2f / 3f, 1e-5f);

            Vec3 rp0, rp1, rt0, rt1;
            CurveConversions.BezierToHermite(b0, b1, b2, b3, out rp0, out rp1, out rt0, out rt1);
            Check("Bezier→Hermite 端点 p0 还原", Near(rp0, p0));
            Check("Bezier→Hermite 端点 p1 还原", Near(rp1, p1));
            Check("Bezier→Hermite 切线 t0 还原", Near(rt0, t0));
            Check("Bezier→Hermite 切线 t1 还原", Near(rt1, t1));

            // 数值等价：原 Hermite 与转成 Bezier 后在同一 t 处求值一致
            bool eq = true;
            for (int i = 0; i <= 20 && eq; i++)
            {
                float t = i / 20f;
                Vec3 h = CurveMath.Hermite(p0, p1, t0, t1, t);
                Vec3 b = CurveMath.Bezier(b0, b1, b2, b3, t);
                eq = Near(h, b);
            }
            Check("Hermite 与 Bezier 形式在 21 个采样点数值一致", eq);
        }

        // ---------- Catmull-Rom 窗口 → Bezier 等价 + 过点 ----------
        {
            Vec3 p0 = new Vec3(0, 0, 0), p1 = new Vec3(1, 0, 0), p2 = new Vec3(2, 1, 0), p3 = new Vec3(3, 0, 0);
            Vec3 b0, b1, b2, b3;
            CurveConversions.CatmullRomToBezier(p0, p1, p2, p3, out b0, out b1, out b2, out b3);

            bool eq = true;
            for (int i = 0; i <= 20 && eq; i++)
            {
                float t = i / 20f;
                eq = Near(CurveMath.CatmullRom(p0, p1, p2, p3, t), CurveMath.Bezier(b0, b1, b2, b3, t));
            }
            Check("Catmull-Rom 段与 Bezier 形式数值一致", eq);
            Check("Catmull-Rom 段 t=0 过 p1", Near(CurveMath.CatmullRom(p0, p1, p2, p3, 0f), p1));
            Check("Catmull-Rom 段 t=1 过 p2", Near(CurveMath.CatmullRom(p0, p1, p2, p3, 1f), p2));
        }

        // ---------- uniform B 样条窗口 → Bezier 等价 + B(0) 公式 ----------
        {
            Vec3 q0 = new Vec3(0, 0, 0), q1 = new Vec3(1, 2, 0), q2 = new Vec3(3, -1, 0), q3 = new Vec3(4, 1, 0);
            Vec3 b0, b1, b2, b3;
            CurveConversions.BSplineToBezier(q0, q1, q2, q3, out b0, out b1, out b2, out b3);

            bool eq = true;
            for (int i = 0; i <= 20 && eq; i++)
            {
                float t = i / 20f;
                eq = Near(CurveMath.BSpline(q0, q1, q2, q3, t), CurveMath.Bezier(b0, b1, b2, b3, t));
            }
            Check("B样条段与 Bezier 形式数值一致", eq);

            Vec3 start = CurveMath.BSpline(q0, q1, q2, q3, 0f);
            CheckClose("B样条 B(0)=(q0+4q1+q2)/6 (x)", start.X, (0f + 4f + 3f) / 6f, 1e-5f);
            CheckClose("B样条 B(0)=(q0+4q1+q2)/6 (y)", start.Y, (0f + 8f - 1f) / 6f, 1e-5f);
            // B 样条一般不过控制点（逼近性）
            Check("B样条起点不等于 q0（逼近曲线）", !Near(start, q0));
        }

        // ---------- 二次升阶为三次（形状不变） ----------
        {
            Vec3 q0 = new Vec3(0, 0, 0), q1 = new Vec3(2, 3, 0), q2 = new Vec3(4, 0, 0);
            Vec3 b0, b1, b2, b3;
            CurveConversions.ElevateQuadraticToCubic(q0, q1, q2, out b0, out b1, out b2, out b3);

            bool eq = true;
            for (int i = 0; i <= 20 && eq; i++)
            {
                float t = i / 20f;
                eq = Near(CurveMath.QuadraticBezier(q0, q1, q2, t), CurveMath.Bezier(b0, b1, b2, b3, t));
            }
            Check("二次Bezier 升阶后形状不变（21 点一致）", eq);
        }

        // ---------- 模型级：Hermite 链 C1 ----------
        {
            List<Vec3> pts = new List<Vec3>();
            pts.Add(new Vec3(0, 0, 0));
            pts.Add(new Vec3(2, 0, 1));
            pts.Add(new Vec3(4, 1, 0));
            List<Vec3> tgs = new List<Vec3>();
            tgs.Add(new Vec3(1, 0, 1));
            tgs.Add(new Vec3(1, 0, -1));
            tgs.Add(new Vec3(1, 0, 1));

            List<Vec3[]> segs = CurveSampler.ToBezierSegments(CurveMode.Hermite, pts, tgs);
            Check("Hermite 链段数 = 点数-1", segs.Count == 2);

            // 接缝处一阶导连续：seg0 末端导 == seg1 起始导（精确段边界求值）
            Vec3 dEnd = CurveMath.BezierDerivative(segs[0][0], segs[0][1], segs[0][2], segs[0][3], 1f);
            Vec3 dStart = CurveMath.BezierDerivative(segs[1][0], segs[1][1], segs[1][2], segs[1][3], 0f);
            Check("Hermite 链 C1（接缝导数一致）", Near(dEnd, dStart));
        }

        // ---------- 模型级：Catmull-Rom 链过全部点 + C1 ----------
        {
            List<Vec3> pts = new List<Vec3>();
            pts.Add(new Vec3(0, 0, 0));
            pts.Add(new Vec3(1, 1, 0));
            pts.Add(new Vec3(2, 0, 1));
            pts.Add(new Vec3(3, -1, 0));
            pts.Add(new Vec3(4, 0, 0));

            List<Vec3[]> segs = CurveSampler.ToBezierSegments(CurveMode.CatmullRom, pts, null);
            Check("Catmull-Rom 链段数 = 点数-1", segs.Count == 4);

            // 全局参数 u 对应到每个型值点
            bool passes = true;
            for (int i = 0; i < pts.Count && passes; i++)
            {
                float u = (float)i / (segs.Count);
                passes = Near(CurveSampler.Evaluate(segs, u), pts[i]);
            }
            Check("Catmull-Rom 曲线穿过全部型值点", passes);

            // C1（段边界精确求值）
            Vec3 crEnd = CurveMath.BezierDerivative(segs[0][0], segs[0][1], segs[0][2], segs[0][3], 1f);
            Vec3 crStart = CurveMath.BezierDerivative(segs[1][0], segs[1][1], segs[1][2], segs[1][3], 0f);
            Check("Catmull-Rom 链 C1", Near(crEnd, crStart));
        }

        // ---------- 模型级：B 样条链 C1 + C2（uniform 三次 B 样条性质） ----------
        {
            List<Vec3> pts = new List<Vec3>();
            pts.Add(new Vec3(0, 0, 0));
            pts.Add(new Vec3(1, 2, 0));
            pts.Add(new Vec3(3, -1, 0));
            pts.Add(new Vec3(4, 1, 0));
            pts.Add(new Vec3(6, 0, 1));

            List<Vec3[]> segs = CurveSampler.ToBezierSegments(CurveMode.BSpline, pts, null);
            Check("B样条链段数 = 点数-3", segs.Count == 2);

            // 接缝 u = 1/S（段边界精确求值）
            Vec3 bEnd = CurveMath.BezierDerivative(segs[0][0], segs[0][1], segs[0][2], segs[0][3], 1f);
            Vec3 bStart = CurveMath.BezierDerivative(segs[1][0], segs[1][1], segs[1][2], segs[1][3], 0f);
            Vec3 sEnd = CurveMath.BezierSecondDerivative(segs[0][0], segs[0][1], segs[0][2], segs[0][3], 1f);
            Vec3 sStart = CurveMath.BezierSecondDerivative(segs[1][0], segs[1][1], segs[1][2], segs[1][3], 0f);
            Check("B样条链 C1", Near(bEnd, bStart));
            Check("B样条链 C2", Near(sEnd, sStart));
        }

        // ---------- 模型级：二次/三次 Bezier 链 C0 ----------
        {
            List<Vec3> pts = new List<Vec3>();
            pts.Add(new Vec3(0, 0, 0));
            pts.Add(new Vec3(1, 2, 0));
            pts.Add(new Vec3(2, 0, 0));
            pts.Add(new Vec3(3, 2, 0));
            pts.Add(new Vec3(4, 0, 0));
            List<Vec3[]> segs = CurveSampler.ToBezierSegments(CurveMode.QuadraticBezier, pts, null);
            Check("二次Bezier 链段数", segs.Count == 2);
            // 接缝处 C0（共点）：seg0 末端 == seg1 起始（精确）
            Check("二次Bezier 链接缝 C0",
                Near(CurveMath.Bezier(segs[0][0], segs[0][1], segs[0][2], segs[0][3], 1f),
                     CurveMath.Bezier(segs[1][0], segs[1][1], segs[1][2], segs[1][3], 0f)));
        }

        // ---------- 弧长参数化 ----------
        {
            List<Vec3> pts = new List<Vec3>();
            pts.Add(new Vec3(0, 0, 0));
            pts.Add(new Vec3(1, 2, 0));
            pts.Add(new Vec3(3, -1, 0));
            pts.Add(new Vec3(4, 1, 0));
            List<Vec3[]> segs = CurveSampler.ToBezierSegments(CurveMode.CatmullRom, pts, null);

            CurveSampler.ArcLengthTable arc = new CurveSampler.ArcLengthTable();
            arc.Build(segs, 64);
            Check("弧长 > 0", arc.Total > 0.1f);

            // 端点到端点的映射
            CheckClose("Uniform(0)=0", arc.Uniform(0f), 0f, 1e-6f);
            CheckClose("Uniform(1)=1", arc.Uniform(1f), 1f, 1e-6f);

            // 单调性：s 递增 → u 非递减
            bool mono = true;
            float prevU = -1f;
            for (int i = 0; i <= 50 && mono; i++)
            {
                float u = arc.Uniform(i / 50f);
                if (u < prevU - 1e-4f)
                    mono = false;
                prevU = u;
            }
            Check("弧长映射单调", mono);

            // 中点：用独立细折线沿曲线测弧长 ≈ 总长一半（误差 ≤ 折线步长）
            List<Vec3> fine = CurveSampler.SamplePolyline(segs, 256);
            Vec3 halfWorld = CurveSampler.Evaluate(segs, arc.Uniform(0.5f));
            int halfIdx = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < fine.Count; i++)
            {
                float d = (fine[i] - halfWorld).Length;
                if (d < bestD)
                {
                    bestD = d;
                    halfIdx = i;
                }
            }
            float halfLen = 0f;
            for (int i = 1; i <= halfIdx; i++)
                halfLen += (fine[i] - fine[i - 1]).Length;
            CheckClose("Uniform(0.5) 处沿曲线弧长 ≈ 总长一半", halfLen, arc.Total * 0.5f, arc.Total * 0.02f);
        }

        // ---------- 折线采样 ----------
        {
            List<Vec3> pts = new List<Vec3>();
            pts.Add(new Vec3(0, 0, 0));
            pts.Add(new Vec3(1, 2, 0));
            pts.Add(new Vec3(3, -1, 0));
            pts.Add(new Vec3(4, 1, 0));
            List<Vec3[]> segs = CurveSampler.ToBezierSegments(CurveMode.CatmullRom, pts, null);
            List<Vec3> poly = CurveSampler.SamplePolyline(segs, 16);
            Check("折线点数为 n + (S-1)(n-1)", poly.Count == 16 + (segs.Count - 1) * 15);
        }

        // ---------- 空数据保护 ----------
        {
            List<Vec3[]> segs = CurveSampler.ToBezierSegments(CurveMode.BSpline, new List<Vec3>(), null);
            Check("空控制点返回空段", segs.Count == 0);
            Check("空段求值返回零", Near(CurveSampler.Evaluate(segs, 0.5f), Vec3.Zero));
        }

        Console.WriteLine();
        if (failures == 0)
        {
            Console.WriteLine("RESULT: ALL PASS");
            return 0;
        }
        Console.WriteLine("RESULT: FAILED (" + failures + " 项未通过)");
        return 1;
    }

    // ------------------------------------------------------------------
    // 断言辅助
    // ------------------------------------------------------------------

    private static bool Near(Vec3 a, Vec3 b)
    {
        return Near(a, b, 1e-4f);
    }

    private static bool Near(Vec3 a, Vec3 b, float eps)
    {
        return Math.Abs(a.X - b.X) <= eps && Math.Abs(a.Y - b.Y) <= eps && Math.Abs(a.Z - b.Z) <= eps;
    }

    private static void Check(string name, bool cond)
    {
        Console.WriteLine((cond ? "   PASS: " : "   FAIL: ") + name);
        if (!cond)
            failures++;
    }

    private static void CheckClose(string name, float actual, float expected, float eps)
    {
        bool ok = Math.Abs(actual - expected) <= eps;
        Console.WriteLine((ok ? "   PASS: " : "   FAIL: ") + name + " (实际 " + actual + " vs 期望 " + expected + ")");
        if (!ok)
            failures++;
    }
}
