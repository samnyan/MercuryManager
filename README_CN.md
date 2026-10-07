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

启动成功后会自动打开默认浏览器，默认地址为 http://127.0.0.1:5087。

## 启动选项

```sh
MercuryManager --host 0.0.0.0 -port 8081
MercuryManager --no-launch
# 开发环境：应用参数放在 -- 后
dotnet run --project src/MercuryManager.Server --no-launch-profile -- --host 127.0.0.1 --port 8081 --no-launch
```

- `--host`：监听 IP，默认 `127.0.0.1`；也支持 `localhost` 和 IPv6。
- `--port` / `-port`：监听端口，默认 `5087`。
- `--no-launch`：不自动打开浏览器。
- 监听 `0.0.0.0` 或 `::` 时，浏览器打开 `127.0.0.1`。
- 远程访问仍需用 `--allowed-clients 192.168.1.10,192.168.1.11` 明确允许客户端 IP；改变监听地址不会关闭访问保护。
- 兼容原有 `--listen-url`，但不能与 `--host` / `--port` 混用。

这个项目使用了 AI 辅助开发。
