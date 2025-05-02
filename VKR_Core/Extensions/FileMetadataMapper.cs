using Google.Protobuf.WellKnownTypes;
using VKR_Core.Models;
using VKR.Protos;

namespace VKR_Core.Extensions;

public static class FileMetadataMapper
{
    public static FileMetadata MapCoreToProto(FileModel core)
    {
        if (core == null) return null;
        return new FileMetadata {
            FileName = core.FileName ?? "",
            FileSize = core.FileSize,
            CreationTime = core.CreationTime > DateTime.MinValue ? Timestamp.FromDateTime(core.CreationTime.ToUniversalTime()) : null,
            ModificationTime = core.ModificationTime > DateTime.MinValue ? Timestamp.FromDateTime(core.ModificationTime.ToUniversalTime()) : null,
            ContentType = core.ContentType ?? "",
            ChunkSize = core.ChunkSize,
            TotalChunks = core.TotalChunks,
            State = core.State.ToProto()
        };
    }
    
    public static FileModel MapProtoToCore(FileMetadata proto)
    {
        if (proto == null) return null;
        return new FileModel
        {
            FileId = proto.FileId,
            FileName = proto.FileName,
            FileSize = proto.FileSize,
            CreationTime = proto.CreationTime?.ToDateTime().ToLocalTime() ?? DateTime.MinValue,
            ModificationTime = proto.ModificationTime?.ToDateTime().ToLocalTime() ?? DateTime.MinValue,
            ContentType = proto.ContentType,
            ChunkSize = proto.ChunkSize,
            TotalChunks = proto.TotalChunks,
            State = proto.State.ToCore() 
        };
    }
}