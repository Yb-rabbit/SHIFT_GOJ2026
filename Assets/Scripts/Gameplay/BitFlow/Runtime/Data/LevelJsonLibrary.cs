using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 选关面板上的一条 JSON 关卡。<b>不持有</b> <see cref="LevelSO"/>：
    /// 面板只是列个名字，真正进关时再读一遍文件 —— 玩家手改了 JSON，重开面板就能看到最新内容，
    /// 也不会出现「面板缓存了一份、关卡跑的是另一份」这种所有权扯不清的情况。
    /// </summary>
    public sealed class LevelJsonEntry
    {
        public readonly string Path;
        public readonly string DisplayName;
        public readonly int Width;
        public readonly int Height;

        public LevelJsonEntry(string path, string displayName, int width, int height)
        {
            Path = path;
            DisplayName = string.IsNullOrEmpty(displayName)
                ? System.IO.Path.GetFileNameWithoutExtension(path)
                : displayName;
            Width = width;
            Height = height;
        }

        /// <summary>「文件名 · 宽x高」，面板拿它当副标题。</summary>
        public string Subtitle => System.IO.Path.GetFileName(Path) + "  " + Width + "x" + Height;
    }

    /// <summary>
    /// 运行时关卡 JSON 的落盘目录与读取入口 —— <b>打包后也有</b>。
    ///
    /// 目录固定为 <c>Application.persistentDataPath/LevelJson</c>，不放 Assets：
    /// 打包后的项目里没有 Assets，而 persistentDataPath 是 Player 唯一
    /// 「作者/玩家能直接扔文件进去」的地方。编辑器里同一个路径照样能读写，
    /// 所以三个环境（Play / EditMode / Player）共用这一份代码，不用 #if UNITY_EDITOR。
    ///
    /// 编辑器侧另有一个菜单把工程里的 <c>LevelJson/</c> 同步过来，见 LevelJsonRuntimeSetup。
    /// </summary>
    public static class LevelJsonLibrary
    {
        /// <summary>persistentDataPath 下的关卡目录名。</summary>
        public const string FolderName = "LevelJson";

        /// <summary>关卡 JSON 目录的绝对路径（可能还不存在）。</summary>
        public static string Folder => System.IO.Path.Combine(Application.persistentDataPath, FolderName);

        /// <summary>建目录（已存在就是空操作）。</summary>
        public static void EnsureFolder()
        {
            string folder = Folder;
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        }

        /// <summary>
        /// 列出目录里所有能读通的 <c>*.json</c> 关卡（按文件名排序，大小写不敏感）。
        /// 读不出来的文件不会静默消失：原因都塞进 <paramref name="problems"/>，交给面板/日志显示。
        /// </summary>
        public static List<LevelJsonEntry> Scan(out List<string> problems)
        {
            problems = new List<string>();
            List<LevelJsonEntry> entries = new List<LevelJsonEntry>();

            string folder = Folder;
            if (!Directory.Exists(folder))
            {
                problems.Add("还没有关卡目录：" + folder);
                return entries;
            }

            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            if (registry == null)
            {
                problems.Add("没有类型注册表（Resources/" + LevelTypeRegistrySO.ResourcePath + "）");
                return entries;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(folder, "*.json");
            }
            catch (Exception exception)
            {
                problems.Add("列目录失败：" + exception.Message);
                return entries;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            LevelTypeRegistryResolver resolver = new LevelTypeRegistryResolver(registry);
            foreach (string file in files)
            {
                // 下划线开头的是模板 / 草稿，不该出现在选关列表里
                if (System.IO.Path.GetFileName(file).StartsWith("_", StringComparison.Ordinal)) continue;

                if (!TryRead(file, resolver, out LevelSO level, out List<string> fileProblems))
                {
                    foreach (string problem in fileProblems)
                    {
                        problems.Add(System.IO.Path.GetFileName(file) + "：" + problem);
                    }
                    continue;
                }

                entries.Add(new LevelJsonEntry(file, level.displayName, level.width, level.height));

                // 面板只要元数据：临时资产立刻回收，免得每开一次面板堆一堆 LevelSO
                DestroyLevel(level);
            }

            return entries;
        }

        /// <summary>读一个 JSON 文件成一个<b>临时</b> <see cref="LevelSO"/>（调用方负责用完销毁）。</summary>
        public static bool TryLoad(string path, out LevelSO level, out List<string> problems)
        {
            level = null;
            problems = new List<string>();

            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            if (registry == null)
            {
                problems.Add("没有类型注册表（Resources/" + LevelTypeRegistrySO.ResourcePath + "）");
                return false;
            }

            return TryRead(path, new LevelTypeRegistryResolver(registry), out level, out problems);
        }

        static bool TryRead(string path, ILevelJsonTypeResolver resolver, out LevelSO level, out List<string> problems)
        {
            level = null;
            problems = new List<string>();

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception exception)
            {
                problems.Add("读不了文件：" + exception.Message);
                return false;
            }

            LevelJsonReadResult result = LevelJsonSerializer.Read(json, resolver);
            if (!result.HasLevel)
            {
                problems.AddRange(result.Errors);
                if (problems.Count == 0) problems.Add("读不出关卡");
                return false;
            }

            if (result.Errors.Count > 0)
            {
                // 有错 = 这份文件还不完整，不拿半成品去跑关（否则玩家会看到一关莫名其妙的东西）
                problems.AddRange(result.Errors);
                DestroyLevel(result.Level);
                return false;
            }

            foreach (string warning in result.Warnings)
            {
                Debug.LogWarning("[BitFlow] " + System.IO.Path.GetFileName(path) + "：" + warning);
            }

            level = result.Level;
            return true;
        }

        /// <summary>回收运行时造出来的临时关卡资产（播放中 Destroy、编辑器里 DestroyImmediate）。</summary>
        public static void DestroyLevel(LevelSO level)
        {
            if (level == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(level);
            else UnityEngine.Object.DestroyImmediate(level);
        }
    }
}
