using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Регистрация gRPC сервиса
        services.AddGrpc();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            // Добавляем gRPC Endpoint для тестов
            endpoints.MapGrpcService<GrpcServer>();
        });
    }
}