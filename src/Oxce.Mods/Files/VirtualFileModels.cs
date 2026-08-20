using System.IO.Compression;

namespace Oxce.Mods.Files;

public sealed record VirtualFileProvenance
{
    public VirtualFileProvenance(string layerId, string? modId, string origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        if (modId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        }

        LayerId = layerId;
        ModId = modId;
        Origin = origin;
    }

    public string LayerId { get; }

    public string? ModId { get; }

    public string Origin { get; }
}

public class VirtualFileSource
{
    public VirtualFileSource(string relativePath, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        RelativePath = relativePath;
        SourcePath = sourcePath;
    }

    public string RelativePath { get; }

    public string SourcePath { get; }

    internal virtual Stream OpenRead() => OpenFile(SourcePath);

    internal virtual VirtualFileEntry CreateEntry(string canonicalPath, VirtualFileProvenance provenance) =>
        new(canonicalPath, SourcePath, provenance);

    internal static VirtualFileSource FromZip(
        string relativePath,
        string sourcePath,
        string archivePath,
        int entryIndex,
        string entryName) => new ZipVirtualFileSource(
            relativePath,
            sourcePath,
            archivePath,
            entryIndex,
            entryName);

    internal static FileStream OpenFile(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 81920,
        FileOptions.SequentialScan);

    internal static OwnedArchiveEntryStream OpenZipEntry(string archivePath, int entryIndex, string entryName)
    {
        var input = OpenFile(archivePath);
        ZipArchive? archive = null;
        try
        {
            archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
            if ((uint)entryIndex >= (uint)archive.Entries.Count ||
                !string.Equals(archive.Entries[entryIndex].FullName, entryName, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"ZIP entry '{entryName}' is no longer present in '{archivePath}'.");
            }

            return new OwnedArchiveEntryStream(archive.Entries[entryIndex].Open(), archive);
        }
        catch
        {
            archive?.Dispose();
            if (archive is null)
            {
                input.Dispose();
            }

            throw;
        }
    }

    internal sealed class OwnedArchiveEntryStream(Stream content, ZipArchive archive) : Stream
    {
        public override bool CanRead => content.CanRead;

        public override bool CanSeek => content.CanSeek;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position
        {
            get => content.Position;
            set => content.Position = value;
        }

        public override void Flush() => content.Flush();

        public override int Read(byte[] buffer, int offset, int count) => content.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => content.Read(buffer);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) => content.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => content.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                content.Dispose();
                archive.Dispose();
            }

            base.Dispose(disposing);
        }

    }

    private sealed class ZipVirtualFileSource : VirtualFileSource
    {
        private readonly string _archivePath;
        private readonly int _entryIndex;
        private readonly string _entryName;

        public ZipVirtualFileSource(
            string relativePath,
            string sourcePath,
            string archivePath,
            int entryIndex,
            string entryName)
            : base(relativePath, sourcePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
            ArgumentOutOfRangeException.ThrowIfNegative(entryIndex);
            ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
            _archivePath = archivePath;
            _entryIndex = entryIndex;
            _entryName = entryName;
        }

        internal override Stream OpenRead() => OpenZipEntry(_archivePath, _entryIndex, _entryName);

        internal override VirtualFileEntry CreateEntry(
            string canonicalPath,
            VirtualFileProvenance provenance) => new ZipVirtualFileEntry(
                canonicalPath,
                SourcePath,
                provenance,
                _archivePath,
                _entryIndex,
                _entryName);
    }
}

public class VirtualFileEntry
{
    internal VirtualFileEntry(
        string canonicalPath,
        string sourcePath,
        VirtualFileProvenance provenance)
    {
        CanonicalPath = canonicalPath;
        SourcePath = sourcePath;
        Provenance = provenance;
    }

    public string CanonicalPath { get; }

    public string SourcePath { get; }

    public VirtualFileProvenance Provenance { get; }

    public virtual Stream OpenRead() => VirtualFileSource.OpenFile(SourcePath);
}

internal sealed class ZipVirtualFileEntry : VirtualFileEntry
{
    private readonly string _archivePath;
    private readonly int _entryIndex;
    private readonly string _entryName;

    public ZipVirtualFileEntry(
        string canonicalPath,
        string sourcePath,
        VirtualFileProvenance provenance,
        string archivePath,
        int entryIndex,
        string entryName)
        : base(canonicalPath, sourcePath, provenance)
    {
        _archivePath = archivePath;
        _entryIndex = entryIndex;
        _entryName = entryName;
    }

    public override Stream OpenRead() =>
        VirtualFileSource.OpenZipEntry(_archivePath, _entryIndex, _entryName);
}

public sealed class DirectoryScanOptions
{
    public const int DefaultMaximumFiles = 100_000;
    public const int DefaultMaximumDepth = 64;
    public const int DefaultMaximumRelativePathLength = 4096;

    public int MaximumFiles { get; init; } = DefaultMaximumFiles;

    public int MaximumDepth { get; init; } = DefaultMaximumDepth;

    public int MaximumRelativePathLength { get; init; } = DefaultMaximumRelativePathLength;

    public bool IgnoreRulesets { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumRelativePathLength);
    }
}
