# Source adapter probe — 2026-10-21

Environment: Windows 10.0.26200, win-x64, .NET SDK 10.0.402 installed into the worktree's ignored `.tools` directory. All game files in this probe were written to a fresh directory under the OS temporary folder, never to the user's PCL directory.

## Observed

- `dotnet test PclVersionBridge.sln`: the initial one-test template baseline passed.
- Official `version_manifest_v2.json` reported release `26.3` on this date. CmlLib.Core 4.1.0 listed this and newer snapshots.
- `MinecraftLauncher.InstallAsync("26.3")` with CmlLib.Core defaults made 5,634 file tasks. Its first run stopped after about 3,001 tasks with `HttpRequestException` caused by a TLS unexpected EOF. A second run against the same temporary root completed; the library reused valid files.
- `MinecraftLauncher.ExtractFiles("26.3")` then yielded 5,634 files; every entry had a SHA-1 and URL. Independent SHA-1 checks of all 5,634 staged files found **zero mismatches**.
- Download file hosts observed in the extraction plan: `libraries.minecraft.net`, `piston-data.mojang.com`, `resources.download.minecraft.net`. Metadata and asset index came from Mojang hosts. The saved `versions/26.3/26.3.json` SHA-1 matched the official version manifest; `assets/indexes/34.json` matched the version JSON's asset index SHA-1.
- Default CmlLib configuration also downloads a Java runtime into `runtime/`. The product will disable that extractor because PCL handles Java and the spec only permits game version files, libraries and assets.
- Source review of CmlLib.Core's default launcher shows a cancellation token on `InstallAsync`, and that it checks existing files against hashes. Its download helper may leave a partial file after network failure, so the product must stage and verify independently before any commit. Actual cancellation behavior is still a required test.

## Decision

Use CmlLib.Core 4.1.0 behind an adapter for the technical MVP, with a bounded host allowlist, a separate official release catalog, independent hashes for the saved version JSON, asset index and all extracted files, and staged retry/cancel handling. The first network failure shows why a single library success must not be treated as product reliability evidence. Package license is MIT per its repository `LICENSE`; include notice if the package ships.

This probe does **not** establish PCL 2.99 recognition, clean-Windows behavior, or permission to publish a mainland downloader.
