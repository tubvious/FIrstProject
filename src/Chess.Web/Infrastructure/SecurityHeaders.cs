namespace Chess.Web.Infrastructure;

public static class SecurityHeaders
{
    /// <summary>
    /// Adds conservative browser security headers. The content security policy only allows same-origin
    /// scripts, styles and connections (including the SignalR WebSocket); it is skipped in Development
    /// so tooling such as Visual Studio's browser refresh can inject its scripts.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IWebHostEnvironment environment) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            if (!environment.IsDevelopment())
            {
                var host = context.Request.Host.Value;
                headers.ContentSecurityPolicy =
                    "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
                    $"connect-src 'self' wss://{host} ws://{host}; object-src 'none'; base-uri 'self'; " +
                    "form-action 'self'; frame-ancestors 'none'";
            }

            await next(context);
        });
}
