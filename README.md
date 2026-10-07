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

Open http://127.0.0.1:5087 in your browser.

This project was developed with AI assistance.
