# Getting Started

## Prerequisites

- .NET 6.0 to .NET 10.0
- An ASP.NET Core application (an OpenAPI generator is optional)

## Installation

```bash
dotnet add package Romatech.Extensions.Ai
```

## Basic Setup

Add two lines to your `Program.cs`:

```csharp
builder.Services.UseMcp();
builder.Services.UseRag();
```

And map the endpoint:

```csharp
app.MapMcp(); // or app.UseMcp()
```

The MCP endpoint requires an authenticated caller by default: the MCP client sends the same cookie or bearer token
your API already accepts, and every tool runs as that user.

## Verifying Installation

1. Start your application
2. Send an authenticated POST to `/mcp` with `Content-Type: application/json`:

```json
{ "jsonrpc": "2.0", "id": 1, "method": "initialize", "params": { "protocolVersion": "2025-06-18" } }
```

3. You should receive the MCP server capabilities response.

## Next Steps

- Add `[AiTool]` to endpoints you want LLMs to execute
- Add `[AiHidden]` to internal endpoints
- Add `[AiDescription]` for better AI understanding
- See the [architecture](./architecture.md) guide for details
