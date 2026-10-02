using System.Reflection;
using ExpertToJob.Agents.RosterScan;
using ExpertToJob.Agents.Staffing;
using ExpertToJob.Agents.Usage;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Mvc.Testing;
using ExpertToJob.Agents.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The six knobs EXP-107 moved out of code defaults and into <c>api/Agents/appsettings.json</c>:
/// the three system-default token caps, the two Roster Scan quota numbers, and the staffing match
/// concurrency. They are the settings an operator actually has to turn without a deploy — the 50k
/// daily cap was hit inside one verification session — and a bindable value hidden in a property
/// initialiser is one nobody knows exists.
///
/// <para>This reverses EXP-89's code-default-only choice <em>for these values only</em>, so
/// EXP-89's real rule still holds: each value lives on exactly <em>one</em> side. The side is now
/// the JSON, which is why the property defaults are gone rather than duplicated — and why the
/// options are validated with <c>ValidateOnStart</c>. Without a property default, a missing key
/// binds to zero, and zero is a working-looking host that caps every new user at nothing, paces
/// the scan limiter at no requests, and hands <see cref="StaffingThrottle"/> a semaphore with no
/// slots. Refusing to boot is the only honest answer.</para>
///
/// <para>Replaces the <c>Default_caps_are_50k_daily_150k_weekly_500k_monthly</c> assertion that
/// lived in <c>UsageServiceTests</c> and read the property defaults that no longer exist.</para>
/// </summary>
public class OperatorKnobTests
{
    /// <summary>The shipped numbers, as literals. Not read from the options classes and not read
    /// from the file under a different name — a pin that computes its expectation from the thing
    /// it is pinning passes on any value. Changing one of these in the settings file takes an edit
    /// here too, which is the whole point: it shows up in review.</summary>
    public static readonly (string Key, long Value)[] Shipped =
    [
        ("Usage:DefaultDailyTokens", 50_000),
        ("Usage:DefaultWeeklyTokens", 150_000),
        ("Usage:DefaultMonthlyTokens", 500_000),
        ("RosterScan:RequestsPerMinute", 12),
        ("RosterScan:RequestsPerDay", 500),
        ("Staffing:MaxConcurrentMatches", 2),
    ];

    private static readonly System.Globalization.CultureInfo Invariant =
        System.Globalization.CultureInfo.InvariantCulture;

    public static TheoryData<string, long> ShippedKnobs()
    {
        var data = new TheoryData<string, long>();
        foreach (var (key, value) in Shipped)
        {
            data.Add(key, value);
        }

        return data;
    }

    public static TheoryData<string> KnobKeys()
    {
        var data = new TheoryData<string>();
        foreach (var (key, _) in Shipped)
        {
            data.Add(key);
        }

        return data;
    }

    /// <summary>Every knob is in the shipped file, spelled under the section its options class
    /// reads, carrying the value the code default used to carry.</summary>
    [Theory]
    [MemberData(nameof(ShippedKnobs))]
    public void The_shipped_settings_carry_the_knob(string key, long value)
        => ShippedAgentsSettings.Configuration[key].Should().Be(
            value.ToString(Invariant),
            $"'{key}' is an operator knob and the settings file is the one place it lives (EXP-107)");

    /// <summary>And the shipped file really does bind into the options classes under those paths.
    /// A section rename is not a compile error: the binder answers a path it does not know with
    /// silence, and without property defaults the result is zero rather than the old number.
    /// </summary>
    [Fact]
    public void The_shipped_settings_bind_into_the_options_classes()
    {
        var config = ShippedAgentsSettings.Configuration;

        using var _ = new AssertionScope();

        var usage = config.GetSection(UsageOptions.Section).Get<UsageOptions>()!;
        usage.DefaultDailyTokens.Should().Be(50_000);
        usage.DefaultWeeklyTokens.Should().Be(150_000);
        usage.DefaultMonthlyTokens.Should().Be(500_000);

        var scan = config.GetSection(RosterScanOptions.Section).Get<RosterScanOptions>()!;
        scan.RequestsPerMinute.Should().Be(12);
        scan.RequestsPerDay.Should().Be(500);

        config.GetSection(StaffingOptions.Section).Get<StaffingOptions>()!
            .MaxConcurrentMatches.Should().Be(2);
    }

