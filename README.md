# NexaAsset Editor v2

**A modern asset-editing workspace for NexaMap and compatible OTClient projects.**

Maintained by [JokeCore / Mateuzkl](https://github.com/Mateuzkl), NexaAsset Editor v2 builds on the original Object Builder editor rather than replacing its proven editing foundation. It combines a redesigned dark interface with practical improvements to asset loading, compilation, format validation and resource handling.

The codebase was imported from [punkice3407/ObjectBuilder](https://github.com/punkice3407/ObjectBuilder) at commit `b07f254`, retaining its Git history. The original MIT license, copyright notices and upstream attribution remain intact. This is an independently maintained adaptation, not an official upstream release.

[Interface documentation](docs/NEXA_UI.md) · [Reliability audit](docs/AUDIT_2026-10-10.md) · [MIT license](LICENSE)

### Pixel Art Studio and MCP (feature branch)

NexaAsset version **1.0.0** includes **Pixel Art / MCP** in the **Sprites** panel. Draw real pixels, edit paint
layers and animation cells, save editable `.nexaart` documents, and import/export
PNG/spritesheets. An authenticated local MCP client shares those drafts with the
manual canvas. Remote commit requests require the user's **Apply as NEW asset**
action; the existing Save/Compile flow persists the result in legacy or modern
format. MCP starts disabled. Existing objects and outfit masks are preserved.

The Studio also offers optional local Stable Diffusion text/variation through the
AUTOMATIC1111/Forge API, from the UI or MCP. Install the server/checkpoint separately;
the integration tests use a local stub and do not certify GPU inference quality.

Build with `./build.ps1`. Run the new end-to-end suite with `./tests/run-mcp.ps1`;
it launches the real AIR editor plus an independent official MCP SDK client and
checks drawing, human approval, stored pixels and Save/Compile in both formats.

### Native modern assets in the standard workspace

Use **Open Asset Files** to open a modern client folder containing
`catalog-content.json`, its explicitly catalogued appearances protobuf file,
and CIP/LZMA-compressed BMP spritesheets. Objects and real 32/64-pixel sprites
appear directly in the existing list/grid panels. The standard category tabs,
selection, preview zoom, inspector, frame/group/direction controls and ID/name
search use the native source. **No DAT/SPR conversion or second browser is
required to view modern assets.** DAT/SPR and multi-SPR retain their legacy paths.
The native session provides an editable
**working copy** for add, duplicate, remove, copy/paste, boolean and typed flag changes,
PNG sprite import/replacement and assignment to the selected frame/pattern/layer.
The Properties tab lists modern flags with names, values, search and a
**Present only** filter (off by default). **Edit attributes** provides the known
fields for ground speed, writable limits, hooks, light, offsets, elevation,
automap, lens help, equipment, default actions, Market, repeated NPC sale
records, expiry IDs, Cyclopedia, classification, gems, imbuements, proficiency,
vocation restrictions, minimum level, weapon type, transparency and wings.
Signed values, packed vocations, optional-field presence and unknown subfields
are preserved. Unknown or unrecognized structures are inspectable but not
rewritten. The **Attributes** tab independently inspects appearance names,
descriptions, group IDs, patterns, sprite IDs, bounding boxes and phase timing;
these structural fields are currently read-only.

**Export** opens the standard PNG/BMP/JPG/OBD dialog for native objects, including
the registered target version and OBD v1/v2/v3 choices. OBD uses profile-mapped
legacy properties and correctly ordered 32-pixel tiles; it can be imported through
the existing legacy **Import** action. Compatibility losses are shown before OBD
export. Each normalized group is bounded to 4,096 references. Batch object exports
wait for the encoder acknowledgment before preparing the next object, avoiding an
unbounded queue of pixel buffers. Sprite export retains the native PNG dialog.
**Native package / AEC…** preserves all groups, flag bytes and referenced pixels in
portable `.nexaobject` files or Assets Editor-compatible `.aec` containers.
**Import** in the native working copy accepts **OBD v1/v2/v3, AEC containers,
Nexa packages and PNG objects**. OBD/AEC can be imported in batches from the
file picker or dropped onto the Objects panel. Their own categories are retained,
object and sprite IDs are remapped without collisions, and explicit replacement
requires one object matching the selected category. Standard OBD tiles are
recomposed in bottom-right order into 32/64-pixel native sprites, retaining
straight RGBA, mapped properties, groups, patterns and v2/v3 phase timings.
OBD v1 lacks phase timings and uses an explicitly reported 100 ms fallback.
Legacy charges, floor-change, profession masks and bone offsets have no assumed
modern meaning: their data is retained as an inspectable custom flag (#200),
with compatibility notices. AEC preserves unknown appearance/group/flag bytes
and carries embedded PNG pixels; these container-only images are removed before
native project persistence. AEC export stores selected objects together in one
file without a legacy conversion. Imports are completely prevalidated before
working-copy changes: up to 100 objects and 32 MiB of decoded pixels per batch;
OBD allows 4,096 tile references per group and at most two outfit groups. Input
files are limited to 32 MiB, and sprites larger than 64 pixels per axis are rejected.
The Sprites panel still imports/replaces PNG sprites.
The standard object **Export** dialog also offers **Modern assets — appearances.dat**
for both legacy and native projects. It creates a **new named folder** containing
an appearances protobuf, `catalog-content.json` and CIP/LZMA spritesheets; it is
not an OBD file renamed to `.dat`. Selected object IDs are retained. Native
objects keep their existing groups, flags and referenced pixels. Legacy objects
use the independently validated OBD bridge to reassemble 32-pixel tiles into
32/64-pixel native sprites. An animated legacy outfit without separate groups
gets an idle group referencing its existing first frame and a moving group
retaining all source frames and available timings. Existing two-group outfits
are preserved; no new artwork or distinct idle animation is invented.
This selected-object export is limited to **100 objects / 32 MiB of pixels**;
legacy sprites larger than 64 pixels per axis are rejected explicitly. Existing
folders are not overwritten, and the current document remains active and dirty
if it had unsaved edits. Use native **Save As** for a complete modern project.
Packages remap sprite dependencies and currently allow 256 distinct sprites
per object. Native PNG input is bounded to 1 MiB, 32/64 pixels per axis,
non-interlaced RGB/RGBA/grayscale/palette encodings supported by the decoder.
Import decodes straight RGBA in the helper to avoid AIR premultiplication
rounding in stored pixels. Blank sprite removal clears pixels rather than
invalidating references used by other objects.

**Save / Ctrl+S / Compile** persist the modern project in its current folder,
without opening a converter or an export modal. Native definitions and edited
CIP/LZMA spritesheets are staged, validated and published as content-addressed
files. The catalog is then replaced atomically where supported, retaining the
previous head as `.nexa-backup-*.catalog.json`. After the committed generation is
validated, retention keeps the **three newest writer backups** and all resources
needed by the current catalog and retained backups. Unreferenced writer-format
appearance/sheet payloads are removed only after their SHA-256 filename is verified;
unrelated or unverified files are left alone. Malformed catalogs or locked files
defer cleanup without turning a successful save into a failure. Metadata-only changes do not rebuild spritesheets;
unchanged sheets are not copied for an in-place save. Existing IDs, unknown
protobuf fields, dimensions and group/phase metadata are preserved.

**Save As / Ctrl+Shift+S** publishes a complete independent modern copy in a new
folder and makes that copy the active document. Further Save commands update
that folder. The native toolbar calls this action **Save As**; the legacy toolbar
retains **Compile As**. Successful writes reset the saved revision and release
the committed edit caches; failures retain dirty state and report the stage/file.
Native sprite Copy/Paste carries exact ARGB bytes alongside the system bitmap
clipboard format, avoiding preview-image premultiplication in internal copies.
Closing an unsaved modern project prompts before discarding its edits.
The native opening dialog shows category counts, sprite/sheet counts, dimensions
and multi-group counts. It does not invent a client release when the catalog lacks one.

Modern animation/geometry editing and whole-catalog PNG export do not yet have
native parity; use an exported DAT/SPR project for the complete legacy toolbox.

**Convert DAT/SPR…** is a separate native-toolbar action that optionally exports the complete native working copy or selected objects
to a **new DAT/SPR project using the registered version list**: 104
version/signature profiles, from 7.10 through 10.99 plus the custom 13.10
DAT/SPR profile. Signature variants such as 8.60 v1/v2 remain distinct.
Choose the target and RGB/RGBA option, select **Review conversion**, inspect the
compatibility notices, accept the reported losses, then select **Export DAT/SPR**
and a parent directory. The exporter creates a uniquely named project rather
than overwriting an existing folder. **Open exported project** returns to the
standard editor's loading dialog; after loading, use its existing add/remove,
replace, import/export, animation and Compile As tools. Save or close any edited
current project before opening the conversion.

Legacy conversion never rewrites source modern assets. Supported flags are mapped by profile;
modern-only attributes are reported, not silently assumed equivalent. Sparse
IDs are preserved, with explicit blank legacy objects in the gaps and at the
base ID of otherwise empty categories so partial projects remain editable.
Exporting **all objects** is the default. **Selected objects only** intentionally
leaves other ID slots blank, rather than remapping the selected IDs. Newly
exported projects include a bounded, signature-checked `nexa-project.json`
selection hint so reopening starts at the first exported object, not an empty
base-ID slot. Larger sprites
are split into correctly ordered 32-pixel quadrants. The output includes OTFI,
a versioned conversion report, the selection hint, the original appearances protobuf and original
catalog bytes. These sidecars preserve unsupported metadata for reference; they
are **not** an automatic modern reimport/round-trip mechanism.
`working-copy-appearances.json` separately retains the selected current
appearance records, including unsupported flag edits. It does not bundle native
sprite/catalog dependencies; use `.nexaobject` packages to preserve portable
native objects and pixels, including alpha, before exporting to a lossy target.

Targets before 9.60 automatically use standard 16-bit sprite IDs/counts when the converted
sprite count fits 65,535; larger exports use the custom 32-bit extension.
9.60 and newer targets use 32-bit sprite IDs/counts. RGBA requires compatible client
support; RGB promotes nonzero alpha to opaque.
Fully transparent pixels remain transparent in **both RGB and RGBA SPR**, even
when their hidden RGB channels contain magenta or another color. This applies
to Items, Outfits, Effects and Missiles and to OBD imports as well as direct
DAT/SPR conversion. Re-export and replace objects previously imported with an
opaque pink background; a saved RGB SPR cannot recover the discarded alpha
unambiguously. Metadata numbers are translated
using the six existing legacy reader families. Targets before 7.60 omit Z
patterns and use implicit offsets; phase timing requires 10.50 and separate
outfit frame groups require 10.57. Unsupported information is listed before
exporting and retained in the source sidecar.
Unsupported vocation restrictions and non-Latin-1 market text are reported.
No full Assets-Editor feature parity or in-game client compatibility certification is claimed.
The optional converted project retains the complete existing legacy editing tools.
The reader targets the catalog/appearances format used by modern Tibia clients,
including the 15.x format. Validation uses the supplied Crystal fixture; the
catalog does not reliably identify its client release, and every 11.x–15.25
release variant has not been independently certified.

The independent helper runs in a separate process with a 64 MiB pixel-cache
budget, bounded inputs/decompression, request-generation checks and explicit
session cancellation. That cache budget is not a bound on total process RAM.
Main-view pages are limited to 100 records, binary pixel responses to 16 MiB,
and selected-object preview caching to 8 MiB / 64 images. Queued requests are
bounded and newer page/selection requests supersede stale responses.
Working-copy pixel edits have a separate 64 MiB budget and metadata edits a
32 MiB budget; these are not total process-memory limits. Edit commands retain
their order. Cache misses retain the previous complete preview frame rather
than blanking it while the next frame is decoded.
Temporary pixel packets use a writable `nexa-modern-sessions` folder beside the
portable bundle, on the same drive, rather than AIR's protected application
resources. Use a writable location for the portable application.
Read the [modern assets audit and implementation status](docs/MODERN_APPEARANCES_AUDIT.md)
for measured results, compatibility boundaries and remaining work.
The [2026-10-14 reliability audit](docs/AUDIT_2026-10-14.md) covers state protection,
request recovery, outfit layer policy, bounded disposal and backup retention.

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
| AEC | Assets Editor-compatible object containers, with native definitions and embedded PNG sprites; import/export in modern projects. |
| Extended DAT item IDs | Custom 32-bit item-count encoding. The consuming client must support it; this is not a universal legacy-client format. |

Choose **Open Asset Files** to select the input, then **Compile As** to select the output. A multi-SPR input can be compiled into a standard archive, and a standard input can be split into multi-SPR. The input files are not themselves an output-format restriction.

Compatibility depends on the DAT signature, metadata controller, sprite encoding and selected options—not solely on a version label. Consult `src/config/versions.xml` for the configured signatures. The tests include a retained 8.60 project and synthetic boundary fixtures; they do not certify every configured version or custom archive.

### Data protection

Completed project generations are staged before publication. Replaced generations are retained in a `.ob-backup-*` directory, and ordinary publication failures trigger rollback. Invalid offsets, truncated records, invalid RLE and unsupported count limits are rejected.

These safeguards are not a substitute for an independent backup. Publication is not guaranteed to be atomic across a power loss, and in-memory merge/bulk-edit operations are not all-or-nothing transactions. A failed import may leave successfully appended sprites or earlier completed replacements in the current document; it must not silently substitute preview error icons or retain foreign sprite IDs. Review the document before saving.

Modern conversion writes to a fresh `.partial` staging directory and publishes
the project only after all files finish. Ordinary failures clean the known files
in that owned directory. Forced cancellation, termination or a power loss may
leave an incomplete `.partial` folder; do not open it as a completed project.

Native Save uses immutable payloads and a catalog-head commit. A forced stop may
leave an unreferenced `.nexa-save-*.partial` directory; the previous catalog is
kept until commit. Catalog backups reference retained old payloads; restore a
backup as `catalog-content.json` to recover that generation. This is not a
power-loss durability guarantee or a substitute for independent backups.

When a feature alert appears, verify **Extended**, **Transparency**, **Improved animations**, **Frame Groups** and any item-count extension against the intended client. Ambiguous samples are advisory, not proof of a format.

## Building on Windows

The editor uses ActionScript 3, Apache Flex and the HARMAN AIR SDK. It is not the separate Rust Object Builder implementation. Packaging native modern support additionally requires the .NET 10 SDK; the published helper includes its runtime, so end users do not need to install .NET.

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

The separate modern-assets runner never writes to its input folder:

```powershell
./tests/run-modern.ps1
./tests/run-modern.ps1 -RealAssets 'D:\modern-client\assets' -AllSheets -Stress
./tests/run-modern.ps1 -RealAssets 'D:\modern-client\assets' -Convert -Ui
./tests/run-modern.ps1 -RealAssets 'D:\modern-client\assets' -ConvertAll
./tests/run-native.ps1 -RealAssets 'D:\modern-client\assets'
./tests/run-native-interruption.ps1 -Assets 'D:\modern-client\assets'
```

`-Ui` additionally runs native-helper and main-workspace integration checks against the
specific Crystal fixture documented in the audit. Those assertions intentionally
check that fixture's counts; use the non-UI runner for a different client set.
The native UI/interruption runners copy input assets to an owned test directory
on the checkout drive before exercising in-place saves; supplied originals are
not modified. `tests/validate_native.py` separately validates the appearance
graph and CIP sheets with Python/liblzma/Pillow and an optional edited-pixel oracle.
`-Convert` tests all three preflights, selected real exports, controlled RGB/RGBA
fixtures and an independent AIR reader/writer oracle. `-ConvertAll` additionally
exports the complete fixture to 10.98 and strictly validates every SPR record.
The oracle checks source-derived pixel hashes, dimensions, references, animation
metadata, mapped property retention, add/remove/replace and DAT recompilation;
the smaller projects also exercise SPR recompilation and reopening.
Modern outputs, test reports, previews and process-local NuGet caches stay beside
the checkout/toolchain. The portable bundle includes the helper and third-party notices.

Coverage includes count boundaries, RGB/RGBA conversion, standard/multi-SPR round trips, invalid records, Windows file locks, rollback, close cycles, import failures, clipboard synchronization, Save state, actual instantiated list renderers and dark confirmation callbacks. The real-data test validates every SPR record and compares its compressed payload, in addition to sampled decoded pixels and DAT reopen checks.

See the [audit report](docs/AUDIT_2026-10-10.md) for measured results, known diagnostics and the limits of this validation. Neither a successful build nor a short test run establishes an absolute absence of memory leaks, corruption or runtime defects.
The [transparency and modern-export follow-up](docs/TRANSPARENCY_AND_MODERN_EXPORT_2026-10-14.md)
documents the alpha-zero pixel regression, native project export, resource ownership
and the corresponding real/synthetic interoperability tests.

## Development principles

Retain the original editing functionality while improving presentation, correctness and maintainability. Changes should be supported by a reproduced issue or a regression test; apparent dead code should not be removed solely because a text search finds no direct caller. MXML bindings, registered worker commands and library callbacks can be indirect entry points.

ActionScript objects are garbage-collected, but file streams, bitmaps, timers and event subscriptions still require careful ownership and lifecycle handling. Cleanup work focuses on those resources rather than introducing C++ smart-pointer patterns into a managed-language application.

## License and attribution

Distributed under the [MIT license](LICENSE), preserving the original Object Builder notices and contributor attribution. Nexa branding and adaptation are maintained by [JokeCore](https://github.com/Mateuzkl). Included artwork retains its associated license notices.
