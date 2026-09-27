using ExpertToJob.Application.Compliance;
using ExpertToJob.Infrastructure.Embeddings;

namespace ExpertToJob.Mcp.Compliance;

/// <summary>
/// Writes the embeddings provider history at startup (EXP-66,
/// <c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 17): if the provider this host is
/// running differs from the latest period, close that period and open a new one.
///
/// <para><b>Here rather than in the Web host</b>, because this is the host that actually embeds.
/// Switching provider is a configuration edit nobody records anywhere else, and the Web host — which
/// only <em>discloses</em> the choice — could be told the wrong thing and would then write a
/// history that never happened. The host doing the sending is the one whose word about it is worth
/// anything.</para>
///
/// <para><b>A failure is logged, not thrown.</b> A host that cannot reach the database at startup
/// has a worse problem than an unwritten period row, and taking the roster's only search backend
/// down over it would not fix the disclosure — the next start writes the row. It is logged at
/// error, because a deployment that switched provider and never recorded it shows a data subject a
/// disclosure with a gap in it.</para>
/// </summary>
public sealed class EmbeddingsProviderPeriodWriter(
    IServiceScopeFactory scopeFactory,
    EmbeddingsProvider provider,
    ILogger<EmbeddingsProviderPeriodWriter> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var history = scope.ServiceProvider.GetRequiredService<IEmbeddingsProviderHistory>();
            await history.RecordActiveProviderAsync(provider.ToString(), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not record {Provider} as the active embeddings provider. The Art. 15 "
                + "recipient disclosure reads this history, so a switch that goes unrecorded is a "
                + "gap in what a data subject is told.",
                provider);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
