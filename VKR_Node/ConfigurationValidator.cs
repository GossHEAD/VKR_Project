using VKR_Node.Configuration;

namespace VKR.Node;

public interface IConfigurationValidator
{
    void ValidateConfiguration();
}

public class ConfigurationValidator : IConfigurationValidator
{
    private readonly DistributedStorageConfiguration _configuration;
    
    public ConfigurationValidator(
        Microsoft.Extensions.Options.IOptions<DistributedStorageConfiguration> options)
    {
        _configuration = options.Value;
    }
    
    public void ValidateConfiguration()
    {
        _configuration.Validate();
    }
}