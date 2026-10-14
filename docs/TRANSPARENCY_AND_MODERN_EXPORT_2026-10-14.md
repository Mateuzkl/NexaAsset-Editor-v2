# Transparency and Modern Project Export — 2026-10-14

## Confirmed cause and correction

Modern CIP sheet decoding retains RGB magenta under zero alpha. OBD preserves
those ARGB bytes correctly, but the legacy Sprite compressor previously treated
only an entirely zero ARGB word as transparent. Importing an alpha-zero colored
pixel into RGB SPR therefore emitted an opaque colored run, producing the pink
background. Visibility now depends on the alpha byte for both RGB and RGBA SPR.
Visible magenta is not indiscriminately removed. RGB still promotes nonzero
partial alpha to opaque; RGBA requires a compatible client.

The regression failed before the fix and passed afterwards. It covers all four
categories, not only outfits. Existing RGB files with lost alpha must be replaced
by re-exporting/reimporting from the transparent original; guessing transparency
from every magenta pixel in a saved RGB archive would damage legitimate artwork.

## Selected modern projects

The existing Export dialog offers Modern assets — appearances.dat for legacy
and native objects. This writes a new project folder containing an appearances
protobuf, catalog and native CIP/LZMA sheets, through the validated atomic native
writer. It preserves selected object IDs and never changes the active document
or marks an unrelated native working copy as saved.

Legacy export uses strict SPR reads and bounded OBD v3 worker packets, then the
independent OBD reader reassembles native pixels. A single animated outfit gets
an idle group using its existing first frame and a moving group containing all
source frames and available timing. Existing two-group outfits are unchanged.
Native-to-native selection export preserves the native groups and unknown flag
bytes rather than passing through a lossy legacy profile.

Limits: 100 selected objects, 32 MiB pixels, 4 MiB native metadata, 32/64 pixels
per native sprite axis. Existing destinations, source-contained native targets,
preview-session destinations, duplicate IDs, corrupt SPR/OBD and invalid paths
are rejected. Cleanup removes only explicitly owned staging files, not source
directories. All legacy-only conversion notices remain explicit; this is not
commercial-client certification or full third-party feature parity.

## Review findings and lifetime checks

- Borrowed model.pixels is retained, including its position/endian, for repeated
  export attempts. Only locally loaded temporary packets are cleared.
- Native export has a batch-wide state and generation token spanning dialogs,
  preflight, confirmation, worker acknowledgements and package/project export.
  Cancellation, failure, revision changes and document clear invalidate it.
- Save-and-close and discard-and-close use the normal close event path; a
  synchronous save failure cancels the pending close intent.
- The native sprite export alert correctly points to Convert DAT/SPR….
- The interruption runner declares PowerShell 7.0 before incompatible APIs.
- The repeated outfit base-layer finding was already fixed in d663c0d. It was
  verified in current code and rerun without duplicating the implementation.
- Grid/list renderers now own snapshots rather than borrowing page bitmaps
  after cache retirement. Aliased cropped/original ThingListItem bitmaps are
  disposed once. A retirement regression and repeated project/page transitions
  exercise this boundary. Renderer snapshots are reclaimed with their pooled
  views; paging and virtualization bounds remain in force.

## Executed validation

- 32 real/synthetic category-profile fixtures: Items, Outfits, Effects, Missiles;
  8.60, 9.60, 10.98, 13.10; OBD v1/v2/v3 import and RGB/RGBA SPR compile/reopen;
  every normalized referenced tile hash, not only one visible preview pixel.
  Real cases include item 102, outfit 1945, effect 1 and missile 1 from Crystal.
- Independent AIR oracle: 117 converted projects, including all 104 registered
  version/signature contracts and real-source selected exports.
- C# legacy 8.00 to native project: IDs, idle/moving groups, RGBA, existing groups,
  source guards, rejected batches, seed cleanup and JSON-lines Save As startup.
- Main AIR UI: cancellation/reentry, worker failure/retry, revision invalidation,
  complete batch, modern selection export, legacy-to-modern export/reopen,
  OBD/AEC import, Save/Save As, clipboard and failed Save retaining dirty state.
- Legacy 8.60 UI and storage regressions: Save/clipboard, translated/scaled
  layout, Multi-SPR, strict RLE, boundaries, rollback and close cycles.

Original supplied assets were only read; destructive fixture tests used copies
under bin-tests. Inherited binding/startup warnings remain. CodeRabbit CLI was
unavailable; no CodeRabbit review, zero-leak/UAF proof or in-game certification
is claimed. The evidence above describes bounded regression coverage.
