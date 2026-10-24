# Kimi UI refinement implementation plan

> **For agentic workers:** Use superpowers:executing-plans for inline execution.

**Goal:** Apply the desktop Kimi review to the existing WPF interface.

**Architecture:** Shared native styles, compact common header/directory, bounded tab content and fixed footers.

**Tech Stack:** Existing .NET 10 WPF; no new dependencies.

**Spec:** docs/superpowers/specs/2026-10-23-kimi-ui-refinement.md

## Constraints and review focus

Preserve bindings, handlers, DirectoryPanel/Pages/PackPage/VanillaFooter names, cancellation and installation guards. Check minimum size, long status/error text, keyboard focus and disabled form/live cancel. Preserve existing uncommitted work.

- [x] Merge Styles.xaml from App.xaml; share semantic brushes and native control styles.
- [x] Compact MainWindow directory and title; bound each tab with a Grid and fixed footer.
- [x] Group ModpackView into three sections with an independently scrolling body.
- [x] Build Release into .artifacts/ui-kimi-check: zero warnings and errors.
- [x] Run the existing test suite: 80 passed. No implementation-mirroring markup tests added.
- [x] Inspect WPF layout renders at default/minimum sizes and fixed-footer scrolling; record the native helper activation failure and pending keyboard checks in docs/verification/kimi-ui-results.md.
- [x] Deliver .artifacts/VanillaReach-Kimi-UI-preview.zip (61,133,251 bytes; 16 entries, includes executable and licenses). State pending keyboard/hover and full modpack install verification.
