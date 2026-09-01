using System.Text.Json;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Web.Auth;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Web.Controllers;

/// <summary>
/// Passwordless auth endpoints. Signup is a two-step WebAuthn registration: <c>begin</c> returns
/// credential-creation options (and a ceremony id), the browser invokes the authenticator, then
/// <c>complete</c> verifies the attestation, creates the account, and issues a session token.
/// Signin and recovery are added by their own issues.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public class AuthController(
    IFido2 fido2,
    IChallengeStore challenges,
    IControlWordHasher controlWords,
    IJwtTokenIssuer tokens,
    IAppDbContext db,
    TimeProvider clock) : ControllerBase
{
    // fido2 models carry their own enum converters (e.g. "public-key"). The app's global MVC
    // JsonStringEnumConverter outranks those type-level converters, so the authenticator responses
    // are bound as raw JSON and deserialized here with clean web defaults instead.
    private static readonly JsonSerializerOptions FidoJson = new(JsonSerializerDefaults.Web);

    [HttpPost("signup/begin")]
    public async Task<ActionResult<SignupBeginResponse>> SignupBegin(SignupBeginRequest request, CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { error = "email is required." });
        }

        if (string.IsNullOrWhiteSpace(request.ControlWord))
        {
            return BadRequest(new { error = "controlWord is required." });
        }

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Conflict(new { error = "An account with this email already exists." });
        }

        // The user handle is the eventual user id, so a resident-key assertion at signin resolves
        // straight back to the account without a separate lookup table.
        var userId = Guid.NewGuid();
        var fidoUser = new Fido2User { Id = userId.ToByteArray(), Name = email, DisplayName = email };

        var options = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = fidoUser,
            ExcludeCredentials = [],
            AuthenticatorSelection = new AuthenticatorSelection
            {
                // Required so the passkey is discoverable — enables usernameless sign-in.
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Preferred,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });

        var ceremony = new SignupCeremony(userId, email, controlWords.Hash(request.ControlWord), options.ToJson());
        var ceremonyId = await challenges.StashAsync(JsonSerializer.Serialize(ceremony), ct);

        return Ok(new SignupBeginResponse(ceremonyId, options.ToJson()));
    }

    [HttpPost("signup/complete")]
    public async Task<ActionResult<AuthSessionResponse>> SignupComplete(SignupCompleteRequest request, CancellationToken ct)
    {
        var stashed = await challenges.ConsumeAsync(request.CeremonyId, ct);
        if (stashed is null)
        {
            return BadRequest(new { error = "Registration ceremony expired or not found. Start over." });
        }

        var ceremony = JsonSerializer.Deserialize<SignupCeremony>(stashed)!;
        var options = CredentialCreateOptions.FromJson(ceremony.OptionsJson);

        var attestation = request.Attestation.Deserialize<AuthenticatorAttestationRawResponse>(FidoJson);
        if (attestation is null)
        {
            return BadRequest(new { error = "Invalid attestation response." });
        }

        RegisteredPublicKeyCredential credential;
        try
        {
            credential = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = attestation,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = async (args, innerCt) =>
                    !await db.PasskeyCredentials.AnyAsync(p => p.CredentialId == args.CredentialId, innerCt),
            }, ct);
        }
        catch (Fido2VerificationException ex)
        {
            return BadRequest(new { error = $"Passkey registration failed: {ex.Message}" });
        }

        // Re-check uniqueness: another signup with the same email could have completed since begin.
        if (await db.Users.AnyAsync(u => u.Email == ceremony.Email, ct))
        {
            return Conflict(new { error = "An account with this email already exists." });
        }

        var now = clock.GetUtcNow();
        var user = new User
        {
            Id = ceremony.UserId,
            Email = ceremony.Email,
            ControlWordHash = ceremony.ControlWordHash,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            Passkeys =
            {
                new PasskeyCredential
                {
                    Id = Guid.NewGuid(),
                    CredentialId = credential.Id,
                    PublicKey = credential.PublicKey,
                    SignatureCounter = credential.SignCount,
                    AaGuid = credential.AaGuid == Guid.Empty ? null : credential.AaGuid,
                    Transports = credential.Transports is { Length: > 0 }
                        ? string.Join(",", credential.Transports)
                        : null,
                    CreatedAt = now,
                },
            },
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var (token, expiresAt) = tokens.Issue(user);
        return Ok(new AuthSessionResponse(token, expiresAt, user.Id, user.Email));
    }

    [HttpPost("signin/begin")]
    public async Task<ActionResult<SigninBeginResponse>> SigninBegin(SigninBeginRequest request, CancellationToken ct)
    {
        // Usernameless by default: with no email we send no allowed credentials and let the browser
        // offer its discoverable passkeys. An email narrows the options to that account's
        // credentials — a fallback for authenticators that didn't store a discoverable key.
        var allowed = new List<PublicKeyCredentialDescriptor>();
        var email = request.Email?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(email))
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            if (user is null)
            {
                return BadRequest(new { error = "No account found for this email." });
            }

            var credentialIds = await db.PasskeyCredentials
                .Where(p => p.UserId == user.Id)
                .Select(p => p.CredentialId)
                .ToListAsync(ct);

            if (credentialIds.Count == 0)
            {
                return BadRequest(new { error = "No passkey is registered for this account. Use account recovery." });
            }

            allowed = credentialIds.Select(id => new PublicKeyCredentialDescriptor(id)).ToList();
        }

        var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowed,
            UserVerification = UserVerificationRequirement.Preferred,
        });

        var ceremonyId = await challenges.StashAsync(options.ToJson(), ct);
        return Ok(new SigninBeginResponse(ceremonyId, options.ToJson()));
    }

    [HttpPost("signin/complete")]
    public async Task<ActionResult<AuthSessionResponse>> SigninComplete(SigninCompleteRequest request, CancellationToken ct)
    {
        var optionsJson = await challenges.ConsumeAsync(request.CeremonyId, ct);
        if (optionsJson is null)
        {
            return BadRequest(new { error = "Sign-in ceremony expired or not found. Start over." });
        }

        var options = AssertionOptions.FromJson(optionsJson);

        var assertion = request.Assertion.Deserialize<AuthenticatorAssertionRawResponse>(FidoJson);
        if (assertion is null)
        {
            return BadRequest(new { error = "Invalid assertion response." });
        }

        // Resolve the account from the credential that signed (credential ids are globally unique),
        // so usernameless sign-in needs no email up front.
        var credentialId = assertion.RawId;
        var credential = await db.PasskeyCredentials
            .FirstOrDefaultAsync(p => p.CredentialId == credentialId, ct);
        if (credential is null)
        {
            return BadRequest(new { error = "Unknown passkey. Sign up or use account recovery." });
        }

        VerifyAssertionResult result;
        try
        {
            result = await fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = assertion,
                OriginalOptions = options,
                StoredPublicKey = credential.PublicKey,
                StoredSignatureCounter = (uint)credential.SignatureCounter,
                IsUserHandleOwnerOfCredentialIdCallback = async (args, innerCt) =>
                {
                    if (args.UserHandle is not { Length: 16 })
                    {
                        return false;
                    }

                    var handle = new Guid(args.UserHandle);
                    return await db.PasskeyCredentials
                        .AnyAsync(p => p.CredentialId == args.CredentialId && p.UserId == handle, innerCt);
                },
            }, ct);
        }
        catch (Fido2VerificationException ex)
        {
            return BadRequest(new { error = $"Sign-in failed: {ex.Message}" });
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == credential.UserId, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This account is not active." });
        }

        // Advance the stored counter to the authenticator's latest value (clone-detection guard).
        credential.SignatureCounter = result.SignCount;
        await db.SaveChangesAsync(ct);

        var (token, expiresAt) = tokens.Issue(user);
        return Ok(new AuthSessionResponse(token, expiresAt, user.Id, user.Email));
    }

    [HttpPost("recover/begin")]
    public async Task<ActionResult<RecoverBeginResponse>> RecoverBegin(RecoverBeginRequest request, CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.ControlWord))
        {
            return BadRequest(new { error = "email and controlWord are required." });
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // One generic failure for "no such account", "wrong control word", and "deactivated" — don't
        // leak which emails exist or whether the control word was the wrong part.
        if (user is null
            || user.Status != UserStatus.Active
            || !controlWords.Verify(request.ControlWord, user.ControlWordHash))
        {
            return BadRequest(new { error = "Invalid email or control word." });
        }

        var existing = await db.PasskeyCredentials
            .Where(p => p.UserId == user.Id)
            .Select(p => p.CredentialId)
            .ToListAsync(ct);

        var fidoUser = new Fido2User { Id = user.Id.ToByteArray(), Name = user.Email, DisplayName = user.Email };
        var options = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = fidoUser,
            // Exclude already-registered authenticators so the user enrols a genuinely new device.
            ExcludeCredentials = existing.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Preferred,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });

        var ceremony = new RecoverCeremony(user.Id, options.ToJson());
        var ceremonyId = await challenges.StashAsync(JsonSerializer.Serialize(ceremony), ct);

        return Ok(new RecoverBeginResponse(ceremonyId, options.ToJson()));
    }

    [HttpPost("recover/complete")]
    public async Task<ActionResult<AuthSessionResponse>> RecoverComplete(RecoverCompleteRequest request, CancellationToken ct)
    {
        var stashed = await challenges.ConsumeAsync(request.CeremonyId, ct);
        if (stashed is null)
        {
            return BadRequest(new { error = "Recovery ceremony expired or not found. Start over." });
        }

        var ceremony = JsonSerializer.Deserialize<RecoverCeremony>(stashed)!;
        var options = CredentialCreateOptions.FromJson(ceremony.OptionsJson);

        var attestation = request.Attestation.Deserialize<AuthenticatorAttestationRawResponse>(FidoJson);
        if (attestation is null)
        {
            return BadRequest(new { error = "Invalid attestation response." });
        }

        RegisteredPublicKeyCredential credential;
        try
        {
            credential = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = attestation,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = async (args, innerCt) =>
                    !await db.PasskeyCredentials.AnyAsync(p => p.CredentialId == args.CredentialId, innerCt),
            }, ct);
        }
        catch (Fido2VerificationException ex)
        {
            return BadRequest(new { error = $"Passkey registration failed: {ex.Message}" });
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == ceremony.UserId, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This account is not active." });
        }

        var now = clock.GetUtcNow();
        db.PasskeyCredentials.Add(new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CredentialId = credential.Id,
            PublicKey = credential.PublicKey,
            SignatureCounter = credential.SignCount,
            AaGuid = credential.AaGuid == Guid.Empty ? null : credential.AaGuid,
            Transports = credential.Transports is { Length: > 0 } ? string.Join(",", credential.Transports) : null,
            CreatedAt = now,
        });
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var (token, expiresAt) = tokens.Issue(user);
        return Ok(new AuthSessionResponse(token, expiresAt, user.Id, user.Email));
    }

    /// <summary>Server-side signup state held between the begin and complete steps.</summary>
    private sealed record SignupCeremony(Guid UserId, string Email, string ControlWordHash, string OptionsJson);

    /// <summary>Server-side recovery state held between the begin and complete steps.</summary>
    private sealed record RecoverCeremony(Guid UserId, string OptionsJson);
}

public sealed record SignupBeginRequest(string Email, string ControlWord);
public sealed record SignupBeginResponse(string CeremonyId, string OptionsJson);
public sealed record SignupCompleteRequest(string CeremonyId, JsonElement Attestation);
public sealed record SigninBeginRequest(string? Email);
public sealed record SigninBeginResponse(string CeremonyId, string OptionsJson);
public sealed record SigninCompleteRequest(string CeremonyId, JsonElement Assertion);
public sealed record RecoverBeginRequest(string Email, string ControlWord);
public sealed record RecoverBeginResponse(string CeremonyId, string OptionsJson);
public sealed record RecoverCompleteRequest(string CeremonyId, JsonElement Attestation);
public sealed record AuthSessionResponse(string Token, DateTimeOffset ExpiresAt, Guid UserId, string Email);
