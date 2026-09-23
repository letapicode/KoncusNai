import argparse
import json
import os
import sys
import time

os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")
# Inference must never acquire model files or executable code. Setup owns downloads.
os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"


DEFAULT_SYSTEM_PROMPT = (
    "You are Koncus Nai, a concise local assistant. "
    "Answer directly, avoid inventing facts, and say when you are uncertain."
)


def emit(payload: dict) -> None:
    print(json.dumps(payload, ensure_ascii=True), flush=True)


def get_value(payload: dict, *names: str, default=None):
    for name in names:
        if name in payload:
            return payload[name]

    return default


def normalize_input_line(raw_line: str) -> str:
    line = raw_line.strip().lstrip("\ufeff")
    if line.startswith("\u00ef\u00bb\u00bf"):
        line = line[3:]
    return line


def normalize_messages(raw_messages) -> list[dict]:
    messages = []
    if isinstance(raw_messages, list):
        for item in raw_messages:
            if not isinstance(item, dict):
                continue

            role = str(get_value(item, "role", "Role", default="user") or "user").strip().lower()
            content = str(get_value(item, "content", "Content", default="") or "").strip()
            if role not in ("system", "user", "assistant"):
                role = "user"
            if content:
                messages.append({"role": role, "content": content})

    if not any(message["role"] == "system" for message in messages):
        messages.insert(0, {"role": "system", "content": DEFAULT_SYSTEM_PROMPT})

    return messages


def build_inputs(processor, messages):
    try:
        text = processor.apply_chat_template(
            messages,
            tokenize=False,
            add_generation_prompt=True,
            enable_thinking=False,
        )
    except TypeError:
        text = processor.apply_chat_template(
            messages,
            tokenize=False,
            add_generation_prompt=True,
        )

    inputs = processor(text=text, return_tensors="pt")
    return inputs


def parse_response(processor, raw_response: str, fallback_response: str) -> str:
    if hasattr(processor, "parse_response"):
        try:
            parsed = processor.parse_response(raw_response)
            if isinstance(parsed, str):
                return clean_response_text(parsed)
            if isinstance(parsed, dict):
                for key in ("text", "response", "answer", "content"):
                    value = parsed.get(key)
                    if isinstance(value, str) and value.strip():
                        return clean_response_text(value)
        except Exception:
            pass

    return clean_response_text(fallback_response)


def clean_response_text(text: str) -> str:
    value = str(text or "").strip()
    for marker in ("<end_of_turn>", "<eos>"):
        marker_index = value.find(marker)
        if marker_index >= 0:
            value = value[:marker_index].strip()

    return value


def resolve_model_class():
    try:
        from transformers import AutoModelForMultimodalLM

        return AutoModelForMultimodalLM
    except ImportError:
        pass

    try:
        from transformers import AutoModelForImageTextToText

        return AutoModelForImageTextToText
    except ImportError:
        pass

    from transformers import AutoModelForCausalLM

    return AutoModelForCausalLM


def load_model(model_class, model_id: str, cache_dir: str, torch):
    kwargs = {
        "cache_dir": cache_dir,
        "trust_remote_code": False,
        "local_files_only": True,
        "device_map": "auto" if torch.cuda.is_available() else None,
    }
    try:
        return model_class.from_pretrained(
            model_id,
            dtype="auto",
            **kwargs,
        )
    except TypeError:
        return model_class.from_pretrained(
            model_id,
            torch_dtype=torch.float16 if torch.cuda.is_available() else torch.float32,
            **kwargs,
        )


def format_exception_message(exc: Exception) -> str:
    message = str(exc).strip()
    return message if message else type(exc).__name__


def load_assistant_model(model_id, cache_dir: str, torch):
    if not model_id:
        return None, None

    try:
        from transformers import AutoModelForCausalLM
    except ImportError as exc:
        return None, format_exception_message(exc)

    try:
        return load_model(AutoModelForCausalLM, model_id, cache_dir, torch), None
    except Exception as exc:
        return None, format_exception_message(exc)


