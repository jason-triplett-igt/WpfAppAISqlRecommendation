// IAiService.cs
using System.Collections.Generic;
using System.Threading;

namespace WinUIAppSqlRecommendation.Services
{
    public interface IAiService
    {
        // Takes SQL text and optionally the XML execution plan
        // Changed return type to IAsyncEnumerable<string>
        // Added CancellationToken for better cancellation support
        IAsyncEnumerable<string> GetQueryOptimizationRecommendationStreamAsync(
            string sqlQuery,
            string? executionPlanXml,
            CancellationToken cancellationToken = default); // Added CancellationToken    }
    }

}