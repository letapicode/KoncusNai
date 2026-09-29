"""Pad the authorized corpus to the desktop harness's exact recording lengths."""
import argparse
import wave
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--directory', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args()
root, output = Path(args.directory), Path(args.output).resolve()
repo = Path(__file__).resolve().parent.parent
if output.is_relative_to(repo) and output.relative_to(repo).parts[0] != 'artifacts':
    parser.error('Audio must stay under ignored artifacts/ or outside Git.')
output.mkdir(parents=True, exist_ok=True)
for seconds, name in ((3, 'short'), (7, 'medium'), (11, 'numbers')):
    with wave.open(str(root/(name+'.wav'))) as source:
        if (source.getnchannels(), source.getsampwidth(), source.getframerate()) != (1, 2, 16000):
            raise ValueError('Requires PCM16 mono 16 kHz fixtures.')
        pcm = source.readframes(source.getnframes())
    pcm = pcm[:seconds*32000] + bytes(max(0, seconds*32000-len(pcm)))
    with wave.open(str(output/f'cohere-healthcheck-{seconds}.wav'), 'wb') as target:
        target.setparams((1, 2, 16000, 0, 'NONE', 'not compressed'))
        target.writeframes(pcm)
