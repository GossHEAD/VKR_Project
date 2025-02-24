namespace VKR_Node.Utilities;

public class JoinQueue
{
    private readonly Queue<JoinRequest> _joinRequests = new Queue<JoinRequest>();
    private readonly object _lock = new object();

    public void Enqueue(JoinRequest request)
    {
        lock (_lock)
        {
            _joinRequests.Enqueue(request);
        }
    }

    public JoinRequest Dequeue()
    {
        lock (_lock)
        {
            return _joinRequests.Count > 0 ? _joinRequests.Dequeue() : null;
        }
    }
}
