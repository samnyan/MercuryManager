# MercuryManager

用于 WACCA 的 UE 数据表编辑工具，基于 [UAssetAPI](https://github.com/atenfyr/UAssetAPI)。

[English](README.md)

## 下载

到 Release 页面下载最新版本。

## 开发

需要 .NET 10 SDK、Node.js 22+ 和 pnpm 11。

```sh
pnpm --dir web install --frozen-lockfile
pnpm --dir web build
dotnet test tests/MercuryManager.Assets.Tests
dotnet run --project src/MercuryManager.Server --no-launch-profile
```

在浏览器中访问 http://127.0.0.1:5087。

这个项目使用了 AI 辅助开发。
