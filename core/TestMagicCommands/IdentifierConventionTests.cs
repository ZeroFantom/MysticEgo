using Models.Commands;

namespace TestMagicCommands;

public class IdentifierConventionTests
{
    [Theory]
    [InlineData("AudioListCommand",     "Command", "audio.list")]
    [InlineData("RgbMouseCommand",      "Command", "rgb.mouse")]
    [InlineData("RGBCommand",           "Command", "rgb")]
    [InlineData("AudioRGBCommand",      "Command", "audio.rgb")]
    [InlineData("AudioRGBListCommand",  "Command", "audio.rgb.list")]
    [InlineData("Http2ServerCommand",   "Command", "http2.server")]
    [InlineData("XMLHttpRequestCommand","Command", "xml.http.request")]
    [InlineData("RolesController",      "Controller", "roles")]
    [InlineData("AudioDevicesController","Controller","audio.devices")]
    public void FromTypeName_Works(string typeName, string suffix, string expected)
    {
        Assert.Equal(expected, IdentifierConvention.FromTypeName(typeName, suffix));
    }

    [Theory]
    [InlineData("audio.list",   "audio.list")]
    [InlineData("Audio.List",   "audio.list")]
    [InlineData("audio_list",   "audio.list")]
    [InlineData("audio-list",   "audio.list")]
    [InlineData("Audio List",   "audio.list")]
    [InlineData("AUDIO.LIST",   "audio.list")]
    public void Normalize_IsIdempotent(string input, string expected)
    {
        var once  = IdentifierConvention.Normalize(input);
        var twice = IdentifierConvention.Normalize(once);

        Assert.Equal(expected, once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void ToolId_PermissionMethod_Split()
    {
        var tool = ToolId.FromName("audio.devices.list");

        Assert.Equal("audio.devices", tool.Permission.Value);
        Assert.Equal("list", tool.Method);
        Assert.Equal("Audio", tool.Category);
    }

    [Fact]
    public void ToolId_AsCommandId_SameValue()
    {
        var tool = ToolId.FromName("audio.list");
        var cmd  = tool.AsCommandId();

        Assert.Equal(tool.Value, cmd.Value);
    }
}