using System.Text.Json;
using EmployeeManager.Application.Abstractions;
using EmployeeManager.Domain.Entities;
using EmployeeManager.Domain.Enums;
using EmployeeManager.Web.Auth;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManager.Web.Controllers;

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
                ResidentKey = ResidentKeyRequirement.Preferred,
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
        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { error = "email is required." });
        }

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

        var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = credentialIds.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            UserVerification = UserVerificationRequirement.Preferred,
        });

        var ceremony = new SigninCeremony(user.Id, options.ToJson());
        var ceremonyId = await challenges.StashAsync(JsonSerializer.Serialize(ceremony), ct);

        return Ok(new SigninBeginResponse(ceremonyId, options.ToJson()));
    }

    [HttpPost("signin/complete")]
    public async Task<ActionResult<AuthSessionResponse>> SigninComplete(SigninCompleteRequest request, CancellationToken ct)
    {
        var stashed = await challenges.ConsumeAsync(request.CeremonyId, ct);
        if (stashed is null)
        {
            return BadRequest(new { error = "Sign-in ceremony expired or not found. Start over." });
        }

        var ceremony = JsonSerializer.Deserialize<SigninCeremony>(stashed)!;
        var options = AssertionOptions.FromJson(ceremony.OptionsJson);

        var assertion = request.Assertion.Deserialize<AuthenticatorAssertionRawResponse>(FidoJson);
        if (assertion is null)
        {
            return BadRequest(new { error = "Invalid assertion response." });
        }

        var credentialId = assertion.RawId;
        var credential = await db.PasskeyCredentials
            .FirstOrDefaultAsync(p => p.UserId == ceremony.UserId && p.CredentialId == credentialId, ct);
        if (credential is null)
        {
            return BadRequest(new { error = "Unknown credential for this account." });
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

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == ceremony.UserId, ct);
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

    /// <summary>Server-side signup state held between the begin and complete steps.</summary>
    private sealed record SignupCeremony(Guid UserId, string Email, string ControlWordHash, string OptionsJson);

    /// <summary>Server-side signin state held between the begin and complete steps.</summary>
    private sealed record SigninCeremony(Guid UserId, string OptionsJson);
}

public sealed record SignupBeginRequest(string Email, string ControlWord);
public sealed record SignupBeginResponse(string CeremonyId, string OptionsJson);
public sealed record SignupCompleteRequest(string CeremonyId, JsonElement Attestation);
public sealed record SigninBeginRequest(string Email);
public sealed record SigninBeginResponse(string CeremonyId, string OptionsJson);
public sealed record SigninCompleteRequest(string CeremonyId, JsonElement Assertion);
public sealed record AuthSessionResponse(string Token, DateTimeOffset ExpiresAt, Guid UserId, string Email);
