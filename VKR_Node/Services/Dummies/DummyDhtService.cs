using VKR_Core.Models;
using VKR_Core.Services;

namespace VKR_Node.Services.Dummies;

public class DummyDhtService : IDhtService { /* TODO: Реализовать методы, бросая NotImplementedException */
        public Task<NodeModel> FindSuccessorAsync(string keyId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<NodeModel?> GetPredecessorAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task NotifyAsync(NodeModel potentialPredecessor, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task StabilizeAsync(CancellationToken cancellationToken = default) { Console.WriteLine("Dummy Stabilize"); return Task.CompletedTask; } // Пример
        public Task FixFingersAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task CheckPredecessorAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public NodeModel GetCurrentNodeInfo() => throw new NotImplementedException();
        public Task JoinNetworkAsync(NodeModel bootstrapNode, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task LeaveNetworkAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
    
    
    
    