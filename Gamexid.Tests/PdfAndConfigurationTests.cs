using Gamexid.Services;
using Microsoft.Extensions.Configuration;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Gamexid.Tests;

public class PdfAndConfigurationTests
{
    public static byte[] Pdf(params string[] rows)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        var y = 720;
        foreach (var row in rows) { page.AddText(row, 10, new PdfPoint(30, y), font); y -= 25; }
        return builder.Build();
    }

    [Fact]
    public void PdfExtractsLabelsWithPricesWithoutAi()
    {
        var bytes = Pdf("EAN 7790000000011 Producto Notebook Cantidad 3 Precio ARS 1.234,56 Total 3.703,68");
        var result = new PdfInvoiceExtractor().Extract(bytes);
        var line = Assert.Single(result.Lines);
        Assert.Equal("7790000000011", line.Ean);
        Assert.Equal("Notebook", line.Name);
        Assert.Equal(3m, line.Expected);
        Assert.Equal(1234.56m, line.UnitPrice);
        Assert.Equal(3703.68m, line.Total);
        Assert.Equal("ARS", line.Currency);
        Assert.Null(line.Confidence);
        Assert.False(result.RequiresOcr);
    }

    [Fact]
    public void PdfExtractsAlignedColumns()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        var headings = new[] { "EAN", "Descripcion", "Cantidad", "Precio", "Total" };
        var values = new[] { "7790000000011", "Notebook", "2", "1.200,50", "2.401,00" };
        var xs = new[] { 20, 135, 290, 370, 465 };
        for (var i = 0; i < headings.Length; i++) {
            page.AddText(headings[i], 9, new PdfPoint(xs[i], 700), font);
            page.AddText(values[i], 9, new PdfPoint(xs[i], 675), font);
        }
        var line = Assert.Single(new PdfInvoiceExtractor().Extract(builder.Build()).Lines);
        Assert.Equal(2m, line.Expected);
        Assert.Equal(1200.5m, line.UnitPrice);
        Assert.Equal("Notebook", line.Name);
    }

    [Fact]
    public void PdfDoesNotInventMissingQuantityOrCurrency()
    {
        var line = PdfInvoiceExtractor.FromLabels("SKU MON-24 Producto Monitor Precio $ 100,00")!;
        Assert.Null(line.Expected);
        Assert.Null(line.Currency);
        Assert.Equal("MON-24", line.Sku);
        Assert.Null(PdfInvoiceExtractor.FromLabels("CUIT 30123456789 TOTAL 12000"));
    }

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("100.50", 100.5)]
    [InlineData("12.000", 12000)]
    public void ParsesFinancialNumbers(string input, decimal expected) => Assert.Equal(expected, PdfInvoiceExtractor.Number(input));

    [Fact]
    public void RejectsNonPdfAndMarksImageOnly()
    {
        Assert.Throws<InvalidDataException>(() => new PdfInvoiceExtractor().Extract("not a pdf"u8.ToArray()));
        var result = new PdfInvoiceExtractor().Extract(Pdf());
        Assert.True(result.RequiresOcr);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void UriDecodesCredentialsWithoutLoggingAndForcesTls()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["MYSQL_URL"] = "mysql://test:p%40ss%2Fword@example.invalid:1234/defaultdb?ssl-mode=REQUIRED",
            ["ConnectionStrings:GamexidDatabase"] = ""
        }).Build();
        var result = DatabaseConfiguration.Load(config);
        var cs = DatabaseConfiguration.Parse(result.ConnectionString!);
        Assert.Equal("p@ss/word", cs.Password);
        Assert.Equal("defaultdb", cs.Database);
        Assert.Equal(1234u, cs.Port);
        Assert.Equal(MySqlConnector.MySqlSslMode.VerifyFull, cs.SslMode);
    }

    [Fact]
    public void MissingDatabaseIsAnExplicitState()
    {
        var result = DatabaseConfiguration.Load(new ConfigurationBuilder().Build());
        Assert.Null(result.ConnectionString);
        Assert.Equal("not_configured", result.Status);
    }
}
