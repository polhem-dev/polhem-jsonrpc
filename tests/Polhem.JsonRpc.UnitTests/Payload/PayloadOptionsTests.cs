using System.ComponentModel;
using Polhem.JsonRpc.Payload;

namespace Polhem.JsonRpc.UnitTests.Payload;

public class PayloadOptionsTests
{
    [Theory]
    [DisplayName("Options: a codec name must be lower-case letters, digits and hyphens of at most 32 characters")]
    [InlineData("")]
    [InlineData("MsgPack")]
    [InlineData("msg pack")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    public void RegisterCodec_MalformedName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => new PayloadOptions().RegisterCodec(new NamedCodec(name)));
    }

    [Fact]
    [DisplayName("Options: json cannot be registered over, and a name registers once")]
    public void RegisterCodec_ReservedOrDuplicate_Throws()
    {
        var options = new PayloadOptions();
        options.RegisterCodec(new NamedCodec("msgpack"));

        Assert.Throws<InvalidOperationException>(() => options.RegisterCodec(new NamedCodec("json")));
        Assert.Throws<InvalidOperationException>(() => options.RegisterCodec(new NamedCodec("msgpack")));
        Assert.Equal(["json", "msgpack"], options.CodecNames.Order());
    }

    [Fact]
    [DisplayName("Options: an empty codec name resolves to the default codec")]
    public void ResolveCodec_Empty_ReturnsDefault()
    {
        var options = new PayloadOptions();
        var msgpack = new NamedCodec("msgpack");
        options.RegisterCodec(msgpack);
        options.DefaultCodec = "msgpack";

        Assert.Same(msgpack, options.ResolveCodec(""));
        Assert.Same(options.JsonCodec, options.ResolveCodec("json"));
    }

    [Theory]
    [DisplayName("Options: an unknown or malformed codec name is not supported")]
    [InlineData("cbor")]
    [InlineData("../json")]
    public void ResolveCodec_Unknown_Throws(string name)
    {
        Assert.Throws<NotSupportedException>(() => new PayloadOptions().ResolveCodec(name));
    }

    [Fact]
    [DisplayName("Options: the frame timestamp tolerance must be positive")]
    public void FrameTimestampTolerance_NotPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PayloadOptions { FrameTimestampTolerance = TimeSpan.Zero });
    }

    private sealed class NamedCodec(string name) : IPayloadCodec
    {
        public string Name => name;

        public byte[] Serialize(object value, Type type) => [];

        public object? Deserialize(byte[] bytes, Type type) => null;
    }
}
