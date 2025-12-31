<div align="center">
  <img src="WindowDump.png"  width="200"/>
  <h1 align="center">WindowDump</h1>
  <h4 align="center">Simple, Powerful Window Handles Dumper</h4>
</div>

<div align="center">
  <a href="https://github.com/HiSkyZen/WindowDump"><img src="https://img.shields.io/github/stars/hiskyzen/windowdump.svg?logo=github&style=for-the-badge" alt="GitHub stars"></a>
  <a href="https://github.com/HiSkyZen/WindowDump/releases/latest"><img src="https://img.shields.io/github/downloads/hiskyzen/windowdump/total.svg?style=for-the-badge&logo=github" alt="GitHub Releases"></a>
</div>


A small Windows utility that prints a **tree view of a process's window hierarchy** (top-level windows and their child windows), with powerful filtering and optional JSON export.


## Features

- **True window tree output** (parent → child → sibling traversal), not a flat child list
- Target by **process name** *or* **PID**
  - If multiple processes share the same name, they are **sorted by PID** (ascending) in both text and JSON output
- Optional details (only shown when enabled)
  - **Style / ExStyle** (`--style` / `-s`)
  - **WindowRect / ClientRect** (`--rect` / `-r`)
- Filters (filters keep the **ancestor path** to matched nodes)
  - Visible windows only (`--visibleOnly` / `-v`)
  - Minimum / maximum WindowRect size (`--minRect` / `-m`, `--maxRect` / `-x`)
  - Class/title contains or regex filters
- Optional **JSON output** (`--json` / `-j`)
  - `--jsonFile` / `-o` writes JSON to a file (**requires `--json` / `-j`**)
- Unicode or ASCII tree glyphs (`--ascii` / `-a`)
- No external dependencies (JSON output uses an internal writer)

## Requirements

- Windows (uses Win32 `user32.dll`)
- C# **7.3** compatible build environment  
  (e.g., Visual Studio with a .NET Framework project)

## Build

Open the solution in Visual Studio and build, or compile `Program.cs` in your existing project.

> Note: The tool relies on Win32 APIs (`user32.dll`), so it must run on Windows.

## Usage

```bash
WindowDump <processName|pid> [options]
```

You can also explicitly pass PID/name:

```bash
WindowDump -p <pid> [options]
WindowDump -n <processName> [options]
```

### Examples

Target by PID, show only visible windows, filter by minimum size, and show rectangles:

```bash
WindowDump 1234 -v -m 200x100 -r
```

Target by name (multiple processes will be handled and printed in PID order):

```bash
WindowDump notepad
```

Print ASCII tree glyphs, show style/exstyle, and limit depth:

```bash
WindowDump notepad -a -s -d 6
```

Export JSON to stdout:

```bash
WindowDump notepad -j
```

Export JSON to a file (**`-o` requires `-j`**):

```bash
WindowDump notepad -j -o tree.json
```

## Options

### Target selection
- `--pid=PID`, `-p PID`  
  Target a specific PID (you can also pass PID as the positional argument)
- `--name=NAME`, `-n NAME`  
  Target a process name (you can also pass the name as the positional argument)

### Tree / enumeration
- `--maxDepth=N`, `-d N`  
  Max tree depth (default: unlimited)
- `--ascii`, `-a`  
  Use ASCII tree glyphs instead of Unicode
- `--childPidOnly`, `-P`  
  Only enumerate child windows owned by the same PID (useful when hosted windows belong to other processes)

### Filters (ancestor path is preserved)
- `--visibleOnly`, `-v`  
  Match visible windows only
- `--minRect=WxH`, `-m WxH`  
  Minimum **WindowRect** size (e.g., `800x600`)
- `--maxRect=WxH`, `-x WxH`  
  Maximum **WindowRect** size (e.g., `1920x1080`)
- `--classContains=TEXT`, `-c TEXT`  
  Class name contains filter
- `--titleContains=TEXT`, `-t TEXT`  
  Window title contains filter
- `--classRegex=REGEX`, `-C REGEX`  
  Class name regex filter
- `--titleRegex=REGEX`, `-T REGEX`  
  Window title regex filter

### Optional output fields (only shown when enabled)
- `--style`, `-s`  
  Show `Style` / `ExStyle`
- `--rect`, `-r`  
  Show `WindowRect` and `ClientRect`

### JSON
- `--json`, `-j`  
  Output JSON (to stdout, or use `-o`)
- `--jsonFile=PATH`, `-o PATH`  
  Write JSON to a file (**must be used with `--json` / `-j`**)

### Short option bundles

Flag-only short options can be bundled, e.g.:

```bash
WindowDump -p 1234 -avsrjP -o tree.json
```

Bundling is supported for flags: `-a -v -s -r -j -P`  
Options that require a value (`-d -m -x -c -t -C -T -o -p -n`) **cannot** be bundled.

## Notes

- `--minRect` / `--maxRect` are based on **WindowRect** (screen coordinates size), not ClientRect.
- Filtering is **pruning-based**: nodes are kept if they match filters **or** have descendants that match.
- Unicode tree glyphs look best in Windows Terminal / modern consoles. Use `--ascii` if glyphs appear broken.
