namespace EasyUnpack.Core.Engines;

public sealed record EngineExecutionResult(
    bool Succeeded,
    int ExitCode,
    string StandardOutput,
    string StandardError);
