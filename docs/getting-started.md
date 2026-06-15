# Getting Started

## Prerequisites

- .NET 6.0 or later
- An ASP.NET Core application with Swagger/OpenAPI enabled

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

And add the middleware:

```csharp
app.UseMcp();
```

## Verifying Installation

1. Start your application
2. Send a POST to `/mcp`:

```json
{ "jsonrpc": "2.0", "id": 1, "method": "initialize" }
```

3. You should receive the MCP server capabilities response.

## Next Steps

- Add `[AiTool]` to endpoints you want LLMs to execute
- Add `[AiHidden]` to internal endpoints
- Add `[AiDescription]` for better AI understanding
- See the [architecture](./architecture.md) guide for details
