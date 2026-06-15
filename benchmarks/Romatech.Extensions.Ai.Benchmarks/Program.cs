using BenchmarkDotNet.Running;
using Romatech.Extensions.Ai.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(RagBenchmarks).Assembly).Run(args);
