using System.ComponentModel.DataAnnotations;
using System.Net;

namespace VKR_Node.Configuration
{
    public class NetworkOptions : IValidatableConfiguration
    {
        public string ListenAddress { get; set; } = "localhost";
        public int ListenPort { get; set; } = 5000;
        public string FullAddress => $"{ListenAddress}:{ListenPort}";
        public int MaxConnections { get; set; } = 100;
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
        public string NodeId { get; set; } = string.Empty;

        [RegularExpression(@"^[a-zA-Z0-9\.\-]+:\d+$", ErrorMessage = "Address must be in host:port format")]
        public string Address { get; set; } = string.Empty;
        
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
        }
    }
}