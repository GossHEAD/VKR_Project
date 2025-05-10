using AutoMapper;
using Google.Protobuf.WellKnownTypes;
using System;
using VKR_Core.Enums;
using VKR_Core.Models;
using VKR_Node.Persistance.Entities;
using VKR.Protos;

namespace VKR_Node.Mapping
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<ChunkModel, ChunkEntity>()
                .ForMember(dest => dest.File, opt => opt.Ignore())
                .ForMember(dest => dest.Locations, opt => opt.Ignore())
                .ReverseMap()
                .ForMember(dest => dest.StoredNodeId, opt => opt.Ignore()); // StoredNodeId must be set manually
                
            CreateMap<FileModel, FileEntity>()
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => (int)src.State))
                .ForMember(dest => dest.Chunks, opt => opt.Ignore())
                .ReverseMap()
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => (FileStateCore)src.State));
                
            CreateMap<NodeModel, NodeEntity>()
                .ForMember(dest => dest.NodeId, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => (int)src.State))
                .ReverseMap()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.NodeId))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => (NodeStateCore)src.State))
                .ForMember(dest => dest.CpuUsagePercent, opt => opt.Ignore())
                .ForMember(dest => dest.MemoryUsedBytes, opt => opt.Ignore())
                .ForMember(dest => dest.MemoryTotalBytes, opt => opt.Ignore());

            // Mapping for ChunkLocationEntity
            CreateMap<ChunkLocationModel, ChunkLocationEntity>()
                .ForMember(dest => dest.StoredNodeId, opt => opt.MapFrom(src => src.NodeId))
                .ForMember(dest => dest.ChunkEntity, opt => opt.Ignore())
                .ReverseMap()
                .ForMember(dest => dest.NodeId, opt => opt.MapFrom(src => src.StoredNodeId));
                
            CreateMap<LogEntryModel, LogEntryEntity>()
                .ForMember(dest => dest.Level, opt => opt.MapFrom(src => (int)src.LogLevel))
                .ReverseMap()
                .ForMember(dest => dest.LogLevel, opt => opt.MapFrom(src => (LogLevelEnum)src.Level));
                
            CreateMap<UserModel, UserEntity>()
                .ReverseMap();
                
            CreateMap<ChunkModel, FileChunk>()
                .ForMember(dest => dest.Data, opt => opt.Ignore()) // Data must be set manually
                .ReverseMap()
                .ForMember(dest => dest.StoredNodeId, opt => opt.Ignore())
                .ForMember(dest => dest.ChunkHash, opt => opt.Ignore());
                
            CreateMap<FileModel, FileMetadata>()
                .ForMember(dest => dest.CreationTime, opt => opt.MapFrom(src => 
                    Timestamp.FromDateTime(src.CreationTime.ToUniversalTime())))
                .ForMember(dest => dest.ModificationTime, opt => opt.MapFrom(src => 
                    Timestamp.FromDateTime(src.ModificationTime.ToUniversalTime())))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => (FileState)src.State))
                .ForMember(dest => dest.TotalChunks, opt => opt.MapFrom(src => 
                    src.TotalChunks < 0 ? 0 : src.TotalChunks))
                .ForMember(dest => dest.ExpectedFileSize, opt => opt.MapFrom(src => src.FileSize))
                .ReverseMap()
                .ForMember(dest => dest.CreationTime, opt => opt.MapFrom(src => 
                    src.CreationTime.ToDateTime().ToLocalTime()))
                .ForMember(dest => dest.ModificationTime, opt => opt.MapFrom(src => 
                    src.ModificationTime.ToDateTime().ToLocalTime()))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => (FileStateCore)src.State));
                
            CreateMap<NodeModel, NodeInfo>()
                .ForMember(dest => dest.NodeId, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.LastSeen, opt => opt.MapFrom(src => 
                    new DateTimeOffset(src.LastSeen).ToUnixTimeSeconds()))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => (NodeState)src.State))
                .ReverseMap()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.NodeId))
                .ForMember(dest => dest.Address, opt => opt.MapFrom(src => ExtractIpAddress(src.Address)))
                .ForMember(dest => dest.LastSeen, opt => opt.MapFrom(src => 
                    DateTimeOffset.FromUnixTimeSeconds(src.LastSeen).UtcDateTime))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => (NodeStateCore)src.State))
                .ForMember(dest => dest.CpuUsagePercent, opt => opt.Ignore())
                .ForMember(dest => dest.MemoryUsedBytes, opt => opt.Ignore())
                .ForMember(dest => dest.MemoryTotalBytes, opt => opt.Ignore());
            
            CreateMap<FileModel, FileMetadata>()
                .ForMember(dest => dest.CreationTime, opt => opt.MapFrom(src => 
                    Timestamp.FromDateTime(src.CreationTime.ToUniversalTime())))
                .ForMember(dest => dest.ModificationTime, opt => opt.MapFrom(src => 
                    Timestamp.FromDateTime(src.ModificationTime.ToUniversalTime())))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => (FileState)(int)src.State))
                .ForMember(dest => dest.TotalChunks, opt => opt.MapFrom(src => 
                    src.TotalChunks < 0 ? 0 : src.TotalChunks))
                .ForMember(dest => dest.ExpectedFileSize, opt => opt.MapFrom(src => src.FileSize))
                .ReverseMap()
                .ForMember(dest => dest.CreationTime, opt => opt.MapFrom(src => 
                    src.CreationTime.ToDateTime().ToLocalTime()))
                .ForMember(dest => dest.ModificationTime, opt => opt.MapFrom(src => 
                    src.ModificationTime.ToDateTime().ToLocalTime()))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => (FileStateCore)(int)src.State));
            
            CreateMap<NodeModel, NodeStatusInfo>()
                .ForMember(dest => dest.NodeId, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Address, opt => opt.MapFrom(src => src.Address))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => (NodeState)src.State))
                .ForMember(dest => dest.Details, opt => opt.MapFrom(src => 
                    src.State == NodeStateCore.Online ? "Online" : 
                    src.State == NodeStateCore.Offline ? "Offline" : 
                    src.State.ToString()));
                
            CreateMap<NodeModel, GetNodeConfigurationReply>()
                .ForMember(dest => dest.NodeId, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.ListenAddress, opt => opt.MapFrom(src => src.Address))
                .ForMember(dest => dest.CpuUsagePercent, opt => opt.MapFrom(src => 
                    src.CpuUsagePercent.HasValue ? src.CpuUsagePercent.Value : 0))
                .ForMember(dest => dest.MemoryUsedBytes, opt => opt.MapFrom(src => 
                    src.MemoryUsedBytes.HasValue ? src.MemoryUsedBytes.Value : 0))
                .ForMember(dest => dest.MemoryTotalBytes, opt => opt.MapFrom(src => 
                    src.MemoryTotalBytes.HasValue ? src.MemoryTotalBytes.Value : 0))
                .ForMember(dest => dest.DiskSpaceAvailableBytes, opt => opt.MapFrom(src => 
                    src.DiskSpaceAvailableBytes.HasValue ? src.DiskSpaceAvailableBytes.Value : 0))
                .ForMember(dest => dest.DiskSpaceTotalBytes, opt => opt.MapFrom(src => 
                    src.DiskSpaceTotalBytes.HasValue ? src.DiskSpaceTotalBytes.Value : 0))
                .ForMember(dest => dest.Success, opt => opt.MapFrom(src => true))
                .ForMember(dest => dest.ErrorMessage, opt => opt.Ignore())
                .ForMember(dest => dest.StorageBasePath, opt => opt.Ignore())
                .ForMember(dest => dest.ReplicationFactor, opt => opt.Ignore())
                .ForMember(dest => dest.DefaultChunkSize, opt => opt.Ignore());
                
            CreateMap<FileModel, FileStatusInfo>()
                .ForMember(dest => dest.FileId, opt => opt.MapFrom(src => src.FileId))
                .ForMember(dest => dest.FileName, opt => opt.MapFrom(src => src.FileName))
                .ForMember(dest => dest.IsAvailable, opt => opt.MapFrom(src => 
                    src.State == FileStateCore.Available))
                .ForMember(dest => dest.CurrentReplicationFactor, opt => opt.Ignore())
                .ForMember(dest => dest.DesiredReplicationFactor, opt => opt.Ignore());
        }
        
        private static string ExtractIpAddress(string address)
        {
            int colonIndex = address?.LastIndexOf(':') ?? -1;
            return colonIndex > 0 ? address.Substring(0, colonIndex) : address;
        }
    }
}