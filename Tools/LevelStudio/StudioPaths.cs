using System;
using System.IO;

namespace ShiftGo.LevelStudio
{
    /// <summary>
    /// 找路径：编译期找不到工程根目录也没关系，运行时会从 exe / 当前目录往上找
    /// 「Assets/Scripts/Gameplay/BitFlow/Runtime/Core」这个特征目录。
    /// </summary>
    public static class StudioPaths
    {
        public static string ToolDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        public static string ProjectRoot => FindProjectRoot() ?? ToolDirectory;

        /// <summary>工程里给人看的关卡目录（exported 是 Unity 导出的参照，incoming 是待导入）。</summary>
        public static string LevelJsonDirectory => Path.Combine(ProjectRoot, "LevelJson");

        public static string ExportedDirectory => Path.Combine(LevelJsonDirectory, "exported");

        public static string IncomingDirectory => Path.Combine(LevelJsonDirectory, "incoming");

        /// <summary>类型资产快照：由 Unity 菜单「导出类型目录」生成，外部编辑器读它认 type。</summary>
        public static string TypeCatalogFile => Path.Combine(ExportedDirectory, "typeCatalog.json");

        /// <summary>打包后的玩家把 JSON 丢这里（= Application.persistentDataPath/LevelJson）。</summary>
        public static string PlayerLevelsDirectory
        {
            get
            {
                string persistent = PersistentDataPath();
                return persistent == null ? null : Path.Combine(persistent, "LevelJson");
            }
        }

        static string FindProjectRoot()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable("SHIFTGO_ROOT");
            if (LooksLikeRoot(fromEnvironment)) return Path.GetFullPath(fromEnvironment);

            string found = WalkUp(AppContext.BaseDirectory);
            if (found != null) return found;

            return WalkUp(Environment.CurrentDirectory);
        }

        static string WalkUp(string start)
        {
            if (string.IsNullOrEmpty(start)) return null;

            DirectoryInfo directory;
            try
            {
                directory = new DirectoryInfo(start);
            }
            catch (ArgumentException)
            {
                return null;
            }

            while (directory != null)
            {
                if (LooksLikeRoot(directory.FullName)) return directory.FullName;
                directory = directory.Parent;
            }

            return null;
        }

        static bool LooksLikeRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            try
            {
                return Directory.Exists(Path.Combine(path, "Assets"))
                    && Directory.Exists(Path.Combine(path, "Assets", "Scripts", "Gameplay", "BitFlow", "Runtime", "Core"));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Application.persistentDataPath 在 Windows 上是
        /// %USERPROFILE%\AppData\LocalLow\&lt;companyName&gt;\&lt;productName&gt;，
        /// 这两个名字从 ProjectSettings/ProjectSettings.asset 里读。
        /// </summary>
        static string PersistentDataPath()
        {
            string settings = Path.Combine(ProjectRoot, "ProjectSettings", "ProjectSettings.asset");
            string company = ReadYamlValue(settings, "companyName") ?? "DefaultCompany";
            string product = ReadYamlValue(settings, "productName") ?? new DirectoryInfo(ProjectRoot).Name;

            string localLow = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");

            return Path.Combine(localLow, company, product);
        }

        static string ReadYamlValue(string file, string key)
        {
            if (!File.Exists(file)) return null;

            try
            {
                foreach (string line in File.ReadLines(file))
                {
                    string trimmed = line.TrimStart();
                    if (!trimmed.StartsWith(key + ":", StringComparison.Ordinal)) continue;

                    string value = trimmed.Substring(key.Length + 1).Trim();
                    return value.Length == 0 ? null : value;
                }
            }
            catch (IOException)
            {
                return null;
            }

            return null;
        }
    }
}
