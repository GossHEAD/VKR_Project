﻿using VKR_Core.Enums;
using VKR_Core.Models.Interfaces;

namespace VKR_Core.Models;

public record class FileModel : IFile
{
    public required string FileId { get; set; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public DateTime CreationTime { get; init; }
    public DateTime ModificationTime { get; init; }
    public string? ContentType { get; init; }
    public int ChunkSize { get; init; }
    public int TotalChunks { get; init; }
    public FileStateCore State { get; init; } = FileStateCore.Unknown;
}