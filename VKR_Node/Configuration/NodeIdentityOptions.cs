using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration
{
    public class NodeIdentityOptions : IValidatableConfiguration
    {
        [Required(ErrorMessage = "Node ID is required")]
        [RegularExpression(@"^[a-zA-Z0-9\-_]+$", ErrorMessage = "Node ID can only contain alphanumeric characters, hyphens, and underscores")]
        public string NodeId { get; set; } = "Node1";
        
        public string? DisplayName { get; set; }
        
        /// <summary>
        /// Validate this configuration section.
        /// </summary>
        // public void Validate()
        // {
        //     var context = new ValidationContext(this);
        //     Validator.ValidateObject(this, context, true);
        // }
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
        }
    }
}