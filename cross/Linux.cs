using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // The Linux and Mac hands: what she can do to the desktop with the mouse, the keyboard and the tools around it.
    internal static class Linux
    {
        private const string Clipboard = "xclip";
        private const string Recorder = "ffmpeg";
        private static readonly object gate = new object();
        private static readonly List<Timer> timers = new List<Timer>();
        private static Process recorder;
        private static string recording;
        private static Timer powerLater;

        private static bool Debug { get { return Environment.GetEnvironmentVariable("CAPRI_DEBUG") == "1"; } }

        private static bool Run(string file, string[] args)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
                foreach (string arg in args) info.ArgumentList.Add(arg);
                using (Process process = Process.Start(info))
                {
                    if (process == null) return false;
                    process.WaitForExit(30000);
                    return process.ExitCode == 0;
                }
            }
            catch (Exception) { return false; }
        }

        private static string Output(string file, string[] args)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string arg in args) info.ArgumentList.Add(arg);
                using (Process process = Process.Start(info))
                {
                    if (process == null) return null;
                    string text = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(30000);
                    return text;
                }
            }
            catch (Exception) { return null; }
        }

        // The mouse and the keyboard.

        internal static bool Click(int x, int y, int button)
        {
            if (!Run("xdotool", new[] { "mousemove", x.ToString(), y.ToString() })) return false;
            Thread.Sleep(120);
            return Run("xdotool", new[] { "click", button.ToString() });
        }

        internal static bool Double(int x, int y)
        {
            if (!Run("xdotool", new[] { "mousemove", x.ToString(), y.ToString() })) return false;
            Thread.Sleep(120);
            return Run("xdotool", new[] { "click", "--repeat", "2", "--delay", "120", "1" });
        }

        internal static bool Type(string text)
        {
            if (text == null) return false;
            return Run("xdotool", new[] { "type", "--delay", "18", "--", text });
        }

        internal static bool Key(string combo)
        {
            if (string.IsNullOrWhiteSpace(combo)) return false;
            return Run("xdotool", new[] { "key", "--clearmodifiers", combo.Trim() });
        }

        internal static bool Scroll(string way, int amount)
        {
            int button = way != null && way.StartsWith("up", StringComparison.OrdinalIgnoreCase) ? 4
                : way != null && way.StartsWith("down", StringComparison.OrdinalIgnoreCase) ? 5
                : way != null && way.StartsWith("left", StringComparison.OrdinalIgnoreCase) ? 6
                : way != null && way.StartsWith("right", StringComparison.OrdinalIgnoreCase) ? 7 : 5;
            int times = Math.Max(1, Math.Min(20, amount));
            string[] args = new string[4 + times];
            args[0] = "click";
            args[1] = "--repeat";
            args[2] = times.ToString();
            args[3] = "--delay";
            args[4] = button.ToString();
            return Run("xdotool", args);
        }

        // The clipboard.

        internal static string Read()
        {
            string text = Output(Clipboard, new[] { "-selection", "clipboard", "-o" });
            return text == null ? null : text.TrimEnd('\n', '\r');
        }

        internal static bool Write(string text)
        {
            if (text == null) return false;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(Clipboard) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
                info.ArgumentList.Add("-selection");
                info.ArgumentList.Add("clipboard");
                info.ArgumentList.Add("-i");
                using (Process process = Process.Start(info))
                {
                    if (process == null) return false;
                    process.StandardInput.Write(text);
                    process.StandardInput.Close();
                    process.WaitForExit(10000);
                    return process.ExitCode == 0;
                }
            }
            catch (Exception) { return false; }
        }

        internal static string Clip(string action, string text)
        {
            if (action == "write")
            {
                if (string.IsNullOrWhiteSpace(text)) return "What should I copy, choom?";
                return Write(text) ? "Copied it: " + (text.Length > 40 ? text.Substring(0, 40) + "..." : text) : "Couldn't copy that.";
            }
            if (action != "read") return "I can read the clipboard or copy something to it.";
            string copied = Read();
            if (copied == null) return "I couldn't read the clipboard.";
            if (copied.Length == 0) return "Your clipboard is empty.";
            return "Your clipboard has: " + (copied.Length > 300 ? copied.Substring(0, 300) + "..." : copied);
        }

        // Her shortcuts.

        internal static string Shortcut(string name)
        {
            if (name == "discord_mute") return Discord("ctrl+shift+m", "Toggled their mic mute in Discord (Ctrl + Shift + M).");
            if (name == "discord_deafen") return Discord("ctrl+shift+d", "Toggled deafen in Discord (Ctrl + Shift + D).");
            if (name == "screenshot")
            {
                string path = Path.Combine(Pictures(), "capri-screenshot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                if (Run("xfce4-screenshooter", new[] { "-f", "-s", path }) && File.Exists(path)) return "Took a screenshot: " + path;
                if (Run("import", new[] { "-window", "root", path }) && File.Exists(path)) return "Took a screenshot: " + path;
                if (Run("scrot", new[] { path }) && File.Exists(path)) return "Took a screenshot: " + path;
                return "There's no screenshot tool on this PC.";
            }
            if (name == "show_desktop") return Run("xdotool", new[] { "key", "--clearmodifiers", "super+d" }) ? "Showed the desktop." : "Couldn't show the desktop.";
            if (name == "task_manager")
            {
                if (Run("xfce4-appfinder", new string[0])) return "Opened the task manager.";
                if (Run("xterm", new[] { "-e", "htop" })) return "Opened the task manager.";
                return "There's no task manager on this PC.";
            }
            if (name == "clipboard")
            {
                string copied = Read();
                if (copied == null) return "Couldn't read the clipboard.";
                if (copied.Length == 0) return "Your clipboard is empty.";
                if (Run("notify-send", new[] { "Clipboard", copied.Length > 300 ? copied.Substring(0, 300) : copied })) return "Your clipboard has: " + (copied.Length > 120 ? copied.Substring(0, 120) + "..." : copied);
                return "Your clipboard has: " + (copied.Length > 120 ? copied.Substring(0, 120) + "..." : copied);
            }
            if (name == "record") return Record();
            if (name == "clip_that") return ClipThat();
            if (name == "game_bar") return "There's no Game Bar on Linux, choom. I can take a screenshot or record your screen instead.";
            if (name == "emoji") return "There's no emoji picker on this PC yet, choom.";
            return "There's no shortcut called " + name + ".";
        }

        private static string Pictures()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string folder = Path.Combine(home, "Imágenes");
            if (!Directory.Exists(folder)) folder = Path.Combine(home, "Pictures");
            if (!Directory.Exists(folder)) folder = home;
            return folder;
        }

        private static string Discord(string combo, string message)
        {
            OpenWindow discord = null;
            foreach (OpenWindow window in Hands.Windows())
                if (string.Equals(window.Program, "Discord", StringComparison.OrdinalIgnoreCase) || string.Equals(window.Program, "DiscordPTB", StringComparison.OrdinalIgnoreCase) || string.Equals(window.Program, "DiscordCanary", StringComparison.OrdinalIgnoreCase))
                {
                    discord = window;
                    break;
                }
            if (discord == null)
            {
                string opened = Hands.Do("open_app", new ToolArguments { Name = "Discord" });
                if (!opened.StartsWith("Opened", StringComparison.Ordinal)) return "Discord isn't open, so I couldn't press its shortcut. " + opened;
                for (int i = 0; i < 20 && discord == null; i++)
                {
                    Thread.Sleep(250);
                    foreach (OpenWindow window in Hands.Windows())
                        if (string.Equals(window.Program, "Discord", StringComparison.OrdinalIgnoreCase)) { discord = window; break; }
                }
                if (discord == null) return "Opened Discord, but its window didn't show up in time. Ask again in a sec.";
            }
            if (!Hands.Steer(discord, "switch_to")) return "Couldn't bring Discord to the front to press its shortcut.";
            Thread.Sleep(250);
            Key(combo);
            Thread.Sleep(150);
            return message;
        }

        // Her recorder, in the spirit of the Game Bar.

        private static string Folder()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string folder = Path.Combine(home, ".cache", "capri");
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string Record()
        {
            lock (gate)
            {
                if (recorder != null && !recorder.HasExited)
                {
                    try { recorder.Kill(); recorder.WaitForExit(5000); } catch (Exception) { }
                    recorder = null;
                    return "Stopped recording. Say clip that to keep the last 30 seconds.";
                }
                string path = Path.Combine(Folder(), "recording.mp4");
                try
                {
                    ProcessStartInfo info = new ProcessStartInfo(Recorder) { UseShellExecute = false, CreateNoWindow = true };
                    info.ArgumentList.Add("-f");
                    info.ArgumentList.Add("x11grab");
                    info.ArgumentList.Add("-i");
                    info.ArgumentList.Add(Environment.GetEnvironmentVariable("DISPLAY") ?? ":0");
                    info.ArgumentList.Add("-framerate");
                    info.ArgumentList.Add("30");
                    info.ArgumentList.Add("-codec:v");
                    info.ArgumentList.Add("libx264");
                    info.ArgumentList.Add("-preset");
                    info.ArgumentList.Add("ultrafast");
                    info.ArgumentList.Add("-crf");
                    info.ArgumentList.Add("28");
                    info.ArgumentList.Add("-y");
                    info.ArgumentList.Add(path);
                    recorder = Process.Start(info);
                    recording = path;
                    if (recorder == null) return "Couldn't start recording.";
                    return "Recording your screen. Say record again to stop it, or clip that to keep the last 30 seconds.";
                }
                catch (Exception) { return "Couldn't start recording."; }
            }
        }

        private static string ClipThat()
        {
            string source = recording;
            if (source == null || !File.Exists(source)) return "I haven't recorded anything yet.";
            FileInfo info = new FileInfo(source);
            if (info.Length < 1000) return "I haven't recorded enough to clip yet.";
            string videos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Vídeos");
            if (!Directory.Exists(videos)) videos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
            Directory.CreateDirectory(videos);
            string clip = Path.Combine(videos, "capri-clip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".mp4");
            if (!Run(Recorder, new[] { "-sseof", "-30", "-i", source, "-c", "copy", "-y", clip })) return "Couldn't clip that.";
            return "Saved the last 30 seconds to " + clip + ".";
        }

        // Her power.

        internal static bool Power(string action, int seconds)
        {
            lock (gate)
            {
                if (action == "cancel")
                {
                    bool stopped = false;
                    if (powerLater != null)
                    {
                        stopped = true;
                        powerLater.Dispose();
                        powerLater = null;
                    }
                    return stopped;
                }
                if (seconds <= 0) return Act(action);
                Timer timer = null;
                timer = new Timer(delegate
                {
                    lock (gate) { if (powerLater != timer) return; powerLater = null; }
                    Act(action);
                }, null, seconds * 1000, Timeout.Infinite);
                if (powerLater != null) powerLater.Dispose();
                powerLater = timer;
                return true;
            }
        }

        private static bool Act(string action)
        {
            if (action == "shut_down") return Run("systemctl", new[] { "poweroff" });
            if (action == "restart") return Run("systemctl", new[] { "reboot" });
            if (action == "sleep") return Run("systemctl", new[] { "suspend" });
            if (action == "lock") return Run("loginctl", new[] { "lock-session" });
            if (action == "sign_out") return Run("xfce4-session-logout", new[] { "--logout" });
            return false;
        }

        // Her reminders.

        internal static string Remind(string text, string minutes, string app)
        {
            if (string.IsNullOrWhiteSpace(text)) return "What should I remind you about, choom?";
            string body = text.Trim();
            if (!string.IsNullOrWhiteSpace(app)) body = app.Trim() + ": " + body;
            int wait = 0;
            if (!string.IsNullOrWhiteSpace(minutes)) int.TryParse(minutes.Trim(), out wait);
            if (wait <= 0)
            {
                if (!Run("notify-send", new[] { "Capri", body })) return "I couldn't pop up a notification on this PC.";
                return "Told you: " + body;
            }
            wait = Math.Min(wait, 1440);
            Timer timer = new Timer(delegate { Run("notify-send", new[] { "Capri", body }); }, null, wait * 60000, Timeout.Infinite);
            lock (gate) { timers.Add(timer); }
            return "I'll remind you in " + wait + (wait == 1 ? " minute" : " minutes") + ": " + body;
        }

        // Her hands, looking at the screen.

        private const string Driver = "You are Capri's hands on a Linux desktop.";
        private const string Pilot = "You are Capri, a desktop pet who does jobs on the user's computer with the mouse and keyboard. You plan one small step at a time. You never explain, you only give the next step.";

        private static string Plan(string task, List<string> done)
        {
            StringBuilder brief = new StringBuilder();
            brief.Append("Their job: ").Append(task).Append('\n');
            if (done.Count > 0) brief.Append("So far: ").Append(string.Join(", ", done.ToArray())).Append('\n');
            brief.Append("Reply with ONE line, the next step, starting with one of these words:\n");
            brief.Append("LOOK   to look at the screen first\n");
            brief.Append("CLICK what to click on\n");
            brief.Append("TYPE the text to type\n");
            brief.Append("KEY the key to press, like Return or ctrl+a\n");
            brief.Append("SCROLL up, down, left or right\n");
            brief.Append("WAIT 2   seconds to wait\n");
            brief.Append("SAY \"the answer to say\"\n");
            brief.Append("DONE \"what you did, in a few words\"\n");
            brief.Append("Nothing else. Do not repeat these instructions.");
            return brief.ToString();
        }

        private static int Screen()
        {
            try { return int.Parse(Output("xdotool", new[] { "getdisplaygeometry" }).Split(' ')[0]); }
            catch (Exception) { return 1366; }
        }

        private static int Tall()
        {
            try { return int.Parse(Output("xdotool", new[] { "getdisplaygeometry" }).Split(' ')[1]); }
            catch (Exception) { return 768; }
        }

        internal static bool Sees(string model)
        {
            if (string.IsNullOrEmpty(model)) return false;
            string name = model.ToLowerInvariant();
            return name.Contains("vision") || name.Contains("-vl-") || name.Contains("-vl/") || name.Contains("vl-") || name.Contains("multimodal");
        }

        internal static string Work(string task, string asked, Brain brain)
        {
            if (brain == null) return "I don't have my hands on this PC yet.";
            return Run(task, asked, brain);
        }

        private static string Run(string task, string asked, Brain brain)
        {
            List<string> done = new List<string>();
            string last = null;
            int stuck = 0;
            int looks = 0;
            for (int step = 0; step < 12; step++)
            {
                string plan;
                try { plan = brain.Say(Pilot, Plan(task, done), 120); }
                catch (BrainException error) { return error.Message; }
                catch (WebException error) { return "My brain took too long: " + error.Message; }
                catch (Exception error) { Console.Error.WriteLine("Capri hands: " + error); return "My hands got stuck: " + error.Message; }
                plan = Clean(plan);
                if (Debug) Console.Error.WriteLine("plan: " + plan);
                Match found = Regex.Match(plan, @"(?im)(?:^|\n|\r)\s*(?:[-*]\s*)?(?:\[|\*\*|`)?\s*(LOOK|CLICK|TYPE|KEY|SCROLL|WAIT|SAY|DONE)\b[:\s]*(.*)$");
                if (!found.Success)
                {
                    if (done.Count == 0) return "I couldn't work out what to do.";
                    Thread.Sleep(500);
                    continue;
                }
                string verb = found.Groups[1].Value.ToUpperInvariant();
                string rest = found.Groups[2].Value.Trim().Trim('`', '*');
                if (verb == "DONE" || verb == "SAY")
                {
                    string spoken = Quoted(rest);
                    if (spoken == null) spoken = rest.Trim().Trim('"').Trim();
                    if (spoken.Length == 0 || Regex.IsMatch(spoken, @"^(the answer to say|what you did|your answer|text)\b", RegexOptions.IgnoreCase)) continue;
                    return spoken;
                }
                if (verb == "LOOK")
                {
                    if (looks >= 3) return "I looked at the screen three times and couldn't make out what to do.";
                    looks++;
                    byte[] shot = X11.Shot();
                    if (shot == null) return "I couldn't see your screen.";
                    if (Debug) System.IO.File.WriteAllBytes("/tmp/capri-shot.jpg", shot);
                    string seen;
                    try { seen = Eyes(brain, shot, "You are Capri's eyes on a Linux desktop.", "Describe what is on this screen in one short sentence.", 120); }
                    catch (BrainException error) { return error.Message; }
                    catch (WebException error) { return "My eyes took too long: " + error.Message; }
                    seen = Clean(seen);
                    if (Debug) Console.Error.WriteLine("seen: " + seen);
                    if (seen == null || seen.Length == 0) continue;
                    done.Add("you saw: " + seen);
                    continue;
                }
                string action = verb + " " + rest;
                if (action == last) { stuck++; if (stuck >= 2) return "I kept trying the same thing and nothing changed, so I stopped."; }
                else { stuck = 0; last = action; }
                if (verb == "WAIT")
                {
                    int seconds = 1;
                    int.TryParse(rest.Split(' ')[0], out seconds);
                    Thread.Sleep(Math.Max(1, Math.Min(10, seconds)) * 1000);
                    continue;
                }
                if (verb == "CLICK")
                {
                    looks = 0;
                    string target = rest.Trim().Trim('"');
                    if (target.Length == 0) continue;
                    int[] at = Where(brain, target);
                    if (at == null) { done.Add("couldn't find " + target); continue; }
                    Click(at[0], at[1], 1);
                    done.Add("clicked " + target);
                    Thread.Sleep(800);
                    continue;
                }
                if (verb == "TYPE")
                {
                    looks = 0;
                    string text = rest.Trim().Trim('"');
                    if (text.Length == 0) continue;
                    if (!Type(text)) return "Couldn't type that.";
                    done.Add("typed " + (text.Length > 24 ? text.Substring(0, 24) + "..." : text));
                    Thread.Sleep(500);
                    continue;
                }
                if (verb == "KEY")
                {
                    string key = rest.Trim().Trim('"');
                    if (key.Length == 0) continue;
                    if (!Key(key)) return "Couldn't press that key.";
                    done.Add("pressed " + key);
                    Thread.Sleep(500);
                    continue;
                }
                if (verb == "SCROLL")
                {
                    Scroll(rest, 3);
                    done.Add("scrolled " + rest);
                    Thread.Sleep(600);
                    continue;
                }
            }
            return done.Count == 0 ? "I couldn't get that done." : "I got this far: " + string.Join(", ", done.ToArray()) + ". It didn't finish.";
        }

        // Her eyes, with a second try when the cloud is slow. She looks with a model that can see.
        internal static string Eyes(Brain brain, byte[] shot, string system, string instruction, int limit)
        {
            Cloud remote = brain.Remote;
            string model = remote != null ? remote.Model : brain.Model;
            bool swapped = false;
            if (remote != null && !Sees(model))
            {
                remote.Model = Pet.SeeingCloud;
                brain.Model = Pet.SeeingCloud;
                swapped = true;
            }
            try
            {
                for (int try_ = 0; try_ < 2; try_++)
                {
                    try { return brain.See(shot, system, instruction, limit); }
                    catch (WebException error) { if (try_ > 0) throw; }
                    catch (BrainException error) { if (try_ > 0 || !Regex.IsMatch(error.Message, "too long", RegexOptions.IgnoreCase)) throw; }
                }
                return null;
            }
            finally
            {
                if (swapped)
                {
                    remote.Model = model;
                    brain.Model = model;
                }
            }
        }

        // Where on the screen is what they asked for. A window with that name beats guessing pixels.
        private static int[] Where(Brain brain, string target)
        {
            int[] window = Window(target);
            if (window != null) return window;
            byte[] shot = X11.Shot();
            if (shot == null) return null;
            string said;
            try { said = Eyes(brain, shot, "You are Capri's eyes on a Linux desktop.", "The screen is " + Screen() + " by " + Tall() + " pixels. Give the position of " + target + " as two numbers between 0 and 1: the first is how far from the left, the second how far from the top. Reply with only the two numbers.", 100); }
            catch (BrainException error) { Console.Error.WriteLine("Capri eyes: " + error.Message); return null; }
            catch (WebException error) { Console.Error.WriteLine("Capri eyes: " + error.Message); return null; }
            said = Clean(said);
            Match at = Regex.Match(said, @"(0?\.\d+|\d+)\D+(0?\.\d+|\d+)");
            if (!at.Success) return null;
            double left = double.Parse(at.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            double top = double.Parse(at.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (left <= 1 && top <= 1) { left *= Screen(); top *= Tall(); }
            return new[] { (int)Math.Round(left), (int)Math.Round(top) };
        }

        // The window with that name, and where its title bar is.
        private static int[] Window(string target)
        {
            string text = Output("xdotool", new[] { "search", "--name", target });
            if (string.IsNullOrWhiteSpace(text)) return null;
            string first = text.Trim().Split('\n')[0].Trim();
            long id;
            if (!long.TryParse(first, out id)) return null;
            string place = Output("xdotool", new[] { "getwindowgeometry", "--shell", first });
            if (string.IsNullOrWhiteSpace(place)) return null;
            Match x = Regex.Match(place, @"X=(-?\d+)");
            Match y = Regex.Match(place, @"Y=(-?\d+)");
            Match w = Regex.Match(place, @"WIDTH=(\d+)");
            if (!x.Success || !y.Success || !w.Success) return null;
            return new[] { int.Parse(x.Groups[1].Value) + int.Parse(w.Groups[1].Value) / 2, int.Parse(y.Groups[1].Value) + 12 };
        }

        private static string Quoted(string text)
        {
            Match quoted = Regex.Match(text, "\"([^\"]+)\"");
            return quoted.Success ? quoted.Groups[1].Value.Trim() : null;
        }

        private static string Clean(string said)
        {
            if (said == null) return "";
            said = said.Trim();
            if (said.StartsWith("```", StringComparison.Ordinal))
            {
                int first = said.IndexOf('\n');
                int last = said.LastIndexOf("```", StringComparison.Ordinal);
                if (first > 0 && last > first) said = said.Substring(first + 1, last - first - 1);
            }
            return said.Trim();
        }
    }
}
