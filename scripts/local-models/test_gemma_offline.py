"""Dependency-free checks for inference-only model loading."""
import os
import types
import unittest

import gemma_chat_worker as worker


class OfflineLoadingTests(unittest.TestCase):
    def test_normal_and_legacy_dtype_loads_remain_local(self):
        for legacy in (False, True):
            with self.subTest(legacy=legacy):
                calls = []

                class Model:
                    @staticmethod
                    def from_pretrained(model_id, **kwargs):
                        calls.append(kwargs)
                        if legacy and "dtype" in kwargs:
                            raise TypeError("legacy dtype interface")
                        return "loaded"

                torch = types.SimpleNamespace(
                    cuda=types.SimpleNamespace(is_available=lambda: False),
                    float32="float32", float16="float16")
                self.assertEqual(worker.load_model(Model, "fixture", ".", torch), "loaded")
                self.assertEqual(len(calls), 2 if legacy else 1)
                for call in calls:
                    self.assertTrue(call["local_files_only"])
                    self.assertFalse(call["trust_remote_code"])

    def test_inference_disables_hub_network_access(self):
        self.assertEqual(os.environ["HF_HUB_OFFLINE"], "1")
        self.assertEqual(os.environ["TRANSFORMERS_OFFLINE"], "1")


if __name__ == "__main__":
    unittest.main()
