using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace Nami.Tests;

/// <summary>The lifecycle of a Kestrel host that a receiver or relay owns: quiet build, owned start, stop and dispose.</summary>
internal static class LoopbackHost
{
    /// <summary>A slim builder without logging providers; the owner reports its own evidence.</summary>
    internal static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateSlimBuilder();
        _ = builder.Logging.ClearProviders();
        return builder;
    }

    extension(WebApplication application)
    {
        /// <summary>Starts the host and creates its owner; if either fails, the host is disposed before the error propagates.</summary>
        internal async Task<TOwner> StartOwnedAsync<TOwner>(Func<TOwner> owner, CancellationToken cancellationToken)
        {
            try
            {
                await application.StartAsync(cancellationToken).ConfigureAwait(false);
                return owner();
            }
            catch
            {
                await application.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Stops the host within <paramref name="cancellationToken" />, then disposes it even when stopping fails.</summary>
        internal async ValueTask StopAndDisposeAsync(CancellationToken cancellationToken)
        {
            try
            {
                await application.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await application.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
