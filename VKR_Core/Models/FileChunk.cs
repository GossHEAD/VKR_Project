using System;

namespace VKR_Core.Models
{
    public class FileChunk
    {
        public string Id { get; set; }
        public string FileId { get; set; }
        public int ChunkIndex { get; set; }
        public long Size { get; set; }
        public string Hash { get; set; }
        public string NodeId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
} 