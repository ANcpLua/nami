using System.Globalization;
using Nami.Orchestration;

if (Environment.GetEnvironmentVariable("ORCHESTRATION_SMOKE_WORKER") is { } mode)
{
    if (Environment.GetEnvironmentVariable("ORCHESTRATION_SMOKE_PID") is { } file)
        await File.WriteAllTextAsync(file, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
    if (mode == "fail")
    {
        await Console.Error.WriteLineAsync("orchestration smoke startup failure");
        return 17;
    }
    var worker = WebApplication.CreateBuilder(args);
    worker.Logging.ClearProviders();
    worker.Services.AddHttpClient();
    worker.Services.AddHostedService(static services => new ShutdownMarker(
        services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IHttpClientFactory>()));
    await using var app = worker.Build();
    if (worker.Configuration["services:payments:http:0"] is { } dependency)
    {
        using var dependencyClient = new HttpClient();
        using var healthy = await dependencyClient.GetAsync(new Uri(new Uri(dependency), "/health"));
        healthy.EnsureSuccessStatusCode();
    }
    app.MapGet("/health", () => mode == "unhealthy"
        ? Results.StatusCode(503)
        : Results.Text(Environment.ProcessId.ToString(CultureInfo.InvariantCulture)));
    app.MapGet("/dependency", () => worker.Configuration["services:payments:http:0"] ?? "none");
    app.MapGet("/value", () => Environment.GetEnvironmentVariable("ORCHESTRATION_SMOKE_VALUE") ?? "missing");
    app.MapPost("/flush", async () =>
    {
        if (Environment.GetEnvironmentVariable("ORCHESTRATION_SMOKE_RECEIVED") is { } received)
            await File.WriteAllTextAsync(received, "received");
        return Results.Ok();
    });
    await app.RunAsync();
    return 0;
}

if (args is ["--expected-version", var expectedVersion])
    Smoke.Require(typeof(NamiApplication).Assembly.GetName().Version?.ToString(3) == expectedVersion,
        "The consumer loaded a different orchestration package version.");

var scratch = Path.Combine(Path.GetTempPath(), $"nami-orchestration-smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(scratch);
try
{
    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The smoke process path is unavailable.");
    var builder = new NamiApplicationBuilder { StartupTimeoutSeconds = 10, MaxRestarts = 0 };
    var payments = Smoke.Worker(builder, "payments", executable, "ready");
    var stopped = Path.Combine(scratch, "payments.stopped");
    var received = Path.Combine(scratch, "payments.received");
    payments.WithEnvironment("ORCHESTRATION_SMOKE_STOPPED", stopped)
        .WithEnvironment("ORCHESTRATION_SMOKE_RECEIVED", received);
    Smoke.Worker(builder, "orders", executable, "ready")
        .WithReference(payments).WaitFor(payments)
        .WithoutEnvironmentPrefix("ORCHESTRATION_SMOKE_")
        .WithEnvironment("ORCHESTRATION_SMOKE_VALUE", "argument with spaces")
        .WithEnvironment("ORCHESTRATION_SMOKE_FLUSH_TO", "payments");
    {
        var app = builder.Build();
        try
        {
            await Smoke.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => payments.WithEnvironment("LATE", "invalid")));
            await app.StartAsync();
            using var client = new HttpClient();
            var paymentAddress = app.GetEndpoint("payments");
            var orderAddress = app.GetEndpoint("orders");
            Smoke.Require(paymentAddress.Port != orderAddress.Port, "Service ports collided.");
            Smoke.Require(await client.GetStringAsync(new Uri(orderAddress, "/dependency")) == paymentAddress.ToString(),
                "The reference endpoint did not reach the child.");
            Smoke.Require(await client.GetStringAsync(new Uri(orderAddress, "/value")) == "argument with spaces",
                "Explicit environment must survive inherited-prefix removal.");
            using var paymentProcess = Process.GetProcessById(int.Parse(
                await client.GetStringAsync(new Uri(paymentAddress, "/health")), CultureInfo.InvariantCulture));
            using var orderProcess = Process.GetProcessById(int.Parse(
                await client.GetStringAsync(new Uri(orderAddress, "/health")), CultureInfo.InvariantCulture));
            await app.DisposeAsync();
            Smoke.Require(paymentProcess.HasExited && orderProcess.HasExited, "Dispose left a child alive.");
            if (!OperatingSystem.IsWindows())
            {
                Smoke.Require(File.Exists(stopped) && await File.ReadAllTextAsync(stopped) == "flushed",
                    "Dispose bypassed the child's asynchronous hosted-service shutdown.");
                Smoke.Require(File.Exists(received) && await File.ReadAllTextAsync(received) == "received",
                    "Dispose stopped the dependency before its dependent finished exporting.");
            }
        }
        finally
        {
            await app.DisposeAsync();
        }
    }
    Console.WriteLine("PASS: real services, references, dependency readiness, environment, shutdown");

    var failedPeer = Path.Combine(scratch, "failed-peer.pid");
    var failing = new NamiApplicationBuilder { StartupTimeoutSeconds = 10, MaxRestarts = 0 };
    var peer = Smoke.Worker(failing, "peer", executable, "ready").WithEnvironment("ORCHESTRATION_SMOKE_PID", failedPeer);
    Smoke.Worker(failing, "broken", executable, "fail").WaitFor(peer);
    await using (var app = failing.Build())
    {
        var error = await Smoke.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());
        Smoke.Require(error.Message.Contains("exited with code 17", StringComparison.Ordinal)
            && error.Message.Contains("orchestration smoke startup failure", StringComparison.Ordinal),
            "Startup failure lost the exit code or child's stderr: " + error.Message);
        Smoke.Require(app.GetOutput("broken").Any(line => line.Contains("orchestration smoke startup failure", StringComparison.Ordinal)),
            "The output snapshot lost the failed child's stderr.");
        Smoke.RequireStopped(failedPeer);
    }
    Console.WriteLine("PASS: early exit diagnostics, output snapshot and peer cleanup");

    var cancelledPid = Path.Combine(scratch, "cancelled.pid");
    var cancelled = new NamiApplicationBuilder { StartupTimeoutSeconds = 10, MaxRestarts = 0 };
    var waiting = Smoke.Worker(cancelled, "waiting", executable, "unhealthy").WithEnvironment("ORCHESTRATION_SMOKE_PID", cancelledPid);
    var deferredPid = Path.Combine(scratch, "deferred.pid");
    Smoke.Worker(cancelled, "deferred", executable, "ready").WithEnvironment("ORCHESTRATION_SMOKE_PID", deferredPid).WaitFor(waiting);
    await using (var app = cancelled.Build())
    {
        using var cancellation = new CancellationTokenSource();
        var start = app.StartAsync(cancellation.Token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(cancelledPid) || !int.TryParse(await File.ReadAllTextAsync(cancelledPid, deadline.Token), out _))
            await Task.Delay(20, deadline.Token);
        await cancellation.CancelAsync();
        await Smoke.ThrowsAsync<OperationCanceledException>(() => start);
        Smoke.RequireStopped(cancelledPid);
        Smoke.Require(!File.Exists(deferredPid), "A dependency launched before its prerequisite was ready.");
    }
    Console.WriteLine("PASS: cancellation stops children and prevents deferred startup");

    var timedOutPid = Path.Combine(scratch, "timeout.pid");
    var timedOut = new NamiApplicationBuilder { StartupTimeoutSeconds = 3, MaxRestarts = 0 };
    Smoke.Worker(timedOut, "unhealthy", executable, "unhealthy").WithEnvironment("ORCHESTRATION_SMOKE_PID", timedOutPid);
    await using (var app = timedOut.Build())
    {
        await Smoke.ThrowsAsync<TimeoutException>(() => app.StartAsync());
        Smoke.RequireStopped(timedOutPid);
    }
    Console.WriteLine("PASS: readiness deadline and cleanup");

    var cycle = new NamiApplicationBuilder();
    var first = cycle.AddExecutable("first", "unused", []);
    var second = cycle.AddExecutable("second", "unused", []).WaitFor(first);
    first.WaitFor(second);
    var cycleError = await Smoke.ThrowsAsync<InvalidOperationException>(() => Task.Run(cycle.Build));
    Smoke.Require(cycleError.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase), "Cycle was not rejected.");
    var other = new NamiApplicationBuilder().AddExecutable("other", "unused", []);
    await Smoke.ThrowsAsync<ArgumentException>(() => Task.Run(() => first.WithReference(other)));
    Console.WriteLine("PASS: dependency cycle and cross-application reference validation");
    Console.WriteLine($"VERIFIED Nami.Orchestration {typeof(NamiApplication).Assembly.GetName().Version}");
    return 0;
}
finally
{
    Directory.Delete(scratch, recursive: true);
}

