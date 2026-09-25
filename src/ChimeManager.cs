using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace EpicChimeMuter
{
    public enum ChimeState { NotFound, Empty, AllOn, AllMuted, Mixed }

    public class ChimeFile
    {
        public string Name;      // e.g. PartyUserJoined.wav
        public string FullPath;  // actual path on disk (.wav or .wav.off)
        public bool Muted;

        public ChimeFile(string name, string fullPath, bool muted)
        {
            Name = name;
            FullPath = fullPath;
            Muted = muted;
        }

        // "PartyUserJoined.wav" -> "Party User Joined"
        public string FriendlyName
        {
            get
            {
                string n = Path.GetFileNameWithoutExtension(Name);
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < n.Length; i++)
                {
                    if (i > 0 && char.IsUpper(n[i]) && !char.IsUpper(n[i - 1])) sb.Append(' ');
                    sb.Append(n[i]);
                }
                return sb.ToString();
            }
        }
    }

    public class ChimeManager
    {
        public const string OffSuffix = ".off";
        const string ChimesDirName = "SocialChimes";
        const string RelativePath = @"Epic Games\Launcher\Portal\Extras\SocialChimes";

        readonly bool demo;
        readonly string settingsPath;   // null = don't persist anything
        string customFolder;            // folder the user picked by hand
        HashSet<string> wanted;         // chimes the user wants muted; null until first saved

        public string Folder { get; private set; }
        public string FolderSource { get; private set; }
        public List<ChimeFile> Files { get; private set; }

        public ChimeManager() : this(null, DefaultSettingsPath) { }

        // folder: fixed folder to use (null = auto-detect). settingsPath: where to persist choices (null = nowhere).
        public ChimeManager(string folder, string settingsPath)
        {
            Files = new List<ChimeFile>();
            this.settingsPath = settingsPath;
            LoadSettings();
            if (folder != null)
            {
                Folder = folder;
                FolderSource = "Custom folder";
            }
        }

        // Fake data for rendering README screenshots; never touches disk.
        public ChimeManager(string folder, IEnumerable<ChimeFile> files, string source, IEnumerable<string> wantedMuted)
        {
            demo = true;
            Folder = folder;
            FolderSource = source;
            Files = new List<ChimeFile>(files);
            wanted = new HashSet<string>(wantedMuted, StringComparer.OrdinalIgnoreCase);
        }

        public ChimeState State
        {
            get
            {
                if (Folder == null) return ChimeState.NotFound;
                if (Files.Count == 0) return ChimeState.Empty;
                int muted = MutedCount;
                if (muted == 0) return ChimeState.AllOn;
                if (muted == Files.Count) return ChimeState.AllMuted;
                return ChimeState.Mixed;
            }
        }

        public int MutedCount { get { return Files.Count(f => f.Muted); } }

        public bool IsCustomFolder { get { return customFolder != null; } }

        // Chimes the user muted that are playing again (an Epic update put the .wav back).
        public List<ChimeFile> Restored
        {
            get
            {
                if (wanted == null) return new List<ChimeFile>();
                return Files.Where(f => !f.Muted && wanted.Contains(f.Name)).ToList();
            }
        }

        // ------------------------------------------------------------------ locating the folder

        public void Scan()
        {
            if (demo) return;
            if (Folder == null || !Directory.Exists(Folder) || (FolderSource != "Custom folder" && !HasChimes(Folder)))
                Locate();

            var byName = new Dictionary<string, ChimeFile>(StringComparer.OrdinalIgnoreCase);
            if (Folder != null)
            {
                foreach (string path in SafeGetFiles(Folder))
                {
                    string name = Path.GetFileName(path);
                    if (IsMutedName(name))
                    {
                        string baseName = name.Substring(0, name.Length - OffSuffix.Length);
                        if (!byName.ContainsKey(baseName)) byName[baseName] = new ChimeFile(baseName, path, true);
                    }
                    else if (IsWavName(name))
                    {
                        // A live .wav always wins: if Epic restored it, the chime is audible.
                        byName[name] = new ChimeFile(name, path, false);
                    }
                }
            }
            Files = byName.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

            // First run: whatever is muted on disk right now is what the user wants.
            if (wanted == null && Files.Count > 0)
            {
                wanted = new HashSet<string>(Files.Where(f => f.Muted).Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
                SaveSettings();
            }
        }

        // Tries, in order: the folder the user picked, the default install paths, the install path
        // Windows has registered for the launcher, the running launcher's exe, then a search inside
        // any launcher folder found. A folder only counts if it actually contains chime files, so an
        // emptied-out old location doesn't hide a new one.
        void Locate()
        {
            string firstExisting = null, firstSource = null;
            foreach (KeyValuePair<string, string> c in Candidates())
            {
                if (!Directory.Exists(c.Key)) continue;
                if (HasChimes(c.Key))
                {
                    Folder = c.Key;
                    FolderSource = c.Value;
                    return;
                }
                if (firstExisting == null) { firstExisting = c.Key; firstSource = c.Value; }
            }

            foreach (KeyValuePair<string, string> root in LauncherRoots())
            {
                string found = SearchForChimes(root.Key);
                if (found != null)
                {
                    Folder = found;
                    FolderSource = "Found by searching " + root.Value;
                    return;
                }
            }

            Folder = firstExisting;
            FolderSource = firstSource;
        }

        IEnumerable<KeyValuePair<string, string>> Candidates()
        {
            if (customFolder != null)
                yield return new KeyValuePair<string, string>(customFolder, "Custom folder");
            foreach (string root in ProgramFilesRoots())
                yield return new KeyValuePair<string, string>(Path.Combine(root, RelativePath), "Default location");
        }

        static IEnumerable<string> ProgramFilesRoots()
        {
            var roots = new[]
            {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetEnvironmentVariable("ProgramFiles"),
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            };
            return roots.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        // Folders that should contain the launcher, from the most to the least reliable source.
        static IEnumerable<KeyValuePair<string, string>> LauncherRoots()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string exe in RunningLauncherExes())
            {
                // ...\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe -> ...\Launcher
                string dir = Path.GetDirectoryName(exe);
                while (dir != null && !string.Equals(Path.GetFileName(dir), "Launcher", StringComparison.OrdinalIgnoreCase))
                    dir = Path.GetDirectoryName(dir);
                if (dir == null) dir = Path.GetDirectoryName(Path.GetDirectoryName(exe));
                if (dir != null && seen.Add(dir)) yield return new KeyValuePair<string, string>(dir, "the running launcher");
            }

            foreach (string install in RegistryInstallLocations())
            {
                string launcher = Path.Combine(install, "Launcher");
                string dir = Directory.Exists(launcher) ? launcher : install;
                if (seen.Add(dir)) yield return new KeyValuePair<string, string>(dir, "Epic's install location");
            }

            foreach (string root in ProgramFilesRoots())
            {
                string dir = Path.Combine(root, @"Epic Games\Launcher");
                if (Directory.Exists(dir) && seen.Add(dir)) yield return new KeyValuePair<string, string>(dir, "Program Files");
            }
        }

        static IEnumerable<string> RunningLauncherExes()
        {
            var list = new List<string>();
            try
            {
                foreach (Process p in Process.GetProcessesByName("EpicGamesLauncher"))
                {
                    try { list.Add(p.MainModule.FileName); }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return list;
        }

        static IEnumerable<string> RegistryInstallLocations()
        {
            var list = new List<string>();
            string[] keys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            };
            foreach (string keyPath in keys)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
                    {
                        if (key == null) continue;
                        foreach (string sub in key.GetSubKeyNames())
                        {
                            using (RegistryKey app = key.OpenSubKey(sub))
                            {
                                if (app == null) continue;
                                string name = app.GetValue("DisplayName") as string;
                                string loc = app.GetValue("InstallLocation") as string;
                                if (name != null && loc != null && name.IndexOf("Epic Games Launcher", StringComparison.OrdinalIgnoreCase) >= 0
                                    && Directory.Exists(loc))
                                    list.Add(loc);
                            }
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        // Breadth-first search for a folder holding the chime files. Bounded so it stays fast.
        public static string SearchForChimes(string root)
        {
            if (root == null || !Directory.Exists(root)) return null;
            if (HasChimes(root)) return root;

            var queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(root, 0));
            string fallback = null;
            int visited = 0;
            while (queue.Count > 0 && visited < 6000)
            {
                KeyValuePair<string, int> item = queue.Dequeue();
                visited++;
                string[] subs;
                try { subs = Directory.GetDirectories(item.Key); }
                catch { continue; }
                foreach (string sub in subs)
                {
                    if (HasChimes(sub))
                    {
                        if (string.Equals(Path.GetFileName(sub), ChimesDirName, StringComparison.OrdinalIgnoreCase)) return sub;
                        if (fallback == null && HasKnownChime(sub)) fallback = sub;
                    }
                    if (item.Value < 7) queue.Enqueue(new KeyValuePair<string, int>(sub, item.Value + 1));
                }
            }
            return fallback;
        }

        static bool HasChimes(string folder)
        {
            return SafeGetFiles(folder).Any(p => IsWavName(p) || IsMutedName(p));
        }

        static bool HasKnownChime(string folder)
        {
            return SafeGetFiles(folder).Any(p => Path.GetFileName(p).StartsWith("PartyUser", StringComparison.OrdinalIgnoreCase)
                                              || Path.GetFileName(p).StartsWith("PushToTalk", StringComparison.OrdinalIgnoreCase));
        }

        // Accepts the SocialChimes folder itself, or any folder above it (e.g. "Epic Games").
        // Returns false if no chime files could be found there.
        public bool ChooseFolder(string picked)
        {
            string found = SearchForChimes(picked);
            if (found == null) return false;
            customFolder = found;
            Folder = found;
            FolderSource = "Custom folder";
            SaveSettings();
            Scan();
            return true;
        }

        public void UseAutoDetect()
        {
            customFolder = null;
            Folder = null;
            SaveSettings();
            Scan();
        }

        // ------------------------------------------------------------------ muting

        // Renames every *.wav to *.wav.off. Returns the number of files renamed.
        public int MuteAll()
        {
            return Apply(Files.Select(f => f.Name), true);
        }

        // Renames every *.wav.off back to *.wav. Returns the number of files restored.
        public int UnmuteAll()
        {
            return Apply(Files.Select(f => f.Name), false);
        }

        public void SetMuted(ChimeFile file, bool muted)
        {
            Apply(new[] { file.Name }, muted);
        }

        // Mutes the chimes the user had muted before an Epic update restored them.
        public int ReapplyMuted()
        {
            return Apply(Restored.Select(f => f.Name), true);
        }

        int Apply(IEnumerable<string> names, bool mute)
        {
            if (demo || Folder == null) return 0;
            int count = 0;
            var errors = new List<string>();
            foreach (string name in names.ToList())
            {
                try
                {
                    if (mute ? MuteFile(name) : UnmuteFile(name)) count++;
                }
                catch (Exception ex)
                {
                    errors.Add(name + ": " + ex.Message);
                }
                if (wanted == null) wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (mute) wanted.Add(name); else wanted.Remove(name);
            }
            SaveSettings();
            Scan();
            if (errors.Count > 0) throw new IOException(string.Join("\n", errors.ToArray()));
            return count;
        }

        bool MuteFile(string name)
        {
            string live = Path.Combine(Folder, name);
            string off = live + OffSuffix;
            if (!File.Exists(live)) return false;
            if (File.Exists(off))
            {
                // Stale copy left over from before an Epic update restored the .wav.
                File.SetAttributes(off, FileAttributes.Normal);
                File.Delete(off);
            }
            File.Move(live, off);
            return true;
        }

        bool UnmuteFile(string name)
        {
            string live = Path.Combine(Folder, name);
            string off = live + OffSuffix;
            if (!File.Exists(off)) return false;
            if (File.Exists(live))
            {
                File.SetAttributes(off, FileAttributes.Normal);
                File.Delete(off);
            }
            else
            {
                File.Move(off, live);
            }
            return true;
        }

        static bool IsWavName(string name)
        {
            return name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsMutedName(string name)
        {
            return name.EndsWith(".wav" + OffSuffix, StringComparison.OrdinalIgnoreCase);
        }

        static string[] SafeGetFiles(string folder)
        {
            try { return Directory.GetFiles(folder); }
            catch { return new string[0]; }
        }

        // ------------------------------------------------------------------ settings

        static string DefaultSettingsPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    @"EpicChimeMuter\settings.ini");
            }
        }

        void LoadSettings()
        {
            if (settingsPath == null) return;
            try
            {
                // v1.0 stored only the picked folder in folder.txt.
                string legacy = Path.Combine(Path.GetDirectoryName(settingsPath), "folder.txt");
                if (!File.Exists(settingsPath) && File.Exists(legacy))
                    customFolder = File.ReadAllText(legacy).Trim();

                if (!File.Exists(settingsPath)) return;
                foreach (string line in File.ReadAllLines(settingsPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                    if (key == "folder" && value.Length > 0) customFolder = value;
                    else if (key == "muted")
                        wanted = new HashSet<string>(value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                                                     StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }
        }

        void SaveSettings()
        {
            if (settingsPath == null || demo) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                var lines = new List<string>();
                lines.Add("# Epic Chime Muter settings");
                lines.Add("folder=" + (customFolder ?? ""));
                if (wanted != null) lines.Add("muted=" + string.Join(";", wanted.OrderBy(n => n).ToArray()));
                File.WriteAllLines(settingsPath, lines.ToArray());
            }
            catch { }
        }
    }
}
