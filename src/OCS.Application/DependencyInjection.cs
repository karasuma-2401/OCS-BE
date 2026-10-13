using Microsoft.Extensions.DependencyInjection;

namespace OCS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register application services here as each business feature is implemented.
        return services;
    }
}