internal static class Smoke
{
    internal static NamiProcessBuilder Worker(NamiApplicationBuilder builder, string name, string executable, string mode) =>
        builder.AddExecutable(name, executable,
                Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                    ? [Environment.GetCommandLineArgs()[0]] : [])
            .WithEnvironment("ORCHESTRATION_SMOKE_WORKER", mode)
            .WithHealthCheck(requireProcessId: true);

    internal static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    internal static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    internal static void RequireStopped(string pidFile)
    {
        Require(File.Exists(pidFile), "The child never wrote its process ID.");
        var id = int.Parse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture);
        try
        {
            using var process = Process.GetProcessById(id);
            Require(process.HasExited, $"Child {id} is still alive.");
        }
        catch (ArgumentException)
        {
            // The operating system has already removed the exited process.
        }
    }
}

internal sealed class ShutdownMarker(IConfiguration configuration, IHttpClientFactory clients) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var path = Environment.GetEnvironmentVariable("ORCHESTRATION_SMOKE_STOPPED");
        var destination = Environment.GetEnvironmentVariable("ORCHESTRATION_SMOKE_FLUSH_TO");
        if (path is null && destination is null) return;
        await Task.Delay(100, cancellationToken);
        if (destination is not null)
        {
            using var client = clients.CreateClient();
            var endpoint = configuration[$"services:{destination}:http:0"]
                ?? throw new InvalidOperationException("Missing shutdown export destination.");
            using var response = await client.PostAsync(new Uri(new Uri(endpoint), "flush"), null, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        if (path is not null) await File.WriteAllTextAsync(path, "flushed", cancellationToken);
    }
}
