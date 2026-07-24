# Purple Star Notes

A lightweight Windows desktop notes app with a two-pane layout: a list of notes
on the left and a rich-text editor on the right. Dark and light themes, headings,
lists, check lists, per-note text sizing, custom text colors, and an in-app
updater.

## Features

- **Two-pane layout** — a "Notes" sidebar on the left, an editor on the right.
- **Note list** showing each note's title, a snippet of its text, and the date
  it was last modified.
- **+ button** to create a new note; **search** to filter by title, text, or tag.
- **Formatting menu** — Normal, Heading 1/2/3, Bulleted list, Numbered list, and
  a clickable **Check list** (ticking an item crosses it out; unticking restores
  it). The button shows the icon of the current paragraph's format.
- **Bold / Italic / Underline / Strikethrough.**
- **− / + buttons** that shrink or grow the selected text one point at a time.
  When you resize text of a given kind (Normal, H2, …), new text of the same
  kind in that note picks up the same size automatically.
- **Text color** via the standard Windows color picker, plus a **clear** button
  that returns text to the theme's default color.
- **Alignment** (left / center / right / justify) and **indent / outdent.**
- **Title** field and add/remove **tags** (e.g. `Games/EU4`) per note.
- **Dark / light mode toggle** at the bottom-left; your choice is remembered.
- **Auto-save** — everything is written to disk a moment after you stop typing.
- **Check for updates** — pulls new versions from GitHub Releases (see below).

Your notes and settings are stored on your machine at:

```
%AppData%\PurpleStarNotes\
```

## What it's built with

A native Windows app written in **C# using WPF** on **.NET 8**. WPF is
Windows-only, so this app runs on Windows (10 or 11). You do **not** need Visual
Studio — the free .NET SDK and a single command are enough.

---

## How to compile and launch

Do this on a **Windows** PC.

1. **Install the .NET 8 SDK** (one time): <https://dotnet.microsoft.com/download/dotnet/8.0>
   Download the **SDK** for **Windows x64**, run it, then confirm in a new
   terminal:

   ```powershell
   dotnet --version
   ```

2. **Run the app** from the project folder:

   ```powershell
   dotnet run --project src/PurpleStarNotes.csproj
   ```

   The first run compiles and opens the window (leave the terminal open while
   using it).

3. **(Optional) Build a double-clickable .exe:**

   ```powershell
   dotnet publish src/PurpleStarNotes.csproj -c Release -r win-x64 --self-contained false -o publish
   ```

   Then double-click `publish\PurpleStarNotes.exe`. It shows the app icon and
   launches like any normal Windows program.

---

## Installing it like a real program (one-click installer)

The project ships an **Inno Setup** installer script
(`installer/PurpleStarNotes.iss`) that produces a single
`PurpleStarNotesSetup.exe`.

### Option A — let GitHub build it (recommended)

Push a tag like `v1.1.0` (see **"Publishing an update"**) and, a few minutes
later, a **Release** appears on GitHub with `PurpleStarNotesSetup.exe` attached.
Download and run it — no local tools required.

### Option B — build the installer yourself

1. Install **Inno Setup** (free): <https://jrsoftware.org/isdl.php>
2. Publish a self-contained build (bundles the .NET runtime):

   ```powershell
   dotnet publish src/PurpleStarNotes.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish
   ```

3. Compile the installer:

   ```powershell
   & "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\PurpleStarNotes.iss
   ```

4. The installer is at `installer\Output\PurpleStarNotesSetup.exe`.

Running it installs per-user (no admin prompt), adds Start-menu and optional
desktop shortcuts, and registers an entry in **Settings → Apps**.

---

## Updates: going from the repository to an installed update

Updates flow through **GitHub Releases**:

```
  edit code -> bump version -> push a tag (vX.Y.Z) -> GitHub Action builds the
  installer and publishes a Release -> the app's "Check for updates" button
  installs it
```

### Publishing an update (you, the developer)

1. Make your changes.
2. Bump the version in **`src/PurpleStarNotes.csproj`** (`<Version>1.2.0</Version>`).
3. Commit and push.
4. Create and push a matching tag:

   ```powershell
   git tag v1.2.0
   git push origin v1.2.0
   ```

   (Or, from the repo's **Actions** tab, run **Build and Release** and type the
   version — handy if tag pushes are blocked in your environment.)

5. The **Build and Release** workflow compiles a self-contained build, builds
   `PurpleStarNotesSetup.exe`, and publishes a GitHub Release named `v1.2.0`
   with the installer attached.

### Updating from inside the app (your users)

The **Check for updates** link at the bottom-left compares the running version
against the latest Release and, if newer, downloads and runs the installer — the
app closes, upgrades in place, and relaunches on the new version.

> **Important:** the in-app updater reads the repository's Releases
> *anonymously*, so this works only if the repository is **public**. If it is
> private, GitHub won't let the app read or download the release.

---

## Using the app

- **New note:** the round purple **+** button (top-left).
- **Rename:** click the title at the top of the right pane and type.
- **Tags:** click **"Link tags, notes, files..."**, type a tag (nested like
  `Games/EU4`), press **Enter**. Remove with the ✕ on a chip.
- **Paragraph format:** the leftmost toolbar button opens the Formatting menu
  (Normal, Headings, Lists, Check list). Its icon reflects the current format.
- **Check lists:** click a checkbox to tick and cross out the item; click again
  to restore it.
- **Text size:** select text and use **−** / **+** to nudge it a point at a time.
- **Color:** the color button opens the Windows color picker; the clear button
  resets to the theme default.
- **Alignment / indent:** the alignment dropdown and the outdent / indent
  buttons on the right of the toolbar.
- **Delete a note:** the trash-can button at the top-right of the editor.
- **Light / dark:** the sun / moon button at the bottom-left.

## Project layout

```
src/
  PurpleStarNotes.csproj  Project file (.NET 8, WPF + WinForms, version, icon)
  App.xaml / .cs          Startup + themed brushes and control styles
  Theme.cs                Dark/light palettes and runtime theme swapping
  Settings.cs             Persists the chosen theme
  MainWindow.xaml / .cs    The two-pane window, toolbar, and editor logic
  Note.cs                 Note model (incl. per-category font sizes)
  NoteStore.cs            Loads/saves notes to %AppData%\PurpleStarNotes
  Updater.cs              "Check for updates" via the GitHub Releases API
  app.ico                 The application icon
installer/
  PurpleStarNotes.iss     Inno Setup script -> PurpleStarNotesSetup.exe
.github/workflows/
  release.yml             Builds the installer + publishes a Release on a tag
```

## Troubleshooting

- **`dotnet` is not recognized** — install the .NET 8 SDK, then open a fresh
  terminal.
- **Build errors about `net8.0-windows`** — WPF is Windows-only; build and run
  on Windows.
- **Icons look like boxes** — the toolbar uses the *Segoe MDL2 Assets* font that
  ships with Windows 10/11; it isn't present on older systems.
