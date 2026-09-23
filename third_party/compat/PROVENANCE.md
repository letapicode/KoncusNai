# Indic Parler compatibility sources

These source trees are narrow compatibility patches used by the local Indic Parler inference runtime. They are included as source so setup never executes a moving Git branch.

## Parler-TTS

- Upstream: https://github.com/huggingface/parler-tts
- Upstream commit: `d108732cd57788ec86bc857d99a6cabd66663d68`
- Upstream archive SHA-256: `216767fadd90807a8bb8d81dbe9e9e6b9ad91f76e8371fcfa93a1d59e99f273a`
- License: Apache-2.0 (`parler-tts/LICENSE`)
- Local version: `0.2.2+koncus1`
- Patch scope: Transformers 5 generation mixin/config compatibility, `torch.isin`, safe weight tying, and generation defaults. Runtime generation uses the legacy cache path because Transformers 5 `DynamicCache` is incompatible with this upstream model implementation.

## AudioTools

- Upstream: https://github.com/descriptinc/audiotools
- Upstream commit: `348ebf2034ce24e2a91a553e3171cb00c0c71678`
- Upstream archive SHA-256: `c93fc09f338eec2e8b08f39af18699cf75f24db156802590fa87934dafd3b9a1`
- License: MIT (`audiotools/LICENSE`)
- Local version: `0.7.4+koncus1`
- Patch scope: allow exactly protobuf 7.36.2. AudioTools does not directly import protobuf; the patched graph was validated through real Indic model inference.

The inference-only patch also loads torchaudio lazily inside optional editing
operations and removes the unused `torch-stoi` dependency. Indic synthesis does
not call those editing or quality-metric operations.

## Descript Audio Codec

- Upstream: https://github.com/descriptinc/descript-audio-codec
- Upstream commit/tag: `c7cfc5d2647e26471dc394f95846a0830e7bec34` (`1.0.0`)
- Upstream archive SHA-256: `e9817efb973b9913b7cbda1fca2e771f065c27a8b502f489926354fa7fa3e35d`
- License: MIT (`descript-audio-codec/LICENSE`)
- Local version: `1.0.0+koncus1`
- Patch scope: remove the declared torchaudio dependency. The codec path used
  by Indic Parler operates on PyTorch tensors and does not import torchaudio.

`MANIFEST.sha256` covers every shipped compatibility-source file. The runtime provisioner verifies it before installing the local packages.
