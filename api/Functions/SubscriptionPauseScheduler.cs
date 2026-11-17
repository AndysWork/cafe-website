using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public sealed class SubscriptionPauseScheduler(IOperationsRepository repository, ILogger<SubscriptionPauseScheduler> logger)
{
    [Function("ResumeSubscriptionPauses")]
    public async Task Run([TimerTrigger("0 * * * * *")] TimerInfo timer)
    {
        var count = await repository.ResumeDueSubscriptionPausesAsync(MongoService.GetIstNow());
        if (count > 0) logger.LogInformation("Automatically resumed {Count} subscription pauses", count);
    }
}
