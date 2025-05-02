using AutoMapper;
using VKR_Core.DTOs;
using VKR_Core.Extensions;
using VKR_Core.Models;
using VKR_Node.Persistance.Entities;
using VKR.Protos;

namespace VKR_Node.Mapping
{
    /// <summary>
    /// AutoMapper profile for mapping between domain models and DTOs.
    /// </summary>
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // Core Model <-> Persistence DTO
            CreateMap<ChunkModel, ChunkPersistenceDto>().ReverseMap();
            CreateMap<FileModel, FilePersistenceDto>().ReverseMap();
            CreateMap<NodeModel, NodePersistenceDto>().ReverseMap();
            
            // Persistence DTO <-> Entity
            CreateMap<ChunkPersistenceDto, ChunkEntity>()
                .ForMember(dest => dest.File, opt => opt.Ignore())
                .ForMember(dest => dest.Locations, opt => opt.Ignore())
                .ReverseMap();
                
            CreateMap<FilePersistenceDto, FileEntity>()
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => (int)src.State))
                .ForMember(dest => dest.Chunks, opt => opt.Ignore())
                .ReverseMap()
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.State.ToFileStateCore()));
                
            CreateMap<NodePersistenceDto, NodeEntity>()
                .ForMember(dest => dest.NodeId, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => (int)src.State))
                .ReverseMap()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.NodeId))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.State.ToNodeStateCore()));
                
            // Core Model <-> Protobuf
            CreateMap<ChunkModel, FileChunk>()
                .ForMember(dest => dest.Data, opt => opt.Ignore())
                .ReverseMap()
                .ForMember(dest => dest.StoredNodeId, opt => opt.Ignore())
                .ForMember(dest => dest.ChunkHash, opt => opt.Ignore());
                
            CreateMap<FileModel, FileMetadata>()
                .ForMember(dest => dest.CreationTime, opt => opt.MapFrom(src => 
                    Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(src.CreationTime.ToUniversalTime())))
                .ForMember(dest => dest.ModificationTime, opt => opt.MapFrom(src => 
                    Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(src.ModificationTime.ToUniversalTime())))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.State.ToProto()))
                .ReverseMap()
                .ForMember(dest => dest.CreationTime, opt => opt.MapFrom(src => 
                    src.CreationTime.ToDateTime().ToLocalTime()))
                .ForMember(dest => dest.ModificationTime, opt => opt.MapFrom(src => 
                    src.ModificationTime.ToDateTime().ToLocalTime()))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.State.ToCore()));
                
            CreateMap<NodeModel, NodeInfo>()
                .ForMember(dest => dest.NodeId, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.LastSeen, opt => opt.MapFrom(src => 
                    new DateTimeOffset(src.LastSeen).ToUnixTimeSeconds()))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.State.ToProto()))
                .ReverseMap()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.NodeId))
                .ForMember(dest => dest.Address, opt => opt.MapFrom(src => ExtractIpAddress(src.Address)))
                .ForMember(dest => dest.LastSeen, opt => opt.MapFrom(src => 
                    DateTimeOffset.FromUnixTimeSeconds(src.LastSeen).UtcDateTime))
                .ForMember(dest => dest.State, opt => opt.MapFrom(src => src.State.ToCore()));
        }
        
        private static string ExtractIpAddress(string address)
        {
            int colonIndex = address.LastIndexOf(':');
            return colonIndex > 0 ? address.Substring(0, colonIndex) : address;
        }
        
        private static int ExtractPort(string address)
        {
            int colonIndex = address.LastIndexOf(':');
            if (colonIndex > 0 && int.TryParse(address.Substring(colonIndex + 1), out int port))
            {
                return port;
            }
            return 0;
        }
    }
}