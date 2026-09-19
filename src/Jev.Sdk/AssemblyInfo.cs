// AssemblyInfo.cs
// Part of Jev.Sdk. Grants the test project access to the client's internal members.
//
// The internal surface exists for two reasons: the test project pins the clock and the jitter
// source so that retry timing is asserted without waiting, and it checks the serialization
// internals directly. Neither belongs in the public API, where it would be a promise to keep.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Jev.Sdk.Tests")]
