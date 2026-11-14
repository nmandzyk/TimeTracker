using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TimeTracker.Core;

namespace TimeTracker.Api;

public record CreateReportRequest(DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, string? Description);

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/reports", async (CreateReportRequest req, IReportStore store) =>
        {
            var errors = Validate(req);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var entry = await store.AddAsync(req.StartUtc!.Value.UtcDateTime, req.EndUtc!.Value.UtcDateTime,
                req.Description!.Trim());
            return Results.Created($"/api/reports/{JsonReportStore.DayKey(entry.StartUtc)}", entry);
        });

        app.MapGet("/api/reports/{date}", async (string date, IReportStore store) =>
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var day))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["date"] = ["Date must be in yyyy-MM-dd format."],
                });

            return Results.Ok(await store.GetByDateAsync(day));
        });
    }

    private static Dictionary<string, string[]> Validate(CreateReportRequest req)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(req.Description))
            errors["description"] = ["Description is required."];
        else if (req.Description.Trim().Length > 500)
            errors["description"] = ["Description must be at most 500 characters."];

        if (req.StartUtc is null || req.StartUtc.Value.Offset != TimeSpan.Zero)
            errors["startUtc"] = ["startUtc is required and must be UTC (e.g. 2026-11-12T08:00:00Z)."];
        if (req.EndUtc is null || req.EndUtc.Value.Offset != TimeSpan.Zero)
            errors["endUtc"] = ["endUtc is required and must be UTC (e.g. 2026-11-12T09:30:00Z)."];

        if (errors.Count == 0)
        {
            if (req.EndUtc <= req.StartUtc)
                errors["endUtc"] = ["endUtc must be after startUtc."];
            else if (req.EndUtc - req.StartUtc > TimeSpan.FromHours(24))
                errors["endUtc"] = ["Range must not exceed 24 hours."];
        }

        return errors;
    }
}
