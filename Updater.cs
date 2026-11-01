// Self-update for the single-exe WeekNumber tray app.
//
// How it works: GitHub Actions builds the exe with a real version (1.0.<run number>) and, for every push to main,
// publishes it with a .sha256 file next to it as the latest release of joback94/Calendar-Week (a public repo).
// The running app asks for the latest release a couple of minutes after starting, then twice a day, and when
// "Check for updates" is clicked. When it finds a newer one it downloads the exe beside itself, checks it against
// the SHA-256, and swaps the two files (a running exe can be renamed, not overwritten), then restarts into it.
// Files the app saves itself carry no "downloaded from the internet" mark.
//
// Builds made on your own PC are 0.0.0 and never update themselves.
// Written for the C# 5 compiler that ships with Windows (no $"", no ?., no => members).
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AppUpdate
{
    public enum UpdateResult { UpToDate, Installed, Failed, Disabled }

    public sealed class Updater
    {
        readonly string repo, asset, exe, fresh, old;
        System.Threading.Timer timer;   // held here so it is not garbage-collected
        int busy;

        // repo: the public repo with the releases, e.g. "joback94/Snapshot_Tool_Public".
        // asset: the exe's name in each release, e.g. "Snapshot.exe".
        public Updater(string repo, string asset)
        {
            this.repo = repo;
            this.asset = asset;
            exe = Application.ExecutablePath;
            fresh = exe + ".new";
            old = Path.ChangeExtension(exe, ".old.exe");
        }

        public static string CurrentVersion { get { return BuildInfo.Version; } }

        // The version installed beside the running exe, waiting for a restart; null if none.
        public string Pending { get; private set; }

        // Raised on a worker thread after every check: marshal to the UI thread before touching controls.
        // The string is the new version for Installed, the reason for Failed, null otherwise.
        public event Action<UpdateResult, string, bool> Checked;

        public bool Enabled { get { return !CurrentVersion.StartsWith("0."); } }

        // First check after two minutes (not to slow the start), then every twelve hours.
        public void Start()
        {
            ThreadPool.QueueUserWorkItem(delegate { RemoveOld(); });
            if (!Enabled) return;
            timer = new System.Threading.Timer(delegate { CheckNow(false); }, null,
                TimeSpan.FromMinutes(2), TimeSpan.FromHours(12));
        }

        // manual = the user asked (e.g. a "Check for updates" menu item): the app should say what happened.
        public void CheckNow(bool manual)
        {
            if (!Enabled) { Raise(UpdateResult.Disabled, "This copy was built locally and does not update itself.", manual); return; }
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Check(manual); }
                finally { Interlocked.Exchange(ref busy, 0); }
            });
        }

        void Check(bool manual)
        {
            string step = "Could not reach GitHub";
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string tag;
                using (WebClient wc = Client())
                {
                    string json = wc.DownloadString("https://api.github.com/repos/" + repo + "/releases/latest");
                    Match m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                    if (!m.Success) throw new InvalidDataException("no tag");
                    tag = m.Groups[1].Value;
                }
                System.Version latest = Parse(tag), current = Parse(CurrentVersion);
                string pending = Pending;
                if (latest == null || current == null || latest <= current ||
                    (pending != null && latest <= Parse(pending)))
                {
                    Raise(pending != null ? UpdateResult.Installed : UpdateResult.UpToDate, pending, manual);
                    return;
                }

                string url = "https://github.com/" + repo + "/releases/download/" + tag + "/" + asset;
                step = "Could not write next to " + Path.GetFileName(exe);
                File.WriteAllText(fresh, "");
                step = "Could not download the update";
                string expected;
                using (WebClient wc = Client())
                {
                    expected = wc.DownloadString(url + ".sha256").Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
                    wc.DownloadFile(url, fresh);
                }
                string actual;
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = File.OpenRead(fresh))
                    actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                step = "The download did not match its SHA-256";
                if (expected.Length != 64 || actual != expected) throw new InvalidDataException("SHA-256 mismatch");

                step = "Could not replace " + Path.GetFileName(exe);
                if (File.Exists(old)) File.Delete(old);   // fails while an older copy still runs: try again next time
                File.Move(exe, old);
                try { File.Move(fresh, exe); }
                catch { File.Move(old, exe); throw; }
                Pending = tag.TrimStart('v', 'V');
                Raise(UpdateResult.Installed, Pending, manual);
            }
            catch (Exception)
            {
                try { if (File.Exists(fresh)) File.Delete(fresh); } catch { }
                Raise(UpdateResult.Failed, step, manual);
            }
        }

        // Starts the new exe and returns; the caller then exits the usual way (saving what it must).
        // The new copy gets "--updated <pid>" first, so WaitForPrevious lets it wait for this one to finish.
        public void Relaunch(string args)
        {
            string a = "--updated " + Process.GetCurrentProcess().Id + (string.IsNullOrEmpty(args) ? "" : " " + args);
            Process.Start(new ProcessStartInfo(exe, a) { UseShellExecute = false });
        }

        // Call first thing in Main: drops "--updated <pid>" from the arguments and waits (up to 30 s) for that process
        // to exit, so the new copy can take the single-instance mutex.
        public static string[] WaitForPrevious(string[] args)
        {
            if (args == null || args.Length < 2 || args[0] != "--updated") return args ?? new string[0];
            int pid;
            if (int.TryParse(args[1], out pid))
                try { Process.GetProcessById(pid).WaitForExit(30000); } catch (ArgumentException) { }
            string[] rest = new string[args.Length - 2];
            Array.Copy(args, 2, rest, 0, rest.Length);
            return rest;
        }

        void RemoveOld()
        {
            // The previous version may still be closing: give it a while.
            for (int i = 0; i < 10 && File.Exists(old); i++)
            {
                try { File.Delete(old); return; }
                catch (Exception) { Thread.Sleep(3000); }
            }
        }

        void Raise(UpdateResult r, string detail, bool manual)
        {
            Action<UpdateResult, string, bool> h = Checked;
            if (h != null) h(r, detail, manual);
        }

        WebClient Client()
        {
            WebClient wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = Path.GetFileNameWithoutExtension(asset) + "/" + CurrentVersion;
            return wc;
        }

        static System.Version Parse(string s)
        {
            System.Version v;
            return s != null && System.Version.TryParse(s.TrimStart('v', 'V'), out v) ? v : null;
        }
    }
}
