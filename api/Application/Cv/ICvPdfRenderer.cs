using System.Globalization;
using System.Text;

namespace ExpertToJob.Application.Cv;

/// <summary>
/// Headless render of an assembled <see cref="CvDto"/> to a PDF byte array. The seam lives in the
/// Application layer so every adapter — REST today, MCP or an agent-driven export later — shares one
/// renderer instead of each growing its own. Implementations must not touch the network: the input is
/// already a complete projection, so a render can never fail for want of a remote resource.
/// </summary>
public interface ICvPdfRenderer
{
    byte[] Render(CvDto cv);
}

/// <summary>Download filename for a rendered CV — kept beside the seam so adapters stay thin.</summary>
public static class CvPdfFileName
{
    /// <summary>Slugs the expert name into <c>firstname-lastname-cv.pdf</c>. Accents are folded to
    /// their base letter; anything still outside ASCII is dropped, so the name survives a bare
    /// <c>Content-Disposition</c> filename without needing the encoded form. A name that folds away
    /// to nothing (a wholly non-Latin one) falls back to <c>cv.pdf</c>.</summary>
    public static string For(CvDto cv)
    {
        var slug = new StringBuilder();
        foreach (var ch in cv.FullName.Normalize(NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(ch)) slug.Append(char.ToLowerInvariant(ch));
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        var name = slug.ToString().Trim('-');
        return name.Length == 0 ? "cv.pdf" : $"{name}-cv.pdf";
    }
}
