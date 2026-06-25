namespace EmployeeManager.Web.Auth;

/// <summary>
/// Hashes and verifies the account-recovery control word. Same one-way-hash treatment a password
/// would get — the plaintext is never stored.
/// </summary>
public interface IControlWordHasher
{
    string Hash(string controlWord);
    bool Verify(string controlWord, string hash);
}
