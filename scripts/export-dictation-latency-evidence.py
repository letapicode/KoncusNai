"""Export an explicit numeric whitelist from private benchmark reports.

Never copies transcripts, references, history IDs, absolute paths, or raw logs.
The input inventory is the documented 2026-09-29 experiment series.
"""
import argparse
import json
import math
from pathlib import Path

def percentile(values, fraction):
    return sorted(values)[math.ceil(len(values)*fraction)-1] if values else None

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def desktop(report):
    groups = []
    for seconds in (3, 7, 11):
        rows = [r for r in report['samples'] if r['nominalSeconds'] == seconds and r['acceptedByAutomation']]
        groups.append({
            'nominal_seconds':seconds, 'accepted_samples':len(rows),
            'stop_to_visible_p50_ms':percentile([r['stopToVisibleMs'] for r in rows], .5),
            'stop_to_visible_p95_ms':percentile([r['stopToVisibleMs'] for r in rows], .95),
            'actual_backends':sorted({r['actualBackend'] for r in rows}),
            'precisions':sorted({r['actualPrecision'] for r in rows}),
            'reference_words_scored':sum(r['historyAccuracy']['referenceWords'] for r in rows),
            'normalized_word_errors':sum(r['historyAccuracy']['normalizedWordErrors'] for r in rows),
            'canonical_word_errors':sum(r['historyAccuracy']['canonicalWordErrors'] for r in rows),
            'peak_private_bytes':max(r['peakPrivateBytes'] for r in rows),
            'peak_working_set_bytes':max(r['peakWorkingSetBytes'] for r in rows),
            'peak_backlog_ms':max(r['peakBacklogMs'] for r in rows),
            'samples':[{k:r[k] for k in ('stopToVisibleMs', 'captureFinalizationMs', 'transcriptionWallMs', 'insertionMs', 'recordingWindowMs', 'capturedAudioMs')} for r in rows],
        })
    return {'groups':groups, 'rejected_samples':len(report['samples'])-sum(g['accepted_samples'] for g in groups),
            'rejection_reasons':sorted({reason for r in report['samples'] for reason in r['rejectionReasons']}),
            'first_use_after_model_ready_ms':report['firstUseAfterModelReady']['stopToVisibleMs'],
            'operator_verification':report['operatorVerification'],
            'insertion_verification':'application UI Automation VerifiedInserted; manual operator verification remains pending'}

def worker(report):
    rows = []
    ready = report['ready']['payload']
    ready_backend = ready.get('backend') or ('transformers/'+ready['device'] if ready.get('device') in ('cpu', 'cuda') else None)
    for fixture in report['fixtures']:
        runs = fixture['runs']
        row = {k:fixture.get(k) for k in ('id', 'audio_seconds', 'audio_sha256', 'first_ms', 'warm_p50_ms', 'warm_p95_ms')}
        row['warm_rtf'] = fixture['warm_p50_ms']/1000/fixture['audio_seconds'] if fixture.get('warm_p50_ms') is not None else None
        row.update({
            'samples':len(runs), 'warm_samples':len(runs)-1,
            'backends':sorted({str(r['metadata'].get('backend') or ready_backend) for r in runs}),
            'precisions':sorted({str(r['metadata'].get('dtype') or ready.get('dtype')) for r in runs}),
            'model_load_ms_per_request':[r['metadata'].get('model_load_ms') for r in runs],
            'canonical_word_errors':[r['canonical_word_errors'] for r in runs],
            'canonical_reference_words':runs[0]['canonical_reference_words'],
            'normalized_word_errors':[r['normalized_word_errors'] for r in runs],
            'normalized_reference_words':runs[0]['normalized_reference_words'],
            'fallback_reasons':sorted({r['metadata']['fallback_reason'] for r in runs if r['metadata'].get('fallback_reason')}),
        })
        if 'paced_capture_stop_to_final_ms' in runs[0]['metadata']:
            row['paced_capture_stop_to_final_ms'] = [r['metadata']['paced_capture_stop_to_final_ms'] for r in runs]
            row['active_compute_rtf'] = [r['metadata']['active_compute_rtf'] for r in runs]
            row['max_audio_clock_backlog_ms'] = [r['metadata']['max_audio_clock_backlog_ms'] for r in runs]
        rows.append(row)
    return {'latency_metric':'request to worker response; paced streaming reported separately, never visible insertion',
            'ready_ms':report['load_ms'], 'inference_warmup_ms':report.get('warmup_ms'),
            'model_load_ms':report['ready']['payload'].get('model_load_ms'),
            'peak_private_bytes':max(r['private_bytes'] for r in report['process_metrics']),
            'peak_working_set_bytes':max(r['working_set'] for r in report['process_metrics']), 'fixtures':rows}

