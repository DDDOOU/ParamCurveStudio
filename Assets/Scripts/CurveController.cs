using System.Collections.Generic;
using UnityEngine;
using ParamCurve;

namespace ParamCurve
{
    /// <summary>
    /// 参数曲线建模与交互主组件。
    ///
    /// 【自设计的控制点交互方案】
    ///   - 左键在控制点上按下：选中并开始拖拽（选中为红色，悬停为白色）
    ///   - 拖拽平面策略：
    ///       默认        在水平面(XZ)移动（保持 y）
    ///       按住 Shift  仅在竖直方向移动（保持 x,z）
    ///       按住 Ctrl   在面向相机的平面上自由 3D 移动
    ///   - 左键在空白处点击：插入控制点
    ///       型值点模式（Hermite / Catmull-Rom / B 样条）：插入在曲线上最近位置
    ///       Bezier 模式（二次/三次）：在末端追加一组（曲线点 + 手柄）
    ///   - X 删除选中（Bezier 模式删除整组：曲线点 + 手柄）
    ///   - C 在末端追加；Tab 循环切换模式；R 重置示例；F 聚焦；F1 帮助
    ///
    /// 【曲线类型与转换】
    ///   模式：二次Bezier / 三次Bezier / Hermite / Catmull-Rom / B样条
    ///   转换按钮：
    ///     →三次Bezier：从任意模式精确转换（Hermite/CR/B样条→Bezier，二次升阶）
    ///     →Hermite：   从任意模式精确转换（取每段的端点与切线）
    ///     →Catmull-Rom / →B样条：提取曲线点作为型值/控制点（形状为近似，可继续编辑）
    ///   全部曲线在内部统一降维成三次 Bezier 段渲染（见 CurveSampler）。
    /// </summary>
    public class CurveController : MonoBehaviour
    {
        [Header("模式与数据")]
        public CurveMode mode = CurveMode.CatmullRom;
        public List<Vector3> points = new List<Vector3>();
        public List<Vector3> tangents = new List<Vector3>();

        [Header("渲染")]
        [Range(8, 128)] public int samplesPerSegment = 32;
        public float pointScale = 0.22f;

        [Header("交互")]
        [Tooltip("拾取控制点的屏幕像素半径。")]
        public float pickRadiusPx = 30f;
        public KeyCode cycleKey = KeyCode.Tab;

        // ------------------------------------------------------------------
        // 内部状态
        // ------------------------------------------------------------------
        private Camera cam;
        private readonly List<GameObject> pointHandles = new List<GameObject>();
        private readonly List<GameObject> tangentHandles = new List<GameObject>();
        private readonly List<LineRenderer> tangentLines = new List<LineRenderer>();
        private readonly List<Material> materials = new List<Material>();

        private Material matOnCurve, matHandle, matTangent, matSelected, matHover;
        private LineRenderer curveLine, polyLine;
        private GameObject gridRoot;
        private GameObject dot;

        private int selected = -1;
        private bool selectedIsTangent;
        private bool dragging;
        private int dragIndex = -1;
        private bool dragTangent;
        private int hoverPoint = -1;
        private bool hoverTangent;

        private bool animating;
        private float animS;                 // 弧长比例 s ∈ [0,1]
        public float animSpeed = 0.25f;

        private bool showHud = true;
        private bool showHelpers = true;

        private List<Vec3[]> segments = new List<Vec3[]>();
        private readonly CurveSampler.ArcLengthTable arcTable = new CurveSampler.ArcLengthTable();

        // 配色
        private static readonly Color ColOnCurve = new Color(0.25f, 0.9f, 0.9f);
        private static readonly Color ColHandle = new Color(0.95f, 0.8f, 0.2f);
        private static readonly Color ColTangent = new Color(1f, 0.55f, 0.1f);
        private static readonly Color ColSelected = new Color(1f, 0.25f, 0.2f);
        private static readonly Color ColHover = new Color(1f, 1f, 1f);
        private static readonly Color ColCurve = new Color(0.25f, 1f, 0.55f);
        private static readonly Color ColPoly = new Color(0.6f, 0.6f, 0.65f);
        private static readonly Color ColDot = new Color(1f, 0.4f, 0.9f);

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        private void Start()
        {
            cam = Camera.main != null ? Camera.main : ObjectFinder.FindFirst<Camera>();
            CreateMaterials();
            CreateLines();
            CreateGrid();
            CreateDot();

            if (points.Count == 0)
                LoadSampleCurve();
            EnsureTangents();
            Rebuild();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                    Destroy(materials[i]);
            }
            materials.Clear();
        }

