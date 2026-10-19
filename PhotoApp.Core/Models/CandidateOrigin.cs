namespace PhotoApp.Core.Models;

/// <summary>D'où vient une date candidate (spec de phase 1, §5.1).</summary>
public enum CandidateOrigin
{
    Unknown = 0,
    ExifOriginal,
    ExifDigitized,
    QuickTimeApple,
    QuickTimeMovieHeader,
    Xmp,
    XmpSidecar,
    Gps,
    FileName,
    FileModified
}
