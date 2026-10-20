namespace LakskongenSim.Environment
{
    /// <summary>
    /// 2D vector with float components.
    /// </summary>
    public readonly struct Vector2 : IEquatable<Vector2>
    {
        public readonly float X;
        public readonly float Y;

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float Length() => MathF.Sqrt(X * X + Y * Y);
        public float LengthSquared() => X * X + Y * Y;
        
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);
        public static Vector2 operator *(Vector2 a, float s) => new Vector2(a.X * s, a.Y * s);
        public static Vector2 operator /(Vector2 a, float s) => new Vector2(a.X / s, a.Y / s);
        
        public static Vector2 Zero => new Vector2(0, 0);
        public static Vector2 One => new Vector2(1, 1);
        public static Vector2 UnitX => new Vector2(1, 0);
        public static Vector2 UnitY => new Vector2(0, 1);
        
        public bool Equals(Vector2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Vector2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X}, {Y})";
        
        public static bool operator ==(Vector2 a, Vector2 b) => a.Equals(b);
        public static bool operator !=(Vector2 a, Vector2 b) => !a.Equals(b);
        
        public static implicit operator System.Numerics.Vector2(Vector2 v) => new System.Numerics.Vector2(v.X, v.Y);
        public static explicit operator Vector2(System.Numerics.Vector2 v) => new Vector2(v.X, v.Y);
    }

    /// <summary>
    /// 3D vector with float components.
    /// </summary>
    public readonly struct Vector3 : IEquatable<Vector3>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float Length() => MathF.Sqrt(X * X + Y * Y + Z * Z);
        public float LengthSquared() => X * X + Y * Y + Z * Z;
        
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.X * s, a.Y * s, a.Z * s);
        public static Vector3 operator /(Vector3 a, float s) => new Vector3(a.X / s, a.Y / s, a.Z / s);
        
        public static Vector3 Zero => new Vector3(0, 0, 0);
        public static Vector3 One => new Vector3(1, 1, 1);
        public static Vector3 UnitX => new Vector3(1, 0, 0);
        public static Vector3 UnitY => new Vector3(0, 1, 0);
        public static Vector3 UnitZ => new Vector3(0, 0, 1);
        
        public Vector3 Normalized() => this / Length();
        
        public bool Equals(Vector3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object? obj) => obj is Vector3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X}, {Y}, {Z})";
        
        public static bool operator ==(Vector3 a, Vector3 b) => a.Equals(b);
        public static bool operator !=(Vector3 a, Vector3 b) => !a.Equals(b);
    }
}