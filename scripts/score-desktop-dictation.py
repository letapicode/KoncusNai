"""Score only benchmark history entries, never export private text to Git.

The desktop report supplies an exact run window and completion event per sample.
History validates microphone transcription; VerifiedInserted separately validates
visible insertion. No pre-existing history entries are copied or printed.
"""
import argparse
import importlib.util
import json
from datetime import datetime, timedelta
from pathlib import Path
from transformers.models.whisper.english_normalizer import EnglishTextNormalizer

spec = importlib.util.spec_from_file_location('benchmark', Path(__file__).with_name('benchmark-dictation-latency.py'))
benchmark = importlib.util.module_from_spec(spec)
spec.loader.exec_module(benchmark)

def timestamp(value):
    try:
        return datetime.fromisoformat(value.replace('Z', '+00:00'))
    except ValueError:
        # Earlier PowerShell evidence formatted a parsed timestamp in the host's
        # local culture, losing subsecond precision. Preserve that limitation.
        return datetime.strptime(value, '%m/%d/%Y %H:%M:%S').astimezone()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--history', required=True)
    parser.add_argument('--report', required=True)
    parser.add_argument('--manifest', required=True)
    args = parser.parse_args()
    report_path = Path(args.report).resolve()
    repo = Path(__file__).resolve().parent.parent
    if report_path.is_relative_to(repo) and report_path.relative_to(repo).parts[0] != 'artifacts':
        parser.error('Private report must stay under artifacts/ or outside Git.')
    report = json.loads(report_path.read_text(encoding='utf-8-sig'))
    start, end = timestamp(report['startedUtc']), timestamp(report['completedUtc']) + timedelta(seconds=5)
    records = []
    with Path(args.history).open(encoding='utf-8-sig') as history:
        for line in history:
            try:
                record = json.loads(line)['record']
                if start <= timestamp(record['createdUtc']) <= end and record['source'] in ('global-hotkey', 'global-hotkey-recovery'):
                    records.append(record)
            except (ValueError, KeyError):
                continue
    fixtures = {f['id']: f for f in json.loads(Path(args.manifest).read_text(encoding='utf-8-sig'))}
    normalizer = EnglishTextNormalizer({})
    samples = list(report['samples'])
    if report.get('firstUseAfterModelReady'):
        samples.append(report['firstUseAfterModelReady'])
    for sample in samples:
        completed = timestamp(sample['operationId'][:-(len(sample['label'])+1)])
        matches = [r for r in records if completed <= timestamp(r['createdUtc']) <= completed+timedelta(seconds=2)]
        if len(matches) != 1:
            raise RuntimeError(f"Benchmark history match is missing or ambiguous for {sample['label']}.")
        record = matches[0]
        fixture = fixtures[{3:'short', 7:'medium', 11:'numbers'}[sample['nominalSeconds']]]
        reference = normalizer(fixture['reference']).split()
        transcript = record['rawTranscript']
        final = record['finalText']
        sample['historyAccuracy'] = {
            'normalizer':'Whisper EnglishTextNormalizer({})',
            'referenceWords':len(reference),
            'normalizedWordErrors':benchmark.distance(reference, normalizer(transcript).split()),
            'rawWordErrors':benchmark.distance(benchmark.words(fixture['reference']), benchmark.words(transcript)),
            'canonicalWordErrors':benchmark.distance(benchmark.words(fixture.get('scoring_reference', fixture['reference'])), benchmark.words(transcript)),
            'rawTranscript':transcript, 'finalText':final,
            'source':record['source'],
        }
    report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(f'Scored {len(samples)} benchmark history entries; transcript text remains private.')

if __name__ == '__main__':
    main()
