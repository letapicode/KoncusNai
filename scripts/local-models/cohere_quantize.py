"""Compiler-free Cohere Q8_0 conversion using the pinned release's ggml C ABI.

The scoped policy mirrors transcribe.cpp 0.2.4 tools/transcribe-quantize/policy.cpp.
No lower quantization or model-family substitution is supported.
"""
import ctypes
from pathlib import Path


def target_type(name, ne0):
    # Cohere norm/BN/bias/position/frontend and convolution kernels remain F32.
    if (name.endswith((".bias", ".running_mean", ".running_var", ".pos_bias_u", ".pos_bias_v", ".pos_enc",
                       ".final_norm.weight", ".embed.norm.weight"))
            or ".bn." in name or "norm_" in name and name.endswith(".weight")
            or name in ("frontend.mel_filterbank", "frontend.window")):
        return 0  # F32 (generic other scalars also remain F32)
    if "enc.blocks." in name and name.endswith((".conv.pointwise1.weight", ".conv.pointwise2.weight")):
        return 1 if ne0 == 1 else 8 if ne0 % 32 == 0 else 1
    if ".conv." in name and name.endswith(".weight"):
        return 0
    return 8 if ne0 % 32 == 0 else 1


def quantize(source, destination, library):
    import numpy as np
    import gguf
    from gguf.quants import dequantize
    lib = ctypes.CDLL(str(Path(library).resolve()))
    lib.ggml_quantize_chunk.argtypes = [ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p,
                                      ctypes.c_int64, ctypes.c_int64, ctypes.c_int64, ctypes.c_void_p]
    lib.ggml_quantize_chunk.restype = ctypes.c_size_t
    reader = gguf.GGUFReader(str(source))
    if reader.fields["general.architecture"].contents() != "cohere_asr":
        raise ValueError("Only the configured Cohere architecture can be quantized.")
    writer = gguf.GGUFWriter(str(destination), "cohere_asr")
    for name, field in reader.fields.items():
        if name.startswith("GGUF.") or name == "general.architecture":
            continue
        writer.add_key_value(name, 7 if name == "general.file_type" else field.contents(),
                             field.types[0], field.types[-1] if len(field.types) > 1 else None)
    plans = []
    for tensor in reader.tensors:
        shape = tuple(int(x) for x in reversed(tensor.shape))
        ne0 = int(tensor.shape[0])
        dtype = target_type(tensor.name, ne0)
        elements = int(np.prod(shape))
        size = elements * 4 if dtype == 0 else elements * 2 if dtype == 1 else elements // 32 * 34
        writer.add_tensor_info(tensor.name, shape, np.dtype("float32"), size,
                               raw_dtype=gguf.GGMLQuantizationType(dtype))
        plans.append((tensor, dtype, size, ne0, elements))
    writer.write_header_to_file()
    writer.write_kv_data_to_file()
    writer.write_ti_data_to_file()
    try:
        for index, (tensor, dtype, size, ne0, elements) in enumerate(plans):
            if int(tensor.tensor_type) == dtype:
                encoded = tensor.data.view(np.uint8)
            else:
                values = np.ascontiguousarray(dequantize(tensor.data, tensor.tensor_type), dtype=np.float32)
                encoded = np.empty(size, dtype=np.uint8)
                written = lib.ggml_quantize_chunk(dtype, values.ctypes.data, encoded.ctypes.data,
                                                  0, elements // ne0, ne0, None)
                if written != size:
                    raise RuntimeError("Pinned ggml quantization returned an incompatible tensor size.")
            writer.write_tensor_data(encoded)
            if index % 200 == 0:
                print(f"Quantizing tensor {index + 1}/{len(plans)}", flush=True)
    finally:
        writer.close()


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("source")
    parser.add_argument("destination")
    parser.add_argument("library")
    args = parser.parse_args()
    quantize(args.source, args.destination, args.library)
