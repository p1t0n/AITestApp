using System.Text.Json;
using EmployeeManager.Application.Abstractions;
using EmployeeManager.Domain.Entities;
using EmployeeManager.Domain.Enums;
using EmployeeManager.Web.Auth;
using Fido2NetLib;
using Fido2NetLib.Objects;
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
[Route("api/auth")]
public class AuthController(
    IFido2 fido2,
    IChallengeStore challenges,
    IControlWordHasher controlWords,
    IJwtTokenIssuer tokens,
    IAppDbContext db,
    TimeProvider clock) : ControllerBase
{
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

        var credential = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = request.Attestation,
            OriginalOptions = options,
            IsCredentialIdUniqueToUserCallback = async (args, innerCt) =>
                !await db.PasskeyCredentials.AnyAsync(p => p.CredentialId == args.CredentialId, innerCt),
        }, ct);

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

    /// <summary>Server-side signup state held between the begin and complete steps.</summary>
    private sealed record SignupCeremony(Guid UserId, string Email, string ControlWordHash, string OptionsJson);
}

public sealed record SignupBeginRequest(string Email, string ControlWord);
public sealed record SignupBeginResponse(string CeremonyId, string OptionsJson);
public sealed record SignupCompleteRequest(string CeremonyId, AuthenticatorAttestationRawResponse Attestation);
public sealed record AuthSessionResponse(string Token, DateTimeOffset ExpiresAt, Guid UserId, string Email);
