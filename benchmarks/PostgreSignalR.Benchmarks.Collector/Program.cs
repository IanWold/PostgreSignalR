using System.Collections.Concurrent;

var results = new ConcurrentDictionary<string, string>();

var app = WebApplication.CreateBuilder(args).Build();

app.MapPost("/results/{key}", async (string key, HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    results[key] = await reader.ReadToEndAsync();
    return Results.Ok();
});

app.MapGet("/results/{key}", async (string key, HttpContext context) =>
{
    var deadline = DateTime.UtcNow.AddSeconds(
        context.Request.Query.TryGetValue("waitSeconds", out var waitSecondsRaw)
        && int.TryParse(waitSecondsRaw, out var parsedWaitSeconds)
            ? parsedWaitSeconds
            : 0
    );

    while (true)
    {
        if (results.TryGetValue(key, out var body))
        {
            return Results.Text(body);
        }

        if (DateTime.UtcNow >= deadline)
        {
            return Results.NotFound();
        }

        await Task.Delay(250, context.RequestAborted);
    }
});

app.Run();
