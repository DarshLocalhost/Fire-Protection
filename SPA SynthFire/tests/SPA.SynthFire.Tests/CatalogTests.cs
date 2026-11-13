using System;
using System.IO;
using SPA.SynthFire.Catalog;
using SPA.SynthFire.Core.Catalog;
using SPA.SynthFire.Core.Standards;
using Xunit;

namespace SPA.SynthFire.Tests;

public class CatalogTests
{
    private static string TempXlsx() =>
        Path.Combine(Path.GetTempPath(), $"synthfire-catalog-{Guid.NewGuid():N}.xlsx");

    [Fact]
    public void TemplateRoundTrip_LoadsExpectedProducts()
    {
        string path = TempXlsx();
        try
        {
            CatalogTemplateWriter.Write(path);
            var catalog = CatalogLoader.LoadFromFile(path);

            Assert.Equal(CatalogSchema.CurrentVersion, catalog.Version);
            Assert.Equal(3, catalog.Sprinklers.Count);
            Assert.Equal(2, catalog.SmokeDetectors.Count);

            var first = catalog.Sprinklers[0];
            Assert.Equal("Std Pendent", first.Family);
            Assert.Equal(SprinklerOrientation.Pendent, first.Orientation);
            Assert.Equal(5.6, first.KFactor, precision: 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validator_TemplateIsValid_AgainstRegisteredStandards()
    {
        string path = TempXlsx();
        try
        {
            CatalogTemplateWriter.Write(path);
            var catalog = CatalogLoader.LoadFromFile(path);

            var registry = StandardRegistry.CreateDefault();
            var validator = new CatalogValidator(new[] { StandardIds.Nfpa13, StandardIds.As2118 });
            var result = validator.Validate(catalog);

            Assert.True(result.IsValid, string.Join(" ", result.Errors));
            Assert.Empty(result.Warnings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetSprinklers_FiltersByStandardAndHazard()
    {
        string path = TempXlsx();
        try
        {
            CatalogTemplateWriter.Write(path);
            var service = CatalogService.FromCatalog(CatalogLoader.LoadFromFile(path));

            var nfpaOh1 = service.GetSprinklers(StandardIds.Nfpa13, "OH1");
            Assert.Single(nfpaOh1);
            Assert.Equal("SP-2", nfpaOh1[0].Type);

            var as2118Oh1 = service.GetSprinklers(StandardIds.As2118, "OH1");
            Assert.Single(as2118Oh1);
            Assert.Equal("CU-1", as2118Oh1[0].Type);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ThrowsCatalogLoadException()
    {
        Assert.Throws<CatalogLoadException>(() =>
            CatalogLoader.LoadFromFile(Path.Combine(Path.GetTempPath(), "does-not-exist.xlsx")));
    }

    [Fact]
    public void Validator_DetectsDuplicateSprinkler()
    {
        var dup = new SprinklerCatalogRow
        {
            Family = "F", Type = "T", KFactor = 5.6,
            Orientation = SprinklerOrientation.Pendent,
            HazardClassId = "LH", StandardId = StandardIds.Nfpa13
        };
        var catalog = new SPA.SynthFire.Core.Catalog.Catalog
        {
            Version = "1.0",
            Sprinklers = new[] { dup, dup },
            SmokeDetectors = Array.Empty<SmokeDetectorCatalogRow>()
        };

        var result = new CatalogValidator().Validate(catalog);
        Assert.False(result.IsValid);
    }
}
