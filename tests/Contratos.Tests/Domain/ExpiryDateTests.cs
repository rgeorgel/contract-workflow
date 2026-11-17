using Contratos.Application.Contracts;

namespace Contratos.Tests.Domain;

public sealed class ExpiryDateTests
{
    private static UpdateContractRequest Expiring(DateOnly date) => new("Contrato", null, date, null, null);

    [Theory]
    [InlineData("2026-11-17T01:30:00Z", "2026-11-16")] // 22h30 em Brasília: em UTC já é dia 17
    [InlineData("2026-11-17T02:59:59Z", "2026-11-16")]
    [InlineData("2026-11-17T03:00:00Z", "2026-11-17")] // meia-noite em Brasília
    [InlineData("2026-11-16T12:00:00Z", "2026-11-16")]
    public void Today_is_the_date_in_Brazil(string utc, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), ContractValidator.TodayInBrazil(DateTimeOffset.Parse(utc)));

    [Fact]
    public void Tomorrow_is_accepted_late_at_night_in_Brazil()
    {
        var today = ContractValidator.TodayInBrazil(DateTimeOffset.Parse("2026-11-17T01:30:00Z")); // 16/11, 22h30
        Assert.True(ContractValidator.ValidateDraft(Expiring(new DateOnly(2026, 11, 17)), [], today).IsEmpty);
        Assert.False(ContractValidator.ValidateDraft(Expiring(new DateOnly(2026, 11, 16)), [], today).IsEmpty);
    }
}
