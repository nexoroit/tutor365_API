using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tutor365.Application.Common;
using Tutor365.Application.Interfaces;
using Tutor365.Application.Services;
using Tutor365.Infrastructure.AI;
using Tutor365.Infrastructure.Data;
using Tutor365.Infrastructure.Email;
using Tutor365.Infrastructure.Identity;
using Tutor365.Infrastructure.Services;

namespace Tutor365.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(
            configuration.GetConnectionString("Default"),
            sql => sql.EnableRetryOnFailure(3).CommandTimeout(60)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddScoped<IMailSettingsService, MailSettingsService>();
        services.AddScoped<IAppEmailService, AppEmailService>();
        services.AddScoped<IAiProvider, AnthropicAiProvider>();
        services.AddScoped<IAiSettingsService, AiSettingsService>();
        services.AddScoped<IAiMarker, AiMarkingService>();

        // Application services
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccessService, AccessService>();
        services.AddScoped<IParentService, ParentService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ICurriculumService, CurriculumService>();
        services.AddSingleton<IMarkingService, MarkingService>();
        services.AddScoped<IProgressService, ProgressService>();
        services.AddScoped<IStudySessionService, StudySessionService>();
        services.AddScoped<IPlannerService, PlannerService>();
        services.AddScoped<ITimetableService, TimetableService>();
        services.AddScoped<IStudentService, StudentService>();
        services.AddScoped<IAssessmentService, AssessmentService>();
        services.AddScoped<IAiTutorService, AiTutorService>();
        services.AddScoped<IStudyPlanService, StudyPlanService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddHostedService<MaintenanceHostedService>();

        services.AddScoped<Data.Seed.DatabaseSeeder>();
        services.AddScoped<Data.Seed.ContentSeeder>();
        return services;
    }
}
