"""Private, sequential actual-inference corpus benchmark; never a UI-latency estimate.

Manifest: [{id, audio (relative WAV path), reference, language, source,
           scoring_reference (optional, explicitly normalized numbers)}].
Output contains transcripts: use artifacts/ or a directory outside the checkout.
Requires psutil in the harness Python, not in the worker runtime.
"""
import argparse, hashlib, json, math, os, queue, re, subprocess, threading, time, wave
from pathlib import Path
import psutil

def words(text):
    return re.findall(r"\w+", text.casefold())

def distance(a, b):
    row = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        nxt = [i]
        for j, y in enumerate(b, 1):
            nxt.append(min(nxt[-1]+1, row[j]+1, row[j-1]+(x != y)))
        row = nxt
    return row[-1]

def percentile(values, p):
    if not values:
        return None
    return sorted(values)[max(0, math.ceil(len(values)*p)-1)]

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--python', required=True)
    p.add_argument('--script', required=True)
    p.add_argument('--model', required=True)
    p.add_argument('--manifest', required=True)
    p.add_argument('--output', required=True)
    p.add_argument('--iterations', type=int, default=6)
    p.add_argument('--worker-arg', action='append', default=[])
    p.add_argument('--warmup', action='store_true')
    p.add_argument('--fixture-id', action='append')
    args = p.parse_args()
    manifest = Path(args.manifest)
    fixtures = json.loads(manifest.read_text(encoding='utf-8-sig'))
    if args.fixture_id:
        fixtures = [fixture for fixture in fixtures if fixture['id'] in args.fixture_id]
    if not fixtures or args.iterations < 1:
        p.error('At least one fixture and one iteration are required. One iteration reports no warm statistics.')
    output = Path(args.output).resolve()
    repo = Path(__file__).resolve().parent.parent
    if output.is_relative_to(repo) and output.relative_to(repo).parts[0] != 'artifacts':
        p.error('Private output must be under ignored artifacts/ or outside the checkout.')
    output.parent.mkdir(parents=True, exist_ok=True)
    started = time.perf_counter()
    proc = subprocess.Popen([args.python, '-u', args.script, '--model-dir', args.model, *args.worker_arg], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW)
    lines = queue.Queue(maxsize=64)
    errors = []
    def read_lines():
        for line in proc.stdout: lines.put(line)
        lines.put(None)
    def read_errors():
        for line in proc.stderr:
            errors.append(line)
            if len(errors) > 100: del errors[0]
    threading.Thread(target=read_lines, daemon=True).start()
    threading.Thread(target=read_errors, daemon=True).start()
    metrics = []
    stop = threading.Event()
    def sample():
        while not stop.wait(.25):
            try:
                owned = [psutil.Process(proc.pid), *psutil.Process(proc.pid).children(recursive=True)]
                memory = [x.memory_info() for x in owned]
                metrics.append({'seconds': time.perf_counter()-started, 'working_set': sum(x.rss for x in memory), 'private_bytes': sum(getattr(x, 'private', x.vms) for x in memory), 'cpu_seconds': sum(sum(x.cpu_times()[:2]) for x in owned)})
            except psutil.Error: pass
    threading.Thread(target=sample, daemon=True).start()
    def receive():
        line = lines.get(timeout=600)
        if line is None: raise RuntimeError('Worker exited: '+''.join(errors)[-2000:])
        result = json.loads(line)
        if result.get('status') not in ('ready', 'ok'): raise RuntimeError(result.get('error', str(result)))
        return result
    try:
        ready = receive()
        load_ms = (time.perf_counter()-started)*1000
        print('Worker ready', round(load_ms), 'ms', flush=True)
        warmup_ms = None
        if args.warmup:
            tick = time.perf_counter()
            proc.stdin.write(json.dumps({'operation':'warmup', 'language':fixtures[0]['language'], 'punctuation':True})+'\n')
            proc.stdin.flush()
            response = receive()['payload']
            warmup_ms = (time.perf_counter()-tick)*1000
            print('Inference warmup', round(warmup_ms), 'ms', flush=True)
        rows = []
        for fixture in fixtures:
            audio = manifest.parent / fixture['audio']
            with wave.open(str(audio)) as w: seconds = w.getnframes()/w.getframerate()
            runs = []
            for iteration in range(args.iterations):
                tick = time.perf_counter()
                proc.stdin.write(json.dumps({'audio_path':str(audio.resolve()), 'language': fixture['language'], 'punctuation':True})+'\n')
                proc.stdin.flush()
                response = receive()['payload']
                elapsed = (time.perf_counter()-tick)*1000
                reference, hypothesis = words(fixture.get('scoring_reference', fixture['reference'])), words(response['text'])
                runs.append({'elapsed_ms':elapsed, 'worker_ms':response['duration_ms'], 'text':response['text'], 'word_errors':distance(reference, hypothesis), 'reference_words':len(reference), 'metadata':{k:v for k,v in response.items() if k not in ('text','duration_ms')}})
                print(f"{fixture['id']} {iteration+1}: {elapsed:.0f} ms errors={runs[-1]['word_errors']}/{len(reference)}", flush=True)
            warm = [x['elapsed_ms'] for x in runs[1:]]
            rows.append({'id':fixture['id'], 'source':fixture.get('source', 'operator-supplied'),
                         'audio_seconds':seconds, 'audio_sha256':hashlib.sha256(audio.read_bytes()).hexdigest(),
                         'first_ms':runs[0]['elapsed_ms'], 'warm_p50_ms':percentile(warm,.5), 'warm_p95_ms':percentile(warm,.95),
                         'warm_rtf':percentile(warm,.5)/1000/seconds if warm else None,
                         'score_normalization':'casefold, word tokens; optional explicit scoring_reference', 'runs':runs})
        output.write_text(json.dumps({'schema_version':1, 'latency_metric':'request-to-worker-response (not stop-to-visible)',
                         'max_outstanding_requests':1, 'worker':args.script, 'ready':ready, 'load_ms':load_ms,
                         'warmup_ms':warmup_ms, 'fixtures':rows, 'process_metrics':metrics},indent=2), encoding='utf-8')
    finally:
        stop.set()
        parent = psutil.Process(proc.pid) if proc.poll() is None else None
        if parent:
            for child in parent.children(recursive=True):
                try: child.kill()
                except psutil.Error: pass
            proc.kill()
        proc.wait(timeout=10)

if __name__ == '__main__': main()
