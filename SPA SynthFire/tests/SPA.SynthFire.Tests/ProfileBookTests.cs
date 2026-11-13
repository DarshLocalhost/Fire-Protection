using SPA.SynthFire.Core.Settings;
using SPA.SynthFire.Core.Standards;
using Xunit;

namespace SPA.SynthFire.Tests;

public class ProfileBookTests
{
    private static SynthFireSettings SampleSettings() => new()
    {
        StandardId = StandardIds.As2118,
        CeilingOffsetInches = 6.0,
        NotificationMountingHeightInches = 90.0,
        FamilyMap = { ["k"] = "Pendent : SP-1" }
    };

    [Fact]
    public void Save_AddsProfileAndMarksActive()
    {
        var book = new ProfileBook();
        var saved = book.Save("Warehouse", SampleSettings());

        Assert.NotNull(saved);
        Assert.Single(book.Profiles);
        Assert.Equal("Warehouse", book.ActiveProfileName);
        Assert.Equal(StandardIds.As2118, book.Find("Warehouse")!.Settings.StandardId);
    }

    [Fact]
    public void Save_ReplacesExistingByName_CaseInsensitive()
    {
        var book = new ProfileBook();
        book.Save("Office", new SynthFireSettings { CeilingOffsetInches = 4.0 });
        book.Save("office", new SynthFireSettings { CeilingOffsetInches = 12.0 });

        Assert.Single(book.Profiles);
        Assert.Equal(12.0, book.Find("OFFICE")!.Settings.CeilingOffsetInches);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Save_BlankName_IsRejected(string? name)
    {
        var book = new ProfileBook();
        var result = book.Save(name!, SampleSettings());

        Assert.Null(result);
        Assert.Empty(book.Profiles);
    }

    [Fact]
    public void Save_TrimsName()
    {
        var book = new ProfileBook();
        book.Save("  Retail  ", SampleSettings());
        Assert.Equal("Retail", book.Profiles[0].Name);
        Assert.Equal("Retail", book.ActiveProfileName);
    }

    [Fact]
    public void Save_DeepCopies_MutatingSourceAfterwardsDoesNotLeak()
    {
        var book = new ProfileBook();
        var source = SampleSettings();
        book.Save("Snap", source);

        source.CeilingOffsetInches = 99.0;
        source.FamilyMap["k"] = "changed";

        var stored = book.Find("Snap")!.Settings;
        Assert.Equal(6.0, stored.CeilingOffsetInches);
        Assert.Equal("Pendent : SP-1", stored.FamilyMap["k"]);
    }

    [Fact]
    public void Apply_ReturnsIndependentCopyAndMarksActive()
    {
        var book = new ProfileBook();
        book.Save("A", SampleSettings());
        book.Save("B", new SynthFireSettings { CeilingOffsetInches = 3.0 });

        var applied = book.Apply("A");

        Assert.NotNull(applied);
        Assert.Equal("A", book.ActiveProfileName);
        Assert.Equal(6.0, applied!.CeilingOffsetInches);

        // Mutating the applied copy must not touch the stored profile.
        applied.CeilingOffsetInches = 1.0;
        Assert.Equal(6.0, book.Find("A")!.Settings.CeilingOffsetInches);
    }

    [Fact]
    public void Apply_UnknownName_ReturnsNull()
    {
        var book = new ProfileBook();
        Assert.Null(book.Apply("nope"));
    }

    [Fact]
    public void Remove_DeletesAndClearsActiveWhenItPointedThere()
    {
        var book = new ProfileBook();
        book.Save("Gone", SampleSettings());

        Assert.True(book.Remove("gone"));
        Assert.Empty(book.Profiles);
        Assert.Null(book.ActiveProfileName);
    }

    [Fact]
    public void Remove_KeepsActiveWhenRemovingADifferentProfile()
    {
        var book = new ProfileBook();
        book.Save("Keep", SampleSettings());
        book.Save("Drop", SampleSettings()); // Drop is now active

        book.Save("Keep", SampleSettings());  // Keep is active again
        Assert.True(book.Remove("Drop"));

        Assert.Equal("Keep", book.ActiveProfileName);
        Assert.Single(book.Profiles);
    }

    [Fact]
    public void Remove_UnknownName_ReturnsFalse()
    {
        var book = new ProfileBook();
        Assert.False(book.Remove("missing"));
    }
}
