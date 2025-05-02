using VKR_Core.Enums;
using VKR.Protos; // Namespace containing protobuf-generated enums

namespace VKR_Core.Extensions
{
    /// <summary>
    /// Provides conversion extensions between Core enums and their Protobuf counterparts.
    /// </summary>
    public static class EnumExtensions
    {
        /// <summary>
        /// Converts a FileStateCore enum to its Protobuf FileState equivalent.
        /// </summary>
        public static FileState ToProto(this FileStateCore coreState)
        {
            return coreState switch
            {
                FileStateCore.Unknown => FileState.Unknown,
                FileStateCore.Uploading => FileState.Uploading,
                FileStateCore.Available => FileState.Available,
                FileStateCore.Deleting => FileState.Deleting,
                FileStateCore.Deleted => FileState.Deleted,
                FileStateCore.Error => FileState.Error,
                FileStateCore.Recovering => FileState.Recovering,
                FileStateCore.Incomplete => FileState.Unknown, // Map non-proto value to suitable default
                _ => FileState.Unknown // Default case for safety
            };
        }

        /// <summary>
        /// Converts a Protobuf FileState to its Core FileStateCore equivalent.
        /// </summary>
        public static FileStateCore ToCore(this FileState protoState)
        {
            return protoState switch
            {
                FileState.Unknown => FileStateCore.Unknown,
                FileState.Uploading => FileStateCore.Uploading,
                FileState.Available => FileStateCore.Available,
                FileState.Deleting => FileStateCore.Deleting,
                FileState.Deleted => FileStateCore.Deleted,
                FileState.Error => FileStateCore.Error,
                FileState.Recovering => FileStateCore.Recovering,
                _ => FileStateCore.Unknown // Default case for safety
            };
        }

        /// <summary>
        /// Converts a NodeStateCore enum to its Protobuf NodeState equivalent.
        /// </summary>
        public static NodeState ToProto(this NodeStateCore coreState)
        {
            return coreState switch
            {
                NodeStateCore.Offline => NodeState.Offline,
                NodeStateCore.Joining => NodeState.Joining,
                NodeStateCore.Online => NodeState.Online,
                NodeStateCore.Leaving => NodeState.Leaving,
                NodeStateCore.Error => NodeState.Error,
                _ => NodeState.Unknown // Default case for safety
            };
        }

        /// <summary>
        /// Converts a Protobuf NodeState to its Core NodeStateCore equivalent.
        /// </summary>
        public static NodeStateCore ToCore(this NodeState protoState)
        {
            return protoState switch
            {
                NodeState.Unknown => NodeStateCore.Offline, // Map both to Offline per comment in NodeStateCore
                NodeState.Offline => NodeStateCore.Offline,
                NodeState.Joining => NodeStateCore.Joining,
                NodeState.Online => NodeStateCore.Online,
                NodeState.Leaving => NodeStateCore.Leaving,
                NodeState.Error => NodeStateCore.Error,
                _ => NodeStateCore.Offline // Default case for safety
            };
        }

        /// <summary>
        /// Converts a UserRole enum to its integer representation for storage.
        /// </summary>
        public static int ToInt(this UserRole role)
        {
            return (int)role;
        }

        /// <summary>
        /// Converts an integer to its UserRole enum representation.
        /// </summary>
        public static UserRole ToUserRole(this int roleValue)
        {
            return roleValue switch
            {
                0 => UserRole.ITSpecialist,
                1 => UserRole.Administrator,
                _ => UserRole.ITSpecialist // Default case for safety
            };
        }
        
        /// <summary>
        /// Converts an integer to its FileStateCore enum equivalent.
        /// </summary>
        public static FileStateCore ToFileStateCore(this int stateValue)
        {
            return stateValue switch
            {
                0 => FileStateCore.Unknown,
                1 => FileStateCore.Uploading,
                2 => FileStateCore.Available,
                3 => FileStateCore.Deleting,
                4 => FileStateCore.Deleted,
                5 => FileStateCore.Error,
                6 => FileStateCore.Recovering,
                7 => FileStateCore.Incomplete,
                _ => FileStateCore.Unknown // Default case for safety
            };
        }
        
        /// <summary>
        /// Converts a FileStateCore enum to its integer equivalent.
        /// </summary>
        public static int ToInt(this FileStateCore state)
        {
            return (int)state;
        }
        
        /// <summary>
        /// Converts an integer to its NodeStateCore enum equivalent.
        /// </summary>
        public static NodeStateCore ToNodeStateCore(this int stateValue)
        {
            return stateValue switch
            {
                0 => NodeStateCore.Offline,
                2 => NodeStateCore.Joining,
                3 => NodeStateCore.Online,
                4 => NodeStateCore.Leaving,
                5 => NodeStateCore.Error,
                _ => NodeStateCore.Offline // Default case for safety
            };
        }
        
        /// <summary>
        /// Converts a NodeStateCore enum to its integer equivalent.
        /// </summary>
        public static int ToInt(this NodeStateCore state)
        {
            return (int)state;
        }
    }
}