# PCL Version Bridge MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and verify a Windows tool that installs a complete vanilla Minecraft Java Edition release into an isolated `.minecraft` directory for use with PCL 2.99.

**Architecture:** A WPF shell calls a core coordinator. The core obtains an official release manifest, asks a source adapter to stage files, verifies every required file, and merges only verified files into the selected `.minecraft`; the version directory is committed last. CmlLib.Core is evaluated as the source adapter before adoption.

**Tech Stack:** .NET 10, WPF, CmlLib.Core if its current behavior passes the probe, xUnit; Windows self-contained publish.

**Spec:** `docs/superpowers/specs/2026-10-21-pcl-version-bridge-design.md`

## Global Constraints

- Windows-only first release; simplified Chinese UI.
- Vanilla release versions only. No accounts, launch, loaders, mirrors, repair, or update service.
- Never write test output to the user's current PCL `.minecraft`.
- Never bundle game files or silently overwrite existing target files.
- Public mainland distribution requires the spec's independent release gate; technical test success is insufficient.

## Review Focus

- A version ID or manifest path containing `..`, rooted syntax, or alternate separators must never escape staging or target.
- A junction/symlink inside the target tree must not redirect writes outside it.
- A target file that changes between preflight and commit must cause a conflict, not an overwrite.
- Cancellation between shared-file merge and version-directory commit must leave existing files intact and no visible incomplete version.
- A corrupt downloaded object or missing expected hash must fail before any target writes.

---

### Task 1: Toolchain and source-adapter feasibility

**Files:** Create `src/PclVersionBridge.Core/PclVersionBridge.Core.csproj`, `tests/PclVersionBridge.Tests/PclVersionBridge.Tests.csproj`, `docs/verification/source-probe.md`.

**Interfaces:** Produces a .NET 10 solution and a documented go/no-go decision for CmlLib.Core's vanilla install, official hosts, progress, cancellation, hashes, and current release support. No user game directory may be passed to the probe.

- [ ] Install or locate the official .NET 10 SDK, then run `dotnet --info`; record the actual SDK version.
- [ ] Create a minimal solution, core project, and xUnit project; run `dotnet test` to verify the baseline.
- [ ] Write a controlled probe targeting a new temporary `.minecraft`, run it against the current release manifest, and record CmlLib.Core's actual behavior and license version. If any source or hash behavior fails the spec, reject the adapter and document a direct official-manifest implementation choice before Task 3.
- [ ] Commit the toolchain files and probe report.

### Task 2: Target-path safety and install models

**Files:** Create `src/PclVersionBridge.Core/Paths/GamePathValidator.cs`, `Models/VersionChoice.cs`, `Models/InstallProgress.cs`; test `tests/PclVersionBridge.Tests/GamePathValidatorTests.cs`.

**Interfaces:** `GamePathValidator.ValidateRoot(string candidate): string` returns a canonical absolute `.minecraft` root or throws `InstallException`; `ValidateRelative(string relative): string` accepts only paths below it. `VersionChoice(string Id, Uri ManifestUrl, string ManifestSha1)`; `InstallProgress(string Stage, long Completed, long Total, string? CurrentFile)`.

- [ ] Write failing tests for valid empty root, invalid root name, traversal/rooted version IDs, and symlink/junction redirection, including Review Focus entries 1–2.
- [ ] Run targeted tests; confirm failures.
- [ ] Implement the models and path checks, then run targeted and whole-suite tests.
- [ ] Commit.

### Task 3: Official release catalog and staging adapter

**Files:** Create `Catalog/VersionCatalog.cs`, `Download/IInstallSource.cs`, `Download/CmlInstallSource.cs` or `Download/OfficialManifestInstallSource.cs`, `Download/StagedVersion.cs`; tests `VersionCatalogTests.cs`, `InstallSourceTests.cs`.

**Interfaces:** `VersionCatalog.ListReleasesAsync(CancellationToken): Task<IReadOnlyList<VersionChoice>>`; `IInstallSource.StageAsync(VersionChoice version, string stagingRoot, IProgress<InstallProgress> progress, CancellationToken cancellationToken): Task<StagedVersion>`; `StagedVersion` carries version ID, staging root, and required paths with expected SHA-1 and size.

- [ ] Write failing fixture-backed tests for release filtering, malformed manifest, non-official URL/redirect, missing hash, corrupted object, and cancellation; cover Review Focus entry 5.
- [ ] Run targeted tests; confirm failures.
- [ ] Implement the catalog and chosen source adapter using only the verified official endpoints; stage into a new temporary root and verify all required files before returning.
- [ ] Run targeted and whole-suite tests. Commit.

### Task 4: Safe commit and retry semantics

**Files:** Create `Install/InstallCommitter.cs`, `Install/InstallJournal.cs`, `Install/InstallCoordinator.cs`; test `InstallCommitterTests.cs`, `InstallCoordinatorTests.cs`.

**Interfaces:** `InstallCommitter.CommitAsync(StagedVersion staged, string targetRoot, CancellationToken cancellationToken): Task`; `InstallCoordinator.InstallAsync(VersionChoice version, string targetRoot, IProgress<InstallProgress> progress, CancellationToken cancellationToken): Task`. The coordinator validates, stages, verifies, commits, and reports stages.

- [ ] Write failing tests for same-hash reuse, conflicting existing bytes, target changed after preflight, interrupted merge, cancelled install, pre-existing version, low space, and permission failure; cover Review Focus entries 3–4.
- [ ] Run targeted tests; confirm failures.
- [ ] Implement no-overwrite writes for shared files and last-step atomic version-directory commit. Journal only files created by this run; never remove prior user files.
- [ ] Run targeted and whole-suite tests. Commit.

### Task 5: Single-window WPF flow

**Files:** Create `src/PclVersionBridge.App/PclVersionBridge.App.csproj`, `App.xaml`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `MainViewModel.cs`; test `MainViewModelTests.cs` in the existing test project.

**Interfaces:** The view model exposes selected directory/version, release list, current stage, progress, user-readable error, install/cancel/retry commands. The UI delegates all file writes to `InstallCoordinator`.

- [ ] Write failing view-model tests for initial directory suggestion, explicit target confirmation, offline catalog, install progress, cancel and retry.
- [ ] Run targeted tests; confirm failures.
- [ ] Implement the Chinese single-window flow and use native folder picker. Ensure controls remain responsive during downloads.
- [ ] Run tests and launch the window locally. Commit.

### Task 6: End-to-end evidence and distribution candidate

**Files:** Create `README.md`, `docs/verification/mvp-results.md`, `.github/workflows/build.yml`; adjust source/tests only for observed failures.

**Interfaces:** Deliver a buildable local artifact and an evidence table that separates passed, failed, and untested criteria from the spec. Do not publish a GitHub Release while the mainland publication gate is unresolved.

- [ ] Run the full test suite repeatedly after a clean build; record commands and counts, then test in a temporary empty and populated `.minecraft`.
- [ ] Verify a current vanilla release's complete files and PCL 2.99 recognition against an isolated target; record whether actual game launch can proceed without PCL downloading.
- [ ] Build `win-x64` self-contained package, start it on Windows, inspect dependencies, and document any clean-machine test that could not be run.
- [ ] Write README with installation steps, limits, unofficial disclaimer, and no game-file redistribution; run CI locally where possible.
- [ ] Review the final diff and commit the evidence and packaging files.