def resolve_model_device(model):
    device = getattr(model, "device", None)
    if device is not None:
        return device

    try:
        return next(model.parameters()).device
    except Exception:
        return None


def resolve_tokenizer(processor):
    tokenizer = getattr(processor, "tokenizer", None)
    return tokenizer if tokenizer is not None else processor


def resolve_eos_token_ids(tokenizer) -> list[int]:
    token_ids = []
    eos_token_id = getattr(tokenizer, "eos_token_id", None)
    if isinstance(eos_token_id, int) and eos_token_id >= 0:
        token_ids.append(eos_token_id)

    convert = getattr(tokenizer, "convert_tokens_to_ids", None)
    if callable(convert):
        for token in ("<end_of_turn>", "<eos>"):
            try:
                token_id = convert(token)
            except Exception:
                token_id = None
            if isinstance(token_id, int) and token_id >= 0 and token_id not in token_ids:
                token_ids.append(token_id)

    return token_ids


def build_generation_kwargs(tokenizer, max_new_tokens: int, max_generation_seconds, assistant_model=None) -> dict:
    generation_kwargs = {
        "do_sample": False,
        "max_new_tokens": max_new_tokens,
        "use_cache": True,
    }
    if assistant_model is not None:
        generation_kwargs["assistant_model"] = assistant_model

    if max_generation_seconds is not None and max_generation_seconds > 0:
        generation_kwargs["max_time"] = float(max_generation_seconds)

    pad_token_id = getattr(tokenizer, "pad_token_id", None)
    eos_token_ids = resolve_eos_token_ids(tokenizer)
    if isinstance(pad_token_id, int) and pad_token_id >= 0:
        generation_kwargs["pad_token_id"] = pad_token_id
    elif eos_token_ids:
        generation_kwargs["pad_token_id"] = eos_token_ids[0]

    if len(eos_token_ids) == 1:
        generation_kwargs["eos_token_id"] = eos_token_ids[0]
    elif eos_token_ids:
        generation_kwargs["eos_token_id"] = eos_token_ids

    return generation_kwargs


def resolve_completion_token_count(completion) -> int:
    try:
        return int(completion.shape[-1])
    except Exception:
        try:
            return len(completion)
        except Exception:
            return 0


def resolve_last_token_id(completion):
    try:
        if resolve_completion_token_count(completion) == 0:
            return None
        value = completion[-1]
        if hasattr(value, "item"):
            value = value.item()
        return int(value)
    except Exception:
        return None


