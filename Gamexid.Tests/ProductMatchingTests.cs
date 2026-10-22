using Gamexid.Services;
using Xunit;

namespace Gamexid.Tests;

public class ProductMatchingTests
{
    [Theory]
    [InlineData(" ab ", "AB", 0)]
    [InlineData(" note-001 ", "NOTE-001", 1)]
    [InlineData("play", "PLAYSTATION 5", 0.9)]
    [InlineData("abcd", "abce", 0.75)]
    [InlineData("abcd", "abc", 0.75)]
    [InlineData("abcd", "abxcd", 0.8)]
    [InlineData("abcd", "abef", 0)]
    [InlineData("abcdefgh", "abcdefxy", 0.75)]
    [InlineData("abcdefgh", "abcdexyz", 0)]
    [InlineData("abcd", "", 0)]
    [InlineData("abcd", "xyzxyzxyz", 0)]
    public void ConservaPuntajeDeCoincidencia(string busqueda, string candidato, double esperado)
    {
        Assert.Equal(esperado, ProductMatching.Score(busqueda, candidato), 10);
    }
}
