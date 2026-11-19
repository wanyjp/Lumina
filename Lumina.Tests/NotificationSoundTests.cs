using Lumina.Services.Notifications;

namespace Lumina.Tests;

/// <summary>到点提示音目录：两段随程序发布的音效 + 静音，未知值回落提示音 1。</summary>
public class NotificationSoundTests
{
    [Fact]
    public void Catalog_exposes_two_sounds_and_silence()
    {
        Assert.Equal(3, NotificationSoundCatalog.All.Count);
        Assert.Contains(NotificationSoundCatalog.All, option => option.FileName == "Sounds/notify-1.wav");
        Assert.Contains(NotificationSoundCatalog.All, option => option.FileName == "Sounds/notify-2.wav");
        Assert.Null(NotificationSoundCatalog.FileNameFor(NotificationSoundCatalog.Silent));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void Unknown_sound_falls_back_to_first_chime(string? value)
    {
        Assert.Equal(NotificationSoundCatalog.Chime1, NotificationSoundCatalog.Normalize(value));
        Assert.Equal("Sounds/notify-1.wav", NotificationSoundCatalog.FileNameFor(value));
    }

    [Fact]
    public void Sound_ids_are_case_insensitive()
    {
        Assert.Equal(NotificationSoundCatalog.Chime2, NotificationSoundCatalog.Normalize("CHIME2"));
    }
}
