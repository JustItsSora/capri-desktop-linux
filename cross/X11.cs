using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CapriDesktop
{
    internal static class X11
    {
        private const string Library = "libX11.so.6";
        private const int ClientMessage = 33;
        private const long Substructure = 1 | 2;
        private static readonly object gate = new object();
        private static readonly Dictionary<string, IntPtr> atoms = new Dictionary<string, IntPtr>();
        private static IntPtr display;
        private static IntPtr root;
        private static Handler handler;
        private static IntPtr previous;
        private static IntPtr ours;

        private delegate int Handler(IntPtr display, IntPtr error);

        [DllImport(Library)] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(Library)] private static extern int XCloseDisplay(IntPtr display);
        [DllImport(Library)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport(Library)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
        [DllImport(Library)] private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr offset, IntPtr length, bool delete, IntPtr type, out IntPtr actualType, out int actualFormat, out IntPtr items, out IntPtr after, out IntPtr data);
        [DllImport(Library)] private static extern int XFree(IntPtr data);
        [DllImport(Library)] private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, long eventMask, ref XEvent data);
        [DllImport(Library)] private static extern int XFlush(IntPtr display);
        [DllImport(Library)] private static extern int XRaiseWindow(IntPtr display, IntPtr window);
        [DllImport(Library)] private static extern IntPtr XSetErrorHandler(IntPtr handler);

        [StructLayout(LayoutKind.Sequential)]
        private struct XEvent
        {
            internal int Type;
            internal IntPtr Serial;
            internal int SendEvent;
            internal IntPtr Display;
            internal IntPtr Window;
            internal IntPtr MessageType;
            internal int Format;
            internal IntPtr Data0;
            internal IntPtr Data1;
            internal IntPtr Data2;
            internal IntPtr Data3;
            internal IntPtr Data4;
            private IntPtr Pad0;
            private IntPtr Pad1;
            private IntPtr Pad2;
            private IntPtr Pad3;
            private IntPtr Pad4;
            private IntPtr Pad5;
            private IntPtr Pad6;
            private IntPtr Pad7;
            private IntPtr Pad8;
            private IntPtr Pad9;
            private IntPtr Pad10;
            private IntPtr Pad11;
        }

        private static IntPtr Open()
        {
            lock (gate)
            {
                if (display != IntPtr.Zero) return display;
                IntPtr opened = XOpenDisplay(IntPtr.Zero);
                if (opened == IntPtr.Zero) return IntPtr.Zero;
                ours = opened;
                handler = delegate(IntPtr where, IntPtr error)
                {
                    if (where == ours || previous == IntPtr.Zero) return 0;
                    return Marshal.GetDelegateForFunctionPointer<Handler>(previous)(where, error);
                };
                previous = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(handler));
                root = XDefaultRootWindow(opened);
                display = opened;
                return display;
            }
        }

        private static IntPtr Atom(string name)
        {
            lock (gate)
            {
                IntPtr atom;
                if (!atoms.TryGetValue(name, out atom)) { atom = XInternAtom(display, name, false); atoms[name] = atom; }
                return atom;
            }
        }

        private static byte[] Bytes(IntPtr window, string name, int units)
        {
            IntPtr type;
            int format;
            IntPtr items;
            IntPtr after;
            IntPtr data;
            if (XGetWindowProperty(display, window, Atom(name), IntPtr.Zero, new IntPtr(units), false, IntPtr.Zero, out type, out format, out items, out after, out data) != 0 || data == IntPtr.Zero) return null;
            try
            {
                if (format != 8) return null;
                int count = (int)items.ToInt64();
                if (count <= 0) return null;
                byte[] bytes = new byte[count];
                Marshal.Copy(data, bytes, 0, count);
                return bytes;
            }
            finally { XFree(data); }
        }

        private static long[] Longs(IntPtr window, string name)
        {
            IntPtr type;
            int format;
            IntPtr items;
            IntPtr after;
            IntPtr data;
            if (XGetWindowProperty(display, window, Atom(name), IntPtr.Zero, new IntPtr(4096), false, IntPtr.Zero, out type, out format, out items, out after, out data) != 0 || data == IntPtr.Zero) return null;
            try
            {
                if (format != 32) return null;
                long[] values = new long[items.ToInt64()];
                for (int i = 0; i < values.Length; i++) values[i] = Marshal.ReadIntPtr(data, i * IntPtr.Size).ToInt64();
                return values;
            }
            finally { XFree(data); }
        }

        private static string Text(IntPtr window, string name)
        {
            byte[] bytes = Bytes(window, name, 1024);
            if (bytes == null) return null;
            int count = Array.IndexOf(bytes, (byte)0);
            if (count < 0) count = bytes.Length;
            if (count <= 0) return null;
            string text = Encoding.UTF8.GetString(bytes, 0, count);
            return text.Length == 0 ? null : text;
        }

        private static string[] Class(IntPtr window)
        {
            byte[] bytes = Bytes(window, "WM_CLASS", 64);
            if (bytes == null) return null;
            List<string> parts = new List<string>();
            int start = 0;
            for (int i = 0; i < bytes.Length; i++)
                if (bytes[i] == 0)
                {
                    if (i > start) parts.Add(Encoding.UTF8.GetString(bytes, start, i - start));
                    start = i + 1;
                }
            return parts.Count == 0 ? null : parts.ToArray();
        }

        internal static List<OpenWindow> List()
        {
            List<OpenWindow> found = new List<OpenWindow>();
            try
            {
                lock (gate)
                {
                    if (Open() == IntPtr.Zero) return found;
                    long[] clients = Longs(root, "_NET_CLIENT_LIST");
                    if (clients == null) clients = Longs(root, "_NET_CLIENT_LIST_STACKING");
                    if (clients == null) return found;
                    int self = Environment.ProcessId;
                    foreach (long id in clients)
                    {
                        IntPtr window = new IntPtr(id);
                        long[] pid = Longs(window, "_NET_WM_PID");
                        if (pid != null && pid.Length > 0 && pid[0] == self) continue;
                        string[] kind = Class(window);
                        string title = Text(window, "_NET_WM_NAME") ?? Text(window, "WM_NAME");
                        string program = null;
                        if (kind != null)
                        {
                            program = kind.Length > 1 ? kind[1] : kind[0];
                            if (program.Length == 0 && kind.Length > 1) program = kind[0];
                        }
                        if (program == null) program = title;
                        if (title == null) title = program;
                        if (program == null || title == null) continue;
                        if (program.Length == 0 && title.Length == 0) continue;
                        found.Add(new OpenWindow(window, title, program));
                    }
                }
            }
            catch (Exception) { }
            return found;
        }

        private static bool Tell(long id, string message, long first, long second)
        {
            try
            {
                lock (gate)
                {
                    if (Open() == IntPtr.Zero) return false;
                    XEvent sent = new XEvent();
                    sent.Type = ClientMessage;
                    sent.Display = display;
                    sent.Window = root;
                    sent.MessageType = Atom(message);
                    sent.Format = 32;
                    sent.Data0 = new IntPtr(first);
                    sent.Data1 = new IntPtr(second);
                    if (XSendEvent(display, root, false, Substructure, ref sent) == 0) return false;
                    XFlush(display);
                    return true;
                }
            }
            catch (Exception) { return false; }
        }

        internal static bool Activate(long id)
        {
            try
            {
                lock (gate)
                {
                    if (Open() == IntPtr.Zero) return false;
                    XRaiseWindow(display, new IntPtr(id));
                }
                return Tell(id, "_NET_ACTIVE_WINDOW", 2, 0);
            }
            catch (Exception) { return false; }
        }

        internal static bool Minimize(long id)
        {
            return Tell(id, "_NET_WM_STATE", 1, Atom("_NET_WM_STATE_HIDDEN").ToInt64());
        }

        internal static bool Close(long id)
        {
            return Tell(id, "_NET_CLOSE_WINDOW", 0, 0);
        }

        internal static bool CloseTab(long id)
        {
            try
            {
                if (!Activate(id)) return false;
                ProcessStartInfo info = new ProcessStartInfo("xdotool") { UseShellExecute = false, CreateNoWindow = true };
                info.ArgumentList.Add("key");
                info.ArgumentList.Add("--clearmodifiers");
                info.ArgumentList.Add("ctrl+w");
                using (Process process = Process.Start(info))
                {
                    if (process == null) return false;
                    process.WaitForExit(5000);
                    return process.ExitCode == 0;
                }
            }
            catch (Exception) { return false; }
        }

        internal static byte[] Shot()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("import") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                info.ArgumentList.Add("-window");
                info.ArgumentList.Add("root");
                info.ArgumentList.Add("-quality");
                info.ArgumentList.Add("80");
                info.ArgumentList.Add("jpeg:-");
                using (Process process = Process.Start(info))
                {
                    if (process == null) return null;
                    using (System.IO.MemoryStream buffer = new System.IO.MemoryStream())
                    {
                        process.StandardOutput.BaseStream.CopyTo(buffer);
                        process.WaitForExit(20000);
                        if (process.ExitCode != 0) return null;
                        byte[] bytes = buffer.ToArray();
                        return bytes.Length < 2000 ? null : bytes;
                    }
                }
            }
            catch (Exception) { return null; }
        }
    }
}
