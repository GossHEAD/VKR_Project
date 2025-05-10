using System.ComponentModel.DataAnnotations;
using System.Net;

namespace VKR_Node.Configuration
{
    public class NetworkOptions : IValidatableConfiguration
    {
        [Required(ErrorMessage = "Listen address is required")]
        public string ListenAddress { get; set; } = "localhost";
        
        [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535")]
        public int ListenPort { get; set; } = 5000;
        
        public string FullAddress => $"{ListenAddress}:{ListenPort}";
        
        [Range(1, 1000, ErrorMessage = "Maximum connections must be between 1 and 1000")]
        public int MaxConnections { get; set; } = 100;
        
        [Range(1, 300, ErrorMessage = "Connection timeout must be between 1 and 300 seconds")]
        public int ConnectionTimeoutSeconds { get; set; } = 30;
        
        public List<KnownNodeOptions> KnownNodes { get; set; } = new();
        
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
        
            if (KnownNodes != null)
            {
                foreach (var node in KnownNodes)
                {
                    node.Validate();
                }
            }
        
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
    
    public class KnownNodeOptions : IValidatableConfiguration
    {
        [Required(ErrorMessage = "Node ID is required")]
        public string NodeId { get; set; } = string.Empty;
        

        [Required(ErrorMessage = "Address is required")]
        [RegularExpression(@"^[a-zA-Z0-9\.\-]+:\d+$", ErrorMessage = "Address must be in host:port format")]
        public string Address { get; set; } = string.Empty;
        
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
        }
    }
}