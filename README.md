# MacroRecorder

A macro recorder for Windows that I wrote in C# and WPF. It records your mouse and
keyboard, turns the recording into a plain-text script you can edit, and plays it back.

The script part is what I actually wanted out of this. Most recorders give you a timeline
you can't do much with. Here a recording becomes something like this:

```
Delay 350
MouseClick Left 812 440
TypeText "hello"
KeyCombo Ctrl S
```

You can tidy that up by hand, wrap it in a `Repeat`, add variables, and so on.

## Building and running

You need Windows 10 or 11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run --project src/MacroApp.UI
```

Or open `MacroApp.sln` in Visual Studio 2022 or Rider and run `MacroApp.UI`.

Tests:

```powershell
dotnet test
```

## Using it

1. Hit **New** (Ctrl+N) to make a macro, or select an existing one.
2. Press **F9** (or click Record). There's a 3 second countdown, then everything you do
   gets recorded.
3. Press **F9** again to stop. The recording shows up as a script in the editor.
4. Press **F10** to play it back. **Esc** stops playback immediately, even if another
   window has focus.

The hotkeys are global, so you can start and stop things without switching back to the
app. If another program has already claimed F9/F10/F11, the status bar will tell you.

| Key | What it does |
| --- | --- |
| F9  | Start / stop recording |
| F10 | Start / stop playback |
| F11 | Stop whatever is running |
| Esc | Emergency stop during playback |
| Ctrl+S | Save the current macro |
| Ctrl+N | New macro |

Some details worth knowing:

- Clicks and typing inside MacroRecorder's own window aren't recorded, so you won't get
  the click on "Stop" at the end of every macro.
- If a script has text in it, Play runs the script. A macro only falls back to the raw
  recorded events when the script is empty.
- Stopping halfway through a macro releases any keys or mouse buttons it was holding,
  so you don't end up with a stuck Shift.
- The speed slider scales every delay. Repeat count `-1` loops until you stop it.
- **Step** pauses before each line; press it again to run the next one.
- Recording into a macro that's already selected replaces its script. Make a new macro
  first if you want to keep the old one.

Macros are saved as XML `.mcr` files in a `Macros` folder next to the executable, grouped
by category. Logs go to `logs/`.

## Script reference

One command per line. Commands are case-insensitive, `//` starts a comment, and text
with spaces goes in quotes.

**Keyboard**

```
KeyPress Enter            // down + up
KeyDown LeftShift
KeyUp LeftShift
KeyCombo Ctrl Shift Esc   // modifiers first, the last key is the one that gets tapped
TypeText "any unicode text"
```

Key names: letters and digits as-is (`A`, `7`), `F1`–`F24`, `Enter`, `Tab`, `Space`,
`Esc`, `Back`/`Backspace`, `Delete`, `Home`, `End`, `PageUp`, `Left`/`Up`/`Right`/`Down`,
`Ctrl`, `Alt`, `Shift`, `Win`, `LeftCtrl`, `RightAlt`, `NumPad0`–`NumPad9`, `OemComma`,
`VolumeUp`, and so on (the same names WPF's `Key` enum uses). Anything else can be
given as a hex virtual-key code, e.g. `0xE2`.

**Mouse**

```
MouseMove 500 300
MouseClick Left 500 300
MouseDoubleClick Left 500 300
MouseDown Right 500 300
MouseUp Right 500 300
MouseScroll -120 500 300        // negative scrolls down
MouseDrag Left 100 100 400 100
```

Buttons are `Left`, `Right`, `Middle`, `X1`, `X2`. Coordinates are screen pixels and
work across multiple monitors (negative values are fine for a monitor left of the
primary one).

**Timing and flow**

```
Delay 250                 // milliseconds
RandomDelay 100 400

Repeat 5
  KeyPress Down
EndRepeat

SetVar count 0
While {count} < 10
  IncVar count
  TypeText "line {count}"
  KeyPress Enter
EndWhile

If {count} >= 10
  MsgBox "done"
Else
  Stop
EndIf

start:                    // or: Label start
Goto start
```

Variables are written `{name}` and work in any argument. Conditions support `==`, `!=`,
`<`, `>`, `<=`, `>=`, or just `true`/`false`. Numbers compare as numbers, everything else
as case-insensitive text. `Goto` can jump to a label in the same block or any block
around it, but not into the middle of a loop.

**Windows, clipboard, misc**

```
RunProgram "notepad.exe"
WaitForWindow "Notepad" 5000    // fails the script if it doesn't show up in time
ActivateWindow "Notepad"        // matches part of the title, case-insensitive
MinWindow "Notepad"
MaxWindow "Notepad"
RestoreWindow "Notepad"
CloseWindow "Notepad"

SetClipboard "some text"
GetClipboard myVar

PlaySound "C:\Windows\Media\chimes.wav"
ShowMessage "Hello"
MsgBox "Hello" "Title"
```

The editor checks the script as you type and shows errors with line numbers at the
bottom. If something fails while it runs (a window that isn't there, a key name it
doesn't know), playback stops and the status bar says which line it was.

## How the code is laid out

```
src/
  MacroApp.NativeInterop   Win32 P/Invoke: low-level hooks, SendInput, hotkeys, key names
  MacroApp.Scripting       Lexer, parser and interpreter for the script language (no Windows deps)
  MacroApp.Core            Recording, the two playback engines, macro storage and serialization
  MacroApp.UI              WPF app (MVVM with CommunityToolkit.Mvvm, AvalonEdit for the editor)
  MacroApp.ImageMatch      Placeholder for OpenCV image matching, not wired up yet
tests/
  MacroApp.Tests           xUnit tests for the scripting, serialization and key-name code
```

Recording uses `WH_KEYBOARD_LL` / `WH_MOUSE_LL` hooks on their own thread with a message
loop. Playback runs on a background thread and uses `SendInput`, with a sleep-then-spin
delay so timing is accurate to well under a millisecond.

## Not done yet

- `WaitForImage`, `ClickImage`, `IfImageExists`, `OCRGetText` and `IfTextOnScreen` parse
  fine but fail at run time. The plan is OpenCV template matching in `MacroApp.ImageMatch`.
- The hotkeys are fixed to F9/F10/F11. There's a settings view model for changing them
  but no settings window or persistence behind it yet.
- No import/export buttons in the UI, although `MacroManager` supports both.
- Breakpoints exist in the file format and the event player but can't be set from the editor.