        private void Update()
        {
            if (cam == null)
                cam = Camera.main != null ? Camera.main : ObjectFinder.FindFirst<Camera>();
            if (cam == null)
                return;

            // 动画：沿曲线匀速运动
            if (animating && segments.Count > 0)
            {
                animS += Time.deltaTime * animSpeed;
                if (animS > 1f)
                    animS -= 1f;
                UpdateDot();
            }

            HandleKeys();

            bool overHud = showHud && HudScreenRect.Contains(GuiMousePos());

            // 悬停高亮
            if (!dragging && !overHud)
                UpdateHover();

            // 左键：拾取 / 插入（鼠标不在 HUD 面板上时才生效）
            if (Input.GetMouseButtonDown(0) && !overHud)
            {
                if (!TryPickHandle())
                    TryInsertPoint();
            }

            // 拖拽
            if (dragging && Input.GetMouseButton(0))
                DragHandle();

            // 左键抬起：结束拖拽并重建（刷新弧长表）
            if (Input.GetMouseButtonUp(0) && dragging)
            {
                dragging = false;
                Rebuild();
            }
        }

        // ------------------------------------------------------------------
        // 交互：拾取 / 拖拽 / 插入 / 删除
        // ------------------------------------------------------------------

        private bool TryPickHandle()
        {
            int best = -1;
            bool bestTangent = false;
            float bestSq = pickRadiusPx * pickRadiusPx;

            for (int i = 0; i < points.Count; i++)
            {
                float d = ScreenDistSq(points[i]);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = i;
                    bestTangent = false;
                }
            }
            if (mode == CurveMode.Hermite)
            {
                for (int i = 0; i < tangents.Count; i++)
                {
                    float d = ScreenDistSq(points[i] + tangents[i]);
                    if (d < bestSq)
                    {
                        bestSq = d;
                        best = i;
                        bestTangent = true;
                    }
                }
            }

