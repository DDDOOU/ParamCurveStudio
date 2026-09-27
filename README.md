# ParamCurveStudio — 参数曲线建模与交互（Unity）

在 Unity 场景中交互式建模参数曲线：自设计控制点交互方案，支持 **二次/三次 Bezier、Hermite、Catmull-Rom、B 样条** 五种表示，并提供相互转换、沿曲线匀速动画。

---

## 1. 快速开始

1. 用 Unity 打开本工程目录（推荐 **Unity 2022.3 LTS**，2021.3+ 均可；脚本已兼容 2023.x）。
2. 打开任意场景，执行菜单 **Tools → ParamCurveStudio → 一键搭建曲线建模场景**。
   - 自动创建：`Main Camera`（含轨道相机控制）+ `Directional Light` + `CurveStudio`（含 `CurveController`）。
   - 场景自动保存到 `Assets/Scenes/ParamCurveStudio.unity`。
3. 点击 **Play**。加载示例曲线（Catmull-Rom），即可开始交互。

> 说明：曲线渲染、控制点球体、网格、HUD 均由 `CurveController` 在运行时自动创建，工程无需额外美术资源。

## 2. 交互设计（自设计的控制点交互方法）

| 操作 | 效果 |
|---|---|
| 左键拖拽控制点 | 选中并在**水平面 (XZ)** 移动（保持 y 不变） |
| Shift + 拖拽 | 仅**竖直方向**移动（保持 x,z） |
| Ctrl + 拖拽 | 在**面向相机的平面**上自由 3D 移动 |
| 左键空白处 | 插入控制点：型值点模式插入到**曲线上最近位置**；Bezier 模式在末端追加“曲线点 + 手柄”一组 |
| X | 删除选中控制点（Bezier 模式删除整组：曲线点 + 手柄） |
| C | 在末端追加控制点 |
| Tab | 循环切换 5 种曲线模式（不转换数据，只改变解释方式） |
| R | 重置示例曲线 |
| F | 相机聚焦曲线 |
| F1 | 显示/隐藏 HUD 面板 |
| H | 显示/隐藏辅助（控制多边形 / Hermite 切线） |
| Space | 沿曲线**匀速**动画（弧长参数化） |
| 右键拖拽 / 中键拖拽 / 滚轮 | 相机旋转 / 平移 / 缩放（左键让位给建模） |

拾取与高亮：控制点悬停变白、选中变红；Hermite 模式显示切线端点（橙色小球）可单独拖拽，直接编辑端点切线。

## 3. 曲线类型与表示转换

| 模式 | 说明 |
|---|---|
| 二次 Bezier | 每 2 点一组（1 曲线点 + 1 手柄），段间 C0 |
| 三次 Bezier | 每 3 点一组（1 曲线点 + 2 手柄），段间 C0 |
| Hermite | 型值点 + 显式端点切线，可拖拽切线端点，段间 C1 |
| Catmull-Rom | 型值点（曲线过点），相邻四点构造，段间 C1 |
| B 样条 | uniform 三次 B 样条（曲线不过点、逼近控制多边形），段间 C2 |

HUD 上的转换按钮：

| 转换 | 精度 | 机制 |
|---|---|---|
| → 三次 Bezier | **精确** | Hermite/CR/B 样条→Bezier 为同一多项式的系数重写；二次 Bezier 升阶 |
| → Hermite | **精确** | 取每段 Bezier 端点 + 端点切线（×3） |
| → Catmull-Rom | 近似 | 提取“位于曲线上”的控制点作为新型值点，形状保留、可继续编辑 |
| → B 样条 | 近似 | 同上（提取曲线点作为控制点） |
| 二次→三次 升阶 | **精确** | 二次 Bezier 升阶为三次 Bezier |

全部模式在内部统一转换为 **三次 Bezier 段列表** 再进行采样/求值/求导（`CurveSampler`），因此所有转换都建立在同一条数学通路上。

## 4. 工程结构

```
Assets/
  Scripts/
    Core/                纯 C# 数学核心（不依赖 UnityEngine，可离线测试）
      Vec3.cs            轻量三维向量
      CurveMath.cs       Bezier / Hermite / Catmull-Rom / B 样条求值与导数
      CurveConversions.cs 各表示间精确转换
      CurveSampler.cs    统一段转换 / 全局求值 / 折线采样 / 弧长表
    ObjectFinder.cs      跨 Unity 版本的对象查找
    OrbitCameraController.cs 轨道浏览相机
    CurveController.cs   建模交互主组件（拾取/拖拽/插入/删除/转换/HUD/动画）
  Editor/
    CurveStudioSetup.cs  一键搭建场景菜单
  Scenes/
    ParamCurveStudio.unity （由菜单生成）
ProjectSettings/ProjectVersion.txt
Tests/
  CurveMathTest.cs       离线数学测试（38 项断言）
  CurveMathTest.exe       已编译测试程序（可重跑）
```

## 5. 数学核心与离线验证

- 核心库全部位于 `ParamCurve` 命名空间，**不依赖 UnityEngine**，因此既能被 Unity 工程编译，也能被 .NET csc 独立编译验证。
- 测试程序覆盖：端点插值、Hermite↔Bezier 双向还原、CR/B 样条→Bezier 数值等价、B 样条端点公式 (q0+4q1+q2)/6、二次升阶形状不变、段间 C0/C1/C2 连续性、弧长单调/中点、折线点数公式、空数据保护等 **38 项断言，全部 PASS**（退出码 0）。
- 重跑测试（Windows）：
  `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:Tests\CurveMathTest.exe Tests\CurveMathTest.cs Assets\Scripts\Core\Vec3.cs Assets\Scripts\Core\CurveMath.cs Assets\Scripts\Core\CurveConversions.cs Assets\Scripts\Core\CurveSampler.cs && Tests\CurveMathTest.exe`

## 6. 已知限制

- 本项目在无 Unity 编辑器的机器上开发，运行时行为（拖拽手感、HUD、相机）需在 Unity 中实际体验；数学核心已离线全量验证。
- 插入点的水平面取**当前控制点平均 y**；如需精确落到场景网格，可后续扩展。
- →Catmull-Rom 与 →B 样条为近似转换（形状保留，编辑语义改变），文档已注明。
- 编辑结果保存在内存中，退出运行模式即还原；如需持久化可后续加场景序列化。
- LineRenderer/材质为运行时创建，未打进预制体；运行模式退出后场景中的几何不保留（再次 Play 自动重建）。
