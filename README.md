# Simple Notes

A lightweight Windows desktop notes app, inspired by the two‑pane layout in the
reference screenshot: a list of notes on the left and a rich‑text editor on the
right.

## Features

- **Two‑pane layout** — a "Notes" sidebar on the left, an editor on the right.
- **Note list** showing each note's title, a snippet of its text, and the date
  it was last modified.
- **+ button** to create a new note.
- **Rich‑text editor** with basic controls: **bold**, *italics*, underline,
  strikethrough, bulleted / numbered lists, font family, and font size.
- **Title** field at the top of each note.
- **Tags** you can add and remove (e.g. `Games/EU4`), shown as chips above the
  editor.
- **Search** box to filter notes by title, text, or tag.
- **Auto‑save** — everything is written to disk a moment after you stop typing.

Your notes are stored on your machine at:

```
%AppData%\SimpleNotes\notes.json
```

## What it's built with

This is a native Windows app written in **C# using WPF** (Windows Presentation
Foundation) on **.NET 8**. WPF is Windows‑only, so this app runs on Windows
(10 or 11). You do **not** need Visual Studio — the free .NET SDK and a single
command are enough.

---

## How to compile and launch (step by step)

You'll do all of this on a **Windows** PC.

### 1. Install the .NET SDK (one time)

1. Go to <https://dotnet.microsoft.com/download/dotnet/8.0>
2. Under **.NET 8.0**, download the **SDK** installer for **Windows x64**
   (the "SDK", not the "Runtime").
3. Run the installer and accept the defaults.
4. To confirm it worked, open a new **Command Prompt** or **PowerShell** window
   and run:

   ```powershell
   dotnet --version
   ```

   You should see a version number like `8.0.xxx`. If you get an error, close
   and reopen the terminal (or restart) so the new install is picked up.

### 2. Get the code

If you cloned this repository with git, you already have it. Otherwise download
it and unzip it somewhere convenient, e.g. `C:\Users\you\ps-thingmanager`.

### 3. Run the app

In the terminal, change into the project folder and run it:

```powershell
cd C:\path\to\ps-thingmanager
dotnet run --project src/SimpleNotes.csproj
```

The first run downloads dependencies and compiles the app (this can take a
minute). After that, the **Simple Notes** window opens. Leave the terminal
window open while you use the app — closing it closes the app.

### 4. (Optional) Build a standalone .exe you can double‑click

If you'd rather launch it like a normal program instead of typing a command
each time, build a release version:

```powershell
dotnet publish src/SimpleNotes.csproj -c Release -r win-x64 --self-contained false -o publish
```

Then open the `publish` folder and double‑click **SimpleNotes.exe**.

> Want a version that runs on a PC without .NET installed? Use
> `--self-contained true` instead. The output folder will be larger because it
> bundles the runtime.

You can also create a desktop shortcut: right‑click `SimpleNotes.exe` →
**Send to → Desktop (create shortcut)**.

---

## Using the app

- **New note:** click the round purple **+** button at the top‑left.
- **Rename:** click the big title at the top of the right pane and type.
- **Add a tag:** click **"Link tags, notes, files..."** next to the tags,
  type a tag (for a nested tag just type `Games/EU4`), and press **Enter**.
  Remove a tag with the small ✕ on its chip.
- **Format text:** select some text, then use the toolbar (Bold, Italic,
  Underline, Strikethrough, lists, font, size). Keyboard shortcuts
  **Ctrl+B / Ctrl+I / Ctrl+U** also work.
- **Delete a note:** the **···** button at the top‑right of the editor.
- **Search:** type in the **Search...** box in the sidebar.

## Project layout

```
src/
  SimpleNotes.csproj    Project file (targets .NET 8, WPF enabled)
  App.xaml / .cs        App startup + the dark theme (colors, button styles)
  MainWindow.xaml / .cs The two‑pane window and all its behavior
  Note.cs               The data model for a single note
  NoteStore.cs          Loads/saves notes to notes.json in %AppData%
```

## Troubleshooting

- **`dotnet` is not recognized** — the SDK isn't installed or the terminal was
  open before you installed it. Reinstall from the link above, then open a
  fresh terminal.
- **"This app can only run on Windows" / build errors about `net8.0-windows`**
  — WPF is Windows‑only; build and run on Windows, not macOS/Linux.
- **Nothing happens after `dotnet run`** — check the terminal for red error
  text and make sure you're pointing at `src/SimpleNotes.csproj`.