def ui(report):
    values = [r['elapsedMs'] for r in report['samples']]
    return {'metric':report['metric'], 'samples':len(values), 'p50_ms':percentile(values, .5),
            'p95_ms':percentile(values, .95), 'maximum_ms':max(values),
            'timeouts':sum(not r['responded'] for r in report['samples']), 'timeout_ms':100}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--directory', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    root = Path(args.directory)
    before = read(root/'desktop-before/application-evidence-raw.json')
    after = read(root/'desktop-after/application-evidence-raw.json')
    before_summary, after_summary = desktop(before), desktop(after)
    improvements = []
    for old, new in zip(before_summary['groups'], after_summary['groups']):
        improvements.append({'nominal_seconds':old['nominal_seconds'],
                             'p50_improvement_percent':(1-new['stop_to_visible_p50_ms']/old['stop_to_visible_p50_ms'])*100,
                             'p95_improvement_percent':(1-new['stop_to_visible_p95_ms']/old['stop_to_visible_p95_ms'])*100,
                             'raw_history_transcript_sets_equal':{r['historyAccuracy']['rawTranscript'] for r in before['samples'] if r['nominalSeconds']==old['nominal_seconds']} == {r['historyAccuracy']['rawTranscript'] for r in after['samples'] if r['nominalSeconds']==old['nominal_seconds']}})
    regression = read(root/'regression-native/model-benchmark.json')['Measurements'][0]
    metrics = read(root/'regression-native/model-benchmark-process-metrics.json')
    cancelled_at = regression['Lifecycle']['CancellationCompletedUtc']
    # Last process observations occur after cancellation in this documented run.
    tail = metrics['samples'][-3:]
    inventory = ['baseline', 'native-cpu4', 'native-cpu8', 'native-cpu12', 'native-vulkan',
                 'integrated-vulkan', 'transformers-warmup', 'nemotron-cpu4', 'nemotron-paced',
                 'faster-whisper-small-int8', 'long-fallback', 'transformers-speech-warmup', 'native-speech-warmup']
    evidence = {
        'schema_version':1, 'measured_date':'2026-09-29',
        'code_base_commit':'aa3a31dcc99f0740bc518fa73a82798a127c3c6d',
        'code_state':'uncommitted implementation described in adjacent report',
        'scope':'authorized English Windows SAPI synthetic speech; no real-speaker or multilingual validation',
        'hardware':{'cpu':'i7-1260P', 'physical_cores':12, 'logical_processors':16, 'gpu':'Intel Iris Xe', 'ram_bytes':16774152192},
        'installed_runtime':{'python':'3.11.9', 'torch':'2.11.0+cpu', 'transformers':'5.8.1', 'dotnet':'8.0.24', 'os':'Windows 10.0.26300.0'},
        'cohere_revision':'32d9e4ba6271d78168c095c2f90bc173eaad97d2',
        'native':{'version':'0.2.4', 'commit':'4807edaf210d0d7e8a6f7fb2a44b65966a2797f0', 'header_hash':'7df72bf9e667b8c2', 'archive_sha256':'09705f54218817c065602ada8fd0f4d13b3f7fbb9d94929eeb3789c6c2b1f34a', 'precision':'Q8_0 with remaining F16/F32 tensors', 'backend':'Vulkan0', 'threads':12, 'production_gate':'English, punctuation enabled, at most 45 seconds per provider request; otherwise sticky Transformers fallback'},
        'desktop':{'baseline':before_summary, 'native':after_summary, 'improvements':improvements,
                   'baseline_repeat':desktop(read(root/'desktop-before-ui/application-evidence-raw.json'))},
        'ui_responsiveness':{'baseline':ui(read(root/'desktop-before-ui/ui-responsiveness.json')), 'native':ui(read(root/'ui-after.json'))},
        'native_regression':{'iterations':20, 'warm_samples':19, 'first_after_warmup_ms':regression['FirstRequestMs'],
                             'warm_p50_ms':regression['WarmP50Ms'], 'warm_p95_ms':regression['WarmP95Ms'],
                             'full_reference_accuracy_passed':regression['AccuracyPassed'], 'warmup':regression['Warmup'],
                             'cancellation_observed':regression['Lifecycle']['CancellationObserved'],
                             'post_cancellation_python_counts':[r['pythonProcessCount'] for r in tail],
                             'peak_private_bytes':metrics['peak']['privateBytes']},
        'worker_experiments':{name:worker(read(root/(name+'.json'))) for name in inventory},
        'caveats':['p95 of five desktop samples is the maximum, not a robust population tail estimate',
                   'private bytes are process commit; GPU driver allocation and VRAM residency are not measured',
                   'streaming uses paced WAV replay; no streaming stop-to-visible application measurement',
                   'short desktop native p95 regressed; no universal latency improvement claim',
                   'unrestricted 136-second native output lost two passages despite EOS; never approved for insertion'],
    }
    # Keep the machine-readable evidence compact; the adjacent Markdown report
    # provides the readable tables. Whitespace changes must not discard samples.
    Path(args.output).write_text(json.dumps(evidence, separators=(',', ':'))+'\n', encoding='utf-8')
    print('Exported numeric evidence without private payloads.')

if __name__ == '__main__':
    main()
