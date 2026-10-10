# NexaAsset Editor v2 — presentation update

## Scope

This branch adapts the supplied Nexa Dark reference to the existing AIR/Flex application. It changes branding, component skins, typography, spacing, icons, preview presentation and panel layout. It does not replace the editor or remove its original implementation.

The asset parsers, storage classes, workers, commands, serialization, file-format options and progress algorithms are unchanged. The AIR application ID, settings format and file associations are retained for compatibility. Following the explicit branding request, the executable is **NexaAsset Editor v2.exe** and the packaged SWF is **NexaAsset-Editor-v2.swf** (the AIR content path does not accept spaces). The existing AIR version number is not changed. The original New Window command resolves the renamed executable.

## UI integration

- Nine navigation entries use the supplied SVG icons. Category entries select the original item/outfit/effect/missile categories; Home, Objects and Sprites only change the visible workspace. Preferences opens the original dialog.
- The action bar calls the existing open, compile, compile-as, export, new, duplicate, remove, copy and paste handlers. Enabled states reflect the existing document, selection and object clipboard state.
- The original toolbar remains available through Tools, including its original utilities. Native menus, shortcuts, confirmations and context menus are retained.
- The four resizable areas contain the original file information, preview, object lists, editor and sprite lists. Search opens the existing Finder rather than introducing another search implementation.
- Texture, Properties and Attributes retain their fields and event handlers. Advanced controls remain in scrollable areas. Attribute templates use vertically arranged captions to avoid overflow.
- Preview interpolation is disabled for crisp pixel art. The underlying pixels, zoom calculations, offsets and sprite transforms are unchanged.

The mockup's "Open OTB" and "Import DAT" labels are represented by **Open Asset Files** and **Compile As**. Those captions describe the commands the current application actually supports; no new import workflow is implied. Native window chrome is preserved.

## Files

New presentation components and native skin contracts are under `src/ob/ui/`. Original UI integration is in `src/ObjectBuilder.mxml` and `src/ObjectBuilder.css`. Additional presentation adjustments affect the information panel, preview, list/grid renderers, texture/attribute panels, toolbar, splash screen and selected dialog backgrounds. New UI strings are provided in English, Portuguese and Spanish.

Supplied artwork and tokens are retained under `assets/nexa/`, including their license. Transparent SVG bounds preserve the complete icon viewport during native Flex embedding. Icons are compiled into the application; no runtime network access or additional library is required.

## Validation

The original release build and existing tests passed before the presentation changes. Validation commands are:

```powershell
./build.ps1 -CompileOnly
./tests/run.ps1
./tests/run-ui.ps1 -RealAssets '<path to a test copy containing Tibia.dat and Tibia.spr>'
```

The existing suite checks standard/multi-SPR I/O, RGB/RGBA detection, extended counts, publication rollback, close cycles, feature warnings and progress-label layout. These tests pass with the new theme.

The new harness subclasses the actual application, loads a retained 8.60 test copy with 485,804 sprites and selects item 54600. It captures live controls at logical 1366×768, 1600×900 and 1920×1080 sizes, including Portuguese/English/Spanish labels and all three editor tabs. It checks panel minimum sizes, bounded visible list renderers, clipboard gating and repeated navigation through actual navigation-button events without changing selection or the document's dirty state. Captures are saved in `bin-tests/nexa-layout-*`.

Additional captures render at 1.25× and 1.5×. They check enlarged vector/text presentation, **not actual Windows DPI configuration**. The screenshots show the real client-area display list rather than a generated mockup. Some legacy labels are constructed at startup and require restarting after a language change; this update does not redesign that lifecycle.

## Limitations

The application-subclass harness logs two worker-bootstrap diagnostics (`RegisteredCallbacksCommand` and `BackgroundWorkerStartedCommand`) and a `ThingData.category` binding diagnostic while its load and UI assertions pass. Their origin has not been established by a baseline subclass comparison. Worker/library protocol changes are outside this layout-only request. No error-free-runtime or leak-free claim is made.

