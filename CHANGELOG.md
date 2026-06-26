# Changelog

All notable changes to Romatech.Extensions.Ai will be documented in this file.

## [Unreleased]

### Changed
- Standardized `index.html` landing page (GitHub Pages) with new template: header, hero, quick start, features grid, install command, footer

## [2.0.0] - 2026-06-26

### Added
- MCP: `ping`, `notifications/initialized`, `completions/complete`, `prompts/list`, `prompts/get`
- Built-in `rag_search` MCP tool (automatic when RAG is enabled)
- `AttachRagSearch()` wires RAG into MCP handler automatically
- `AiMetadataOperationFilter` injects `x-ai-*` extensions into Swagger spec
- `SwaggerEndpointDiscoveryProvider` reads `x-ai-*` for exposure levels
- `BaseUrl` configuration for internal HTTP calls
- McpMiddleware initializes tool registry on first request

### Fixed
- tools/list was returning empty because discovery always set ReadOnly
- McpToolRegistry.InitializeAsync() was never called