    /// <summary>The other half of "one side": no property initialiser is left to fall back to.
    /// A re-added default would be invisible — the host would keep working on a machine whose
    /// settings file had lost the key, which is exactly the silence the validation exists to
    /// break.</summary>
    [Fact]
    public void The_knobs_have_no_code_default()
    {
        using var _ = new AssertionScope();

        var usage = new UsageOptions();
        usage.DefaultDailyTokens.Should().Be(0);
        usage.DefaultWeeklyTokens.Should().Be(0);
        usage.DefaultMonthlyTokens.Should().Be(0);

        var scan = new RosterScanOptions();
        scan.RequestsPerMinute.Should().Be(0);
        scan.RequestsPerDay.Should().Be(0);

        new StaffingOptions().MaxConcurrentMatches.Should().Be(0);
    }

    /// <summary>Each knob is range-validated, and the message names the configuration path rather
    /// than only the property — the operator reading the startup failure is holding a JSON file,
    /// not a class.</summary>
    [Theory]
    [MemberData(nameof(KnobKeys))]
    public void Each_knob_is_range_validated_and_names_its_key(string key)
    {
        var (section, property) = Split(key);
        var owner = section switch
        {
            UsageOptions.Section => typeof(UsageOptions),
            RosterScanOptions.Section => typeof(RosterScanOptions),
            StaffingOptions.Section => typeof(StaffingOptions),
            _ => throw new InvalidOperationException($"No options class owns '{section}'."),
        };

        var range = owner.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttribute<System.ComponentModel.DataAnnotations.RangeAttribute>();

        range.Should().NotBeNull($"'{key}' has no code default, so zero has to be rejected");
        Convert.ToInt64(range!.Minimum).Should().Be(1, "the knob has to be greater than 0");
        range.ErrorMessage.Should().Contain(key, "the startup failure has to name the key an operator can edit");
    }

    /// <summary>A zero knob stops the host, naming the key. Run against the host's own
    /// <see cref="OperatorKnobServiceCollectionExtensions.AddOperatorKnobs"/> and its own
    /// <see cref="IAsyncStartupValidator"/> — that is the production registration, not a re-creation of
    /// it, and <see cref="The_host_validates_the_knobs_on_start"/> below holds the other half:
    /// that the host really calls it.
    ///
    /// <para>Not driven through <c>WebApplicationFactory</c>, which was tried: when the inner host
    /// fails to start, its deferred host races its own disposal and surfaces
    /// <c>ObjectDisposedException</c> instead of the validation failure. It passed alone and went
    /// red in the full solution run, which is a flaky test rather than a stricter one.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(KnobKeys))]
    public void A_zero_knob_fails_startup_naming_the_key(string key)
    {
        var act = () => ValidateOnStart(Knobs(k => k.Key == key ? "0" : k.Value.ToString(Invariant)));

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{key}*", "the operator has to be told which key to fix");
    }

    /// <summary>And so does a missing one — the case a deployment that forgot the section
    /// produces. Without a property default the binder leaves the CLR zero behind, which is why
    /// "absent" and "zero" have to fail the same way.</summary>
    [Theory]
    [MemberData(nameof(KnobKeys))]
    public void A_missing_knob_fails_startup_naming_the_key(string key)
    {
        var act = () => ValidateOnStart(Knobs(k => k.Key == key ? null : k.Value.ToString(Invariant)));

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{key}*", "an absent key has to fail as loudly as a zero");
    }

