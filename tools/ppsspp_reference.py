"""Capture two debugger-controlled boot anchors from the local research PPSSPP."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import time

import websocket

ROOT = Path(__file__).resolve().parents[1]


class Debugger:
    def __init__(self, url):
        if url != 'ws://127.0.0.1:56244/debugger':
            raise ValueError('Only the research localhost endpoint is supported')
        self.socket = websocket.create_connection(url, subprotocols=['debugger.ppsspp.org'], timeout=15)
        self.counter = 0
        self.transcript = []

    def call(self, event, **params):
        self.counter += 1
        ticket = str(self.counter)
        request = dict(event=event, ticket=ticket, **params)
        self.transcript.append({'request': request})
        self.socket.send(json.dumps(request))
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            reply = json.loads(self.socket.recv())
            if reply.get('ticket') != ticket:
                continue
            self.transcript.append({'response': reply})
            if reply['event'] == 'error':
                raise RuntimeError(reply)
            return reply
        raise TimeoutError(event)

    def pause(self):
        if self.call('cpu.status')['stepping']:
            return
        self.transcript.append({'request': {'event': 'cpu.stepping'}})
        self.socket.send(json.dumps({'event': 'cpu.stepping'}))
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            if self.call('cpu.status')['stepping']:
                return
            time.sleep(0.1)
        raise TimeoutError('Pause failed')


def save_screenshot(debugger, output):
    try:
        response = debugger.call('gpu.buffer.screenshot')
    except RuntimeError:
        response = debugger.call('gpu.buffer.renderColor')
    uri = response['uri']
    if not uri.startswith('data:image/png;base64,'):
        raise ValueError('Unexpected screenshot encoding')
    output.write_bytes(base64.b64decode(uri.split(',', 1)[1]))
    # Retain image locally, avoid duplicating large screenshot payload in transcript.
    debugger.transcript[-1]['response'] = {k: v for k, v in response.items() if k != 'uri'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('extraction', type=Path)
    parser.add_argument('--name', default='boot-reference')
    args = parser.parse_args()
    if not args.name.isidentifier():
        # Permit descriptive default while rejecting traversal.
        if args.name != 'boot-reference':
            parser.error('Name must be an identifier')
    extraction = args.extraction.resolve(strict=True)
    manifest = json.loads((extraction / 'manifest.json').read_text())
    entry = next(e for e in manifest['files'] if e['path'] == 'PSP_GAME/SYSDIR/EBOOT.BIN')
    source = extraction / 'files' / entry['path']
    if hashlib.sha256(source.read_bytes()).hexdigest() != entry['sha256']:
        raise ValueError('Main ELF hash mismatch')
    output = ROOT / 'analysis' / manifest['source_sha256'] / 'stage3' / args.name
    output.mkdir(parents=True, exist_ok=False)
    paths = json.loads((ROOT / 'analysis/stage3-environment/paths.json').read_text())
    debugger = Debugger(paths['debugger_url'])
    report = {'source_sha256': manifest['source_sha256'], 'main_elf_sha256': entry['sha256'],
              'experiment': 'Two game.reset(break=true) boots; compare module map, entry PC and entry code hash.',
              'scope': 'Boot loader anchor only; no duel, UI-action or RNG determinism claim.', 'anchors': []}
    try:
        report['emulator'] = debugger.call('version', name='TagForceLocalResearch', version='1')
        report['initial_status'] = debugger.call('game.status')
        report['initial_modules'] = debugger.call('hle.module.list')
        debugger.pause()
        try:
            save_screenshot(debugger, output / 'initial-screen.png')
        except RuntimeError as error:
            report['screenshot_error'] = str(error)
        for trial in range(2):
            debugger.call('game.reset', **{'break': True})
            time.sleep(0.5)
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                status = debugger.call('cpu.status')
                if status['stepping']:
                    break
                time.sleep(0.2)
            else:
                raise TimeoutError('Did not reach a stepping boot anchor')
            anchor = {'trial': trial + 1, 'cpu': status,
                      'game': debugger.call('game.status'),
                      'modules': debugger.call('hle.module.list'),
                      'threads': debugger.call('hle.thread.list')}
            memory = debugger.call('memory.read', address=status['pc'], size=128, replacements=False)
            code = base64.b64decode(memory['base64'])
            anchor['entry_bytes_sha256'] = hashlib.sha256(code).hexdigest()
            anchor['entry_bytes_size'] = len(code)
            (output / f'entry-code-{trial + 1}.bin').write_bytes(code)
            report['anchors'].append(anchor)
        a, b = report['anchors']
        report['repeatability_checks'] = {
            'entry_pc': a['cpu']['pc'] == b['cpu']['pc'],
            'entry_code': a['entry_bytes_sha256'] == b['entry_bytes_sha256'],
            'module_map': a['modules']['modules'] == b['modules']['modules'],
            'game_identity': a['game']['game'] == b['game']['game'],
        }
        report['status'] = 'pass' if all(report['repeatability_checks'].values()) else 'divergence'
        print(json.dumps({'status': report['status'], 'repeatability_checks': report['repeatability_checks'],
                          'entry_pc': hex(a['cpu']['pc']), 'modules': a['modules']['modules'],
                          'output': str(output)}, indent=2))
    finally:
        (output / 'reference.json').write_text(json.dumps(report, indent=2) + '\n')
        (output / 'transcript.json').write_text(json.dumps(debugger.transcript, indent=2) + '\n')
        debugger.socket.close()


if __name__ == '__main__':
    main()
