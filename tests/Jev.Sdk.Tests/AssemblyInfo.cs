// AssemblyInfo.cs
// Part of Jev.Sdk.Tests.
//
// Test parallelization is disabled deliberately. Diagnostics work through process-wide
// infrastructure: ActivityListener, MeterListener, and the ambient Activity stack are shared by
// every test in the process, so two tests running concurrently observe each other's spans and
// measurements. Serializing the suite makes those assertions deterministic, and this suite is
// small enough that the cost is irrelevant.

using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
