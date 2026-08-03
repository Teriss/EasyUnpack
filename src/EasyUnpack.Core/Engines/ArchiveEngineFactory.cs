namespace EasyUnpack.Core.Engines;

public static class ArchiveEngineFactory
{
    public static bool IsSupported(ArchiveEngineKind kind) => kind is
        ArchiveEngineKind.SevenZip or
        ArchiveEngineKind.NanaZip or
        ArchiveEngineKind.WinRar or
        ArchiveEngineKind.Bandizip;

    public static IArchiveEngine? CreatePreferred(IReadOnlyList<ArchiveEngineDescriptor> descriptors, ArchiveEngineKind? preferredEngine = null)
        => CreateAll(descriptors, preferredEngine).FirstOrDefault();

    public static IReadOnlyList<IArchiveEngine> CreateAll(IReadOnlyList<ArchiveEngineDescriptor> descriptors, ArchiveEngineKind? preferredEngine = null)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var ordered = preferredEngine is null
            ? descriptors.OrderBy(static descriptor => GetPriority(descriptor.Kind))
            : descriptors.OrderBy(descriptor => descriptor.Kind == preferredEngine ? 0 : GetPriority(descriptor.Kind));

        var engines = new List<IArchiveEngine>();
        foreach (var descriptor in ordered)
        {
            switch (descriptor.Kind)
            {
                case ArchiveEngineKind.SevenZip:
                case ArchiveEngineKind.NanaZip:
                    engines.Add(new SevenZipEngine(descriptor));
                    break;
                case ArchiveEngineKind.WinRar:
                    engines.Add(new WinRarEngine(descriptor));
                    break;
                case ArchiveEngineKind.Bandizip:
                    engines.Add(new BandizipEngine(descriptor));
                    break;
            }
        }

        return engines;
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
