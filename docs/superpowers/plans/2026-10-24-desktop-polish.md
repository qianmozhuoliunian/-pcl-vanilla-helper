# Desktop Polish Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline. Keep the existing isolated worktree and preserve unrelated publication files.

**Goal:** Deliver a coherent macOS-inspired light WPF desktop UI without changing installer or social service behavior.

**Architecture:** Semantic shared resources and native control templates in Styles.xaml; sidebar selection synchronizes the existing Pages TabControl and keeps its install gate. Existing views receive consistent grouping and typography.

**Tech Stack:** Existing .NET10 WPF, no dependencies or downloaded branding assets.

**Spec:** docs/superpowers/specs/2026-10-24-desktop-polish-design.md

## Global constraints

- Preserve existing bindings, event handlers, confirmation and installation locks; no Core/backend behavior edits.
- Keep standard Windows title bar and system fonts, no copied Apple assets or fake status.
- Explicit staging only; existing uncommitted publication work remains separate. No push/deploy.

## Review focus

1. Navigation/account pages cannot bypass the shared installation gate.
2. ComboBox popup/item selection and text/password entry preserve native semantics and keyboard focus.
3. Minimum window and scaled render keep primary actions visible and long text scrollable.
4. All account forms, source detail and invitation views share the theme without hiding source/error notices.
5. Resource lookup/construction must work before any window is loaded and no test triggers game writes.

### Task 1 — Theme and shell

- [x] Add native navigation regression and render harness in tests/PclVersionBridge.UiChecks/DesktopPolishChecks.cs; observe missing navigation RED. Assert all3 pages, hidden directory on friends, gate blocks navigation then unlocks. Avoid game/network mutations.
- [x] Implement shared brushes, card/input/button/select/list/tab templates in src/PclVersionBridge.App/Styles.xaml. Update MainWindow.xaml shell plus minimal navigation lock wiring in MainWindow.xaml.cs.
- [x] Run native harness GREEN. Inspect real WPF images at default/minimum sizes, normal/125%/150% scale.

### Task 2 — Page consistency

- [x] Group ModpackView.xaml into source search, exact version check and instance cards; retain every binding and handler.
- [x] Apply shared card/font/button treatments to FriendsView.cs, FriendDialog.cs, InvitationDetailsWindow.cs, ModpackDetailsWindow.xaml and ProjectImage.cs, without changing data/actions.
- [x] Render connected and unconfigured friend page, source details, password/input/button states. Check popup/item selection and visible focus.

### Task 3 — Verify and deliver

- [x] Run full existing Release tests and native checks, two-client integration to detect native resource regressions. One focused fresh review of UI change; fix material findings.
- [x] Publish to a new preview directory preserving the currently open kit. Build a complete ZIP with licenses and no credentials/data, inspect archive and launch new app.
- [x] Record tested scope/screenshots in docs/verification/desktop-polish-results.md and create explicit-path local checkpoint.
