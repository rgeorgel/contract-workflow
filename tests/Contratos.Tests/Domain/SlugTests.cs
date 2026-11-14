using Contratos.Domain.Organizations;

namespace Contratos.Tests.Domain;

public sealed class SlugTests
{
    [Theory]
    [InlineData("Ação & Cia. Ltda", "acao-cia-ltda")]
    [InlineData("  Café   Aurora  ", "cafe-aurora")]
    [InlineData("ÇÃO--çÃo", "cao-cao")]
    [InlineData("!!!", "org")]
    public void Builds_url_friendly_slugs(string input, string expected) =>
        Assert.Equal(expected, Slug.From(input));

    [Fact]
    public void Respects_max_length_without_trailing_dash()
    {
        var slug = Slug.From(new string('a', 58) + " bcd");
        Assert.True(slug.Length <= Organization.SlugMaxLength);
        Assert.False(slug.EndsWith('-'));
    }
}
