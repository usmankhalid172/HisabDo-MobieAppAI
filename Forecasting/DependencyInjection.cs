using Microsoft.Extensions.DependencyInjection;

namespace HisabDo.Forecasting;

public static class DependencyInjection
{
    public static IServiceCollection AddHisabDoForecasting(this IServiceCollection services)
    {
        services.AddScoped<ForecastingService>();
        services.AddScoped<BudgetAnalysisService>();
        services.AddScoped<AskHisabDoService>();
        return services;
    }
}