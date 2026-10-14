# Modern Appearances: Audit and Implementation Status

Date: 13 October 2026. Status: **native working-copy integration in the standard main workspace**, with protected original files and optional legacy export.

The earlier separate modern browser was superseded. Open Asset Files detects
`catalog-content.json` before attempting legacy DAT signatures. The main Objects
and Sprites list/grid pairs, existing category navigation and ThingTypeEditor
now consume native pages and frames. No legacy export is involved in viewing.
The inspected fixture does not establish compatibility with every individual
11.x–15.25 release; a client version is not inferred from an unsigned catalog.

## Scope and provenance

The requested integration was evaluated against the supplied audit archive and
the actual Crystal client assets folder. The archive is a proposal, not trusted
executable instructions. Its format descriptions were checked against source
code and real files before implementation.

- NexaAsset base: `e6e96c8811434323cff3f31a6f7ce062d5e91594`.
- Local implementation branch: `feat/modern-appearances-import`.
- Reference Assets-Editor: `d673c33899b48641f44b7494df7ee29824dee502`.
- Reference: [Arch-Mina/Assets-Editor](https://github.com/Arch-Mina/Assets-Editor).
- Fixture: `crystal/gameclient-main/assets`, supplied on the secondary drive.
- Catalog-plus-appearances SHA-256:
  `E610017D0394663C99A8EA7DABF022CD058FB21A890779149280375406F9A35F`.

The fingerprint covers concatenated catalog and appearances bytes, **not every
spritesheet**. No game assets or third-party editor binaries are committed.

At inspection, the reference repository contained no declared LICENSE and its
GitHub license field was null. No reference classes, protobuf schema, UI,
artwork or binaries were copied. The helper's wire reader, indexing, bounded
I/O, BMP/PNG handling and protocol are independent implementations informed by
format interoperability facts. SharpCompress 1.0.0 is an explicit MIT-licensed
dependency, pinned by version and NuGet content hash. Portable .NET runtime
licenses and third-party notices are copied from the actual runtime package;
the helper's notices do not overwrite them. Reference availability is not a
redistribution license or a claim of endorsement.

## Actual asset inventory

| Category | Records | Highest ID |
| --- | ---: | ---: |
| Items | 42,108 | 54,266 |
| Outfits | 1,443 | 1,949 |
| Effects | 242 | 349 |
| Missiles | 62 | 68 |
| Total | 43,855 | — |

The catalog contains one explicit appearances entry and 4,927 sprite entries.
The appearances file is 4,862,378 bytes. There are additional files in the
folder that are not sprite entries in this catalog; they are not selected by
guessing a filename or by using the first catalog entry.

| Sprite type | Dimensions | Catalogued sheets |
| --- | --- | ---: |
| 0 | 32 × 32 | 327 |
| 1 | 32 × 64 | 57 |
| 2 | 64 × 32 | 55 |
| 3 | 64 × 64 | 4,488 |

There are 1,418 appearances with multiple frame groups and 6,920 animated
groups. The fixture contains 273 phases with an explicit zero duration, and no
phases missing a duration field. Zero and absence are distinct in the model.
Initially rejecting zero durations incorrectly excluded 64 appearances; that
restriction was removed from parsing. The 10.98 profile preserves explicitly
encoded zero durations; their in-game behavior still depends on the client.
Older profiles omit phase timing with an explicit compatibility notice.

The fixture contains modern flag fields through number 64, including NPC sale,
cyclopedia, equipment, classification, imbuement and proficiency information.
Legacy DAT does not provide a universal one-to-one representation for these.
Modern IDs are sparse: the highest ID is not the number of appearances.

## Implemented architecture

```text
Existing main Objects / Sprites / inspector (AIR/Flex, Nexa Dark)
  -> ModernNativeSource, ModernThingData, ModernSpriteData adapters
  -> one serial NativeProcess JSON-lines session
     -> independent .NET helper
        -> checked catalog paths + bounded protobuf index
        -> binary-search spritesheet ranges
        -> lazy bounded CIP/LZMA -> BMP -> RGBA decoding
        -> 64 MiB LRU of decoded sheets
        -> bounded ARGB packets: 100 objects/sprites, one selected frame/grid
        -> 8 MiB / 64-image selected-object cache in AIR
        -> profile-aware normalization and loss preflight
        -> fresh staged DAT/SPR/OTFI + source sidecars and report
           -> open as a new project in the standard legacy editor
```

The main workspace offers 100-entry pages, ID/name search, four categories, frame-group
selection and manual frame/X/Y/Z pattern inspection. Layers are alpha-composed
for preview, bottom/right aligned. Large sprites are displayed at their native
32/64-pixel geometry. Legacy conversion splits those sprites into 32-pixel
quadrants, bottom-right first, padding smaller layers at the top/left.

Protocol 1 also accepts `native-page`, `native-sprites`, `native-detail`,
`native-frame`; it retains `list`, `detail`, `render`, `stats`, `validate`, `preflight`,
`export` and `quit`.
Replies carry `schemaVersion`, `requestId`, `generation` and `type`. Startup
emits progress and a ready message with counts and a source fingerprint.
There is one outstanding request, a 30-second timeout, bounded stdout/stderr
buffers, and generation invalidation on close/cancel/reopen. Request strings
are process arguments or JSON data, never shell commands.

Native main-view pixels use fixed, owned binary packets with checked magic,
row count, dimensions, payload lengths, file path and trailing bytes. A response
is bounded to 16 MiB and a frame/grid to 8 MiB. Sprite sheets decode on demand,
not all at startup. The serial adapter keeps at most 16 queued jobs, deduplicates
frame requests and rejects superseded object/selection responses. Legacy preview
commands retain their two managed PNG slots for helper tests.

Closing a session stops its helper, removes listeners and clears pending state.
Cleanup deletes only the five known preview/pixel files and the empty owned session
directory. It never recursively deletes an input directory. Temporary sessions
live in a sibling `nexa-modern-sessions` folder outside the portable bundle, on
the same drive. The path is an explicit `file:` URL, never a protected `app:/`
resource. Users of a read-only installation must move the portable bundle to a
writable folder or supply a writable parent through the session configuration.
Creation failures clear the session state and report the attempted folder.

The native adapter never sends legacy-worker mutation commands. Conversion publishes
a separate project, then opens it through the existing loading dialog. The
current project's unsaved changes must be saved or closed first. DAT/SPR,
SPMT/SPIX, OTFI, OTB, object editing and compilation retain their standard paths. Builds
include a self-contained Windows x64 helper; end users need neither a .NET SDK
nor an installed .NET runtime. Building the helper requires the .NET 10 SDK.

## Bounds and defensive checks

- Explicit catalog appearances entry; no first-entry fallback.
- Root-contained paths; absolute paths, alternate data streams, traversal,
  junctions and symbolic links are rejected for this implementation.
- All referenced catalog files checked before readiness; range overlaps,
  reversed ranges, unknown sprite types and excess sheet capacities rejected.
- Catalog cap 16 MiB; appearances cap 128 MiB; one million appearance limit.
- Duplicate category/ID pairs rejected. UTF-8 names decoded strictly.
- Varints bounded to ten bytes with overflow/truncation checks. Length-delimited
  fields checked before slicing. Unknown source fields are not rewritten.
- Pattern/layer/group/reference counts bounded and multiplied in checked
  64-bit arithmetic; missing sprites are errors, never blank placeholders.
- CIP magic, padding and compressed length checked. LZMA dictionary declarations
  above 64 MiB rejected; decompressed output capped at 1 MiB.
- Before producing at most 1 MiB, a valid LZMA match cannot require more than
  1 MiB of previously produced history. The decoder window is therefore limited
  to that cap even when the input declares a 32/64 MiB dictionary. Oversize
  output is rejected, not truncated or accepted as an image.
- Complete 384 × 384 32-bit BMP sheets required; channel masks, data bounds,
  bottom-up orientation, alpha and magenta transparency handled explicitly.
- JSON request cap 64 KiB; UI stdout line cap 1 MiB; stderr tail cap 64 KiB.
- Target category IDs must fit uint16; sprite IDs/counts use uint32, bounded to
  four million converted tiles and 4 GiB per SPR. Each normalized group is
  limited to 65,536 references and byte-sized dimensions/patterns.
- Conversion outputs must be disjoint from the source and preview directory;
  existing output or staging folders are rejected. No source file is opened for writing.

These are application-level safeguards, not an operating-system sandbox.
The helper still has the user's OS privileges. A concurrent local attacker
changing filesystem links between checks and opens is not a tested threat model.

## Validation and measurements

### Native working-copy follow-up, 13 October 2026

The working-copy follow-up again loaded the actual Crystal assets directory,
not a converted 10.98 project. The main-application test passed native flags,
retained-frame cache misses, add/duplicate/copy/paste/remove, delete-after-paste
clipboard state, 64-pixel partial-alpha PNG import/export, portable object
packages, and export of an edited object to standard 16-bit 8.60. It also
verified category/search transitions, close/reopen and session-file cleanup.
Initial native Objects/Sprites/preview availability was 4,899 ms; AIR reported
111 MiB at the end, excluding the helper. This is a finite observation, not a
memory-leak guarantee or a controlled performance benchmark.

Screenshots and generated samples for that run are in
`bin-tests/native-17f18865c08543a5a431a14add5a89c7/native-main`.
The final repeat after edit-capacity and binding fixes also passed, in
`bin-tests/native-82ad8517f15b45f79e29b5fd227800b2/native-main` (8,108 ms
initial availability; 113 MiB AIR memory). These timing differences illustrate
why individual test runs are not controlled speed comparisons.
The current working-copy bundle is `bin/NexaAsset-native-editing-20261013`.

The complete real-input pass in
`bin-tests/modern-690f03594c794d08a24d733dd9d05a20` decoded all 4,927 sheets
and validated all 43,855 modeled appearances. Including indexing, validation
and sample previews, it took 99.70 seconds. Cache occupancy ended at
66,650,112 bytes, below 64 MiB. All three full preflights and selected real
exports passed; the independent AIR legacy reader passed pixel/reference/
geometry/animation and recompile/reopen checks on 12 generated projects.
This run is not a new complete 708,524-record legacy export; the complete
10.98 round-trip evidence below is from the earlier recorded pass.

PNG fixtures exercise all filters 0–4, exact straight RGBA (including RGB under
zero alpha), CRC rejection and early dimension bounds. Native packages retain
unknown protobuf fields and remap dependencies. Batch duplicate/paste validates
all metadata and sprite references before mutation; rejected metadata-capacity
checks leave the working copy unchanged. New-object/package capacity is checked
before allocating sprite overrides. Decoded-sheet LRU, edited sprite pixels and
edited object metadata have separate 64/64/32 MiB budgets, respectively; these
budgets do not describe total process RAM. Export does not reset edit budgets;
reopen an exported project to start a fresh working copy.

The unchanged legacy asset/Multi-SPR suite and standard editor layout suite
also passed (`bin-tests/run-0054f74b6f9447b9961c315ea542a133` and
`bin-tests/nexa-layout-1791914312699`). Remaining native-tool gaps are listed
below rather than represented as complete legacy parity.

### Native main-workspace verification, 13 October 2026

The repeated native test used the exact source directory requested by the user:
`D:\DESKTO 25-10-2025\Destkop 20-0426\formatar jk 03-052026\crystal\gameclient-main\assets`.
The user identifies this client as 15.25. The reader does not infer a release
number from this catalog; the UI displays **Modern (Appearances)**, not 10.98.

The real main-application harness verified Open Asset Files detection, native
object/sprite pages, rendered thumbnails, all four categories, grouped outfit
navigation and the final animation frame, inspector integration, ID/name search,
rapid category changes, close/reopen and owned-session cleanup. Initial Objects,
Sprites and preview availability took 4,231 ms, including harness setup, on this
machine. Native inventory is 43,855 appearances and 216,189 catalogued sprites;
the sparse highest sprite ID is not the sprite count. AIR reported 114 MiB at
the end of this finite run, excluding the separate helper process.

Snapshots are under ignored
`bin-tests/native-36347f37ded841cf926b82d6f0e63f65/native-main`.
Legacy 10.98 tests are separate preservation/conversion checks, not the native
source used here. The rebuilt portable bundle is
`bin/NexaAsset-native-modern-20261013`; it replaces the earlier browser UI with
native main-workspace loading and retains only an optional conversion dialog.

The following measurements include earlier helper/conversion/browser tests;
historical browser-only checks are not evidence of native main integration.

Test commands:

```powershell
./tests/run.ps1
./tests/run-ui.ps1 -RealAssets 'D:\legacy-test-copy'
./tests/run-modern.ps1 -RealAssets 'D:\crystal\gameclient-main\assets' -AllSheets -Ui
./tests/run-modern.ps1 -RealAssets 'D:\crystal\gameclient-main\assets' -Stress -Ui
./tests/run-modern.ps1 -RealAssets 'D:\crystal\gameclient-main\assets' -Convert -Ui
./tests/run-modern.ps1 -RealAssets 'D:\crystal\gameclient-main\assets' -ConvertAll
```

Observed on this Windows machine, .NET SDK 10.0.401/runtime 10.0.12 and the
existing Flex/AIR toolchain:

- Legacy asset, warning, progress-layout and dark-alert assertions passed.
- Real legacy editor layout/clipboard/save assertions passed with a test copy.
- 10,000 deterministic malformed-wire fuzz inputs completed without an
  unexpected parser exception. Explicit truncated/overflow varints, lengths,
  duplicates, overlaps, traversal and capacity fixtures were rejected.
- Valid LZMA data expanding above 1 MiB and an excessive dictionary declaration
  were rejected. This is not a general proof about all malformed LZMA streams.
- Synthetic BMP orientation, alpha, magenta and all four crop sizes passed.
- All 43,855 real appearances passed the **modeled frame-count and sprite-reference
  checks**. This does not certify every nested flag's semantic validity.
- All 4,927 catalogued sheets decoded successfully with the bounded dictionary.
  The pass took 18.85 seconds including indexing, modeled validation and samples.
  A previous larger-window run took 186.21 seconds. Different cache/GC conditions
  apply; these observations are not a controlled comparative benchmark.
- Sample RGBA hashes remained identical before and after the window change.
  Every sheet was decoded, but not every pixel of every animation was independently
  compared with another editor or with the game client.
- 10,000 sequential helper previews: render-only p50 0.0582 ms, p95 4.0261 ms;
  8,915 cache hits and 1,476 misses. These exclude UI, IPC, protobuf-detail parsing
  and PNG loading latency. A sequential sequence is not a random-access benchmark.
- Pixel cache ended at 66,650,112 bytes (63.56 MiB), below the 64 MiB limit.
  The same stress process used about 245 MiB working set and 219 MiB private
  memory with 252 handles; the cache budget is **not total process RAM**.
- Native UI assertions covered actual PNG decoding, dark styling, real metadata,
  page changes, cancellation, reopening and closing. Visual capture verified the
  rendered preview, not just a populated image source.
  The original harness supplied a temporary-folder override and missed a
  production-default `fileWriteResource` failure. The revised harness uses the
  default folder, reproduces that failure before the fix, and verifies that
  preview creation and cleanup occur outside application resources after it.
- 100 helper open/preview/close cycles completed with no running helper or owned
  preview directory retained. Observed AIR memory deltas varied with GC; neither
  those deltas nor this finite run establishes absence of leaks.
- Category-change tests opened the dropdown, changed Items/Outfits/Effects/Missiles
  and verified closure, page reset, row count and category count. Dropdowns are
  closed before the asynchronous request disables their owner.
- Native-helper UI tests exercised selected conversion preflight, explicit loss
  acceptance and publication of DAT/SPR/OTFI with the full conversion report.
- All 43,855 appearances passed conversion preflight for all three profiles.
  8.60/9.60 produced 622,899 normalized tiles; 10.98 produced 708,524 tiles.
  Notice counts are per removed/unrepresented field instance, not a count of
  broken objects: 157,065 for 8.60/9.60 and 142,584 for 10.98 in RGBA mode.
- A complete 10.98 export was opened by the independent, existing AIR legacy
  reader. All 708,524 SPR records passed strict offset/payload/RLE validation.
  Source-derived sampled ARGB hashes, geometry, reference ordering and animation
  data matched; add/remove objects and sprites, replacement, DAT recompilation
  and reopening passed. Selected real projects for all three profiles also
  passed standard SPR recompilation and reopening.
- Controlled four-quadrant fixtures passed both RGB and RGBA conversion, signed
  offsets, basic flags, topEffect, explicit zero-duration phases and older-profile
  group/timing losses. Unsupported profile/duplicate selection/source-overlap/
  existing-target checks and ordinary failed-export staging cleanup passed.

Reports and preview images are written under ignored `bin-tests/modern-*`
directories on the secondary drive. Catalog/protobuf fingerprints remained
unchanged. The code only opens source files for reading.

The inherited application-subclass layout test still reports unregistered
`RegisteredCallbacksCommand`/`BackgroundWorkerStartedCommand` diagnostics and a
non-IEventDispatcher ThingData binding warning. Those diagnostics were present
before this integration and are not counted as a clean runtime-error-free run.
The current native main-workspace harness also reports inherited startup
diagnostics and a non-bindable category property; its functional assertions pass, but this is not a
warning-free runtime certification.

## Conversion matrix: implemented legacy export

| Source data | Legacy treatment | Current status |
| --- | --- | --- |
| Basic object booleans, ground speed, writable limits, light | Explicit legacy flags, validated boolean/uint16 payloads | Mapped by target profile |
| Offset/elevation/automap/lens help/clothes/default action | Signed-int16 offsets and range-checked fields | Mapped where supported |
| Market name/category/trade/show IDs/vocations | Preserve IDs; Latin-1 text; clear incompatible profession restrictions with notice | Mapped with reported losses |
| Frame groups, sync/start/loop/durations | 10.98 retains supported groups/timing; older profiles retain moving group and frame count | Exported with profile-specific loss report |
| 32/64-pixel sprite geometry | Deterministic 32-pixel quadrant order and ARGB hash validation | Exported, independently reopened |
| NPC/cyclopedia/imbuement/proficiency/classification and future fields | Versioned loss report plus original protobuf/catalog sidecars | Not encoded in legacy DAT; original bytes retained |
| Modern IDs | Preserve by category; reject over-capacity; explicitly blank sparse gaps | New project only, no append into an open document |

## Publication and compatibility policy

The subsequent request adds in-session native editing: object add/duplicate/
remove/copy/paste, boolean and typed flags, PNG sprites and selected reference assignment.
Viewing, object exports and legacy conversion leave the input generation unchanged.
Explicit native Save commits to the active folder as documented below. Native object packages
preserve flag bytes, groups and pixels with dependency remapping. The complete
legacy toolbox remains available after optional DAT/SPR export.

Targets use all 104 registered version/signature contracts, from 7.10 through
10.99 plus the custom 13.10 DAT/SPR profile, with uint16 DAT category maxima.
The six existing metadata families determine flag numbers, implicit offsets
and presence of Z patterns; versions 10.50 and 10.57 introduce phase durations
and separate outfit groups respectively. Versions before 9.60 use
standard 16-bit sprite IDs/counts up to 65,535 sprites; larger exports require
the custom extended-SPR feature. 9.60/10.98 use 32-bit sprite IDs/counts.
RGBA preserves partial alpha and requires client support; RGB promotes nonzero
alpha to opaque, reported conservatively in preflight. Sparse IDs remain stable;
blank gap objects increase the dense DAT category count. Otherwise empty
categories receive explicit blank base records (item 100, others 1), matching
the standard editor's initial selection and category handling. Category-only
conversion tests enforce those minima. Nonmarket modern names
remain in the ID report/source sidecar, because legacy DAT has no equivalent field.

Export creates a new `.partial` directory outside source/previews, writes all
files and publishes it by directory rename. Ordinary failures delete only known
files from that newly owned directory, never recursively. A forced kill, timeout,
cancel or power loss may leave `.partial`; this is not a completed project and
must not be opened as one. Existing generations are never overwritten by this
converter. Original protobuf/catalog sidecars are reference preservation, not
a promise of automatic reconstruction into a modern client format.

## Remaining work

1. Test the consuming NexaMap/game-client profiles, including alpha, zero timing
   and supported modern semantic flags. A legacy-reader round trip is not an
   in-game compatibility certification.
2. Native animation/geometry editing and complete legacy-tool parity remain
   unimplemented. Native Save now commits in place; Save As creates a copy. Save before
   discarding the working copy.
3. Unregistered/custom target profiles, ID remapping for over-capacity modern sets, and
   append/replace into an existing project require explicit policies.
4. Expand adversarial filesystem/IPC/timeouts, forced-interruption recovery,
   random-access stress and long-duration combined AIR/helper profiling.

No universal format compatibility, lossless modern-to-legacy export, complete
Assets-Editor parity, zero-leak guarantee or production readiness is claimed.

## Follow-up: version selection, complete flags and sparse export reopening

The reference editor's schema and property UI were rechecked at the pinned
commit above. No reference implementation or generated schema was copied.
All known boolean flags are listed, including absent flags. Independent typed
controls cover all known structured/scalar flags, repeated NPC records, packed
vocations, signed offsets and optional-field presence. Removing an NPC record
retains the unknown subfields of the surviving record, not its old array position.
Edits validate before changing the workspace; invalid uint32/int32 values do
not change source bytes or revision. Malformed future submessages remain raw,
read-only metadata instead of breaking the complete Properties panel.

The native Attributes tab shows named appearance, frame-group, sprite-info,
bounding-box and animation fields. It does not require server `items.xml`.
Non-UTF-8 description bytes found in the real fixture are displayed and retained
as raw bytes, not decoded lossily or rewritten. This tab is an inspector;
native geometry/animation editing is not implemented. Modern file writing was
added in the subsequent native-save follow-up below.
`working-copy-appearances.json` preserves the current selected appearance
records separately from the verbatim original protobuf/catalog sidecars.
It retains unsupported flag edits as reference metadata but is not a portable
native project: sprite/catalog dependencies are not bundled. Native object
packages remain the portable preservation mechanism for metadata and pixels.

Export scope is explicit and defaults to all objects. A selected-only project
preserves selected IDs and therefore has intentionally blank intermediate
records. A small `nexa-project.json` manifest stores only the initial selection
and version/signatures. The legacy loader checks signatures, category, ID bounds
and nonempty sprite references before accepting the hint; it accepts no external
paths or executable commands. Existing legacy files without this hint retain
their normal starting selection.

Validation added for this follow-up: all 104 registered contracts exported and
independently loaded/recompiled/reopened by AIR (113 synthetic projects).
All 43,855 Crystal appearances passed the expanded native detail inspection;
all 4,927 sheets decoded with the 64 MiB cache bound (113.29 seconds for this
combined indexing/validation/decoding run, not a UI-load benchmark).
The real item 54,266 is exported separately to 8.60 and checked against source
pixel hashes by the independent legacy reader. In-game client compatibility
still requires separate validation.

The main AIR application also passed real mouse selection/closing for the
104-target dropdown, typed-attribute editing with exactly one workspace revision,
native Properties/Attributes views, optional edited 8.60 export and reopening
at the exported object ID. Reopening and canceling the target dropdown also
retained the committed selection. Its final managed-memory sample was 187 MiB after
loading the dense 54,268-ID legacy project; this is not a leak or RSS benchmark.
Legacy asset I/O, multi-SPR, merging, progress layout, transaction rollback,
warning dialogs, alert styling and debounce/cancel tests passed. Existing
worker-start callback diagnostic messages and non-bindable-category warnings
remain visible in the integration harness; these results do not certify a
warning-free application or complete resource-lifetime audit.

The legacy main-window layout/interaction regression also passed after the
shared dropdown-skin change: ordinary DAT/SPR loading, localized/scaled layouts,
dark dialogs, save/compile state, clipboard callbacks and pending-selection
cancellation. The typed flag popup was visually checked at the top-level
application center with its Apply and Cancel controls visible.

## Follow-up: native persistence, standard export and opening information

Save As offers a complete modern generation in a new, source-disjoint
folder. Native Save commits directly to the current folder. Both preserve category IDs, unknown root and record protobuf fields,
catalog entry metadata and catalogued auxiliary files. Unedited compressed sheets
are copied byte-for-byte. Edited/new sprites are encoded as 384x384 BMP V4
bitfield RGBA sheets inside a CIP/LZMA wrapper; changes in tile dimensions split
consecutive catalog ranges without renumbering IDs. Reserved visible magenta is
rejected rather than silently losing its alpha. The output is reloaded and all
edited sprites compared for exact dimensions/pixels before directory publication.
Failures clean only files/directories created by this save; interruption may
leave a `.partial` generation. The current in-place transaction is described below.

Native objects use the existing PNG/BMP/JPG/OBD export dialog. OBD passes through
the current profile mapper, then the existing ActionScript OBD encoder. Unsupported
modern fields and v1/v2 outfit-group reduction are acknowledged before export.
Pixel packets are snapshotted before the helper reuses its fixed file. The
worker encodes one batch object at a time and acknowledges completion before the
next packet is prepared. OBD references are bounded to the existing 4,096/group
decoder limit; packages/native project save remain the preservation path for
modern-only attributes. Native OBD import is not yet implemented.

The opening dialog analyzes the native catalog before enabling Load and displays
real category/sprite/sheet counts, dimensions and multi-group counts. Legacy-only
feature controls are hidden in native mode. Catalogs without a release field
are reported as native, not guessed to be version 15.25. Changing folders or
closing the dialog releases its temporary metadata process and fixed preview files.

Validation includes synthetic flags/removals/unknown bytes, 32-to-64 pixel range
splitting, new sprites, exact RGBA, auxiliary-file copies, source/target separation,
existing-target rejection and failed native-save cleanup. The real Crystal
main-window harness saved the complete edited native folder, reopened it and
verified edited RGBA; exported OBD v1/v2/v3 and PNG/BMP/JPG; and imported the
modern-produced OBD through the ordinary legacy worker. Startup callback and
non-bindable-category diagnostics still remain; these results do not establish
complete client interoperability or absence of resource leaks.

## Save semantics correction: acceptance tests from the supplied Markdown

The earlier native writer could publish an independent copy, but Ctrl+S routed
to that Save As modal instead of persisting the open folder. This was a routing
defect, not merely a label problem. `ObjectBuilder.compileProject` now calls the
native save operation directly; `compileProjectAs` opens only native Save As.
The toolbar exposes `Convert DAT/SPR` separately. Ordinary legacy routes are
unchanged. Unknown/unhandled operations no longer overwrite the status of a
successfully handled native list or sprite request with a legacy-only warning.

`NativeSave.InPlace` stages only native definitions and edited/new sheets. It
does not copy all untouched graphics or use a legacy converter. Payloads are
content-addressed and flushed before publication. The existing catalog is
compared with the loaded baseline to reject external changes. An exclusive
save lock prevents two cooperating writers. New immutable payloads are placed
first; `File.Replace` commits the catalog head and keeps its previous bytes in
a `.nexa-backup-*.catalog.json` file. Prior payloads remain available for backup
recovery. Ordinary failed commits retain/restore the previous head and remove
only newly owned files. The actual published generation is reloaded and every
edited sprite and definition compared before reporting success. Successful Save
rebases the live model and clears committed edit budgets; Save As switches the
active folder. Failure keeps dirty state. This is not an absolute power-loss
guarantee; a forced stop can leave an unreferenced `.nexa-save-*.partial` directory.

Native sprite Copy/Paste now includes a private, bounded ARGB clipboard record
beside the interoperable system bitmap. Internal copies use exact stored pixels,
not a premultiplied preview. The close handler checks for unsaved/pending native
work before disposing the session; Cancel retains it. New native appearances
explicitly encode pattern width/height/depth/layers as one, rather than relying
on preview-reader fallbacks for absent protobuf scalars. Consumer behavior was
cross-checked against the primary [OTClient appearance loader](https://github.com/mehah/otclient/blob/main/src/client/thingtype.cpp)
and [CIP sheet loader](https://github.com/mehah/otclient/blob/main/src/client/spriteappearances.cpp).
No code from those loaders was copied.

Executed validation:

- Synthetic no-edit/in-place/metadata-only round trips, unknown protobuf fields,
  all four 32/64-axis sprite types, reference updates and cache-budget release.
- Actual Windows read-only and exclusively locked catalog failures, injected
  disk-full/write failures at staging, payload publication and catalog commit;
  prior head retention/rollback and successful retry.
- A separately launched helper forcibly killed during native definition writing,
  on an owned copy of Crystal assets. The old catalog SHA-256 remained unchanged;
  incomplete staging remained unreferenced rather than becoming a saved project.
- Main-window Ctrl+S menu routing without a modal, real sprite Copy/Paste,
  reference edit, same-folder Save/reopen, failure retaining dirty and successful
  retry. Save As was reopened after the original test-copy folder was renamed.
- Independent Python/liblzma/Pillow validation of 43,857 saved appearances,
  4,931 sheets and 297,898 references, including the edited ARGB MD5 oracle.
- Real 8.60 DAT/SPR input with 485,548 sprites: every original compressed record
  preserved, sprite/property edit-save-reopen, full Multi-SPR publication,
  part-boundary checks and Multi-SPR-to-standard edited-pixel round trip.
- Existing progress, mismatch-warning, ownership, close-cycle, rollback and dark
  confirmation tests; the 104-profile/113-project independent legacy oracle.

Native tests copy the supplied originals to D: before any in-place test write.
The original Crystal/legacy inputs are not overwritten. The independent validator
checks file-format interoperability; no running commercial Tibia client was used.
The final rerun also passed canceling the unsaved-close prompt. Its input had
43,856 appearances; after the two test imports the independent validator checked
43,858 appearances, 4,931 sheets and 297,899 references. The test no longer assumes
a fixed maximum item ID when the supplied fixture has gained another object.
Write support remains limited to the supported catalog/protobuf layout with
384x384 CIP/LZMA BMP sheets and sprite types 0–3. Larger/custom layouts require
separate support and are rejected. Geometry/animation editing and native OBD
import remain outside this implementation. Existing startup callback/binding
warnings in the harness are still not a clean, zero-warning lifetime audit.
