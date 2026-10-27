using SixLabors.ImageSharp;

namespace ImageMinify.Services;

/// <summary>Privacy stripping must not remove the colour interpretation of unchanged samples.</summary>
internal static class EncodingMetadata
{
    public static void Write(Image image, bool stripMetadata, Action write)
    {
        if (!stripMetadata)
        {
            write();
            return;
        }

        var metadata = image.Metadata;
        var exif = metadata.ExifProfile;
        var iptc = metadata.IptcProfile;
        var xmp = metadata.XmpProfile;
        metadata.ExifProfile = null;
        metadata.IptcProfile = null;
        metadata.XmpProfile = null;
        try
        {
            write();
        }
        finally
        {
            metadata.ExifProfile = exif;
            metadata.IptcProfile = iptc;
            metadata.XmpProfile = xmp;
        }
    }
}
