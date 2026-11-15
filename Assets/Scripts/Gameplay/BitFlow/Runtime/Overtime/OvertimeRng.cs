using System;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// 确定性随机数（xorshift32）。不用 <see cref="System.Random"/> 是为了：
    /// 1) 同一个 seed 在编辑器 / Player / 测试里给出一模一样的序列；
    /// 2) 生成器可以在失败后按 attempt 重新开一条完全独立的序列。
    /// </summary>
    public sealed class OvertimeRng
    {
        uint state;

        public OvertimeRng(int seed)
        {
            state = unchecked((uint)seed);
            if (state == 0) state = 0x9E3779B9u;
        }

        public uint NextUInt()
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            return (int)(NextUInt() % (uint)maxExclusive);
        }

        public bool Chance(int percent)
        {
            return Next(100) < percent;
        }
    }

    /// <summary>
    /// seed 的派生根。基础 seed 落进 overtime_state.json，题号再派生出每题 seed，
    /// 这样「第 N 题」与「当时生成了多少题」无关，重装状态也能复现同一道题。
    /// </summary>
    public static class OvertimeSeed
    {
        public static int CreateBaseSeed()
        {
            long ticks = DateTime.UtcNow.Ticks;
            return unchecked((int)(ticks ^ (ticks >> 32)));
        }

        public static int ForProblem(int baseSeed, int index)
        {
            unchecked
            {
                uint h = (uint)baseSeed + 0x9E3779B9u;
                h ^= (uint)index * 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return (int)h;
            }
        }
    }
}
