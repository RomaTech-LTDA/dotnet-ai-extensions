using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Romatech.Extensions.Ai.Extensions;
using Romatech.Extensions.Ai.Mcp.Configuration;
using Romatech.Extensions.Ai.Metadata.Attributes;
using Romatech.Extensions.Ai.Metadata.Extensions;

namespace Romatech.Extensions.Ai.Tests.Integration;

/// <summary>
/// A small back-office API with cookie authentication, Minimal APIs and MVC controllers,
/// mirroring the application where the 3.0.0 failures were found.
/// </summary>
public static class CrmApp
{
    public const string LeadsSecret = "LEADS-SECRET-7f3a";
    public const string ExceptionSecret = "db password=hunter2";

    public static void ConfigureServices(WebApplicationBuilder builder, Action<McpOptions>? configureMcp = null, Action<AuthorizationOptions>? configureAuthorization = null, bool withRag = true)
    {
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddAuthorization(o => configureAuthorization?.Invoke(o));
        builder.Services.AddControllers().AddApplicationPart(typeof(CrmApp).Assembly);
        builder.Services.AddSingleton<DisposalProbe>();
        builder.Services.UseMcp(o => configureMcp?.Invoke(o));
        if (withRag)
            builder.Services.UseRag();
    }

    public static void Configure(WebApplication app, bool mapMcp = false)
    {
        // UseMcp() deliberately sits BEFORE UseAuthentication(): the MCP endpoint must still see the caller's cookie.
        if (!mapMcp)
            app.UseMcp();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/login", async (HttpContext ctx) =>
        {
            var user = ctx.Request.Query["user"].ToString();
            var claims = new List<Claim> { new(ClaimTypes.Name, user), new(ClaimTypes.NameIdentifier, "id-" + user) };
            claims.AddRange(ctx.Request.Query["role"].Select(r => new Claim(ClaimTypes.Role, r!)));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return Results.Ok();
        }).AllowAnonymous().AiHidden();

        app.MapGet("/api/leads", () => Results.Ok(new { secret = LeadsSecret }))
            .RequireAuthorization().AiHidden();

        app.MapGet("/api/customers/{id}", (string id, ClaimsPrincipal user, HttpContext ctx) => Results.Ok(new
        {
            id,
            requestedBy = user.Identity?.Name,
            host = ctx.Request.Host.Value,
            tenant = ctx.Request.Headers["X-Tenant"].ToString(),
            path = ctx.Request.Path.Value,
        }))
            .RequireAuthorization().AiTool("get_customer").AiDescription("Gets a customer by id").AiCategory("Customers");

        app.MapGet("/api/customers/{id:int}/orders", (int id) => Results.Ok(new[] { new { orderId = id * 10 } }))
            .RequireAuthorization().AiTool("customer_orders");

        app.MapPost("/api/customers", (CreateCustomer body, ClaimsPrincipal user) =>
                Results.Created($"/api/customers/{body.Name}", new { body.Name, body.Email, createdBy = user.Identity?.Name }))
            .RequireAuthorization().AiTool("create_customer");

        app.MapDelete("/api/customers/{id}", (string id) => Results.NoContent())
            .RequireAuthorization().AiTool("delete_customer");

        app.MapGet("/api/admin/report", () => Results.Ok(new { report = "admin-only" }))
            .RequireAuthorization(new AuthorizeAttribute { Roles = "admin" }).AiTool("admin_report");

        app.MapGet("/api/finance", () => Results.Ok(new { total = 42 }))
            .RequireAuthorization().AiTool("finance_summary").AiRole("finance");

        app.MapGet("/api/boom", IResult () => throw new InvalidOperationException(ExceptionSecret))
            .RequireAuthorization().AiTool("boom");

        app.MapGet("/api/missing", () => Results.NotFound(new { message = "customer not found" }))
            .RequireAuthorization().AiTool("missing");

