# Changelog

## Unreleased

- New look. Neutral dark theme that matches Windows 11, Windows' own icons instead of
  emoji, a flat command bar, and a dark title bar. Scrollbars, dropdowns, checkboxes
  and tooltips are themed instead of showing the default light Windows ones.
- The status bar turns red while recording and blue while a macro plays, replacing the
  banners above the editor.
- One Record button that turns into "Stop recording", and one Stop button for everything.
- The macro list is a plain list with search, Duplicate and Delete (Delete now asks first).
- Speed is a dropdown (0.25× to 10×) instead of a slider.
- Calmer syntax colours in the editor.

## v1.0.0

First proper release. Download `MacroRecorder-v1.0.0-win-x64.zip`, unzip it somewhere you
can write to, and run `MacroRecorder.exe`. It's self-contained, so there's no .NET to
install. Windows will probably warn you the first time because the exe isn't signed;
click "More info" and then "Run anyway".

The app keeps its macros, settings and logs in folders next to the exe.

What's in it:

- Record mouse and keyboard, get an editable script, play it back. Play runs the script,
  so your edits are what actually happens.
- Script language with loops, variables, If/Else, Goto, window commands, clipboard and
  message boxes. Errors point at the line they happened on.
- Image matching (`WaitForImage`, `ClickImage`, `IfImageExists`) and on-screen text
  reading (`OCRGetText`, `IfTextOnScreen`). The Capture image button grabs part of the
  screen and adds the `ClickImage` line for you.
- Global hotkeys (F9 record, F10 play, F11 stop, Esc emergency stop), all changeable
  in Settings.
- Breakpoints and step-through playback in the editor.
- Import and export of `.mcr` files.
- Works properly on multiple monitors and scaled displays, and never leaves keys stuck
  down when you stop a macro halfway.

Needs Windows 10 version 1809 or later, or Windows 11, 64-bit.
