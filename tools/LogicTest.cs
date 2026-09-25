// Exercises ChimeManager against a temporary copy of the chime files. Never touches the real Epic folder.
using System;
using System.IO;

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

        static int Main(string[] args)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "ecm-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string[] names = { "PartyUserJoined.wav", "PartyUserLeft.wav", "PushToTalkActivated.wav" };
                foreach (string n in names) File.WriteAllText(Path.Combine(tmp, n), "RIFF");
                File.WriteAllText(Path.Combine(tmp, "readme.txt"), "not a chime");

                var m = new ChimeManager(tmp);
                m.Scan();
                Check(m.State == ChimeState.AllOn && m.Files.Count == 3, "initial scan: 3 files, all on");
                Check(m.Files[0].FriendlyName == "Party User Joined", "friendly name");

                Check(m.MuteAll() == 3, "mute renames 3 files");
                m.Scan();
                Check(m.State == ChimeState.AllMuted, "state is AllMuted");
                Check(File.Exists(Path.Combine(tmp, "PartyUserJoined.wav.off")), ".off file exists");
                Check(File.Exists(Path.Combine(tmp, "readme.txt")), "non-wav file untouched");

                // Simulate an Epic update restoring one file while its .off copy still exists.
                File.WriteAllText(Path.Combine(tmp, "PartyUserLeft.wav"), "RIFF-new");
                m.Scan();
                Check(m.State == ChimeState.Mixed && m.MutedCount == 2, "restored file detected as Mixed");

                Check(m.MuteAll() == 1, "re-mute handles the restored file");
                m.Scan();
                Check(m.State == ChimeState.AllMuted, "all muted again");
                Check(File.ReadAllText(Path.Combine(tmp, "PartyUserLeft.wav.off")) == "RIFF-new", "newest copy kept");

                // Update restores a file, then user unmutes: no crash, no duplicates.
                File.WriteAllText(Path.Combine(tmp, "PushToTalkActivated.wav"), "RIFF-new");
                Check(m.UnmuteAll() == 3, "unmute processes all .off files");
                m.Scan();
                Check(m.State == ChimeState.AllOn && m.Files.Count == 3, "all on, no duplicates");
                Check(Directory.GetFiles(tmp, "*.off").Length == 0, "no .off files left");

                var missing = new ChimeManager(Path.Combine(tmp, "nope"));
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
