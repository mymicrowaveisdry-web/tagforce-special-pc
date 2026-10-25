"""Configure workspace-local Ghidra and PPSSPP profiles after setup_stage3.py."""
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[1]


def main():
    tools = ROOT / 'third_party' / 'stage3'
    ghidra = next((tools / 'ghidra').glob('ghidra_*_PUBLIC'))
    jdk = next((tools / 'jdk').glob('jdk-*'))
    extension = tools / 'allegrex' / 'ghidra-allegrex'
    target = ghidra / 'Ghidra' / 'Extensions' / 'ghidra-allegrex'
    if not target.exists():
        shutil.copytree(extension, target)
    env = ROOT / 'analysis' / 'stage3-environment'
    env.mkdir(parents=True, exist_ok=True)
    properties = ghidra / 'support' / 'launch.properties'
    original = properties.with_suffix('.properties.original')
    if not original.exists():
        shutil.copyfile(properties, original)
    text = original.read_text()
    text = text.replace('JAVA_HOME_OVERRIDE=', 'JAVA_HOME_OVERRIDE=' + jdk.as_posix())
    for key, folder in [('application.settingsdir', 'settings'), ('application.cachedir', 'cache'), ('application.tempdir', 'temp')]:
        path = env / folder
        path.mkdir(exist_ok=True)
        text += f'\nVMARGS=-D{key}={path.as_posix()}\n'
    properties.write_text(text)
    ppsspp = tools / 'ppsspp'
    profile = ppsspp / 'memstick' / 'PSP' / 'SYSTEM'
    profile.mkdir(parents=True, exist_ok=True)
    config = profile / 'ppsspp.ini'
    if not config.exists():
        config.write_text('[General]\nFirstRun = False\nRunCount = 1\nCheckForNewVersion = False\n'
                          'Enable Reporting = False\nReportingHost = \n'
                          'RemoteDebuggerOnStartup = True\nRemoteDebuggerLocal = True\nRemoteISOPort = 56244\n'
                          'PauseOnLostFocus = False\nEnableCheats = False\n'
                          '[Graphics]\nGraphicsBackend = 0 (OPENGL)\nRenderingMode = 1\nInternalResolution = 1\n'
                          '[Sound]\nEnable = False\n', encoding='utf-8')
    paths = {'ghidra': str(ghidra), 'jdk': str(jdk), 'ppsspp': str(ppsspp / 'PPSSPPWindows64.exe'),
             'ppsspp_config': str(config), 'debugger_url': 'ws://127.0.0.1:56244/debugger'}
    (env / 'paths.json').write_text(json.dumps(paths, indent=2) + '\n')
    print(json.dumps(paths, indent=2))


if __name__ == '__main__':
    main()
