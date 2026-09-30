import contextlib
import io
import json
import os
import sys
import types
import tempfile
import wave
import unittest
from unittest.mock import patch

import cohere_transcribe_worker as worker
from cohere_native_runtime import NativeRuntime, NativeInputOutsideValidation


class CohereRuntimeTests(unittest.TestCase):
    def test_all_generated_chunks_require_eos(self):
        worker.ensure_complete([[13764, 4, 3, 2], [13764, 9, 3]], 3)
        with self.assertRaisesRegex(RuntimeError, 'truncated'):
            worker.ensure_complete([[13764, 4, 3], [13764, 9, 8]], 3)
        with self.assertRaisesRegex(RuntimeError, 'truncated'):
            worker.ensure_complete([[13764, 9]], None)
        with self.assertRaisesRegex(RuntimeError, 'truncated'):
            worker.ensure_complete([], 3)

    def test_unvalidated_languages_and_punctuation_use_fallback(self):
        self.assertTrue(NativeRuntime.supports('en', True))
        self.assertFalse(NativeRuntime.supports('en', False))
        self.assertFalse(NativeRuntime.supports('ja', True))

    def test_long_audio_is_rejected_before_native_compute(self):
        runtime = NativeRuntime.__new__(NativeRuntime)
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, 'synthetic.wav')
            with wave.open(path, 'wb') as audio:
                audio.setparams((1, 2, 16000, 0, 'NONE', 'not compressed'))
                audio.writeframes(bytes(46 * 32000))
            with self.assertRaises(NativeInputOutsideValidation):
                runtime.transcribe(path, 'en', True)

    def test_warmup_is_idempotent_and_never_returns_tentative_text(self):
        events = []
        class FakeRuntime:
            metadata = {'backend':'transformers/cpu', 'dtype':'float32'}
            def __init__(self, path): pass
            def warmup(self, language, punctuation): events.append(('warm', language, punctuation))
            def transcribe(self, path, language, punctuation): return 'final speech', 2.0
            def close(self): events.append(('close',))
        requests = [{'operation':'warmup', 'language':'es', 'punctuation':False}] * 2
        requests += [{'audio_path':'synthetic.wav', 'language':'es', 'punctuation':False}]
        responses = self.run_worker(requests, FakeRuntime)
        self.assertEqual([('warm', 'es', False), ('close',)], events)
        self.assertEqual('', responses[1]['payload']['text'])
        self.assertNotIn('text', responses[2]['payload'])
        self.assertEqual('final speech', responses[3]['payload']['text'])

    def test_native_failure_frees_model_before_loading_sticky_fallback(self):
        events = []
        class FakeTransformers:
            metadata = {'backend':'transformers/cpu', 'dtype':'float32'}
            def __init__(self, path):
                self_test.assertEqual('native-close', events[-1])
                self_test.assertEqual('12', os.environ['OMP_NUM_THREADS'])
                events.append('fallback-load')
            def transcribe(self, *args): return 'complete fallback text', 2.0
            def close(self): pass
        class FakeNative:
            metadata = {'backend':'transcribe.cpp/Vulkan0', 'dtype':'Q8_0'}
            def __init__(self, *args):
                events.append('native-load')
                os.environ['OMP_NUM_THREADS'] = '1'
            def supports(self, *args): return True
            def transcribe(self, *args): raise RuntimeError('incomplete')
            def close(self): events.append('native-close')
        self_test = self
        module = types.SimpleNamespace(NativeRuntime=FakeNative)
        with patch.dict(sys.modules, {'cohere_native_runtime':module}):
            responses = self.run_worker([{'audio_path':'test.wav'}] * 2, FakeTransformers, native=True)
        self.assertEqual(['native-load', 'native-close', 'fallback-load'], events)
        for response in responses[1:]:
            self.assertEqual('complete fallback text', response['payload']['text'])
            self.assertEqual('transformers/cpu', response['payload']['backend'])
            self.assertEqual('native_request_failed:RuntimeError', response['payload']['fallback_reason'])

    @staticmethod
    def run_worker(requests, runtime, native=False, mode='automatic'):
        output = io.StringIO()
        environment = {'DICTATEANYWHERE_COHERE_NATIVE_MANIFEST':'fake.json'} if native else {}
        with patch.dict(os.environ, environment, clear=True), patch.object(worker, 'TransformersRuntime', runtime), \
             patch.object(worker, 'inference_lease', contextlib.nullcontext), \
             patch.object(worker, 'report_runtime_status'), \
             patch('cohere_runtime_selection.resolve_selection', return_value=(None, None)), \
             patch.object(sys, 'argv', ['worker', '--model-dir', 'installed', '--threads', '12', '--runtime-mode', mode]), \
             patch.object(sys, 'stdin', io.StringIO(''.join(json.dumps(r)+'\n' for r in requests))), \
             contextlib.redirect_stdout(output):
            assert worker.main() == 0
        return [json.loads(line) for line in output.getvalue().splitlines()]

    def test_cpu_override_forces_original_cpu_when_native_is_unavailable(self):
        forces = []
        class FakeTransformers:
            metadata = {'backend': 'transformers/cpu', 'dtype': 'float32'}
            def __init__(self, path, force_cpu=False): forces.append(force_cpu)
            def close(self): pass
        self.run_worker([], FakeTransformers, mode='cpu')
        self.assertEqual([True], forces)


if __name__ == '__main__':
    unittest.main()
