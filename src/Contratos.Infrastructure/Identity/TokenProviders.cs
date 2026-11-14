using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Identity;

// Cada finalidade tem seu provedor para ter validade própria:
// confirmação de e-mail 24 h (padrão), convite 7 dias, nova senha 2 h.

public static class TokenPurposes
{
    public const string Invite = "Invite";
    public const string PasswordReset = "PasswordReset";
}

public sealed class InviteTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InviteTokenProviderOptions()
    {
        Name = TokenPurposes.Invite;
        TokenLifespan = TimeSpan.FromDays(7);
    }
}

public sealed class InviteTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<InviteTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;

public sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public PasswordResetTokenProviderOptions()
    {
        Name = TokenPurposes.PasswordReset;
        TokenLifespan = TimeSpan.FromHours(2);
    }
}

public sealed class PasswordResetTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;
