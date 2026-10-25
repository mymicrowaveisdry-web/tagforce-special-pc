import json
import unittest
from unittest.mock import patch

from tools.ppsspp_reference import Debugger


class FakeSocket:
    def __init__(self, replies):
        self.replies = iter(replies)
        self.sent = []

    def recv(self):
        return json.dumps(next(self.replies))

    def send(self, message):
        self.sent.append(json.loads(message))


class DebuggerTests(unittest.TestCase):
    def test_matches_ticket_past_unsolicited_events(self):
        socket = FakeSocket([{'event': 'input.analog'}, {'event': 'version', 'ticket': '1', 'version': 'test'}])
        with patch('tools.ppsspp_reference.websocket.create_connection', return_value=socket):
            debugger = Debugger('ws://127.0.0.1:56244/debugger')
            self.assertEqual('test', debugger.call('version')['version'])
        self.assertEqual('1', socket.sent[0]['ticket'])

    def test_api_error_is_not_success(self):
        socket = FakeSocket([{'event': 'error', 'ticket': '1', 'message': 'CPU not active'}])
        with patch('tools.ppsspp_reference.websocket.create_connection', return_value=socket):
            debugger = Debugger('ws://127.0.0.1:56244/debugger')
            with self.assertRaisesRegex(RuntimeError, 'CPU not active'):
                debugger.call('cpu.status')

    def test_rejects_nonlocal_endpoint(self):
        with self.assertRaises(ValueError):
            Debugger('ws://example.com:56244/debugger')
