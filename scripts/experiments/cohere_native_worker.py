"""Unrestricted research worker; never use for production insertion.

The production worker applies accuracy gates and verifies a provisioning manifest.
This worker intentionally allows long-form experiments that can reveal data loss.
"""
import argparse, json, os, sys, time, wave
from pathlib import Path

def emit(value): print(json.dumps(value), flush=True)

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--model-dir', required=True)
    p.add_argument('--backend', default='cpu')
    p.add_argument('--threads', type=int, default=12)
    p.add_argument('--runtime-root', required=True, help='Private directory containing pinned transcribe.cpp/ and native/.')
    args = p.parse_args()
    root = Path(args.runtime_root).resolve()
    sys.path.insert(0, str(root/'transcribe.cpp/bindings/python/src'))
    os.environ['TRANSCRIBE_LIBRARY'] = str(root/'native/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll')
    import numpy as np
    import transcribe_cpp as tc
    if tc.native_version() != '0.2.4':
        raise RuntimeError('This experiment requires the pinned native 0.2.4 bundle.')
    model = tc.Model(args.model_dir, backend=args.backend)
    session = model.session(n_threads=args.threads)
    emit({'status':'ready','payload':{'backend':model.backend,'native_version':tc.native_version(),'native_commit':tc.native_commit(),'pnc':model.supports('pnc'),'device':str(model.device)}})
    try:
        for line in sys.stdin:
            try:
                request = json.loads(line)
                tick = time.perf_counter()
                with wave.open(request['audio_path']) as w:
                    if (w.getnchannels(), w.getsampwidth(), w.getframerate()) != (1,2,16000): raise ValueError('Requires PCM16 mono 16kHz')
                    pcm = np.frombuffer(w.readframes(w.getnframes()),dtype='<i2').astype(np.float32)/32768
                result = session.run(pcm,language=request.get('language','en'))
                emit({'status':'ok','payload':{'text':result.text,'duration_ms':(time.perf_counter()-tick)*1000,'backend':model.backend,'dtype':'Q8_0','native_timings':str(result.timings)}})
            except Exception as exc:
                emit({'status':'error','error':str(exc)})
    finally:
        session.close()
        model.close()

if __name__ == '__main__': main()
