"""Private PC baseline capture only; this is not a PPSSPP comparison runner."""

import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import time


REPO = Path(__file__).resolve().parents[1]
DEFAULT_HOST = REPO / "TagForceSpecial.PC/bin/Debug/net10.0/TagForceSpecial.PC.dll"


def discover_dotnet(executable=None):
    candidate = executable or "dotnet"
    found = shutil.which(candidate)
    if not found:
        raise ValueError(f"dotnet executable not found: {candidate}. Install .NET 10 or pass --dotnet <executable>.")
    return str(Path(found).resolve())


def host_command(host, executable=None):
    dotnet = discover_dotnet(executable)
    host = Path(host).resolve()
    if not host.is_file() or host.suffix.lower() != ".dll":
        raise ValueError(f"PC host DLL not found: {host}. Build the root solution first or pass --host <host.dll>.")
    return [dotnet, str(host)]


def client_size(pid):
    """Observe Windows client pixels while the short-lived capture window exists."""
    if os.name != "nt":
        return None
    from ctypes import wintypes
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.EnumWindows.argtypes = [callback_type, wintypes.LPARAM]
    size = None

    @callback_type
    def visit(hwnd, _):
        nonlocal size
        owner = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
        rect = wintypes.RECT()
        if owner.value == pid and user32.GetClientRect(hwnd, ctypes.byref(rect)):
            width, height = rect.right - rect.left, rect.bottom - rect.top
            if width >= 480 and height >= 272:
                size = [width, height]
        return True

    user32.EnumWindows(visit, 0)
    return size


def verify_capture(path, stdout):
    with Path(path).open("rb") as capture:
        header = capture.read(24)
    if len(header) != 24 or header[:16] != b"\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR":
        raise ValueError("Host capture is not a PNG with an IHDR header.")
    if struct.unpack(">II", header[16:24]) != (480, 272):
        raise ValueError("Host capture must be the 480x272 logical render target.")
    if "GUI_CAPTURE logical=480x272" not in stdout:
        raise ValueError("Host did not report the logical capture milestone.")


def capture_baseline(command, output_root, timeout):
    root = Path(output_root).resolve()
    root.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="baseline-", dir=root))
    capture = run / "title.png"
    command = command + ["--capture", str(capture), "--data-root", str(run / "data"), "--seed", "0"]
    print(f"Private baseline directory: {run}", flush=True)
    with (run / "stdout.log").open("w", encoding="utf-8") as stdout, (run / "stderr.log").open("w", encoding="utf-8") as stderr:
        process = subprocess.Popen(command, cwd=REPO, stdout=stdout, stderr=stderr, shell=False)
        window = None
        deadline = time.monotonic() + timeout
        try:
            while process.poll() is None:
                window = client_size(process.pid) or window
                if time.monotonic() >= deadline:
                    raise ValueError(f"PC baseline timed out after {timeout:g}s; logs retained in {run}.")
                time.sleep(0.01)
        finally:
            if process.poll() is None:
                process.kill()
            process.wait()
    if process.returncode != 0:
        raise ValueError(f"PC host exited {process.returncode}; inspect private logs in {run}.")
    verify_capture(capture, (run / "stdout.log").read_text(encoding="utf-8"))
    metadata = {
        "scenario": "pc-title-baseline", "command": command, "exit_code": process.returncode,
        "logical_render_target": [480, 272], "configured_window_client": [960, 544],
        "observed_window_client": window, "window_measurement": "observed" if window else "unverified",
        "capture_sha256": hashlib.sha256(capture.read_bytes()).hexdigest(),
        "pc_baseline": "captured", "psp_reference": "unverified",
        "source_profile": None, "game_state": "isolated fresh PC data; title; seed 0; no imported assets",
    }
    with (run / "baseline.json").open("x", encoding="utf-8") as output:
        json.dump(metadata, output, indent=2)
    print("PC_BASELINE captured logical=480x272; PSP reference=unverified")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", help="dotnet executable path or command (default: discover on PATH)")
    parser.add_argument("--host", type=Path, default=DEFAULT_HOST)
    parser.add_argument("--check", action="store_true", help="check executable, SDK and built host without launching UI")
    parser.add_argument("--output-root", type=Path, help="private local directory; never upload or package it")
    parser.add_argument("--timeout", type=float, default=30)
    args = parser.parse_args(argv)
    if not args.check and args.output_root is None:
        parser.error("--output-root <private-directory> is required for capture")
    if not 0 < args.timeout <= 300:
        parser.error("--timeout must be greater than zero and at most 300 seconds")
    try:
        command = host_command(args.host, args.dotnet)
        if args.check:
            result = subprocess.run([command[0], "--version"], check=True, timeout=args.timeout, capture_output=True, text=True, shell=False)
            print(f"PC_RUNNER available SDK={result.stdout.strip()} host={command[1]}")
        else:
            capture_baseline(command, args.output_root, args.timeout)
        return 0
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"Verification failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
