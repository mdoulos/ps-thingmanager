# Changelog

All notable changes to Purple Star Notes. The app reads this file to build the
in-app changelog (Menu → Changelog).

**Adding changes:** put your notes as `-` bullets under `## Unreleased` below.
When your PR merges to `main`, CI stamps them with the next version and date,
updates the app version, and publishes a release automatically — a patch bump
by default, or add `#minor` / `#major` to the merge commit for a larger bump.
You can instead name the version yourself with a `## vX.Y.Z — YYYY-MM-DD`
heading. Newest version first.

## Unreleased

## v1.1.7 — 2026-07-24
- Added a menu (hamburger) button next to Check for Updates with a Changelog view.
- The changelog opens read-only in the main window with an Exit Changelog button.
- Moved the light/dark toggle between Check for Updates and the menu button.
- Tightened the checkbox toggle area so it no longer extends past the box.

## v1.1.6 — 2026-07-24
- Fixed check lists not showing a checkbox when converting text to a check list.
- Widened the checkbox click area and tightened check-list spacing.
- Enter continues a check list; Enter on an empty item leaves the list.
- The sidebar now shows a note's tags under the modified date.
- Added a Cancel button to the Description popup.

## v1.1.4 — 2026-07-20
- Upgrade-friendly installer that keeps your notes between updates.
- Added tag filtering in the sidebar.
- Bigger, easier-to-click checkboxes and general icon polish.

## v1.1.2 — 2026-07-16
- Fixed a startup crash caused by older check-list notes.
- Added a global crash handler for a friendlier failure.

## v1.1.0 — 2026-07-12
- Renamed the app to Purple Star Notes.
- Overhauled the editor, theming, and formatting toolbar.
- Added headings, lists, alignment, text color, and font sizing.
