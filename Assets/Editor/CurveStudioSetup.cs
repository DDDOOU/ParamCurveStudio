using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ParamCurve;

namespace ParamCurve.EditorTools
{
    /// <summary>
    /// 一键搭建曲线建模场景：菜单 Tools/ParamCurveStudio/一键搭建曲线建模场景
    /// 生成：轨道相机 + 方向光 + CurveStudio（CurveController），并保存场景。
    /// </summary>
    public static class CurveStudioSetup
    {
        [MenuItem("Tools/ParamCurveStudio/一键搭建曲线建模场景", priority = 1)]
        public static void BuildScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 1) 主相机 + 轨道控制（右键旋转 / 中键平移 / 滚轮缩放 / F 聚焦）
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            OrbitCameraController orbit = camGo.AddComponent<OrbitCameraController>();
            orbit.yaw = 35f;
            orbit.pitch = 28f;
            orbit.distance = 14f;

            // 2) 方向光
            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 3) 曲线建模对象（控制点、曲线、网格、HUD 均由该组件在运行时创建）
            GameObject studioGo = new GameObject("CurveStudio");
            studioGo.AddComponent<CurveController>();

            // 4) 保存场景
            string path = "Assets/Scenes/ParamCurveStudio.unity";
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path);

            Selection.activeGameObject = studioGo;
            Debug.Log("[CurveStudio] 场景已搭建并保存：" + path +
                      "\n按 Play 进入运行模式，即可拖拽/插入/删除控制点、切换与转换曲线模式。");
        }
    }
}
