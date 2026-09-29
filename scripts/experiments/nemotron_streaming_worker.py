"""English-only streaming comparison, separate from configured application models.

Paced WAV replay retains one recognizer stream through pauses, pads the final
audio and drains input_finished. It produces research results, never inserts text.
Requires sherpa-onnx==1.13.8 and the documented 560 ms INT8 model revision.
"""
import argparse, json, time, wave
from pathlib import Path
import numpy as np
import sherpa_onnx

p=argparse.ArgumentParser()
p.add_argument('--model-dir',required=True)
p.add_argument('--threads',type=int,default=4)
p.add_argument('--real-time',action='store_true')
a=p.parse_args(); root=Path(a.model_dir)
rec=sherpa_onnx.OnlineRecognizer.from_transducer(
    tokens=str(root/'tokens.txt'),encoder=str(root/'encoder.int8.onnx'),decoder=str(root/'decoder.int8.onnx'),
    joiner=str(root/'joiner.int8.onnx'),num_threads=a.threads,feature_dim=128,provider='cpu',
    decoding_method='greedy_search',enable_endpoint_detection=False)
print(json.dumps({'status':'ready','payload':{'backend':'sherpa-onnx/cpu','dtype':'INT8','runtime_version':sherpa_onnx.__version__,'threads':a.threads}}),flush=True)
for line in __import__('sys').stdin:
    try:
        q=json.loads(line); tick=time.perf_counter()
        if q.get('language', 'en') != 'en':
            raise ValueError('This comparison model supports English only.')
        with wave.open(q['audio_path']) as w:
            assert (w.getnchannels(),w.getsampwidth(),w.getframerate())==(1,2,16000)
            pcm=np.frombuffer(w.readframes(w.getnframes()),dtype='<i2').astype(np.float32)/32768
        stream=rec.create_stream(); backlog=0.0; compute=0.0
        for start in range(0,len(pcm),1600):
            end=min(start+1600,len(pcm))
            if a.real_time:
                delay=tick+end/16000-time.perf_counter()
                if delay>0: time.sleep(delay)
                backlog=max(backlog,time.perf_counter()-(tick+end/16000))
            step=time.perf_counter()
            stream.accept_waveform(16000,pcm[start:end])
            while rec.is_ready(stream): rec.decode_stream(stream)
            compute+=time.perf_counter()-step
        stop=time.perf_counter()
        stream.accept_waveform(16000,np.zeros(4800,dtype=np.float32))
        stream.input_finished()
        while rec.is_ready(stream): rec.decode_stream(stream)
        finalization=(time.perf_counter()-stop)*1000
        # Include any compute backlog accrued before the final frame. Flush time
        # alone can look fast while a recognizer has fallen behind live speech.
        capture_stop_to_final = max(0, (time.perf_counter()-tick-len(pcm)/16000)*1000) if a.real_time else None
        text=rec.get_result(stream)
        print(json.dumps({'status':'ok','payload':{'text':text,'duration_ms':(time.perf_counter()-tick)*1000,
          'stop_to_final_response_ms':finalization,'max_audio_clock_backlog_ms':backlog*1000,
          'paced_capture_stop_to_final_ms':capture_stop_to_final,
          'active_compute_rtf':(compute+finalization/1000)/(len(pcm)/16000),'audio_seconds':len(pcm)/16000,
          'backend':'sherpa-onnx/cpu','dtype':'INT8'}}),flush=True)
    except Exception as e: print(json.dumps({'status':'error','error':str(e)}),flush=True)
