namespace AppCap.Tests;

// Serializes every test that binds the machine worker's named pipe. These tests use the
// test-only RecordingIpc.PipeNameOverride, which is process-global, so they must never run
// in parallel with one another.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerPipeSerialization
{
    public const string Name = "WorkerPipe";
}
