# PR #9 audit

## Recolor and MCP corrections

- Drawing consumes its pointer gesture instead of also starting Scroller panning. Stroke segments update the mask immediately; expensive color/bitmap updates are coalesced to display frames. Removed forced synchronous rendering on every mouse move. Stage/ENTER_FRAME handlers are removed on release, deactivation, texture change and close.
- Closing Recolor releases bitmaps, masks, source data and callbacks. Shared gateway consumers are tracked by the controller: only the last consumer suspends MCP, revokes provider consent and rotates its bearer.
- Recolor MCP timeouts explicitly report `EDITOR_TIMEOUT`. A missing response is an error, never a successful edit. This does not repair an already hung running binary; the updated application must be opened to load the fixes.
- Every storage invalidation participates in revision tracking, including already dirty projects. Art transactions batch their invalidations into one revision. Stale saves are rejected. Successful saves are acknowledged even if a later list/hash refresh fails (the refresh error is logged).
- Replace Current updates only mapped OTB sprite hashes. It preserves server flags/type; new items retain the existing synchronization path.
- Equivalent source/tile masks reuse staged SPR records. ID 65535 remains usable for non-extended SPR. Transparent non-Shift wand clicks clear the old selection.
- The runner resolves ToolsRoot before changing directories, forwards it to the gateway build, creates output directories, rebuilds the embedded worker and has an external watchdog.

## Evidence and limits

`tests/run-recolor.ps1` exercises 8x wand clicks, a burst of 200 pointer moves during brush drag, zoom limits through 10x, masks, HSL/alpha, all categories, tile order, templates, shared sprites, ID limits, rollback, compile/reopen, live MCP PNG/selection/HSL/conflicts and shared-consumer closure. The original 8x freeze did not reproduce with the synthetic fixture before the gesture/render changes; those changes address competing gestures and unbounded per-event redraw work, not a proven infinite loop. No claim is made that every possible source or driver was tested.

`RecolorGatewayTests` verifies an unresponsive editor produces a useful MCP timeout. The core/gateway suites also cover histories, session switching and publication approval. No desktop-control automation is needed or used for these tests.

## Final integration audit

- `tests/run-recolor.ps1 -RealAssets <792-folder> -RealOutfitId 1460` passed against the supplied Psychotick DAT/FileParts project. In an isolated test editor it exercises wand, brush, eraser and rectangle at 8x and 10x, with 3,600 move events. An independent official MCP client then reads the real PNG preview. The project remains unmodified; the harness never applies or compiles those source assets. Local result: `bin-tests/recolor-real-result.txt` (ignored).
- `tests/run-mcp.ps1` passed after integrating breathing: core/domain tests, real HTTP MCP tools, AIR manual selection capture/generation, history, modern/legacy Apply and Save/Compile, and eight application closing scenarios. A C#-generated 64x64 breathing cycle was sent through the 8.60 legacy adapter, compiled and reopened; every tile/direction/phase was checked. Variable timings were rejected without mutation; explicit 100 ms succeeded. This tests storage, not in-game idle playback.
- Fifty Studio open/close cycles ran for each format. Final measured managed-memory deltas were -376,832 bytes (modern) and +8,228,864 bytes (legacy); peak AIR working set was 274,243,584 bytes. These measurements do not prove zero leaks. The audited handlers/timers/native-process callbacks have explicit cleanup, and only one snapshot request may be in flight.
- Production compilation passed with `build.ps1 -CompileOnly`. Review the packaged executable for the latest implementation; an already running older application cannot receive these fixes via MCP.

New production paths are `BreathingGenerator.cs`, `BreathingWindow.mxml`, the existing Studio service/tool dispatch and the Pixel Art Studio action. UI and MCP both use `ArtDocument.Edit`, one history transaction and the same deterministic engine. No separate editor, Python runtime, remote auto-apply, or replacement of the existing offset generator was introduced. [Usage and compatibility](BREATHING.md).

Reproducible final full-suite output: `bin-tests/studio-3fafebb3dea8485baf95f38ed156c625/` (ignored; result, memory samples, manual UI captures and closing reports). Core tests also generate asymmetric/biped PNG fixtures for visual review. The source creature supplied with the Python prototype was not available as a standalone PNG, so no visual claim is made for that specific prototype.

Review sources: [Sourcery and CodeRabbit on PR #9](https://github.com/Mateuzkl/NexaAsset-Editor-v2/pull/9). Qodo posted a billing pause, not an actionable code review. Findings were checked against the current code, rather than accepted automatically.
