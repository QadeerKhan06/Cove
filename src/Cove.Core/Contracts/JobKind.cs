namespace Cove.Core.Contracts;

/// <summary>The kinds of work the file engine can do.</summary>
public enum JobKind
{
    /// <summary>Change format, e.g. HEIC to JPG or MOV to MP4.</summary>
    Convert,

    /// <summary>Shrink a file toward a target size, e.g. a video under 10 MB for Discord.</summary>
    Compress,

    /// <summary>Upload to OneDrive and return a share link.</summary>
    Upload,

    /// <summary>Read text from an image or PDF (OCR) and copy it.</summary>
    CopyText,
}