using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace ImageMinify.Services;

/// <summary>
/// Metadata handling for the compression pipeline.
/// </summary>
public sealed class ExifService
{
    /// <summary>
    /// Rotates the pixels to match the EXIF orientation tag and then clears the tag.
    /// </summary>
    /// <remarks>
    /// This has to happen before anything else touches the image. A phone photo is stored in the
    /// sensor's orientation with a tag saying which way up it really is; every viewer honours the
    /// tag, but a resize does not — scale a portrait photo to "longest edge 1920" while it is still
    /// physically landscape and the box is applied to the wrong axis. Baking the rotation in first
    /// means every later step sees the image the way the user does.
    /// </remarks>
    public void AutoOrient(Image image)
    {
        image.Mutate(context => context.AutoOrient());
    }

    /// <summary>Removes personal metadata while retaining the profile needed to display the pixels.</summary>
    public void Strip(Image image)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;
    }

    /// <summary>
    /// Replaces the image's EXIF with camera, software and capture date, and nothing else.
    /// </summary>
    /// <remarks>
    /// This is the "keep metadata" path, and it is deliberately not a straight copy. A camera
    /// profile carries GPS coordinates, an embedded thumbnail of the *un*-processed image, and maker
    /// notes that can run to tens of kilobytes — on a compressed web image the metadata can end up
    /// larger than the pixels, and the coordinates are a privacy leak nobody asked for. What survives
    /// is what people actually want kept: which camera, which software, and when.
    /// </remarks>
    public void Reduce(Image image, string inputPath)
    {
        image.Metadata.ExifProfile = BuildReducedProfile(inputPath);
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;
    }

    private static ExifProfile? BuildReducedProfile(string inputPath)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(inputPath);
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 is null)
            {
                return null;
            }

            var profile = new ExifProfile
            {
                Parts = ExifParts.IfdTags,
            };

            var make = ifd0.GetString(ExifDirectoryBase.TagMake);
            if (!string.IsNullOrWhiteSpace(make))
            {
                profile.SetValue(ExifTag.Make, make);
            }

            var model = ifd0.GetString(ExifDirectoryBase.TagModel);
            if (!string.IsNullOrWhiteSpace(model))
            {
                profile.SetValue(ExifTag.Model, model);
            }

            var software = ifd0.GetString(ExifDirectoryBase.TagSoftware);
            if (!string.IsNullOrWhiteSpace(software))
            {
                profile.SetValue(ExifTag.Software, software);
            }

            var dateTime = ifd0.GetString(ExifDirectoryBase.TagDateTime);
            if (!string.IsNullOrWhiteSpace(dateTime))
            {
                profile.SetValue(ExifTag.DateTime, dateTime);
            }

            // The rotation is already baked into the pixels by AutoOrient, so the tag must say
            // "upright". Carrying the original value forward would rotate the image a second time
            // in every viewer that honours it.
            profile.SetValue(ExifTag.Orientation, (ushort)1);
            return profile;
        }
        catch
        {
            // A malformed or absent profile is not a reason to fail the compression. No metadata is
            // a valid outcome.
            return null;
        }
    }
}
