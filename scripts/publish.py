"""Build portable single-file releases. Requires Python 3, pnpm and .NET 10 SDK."""
import argparse
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--framework-dependent', action='store_true', help='Require installed .NET 10 ASP.NET Core Runtime')
parser.add_argument('--rid', action='append', choices=['linux-x64', 'win-x64'], help='Defaults to both platforms')
args = parser.parse_args()

def run(*command):
    subprocess.run(command, cwd=ROOT, check=True)

run('pnpm', '--dir', 'web', 'build')
run('dotnet', 'test', 'tests/MercuryManager.Assets.Tests')
for rid in args.rid or ['linux-x64', 'win-x64']:
    suffix = '-framework-dependent' if args.framework_dependent else ''
    output = ROOT / 'dist' / (rid + '-single' + suffix)
    if output.exists():
        shutil.rmtree(output)
    run('dotnet', 'publish', 'src/MercuryManager.Server', '-c', 'Release', '-r', rid,
        '-p:PublishProfile=Portable', '--self-contained', 'false' if args.framework_dependent else 'true',
        '-p:SelfContained=' + ('false' if args.framework_dependent else 'true'),
        '-p:EnableCompressionInSingleFile=' + ('false' if args.framework_dependent else 'true'), '-o', str(output))
    executable = 'MercuryManager.Server.exe' if rid == 'win-x64' else 'MercuryManager.Server'
    files = sorted(p.name for p in output.iterdir())
    if files != [executable]:
        raise RuntimeError(f'Expected one executable, got {files}')
    # Include project and dependency licensing notice alongside the executable.
    for name in ['LICENSE', 'THIRD_PARTY_NOTICES.md']:
        if (ROOT / name).is_file():
            shutil.copy2(ROOT / name, output / name)
    archive = shutil.make_archive(str(ROOT / 'dist' / ('MercuryManager-' + rid + '-single' + suffix)),
                                  'zip' if rid == 'win-x64' else 'gztar', root_dir=output)
    print(f'{archive} ({Path(archive).stat().st_size} bytes)', flush=True)
