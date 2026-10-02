using ExpertToJob.Agents.RosterScan;
using ExpertToJob.Agents.Staffing;
using ExpertToJob.Agents.Usage;
using Microsoft.Extensions.Options;

namespace ExpertToJob.Agents.Configuration;

/// <summary>
/// The operator knobs (EXP-107): the settings somebody running this host has to be able to turn
/// without a deploy — the system-default token caps, the Roster Scan quota budget, and the
/// staffing match concurrency. They live in <c>api/Agents/appsettings.json</c> and nowhere else:
/// EXP-89's rule is that each value sits on exactly one side, and for these the side is the file,
/// because a number hidden in a property initialiser is one nobody knows they can change. The 50k
/// daily cap was hit inside a single verification session.
///
/// <para>Which is why the binding is validated here rather than trusted. With the property
/// defaults gone, a missing key binds to zero, and zero is not a smaller setting — it is a host
/// that caps every new user at no tokens, paces the scan limiter at no requests, and hands
/// <see cref="StaffingThrottle"/> a semaphore with no slots, deadlocking every staffing run
/// instead of failing one. <c>ValidateOnStart</c> turns all of that into a refusal to boot that
/// names the key, and the <c>Range</c> error messages spell the configuration path rather than
/// the property, because the operator reading the failure is holding a JSON file.</para>
///
/// <para>One method rather than three registrations inline, so the validation is provable without
/// starting a web host: <c>WebApplicationFactory</c>'s deferred host races its own disposal when
/// startup throws, and surfaces <c>ObjectDisposedException</c> instead of the real exception often
/// enough to make such a test flaky under load.</para>
/// </summary>
public static class OperatorKnobServiceCollectionExtensions
{
    public static IServiceCollection AddOperatorKnobs(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<UsageOptions>(configuration, UsageOptions.Section);
        services.AddValidatedOptions<StaffingOptions>(configuration, StaffingOptions.Section);
        services.AddValidatedOptions<RosterScanOptions>(configuration, RosterScanOptions.Section);

        // Roster Scan's transport, runner and submit estimate inject the bare options object, so
        // the validated instance is surfaced under that type too rather than bound a second time.
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<RosterScanOptions>>().Value);

        return services;
    }

    private static void AddValidatedOptions<TOptions>(
        this IServiceCollection services, IConfiguration configuration, string section)
        where TOptions : class
        => services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
}
