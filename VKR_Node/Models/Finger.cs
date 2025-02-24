using VKR_Node.Models;

namespace VKR_Common.Models;

/// <summary>
/// Represents a single entry in the finger table.
/// </summary>
public class Finger
{
    public Guid Start { get; private set; }       // Start of the interval
    public Guid End { get; private set; }         // End of the interval
    public Node Successor { get; set; }          // Node responsible for this interval

    public Finger(Guid start, Guid end, Node successor)
    {
        Start = start;
        End = end;
        Successor = successor;
    }

    public override string ToString()
    {
        return $"Finger: Start={Start}, End={End}, Successor={Successor?.Identifier}";
    }
}