    /// <summary>The half the two above cannot see: that the Agents host wires the knobs through
    /// that registration at all. A deleted call would leave every test above green and every knob
    /// unvalidated. Asserted on a host that starts normally, so there is no failure for the
    /// deferred host to race.</summary>
    [Fact]
    public void The_host_validates_the_knobs_on_start()
    {
        using var factory = new WebApplicationFactory<Program>();
        var services = factory.Services;

        using var _ = new AssertionScope();

        services.GetService<IAsyncStartupValidator>().Should().NotBeNull(
            "ValidateOnStart is what turns an unset knob into a refusal to boot");

        // Asserted by behaviour, not by validator type: the runtime wraps the data-annotation
        // validator in its own filter, and pinning that wrapper's name would be pinning an
        // implementation detail of the framework rather than this host's wiring.
        RejectsAllZeroes<UsageOptions>(services, "Usage:DefaultDailyTokens");
        RejectsAllZeroes<StaffingOptions>(services, "Staffing:MaxConcurrentMatches");
        RejectsAllZeroes<RosterScanOptions>(services, "RosterScan:RequestsPerMinute");

        // And the shipped file is what the running host read, under the paths it reads.
        services.GetRequiredService<IOptions<UsageOptions>>().Value.DefaultDailyTokens.Should().Be(50_000);
        services.GetRequiredService<IOptions<StaffingOptions>>().Value.MaxConcurrentMatches.Should().Be(2);
        // The bare type too: the transport, runner and submit estimate inject it directly.
        services.GetRequiredService<RosterScanOptions>().RequestsPerDay.Should().Be(500);
    }

    /// <summary>The validators the host registered for <typeparamref name="TOptions"/> reject the
    /// instance an empty configuration section produces, and say which key is missing.</summary>
    private static void RejectsAllZeroes<TOptions>(IServiceProvider services, string key)
        where TOptions : class, new()
    {
        var failures = services.GetServices<IValidateOptions<TOptions>>()
            .Select(v => v.Validate(Options.DefaultName, new TOptions()))
            .Where(r => r.Failed)
            .SelectMany(r => r.Failures)
            .ToList();

        failures.Should().Contain(f => f.Contains(key),
            $"the host has to reject an unset {typeof(TOptions).Name} and name '{key}'");
    }

    /// <summary>Every knob at its shipped value, except what <paramref name="replace"/> says:
    /// a replacement string, or null to leave the key out of the configuration entirely.</summary>
    private static IConfiguration Knobs(Func<(string Key, long Value), string?> replace) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(Shipped
                .Select(k => new KeyValuePair<string, string?>(k.Key, replace(k)))
                .Where(pair => pair.Value is not null))
            .Build();

    /// <summary>Runs exactly what the host runs at startup, over that configuration.</summary>
    private static void ValidateOnStart(IConfiguration configuration)
    {
        using var services = new ServiceCollection()
            .AddOperatorKnobs(configuration)
            .BuildServiceProvider();

        services.GetRequiredService<IAsyncStartupValidator>().ValidateAsync().GetAwaiter().GetResult();
    }

    private static (string Section, string Property) Split(string key)
    {
        var parts = key.Split(':');
        return (parts[0], parts[1]);
    }
}

/// <summary>The Agents host's shipped settings file, read as configuration. Shared with the
/// ingestion cost floor, which prices a Runtime Budget against the shipped daily cap and used to
/// read it from a property default that no longer exists (EXP-107).</summary>
internal static class ShippedAgentsSettings
{
    public static IConfigurationRoot Configuration { get; } = new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(RepoRoot(), "api", "Agents", "appsettings.json"))
        .Build();

    public static long DefaultDailyTokens =>
        Configuration.GetSection(UsageOptions.Section).Get<UsageOptions>()!.DefaultDailyTokens;

    /// <summary>Walks up from the test binary until the solution file appears — the tests run from
    /// <c>bin/</c>, and hard-coding a depth breaks the first time the layout moves.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpertToJob.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not find ExpertToJob.slnx above the test binary; the shipped settings cannot be read.");
    }
}
