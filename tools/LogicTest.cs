// Exercises ChimeManager against temporary copies of chime files. Never touches the real Epic folder
// or the real settings file.
using System;
using System.IO;
using System.Linq;

namespace EpicChimeMuter
{
    static class LogicTest
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
            if (!ok) failures++;
        }

        static void Touch(string dir, string name, string content)
        {
            File.WriteAllText(Path.Combine(dir, name), content);
        }

        static int Main(string[] args)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "ecm-test-" + Guid.NewGuid().ToString("N"));
            string chimes = Path.Combine(tmp, @"Epic Games\Launcher\Portal\Extras\SocialChimes");
            string settings = Path.Combine(tmp, "settings.ini");
            Directory.CreateDirectory(chimes);
            try
            {
                string[] names = { "PartyUserJoined.wav", "PartyUserLeft.wav", "PushToTalkActivated.wav" };
                foreach (string n in names) Touch(chimes, n, "RIFF");
                Touch(chimes, "readme.txt", "not a chime");

                var m = new ChimeManager(chimes, settings);
                m.Scan();
                Check(m.State == ChimeState.AllOn && m.Files.Count == 3, "initial scan: 3 files, all on");
                Check(m.Files[0].FriendlyName == "Party User Joined", "friendly name");

                // --- mute all / unmute all
                Check(m.MuteAll() == 3, "mute all renames 3 files");
                Check(m.State == ChimeState.AllMuted, "state is AllMuted");
                Check(File.Exists(Path.Combine(chimes, "PartyUserJoined.wav.off")), ".off file exists");
                Check(File.Exists(Path.Combine(chimes, "readme.txt")), "non-wav file untouched");
                Check(m.UnmuteAll() == 3 && m.State == ChimeState.AllOn, "unmute all restores 3 files");

                // --- individual chimes
                m.SetMuted(m.Files.First(f => f.Name == "PartyUserLeft.wav"), true);
                Check(m.State == ChimeState.Mixed && m.MutedCount == 1, "mute one chime -> Mixed, 1 muted");
                Check(File.Exists(Path.Combine(chimes, "PartyUserLeft.wav.off")) &&
                      File.Exists(Path.Combine(chimes, "PartyUserJoined.wav")), "only that file renamed");
                Check(m.Restored.Count == 0, "a chosen custom mix is not reported as restored");
                m.SetMuted(m.Files.First(f => f.Name == "PartyUserLeft.wav"), false);
                Check(m.State == ChimeState.AllOn, "unmute that chime -> all on");

                // --- choices survive a restart (settings file)
                m.SetMuted(m.Files.First(f => f.Name == "PushToTalkActivated.wav"), true);
                m.SetMuted(m.Files.First(f => f.Name == "PartyUserJoined.wav"), true);
                var m2 = new ChimeManager(chimes, settings);
                m2.Scan();
                Check(m2.MutedCount == 2 && m2.Restored.Count == 0, "reloaded manager sees 2 muted, none restored");

                // --- Epic update restores a muted chime (the .wav comes back next to the .off)
                Touch(chimes, "PushToTalkActivated.wav", "RIFF-new");
                m2.Scan();
                Check(m2.Restored.Count == 1 && m2.Restored[0].Name == "PushToTalkActivated.wav", "restored chime detected");
                Check(m2.ReapplyMuted() == 1 && m2.Restored.Count == 0, "re-mute fixes only the restored chime");
                Check(m2.MutedCount == 2 && !m2.Files.First(f => f.Name == "PartyUserLeft.wav").Muted,
                      "user's un-muted chime stays on");
                Check(File.ReadAllText(Path.Combine(chimes, "PushToTalkActivated.wav.off")) == "RIFF-new", "newest copy kept");

                // --- unmute when both copies exist: no crash, no duplicates
                Touch(chimes, "PartyUserJoined.wav", "RIFF-new");
                m2.UnmuteAll();
                Check(m2.State == ChimeState.AllOn && m2.Files.Count == 3, "all on, no duplicates");
                Check(Directory.GetFiles(chimes, "*.off").Length == 0, "no .off files left");

                // --- fallback: picking a parent folder searches inside it
                string moved = Path.Combine(tmp, @"Elsewhere\Deep\Nested\SocialChimes");
                Directory.CreateDirectory(moved);
                Touch(moved, "PartyUserJoined.wav", "RIFF");
                var m3 = new ChimeManager(null, Path.Combine(tmp, "settings3.ini"));
                Check(m3.ChooseFolder(Path.Combine(tmp, "Elsewhere")) && m3.Folder == moved, "choosing a parent folder finds SocialChimes inside it");
                Check(m3.IsCustomFolder && m3.FolderSource == "Custom folder", "picked folder is marked custom");
                var m4 = new ChimeManager(null, Path.Combine(tmp, "settings3.ini"));
                m4.Scan();
                Check(m4.Folder == moved, "picked folder is remembered after restart");
                Directory.CreateDirectory(Path.Combine(tmp, "Empty"));
                Check(!m3.ChooseFolder(Path.Combine(tmp, "Empty")) && m3.Folder == moved, "rejected pick keeps the old folder");

                Check(ChimeManager.SearchForChimes(Path.Combine(tmp, "Epic Games")) == chimes, "search finds SocialChimes inside the launcher folder");

                var missing = new ChimeManager(Path.Combine(tmp, "nope"), null);
                Check(missing.MuteAll() == 0, "missing folder does not crash");
            }
            finally
            {
                Directory.Delete(tmp, true);
            }
            Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " FAILURE(S)");
            return failures == 0 ? 0 : 1;
        }
    }
}