        app.MapGet("/api/search", (string q, int? limit) => Results.Ok(new { q, limit }))
            .RequireAuthorization().AiTool("search");

        app.MapGet("/api/files/{*path}", (string path) => Results.Ok(new { path }))
            .RequireAuthorization().AiTool("read_file");

        app.MapGet("/api/region", ([FromHeader(Name = "X-Region")] string region) => Results.Ok(new { region }))
            .RequireAuthorization().AiTool("region");

        app.MapGet("/api/limited", () => Results.Ok("ok"))
            .RequireAuthorization().AiTool("limited").AiRateLimit(2);

        app.MapGet("/api/big", () => Results.Text(new string('x', 4096)))
            .RequireAuthorization().AiTool("big");

        app.MapGet("/api/slow", async (CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return Results.Ok();
            })
            .RequireAuthorization().AiTool("slow");

        app.MapGet("/api/open", () => Results.Ok(new { open = true }))
            .AiTool("open_tool");

        app.MapGet("/api/catalog", () => Results.Ok(new[] { "widget" }))
            .AiDescription("Lists the public catalog");

        app.MapGet("/api/accessor/{id}", (string id, IHttpContextAccessor accessor) =>
            {
                var ctx = accessor.HttpContext!;
                return Results.Ok(new
                {
                    id,
                    accessorPath = ctx.Request.Path.Value,
                    accessorRouteId = ctx.Request.RouteValues["id"]?.ToString(),
                });
            })
            .RequireAuthorization().AiTool("accessor");

        app.MapGet("/api/disposable", (HttpContext ctx, DisposalProbe probe) =>
            {
                ctx.Response.RegisterForDispose(probe.Create());
                ctx.Response.OnStarting(() =>
                {
                    ctx.Response.Headers["X-Started"] = "1";
                    probe.Started();
                    return Task.CompletedTask;
                });
                return Results.Ok("ok");
            })
            .RequireAuthorization().AiTool("disposable");

        app.MapGet("/api/stubborn", async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3)); // ignores RequestAborted on purpose
                return Results.Ok("late");
            })
            .RequireAuthorization().AiTool("stubborn");

        app.MapGet("/api/other-host", () => Results.Ok("other"))
            .RequireHost("other.example").AiTool("other_host");

        app.MapGet("/api/download/{name}.{ext?}", (string name, string? ext, HttpContext ctx) => Results.Ok(new { name, ext, path = ctx.Request.Path.Value }))
            .RequireAuthorization().AiTool("download");

        app.MapControllers();

        if (mapMcp)
            app.MapMcp();
    }
}

public sealed class DisposalProbe
{
    private int _disposed;
    private int _started;

    public int DisposedCount => Volatile.Read(ref _disposed);

    public int StartedCount => Volatile.Read(ref _started);

    public IDisposable Create() => new Tracker(this);

    public void Started() => Interlocked.Increment(ref _started);

    private sealed class Tracker : IDisposable
    {
        private readonly DisposalProbe _owner;

        public Tracker(DisposalProbe owner) => _owner = owner;

        public void Dispose() => Interlocked.Increment(ref _owner._disposed);
    }
}

public sealed record CreateCustomer([Required] string Name, string? Email);

public sealed record CreateOrderRequest([Required] string ProductId, int Quantity);

[ApiController]
[Route("api/orders")]
[Authorize]
[AiCategory("Orders")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet]
    [AiDescription("Lists orders")]
    public IActionResult List() => Ok(Array.Empty<object>());

    [HttpGet("{id:int}")]
    [AiTool("get_order")]
    public IActionResult Get(int id) => Ok(new { id, user = User.Identity?.Name });

    [HttpPost]
    [AiTool("create_order")]
    public IActionResult Create([FromBody] CreateOrderRequest request)
        => Ok(new { request.ProductId, request.Quantity, user = User.Identity?.Name });

    [HttpDelete("{id}")]
    [AiHidden]
    public IActionResult Delete(int id) => NoContent();
}
