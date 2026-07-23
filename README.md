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

Then open the `publish` folder and double‑click **SimpleNotes.exe**. It has its
own app icon and launches like any normal Windows program — no terminal needed.

> Want a version that runs on a PC without .NET installed? Use
> `--self-contained true` instead. The output folder will be larger because it
> bundles the runtime.

### Make it launch from an icon

Once you have `SimpleNotes.exe`, you can start it the same way as any other app:

- **Desktop shortcut:** right‑click `SimpleNotes.exe` →
  **Send to → Desktop (create shortcut)**. Double‑click the desktop icon to run.
- **Pin to taskbar / Start:** launch the app, then right‑click its taskbar icon
  → **Pin to taskbar** (or find it and choose **Pin to Start**).

The window, taskbar, and shortcut all show the app's purple notes icon.

> Keep the whole `publish` folder together and create the shortcut to the
> `.exe` inside it — the shortcut points at that file, so don't move just the
> `.exe` out on its own.

---

## Installing it like a real program (one‑click installer)

For a "download → double‑click → installed, with a Start‑menu entry and an
uninstaller" experience, the project ships an **Inno Setup** installer script
(`installer/SimpleNotes.iss`). It produces a single `SimpleNotesSetup.exe`.

You have two ways to produce that installer — pick one:

### Option A — let GitHub build it (recommended)

The repository includes a GitHub Actions workflow
(`.github/workflows/release.yml`) that builds everything on a Windows runner.
See **"Publishing an update"** below — the short version is: push a tag like
`v1.0.0` and, a few minutes later, a **Release** appears on GitHub with
`SimpleNotesSetup.exe` attached. Download and run it. Done — no tools to install
locally.

### Option B — build the installer yourself

1. Install **Inno Setup** (free): <https://jrsoftware.org/isdl.php>
2. Publish a self‑contained build (bundles the .NET runtime so target PCs need
   nothing installed):

   ```powershell
   dotnet publish src/SimpleNotes.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish
   ```

3. Compile the installer:

   ```powershell
   & "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\SimpleNotes.iss
   ```

4. The finished installer is at `installer\Output\SimpleNotesSetup.exe`.

Running `SimpleNotesSetup.exe` installs Simple Notes per‑user (no admin prompt),
adds Start‑menu and optional desktop shortcuts, and registers an entry in
**Settings → Apps** so it can be uninstalled like any other program.

---

## Updates: going from the repository to an installed update

An app can't safely rebuild itself from source on someone's PC, so updates flow
through **GitHub Releases**. The picture:

```
  edit code  ->  bump version  ->  push a tag (vX.Y.Z)  ->  GitHub Action builds
  the installer and publishes a Release  ->  the app's "Check for updates" button
  sees the new Release and installs it
```

### Publishing an update (you, the developer)

1. Make your changes.
2. Bump the version in **`src/SimpleNotes.csproj`** (`<Version>1.1.0</Version>`).
3. Commit and push.
4. Create and push a matching tag:

   ```powershell
   git tag v1.1.0
   git push origin v1.1.0
   ```

5. The **Build and Release** GitHub Action compiles a self‑contained build,
   builds `SimpleNotesSetup.exe`, and publishes a GitHub Release named `v1.1.0`
   with the installer attached. (Watch it under the repo's **Actions** tab.)

That's the whole "repository → update" path. Keep the tag and the csproj
`<Version>` the same so the app reports the right number.

### Updating from inside the app (your users)

There's a **Check for updates** link at the bottom‑left of the window, next to
the version number. Clicking it:

1. asks GitHub for the latest Release,
2. compares it to the running version,
3. if newer, offers to download `SimpleNotesSetup.exe` and run it — the app
   closes, the installer upgrades it in place, and it relaunches on the new
   version.

If you're already current, it just says so.

> **Important:** the in‑app updater reads the repository's Releases
> *anonymously*, so this works only if the repository is **public**. If you keep
> `ps-thingmanager` private, GitHub won't let the app read or download the
> release without a login, and the button will report that it couldn't check.
> In that case, either make the repo public or distribute new
> `SimpleNotesSetup.exe` builds another way (e.g. a shared folder).

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
  SimpleNotes.csproj    Project file (targets .NET 8, WPF, version, icon)
  App.xaml / .cs        App startup + the dark theme (colors, button styles)
  MainWindow.xaml / .cs The two‑pane window and all its behavior
  Note.cs               The data model for a single note
  NoteStore.cs          Loads/saves notes to notes.json in %AppData%
  Updater.cs            "Check for updates" via the GitHub Releases API
  app.ico               The application icon
installer/
  SimpleNotes.iss       Inno Setup script -> SimpleNotesSetup.exe
.github/workflows/
  release.yml           Builds the installer + publishes a Release on a tag
```

## Troubleshooting

- **`dotnet` is not recognized** — the SDK isn't installed or the terminal was
  open before you installed it. Reinstall from the link above, then open a
  fresh terminal.
- **"This app can only run on Windows" / build errors about `net8.0-windows`**
  — WPF is Windows‑only; build and run on Windows, not macOS/Linux.
- **Nothing happens after `dotnet run`** — check the terminal for red error
  text and make sure you're pointing at `src/SimpleNotes.csproj`.
