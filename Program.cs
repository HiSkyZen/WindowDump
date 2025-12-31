using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

class Program
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpWindowText, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    // GetWindowLongPtr (x86/x64 compatible)
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    private const uint GW_CHILD = 5;
    private const uint GW_HWNDNEXT = 2;

    private readonly struct SizeWH
    {
        public readonly int W;
        public readonly int H;
        public SizeWH(int w, int h) { W = w; H = h; }
        public override string ToString() => $"{W}x{H}";
    }

    private sealed class Options
    {
        // target selectors (optional; if omitted, first positional arg is used)
        public uint? TargetPid { get; set; } = null;
        public string TargetName { get; set; } = null;

        // output
        public bool AsciiTree { get; set; } = false;
        public int MaxDepth { get; set; } = -1;  // -1 = unlimited
        public bool ChildPidOnly { get; set; } = false;

        // visibility / rect filters
        public bool VisibleOnly { get; set; } = false;
        public SizeWH? MinRect { get; set; } = null;
        public SizeWH? MaxRect { get; set; } = null;

        // show extras only when requested
        public bool ShowStyle { get; set; } = false;
        public bool ShowRect { get; set; } = false;

        // text filters
        public string ClassContains { get; set; } = null;
        public string TitleContains { get; set; } = null;
        public Regex ClassRegex { get; set; } = null;
        public Regex TitleRegex { get; set; } = null;

        // json
        public bool Json { get; set; } = false;
        public string JsonFile { get; set; } = null;
    }

    private sealed class WindowNode
    {
        public string hwnd;
        public long hwndDec;
        public uint pid;
        public uint tid;
        public bool visible;
        public string className;
        public string title;

        // shown only if enabled (will be null otherwise)
        public string style;
        public string exStyle;

        public RectInfo rect;
        public ClientInfo client;

        public bool match;
        public List<WindowNode> children = new List<WindowNode>();

        // internal values used for filtering (not emitted in JSON)
        public int? _rectW;
        public int? _rectH;
        public bool _rectKnown;
    }

    private sealed class RectInfo
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
        public int width;
        public int height;
    }

    private sealed class ClientInfo
    {
        public int width;
        public int height;
    }

    private sealed class ProcInfo
    {
        public string name;
        public uint pid;
    }

    private sealed class JsonOptions
    {
        public uint? TargetPid;
        public string TargetName;
        public bool AsciiTree;
        public int MaxDepth;
        public bool ChildPidOnly;
        public bool VisibleOnly;
        public string MinRect;
        public string MaxRect;
        public bool ShowStyle;
        public bool ShowRect;
        public string ClassContains;
        public string TitleContains;
        public string ClassRegex;
        public string TitleRegex;
        public bool Json;
        public string JsonFile;
    }

    private sealed class JsonRoot
    {
        public string generatedAt;
        public List<ProcInfo> processes = new List<ProcInfo>();
        public List<WindowNode> roots = new List<WindowNode>();
        public JsonOptions options;
    }

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        string exeName = Path.GetFileName(Environment.GetCommandLineArgs()[0]);

        string positionalTarget;
        Options opt;
        if (!TryParseArgs(args, exeName, out positionalTarget, out opt))
            return 1;

        // Determine target (PID or Name)
        uint? targetPid = opt.TargetPid;
        string targetName = opt.TargetName;

        if (targetPid == null && targetName == null)
        {
            if (string.IsNullOrWhiteSpace(positionalTarget))
            {
                PrintUsage(exeName);
                return 1;
            }

            uint pidFromPos;
            if (TryParsePid(positionalTarget, out pidFromPos))
                targetPid = pidFromPos;
            else
                targetName = positionalTarget;
        }

        var targetPids = new HashSet<uint>();
        var jsonRoot = new JsonRoot
        {
            generatedAt = DateTimeOffset.Now.ToString("o"),
            options = new JsonOptions
            {
                TargetPid = targetPid,
                TargetName = targetName,
                AsciiTree = opt.AsciiTree,
                MaxDepth = opt.MaxDepth,
                ChildPidOnly = opt.ChildPidOnly,
                VisibleOnly = opt.VisibleOnly,
                MinRect = opt.MinRect.HasValue ? opt.MinRect.Value.ToString() : null,
                MaxRect = opt.MaxRect.HasValue ? opt.MaxRect.Value.ToString() : null,
                ShowStyle = opt.ShowStyle,
                ShowRect = opt.ShowRect,
                ClassContains = opt.ClassContains,
                TitleContains = opt.TitleContains,
                ClassRegex = opt.ClassRegex != null ? opt.ClassRegex.ToString() : null,
                TitleRegex = opt.TitleRegex != null ? opt.TitleRegex.ToString() : null,
                Json = opt.Json,
                JsonFile = opt.JsonFile
            }
        };

        if (targetPid != null)
        {
            try
            {
                var p = Process.GetProcessById((int)targetPid.Value);
                targetPids.Add(targetPid.Value);
                jsonRoot.processes.Add(new ProcInfo { name = p.ProcessName + ".exe", pid = targetPid.Value });
            }
            catch
            {
                Console.WriteLine($"Could not find process with PID {targetPid.Value}.");
                return 1;
            }
        }
        else
        {
            string procName = targetName ?? "";
            if (procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                procName = procName.Substring(0, procName.Length - 4);

            Process[] processes = Process.GetProcessesByName(procName);
            Array.Sort(processes, (a, b) => a.Id.CompareTo(b.Id));
            if (processes.Length == 0)
            {
                Console.WriteLine($"Could not find process '{procName}'.");
                return 1;
            }

            foreach (var p in processes)
            {
                uint pid = (uint)p.Id;
                targetPids.Add(pid);
                jsonRoot.processes.Add(new ProcInfo { name = p.ProcessName + ".exe", pid = pid });
            }
        }

        int matchedTop = 0;
        var rootsForPrint = new List<WindowNode>();

        EnumWindows((hWnd, lParam) =>
        {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (!targetPids.Contains(pid))
                return true;

            matchedTop++;

            var visited = new HashSet<IntPtr>();
            var rootNode = BuildTree(
                hWnd,
                depth: 0,
                visited: visited,
                targetPids: targetPids,
                opt: opt
            );

            // apply filter + prune
            rootNode = ApplyFilterAndPrune(rootNode, opt);

            if (rootNode != null)
            {
                jsonRoot.roots.Add(rootNode);
                rootsForPrint.Add(rootNode);
            }

            return true;
        }, IntPtr.Zero);

        // Sort process list and roots by PID (and HWND as tie-breaker)
        jsonRoot.processes.Sort((x, y) => x.pid.CompareTo(y.pid));
        jsonRoot.roots.Sort((a, b) =>
        {
            int c = a.pid.CompareTo(b.pid);
            if (c != 0) return c;
            return a.hwndDec.CompareTo(b.hwndDec);
        });
        rootsForPrint.Sort((a, b) =>
        {
            int c = a.pid.CompareTo(b.pid);
            if (c != 0) return c;
            return a.hwndDec.CompareTo(b.hwndDec);
        });

        if (opt.Json)
        {
            WriteJson(jsonRoot, opt);
            return 0;
        }

        if (matchedTop == 0)
        {
            Console.WriteLine("\n(Note) No top-level window found for the target PID(s).");
            Console.WriteLine("\n==== Done ====");
            return 0;
        }

        if (rootsForPrint.Count == 0)
        {
            Console.WriteLine("\n(Filter result) No windows matched the given filters.");
            Console.WriteLine("\n==== Done ====");
            return 0;
        }

        foreach (var pi in jsonRoot.processes)
            Console.WriteLine($"Found Process: {pi.name} (PID: {pi.pid})");

        foreach (var r in rootsForPrint)
        {
            Console.WriteLine();
            Console.WriteLine("=== Window Tree (Top-level) ===");
            PrintTree(r, opt);
        }

        Console.WriteLine("\n==== Done ====");
        return 0;
    }

    // ----------------------------
    // CLI parsing
    // ----------------------------

    static bool TryParseArgs(string[] args, string exeName, out string positionalTarget, out Options opt)
    {
        positionalTarget = null;
        opt = new Options();

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];

            if (string.IsNullOrWhiteSpace(a))
                continue;

            if (!a.StartsWith("-"))
            {
                if (positionalTarget != null)
                {
                    Console.WriteLine($"Only one target argument (process name or PID) is allowed: '{positionalTarget}', '{a}'");
                    PrintUsage(exeName);
                    return false;
                }
                positionalTarget = a;
                continue;
            }

            try
            {
                if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    if (!ParseLongOption(a, exeName, opt))
                        return false;
                }
                else
                {
                    if (!ParseShortOption(a, args, ref i, exeName, opt))
                        return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                PrintUsage(exeName);
                return false;
            }
        }

        if (positionalTarget == null && opt.TargetPid == null && opt.TargetName == null)
        {
            PrintUsage(exeName);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(opt.JsonFile) && !opt.Json)
        {
            Console.WriteLine("Option -o/--jsonFile can only be used together with -j/--json.");
            PrintUsage(exeName);
            return false;
        }

        return true;
    }

    static bool ParseLongOption(string a, string exeName, Options opt)
    {
        if (Eq(a, "--ascii")) opt.AsciiTree = true;
        else if (Eq(a, "--childPidOnly")) opt.ChildPidOnly = true;
        else if (Eq(a, "--json")) opt.Json = true;
        else if (Eq(a, "--visibleOnly")) opt.VisibleOnly = true;
        else if (Eq(a, "--style")) opt.ShowStyle = true;
        else if (Eq(a, "--rect")) opt.ShowRect = true;

        else if (Starts(a, "--maxDepth=")) opt.MaxDepth = ParseNonNegInt(GetEqValue(a), "--maxDepth");
        else if (Starts(a, "--jsonFile=")) opt.JsonFile = GetEqValue(a);
        else if (Starts(a, "--classContains=")) opt.ClassContains = GetEqValue(a);
        else if (Starts(a, "--titleContains=")) opt.TitleContains = GetEqValue(a);
        else if (Starts(a, "--classRegex=")) opt.ClassRegex = new Regex(GetEqValue(a), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        else if (Starts(a, "--titleRegex=")) opt.TitleRegex = new Regex(GetEqValue(a), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        else if (Starts(a, "--minRect=")) opt.MinRect = ParseRectWH(GetEqValue(a), "--minRect");
        else if (Starts(a, "--maxRect=")) opt.MaxRect = ParseRectWH(GetEqValue(a), "--maxRect");

        else if (Starts(a, "--pid="))
        {
            uint pid;
            if (!TryParsePid(GetEqValue(a), out pid))
                throw new ArgumentException($"Invalid --pid value: {a}");
            opt.TargetPid = pid;
        }
        else if (Starts(a, "--name="))
        {
            opt.TargetName = GetEqValue(a);
        }
        else
        {
            Console.WriteLine($"Unknown option: {a}");
            PrintUsage(exeName);
            return false;
        }

        return true;
    }

    static bool ParseShortOption(string a, string[] args, ref int i, string exeName, Options opt)
    {
        if (a.Length == 2)
        {
            char c = a[1];
            switch (c)
            {
                case 'a': opt.AsciiTree = true; return true;
                case 'P': opt.ChildPidOnly = true; return true;
                case 'j': opt.Json = true; return true;
                case 'v': opt.VisibleOnly = true; return true;
                case 's': opt.ShowStyle = true; return true;
                case 'r': opt.ShowRect = true; return true;
                case 'o': opt.JsonFile = RequireValue(a, args, ref i); return true;
                case 'd': opt.MaxDepth = ParseNonNegInt(RequireValue(a, args, ref i), "-d"); return true;
                case 'c': opt.ClassContains = RequireValue(a, args, ref i); return true;
                case 't': opt.TitleContains = RequireValue(a, args, ref i); return true;
                case 'C': opt.ClassRegex = new Regex(RequireValue(a, args, ref i), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); return true;
                case 'T': opt.TitleRegex = new Regex(RequireValue(a, args, ref i), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); return true;
                case 'm': opt.MinRect = ParseRectWH(RequireValue(a, args, ref i), "-m"); return true;
                case 'x': opt.MaxRect = ParseRectWH(RequireValue(a, args, ref i), "-x"); return true;
                case 'p':
                {
                    string v = RequireValue(a, args, ref i);
                    uint pid;
                    if (!TryParsePid(v, out pid))
                        throw new ArgumentException($"Invalid -p value: {v}");
                    opt.TargetPid = pid;
                    return true;
                }
                case 'n':
                    opt.TargetName = RequireValue(a, args, ref i);
                    return true;
                default:
                    Console.WriteLine($"Unknown option: {a}");
                    PrintUsage(exeName);
                    return false;
            }
        }

        // -d6 / -m800x600 / -x1024x768
        char first = a[1];
        if (first == 'd')
        {
            string v = a.Substring(2);
            opt.MaxDepth = ParseNonNegInt(v, "-d");
            return true;
        }
        if (first == 'm')
        {
            string v = a.Substring(2);
            opt.MinRect = ParseRectWH(v, "-m");
            return true;
        }
        if (first == 'x')
        {
            string v = a.Substring(2);
            opt.MaxRect = ParseRectWH(v, "-x");
            return true;
        }

        // bundle flags: -avsrjP
        for (int k = 1; k < a.Length; k++)
        {
            switch (a[k])
            {
                case 'a': opt.AsciiTree = true; break;
                case 'v': opt.VisibleOnly = true; break;
                case 's': opt.ShowStyle = true; break;
                case 'r': opt.ShowRect = true; break;
                case 'j': opt.Json = true; break;
                case 'P': opt.ChildPidOnly = true; break;
                default:
                    Console.WriteLine($"Unknown/unsupported short option bundle: {a}");
                    PrintUsage(exeName);
                    return false;
            }
        }

        return true;
    }

    static string RequireValue(string optName, string[] args, ref int i)
    {
        int eq = optName.IndexOf('=');
        if (eq >= 0 && eq < optName.Length - 1)
            return optName.Substring(eq + 1);

        if (i + 1 >= args.Length)
            throw new ArgumentException($"Option {optName} requires a value.");
        i++;
        return args[i];
    }

    static bool TryParsePid(string s, out uint pid)
    {
        pid = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;

        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out pid);

        return uint.TryParse(s, out pid);
    }

    static int ParseNonNegInt(string s, string optName)
    {
        int v;
        if (!int.TryParse(s, out v) || v < 0)
            throw new ArgumentException($"Invalid {optName} value: {s}");
        return v;
    }

    static SizeWH ParseRectWH(string spec, string optName)
    {
        if (string.IsNullOrWhiteSpace(spec))
            throw new ArgumentException($"Invalid {optName} value: (empty)");

        var m = Regex.Match(spec.Trim(), @"^\s*(\d+)\s*[xX,]\s*(\d+)\s*$");
        if (!m.Success)
            throw new ArgumentException($"Invalid {optName} value: {spec} (e.g., 800x600)");

        int w = int.Parse(m.Groups[1].Value);
        int h = int.Parse(m.Groups[2].Value);
        if (w < 0 || h < 0)
            throw new ArgumentException($"Invalid {optName} value: {spec}");

        return new SizeWH(w, h);
    }

    static bool Eq(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
    static bool Starts(string a, string prefix) => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    static string GetEqValue(string a)
    {
        int idx = a.IndexOf('=');
        return idx >= 0 ? a.Substring(idx + 1) : "";
    }

    // ----------------------------
    // Usage
    // ----------------------------

    static void PrintUsage(string exeName)
    {
        Console.WriteLine("Usage:");
        Console.WriteLine($"  {exeName} <processName|pid> [options]");
        Console.WriteLine($"  {exeName} -p <pid> [options]");
        Console.WriteLine($"  {exeName} -n <processName> [options]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine($"  {exeName} 1234 -v -m 200x100 -x 1600x1200 -r");
        Console.WriteLine($"  {exeName} AppleMusic --visibleOnly --minRect=200x100 --rect");
        Console.WriteLine($"  {exeName} -p 1234 -avsrjP -o tree.json");
        Console.WriteLine();
        Console.WriteLine("Options (Long / Short):");
        Console.WriteLine("  --pid=PID            -p PID      Target PID (can be used instead of positional arg)");
        Console.WriteLine("  --name=NAME          -n NAME     Target process name (can be used instead of positional arg)");
        Console.WriteLine();
        Console.WriteLine("  --maxDepth=N         -d N        Max tree depth (default: unlimited)");
        Console.WriteLine("  --ascii              -a          Use ASCII tree glyphs instead of Unicode");
        Console.WriteLine("  --childPidOnly       -P          Only enumerate child windows owned by the same PID");
        Console.WriteLine();
        Console.WriteLine("  --visibleOnly        -v          Visible windows only (keeps ancestor path)");
        Console.WriteLine("  --minRect=WxH        -m WxH      Minimum size filter (WindowRect)");
        Console.WriteLine("  --maxRect=WxH        -x WxH      Maximum size filter (WindowRect)");
        Console.WriteLine();
        Console.WriteLine("  --classContains=TEXT -c TEXT     ClassName contains filter");
        Console.WriteLine("  --titleContains=TEXT -t TEXT     Title contains filter");
        Console.WriteLine("  --classRegex=REGEX   -C REGEX    ClassName regex");
        Console.WriteLine("  --titleRegex=REGEX   -T REGEX    Title regex");
        Console.WriteLine();
        Console.WriteLine("  --style              -s          Show Style/ExStyle (only when enabled)");
        Console.WriteLine("  --rect               -r          Show Rect/Client (only when enabled)");
        Console.WriteLine();
        Console.WriteLine("  --json               -j          Output JSON (stdout or -o)");
        Console.WriteLine("  --jsonFile=PATH      -o PATH     Write JSON to file");
        Console.WriteLine();
        Console.WriteLine("Short option bundle example: -avsrjP (options that require a value: -d,-m,-x,-c,-t,-C,-T,-o,-p,-n cannot be bundled)");
    }

    // ----------------------------
    // JSON output (C# 7.3 / .NET Framework friendly)
    // ----------------------------

    static void WriteJson(JsonRoot root, Options opt)
    {
        var sb = new StringBuilder(1024);
        var jw = new SimpleJsonWriter(sb, indent: "  ");

        jw.BeginObject();

        jw.PropName("generatedAt"); jw.String(root.generatedAt);

        jw.Comma();
        jw.PropName("processes");
        jw.BeginArray();
        for (int i = 0; i < root.processes.Count; i++)
        {
            if (i > 0) jw.Comma();
            jw.BeginObject();
            jw.PropName("name"); jw.String(root.processes[i].name);
            jw.Comma();
            jw.PropName("pid"); jw.Number(root.processes[i].pid);
            jw.EndObject();
        }
        jw.EndArray();

        jw.Comma();
        jw.PropName("options");
        WriteJsonOptions(jw, root.options);

        jw.Comma();
        jw.PropName("roots");
        jw.BeginArray();
        for (int i = 0; i < root.roots.Count; i++)
        {
            if (i > 0) jw.Comma();
            WriteWindowNodeJson(jw, root.roots[i], opt);
        }
        jw.EndArray();

        jw.EndObject();

        string json = sb.ToString();

        if (!string.IsNullOrWhiteSpace(opt.JsonFile))
        {
            File.WriteAllText(opt.JsonFile, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        else
        {
            Console.WriteLine(json);
        }
    }

    static void WriteJsonOptions(SimpleJsonWriter jw, JsonOptions o)
    {
        jw.BeginObject();

        bool first = true;
        Action<string, Action> prop = (name, writeValue) =>
        {
            if (!first) jw.Comma();
            first = false;
            jw.PropName(name);
            writeValue();
        };

        prop("TargetPid", () => { if (o.TargetPid.HasValue) jw.Number(o.TargetPid.Value); else jw.Null(); });
        prop("TargetName", () => jw.StringOrNull(o.TargetName));
        prop("AsciiTree", () => jw.Bool(o.AsciiTree));
        prop("MaxDepth", () => jw.Number(o.MaxDepth));
        prop("ChildPidOnly", () => jw.Bool(o.ChildPidOnly));
        prop("VisibleOnly", () => jw.Bool(o.VisibleOnly));
        prop("MinRect", () => jw.StringOrNull(o.MinRect));
        prop("MaxRect", () => jw.StringOrNull(o.MaxRect));
        prop("ShowStyle", () => jw.Bool(o.ShowStyle));
        prop("ShowRect", () => jw.Bool(o.ShowRect));
        prop("ClassContains", () => jw.StringOrNull(o.ClassContains));
        prop("TitleContains", () => jw.StringOrNull(o.TitleContains));
        prop("ClassRegex", () => jw.StringOrNull(o.ClassRegex));
        prop("TitleRegex", () => jw.StringOrNull(o.TitleRegex));
        prop("Json", () => jw.Bool(o.Json));
        prop("JsonFile", () => jw.StringOrNull(o.JsonFile));

        jw.EndObject();
    }

    static void WriteWindowNodeJson(SimpleJsonWriter jw, WindowNode n, Options opt)
    {
        jw.BeginObject();

        // required fields
        jw.PropName("hwnd"); jw.String(n.hwnd);
        jw.Comma(); jw.PropName("hwndDec"); jw.Number(n.hwndDec);
        jw.Comma(); jw.PropName("pid"); jw.Number(n.pid);
        jw.Comma(); jw.PropName("tid"); jw.Number(n.tid);
        jw.Comma(); jw.PropName("visible"); jw.Bool(n.visible);
        jw.Comma(); jw.PropName("className"); jw.StringOrNull(n.className);
        jw.Comma(); jw.PropName("title"); jw.StringOrNull(n.title);

        // optional: style/exstyle only if enabled
        if (opt.ShowStyle)
        {
            jw.Comma(); jw.PropName("style"); jw.StringOrNull(n.style);
            jw.Comma(); jw.PropName("exStyle"); jw.StringOrNull(n.exStyle);
        }

        // optional: rect/client only if enabled
        if (opt.ShowRect)
        {
            jw.Comma(); jw.PropName("rect");
            if (n.rect == null) jw.Null();
            else
            {
                jw.BeginObject();
                jw.PropName("left"); jw.Number(n.rect.left);
                jw.Comma(); jw.PropName("top"); jw.Number(n.rect.top);
                jw.Comma(); jw.PropName("right"); jw.Number(n.rect.right);
                jw.Comma(); jw.PropName("bottom"); jw.Number(n.rect.bottom);
                jw.Comma(); jw.PropName("width"); jw.Number(n.rect.width);
                jw.Comma(); jw.PropName("height"); jw.Number(n.rect.height);
                jw.EndObject();
            }

            jw.Comma(); jw.PropName("client");
            if (n.client == null) jw.Null();
            else
            {
                jw.BeginObject();
                jw.PropName("width"); jw.Number(n.client.width);
                jw.Comma(); jw.PropName("height"); jw.Number(n.client.height);
                jw.EndObject();
            }
        }

        jw.Comma(); jw.PropName("match"); jw.Bool(n.match);

        jw.Comma(); jw.PropName("children");
        jw.BeginArray();
        for (int i = 0; i < n.children.Count; i++)
        {
            if (i > 0) jw.Comma();
            WriteWindowNodeJson(jw, n.children[i], opt);
        }
        jw.EndArray();

        jw.EndObject();
    }

    // Minimal JSON writer with pretty indentation (no external packages)
    private sealed class SimpleJsonWriter
    {
        private readonly StringBuilder _sb;
        private readonly string _indentUnit;
        private int _depth;
        private bool _needsIndent;

        public SimpleJsonWriter(StringBuilder sb, string indent)
        {
            _sb = sb;
            _indentUnit = indent ?? "  ";
            _depth = 0;
            _needsIndent = false;
        }

        public void BeginObject()
        {
            WriteIndentIfNeeded();
            _sb.Append('{');
            _depth++;
            _needsIndent = true;
        }

        public void EndObject()
        {
            _depth--;
            _sb.AppendLine();
            AppendIndent();
            _sb.Append('}');
            _needsIndent = true;
        }

        public void BeginArray()
        {
            WriteIndentIfNeeded();
            _sb.Append('[');
            _depth++;
            _needsIndent = true;
        }

        public void EndArray()
        {
            _depth--;
            _sb.AppendLine();
            AppendIndent();
            _sb.Append(']');
            _needsIndent = true;
        }

        public void PropName(string name)
        {
            _sb.AppendLine();
            AppendIndent();
            String(name);
            _sb.Append(": ");
            _needsIndent = false;
        }

        public void Comma()
        {
            _sb.Append(',');
            _needsIndent = true;
        }

        public void StringOrNull(string s)
        {
            if (s == null) Null();
            else String(s);
        }

        public void String(string s)
        {
            if (s == null) { Null(); return; }
            WriteIndentIfNeeded();
            _sb.Append('"');
            AppendEscaped(s);
            _sb.Append('"');
            _needsIndent = true;
        }

        public void Number(long n)
        {
            WriteIndentIfNeeded();
            _sb.Append(n);
            _needsIndent = true;
        }

        public void Number(uint n)
        {
            WriteIndentIfNeeded();
            _sb.Append(n);
            _needsIndent = true;
        }

        public void Number(int n)
        {
            WriteIndentIfNeeded();
            _sb.Append(n);
            _needsIndent = true;
        }

        public void Bool(bool b)
        {
            WriteIndentIfNeeded();
            _sb.Append(b ? "true" : "false");
            _needsIndent = true;
        }

        public void Null()
        {
            WriteIndentIfNeeded();
            _sb.Append("null");
            _needsIndent = true;
        }

        private void WriteIndentIfNeeded()
        {
            // Values following PropName should not auto-indent
            // but array/object begins should be on same line after ": "
            // We keep it simple: indentation happens only when _needsIndent is true and next token is a value within array
            // (PropName already inserted newline+indent)
        }

        private void AppendIndent()
        {
            for (int i = 0; i < _depth; i++)
                _sb.Append(_indentUnit);
        }

        private void AppendEscaped(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                switch (ch)
                {
                    case '\\': _sb.Append("\\\\"); break;
                    case '"': _sb.Append("\\\""); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20)
                        {
                            _sb.Append("\\u");
                            _sb.Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            _sb.Append(ch);
                        }
                        break;
                }
            }
        }
    }

    // ----------------------------
    // Tree building + filtering
    // ----------------------------

    static WindowNode BuildTree(
        IntPtr hWnd,
        int depth,
        HashSet<IntPtr> visited,
        HashSet<uint> targetPids,
        Options opt)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
            return null;

        if (opt.MaxDepth >= 0 && depth > opt.MaxDepth)
            return null;

        if (!visited.Add(hWnd))
        {
            // cycle guard: still return minimal node
            return MakeNode(hWnd, opt);
        }

        var node = MakeNode(hWnd, opt);

        if (opt.MaxDepth >= 0 && depth == opt.MaxDepth)
            return node;

        foreach (var child in EnumDirectChildren(hWnd))
        {
            if (opt.ChildPidOnly)
            {
                uint cpid;
                GetWindowThreadProcessId(child, out cpid);
                if (!targetPids.Contains(cpid))
                    continue;
            }

            var c = BuildTree(child, depth + 1, visited, targetPids, opt);
            if (c != null)
                node.children.Add(c);
        }

        return node;
    }

    static WindowNode ApplyFilterAndPrune(WindowNode node, Options opt)
    {
        if (node == null) return null;

        bool filterActive =
            opt.VisibleOnly ||
            opt.MinRect.HasValue ||
            opt.MaxRect.HasValue ||
            !string.IsNullOrEmpty(opt.ClassContains) ||
            !string.IsNullOrEmpty(opt.TitleContains) ||
            opt.ClassRegex != null ||
            opt.TitleRegex != null;

        // prune children first
        if (node.children != null && node.children.Count > 0)
        {
            var newChildren = new List<WindowNode>();
            foreach (var c in node.children)
            {
                var cc = ApplyFilterAndPrune(c, opt);
                if (cc != null)
                    newChildren.Add(cc);
            }
            node.children = newChildren;
        }

        // match self
        node.match = Matches(node, opt);

        if (!filterActive)
            return node;

        // keep if self matches or any child kept
        bool keep = node.match || (node.children != null && node.children.Count > 0);
        return keep ? node : null;
    }

    static bool Matches(WindowNode n, Options opt)
    {
        bool ok = true;

        if (opt.VisibleOnly)
            ok &= n.visible;

        if (opt.MinRect.HasValue || opt.MaxRect.HasValue)
        {
            // if rect unknown -> not a match, but still can be kept via descendants
            if (!n._rectKnown || n._rectW == null || n._rectH == null)
                ok &= false;
            else
            {
                int w = n._rectW.Value;
                int h = n._rectH.Value;

                if (opt.MinRect.HasValue)
                    ok &= (w >= opt.MinRect.Value.W && h >= opt.MinRect.Value.H);

                if (opt.MaxRect.HasValue)
                    ok &= (w <= opt.MaxRect.Value.W && h <= opt.MaxRect.Value.H);
            }
        }

        if (!string.IsNullOrEmpty(opt.ClassContains))
            ok &= (n.className ?? "").IndexOf(opt.ClassContains, StringComparison.OrdinalIgnoreCase) >= 0;

        if (!string.IsNullOrEmpty(opt.TitleContains))
            ok &= (n.title ?? "").IndexOf(opt.TitleContains, StringComparison.OrdinalIgnoreCase) >= 0;

        if (opt.ClassRegex != null)
            ok &= opt.ClassRegex.IsMatch(n.className ?? "");

        if (opt.TitleRegex != null)
            ok &= opt.TitleRegex.IsMatch(n.title ?? "");

        return ok;
    }

    static IEnumerable<IntPtr> EnumDirectChildren(IntPtr parent)
    {
        IntPtr child = GetWindow(parent, GW_CHILD);
        while (child != IntPtr.Zero)
        {
            yield return child;
            child = GetWindow(child, GW_HWNDNEXT);
        }
    }

    // ----------------------------
    // Text printing
    // ----------------------------

    static void PrintTree(WindowNode root, Options opt)
    {
        PrintNode(root, indent: "", isLast: true, isRoot: true, opt: opt);
    }

    static void PrintNode(WindowNode n, string indent, bool isLast, bool isRoot, Options opt)
    {
        string prefix = MakePrefix(indent, isLast, isRoot, opt);
        Console.WriteLine(prefix + DescribeNode(n, opt));
string nextIndent = MakeNextIndent(indent, isLast, isRoot, opt);
        for (int i = 0; i < n.children.Count; i++)
        {
            bool last = (i == n.children.Count - 1);
            PrintNode(n.children[i], nextIndent, last, isRoot: false, opt: opt);
        }
    }

    static string MakePrefix(string indent, bool isLast, bool isRoot, Options opt)
    {
        if (isRoot) return "";
        if (opt.AsciiTree) return indent + (isLast ? "\\- " : "+- ");
        return indent + (isLast ? "└─ " : "├─ ");
    }

    static string MakeNextIndent(string indent, bool isLast, bool isRoot, Options opt)
    {
        if (isRoot) return "";
        if (opt.AsciiTree) return indent + (isLast ? "   " : "|  ");
        return indent + (isLast ? "   " : "│  ");
    }

    static string DescribeNode(WindowNode n, Options opt)
    {
        var sb = new StringBuilder(256);

        string title = (n.title ?? "").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

        sb.Append($"{n.hwnd} ({n.hwndDec}) | PID {n.pid} TID {n.tid} | Visible {(n.visible ? "Y" : "N")}");

        if (opt.ShowStyle)
        {
            sb.Append($" | Style {(n.style ?? "?")} ExStyle {(n.exStyle ?? "?")}");
        }

        if (opt.ShowRect)
        {
            if (n.rect != null)
                sb.Append($" | Rect [{n.rect.left},{n.rect.top},{n.rect.right},{n.rect.bottom}] ({n.rect.width}x{n.rect.height})");
            else
                sb.Append(" | Rect ?");

            if (n.client != null)
                sb.Append($" | Client ({n.client.width}x{n.client.height})");
            else
                sb.Append(" | Client ?");
        }

        sb.Append($" | Class '{(n.className ?? "")}' | Title \"{title}\"");
        return sb.ToString();
    }

    // ----------------------------
    // Node creation
    // ----------------------------

    static WindowNode MakeNode(IntPtr hWnd, Options opt)
    {
        uint pid;
        uint tid = GetWindowThreadProcessId(hWnd, out pid);

        string className = GetClassNameSafe(hWnd);
        string title = GetWindowTextSafe(hWnd);

        bool visible = false;
        try { visible = IsWindowVisible(hWnd); } catch { }

        // Rect is needed for:
        //  - showing (--rect)
        //  - filtering (--minRect/--maxRect)
        bool needRectForFilter = opt.MinRect.HasValue || opt.MaxRect.HasValue;
        bool needRect = opt.ShowRect || needRectForFilter;

        RectInfo rectInfo = null;
        ClientInfo clientInfo = null;

        int? rectW = null, rectH = null;
        bool rectKnown = false;

        if (needRect)
        {
            try
            {
                RECT r;
                if (GetWindowRect(hWnd, out r))
                {
                    rectKnown = true;
                    rectW = r.Width;
                    rectH = r.Height;

                    if (opt.ShowRect)
                    {
                        rectInfo = new RectInfo
                        {
                            left = r.Left,
                            top = r.Top,
                            right = r.Right,
                            bottom = r.Bottom,
                            width = r.Width,
                            height = r.Height
                        };
                    }
                }
            }
            catch { }

            if (opt.ShowRect)
            {
                try
                {
                    RECT cr;
                    if (GetClientRect(hWnd, out cr))
                    {
                        clientInfo = new ClientInfo
                        {
                            width = cr.Width,
                            height = cr.Height
                        };
                    }
                }
                catch { }
            }
        }

        string styleStr = null;
        string exStyleStr = null;
        if (opt.ShowStyle)
        {
            try
            {
                IntPtr style = GetWindowLongPtr(hWnd, GWL_STYLE);
                IntPtr exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
                styleStr = $"0x{style.ToInt64():X}";
                exStyleStr = $"0x{exStyle.ToInt64():X}";
            }
            catch { }
        }

        long hd = hWnd.ToInt64();
        return new WindowNode
        {
            hwnd = $"0x{hd:X}",
            hwndDec = hd,
            pid = pid,
            tid = tid,
            visible = visible,
            className = className,
            title = title,

            style = styleStr,
            exStyle = exStyleStr,
            rect = rectInfo,
            client = clientInfo,

            match = false,
            _rectW = rectW,
            _rectH = rectH,
            _rectKnown = rectKnown
        };
    }

    static string GetClassNameSafe(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        int n = GetClassName(hWnd, sb, sb.Capacity);
        if (n <= 0) return "";
        return sb.ToString();
    }

    static string GetWindowTextSafe(IntPtr hWnd)
    {
        int len = 0;
        try { len = GetWindowTextLength(hWnd); } catch { }

        int cap = Math.Max(len + 1, 256);
        var sb = new StringBuilder(cap);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
