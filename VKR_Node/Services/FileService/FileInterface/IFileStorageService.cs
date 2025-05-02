using Grpc.Core;
using VKR.Protos;

namespace VKR_Node.Services.FileService.FileInterface;

/// <summary>
/// Service for handling file downloads from the distributed storage system.
/// </summary>
public interface IFileStorageService
{
    Task<ListFilesReply> ListFiles(ListFilesRequest request, ServerCallContext context);
    Task<UploadFileReply> UploadFile(IAsyncStreamReader<UploadFileRequest> requestStream, ServerCallContext context);
    Task DownloadFile(DownloadFileRequest request, IServerStreamWriter<DownloadFileReply> responseStream, ServerCallContext context);
    Task<DeleteFileReply> DeleteFile(DeleteFileRequest request, ServerCallContext context);
    Task<GetFileStatusReply> GetFileStatus(GetFileStatusRequest request, ServerCallContext context);
}