# Modpack discovery implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline, TDD for behavior changes, then one independent review.

**Goal:** Accurate searchable modpack cards with source images and a simple source-attributed details view.

**Architecture:** Extend existing catalog DTOs compatibly; immutable submitted search requests; bounded CDN bytes and native WPF image cache/control; native details dialog.

**Tech Stack:** Existing .NET 10/WPF and HttpClient; no new dependency.

**Spec:** docs/superpowers/specs/2026-10-23-modpack-discovery.md

## Constraints / review focus

Preserve existing installer and guards. Do not invent author text or imagery. Check stale requests, draft edits during paging, missing source fields, untrusted image redirects/oversize/decode failures, title click vs project selection, minimum-size layout and cancellation.

### Task 1: Source records and catalog

- [x] Write/run failing tests for sort, source icon and description, details identity/type, safe media URL, and MC-filtered versions.
- [x] Extend ModpackModels, ModrinthCatalog and bounded image retrieval in ModpackNetwork. Preserve existing constructor/call compatibility.
- [x] Run all tests and record evidence: 95 core tests, 0 failures.

### Task 2: Submitted criteria and UI

- [x] Write/run failing VM tests for request snapshots, filter invalidation, failed-search cleanup and paging boundaries.
- [x] Implement VM criteria/notifications; integrate sortable illustrated rows, Enter search, explicit details entry.
- [x] Add bounded cached image control and source-attributed native details window. Image errors are nonfatal.
- [x] Build WPF, exercise actual catalog/media and render known source data at default/minimum sizes.

### Task 3: Review and delivery

- [x] Fresh independent review; tall-thumbnail finding RED→GREEN; full suite and native decode/cache checks green. Minor interaction findings documented in verification results.
- [x] Update privacy and verification, produce Windows x64 local preview with licenses and truthful limits; preserve unrelated changes. Package: `.artifacts/VanillaReach-Modpack-Discovery-preview.zip` (61,148,486 bytes); SHA256 `E579606E8DF82D92AC47A0E95AA89DF1F8F117A7550E95E6346ADBD0EFB8A5DE`. Final checks: 95 core tests passed, native WPF checks passed.
