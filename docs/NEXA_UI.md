# NexaAsset Editor v2 — presentation update

## Scope

This branch adapts the supplied Nexa Dark reference to the existing AIR/Flex application. It changes branding, component skins, typography, spacing, icons, preview presentation and panel layout. It does not replace the editor or remove its original implementation.

The asset parsers, storage classes, workers, commands, serialization, file-format options and progress algorithms are unchanged. The AIR application ID, executable filename, settings format and file associations are retained for compatibility. The displayed product name is **NexaAsset Editor v2**; the existing AIR version number is not changed.

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

This work remains on `feat/nexaasset-editor-v2-dark-ui`. No merge, commit, push or pull request is performed without a new explicit request. A separate local bundle preserves the previous executable.

## Local delivery — 10 October 2026

- Bundle: `bin/NexaAsset-Editor-v2-ui-20261010/ObjectBuilder.exe`. Keep the entire bundle directory together, including its runtime and configuration files.
- The packaged SWF matches the final release build. SHA-256: `8CFC1EE4A3269A2C9BB067BD1DDCE95D230C944407C7EFD10B3180A710118031`.
- Final live-control captures: `bin-tests/nexa-layout-1791668108715/`.
- Final asset/window/progress suite: `bin-tests/run-236302ade7f44311bf1a799527fda297/` and `bin-tests/windows-1791667976412/`; all assertions passed. The intentionally induced file-publication failure is the rollback test, not an unexpected error.
