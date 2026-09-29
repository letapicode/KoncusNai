"""Whisper-small INT8 comparison; cannot replace Cohere or the Crisper fork.

Requires faster-whisper==1.2.1, ctranslate2==4.8.2 and the documented pinned model.
No VAD cropping, batches or multiple concurrent model workers.
"""
import argparse,json,time
from faster_whisper import WhisperModel
import ctranslate2
p=argparse.ArgumentParser();p.add_argument('--model-dir',required=True);p.add_argument('--threads',type=int,default=12)
a=p.parse_args()
model=WhisperModel(a.model_dir,device='cpu',compute_type='int8',cpu_threads=a.threads,num_workers=1,local_files_only=True)
print(json.dumps({'status':'ready','payload':{'backend':'faster-whisper/cpu','dtype':model.model.compute_type,'ctranslate2_version':ctranslate2.__version__,'threads':a.threads}}),flush=True)
for line in __import__('sys').stdin:
    try:
        q=json.loads(line);tick=time.perf_counter()
        segments,info=model.transcribe(q['audio_path'],language=q.get('language','en'),beam_size=5,vad_filter=False)
        text=' '.join(s.text.strip() for s in segments)
        print(json.dumps({'status':'ok','payload':{'text':text,'duration_ms':(time.perf_counter()-tick)*1000,'backend':'faster-whisper/cpu','dtype':model.model.compute_type}}),flush=True)
    except Exception as e:print(json.dumps({'status':'error','error':str(e)}),flush=True)
