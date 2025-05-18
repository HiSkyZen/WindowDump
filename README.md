# WindowDump

A lightweight C# tool to enumerate the top-level and child window handles of a specific running process on Windows.  
Useful for debugging UI behavior, automating window manipulation, or inspecting hidden window hierarchies.

---

## 🔧 Features

- Enumerates **top-level windows** of a given process  
- Recursively lists all **child windows** under each top-level window  
- Displays window handle in **both hexadecimal and decimal**  
- Prints **class name** and **window title** of each window  
- Accepts process name via **command-line argument**

---

## 🖥️ Requirements

- Windows 10/11  
- .NET Framework 4.7.2 or higher (or use .NET Core with minimal adjustments)

---

## 🚀 Usage

```bash
WindowDump.exe <processName>
```

### Example:

```bash
WindowDump.exe AppleMusic
```

or

```bash
WindowDump.exe AppleMusic.exe
```

---

## 📦 Output Example

```
Found Process: AppleMusic.exe (PID: 13508)

=== Window Handle: 0x301926 (3146054) ===  
-- Child Windows of 0x301926 (3146054) --  
Handle: 0x10FA3C (1113148) | ClassName: 'InputNonClientPointerSource' | Title: 'Non Client Input Sink Window'  
Handle: 0x2E1A56 (3029590) | ClassName: 'Microsoft.UI.Content.DesktopChildSiteBridge' | Title: ''  
Handle: 0x1914BE (1644990) | ClassName: 'InputSiteWindowClass' | Title: ''  
```

---

## 📜 Notes

- If the process is not found, the tool will inform you and exit.  
- You can provide the process name **with or without** `.exe` extension.  
- If multiple processes with the same name exist, it handles **all of them**.

---

## 🛠️ Build Instructions

1. Open the `WindowDump.sln` file in Visual Studio  
2. Build with any config and Enjoy!

---

## 🪛 License

This project is released under the MIT License.
