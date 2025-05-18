using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;

class Program
{
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpWindowText, int nMaxCount);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    static int Main(string[] args)
    {
        string exeName = Path.GetFileName(Environment.GetCommandLineArgs()[0]);

        if (args.Length < 1)
        {
            Console.WriteLine($"사용법: {exeName} <processName>");
            Console.WriteLine($"예: {exeName} AppleMusic    또는    {exeName} AppleMusic.exe");
            return 1;
        }

        string procName = args[0];
        if (procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            procName = procName.Substring(0, procName.Length - 4);

        Process[] processes = Process.GetProcessesByName(procName);
        if (processes.Length == 0)
        {
            Console.WriteLine($"프로세스 '{procName}'를 찾지 못했습니다.");
            return 1;
        }

        var targetPids = new HashSet<uint>();
        foreach (var proc in processes)
        {
            targetPids.Add((uint)proc.Id);
            Console.WriteLine($"Found Process: {proc.ProcessName}.exe (PID: {proc.Id})");
        }

        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (targetPids.Contains(pid))
            {
                long h = hWnd.ToInt64();
                Console.WriteLine($"\n=== Window Handle: 0x{h:X} ({h}) ===");
                DumpChildWindows(hWnd);
            }
            return true;
        }, IntPtr.Zero);

        Console.WriteLine("\n==== Done ====");
        return 0;
    }

    static void DumpChildWindows(IntPtr parentHWnd)
    {
        long ph = parentHWnd.ToInt64();
        Console.WriteLine($"-- Child Windows of 0x{ph:X} ({ph}) --");
        EnumChildWindows(parentHWnd, (hWnd, lParam) =>
        {
            long h = hWnd.ToInt64();
            var className = new StringBuilder(256);
            var windowText = new StringBuilder(256);

            GetClassName(hWnd, className, className.Capacity);
            GetWindowText(hWnd, windowText, windowText.Capacity);

            Console.WriteLine($"Handle: 0x{h:X} ({h}) | ClassName: '{className}' | Title: '{windowText}'");
            return true;
        }, IntPtr.Zero);
    }
}
