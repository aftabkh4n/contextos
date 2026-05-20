# ContextOS Performance

Results from running `dotnet run --project benchmarks/ContextOS.Bench`.

| Date | Provider | Insert | Recall (worst of 5) | Hydrate |
|------|----------|--------|---------------------|---------|
| 2026-05-20 | onnx | 15.6ms/insert | 158ms recall | 19ms hydrate |
