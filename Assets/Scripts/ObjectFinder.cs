using UnityEngine;

namespace ParamCurve
{
    /// <summary>跨 Unity 版本兼容的场景查找辅助（2023.1 起 FindObjectOfType 被标记废弃）。</summary>
    public static class ObjectFinder
    {
        public static T FindFirst<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }
    }
}
