using System;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// 正交网格坐标。X 向右为正、Y 向上为正；世界坐标换算放在 View 层，Core 层不关心。
    /// 做成 readonly struct 是为了当字典 key 用而不产生 GC。
    /// </summary>
    public readonly struct GridPoint : IEquatable<GridPoint>
    {
        public readonly int X;
        public readonly int Y;

        public GridPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static GridPoint operator +(GridPoint a, GridPoint b) => new GridPoint(a.X + b.X, a.Y + b.Y);

        public static bool operator ==(GridPoint a, GridPoint b) => a.X == b.X && a.Y == b.Y;

        public static bool operator !=(GridPoint a, GridPoint b) => !(a == b);

        public bool Equals(GridPoint other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is GridPoint other && Equals(other);

        public override int GetHashCode() => (X * 397) ^ Y;

        public override string ToString() => "(" + X + "," + Y + ")";
    }
}
