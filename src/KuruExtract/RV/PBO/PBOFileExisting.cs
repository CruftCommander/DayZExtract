namespace KuruExtract.RV.PBO;

internal sealed class PBOFileExisting
{
    private readonly FileEntry _fileEntry;
    private readonly PBO _pbo;

    public PBOFileExisting(FileEntry fileEntry, PBO pbo)
    {
        _fileEntry = fileEntry;
        _pbo = pbo;
        bool isConfigBin = fileEntry.FileName.EndsWith("config.bin", StringComparison.OrdinalIgnoreCase);
        IsParamFile = isConfigBin || fileEntry.FileName.EndsWith(".rvmat", StringComparison.OrdinalIgnoreCase);
        FileName = isConfigBin ? Path.ChangeExtension(fileEntry.FileName, ".cpp") : fileEntry.FileName;
    }

    public string FileName { get; }

    public bool IsParamFile { get; }

    public int DiskSize => _fileEntry.DataSize;

    public Stream OpenRead() => _pbo.GetFileEntryStream(_fileEntry);

    public void CopyTo(Stream destination) => _pbo.CopyFileTo(_fileEntry, destination);
}
