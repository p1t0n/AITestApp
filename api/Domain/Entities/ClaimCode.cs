using System.Security.Cryptography;
using System.Text;

namespace ExpertToJob.Domain.Entities;

/// <summary>
/// A single-use secret a Service Manager generates for one roster row and hands over out of band —
/// in person, by phone, however they already reach the person (P1T-184). Redeeming it binds
/// ownership with no approval step, because <em>the code is the proof</em>: it is the only evidence
/// this service can offer that is stronger than an unverified email match, and it exists precisely
/// because email is not verified and no mail is ever sent.
///
/// <para>Stored hashed. The plaintext is shown once, at generation, and never again — a bearer
/// secret readable out of the database is a second way to take over a CV, which is the thing the
/// claim design exists to prevent. SHA-256 rather than a password hash is deliberate and
/// sufficient: the code is 160 bits of cryptographic randomness, so there is nothing to guess and
/// nothing for work factor to slow down.</para>
/// </summary>
public class ClaimCode
{
    /// <summary>Bytes of randomness behind a code. 160 bits: 32 Base32 characters, still readable
    /// down a phone line, and far past anything a redemption endpoint could be walked through.</summary>
    private const int SecretBytes = 20;

    /// <summary>Crockford-style alphabet without I, L, O and U — a code gets read aloud and typed
    /// back, and those four are the ones that come back wrong.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public Guid Id { get; set; }

    /// <summary>The row this code binds. Cascade-deleted with it.</summary>
    public Guid ExpertId { get; set; }

    public Expert? Expert { get; set; }

    /// <summary>SHA-256 of the normalised code, Base64. The plaintext is not stored anywhere.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public Guid? IssuedByUserId { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>When it was spent. Non-null is the whole of "single-use": a redeemed code is
    /// refused rather than deleted, so a replay is a fact somebody can see afterwards.</summary>
    public DateTimeOffset? RedeemedAt { get; set; }

    public Guid? RedeemedByUserId { get; set; }

    /// <summary>Generates a fresh code. Returns the plaintext to hand over — the caller shows it
    /// once; only the hash reaches the database.</summary>
    public static (ClaimCode Code, string Plaintext) Issue(
        Guid expertId, Guid? issuedByUserId, DateTimeOffset at)
    {
        var plaintext = NewSecret();
        var code = new ClaimCode
        {
            Id = Guid.NewGuid(),
            ExpertId = expertId,
            CodeHash = HashOf(plaintext),
            IssuedByUserId = issuedByUserId,
            IssuedAt = at,
        };

        return (code, plaintext);
    }

    /// <summary>
    /// The lookup key for a submitted code. Normalises first — case and the grouping dashes a
    /// person will copy along with it are not part of the secret, and refusing a correct code
    /// because it was typed in lower case sends somebody back to the Service Manager for nothing.
    /// </summary>
    public static string HashOf(string plaintext)
    {
        var normalised = Normalise(plaintext);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }

    public static string Normalise(string plaintext) =>
        new(plaintext.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string NewSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(SecretBytes);
        var chars = new char[bytes.Length * 8 / 5];
        var bit = 0;
        for (var i = 0; i < chars.Length; i++)
        {
            var value = 0;
            for (var b = 0; b < 5; b++, bit++)
            {
                value = (value << 1) | ((bytes[bit / 8] >> (7 - (bit % 8))) & 1);
            }

            chars[i] = Alphabet[value];
        }

        // Grouped for reading aloud; the dashes are cosmetic and normalisation drops them.
        return string.Join('-', Enumerable.Range(0, chars.Length / 8)
            .Select(g => new string(chars, g * 8, 8)));
    }
}
