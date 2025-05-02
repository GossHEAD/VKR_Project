using Grpc.Core;
using Microsoft.Extensions.Logging;
using VKR.Protos;
using VKR_Node.Services.FileService.FileInterface;
using VKR_Node.Services.NodeServices.NodeInterfaces;


namespace VKR_Node.Services
{
    // Updated StorageServiceImpl.cs
    public class StorageServiceImpl : StorageService.StorageServiceBase
    {
        private readonly IFileStorageService _fileService;
        private readonly INodeStatusService _nodeStatusService;
        private readonly INodeConfigService _nodeConfigService;
        private readonly ILogger<StorageServiceImpl> _logger;

        public StorageServiceImpl(
            IFileStorageService fileService,
            INodeStatusService nodeStatusService,
            INodeConfigService nodeConfigService,
            ILogger<StorageServiceImpl> logger)
        {
            _fileService = fileService;
            _nodeStatusService = nodeStatusService;
            _nodeConfigService = nodeConfigService;
            _logger = logger;
        }

        // Simply delegate to the appropriate service
        public override Task<ListFilesReply> ListFiles(
            ListFilesRequest request,
            ServerCallContext context)
        {
            return _fileService.ListFiles(request, context);
        }

        public override Task<UploadFileReply> UploadFile(
            IAsyncStreamReader<UploadFileRequest> requestStream,
            ServerCallContext context)
        {
            return _fileService.UploadFile(requestStream, context);
        }

        public override Task DownloadFile(
            DownloadFileRequest request,
            IServerStreamWriter<DownloadFileReply> responseStream,
            ServerCallContext context)
        {
            return _fileService.DownloadFile(request, responseStream, context);
        }

        public override Task<DeleteFileReply> DeleteFile(
            DeleteFileRequest request,
            ServerCallContext context)
        {
            return _fileService.DeleteFile(request, context);
        }

        public override Task<GetFileStatusReply> GetFileStatus(
            GetFileStatusRequest request,
            ServerCallContext context)
        {
            return _fileService.GetFileStatus(request, context);
        }

        public override Task<GetNodeStatusesReply> GetNodeStatuses(
            GetNodeStatusesRequest request,
            ServerCallContext context)
        {
            return _nodeStatusService.GetNodeStatuses(request, context);
        }

        public override Task<GetNodeConfigurationReply> GetNodeConfiguration(
            GetNodeConfigurationRequest request,
            ServerCallContext context)
        {
            return _nodeConfigService.GetNodeConfiguration(request, context);
        }
    }
}