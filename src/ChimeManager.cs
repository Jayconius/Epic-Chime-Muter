using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        const string RelativePath = @"Epic Games\Launcher\Portal\Extras\SocialChimes";

        readonly bool demo;

        public string Folder { get; private set; }
        public List<ChimeFile> Files { get; private set; }

        public ChimeManager()
        {
            Files = new List<ChimeFile>();
        }

        // Works against a specific folder (used for testing).
        public ChimeManager(string folder) : this()
        {
            Folder = folder;
        }

        // Fake data for rendering README screenshots; never touches disk.
        public ChimeManager(string folder, IEnumerable<ChimeFile> files)
        {
            demo = true;
            Folder = folder;
            Files = new List<ChimeFile>(files);
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

        public static string FindFolder()
        {
            var roots = new[]
            {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetEnvironmentVariable("ProgramFiles"),
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            };
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                string path = Path.Combine(root, RelativePath);
                if (Directory.Exists(path)) return path;
            }

            string saved = LoadSavedFolder();
            if (saved != null && Directory.Exists(saved)) return saved;
            return null;
        }

        public void Scan()
        {
            if (demo) return;
            if (Folder == null || !Directory.Exists(Folder)) Folder = FindFolder();

            var byName = new Dictionary<string, ChimeFile>(StringComparer.OrdinalIgnoreCase);
            if (Folder != null)
            {
                foreach (string path in SafeGetFiles(Folder))
                {
                    string name = Path.GetFileName(path);
                    string lower = name.ToLowerInvariant();
                    if (lower.EndsWith(".wav" + OffSuffix))
                    {
                        string baseName = name.Substring(0, name.Length - OffSuffix.Length);
                        if (!byName.ContainsKey(baseName)) byName[baseName] = new ChimeFile(baseName, path, true);
                    }
                    else if (lower.EndsWith(".wav"))
                    {
                        // A live .wav always wins: if Epic restored it, the chime is audible.
                        byName[name] = new ChimeFile(name, path, false);
                    }
                }
            }
            Files = byName.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public void SetFolder(string folder)
        {
            Folder = folder;
            SaveFolder(folder);
            Scan();
        }

        // Renames every *.wav to *.wav.off. Returns the number of files renamed.
        public int MuteAll()
        {
            if (demo || Folder == null) return 0;
            int count = 0;
            var errors = new List<string>();
            foreach (string path in SafeGetFiles(Folder))
            {
                if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    string target = path + OffSuffix;
                    if (File.Exists(target))
                    {
                        // Stale copy left over from before an Epic update restored the .wav.
                        File.SetAttributes(target, FileAttributes.Normal);
                        File.Delete(target);
                    }
                    File.Move(path, target);
                    count++;
                }
                catch (Exception ex)
                {
                    errors.Add(Path.GetFileName(path) + ": " + ex.Message);
                }
            }
            if (errors.Count > 0) throw new IOException(string.Join("\n", errors.ToArray()));
            return count;
        }

        // Renames every *.wav.off back to *.wav. Returns the number of files restored.
        public int UnmuteAll()
        {
            if (demo || Folder == null) return 0;
            int count = 0;
            var errors = new List<string>();
            foreach (string path in SafeGetFiles(Folder))
            {
                if (!path.EndsWith(".wav" + OffSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    string target = path.Substring(0, path.Length - OffSuffix.Length);
                    if (File.Exists(target))
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                    }
                    else
                    {
                        File.Move(path, target);
                    }
                    count++;
                }
                catch (Exception ex)
                {
                    errors.Add(Path.GetFileName(path) + ": " + ex.Message);
                }
            }
            if (errors.Count > 0) throw new IOException(string.Join("\n", errors.ToArray()));
            return count;
        }

        static string[] SafeGetFiles(string folder)
        {
            try { return Directory.GetFiles(folder); }
            catch { return new string[0]; }
        }

        static string SettingsFile
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    @"EpicChimeMuter\folder.txt");
            }
        }

        static string LoadSavedFolder()
        {
            try
            {
                if (File.Exists(SettingsFile)) return File.ReadAllText(SettingsFile).Trim();
            }
            catch { }
            return null;
        }

        static void SaveFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                File.WriteAllText(SettingsFile, folder);
            }
            catch { }
        }
    }
}
