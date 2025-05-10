using System.ComponentModel.DataAnnotations;
using System.Net;

namespace VKR_Node.Configuration
{
    /// <summary>
    /// Network-related configuration.
    /// </summary>
    public class NetworkOptions : IValidatableConfiguration
    {
        /// <summary>
        /// Host on which to listen for incoming connections.
        /// Use "0.0.0.0" to listen on all interfaces.
        /// </summary>
        [Required(ErrorMessage = "Listen address is required")]
        public string ListenAddress { get; set; } = "localhost";
        
        /// <summary>
        /// Port on which to listen for incoming connections.
        /// </summary>
        [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535")]
        public int ListenPort { get; set; } = 5000;
        
        /// <summary>
        /// Full address in "host:port" format.
        /// </summary>
        public string FullAddress => $"{ListenAddress}:{ListenPort}";
        
        /// <summary>
        /// Maximum concurrent connections.
        /// </summary>
        [Range(1, 1000, ErrorMessage = "Maximum connections must be between 1 and 1000")]
        public int MaxConnections { get; set; } = 100;
        
        /// <summary>
        /// Connection timeout in seconds.
        /// </summary>
        [Range(1, 300, ErrorMessage = "Connection timeout must be between 1 and 300 seconds")]
        public int ConnectionTimeoutSeconds { get; set; } = 30;
        
        /// <summary>
        /// List of known peer nodes.
        /// </summary>
        public List<KnownNodeOptions> KnownNodes { get; set; } = new();
        
        /// <summary>
        /// Validate this configuration section.
        /// </summary>
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
        
            // Additional validations
            if (KnownNodes != null)
            {
                foreach (var node in KnownNodes)
                {
                    node.Validate();
                }
            }
        
            // Validate ListenAddress is a valid hostname or IP
            try
            {
                if (ListenAddress != "localhost" && 
                    !IPAddress.TryParse(ListenAddress, out _) && 
                    !Uri.CheckHostName(ListenAddress).Equals(UriHostNameType.Dns))
                {
                    throw new ValidationException($"Invalid listen address: {ListenAddress}");
                }
            }
            catch (Exception ex) when (!(ex is ValidationException))
            {
                throw new ValidationException($"Invalid listen address: {ListenAddress}", ex);
            }
        }
    }
    
    /// <summary>
    /// Configuration for a known peer node.
    /// </summary>
    public class KnownNodeOptions : IValidatableConfiguration
    {
        /// <summary>
        /// Unique identifier of the node.
        /// </summary>
        [Required(ErrorMessage = "Node ID is required")]
        public string NodeId { get; set; } = string.Empty;
        
        /// <summary>
        /// Network address in host:port format.
        /// </summary>
        [Required(ErrorMessage = "Address is required")]
        [RegularExpression(@"^[a-zA-Z0-9\.\-]+:\d+$", ErrorMessage = "Address must be in host:port format")]
        public string Address { get; set; } = string.Empty;
        
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