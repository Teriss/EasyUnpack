namespace EasyUnpack.Core.Extraction;

public sealed class ArchivePasswordRequiredException : IOException
{
    public ArchivePasswordRequiredException(string archivePath, int attemptedPasswordCount)
        : base("The archive could not be opened without a valid password.")
    {
        ArchivePath = archivePath;
        AttemptedPasswordCount = attemptedPasswordCount;
    }

    public string ArchivePath { get; }
    public int AttemptedPasswordCount { get; }
}
