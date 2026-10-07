# Release builds

Requires Python 3, .NET 10 SDK and pnpm 11. Install frontend dependencies first (`pnpm --dir web install --frozen-lockfile`).

```sh
python3 scripts/publish.py
```

The script builds the frontend, runs tests and publishes Linux/Windows x64 single-file self-contained executables. Archives are placed in ignored `dist/`. Each package contains the executable and available license notices; no separate DLLs, wwwroot or appsettings are required. Run the executable and visit http://127.0.0.1:5087. Settings can be passed on the command line.

```sh
python3 scripts/publish.py --rid linux-x64
python3 scripts/publish.py --framework-dependent
```

Framework-dependent builds still bundle application dependencies and the frontend, but require the .NET 10 **ASP.NET Core Runtime** on the user's machine. They are smaller and use the installed runtime's servicing updates. Self-contained is the default for end-user downloads; it includes the runtime and must be rebuilt to receive runtime security updates.

Single-file is packaging, not Native AOT. Trimming is disabled because asset serialization uses reflection. Native runtime libraries may be extracted automatically at startup, so the user's runtime extraction directory must be writable. Linux still requires supported system libraries; self-contained does not mean a fully static executable.

Frontend assets are embedded during compilation. Always build web before compiling/publishing the server. Vite development can still use its proxy. `Portable.pubxml` disables IIS web.config generation and embeds debug symbols to avoid loose PDB files.
