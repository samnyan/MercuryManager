# MercuryManager

A UE data table editor for WACCA, built on [UAssetAPI](https://github.com/atenfyr/UAssetAPI).

[中文版](README_CN.md)

## Download

Download the latest version from the Releases page.

## Development

Requires .NET 10 SDK, Node.js 22+, and pnpm 11.

```sh
pnpm --dir web install --frozen-lockfile
pnpm --dir web build
dotnet test tests/MercuryManager.Assets.Tests
dotnet run --project src/MercuryManager.Server --no-launch-profile
```

Once the server is listening, it automatically opens your default browser at http://127.0.0.1:5087.

## Startup options

```sh
MercuryManager --host 0.0.0.0 -port 8081
MercuryManager --no-launch
# Development: pass application arguments after --
dotnet run --project src/MercuryManager.Server --no-launch-profile -- --host 127.0.0.1 --port 8081 --no-launch
```

- `--host`: listening IP address, defaults to `127.0.0.1`; also accepts `localhost` and IPv6.
- `--port` / `-port`: listening port, defaults to `5087`.
- `--no-launch`: disable automatic browser launch.
- Binding to `0.0.0.0` or `::` opens the browser at `127.0.0.1`.
- Remote access still requires explicit client IPs via `--allowed-clients 192.168.1.10,192.168.1.11`; changing the bind address does not disable access protection.
- The existing `--listen-url` remains supported but cannot be combined with `--host` / `--port`.

This project was developed with AI assistance.
