# Benchmark language scope

The product has two different benchmarks and they must not be confused:

- `CpuCalibrationBenchmarkService` is a lightweight hardware calibration used for UI guidance. Its provider/model tiers are current descriptors, but its timings are estimates derived from a repeatable CPU routine—not measured ASR inference.
- `tools/DictateAnywhere.ModelBenchmark` runs an installed Cohere or CrisperWhisper model against a non-sensitive audio fixture and records actual cold, warm-average, and p95 elapsed time.

Recommendations resolve the requested BCP-47 language to an exact calibration scope or primary-language scope. If no calibration exists, the result records that it evaluated the English scope; it must not imply the requested language was measured.

For performance decisions, use the model benchmark and capture provider ID, model ID, runtime/model revision, language, fixture identity and duration, iteration count, hardware, cold/warm timing, and accuracy result. The performance hardening backlog additionally requires end-to-end stop-to-visible-text stages and worker memory before architecture changes are justified.
