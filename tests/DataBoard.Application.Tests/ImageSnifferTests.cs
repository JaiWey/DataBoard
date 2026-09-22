using DataBoard.Application.Dashboards;

namespace DataBoard.Application.Tests;

public class ImageSnifferTests
{
    private static byte[] Bytes(params byte[] b) => b;

    [Fact]
    public void Recognises_each_kind_of_picture_by_its_first_bytes()
    {
        Assert.Equal(("image/png", "png"), ImageSniffer.Detect(Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0)));
        Assert.Equal(("image/jpeg", "jpg"), ImageSniffer.Detect(Bytes(0xFF, 0xD8, 0xFF, 0xE0, 0, 0)));
        Assert.Equal(("image/gif", "gif"), ImageSniffer.Detect("GIF89a...."u8));
        Assert.Equal(("image/gif", "gif"), ImageSniffer.Detect("GIF87a...."u8));
        Assert.Equal(("image/webp", "webp"), ImageSniffer.Detect("RIFF\0\0\0\0WEBPVP8 "u8));
    }

    [Fact]
    public void Refuses_everything_else_whatever_it_claims_to_be()
    {
        Assert.Null(ImageSniffer.Detect("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"u8));
        Assert.Null(ImageSniffer.Detect("<script>alert(1)</script>"u8));
        Assert.Null(ImageSniffer.Detect("RIFF\0\0\0\0WAVEfmt "u8)); // a RIFF file, but not WebP
        Assert.Null(ImageSniffer.Detect("MZ\u0090\0"u8));
        Assert.Null(ImageSniffer.Detect([]));
        Assert.Null(ImageSniffer.Detect(Bytes(0x89, 0x50)));
    }

    [Theory]
    [InlineData("a.png", "image/png")]
    [InlineData("a.JPG", "image/jpeg")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.svg", "application/octet-stream")]
    public void Tells_the_content_type_from_the_key(string key, string expected) =>
        Assert.Equal(expected, ImageSniffer.ContentTypeOfKey(key));
}
