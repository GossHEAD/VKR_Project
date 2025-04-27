namespace VKR_Core.Enums;

public enum FileStateCore
    {
        /// <summary>
        /// State is unknown or not yet determined.
        /// Corresponds to Protobuf FILE_STATE_UNKNOWN (0).
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// File is currently being uploaded. Chunks may be partially present.
        /// Corresponds to Protobuf FILE_STATE_UPLOADING (1).
        /// </summary>
        Uploading = 1, // Matches Protobuf enum value

        /// <summary>
        /// File upload is complete, and the file is available for download.
        /// Corresponds to Protobuf FILE_STATE_AVAILABLE (2).
        /// </summary>
        Available = 2, // Matches Protobuf enum value

        /// <summary>
        /// File deletion process has been initiated. Chunks may be partially deleted.
        /// Corresponds to Protobuf FILE_STATE_DELETING (3).
        /// </summary>
        Deleting = 3, // Matches Protobuf enum value

        /// <summary>
        /// File has been successfully deleted (or marked as deleted). Data may or may not be physically removed yet.
        /// Corresponds to Protobuf FILE_STATE_DELETED (4).
        /// </summary>
        Deleted = 4, // Matches Protobuf enum value

        /// <summary>
        /// An error occurred related to this file (e.g., upload failed, consistency check failed).
        /// Corresponds to Protobuf FILE_STATE_ERROR (5).
        /// </summary>
        Error = 5, // Matches Protobuf enum value

        /// <summary>
        /// File is undergoing a recovery process (e.g., restoring lost chunks).
        /// Corresponds to Protobuf FILE_STATE_RECOVERING (6).
        /// </summary>
        Recovering = 6, // Matches Protobuf enum value

        /// <summary>
        /// File metadata exists (e.g., on a replica node) but the full file content/metadata
        /// might not be present or verified yet. Used for placeholder records.
        /// Does not have a direct Protobuf equivalent in the current definition,
        /// often mapped from Unknown or a specific placeholder state if added to proto.
        /// </summary>
        Incomplete = 7 // Internal state, used for placeholders
    }