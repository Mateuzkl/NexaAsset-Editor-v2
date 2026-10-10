# NexaAsset Editor v2

**A modern asset-editing workspace for NexaMap and compatible OTClient projects.**

Maintained by [JokeCore / Mateuzkl](https://github.com/Mateuzkl), NexaAsset Editor v2 builds on the original Object Builder editor rather than replacing its proven editing foundation. It combines a redesigned dark interface with practical improvements to asset loading, compilation, format validation and resource handling.

The codebase was imported from [punkice3407/ObjectBuilder](https://github.com/punkice3407/ObjectBuilder) at commit `b07f254`, retaining its Git history. The original MIT license, copyright notices and upstream attribution remain intact. This is an independently maintained adaptation, not an official upstream release.

[Interface documentation](docs/NEXA_UI.md) · [Reliability audit](docs/AUDIT_2026-10-10.md) · [MIT license](LICENSE)

## Capabilities

- Create and edit items, outfits, effects and missiles using the existing Object Builder workflows.
- Work with object properties, server attributes, sprite sheets, animation durations and frame groups.
- Import, export, duplicate, copy and replace objects; browse sprites and edit pixels.
- Use a cohesive Nexa Dark workspace, resizable panels, pixel-art previews and accessible editing tools.
- Read standard DAT/SPR assets and indexed multi-SPR archives.
- Choose the output format independently of the input: a standard SPR archive or a multi-SPR generation.
- Compile legacy 16-bit or extended 32-bit sprite counts; optionally use the custom 32-bit DAT item-count extension.
- Monitor loading and compilation progress with processed counts, percentages and estimated remaining time.
- Check detected format options before loading, with blocking mismatch alerts and advisory warnings where detection is uncertain.

Some tools and translations are inherited from upstream. The NexaMap adaptation does not imply that every custom client format or server extension is interchangeable.

## Formats and compatibility

| Format | Description |
| --- | --- |
| Standard DAT + SPR | Object metadata and a single sprite archive, with legacy or extended sprite IDs. |
| DAT + multi-SPR | Object metadata plus `spr_parts.dat`, `spr_index.dat` and split SPR files. Uses the supported SPMT/SPIX layout, with up to 65,535 sprites per part. |
| OTFI | Companion format settings, including transparency and supported extended options. |
| Extended DAT item IDs | Custom 32-bit item-count encoding. The consuming client must support it; this is not a universal legacy-client format. |

Choose **Open Asset Files** to select the input, then **Compile As** to select the output. A multi-SPR input can be compiled into a standard archive, and a standard input can be split into multi-SPR. The input files are not themselves an output-format restriction.

Compatibility depends on the DAT signature, metadata controller, sprite encoding and selected options—not solely on a version label. Consult `src/config/versions.xml` for the configured signatures. The tests include a retained 8.60 project and synthetic boundary fixtures; they do not certify every configured version or custom archive.

### Data protection

Completed project generations are staged before publication. Replaced generations are retained in a `.ob-backup-*` directory, and ordinary publication failures trigger rollback. Invalid offsets, truncated records, invalid RLE and unsupported count limits are rejected.

These safeguards are not a substitute for an independent backup. Publication is not guaranteed to be atomic across a power loss, and in-memory merge/bulk-edit operations are not all-or-nothing transactions. A failed import may leave successfully appended sprites or earlier completed replacements in the current document; it must not silently substitute preview error icons or retain foreign sprite IDs. Review the document before saving.

When a feature alert appears, verify **Extended**, **Transparency**, **Improved animations**, **Frame Groups** and any item-count extension against the intended client. Ambiguous samples are advisory, not proof of a format.

## Building on Windows

The project uses ActionScript 3, Apache Flex and the HARMAN AIR SDK. It is not the separate Rust Object Builder implementation.

The locally used toolchain is Apache Flex 4.16.1 with HARMAN AIR SDK for Flex 51.2.2.7, a portable Temurin JDK 17, Node.js and asconfigc 1.9.0. Prepare the SDK under the supplier's license terms; SDKs and runtimes are not distributed as source dependencies in this repository.

Keep the toolchain beside the checkout, including on a secondary drive:

```text
workspace/
  ObjectBuilderV2/                   # checkout; its directory name may differ
    build.ps1
    asconfig.json
  ObjectBuilder-tools/
    flex-air/                        # prepared Flex + AIR SDK
    java/<jdk-directory>/bin/java.exe # exactly one extracted JDK
    node/node_modules/asconfigc/
    tmp/
```

Install the build helper with a local cache:

```powershell
npm.cmd install --prefix ..\ObjectBuilder-tools\node --cache ..\ObjectBuilder-tools\npm-cache --ignore-scripts --no-audit --no-fund asconfigc@1.9.0
```

Compile and package from the checkout:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Useful options:

```powershell
./build.ps1 -CompileOnly
./build.ps1 -ToolsRoot 'D:\ObjectBuilder-tools' -BundleName 'NexaAsset-review'
```

The default executable is `bin/NexaAsset Editor v2/NexaAsset Editor v2.exe`. Keep the entire bundle directory together: the executable requires its captive AIR runtime, application files, configuration and license. `-BundleName` creates a separate output directory so earlier builds can be retained. Use `-PackageOnly` only after compiling the intended sources.

The helper uses process-local Java and temporary-directory settings, restores them afterward and does not modify the global PATH. The local package's temporary self-signed AIR certificate is not a trusted Windows publisher signature. Preferences may still be stored in Windows AppData.

The AIR application ID and internal ObjectBuilder class names remain unchanged for compatibility; the user-facing name and executable are **NexaAsset Editor v2**.

## Validation

Run the asset, format-warning, progress-layout and alert tests:

```powershell
./tests/run.ps1
```

For real assets, supply a **test copy**, never the only copy of a production project:

```powershell
./tests/run.ps1 -RealAssets 'D:\asset-test-copy'
./tests/run-ui.ps1 -RealAssets 'D:\asset-test-copy'
```

The real-assets runners expect `Tibia.dat` plus either `Tibia.spr` or `spr_index.dat` and its companion parts/settings. Generated test output is stored under ignored `bin-tests/` directories.

Coverage includes count boundaries, RGB/RGBA conversion, standard/multi-SPR round trips, invalid records, Windows file locks, rollback, close cycles, import failures, clipboard synchronization, Save state, actual instantiated list renderers and dark confirmation callbacks. The real-data test validates every SPR record and compares its compressed payload, in addition to sampled decoded pixels and DAT reopen checks.

See the [audit report](docs/AUDIT_2026-10-10.md) for measured results, known diagnostics and the limits of this validation. Neither a successful build nor a short test run establishes an absolute absence of memory leaks, corruption or runtime defects.

## Development principles

Retain the original editing functionality while improving presentation, correctness and maintainability. Changes should be supported by a reproduced issue or a regression test; apparent dead code should not be removed solely because a text search finds no direct caller. MXML bindings, registered worker commands and library callbacks can be indirect entry points.

ActionScript objects are garbage-collected, but file streams, bitmaps, timers and event subscriptions still require careful ownership and lifecycle handling. Cleanup work focuses on those resources rather than introducing C++ smart-pointer patterns into a managed-language application.

## License and attribution

Distributed under the [MIT license](LICENSE), preserving the original Object Builder notices and contributor attribution. Nexa branding and adaptation are maintained by [JokeCore](https://github.com/Mateuzkl). Included artwork retains its associated license notices.
