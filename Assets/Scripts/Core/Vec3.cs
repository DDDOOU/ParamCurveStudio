using System;

namespace ParamCurve
{
    /// <summary>
    /// 轻量 3D 向量（纯数学，不依赖 UnityEngine）。
    /// 与 UnityEngine.Vector3 字段布局一致，可在 Unity 侧直接互相转换。
    /// </summary>
    [Serializable]
    public struct Vec3
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 Zero
        {
            get { return new Vec3(0f, 0f, 0f); }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b)
        {
            return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Vec3 operator -(Vec3 a, Vec3 b)
        {
            return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static Vec3 operator -(Vec3 a)
        {
            return new Vec3(-a.X, -a.Y, -a.Z);
        }

        public static Vec3 operator *(Vec3 a, float s)
        {
            return new Vec3(a.X * s, a.Y * s, a.Z * s);
        }

        public static Vec3 operator *(float s, Vec3 a)
        {
            return new Vec3(a.X * s, a.Y * s, a.Z * s);
        }

        public static Vec3 operator /(Vec3 a, float s)
        {
            return new Vec3(a.X / s, a.Y / s, a.Z / s);
        }

        public float LengthSq
        {
            get { return X * X + Y * Y + Z * Z; }
        }

        public float Length
        {
            get { return (float)Math.Sqrt(LengthSq); }
        }

        public static float Dot(Vec3 a, Vec3 b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        public override string ToString()
        {
            return "(" + X + ", " + Y + ", " + Z + ")";
        }
    }
}
