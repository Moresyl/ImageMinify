namespace ImageMinify.Models;

/// <summary>A newer published application build that the user can review and install.</summary>
public sealed record UpdateRelease(string Version, string Notes);
