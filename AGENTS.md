# Development guide

- Server: `src/MercuryManager.Server` (.NET 10, UAssetAPI). Frontend: `web` (Vue 3, pnpm). Tests: `tests/MercuryManager.Assets.Tests`.
- Run `pnpm --dir web build` and `dotnet test tests/MercuryManager.Assets.Tests` before committing. Frontend output is ignored `wwwroot`; publish to ignored `dist`, not `artifacts`.
- `web/src/tableCatalog.json` is shared by frontend and embedded server catalog. Reuse AssetGrid/AssetForm/FieldInput and PropertyCodec; avoid one copied implementation per table. Labels show Chinese plus exact original field name.
- Tests must construct their own data and work offline without a game installation. Do not commit game assets, dumps, private research, secrets, personal paths or generated files. Temporary tests must clean up their own output.
- Preserve Unreal property types and FName numbering. Use asset-aware UAssetAPI JSON serialization. Keep 64-bit integers as decimal strings for JavaScript clients.
- Draft editing must not modify source assets. Writing must validate reloaded output, preserve paired files and respect backup/source-change checks. Do not claim JSON roundtrip proves in-game compatibility.
- This is an unauthenticated local filesystem editor: loopback by default, explicit IP allowlist for remote clients, no public exposure. Do not weaken Host/Origin/request-header checks.
- Inspect branch/status first; preserve others' changes. Commit only task files. Do not restart an existing server or push without authorization. Keep README accurate; license selection belongs to the maintainer.
