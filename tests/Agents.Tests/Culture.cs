using System.Globalization;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Runs a body under a pinned culture (P1T-200).
///
/// <para>On its own thread, deliberately. <see cref="CultureInfo.CurrentCulture"/> is ambient state
/// that flows across <c>await</c> boundaries, and xUnit runs test classes in parallel on pooled
/// threads — setting it inline would let one test's culture leak into another's assertions and
/// produce a failure nobody can reproduce. A thread that exists for the duration of the body and
/// dies with it cannot leak.</para>
///
/// <para>The reason this exists at all: numbers that reach a model or a user were being formatted
/// in whatever culture the host happened to have, so the same code produced <c>5,000</c> here and
/// <c>5.000</c> there. Tests that only ever run in one culture cannot see that.</para>
/// </summary>
public static class Culture
{
    /// <summary>A culture that formats numbers differently from English: <c>.</c> groups thousands
    /// and <c>,</c> is the decimal point, which is the exact inversion of what the assertions in
    /// these tests expect to read.</summary>
    public const string Other = "de-DE";

    public static T Under<T>(string name, Func<T> body)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            var culture = new CultureInfo(name);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            try
            {
                result = body();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });

        thread.Start();
        thread.Join();

        if (failure is not null) throw new InvalidOperationException($"Failed under {name}.", failure);
        return result;
    }

    /// <summary>The async overload. The body is awaited *inside* the pinned thread, so every
    /// continuation it schedules inherits that thread's culture through the execution context.</summary>
    public static T Under<T>(string name, Func<Task<T>> body) =>
        Under(name, () => body().GetAwaiter().GetResult());
}
