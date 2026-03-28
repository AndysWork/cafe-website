using Microsoft.Azure.Functions.Worker.Http;

namespace Cafe.Api.Helpers;

public static class PaginationHelper
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;
    public const int SafetyLimit = 5000;

    public static (int? page, int? pageSize) ParsePagination(HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var pageStr = query["page"];
        var pageSizeStr = query["pageSize"];

        int? page = null;
        int? pageSize = null;

        if (int.TryParse(pageStr, out var p) && p > 0)
            page = p;

        if (int.TryParse(pageSizeStr, out var ps) && ps > 0)
            pageSize = Math.Min(ps, MaxPageSize);

        // Both must be present for pagination to be active
        if (page.HasValue && !pageSize.HasValue)
            pageSize = DefaultPageSize;
        if (pageSize.HasValue && !page.HasValue)
            page = 1;

        return (page, pageSize);
    }

    public static void AddPaginationHeaders(HttpResponseData response, long totalCount, int page, int pageSize)
    {
        response.Headers.Add("X-Total-Count", totalCount.ToString());
        response.Headers.Add("X-Page", page.ToString());
        response.Headers.Add("X-Page-Size", pageSize.ToString());
        response.Headers.Add("X-Total-Pages", ((int)Math.Ceiling((double)totalCount / pageSize)).ToString());
    }
}
