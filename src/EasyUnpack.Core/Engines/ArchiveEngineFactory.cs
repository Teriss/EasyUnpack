namespace EasyUnpack.Core.Engines;

public static class ArchiveEngineFactory
{
    public static bool IsSupported(ArchiveEngineKind kind) => kind is
        ArchiveEngineKind.SevenZip or
        ArchiveEngineKind.NanaZip or
        ArchiveEngineKind.WinRar or
        ArchiveEngineKind.Bandizip;

    public static IArchiveEngine? CreatePreferred(IReadOnlyList<ArchiveEngineDescriptor> descriptors, ArchiveEngineKind? preferredEngine = null)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var ordered = preferredEngine is null
            ? descriptors.OrderBy(static descriptor => GetPriority(descriptor.Kind))
            : descriptors.OrderBy(descriptor => descriptor.Kind == preferredEngine ? 0 : GetPriority(descriptor.Kind));

        foreach (var descriptor in ordered)
        {
            switch (descriptor.Kind)
            {
                case ArchiveEngineKind.SevenZip:
                case ArchiveEngineKind.NanaZip:
                    return new SevenZipEngine(descriptor);
                case ArchiveEngineKind.WinRar:
                    return new WinRarEngine(descriptor);
                case ArchiveEngineKind.Bandizip:
                    return new BandizipEngine(descriptor);
            }
        }

        return null;
    }

    private static int GetPriority(ArchiveEngineKind kind) => kind switch
    {
        ArchiveEngineKind.SevenZip => 1,
        ArchiveEngineKind.NanaZip => 2,
        ArchiveEngineKind.WinRar => 3,
        ArchiveEngineKind.Bandizip => 4,
        _ => 100,
    };
}
