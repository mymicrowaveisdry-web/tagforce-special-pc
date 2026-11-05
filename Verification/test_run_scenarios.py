"""Synthetic runner checks: no original assets, emulator or graphical session."""

import contextlib
import io
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch

from Verification import run_scenarios as runner


class RunnerTests(unittest.TestCase):
    def test_discovers_dotnet_on_path(self):
        with patch.object(runner.shutil, "which", return_value=str(Path("sdk with spaces/dotnet.exe").resolve())) as which:
            self.assertTrue(Path(runner.discover_dotnet()).is_absolute())
            which.assert_called_once_with("dotnet")

    def test_absent_executable_reports_actionable_cli_failure(self):
        stderr = io.StringIO()
        with patch.object(runner.shutil, "which", return_value=None), contextlib.redirect_stderr(stderr):
            self.assertEqual(1, runner.main(["--check"]))
        self.assertIn("dotnet executable not found", stderr.getvalue())
        self.assertIn("--dotnet", stderr.getvalue())

    def test_missing_host_reports_build_instruction(self):
        with tempfile.TemporaryDirectory() as root, patch.object(runner, "discover_dotnet", return_value="dotnet"):
            with self.assertRaisesRegex(ValueError, "Build the root solution first"):
                runner.host_command(Path(root) / "missing.dll")

    def test_paths_with_spaces_and_shell_characters_remain_single_arguments(self):
        with tempfile.TemporaryDirectory(prefix="runner space & ") as root:
            host = Path(root) / "PC host.dll"
            host.touch()
            with patch.object(runner, "discover_dotnet", return_value="/sdk space/dotnet"):
                command = runner.host_command(host)
            self.assertEqual(["/sdk space/dotnet", str(host.resolve())], command)

    def test_check_invokes_discovered_executable_without_shell(self):
        with patch.object(runner, "host_command", return_value=["/sdk space/dotnet", "host.dll"]), patch.object(runner.subprocess, "run") as run, contextlib.redirect_stdout(io.StringIO()):
            run.return_value.stdout = "10.0.402\n"
            self.assertEqual(0, runner.main(["--check"]))
            self.assertEqual(["/sdk space/dotnet", "--version"], run.call_args.args[0])
            self.assertFalse(run.call_args.kwargs["shell"])

    def test_window_screenshot_cannot_pass_as_logical_capture(self):
        with tempfile.TemporaryDirectory() as root:
            png = Path(root) / "synthetic.png"
            png.write_bytes(b"\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR" + struct.pack(">II", 960, 544))
            with self.assertRaisesRegex(ValueError, "480x272"):
                runner.verify_capture(png, "GUI_CAPTURE logical=480x272")
            png.write_bytes(png.read_bytes()[:16] + struct.pack(">II", 480, 272))
            with self.assertRaisesRegex(ValueError, "milestone"):
                runner.verify_capture(png, "")
            runner.verify_capture(png, "GUI_CAPTURE logical=480x272")


if __name__ == "__main__":
    unittest.main()
