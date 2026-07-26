namespace EasyUnpack.Core.Extraction;

public interface IArchiveSourceRecycler
{
    Task RecycleAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
}
