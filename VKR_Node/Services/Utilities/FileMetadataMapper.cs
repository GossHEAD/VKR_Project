using Google.Protobuf.WellKnownTypes;
using VKR_Core.Models;
using VKR.Protos;

namespace VKR_Node.Services.Utilities;

public static class FileMetadataMapper
{
    public static FileMetadata MapCoreToProto(FileModel core)
    {
        if (core == null) return null;
        return new FileMetadata {
            FileName = core.FileName ?? "", FileSize = core.FileSize,
            CreationTime = core.CreationTime > DateTime.MinValue ? Timestamp.FromDateTime(core.CreationTime.ToUniversalTime()) : null,
            ModificationTime = core.ModificationTime > DateTime.MinValue ? Timestamp.FromDateTime(core.ModificationTime.ToUniversalTime()) : null,
            ContentType = core.ContentType ?? "", ChunkSize = core.ChunkSize, TotalChunks = core.TotalChunks, State = (FileState)core.State
        };
    }
}