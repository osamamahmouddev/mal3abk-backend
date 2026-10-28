using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Security;

public static class SecurityServiceCollectionExtensions
{
    public const string DataProtectionApplicationName = "malabak";

    public static IServiceCollection AddFieldEncryption(
        this IServiceCollection services,
        IConfiguration configuration,
        string? postgresConnectionString)
    {
        services.AddOptions<EncryptionOptions>()
            .Bind(configuration.GetSection(EncryptionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IPostConfigureOptions<EncryptionOptions>, EncryptionOptionsSetup>();
        services.AddSingleton<IValidateOptions<EncryptionOptions>, EncryptionOptionsValidator>();

        services.AddDbContext<SecurityDbContext>(options =>
            options.UseNpgsql(postgresConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", SecurityDbContext.Schema)));

        services.AddSingleton<IKeyEncryptionKeyProvider, LocalKeyEncryptionKeyProvider>();
        services.AddSingleton<IDataKeyRing, DataKeyRing>();
        services.AddSingleton<IFieldCipher, AesGcmFieldCipher>();
        services.AddSingleton<IBlindIndexService, HmacBlindIndexService>();
        services.AddScoped<EncryptionKeyRotationService>();

        services.AddDataProtection().SetApplicationName(DataProtectionApplicationName);
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
            new ConfigureOptions<KeyManagementOptions>(options =>
            {
                options.XmlRepository = new SecurityDbXmlRepository(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<IClock>());
                options.XmlEncryptor = new KeyEncryptionKeyXmlEncryptor(sp.GetRequiredService<IKeyEncryptionKeyProvider>());
            }));

        return services;
    }
}