            if (best >= 0)
            {
                selected = best;
                selectedIsTangent = bestTangent;
                dragging = true;
                dragIndex = best;
                dragTangent = bestTangent;
                UpdateHandleColors();
                return true;
            }
            return false;
        }

        private void DragHandle()
        {
            if (dragIndex < 0 || dragIndex >= points.Count)
                return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 anchor = dragTangent ? points[dragIndex] + tangents[dragIndex] : points[dragIndex];

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            Vector3 pos = anchor;
            if (ctrl || shift)
            {
                // 面向相机的平面求交；Shift 只取竖直分量
                Plane plane = new Plane(cam.transform.forward, anchor);
                float enter;
                if (!plane.Raycast(ray, out enter))
                    return;
                Vector3 hit = ray.GetPoint(enter);
                pos = shift ? new Vector3(anchor.x, hit.y, anchor.z) : hit;
            }
            else
            {
                // 水平面求交（保持 y）
                Plane plane = new Plane(Vector3.up, anchor);
                float enter;
                if (!plane.Raycast(ray, out enter))
                    return;
                pos = ray.GetPoint(enter);
            }

            if (dragTangent)
                tangents[dragIndex] = pos - points[dragIndex];
            else
                points[dragIndex] = pos;

            UpdateVisuals();
            PositionHandles();
        }

        private void TryInsertPoint()
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            float y = GetInsertPlaneY();
            Plane plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            float enter;
            if (!plane.Raycast(ray, out enter))
                return;
            Vector3 pos = ray.GetPoint(enter);

            switch (mode)
            {
                case CurveMode.Hermite:
                case CurveMode.CatmullRom:
                case CurveMode.BSpline:
                    {
                        int idx = FindInsertIndex(pos);
                        points.Insert(idx, pos);
                        tangents.Insert(idx, Vector3.zero);
                        FixTangentAround(idx);
                        selected = idx;
                        selectedIsTangent = false;
                    }
                    break;
                default:
                    AppendBezierGroup(pos);
                    break;
            }
            Rebuild();
        }

        /// <summary>在曲线上找到离点击位置最近的插入点（型值点模式）。</summary>
        private int FindInsertIndex(Vector3 pos)
        {
            if (segments.Count == 0)
                return 0;

            int steps = segments.Count * samplesPerSegment;
            float bestU = 0f;
            float bestD = float.MaxValue;
            for (int i = 0; i <= steps; i++)
            {
                float u = (float)i / steps;
                Vector3 p = ToUnity(CurveSampler.Evaluate(segments, u));
                float d = (p - pos).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    bestU = u;
                }
            }

            // 全局参数 u → 段 → 该段起点后插入
            int seg = Mathf.FloorToInt(bestU * segments.Count);
            if (seg < 0) seg = 0;
            if (seg >= segments.Count) seg = segments.Count - 1;
            int idx = seg + 1;
            if (idx > points.Count) idx = points.Count;
            return idx;
        }

        /// <summary>Bezier 模式：在末端追加一组（曲线点 + 手柄）。</summary>
        private void AppendBezierGroup(Vector3 pos)
        {
            // 沿最后一段末端方向放置手柄，视觉更自然
            Vector3 dir = Vector3.right;
            if (segments.Count > 0)
            {
                Vec3[] last = segments[segments.Count - 1];
                Vec3 d = last[3] - last[2];
                if (d.Length > 1e-6f)
                    dir = ToUnity(d).normalized;
            }

            if (mode == CurveMode.QuadraticBezier)
            {
                if (points.Count % 2 == 0)
                {
                    points.Add(pos);                     // 补曲线点
                }
                else
                {
                    points.Add(pos + dir * 0.6f);        // 手柄
                    points.Add(pos);                     // 曲线点
                }
            }
            else // 三次 Bezier
            {
                int r = points.Count % 3;
                if (r == 0)
                {
                    points.Add(pos);                     // 补曲线点
                }
                else if (r == 1)
                {
                    points.Add(pos + dir * 0.6f);        // 手柄1
                    points.Add(pos - dir * 0.6f);        // 手柄2
                    points.Add(pos);                     // 曲线点
                }
                else
                {
                    points.Add(pos + dir * 0.6f);        // 手柄
                    points.Add(pos);                     // 曲线点
                }
            }
            selected = points.Count - 1;
            selectedIsTangent = false;
        }

        private void DeleteSelected()
        {
            if (selected < 0 || points.Count == 0)
                return;

            if (mode == CurveMode.QuadraticBezier)
            {
                int on = selected - (selected % 2);
                int removeCount = on + 1 < points.Count ? 2 : 1;
                points.RemoveRange(on, removeCount);
            }
            else if (mode == CurveMode.CubicBezier)
            {
                int on = selected - (selected % 3);
                int removeCount = Mathf.Min(3, points.Count - on);
                points.RemoveRange(on, removeCount);
            }
            else
            {
                points.RemoveAt(selected);
                if (mode == CurveMode.Hermite && tangents.Count > selected)
                    tangents.RemoveAt(selected);
            }

            selected = -1;
            EnsureTangents();
            Rebuild();
        }

        /// <summary>在末端追加一个点（C 键）。</summary>
        private void AppendPointAtEnd()
        {
            if (points.Count == 0)
            {
                points.Add(new Vector3(0f, 0f, 0f));
                tangents.Add(Vector3.zero);
                selected = 0;
                selectedIsTangent = false;
                Rebuild();
                return;
            }

            if (mode == CurveMode.QuadraticBezier || mode == CurveMode.CubicBezier)
            {
                AppendBezierGroup(points[points.Count - 1] + Vector3.right * 1.2f);
                Rebuild();
                return;
            }

            points.Add(points[points.Count - 1] + Vector3.right * 1.2f);
            tangents.Add(Vector3.zero);
            FixTangentAround(points.Count - 1);
            selected = points.Count - 1;
            selectedIsTangent = false;
            Rebuild();
        }

        // ------------------------------------------------------------------
        // 快捷键与模式
        // ------------------------------------------------------------------

        private void HandleKeys()
        {
            if (Input.GetKeyDown(cycleKey))
                SetMode((CurveMode)(((int)mode + 1) % 5));
            if (Input.GetKeyDown(KeyCode.X))
                DeleteSelected();
            if (Input.GetKeyDown(KeyCode.C))
                AppendPointAtEnd();
            if (Input.GetKeyDown(KeyCode.Space))
                animating = !animating;
            if (Input.GetKeyDown(KeyCode.H))
                showHelpers = !showHelpers;
            if (Input.GetKeyDown(KeyCode.R))
                ResetSample();
            if (Input.GetKeyDown(KeyCode.F1))
                showHud = !showHud;
            if (Input.GetKeyDown(KeyCode.F))
                FocusCamera();
        }

        /// <summary>切换模式（不转换数据，只改变控制点的解释方式）。</summary>
        public void SetMode(CurveMode m)
        {
            mode = m;
            EnsureTangents();
            Rebuild();
        }

        // ------------------------------------------------------------------
        // 表示转换（按钮触发）
        // ------------------------------------------------------------------

        /// <summary>精确转换到三次 Bezier（Hermite/CR/B样条→Bezier；二次升阶）。</summary>
        public void ConvertToBezierCubic()
        {
            if (mode == CurveMode.CubicBezier)
                return;

            List<Vec3[]> segs = CurveSampler.ToBezierSegments(mode, ToCorePoints(), ToCoreTangents());
            if (segs.Count == 0)
            {
                Debug.LogWarning("[CurveStudio] 控制点不足，无法转换。");
                return;
            }

            List<Vector3> newPts = new List<Vector3>();
            for (int s = 0; s < segs.Count; s++)
            {
                Vec3[] seg = segs[s];
                if (s == 0)
                    newPts.Add(ToUnity(seg[0]));
                newPts.Add(ToUnity(seg[1]));
                newPts.Add(ToUnity(seg[2]));
                newPts.Add(ToUnity(seg[3]));
            }

            points = newPts;
            mode = CurveMode.CubicBezier;
            EnsureTangents();
            Rebuild();
        }

        /// <summary>精确转换到 Hermite（取每段端点与切线）。</summary>
        public void ConvertToHermite()
        {
            if (mode == CurveMode.Hermite)
                return;

            List<Vec3[]> segs = CurveSampler.ToBezierSegments(mode, ToCorePoints(), ToCoreTangents());
            if (segs.Count == 0)
            {
                Debug.LogWarning("[CurveStudio] 控制点不足，无法转换。");
                return;
            }

            List<Vector3> newPts = new List<Vector3>();
            List<Vector3> newTgs = new List<Vector3>();
            for (int s = 0; s < segs.Count; s++)
            {
                Vec3[] seg = segs[s];
                if (s == 0)
                {
                    newPts.Add(ToUnity(seg[0]));
                    newTgs.Add(ToUnity((seg[1] - seg[0]) * 3f));
                }
                newPts.Add(ToUnity(seg[3]));
                newTgs.Add(ToUnity((seg[3] - seg[2]) * 3f));
            }

            points = newPts;
            tangents = newTgs;
            mode = CurveMode.Hermite;
            Rebuild();
        }

        /// <summary>提取曲线点作为 Catmull-Rom 型值点（形状近似，可继续编辑）。</summary>
        public void ConvertToCatmullRom()
        {
            points = ExtractOnCurvePoints();
            mode = CurveMode.CatmullRom;
            EnsureTangents();
            Rebuild();
        }

        /// <summary>提取曲线点作为 B 样条控制点（形状近似，可继续编辑）。</summary>
        public void ConvertToBSpline()
        {
            points = ExtractOnCurvePoints();
            mode = CurveMode.BSpline;
            EnsureTangents();
            Rebuild();
        }

        /// <summary>二次 Bezier 升阶为三次（仅在二次模式有效）。</summary>
        public void ElevateToCubic()
        {
            if (mode != CurveMode.QuadraticBezier)
            {
                Debug.Log("升阶仅适用于二次 Bezier 模式。");
                return;
            }
            ConvertToBezierCubic();
        }

        /// <summary>加载示例曲线并重置为 Catmull-Rom 模式。</summary>
        public void ResetSample()
        {
            LoadSampleCurve();
            Rebuild();
        }

        // ------------------------------------------------------------------
        // 数据辅助
        // ------------------------------------------------------------------

        private List<Vector3> ExtractOnCurvePoints()
        {
            List<Vector3> result = new List<Vector3>();
            for (int i = 0; i < points.Count; i++)
            {
                if (IsOnCurveIndex(i))
                    result.Add(points[i]);
            }
            return result;
        }

        private bool IsOnCurveIndex(int i)
        {
            switch (mode)
            {
                case CurveMode.QuadraticBezier: return i % 2 == 0;
                case CurveMode.CubicBezier: return i % 3 == 0;
                default: return true;
            }
        }

        private void EnsureTangents()
        {
            while (tangents.Count < points.Count)
                tangents.Add(Vector3.zero);
            while (tangents.Count > points.Count)
                tangents.RemoveAt(tangents.Count - 1);
        }

        private void FixTangentAround(int idx)
        {
            if (mode != CurveMode.Hermite)
                return;
            if (idx < 0 || idx >= points.Count)
                return;
            if (idx > 0 && idx + 1 < points.Count)
                tangents[idx] = (points[idx + 1] - points[idx - 1]) * 0.5f;
            else if (idx == 0 && points.Count > 1)
                tangents[0] = points[1] - points[0];
            else if (idx == points.Count - 1 && points.Count > 1)
                tangents[idx] = points[idx] - points[idx - 1];
        }

        private void LoadSampleCurve()
        {
            points.Clear();
            points.Add(new Vector3(0f, 0f, 0f));
            points.Add(new Vector3(2f, 0.6f, 1.2f));
            points.Add(new Vector3(4f, 0f, 2.4f));
            points.Add(new Vector3(5.4f, 0.8f, 4f));
            points.Add(new Vector3(4f, 0f, 5.8f));
            points.Add(new Vector3(1.8f, 0f, 5f));
            points.Add(new Vector3(0f, 0.4f, 3f));

            tangents.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                if (i == 0) tangents.Add(points[1] - points[0]);
                else if (i == points.Count - 1) tangents.Add(points[i] - points[i - 1]);
                else tangents.Add((points[i + 1] - points[i - 1]) * 0.5f);
            }

            mode = CurveMode.CatmullRom;
            selected = -1;
            animating = false;
        }

        private List<Vec3> ToCorePoints()
        {
            List<Vec3> core = new List<Vec3>(points.Count);
            for (int i = 0; i < points.Count; i++)
                core.Add(new Vec3(points[i].x, points[i].y, points[i].z));
            return core;
        }

        private List<Vec3> ToCoreTangents()
        {
            List<Vec3> core = new List<Vec3>(tangents.Count);
            for (int i = 0; i < tangents.Count; i++)
                core.Add(new Vec3(tangents[i].x, tangents[i].y, tangents[i].z));
            return core;
        }

        private static Vector3 ToUnity(Vec3 v)
        {
            return new Vector3(v.X, v.Y, v.Z);
        }

        public int PointCount
        {
            get { return points.Count; }
        }

        /// <summary>当前曲线包围盒（聚焦用）。</summary>
        public Bounds CurveBounds
        {
            get
            {
                if (points.Count == 0)
                    return new Bounds(Vector3.zero, Vector3.one);

                Vector3 min = points[0];
                Vector3 max = points[0];
                for (int i = 0; i < points.Count; i++)
                {
                    min = Vector3.Min(min, points[i]);
                    max = Vector3.Max(max, points[i]);
                }
                for (int i = 0; i < tangents.Count; i++)
                {
                    Vector3 end = points[i] + tangents[i];
                    min = Vector3.Min(min, end);
                    max = Vector3.Max(max, end);
                }
                Bounds b = new Bounds();
                b.SetMinMax(min, max);
                return b;
            }
        }

        public void FocusCamera()
        {
            OrbitCameraController orbit = ObjectFinder.FindFirst<OrbitCameraController>();
            if (orbit != null)
                orbit.FocusOn(CurveBounds);
        }

        private Rect HudScreenRect
        {
            get { return new Rect(12, 12, 330, Screen.height - 40); }
        }

        private Vector2 GuiMousePos()
        {
            return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        }

        // ------------------------------------------------------------------
        // 重建与渲染
        // ------------------------------------------------------------------

        /// <summary>完整重建（句柄数量、位置、颜色、曲线、切线、弧长表）。</summary>
        public void Rebuild()
        {
            EnsureTangents();
            EnsureHandleCount();
            UpdateVisuals();
            PositionHandles();
            UpdateHandleColors();
        }

        /// <summary>只更新几何/线（拖拽中高频调用）。</summary>
        private void UpdateVisuals()
        {
            segments = CurveSampler.ToBezierSegments(mode, ToCorePoints(), ToCoreTangents());

            // 曲线折线
            List<Vec3> poly = CurveSampler.SamplePolyline(segments, samplesPerSegment);
            ApplyPolyline(curveLine, poly);

            // 控制多边形（Bezier 模式含手柄，Hermite/CR/B样条为控制点连线）
            ApplyPolyline(polyLine, showHelpers ? points : new List<Vector3>());

            // Hermite 切线
            UpdateTangentLines();

            // 弧长表（动画/等距采样用）
            arcTable.Build(segments, 64);

            if (!animating && dot != null)
                dot.SetActive(false);
        }

        private void ApplyPolyline(LineRenderer lr, List<Vec3> poly)
        {
            if (lr == null)
                return;
            if (poly.Count < 2)
            {
                lr.positionCount = 0;
                return;
            }
            lr.positionCount = poly.Count;
            Vector3[] arr = new Vector3[poly.Count];
            for (int i = 0; i < poly.Count; i++)
                arr[i] = new Vector3(poly[i].X, poly[i].Y, poly[i].Z);
            lr.SetPositions(arr);
        }

        private void ApplyPolyline(LineRenderer lr, List<Vector3> poly)
        {
            if (lr == null)
                return;
            if (poly.Count < 2)
            {
                lr.positionCount = 0;
                return;
            }
            lr.positionCount = poly.Count;
            lr.SetPositions(poly.ToArray());
        }

        private void UpdateTangentLines()
        {
            int want = mode == CurveMode.Hermite ? points.Count : 0;
            while (tangentLines.Count < want)
            {
                LineRenderer lr = MakeLineGo("TangentLine_" + tangentLines.Count,
                    matTangent, Vector3.zero, Vector3.zero, 0.012f, transform);
                tangentLines.Add(lr);
            }
            for (int i = 0; i < tangentLines.Count; i++)
            {
                LineRenderer lr = tangentLines[i];
                if (i < want)
                {
                    lr.gameObject.SetActive(true);
                    lr.positionCount = 2;
                    lr.SetPosition(0, points[i]);
                    lr.SetPosition(1, points[i] + tangents[i]);
                }
                else
                {
                    lr.gameObject.SetActive(false);
                }
            }
        }

        private void EnsureHandleCount()
        {
            while (pointHandles.Count < points.Count)
                pointHandles.Add(CreateHandle(false));
            while (pointHandles.Count > points.Count)
            {
                Destroy(pointHandles[pointHandles.Count - 1]);
                pointHandles.RemoveAt(pointHandles.Count - 1);
            }

            while (tangentHandles.Count < tangents.Count)
                tangentHandles.Add(CreateHandle(true));
            while (tangentHandles.Count > tangents.Count)
            {
                Destroy(tangentHandles[tangentHandles.Count - 1]);
                tangentHandles.RemoveAt(tangentHandles.Count - 1);
            }
        }

        private GameObject CreateHandle(bool isTangent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = isTangent ? "TangentHandle" : "ControlPoint";
            Collider col = go.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            go.transform.SetParent(transform, false);
            float scale = pointScale * (isTangent ? 0.65f : 1f);
            go.transform.localScale = Vector3.one * scale;
            go.SetActive(true);
            return go;
        }

        private void PositionHandles()
        {
            for (int i = 0; i < pointHandles.Count && i < points.Count; i++)
                pointHandles[i].transform.position = points[i];

            bool showTangents = mode == CurveMode.Hermite && showHelpers;
            for (int i = 0; i < tangentHandles.Count; i++)
            {
                bool visible = showTangents && i < points.Count;
                tangentHandles[i].SetActive(visible);
                if (visible)
                    tangentHandles[i].transform.position = points[i] + tangents[i];
            }
        }

        private void UpdateHandleColors()
        {
            for (int i = 0; i < pointHandles.Count && i < points.Count; i++)
            {
                Material mat = IsOnCurveIndex(i) ? matOnCurve : matHandle;
                if (i == selected && !selectedIsTangent)
                    mat = matSelected;
                else if (i == hoverPoint && !hoverTangent)
                    mat = matHover;
                ApplyMaterial(pointHandles[i], mat);
            }

            for (int i = 0; i < tangentHandles.Count && i < points.Count; i++)
            {
                Material mat = matTangent;
                if (i == selected && selectedIsTangent)
                    mat = matSelected;
                else if (i == hoverPoint && hoverTangent)
                    mat = matHover;
                ApplyMaterial(tangentHandles[i], mat);
            }
        }

        private static void ApplyMaterial(GameObject go, Material mat)
        {
            MeshRenderer r = go != null ? go.GetComponent<MeshRenderer>() : null;
            if (r != null)
                r.sharedMaterial = mat;
        }

        private void UpdateHover()
        {
            int hp = -1;
            bool ht = false;
            float best = pickRadiusPx * pickRadiusPx;

            for (int i = 0; i < points.Count; i++)
            {
                float d = ScreenDistSq(points[i]);
                if (d < best)
                {
                    best = d;
                    hp = i;
                    ht = false;
                }
            }
            if (mode == CurveMode.Hermite)
            {
                for (int i = 0; i < tangents.Count; i++)
                {
                    float d = ScreenDistSq(points[i] + tangents[i]);
                    if (d < best)
                    {
                        best = d;
                        hp = i;
                        ht = true;
                    }
                }
            }

            if (hp != hoverPoint || ht != hoverTangent)
            {
                hoverPoint = hp;
                hoverTangent = ht;
                UpdateHandleColors();
            }
        }

        private float ScreenDistSq(Vector3 world)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0f)
                return float.MaxValue;
            Vector3 mouse = Input.mousePosition;
            float dx = sp.x - mouse.x;
            float dy = sp.y - mouse.y;
            return dx * dx + dy * dy;
        }

        private float GetInsertPlaneY()
        {
            if (points.Count == 0)
                return 0f;
            float sum = 0f;
            for (int i = 0; i < points.Count; i++)
                sum += points[i].y;
            return sum / points.Count;
        }

        private void UpdateDot()
        {
            if (dot == null || segments.Count == 0)
                return;
            float u = arcTable.Uniform(animS);
            Vector3 p = ToUnity(CurveSampler.Evaluate(segments, u));
            dot.transform.position = p;
            dot.SetActive(true);
        }

        // ------------------------------------------------------------------
        // 场景物件创建
        // ------------------------------------------------------------------

        private void CreateMaterials()
        {
            matOnCurve = MakeUnlit(ColOnCurve);
            matHandle = MakeUnlit(ColHandle);
            matTangent = MakeUnlit(ColTangent);
            matSelected = MakeUnlit(ColSelected);
            matHover = MakeUnlit(ColHover);
        }

        private Material MakeUnlit(Color c)
        {
            Shader sh = Shader.Find("Unlit/Color");
            if (sh == null)
                sh = Shader.Find("Standard");
            Material m = new Material(sh);
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", c);
            materials.Add(m);
            return m;
        }

        private void CreateLines()
        {
            curveLine = MakeLineGo("CurveLine", MakeUnlit(ColCurve), Vector3.zero, Vector3.zero, 0.045f, transform);
            polyLine = MakeLineGo("ControlPolygon", MakeUnlit(ColPoly), Vector3.zero, Vector3.zero, 0.015f, transform);
        }

        private LineRenderer MakeLineGo(string name, Material mat, Vector3 a, Vector3 b, float width, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = mat;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            return lr;
        }

        private void CreateGrid()
        {
            if (gridRoot != null)
                return;
            gridRoot = new GameObject("Grid");
            gridRoot.transform.SetParent(transform, false);

            Material gridMat = MakeUnlit(new Color(0.33f, 0.34f, 0.38f));
            for (int i = -5; i <= 5; i++)
            {
                MakeLineGo("GridX_" + i, gridMat,
                    new Vector3(i, 0f, -5f), new Vector3(i, 0f, 5f), 0.012f, gridRoot.transform);
                MakeLineGo("GridZ_" + i, gridMat,
                    new Vector3(-5f, 0f, i), new Vector3(5f, 0f, i), 0.012f, gridRoot.transform);
            }

            Material axisX = MakeUnlit(new Color(1f, 0.25f, 0.25f));
            Material axisZ = MakeUnlit(new Color(0.25f, 0.5f, 1f));
            Material axisY = MakeUnlit(new Color(0.3f, 1f, 0.3f));
            MakeLineGo("AxisX", axisX, new Vector3(-6f, 0f, 0f), new Vector3(6f, 0f, 0f), 0.03f, gridRoot.transform);
            MakeLineGo("AxisZ", axisZ, new Vector3(0f, 0f, -6f), new Vector3(0f, 0f, 6f), 0.03f, gridRoot.transform);
            MakeLineGo("AxisY", axisY, new Vector3(0f, 0f, 0f), new Vector3(0f, 2f, 0f), 0.03f, gridRoot.transform);
        }

        private void CreateDot()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "CurveDot";
            Collider col = go.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.16f;
            ApplyMaterial(go, MakeUnlit(ColDot));
            go.SetActive(false);
            dot = go;
        }

        // ------------------------------------------------------------------
        // HUD（IMGUI）
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!showHud)
                return;

            GUILayout.BeginArea(new Rect(12, 12, 330, Screen.height - 40));
            GUILayout.BeginVertical(GUI.skin.box);

            GUIStyle title = new GUIStyle(GUI.skin.label);
            title.fontStyle = FontStyle.Bold;
            title.fontSize = 15;
            GUILayout.Label("参数曲线建模与交互", title);

            // 模式
            GUILayout.Label("曲线模式（点击切换解释方式）：");
            GUILayout.BeginHorizontal();
            ModeButton(CurveMode.QuadraticBezier, "二次Bezier");
            ModeButton(CurveMode.CubicBezier, "三次Bezier");
            ModeButton(CurveMode.Hermite, "Hermite");
            ModeButton(CurveMode.CatmullRom, "Catmull-Rom");
            ModeButton(CurveMode.BSpline, "B样条");
            GUILayout.EndHorizontal();

            // 转换
            GUILayout.Label("表示转换：");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("→三次Bezier")) ConvertToBezierCubic();
            if (GUILayout.Button("→Hermite")) ConvertToHermite();
            if (GUILayout.Button("→Catmull-Rom")) ConvertToCatmullRom();
            if (GUILayout.Button("→B样条")) ConvertToBSpline();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("二次→三次 升阶")) ElevateToCubic();
            if (GUILayout.Button("重置示例 (R)")) ResetSample();
            GUILayout.EndHorizontal();

            // 状态
            string sel = selected >= 0 ? "#" + selected + (selectedIsTangent ? "(切线)" : "") : "-";
            GUILayout.Label("控制点: " + points.Count + "  段: " + segments.Count + "  选中: " + sel +
                            "  弧长: " + arcTable.Total.ToString("F2"));

            // 动画
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(animating ? "暂停动画 (Space)" : "播放动画 (Space)"))
                animating = !animating;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("速度", GUILayout.Width(36));
            animSpeed = GUILayout.HorizontalSlider(animSpeed, 0.05f, 1f);
            GUILayout.Label(animSpeed.ToString("F2"), GUILayout.Width(40));
            GUILayout.EndHorizontal();

            // 采样
            GUILayout.BeginHorizontal();
            GUILayout.Label("采样/段", GUILayout.Width(52));
            samplesPerSegment = Mathf.RoundToInt(GUILayout.HorizontalSlider(samplesPerSegment, 8f, 128f));
            GUILayout.Label(samplesPerSegment.ToString(), GUILayout.Width(40));
            GUILayout.EndHorizontal();

            showHelpers = GUILayout.Toggle(showHelpers, "显示辅助（控制多边形/切线/切线端点）");

            // 帮助
            GUILayout.Space(6);
            GUIStyle help = new GUIStyle(GUI.skin.label);
            help.fontSize = 11;
            help.wordWrap = true;
            GUILayout.Label(
                "交互设计：\n" +
                "· 左键拖拽控制点：水平面移动（保持 y）\n" +
                "· Shift+拖拽：仅竖直方向移动\n" +
                "· Ctrl+拖拽：面向相机平面自由移动\n" +
                "· 左键空白处：插入控制点（型值点模式插在\n" +
                "  曲线最近处；Bezier 模式在末端追加一组）\n" +
                "· X 删除选中（Bezier 删整组）  C 末端追加\n" +
                "· Tab 循环切换模式  R 重置  F 聚焦  F1 隐藏本面板\n" +
                "· 相机：右键旋转 中键平移 滚轮缩放",
                help);

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void ModeButton(CurveMode m, string label)
        {
            bool isOn = mode == m;
            bool clicked = GUILayout.Toggle(isOn, label, GUI.skin.button) && !isOn;
            if (clicked)
                SetMode(m);
        }
    }
}
