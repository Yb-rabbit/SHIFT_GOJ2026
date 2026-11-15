using System;
using System.Collections.Generic;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>一道已经落盘的 OVERTIME 题在状态侧车里的记录。</summary>
    [Serializable]
    public sealed class OvertimeProblemRecord
    {
        public int index;
        public string displayName;
        public string fileName;
        public int width;
        public int height;
        public int seed;
        public int targetSteps;
        public int ticks;
        public string createdAt;
    }

    /// <summary>
    /// <c>overtime_state.sav</c> 的内存形态。这个文件只保存“生成器需要记住的东西”：
    /// 基础 seed、下一题题号、是否有开场、以及已生成题目的索引。
    /// 关卡本身仍然是一题一个 JSON，状态丢了可以从目录重建。
    /// </summary>
    [Serializable]
    public sealed class OvertimeState
    {
        public int version = OvertimeConfig.StateVersion;
        public int baseSeed;
        public int nextProblem = OvertimeConfig.FirstProblem;
        public int minTicks;
        public bool introSeen;
        public List<OvertimeProblemRecord> problems = new List<OvertimeProblemRecord>();

        public int GeneratedCount
        {
            get
            {
                int count = 0;
                if (problems == null) return 0;
                foreach (OvertimeProblemRecord record in problems)
                {
                    if (record != null && OvertimeConfig.IsValidIndex(record.index)) count++;
                }
                return count;
            }
        }

        public bool CanGenerate => nextProblem >= OvertimeConfig.FirstProblem
            && nextProblem <= OvertimeConfig.LastProblem;

        public OvertimeProblemRecord Find(int index)
        {
            if (problems == null) return null;
            foreach (OvertimeProblemRecord record in problems)
            {
                if (record != null && record.index == index) return record;
            }
            return null;
        }

        /// <summary>
        /// 把外部读到的字段夹回合法范围，并按题号排序。
        /// 旧版本 / 手改文件少字段时也走这里，调用方不需要分别处理。
        /// </summary>
        public void Normalize()
        {
            if (version < OvertimeConfig.StateVersion) version = OvertimeConfig.StateVersion;
            if (baseSeed == 0) baseSeed = OvertimeSeed.CreateBaseSeed();
            if (minTicks < 0) minTicks = 0;
            if (problems == null) problems = new List<OvertimeProblemRecord>();

            problems.RemoveAll(record => record == null || !OvertimeConfig.IsValidIndex(record.index));
            problems.Sort((left, right) => left.index.CompareTo(right.index));

            int maxIndex = 0;
            List<int> seen = new List<int>();
            for (int i = problems.Count - 1; i >= 0; i--)
            {
                OvertimeProblemRecord record = problems[i];
                if (seen.Contains(record.index))
                {
                    problems.RemoveAt(i);
                    continue;
                }
                seen.Add(record.index);
                if (record.index > maxIndex) maxIndex = record.index;
            }

            nextProblem = maxIndex + 1;
            if (nextProblem < OvertimeConfig.FirstProblem) nextProblem = OvertimeConfig.FirstProblem;
            if (nextProblem > OvertimeConfig.LastProblem + 1) nextProblem = OvertimeConfig.LastProblem + 1;
        }
    }
}
