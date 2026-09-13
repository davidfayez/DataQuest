using System.Reflection;
using DataVerification.Application.Common.Behaviours;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace DataVerification.Application;

public static class DependencyInjection
{
    /// <summary>Registers CQRS handlers, validators and the request pipeline.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(assembly);
            // Order matters: validate before measuring, so a rejected request is not timed.
            config.AddOpenBehavior(typeof(ValidationBehaviour<,>));
            config.AddOpenBehavior(typeof(PerformanceBehaviour<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Shared engine behind every admin lookup CRUD screen.
        services.AddScoped<Features.Lookups.Admin.AdminLookupService>();
        services.AddScoped<Features.Payments.PaymentNotifier>();

        // Cascade validation and server-side pricing, shared by create and update.
        services.AddScoped<Features.Applications.ApplicationWriteService>();
        services.AddScoped<Features.Review.Commands.AttachedDocumentWriter>();

        return services;
    }
}
