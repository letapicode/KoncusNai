"""Deterministic variants of authorized synthetic fixtures. Output stays beside input."""
import argparse
import json
import wave
from pathlib import Path
import numpy as np

p = argparse.ArgumentParser()
p.add_argument('--directory', required=True)
args = p.parse_args()
root = Path(args.directory).resolve()
repo = Path(__file__).resolve().parent.parent
if root.is_relative_to(repo) and root.relative_to(repo).parts[0] != 'artifacts':
    p.error('Private fixtures must be under artifacts/ or outside the checkout.')
manifest_path = root / 'manifest.json'
fixtures = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
fixtures = [f for f in fixtures if f['id'] in ('short', 'medium', 'numbers', 'long')]
for f in fixtures:
    if f['id'] == 'numbers':
        f['scoring_reference'] = 'Order number 427 costs $19. The appointment is at 3 30 on September 29.'
    else:
        f['scoring_reference'] = f['reference'].replace('three thirty', '3 30')

def load(name):
    with wave.open(str(root / (name + '.wav'))) as audio:
        assert (audio.getnchannels(), audio.getsampwidth(), audio.getframerate()) == (1, 2, 16000)
        return np.frombuffer(audio.readframes(audio.getnframes()), dtype='<i2').astype(np.float32)

def save(identifier, samples, reference):
    with wave.open(str(root / (identifier + '.wav')), 'wb') as audio:
        audio.setparams((1, 2, 16000, 0, 'NONE', 'not compressed'))
        audio.writeframes(np.clip(samples, -32768, 32767).astype('<i2').tobytes())
    fixtures.append({'id':identifier, 'audio':identifier+'.wav', 'reference':reference,
                     'scoring_reference':reference.replace('three thirty', '3 30'),
                     'language':'en', 'source':'synthetic-sapi-deterministic-variant'})

medium = load('medium')
reference = next(f['reference'] for f in fixtures if f['id'] == 'medium')
save('quiet', medium * .15, reference)
rng = np.random.default_rng(20260929)
noise_rms = np.sqrt(np.mean(medium ** 2)) / 10  # 20 dB SNR across the clip.
save('noise20db', medium + rng.normal(0, noise_rms, len(medium)), reference)
save('pauses', np.concatenate([load('short'), np.zeros(24000), medium, np.zeros(8000)]),
     next(f['reference'] for f in fixtures if f['id'] == 'short') + ' ' + reference)
long = load('long')
save('sustained', np.concatenate([long, np.zeros(16000), long, np.zeros(16000), long]),
     ' '.join([next(f['reference'] for f in fixtures if f['id'] == 'long')] * 3))
manifest_path.write_text(json.dumps(fixtures, indent=2), encoding='utf-8')
