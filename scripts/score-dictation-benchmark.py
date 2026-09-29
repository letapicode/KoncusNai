"""Rescore private English reports with Whisper's number/contraction normalizer.

Raw text and raw edit counts remain available; normalized counts are additional.
Run with the installed Transformers runtime. Never exports transcripts to Git.
"""
import argparse
import json
from pathlib import Path
import transformers
from transformers.models.whisper.english_normalizer import EnglishTextNormalizer
import importlib.util

spec = importlib.util.spec_from_file_location('dictation_benchmark', Path(__file__).with_name('benchmark-dictation-latency.py'))
benchmark = importlib.util.module_from_spec(spec)
spec.loader.exec_module(benchmark)
p = argparse.ArgumentParser()
p.add_argument('--manifest', required=True)
p.add_argument('--report', required=True, action='append')
args = p.parse_args()
fixtures = {f['id']:f for f in json.loads(Path(args.manifest).read_text(encoding='utf-8-sig'))}
normalize = EnglishTextNormalizer({})
repo = Path(__file__).resolve().parent.parent
for name in args.report:
    path = Path(name).resolve()
    if path.is_relative_to(repo) and path.relative_to(repo).parts[0] != 'artifacts':
        p.error('Private reports must stay under artifacts/ or outside Git.')
    report = json.loads(path.read_text(encoding='utf-8-sig'))
    report['english_normalizer'] = 'Transformers ' + transformers.__version__ + ' Whisper EnglishTextNormalizer({})'
    for row in report['fixtures']:
        fixture = fixtures[row['id']]
        if fixture['language'] != 'en':
            continue
        reference = normalize(fixture['reference']).split()
        canonical = benchmark.words(fixture.get('scoring_reference', fixture['reference']))
        for run in row['runs']:
            hypothesis = normalize(run['text']).split()
            run['normalized_word_errors'] = benchmark.distance(reference, hypothesis)
            run['normalized_reference_words'] = len(reference)
            run['canonical_word_errors'] = benchmark.distance(canonical, benchmark.words(run['text']))
            run['canonical_reference_words'] = len(canonical)
    path.write_text(json.dumps(report, indent=2), encoding='utf-8')
