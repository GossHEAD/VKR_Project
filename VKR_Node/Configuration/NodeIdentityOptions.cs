using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration
{
    /// <summary>
    /// Uniquely identifies this node in the network.
    /// </summary>
    public class NodeIdentityOptions
    {
        /// <summary>
        /// Unique identifier for this node. Must be unique across the entire network.
        /// </summary>
        [Required(ErrorMessage = "Node ID is required")]
        [RegularExpression(@"^[a-zA-Z0-9\-_]+$", ErrorMessage = "Node ID can only contain alphanumeric characters, hyphens, and underscores")]
        public string NodeId { get; set; } = "Node1";
        
        /// <summary>
        /// Optional display name for the node (for UI/logs).
        /// </summary>
        public string? DisplayName { get; set; }
        
        /// <summary>
        /// Validate this configuration section.
        /// </summary>
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
        }
    }
}