def resolve_finish_reason(completion, tokenizer, max_new_tokens: int, max_generation_seconds, elapsed_seconds: float) -> str:
    completion_tokens = resolve_completion_token_count(completion)
    last_token_id = resolve_last_token_id(completion)
    eos_token_ids = resolve_eos_token_ids(tokenizer)
    stopped_on_eos = last_token_id is not None and last_token_id in eos_token_ids
    if stopped_on_eos:
        return "stop"

    if completion_tokens >= max_new_tokens:
        return "length"

    if max_generation_seconds is not None and max_generation_seconds > 0:
        if elapsed_seconds >= max_generation_seconds - 0.25:
            return "time"

    return "stop"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-id", required=True)
    parser.add_argument("--assistant-model-id")
    parser.add_argument("--cache-dir", required=True)
    parser.add_argument("--fixture-mode", action="store_true")
    args = parser.parse_args()

    if args.fixture_mode:
        return run_fixture_worker()

    try:
        import torch
        from transformers import AutoProcessor

        processor = AutoProcessor.from_pretrained(
            args.model_id,
            cache_dir=args.cache_dir,
            trust_remote_code=False,
            local_files_only=True,
        )
        model_class = resolve_model_class()
        model = load_model(model_class, args.model_id, args.cache_dir, torch)
        if hasattr(model, "eval"):
            model.eval()
        assistant_model, assistant_model_error = load_assistant_model(args.assistant_model_id, args.cache_dir, torch)
        if assistant_model is not None and hasattr(assistant_model, "eval"):
            assistant_model.eval()
        ready_payload = {
            "assistant_model": "enabled" if assistant_model is not None else "disabled",
        }
        if assistant_model_error:
            ready_payload["assistant_model_error"] = assistant_model_error
            print(
                f"Gemma MTP assistant disabled; continuing with base model generation: {assistant_model_error}",
                file=sys.stderr,
                flush=True,
            )
        emit({"status": "ready", "payload": ready_payload})
    except Exception as exc:  # pragma: no cover - surfaced to parent process
        emit({"status": "error", "error": str(exc)})
        return 1

    for raw_line in sys.stdin:
        line = normalize_input_line(raw_line)
        if not line:
            continue

        started = time.perf_counter()
        try:
            request = json.loads(line)
            messages = normalize_messages(get_value(request, "messages", "Messages", default=[]))
            max_new_tokens = int(
                get_value(request, "max_new_tokens", "maxNewTokens", "MaxNewTokens", default=512)
                or 512
            )
            max_new_tokens = max(32, min(max_new_tokens, 8192))
            max_generation_seconds = int(
                get_value(
                    request,
                    "max_generation_seconds",
                    "maxGenerationSeconds",
                    "MaxGenerationSeconds",
                    default=165,
                )
                or 165
            )
            max_generation_seconds = max(1, min(max_generation_seconds, 3600))
            model_device = resolve_model_device(model)
            inputs = build_inputs(processor, messages)
            if model_device is not None and hasattr(inputs, "to"):
                inputs = inputs.to(model_device)
            input_len = inputs["input_ids"].shape[-1]
            tokenizer = resolve_tokenizer(processor)
            generation_kwargs = build_generation_kwargs(
                tokenizer,
                max_new_tokens,
                max_generation_seconds,
                assistant_model=assistant_model,
            )

            with torch.inference_mode():
                outputs = model.generate(
                    **inputs,
                    **generation_kwargs,
                )

            completion = outputs[0][input_len:]
            raw_response = processor.decode(completion, skip_special_tokens=False)
            fallback_response = processor.decode(completion, skip_special_tokens=True)
            elapsed_seconds = time.perf_counter() - started
            completion_tokens = resolve_completion_token_count(completion)
            finish_reason = resolve_finish_reason(
                completion,
                tokenizer,
                max_new_tokens,
                max_generation_seconds,
                elapsed_seconds,
            )
            answer = parse_response(processor, raw_response, fallback_response)
            if not answer:
                answer = clean_response_text(fallback_response)

            emit(
                {
                    "status": "ok",
                    "payload": {
                        "text": answer,
                        "duration_ms": elapsed_seconds * 1000,
                        "finish_reason": finish_reason,
                        "completion_tokens": completion_tokens,
                    },
                }
            )
        except Exception as exc:  # pragma: no cover - surfaced to parent process
            emit({"status": "error", "error": str(exc)})

    return 0


def run_fixture_worker() -> int:
    emit(
        {
            "status": "ready",
            "payload": {
                "device": "fixture",
                "model_class": "fixture.GemmaChat",
            },
        }
    )

    for raw_line in sys.stdin:
        line = normalize_input_line(raw_line)
        if not line:
            continue

        started = time.perf_counter()
        try:
            request = json.loads(line)
            messages = normalize_messages(get_value(request, "messages", "Messages", default=[]))
            last_user = ""
            for message in messages:
                if message["role"] == "user":
                    last_user = message["content"]

            answer = f"Fixture Gemma answer: {last_user}".strip()
            emit(
                {
                    "status": "ok",
                    "payload": {
                        "text": answer,
                        "duration_ms": (time.perf_counter() - started) * 1000,
                        "finish_reason": "stop",
                        "completion_tokens": 0,
                    },
                }
            )
        except Exception as exc:  # pragma: no cover - surfaced to parent process
            emit({"status": "error", "error": str(exc)})

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