The harness does not establish long-duration memory stability, uninterrupted scrolling performance, physical-monitor DPI behavior, or every destructive editing workflow. Parser tests and unchanged command source support compatibility, but they do not substitute for those additional interactive checks.

This work remains on `feat/nexaasset-editor-v2-dark-ui`; the user has requested a commit and push on this branch. No merge to `main` or new pull request is included. Separate local bundles preserve the previous executables.

## Initial local delivery — 10 October 2026

- Bundle: `bin/NexaAsset-Editor-v2-ui-20261010/ObjectBuilder.exe`. Keep the entire bundle directory together, including its runtime and configuration files.
- The packaged SWF matches the final release build. SHA-256: `8CFC1EE4A3269A2C9BB067BD1DDCE95D230C944407C7EFD10B3180A710118031`.
- Final live-control captures: `bin-tests/nexa-layout-1791668108715/`.
- Final asset/window/progress suite: `bin-tests/run-236302ade7f44311bf1a799527fda297/` and `bin-tests/windows-1791667976412/`; all assertions passed. The intentionally induced file-publication failure is the rollback test, not an unexpected error.

## Dark dialogs and application branding follow-up

The shared modal skin now supplies dark surfaces while retaining the original title, drag area, close button, content and control-bar skin contracts. Open, Merge, Compile, Preferences and utility footers no longer use the legacy gray/pixel-pattern backgrounds. The Compile dialog has a scrollable content area and a fixed, reachable action bar. Preferences uses dark native tabs. Log and error details use high-contrast dark surfaces; Portuguese and Spanish log/clipboard captions are complete. Slicer, animation, object-viewer and list-renderer changes are display colors only.

About and startup branding highlight **JokeCore**, link to `https://github.com/Mateuzkl`, explain the original Object Builder base and its adaptation to NexaMap, and summarize existing asset features. Promotional sponsor links are removed. Upstream attribution, the descriptor copyright and the original MIT license remain; the bundle includes `LICENSE`.

The application icon derives from the existing Nexa four-block vector mark. `scripts/generate-icons.ps1` reproducibly exports AIR PNG sizes and a Windows ICO without an image-editor dependency. AIR packaging uses the new icon and filename consistently; internal class names and the application ID remain unchanged to avoid unnecessary compatibility changes.

The UI harness additionally captures the expanded log, Open, Compile, two Preferences tabs, About and error details. It checks skin contracts, localized log captions, action-bar bounds and real close-button events. Native operating-system window chrome remains managed by Windows. The logical viewport and scaled-render limitations described above still apply; not every auxiliary tool workflow is interactively exercised.

The updated portable bundle is `bin/NexaAsset-Editor-v2-branding-20261010/NexaAsset Editor v2.exe`. Keep the entire bundle directory together, including its runtime, configuration and license files. Earlier bundles are retained. Packaging and validation records below describe the final delivery rather than replacing the historical initial delivery.

- Release compilation and x64 AIR bundle packaging succeeded. The packaged SWF matches `bin-debug/NexaAsset-Editor-v2.swf`; SHA-256: `8096C492B4F6EBD710C72CB40EDC5F9587AA604575F8DE335166C3F9A69DEAA4`.
- The icon extracted from the actual packaged executable is the cyan four-block mark. The bundle contains no `ObjectBuilder.exe` or `ObjectBuilder.swf` entry and includes the upstream `LICENSE`.
- `tests/run.ps1` passed: asset results are in `bin-tests/run-6806b0965e94468f9ea82ea5501e0a88/`, window warning captures in `bin-tests/windows-1791669750117/`; progress labels remain unclipped.
- `tests/run-ui.ps1` passed with the retained 485,804-sprite dataset. Final live-control captures are in `bin-tests/nexa-layout-1791669735272/`, including the localized log toggle and all seven dialog/log checks. The worker-bootstrap and category-binding diagnostics noted under Limitations remain visible in this harness.
