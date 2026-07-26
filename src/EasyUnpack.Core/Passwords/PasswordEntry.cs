namespace EasyUnpack.Core.Passwords;

public sealed record PasswordEntry(
    Guid Id,
    string Value,
    string? Label,
    DateTimeOffset? LastSuccessfulAt,
    int SuccessCount);
