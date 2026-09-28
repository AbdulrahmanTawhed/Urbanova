using Xunit;

// All integration tests share LocalDB databases (Urbanova_Test) — never run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
