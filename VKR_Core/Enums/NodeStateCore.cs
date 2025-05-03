namespace VKR_Core.Enums;

/// <summary>
/// Defines the possible states of a node within the distributed system.
/// Used in the Core layer and mapped to/from integer storage in the database
/// and the corresponding Protobuf enum.
/// </summary>
public enum NodeStateCore
{
    /// <summary>
    /// State is unknown or node is considered offline/unreachable.
    /// Corresponds to Protobuf NODE_STATE_UNKNOWN (0) and NODE_STATE_OFFLINE (1).
    /// We map both Proto Unknown and Offline to this single Offline state internally for simplicity,
    /// unless a distinction is strictly needed.
    /// </summary>
    Offline = 0, // Combining Proto Unknown(0) and Offline(1) for simplicity

    /// <summary>
    /// Node is in the process of joining the network.
    /// Corresponds to Protobuf NODE_STATE_JOINING (2).
    /// </summary>
    Joining = 2, // Matches Protobuf enum value

    /// <summary>
    /// Node is online and considered fully operational.
    /// Corresponds to Protobuf NODE_STATE_ONLINE (3).
    /// </summary>
    Online = 3, // Matches Protobuf enum value

    /// <summary>
    /// Node is in the process of gracefully leaving the network.
    /// Corresponds to Protobuf NODE_STATE_LEAVING (4).
    /// </summary>
    Leaving = 4, // Matches Protobuf enum value

    /// <summary>
    /// Node is in an error state.
    /// Corresponds to Protobuf NODE_STATE_ERROR (5).
    /// </summary>
    Error = 5, // Matches Protobuf enum value
}