# Audio inspection and repair

## Editor workflow

Open an audio resource and select **Inspect / repair**. Pending audio edits must be applied or removed before inspection/repair. The dialog provides:

- Fast inspection of the UE envelope, CueName lookup order, references, stream slots/work capacity and physical AFS2 indexes.
- Optional deep inspection of referenced HCA headers/frames, CRC, PCM decoding and stored bank MD5. This can scan large banks and take substantially longer.
- Read-only CueName/CueIndex and StreamAwb/header views; native lookup failures are shown independently of whether a name exists in the table.
- Managed StreamAwbTocWork capacity editing: leave blank for the recommended zero-filled allocation, or specify a sufficiently large size (maximum 16 MiB).
- Selected repairs, with confirmation, and downloadable JSON reports.

Repair writes the **project draft**, not the game installation. Save/export are still separate steps. An inspection result means only that its checked contracts passed; it is not proof of successful in-game playback, output routing or loop behavior. Standalone AWB inspection has no paired ACB context. Non-HCA codecs, encrypted PCM without a key, unknown graph types and unknown plugin default bank directories are not declared validated.

## Automatic maintenance

The ordinary create/add/rename/replace/delete writers maintain these conditions before embedding an ACB:

1. Sort CueName rows with case-sensitive UTF-8 byte comparison (`strcmp` order), preserving the CueIndex stored in each row. Cue/Sequence/Track/Synth/Waveform indices are not reordered for name sorting.
2. Keep modern stream slot names sorted. When slot order changes, move each cached header with its slot and remap streaming Waveform ports. The main bank must remain port 0 and first in name order; choose extension names after the sheet name.
3. Ensure zero-filled stream work capacity. The supported modern Win64 layout uses `portCount × (2048 + 8 + 8)` aligned bytes; allocation adds 64 bytes of slack. Alignment is measured relative to the **ACB buffer**, not the enclosing `.uexp` file offset.
4. Synchronize the typed UE NumSlots property.
5. When writing a physical AWB, regenerate its paired cached index from actual file bytes and update the stored hash. Index sizing uses the AFS2-declared ID/offset widths rather than assuming all imported banks use 16-bit IDs/32-bit offsets. Cached bytes immediately following an aligned index are preserved from the file instead of replaced by zeros.
6. Reload generated paired files. Normal batch jobs run the fast inspector before publishing the staged output to the draft. Known blocking issues refuse publication; files that changed during editing require a new operation.

The supported stream work mutation profile is the verified modern Win64 ACB format/layout (versions 1.26–1.30 with the expected root column positions). Other layouts are reported unsupported, not rewritten by guessing. Header-only inspection never loads the entire multi-GB AWB into memory.

Cue ID order and graph references are also inspected. Duplicate IDs/names and invalid graph references require a correct source definition; automatic repair does not invent definitions or reindex unknown consumers.

## API

All paths are Content-relative. Existing loopback/IP allowlist, Host/Origin and `X-Mercury-Local` protections remain in force.

### Read-only inspection

```http
GET /api/projects/{id}/audio-inspection?path=Sound/Bgm/MER_BGM.uasset&source=project&deep=false
```

`source` is `project` or `game`, with the same project-to-game fallback as the resource editor. External bank resolution follows the sheet's explicit AwbDirectory, not merely the directory containing its `.uasset`.

The normal API envelope returns a report with:

- `revision`: sheet/paired-file content hashes plus resolved bank index and file metadata identity. A fast revision is **not** a whole-payload checksum of every bank.
- `scope`, `deep`, `checksPassed`.
- `issues`: stable code, severity, table, row, optional port and repair ID.
- `repairs`: currently safe available action IDs, descriptions and recommended selections.
- `streams`, `cueNames`, `work`, `numSlots`.

AWB file lengths are decimal strings for JavaScript clients. `checksPassed` only means no known blocking issue in this inspection; warnings describe unverified or narrower checks.

### Selected repair

```http
POST /api/projects/{id}/audio-repair?path=Sound/Bgm/MER_BGM.uasset&source=project
X-Mercury-Local: 1
Content-Type: application/json

{
  "revision": "<revision returned by inspection>",
  "actions": ["sort-cue-names", "rebuild-stream-work", "sync-num-slots"],
  "workBytes": null
}
```

Use exact IDs returned by the report. At most 100 distinct actions are accepted. `workBytes` is allowed only with `rebuild-stream-work`; it must cover every port plus worst-case alignment slack.

Available actions, when their prerequisites are satisfied:

- `sort-cue-names`: reorder only the name table, preserving CueIndex.
- `rebuild-stream-work`: regenerate zero-filled work with the chosen/recommended capacity.
- `sync-num-slots`: synchronize the UE property with the stream table count.
- `sort-stream-slots`: reorder slots and headers together, remapping Waveform ports.
- `refresh-awb-header:<port>`: rebuild the cached AFS2 index from a validated physical bank.
- `refresh-awb-hash:<port>`: recompute stored MD5. The complete native hash-validation contract is not claimed recovered.
- `repack-awb:<port>`: rebuild a structurally valid bank while preserving every ID and payload; update the paired cache/hash. It does not re-encode audio.

File-dependent action ports refer to the **pre-repair report**. If slot sorting is also requested, file actions run first, then slots/headers/ports move together. Results are verified by bank name after remapping.

A missing/truncated bank, missing referenced waveform or unsupported ID width cannot be fixed by fabricating bytes. If the physical bank does not contain required entries, cache replacement/repacking for that port is not offered; provide the correct bank first. Repacking does not recover damaged/truncated audio.

The response includes `applied`, `changedFiles`, `before` and `after`. Other unresolved issues remain in `after`; a successful partial repair is not reported as full compatibility. The server requires a fresh revision, stages and reloads output, checks sources again, uses the paired-file transaction and reads back the exact written draft. Selected hash/repack operations additionally check source payload identity. A stale revision or unsafe repair leaves the draft unchanged.

Deep inspection and full-bank repacking are explicit operations; normal fast inspection does not perform a full decode or hash every original bank.
