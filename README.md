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

## Download

Grab the zip from the [releases page](https://github.com/Hayan-AlAli/MacroRecorder/releases),
unzip it and run `MacroRecorder.exe`. Nothing else to install. It needs 64-bit Windows 10
(1809 or later) or Windows 11. The exe isn't signed, so SmartScreen will warn you the
first time: "More info", then "Run anyway".

Put it in a folder you can write to, since it keeps macros, settings and logs next to itself.

## Building and running

You need Windows 10 (version 1809 or later) or Windows 11, and the
[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run --project src/MacroApp.UI
```

Or open `MacroApp.sln` in Visual Studio 2022 or Rider and run `MacroApp.UI`.

Tests:

```powershell
dotnet test
```

Releases are built by GitHub Actions: add a section for the new version to
`CHANGELOG.md`, then push a tag like `v1.1.0` and the workflow builds the exe and
publishes it.

## Using it

1. Hit **New** (Ctrl+N) to make a macro, or select an existing one.
2. Press **F9** (or click Record). There's a 3 second countdown, then everything you do
   gets recorded.
3. Press **F9** again to stop. The recording shows up as a script in the editor.
4. Press **F10** to play it back. **Esc** stops playback immediately, even if another
   window has focus.

The hotkeys are global, so you can start and stop things without switching back to the
app. The status bar turns red while recording and blue while a macro plays. These are the defaults; you can change them under **Settings**, and the status bar
always shows the current ones. If another program has already claimed a hotkey, the
status bar tells you and you can pick a different one.

| Key | What it does |
| --- | --- |
| F9  | Start / stop recording |
| F10 | Start / stop playback |
| F11 | Stop whatever is running |
| Esc | Emergency stop during playback |
| Ctrl+S | Save the current macro |
| Ctrl+N | New macro |

**Settings** also has the recording countdown, how far the mouse has to move before a
move gets recorded, and an option to start MacroRecorder when you sign in to Windows.
They're saved to `settings.json` next to the executable.

Some details worth knowing:

- Clicks and typing inside MacroRecorder's own window aren't recorded, so you won't get
  the click on "Stop" at the end of every macro.
- If a script has text in it, Play runs the script. A macro only falls back to the raw
  recorded events when the script is empty.
- Stopping halfway through a macro releases any keys or mouse buttons it was holding,
  so you don't end up with a stuck Shift.
- The speed slider scales every delay. Repeat count `-1` loops until you stop it.
- **Step** pauses before each line; press it again to run the next one. **Resume** carries
  on at normal speed.
- Click in the strip left of the line numbers to set a breakpoint (a red dot). Playback
  pauses before that line and highlights it, then Step and Resume work as above. Breakpoints
  are saved with the macro and move with their line when you edit around them.
- Recording into a macro that's already selected replaces its script. Make a new macro
  first if you want to keep the old one.

Macros are saved as XML `.mcr` files in a `Macros` folder next to the executable, grouped
by category. Logs go to `logs/`. **Import** copies `.mcr` files into the library and
**Export** saves the selected macro wherever you want, which is how you move macros
between machines.

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

**Images and on-screen text**

```
WaitForImage "images/ok.png" 10000 0.9   // timeout in ms, then how close a match has to be (0-1)
ClickImage "images/ok.png"               // clicks the middle of it; add Right/Middle for other buttons
IfImageExists "images/error.png"
  Stop
EndIf

OCRGetText total 1200 80 300 40          // reads text from x y width height into {total}
OCRGetText everything                    // no region = every monitor
IfTextOnScreen "Saved"
  MsgBox "it saved"
EndIf
```

The easy way to get an image is the **Capture image** button: drag a box around the
thing you want to click and it's saved to an `images` folder next to the macro, with a
`ClickImage` line added to the script. Relative paths in a script (images, sounds) are
relative to the macro's own folder, so a macro and its images can be copied together.

Image matching is OpenCV template matching, so it wants the thing to look the same as
when you captured it: same size, same theme, same display scaling. If it stops matching,
recapture it or lower the threshold a little. Text reading uses the OCR engine built into
Windows and needs an OCR-capable language installed, which English and most other
Windows display languages already are.

The editor checks the script as you type and shows errors with line numbers at the
bottom. If something fails while it runs (a window that isn't there, a key name it
doesn't know), playback stops and the status bar says which line it was.

## How the code is laid out

```
src/
  MacroApp.NativeInterop   Win32 P/Invoke: low-level hooks, SendInput, hotkeys, key names, screen capture
  MacroApp.Scripting       Lexer, parser and interpreter for the script language (no Windows deps)
  MacroApp.Core            Recording, the two playback engines, macro storage, settings
  MacroApp.ImageMatch      Image matching (OpenCvSharp) and OCR (Windows.Media.Ocr)
  MacroApp.UI              WPF app (MVVM with CommunityToolkit.Mvvm, AvalonEdit for the editor)
tests/
  MacroApp.Tests           xUnit tests for scripting, playback, storage, settings and key names
```

Recording uses `WH_KEYBOARD_LL` / `WH_MOUSE_LL` hooks on their own thread with a message
loop. Playback runs on a background thread and uses `SendInput`, with a sleep-then-spin
delay so timing is accurate to well under a millisecond. The app is per-monitor DPI aware,
so every coordinate it records, clicks or captures is a real screen pixel, including on
scaled and mixed-DPI displays.
