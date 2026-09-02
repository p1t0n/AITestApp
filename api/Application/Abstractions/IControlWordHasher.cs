namespace ExpertToJob.Application.Abstractions;

/// <summary>
/// Hashes and verifies the account-recovery control word. Same one-way-hash treatment a password
/// would get — the plaintext is never stored.
///
/// <para>The interface lives in the Application layer and the implementation in the Web host
/// (P1T-186). The control word is the only proof-of-person this service has — there is no email, so
/// no confirmation link and no way to tell anybody afterwards — so the two acts that turn on it,
/// recovery and erasure, both verify it themselves rather than trusting a caller to have done it.
/// </para>
/// </summary>
public interface IControlWordHasher
{
    string Hash(string controlWord);
    bool Verify(string controlWord, string hash);
}
