"""Source ISO locations are explicit inputs, independent of developer home paths."""
import contextlib
import importlib.util
import io
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class DuelValidatorCliTests(unittest.TestCase):
    def test_validators_require_an_explicit_iso_path(self):
        for name in ('validate_duel_reference', 'validate_duel_battle'):
            with self.subTest(validator=name):
                spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / (name + '.py'))
                module = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(module)
                with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as raised:
                    module.parse_args([])
                self.assertEqual(2, raised.exception.code)
                supplied = Path('private-inputs') / 'my-game.iso'
                self.assertEqual(supplied, module.parse_args(['--iso', str(supplied)]).iso)


if __name__ == '__main__':
    unittest.main()
