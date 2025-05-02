using Google.Protobuf.WellKnownTypes;
using VKR_Core.Extensions;
using VKR_Core.Models;
using VKR_Node.Persistance.Entities;
using VKR.Protos;

namespace VKR_Node.Services.Utilities.Mappers;

public static class FileMetadataMapper
{
    public static FileModel ToCore(FileEntity entity)
    {
        return new FileModel
        {
            FileId = entity.FileId,
            FileName = entity.FileName,
            FileSize = entity.FileSize,
            CreationTime = entity.CreationTime,
            ModificationTime = entity.ModificationTime,
            ContentType = entity.ContentType,
            ChunkSize = (int)entity.ChunkSize,
            TotalChunks = entity.TotalChunks,
            State = entity.State.ToFileStateCore() // Convert int to enum
        };
    }
    
    public static FileEntity ToEntity(FileModel core)
    {
        return new FileEntity
        {
            FileId = core.FileId,
            FileName = core.FileName,
            FileSize = core.FileSize,
            CreationTime = core.CreationTime,
            ModificationTime = core.ModificationTime,
            ContentType = core.ContentType,
            ChunkSize = core.ChunkSize,
            TotalChunks = core.TotalChunks,
            State = core.State.ToInt() // Convert enum to int
        };
    }
}