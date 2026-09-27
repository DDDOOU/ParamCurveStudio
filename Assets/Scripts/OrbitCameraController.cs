using UnityEngine;

namespace ParamCurve
{
    /// <summary>
    /// 轨道浏览相机（本工具的相机方案，左键让位给控制点交互）：
    ///   - 右键拖拽：旋转视角
    ///   - 中键拖拽：平移观察点
    ///   - 滚轮：缩放（指数速率）
    ///   - F：聚焦当前曲线模型
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class OrbitCameraController : MonoBehaviour
    {
        [Header("取景")]
        public float distance = 12f;
        public float minDistance = 0.05f;
        public float maxDistance = 1000f;

        [Header("操作灵敏度")]
        public float orbitSpeed = 3f;
        public float panSpeed = 1f;
        public float zoomSpeed = 2f;

        [Header("角度")]
        public float yaw = 35f;
        public float pitch = 28f;

        [Tooltip("为 false 时右键拖拽不再旋转（供需要独占右键的场景使用）。")]
        public bool allowOrbit = true;

        private Vector3 focusPoint;
        private bool initialized;
        private Camera cam;

        private void Start()
        {
            cam = GetComponent<Camera>();

            CurveController curve = ObjectFinder.FindFirst<CurveController>();
            if (curve != null && curve.PointCount > 0)
            {
                FocusOn(curve.CurveBounds);
                return;
            }
            Apply();
        }

        private void Update()
        {
            if (cam == null)
                cam = GetComponent<Camera>();

            // 右键：旋转
            if (allowOrbit && Input.GetMouseButton(1))
            {
                float dx = Input.GetAxis("Mouse X");
                float dy = Input.GetAxis("Mouse Y");
                yaw += dx * orbitSpeed * 10f;
                pitch -= dy * orbitSpeed * 10f;
                pitch = Mathf.Clamp(pitch, -89f, 89f);
            }

            // 中键：平移
            if (Input.GetMouseButton(2))
            {
                float dx = Input.GetAxis("Mouse X");
                float dy = Input.GetAxis("Mouse Y");
                Vector3 right = transform.right;
                Vector3 up = transform.up;
                float scale = distance * 0.0016f * panSpeed;
                focusPoint -= (right * dx + up * dy) * scale;
            }

            // 滚轮：缩放
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
                distance = Mathf.Clamp(distance * Mathf.Exp(-scroll * zoomSpeed), minDistance, maxDistance);

            // F：聚焦曲线
            if (Input.GetKeyDown(KeyCode.F))
                FocusSceneCurve();

            Apply();
        }

        private void Apply()
        {
            if (!initialized)
            {
                initialized = true;
                focusPoint = Vector3.zero;
            }

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 dir = rot * Vector3.back;
            transform.position = focusPoint + dir * distance;
            transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
        }

        // ------------------------------------------------------------------
        // 聚焦 API
        // ------------------------------------------------------------------

        public void FocusOn(Vector3 point, float targetDistance)
        {
            focusPoint = point;
            if (targetDistance > 0f)
                distance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
            initialized = true;
            Apply();
        }

        public void FocusOn(Bounds bounds)
        {
            if (cam == null)
                cam = GetComponent<Camera>();

            float r = bounds.extents.magnitude;
            if (r < 0.001f)
                r = 1f;

            float targetDistance = r * 2.2f;
            if (cam != null)
            {
                float vFov = cam.fieldOfView * Mathf.Deg2Rad;
                float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * cam.aspect);
                float minFov = Mathf.Min(vFov, hFov);
                if (minFov > 0.001f)
                    targetDistance = Mathf.Max(targetDistance, (r / Mathf.Sin(minFov * 0.5f)) * 1.15f);
            }

            FocusOn(bounds.center, targetDistance);
        }

        public void FocusSceneCurve()
        {
            CurveController curve = ObjectFinder.FindFirst<CurveController>();
            if (curve != null && curve.PointCount > 0)
                FocusOn(curve.CurveBounds);
        }
    }
}
