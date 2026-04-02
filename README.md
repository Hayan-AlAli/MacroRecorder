# MacroApp - Advanced Macro Recorder & Automation Tool

MacroApp is a powerful, modern, open-source macro recording and automation utility built in C# & WPF. It allows you to record exact mouse and keyboard inputs, visually manage and edit scripts in a live editor, and play them back with deep configuration settings. 

It is designed to cleanly bridge low-level Win32 hardware input hooks with a beautiful, reactive, MVVM-powered interface.

---

## ✨ Features

- **Global Input Hooking:** Captures precise raw mouse movements, clicks, scrolls, and keystrokes at the hardware level.
- **Advanced Playback Engine:** Play back recorded macros with adjustable settings:
  - Configurable Speed Multiplier (0.25x to 1.75x) 
  - Sub-millisecond delay precision using High-Resolution Timers
  - Infinite or fixed repeat counts and loop delays
  - Emergency Stop kill-switches
- **Live Script Editor:** Built-in IDE (`AvalonEdit`) to manually edit, validate, and tweak your macros using a custom scripting language syntax.
- **Step-Through Debugger:** Play your macros action-by-action to easily identify logical errors or misaligned clicks.
- **Modern Dark UI:** A fully responsive, dark-mode themed interface powered by `CommunityToolkit.Mvvm`.
- **(Coming Soon) Visual Triggers:** Advanced scheduling capability alongside OpenCV-powered Image Matching and OCR validation to act dynamically on visual screen changes.

---

## 🛠️ Architecture and Stack

The application is heavily decoupled to separate core logic from UI bindings, ensuring a crash-proof background recording environment. 

- **Frontend:** WPF / XAML + CommunityToolkit.Mvvm 
- **Backend / Core Engine:** .NET 8 (C# 12)
- **Low-Level Native Calls:** `user32.dll` (SendInput, SetWindowsHookEx)
- **Serialization:** Atomic XML `.mcr` files 

### Project Structure 
* **`MacroApp.Core`**: Handles serialization, models, recording state, and playback loops.
* **`MacroApp.UI`**: Handles the visuals, WPF ViewModels, and the AvalonEdit bridge.
* **`MacroApp.Scripting`**: The Lexer, Parser, and AST engine for text-to-macro interpretation.
* **`MacroApp.NativeInterop`**: P/Invoke declarations, hook managers, and hardware integration.
* **`MacroApp.ImageMatch`**: (In Progress) OpenCV bindings for advanced detection.

---

## 🚀 Getting Started

### Prerequisites
- Windows 10/11
- .NET 8.0 Desktop Runtime SDK
- Visual Studio 2022 / Rider / VS Code.

### Running Locally
To launch the application from the CLI, simply execute the main UI project:
```powershell
# Run straight from the solution root
dotnet run --project src/MacroApp.UI/MacroApp.UI.csproj
```

### Basic Usage
1. Click **New Macro** (`Ctrl+N`).
2. Click the red **Record** button (or press `F9`) and perform your desired mouse/keyboard clicks anywhere on Windows.
3. Click **Stop** when finished.
4. Give your macro a name and adjust the playback settings via the **Properties** panel.
5. Click **Play** (or press `F10`) to witness your actions replayed! 

> **Tip:** You can manually tweak the recorded actions generated in the center Script Editor. Any edits you apply textually will instantly translate back into your macro's raw event timeline.
