using Contratos.Application.Contracts;

namespace Contratos.Tests.Domain;

public sealed class PhoneTests
{
    [Theory]
    [InlineData("+55 (11) 98765-4321", "+5511987654321")]
    [InlineData("11 98765-4321", "+5511987654321")]
    [InlineData("(21) 3456-7890", "+552134567890")]
    [InlineData("5511987654321", "+5511987654321")]
    [InlineData("+1 415 555 0100", "+14155550100")]
    [InlineData("", "")]
    public void Normalizes_to_international_format(string input, string expected) =>
        Assert.Equal(expected, ContractValidator.NormalizePhone(input));
}
