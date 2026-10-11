using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace KappuCitti.Plugin.SyncVote.Infrastructure;

/// <summary>
/// Puts <see cref="WebClientInjectionMiddleware"/> at the start of the request pipeline.
/// </summary>
public class WebClientStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<WebClientInjectionMiddleware>();
            next(app);
        };
    }
}
