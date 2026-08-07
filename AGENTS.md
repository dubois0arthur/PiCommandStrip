# PiCommandStrip contributor instructions

## Product direction

PiCommandStrip is a context-aware command strip for a 7-inch, 1024x600 landscape touchscreen. A Windows PC provides foreground-application context and executes a deliberately small set of safe actions. The dashboard will eventually run on a Raspberry Pi 4B, but the first milestone runs entirely on the Windows PC in a browser.

## Engineering constraints

- Use C# and ASP.NET Core.
- Target the highest stable, non-preview .NET SDK installed on the development machine. Pin the selected SDK in `global.json` when application scaffolding begins.
- For the first milestone, use native WebSockets rather than SignalR.
- Build the frontend with plain HTML, CSS, and JavaScript. Do not add Node.js, npm, a frontend framework, a build pipeline, third-party CDNs, or remotely hosted frontend assets.
- Keep the UI usable at 1024x600 in landscape orientation. Prefer large touch targets, readable status, and layouts that do not depend on hover.
- Prefer the .NET and browser standard libraries. Before adding any external package, explain the need and why built-in functionality is insufficient.
- Keep the architecture small. Do not add Docker, a database, MQTT, TLS, plugins, or microservices unless the project scope is explicitly changed. Pre-shared-token WebSocket authentication is now part of the explicitly enabled LAN mode.
- Keep all project files in this repository.

## Safety boundary

- Never accept a shell command, executable path, script, command-line argument list, or arbitrary code from the dashboard.
- Represent every PC action with a fixed command identifier defined by the server.
- Resolve identifiers through an explicit server-side allowlist. Unknown identifiers must be rejected without execution.
- Validate every inbound WebSocket message, limit message sizes, and handle malformed or unsupported messages without terminating the host.
- Keep command execution narrow and auditable. Do not introduce a generic process-launching command.
- Log command identifiers, outcomes, and operational errors, but do not log secrets or unnecessary user content.
- Keep the pre-shared token outside Git and frontend source. Development remains loopback-only; LAN binding requires the explicit `Lan` configuration and a concrete PC address.
- Treat the current HTTP LAN mode as unencrypted and suitable only for a trusted Private network. Never suggest exposing its port to the internet.

## Architecture and code quality

- Use dependency injection and small interfaces where they create a useful test seam, especially around foreground-window detection, command handling, and time-dependent/background behavior.
- Isolate Windows-specific interop behind an interface so the web and protocol layers do not depend directly on Win32 APIs.
- Use cancellation tokens for polling, WebSocket operations, and command execution. Background services must stop cleanly during host shutdown.
- Keep WebSocket message contracts explicit, versionable, and direction-specific. Prefer small JSON messages with a `type` discriminator.
- Keep network transport, application coordination, Windows integration, and UI concerns separate without adding unnecessary projects or abstraction layers.
- Treat the browser as untrusted input even during local-only development.
- Add focused automated tests for protocol parsing, allowlist rejection, command dispatch, and other logic that does not require a real desktop session.

## Working style and learning goals

- Before each implementation phase, briefly explain what will be built, why it is needed, and any important tradeoffs.
- After changes, list the important files and explain their responsibilities.
- Explain unfamiliar C#, ASP.NET Core, Windows interop, and networking concepts in plain language.
- Show the exact commands used to build, test, and run the project.
- Run relevant build and tests after implementation changes and report the results accurately.
- Update documentation when architecture, message contracts, commands, or setup steps change.
- Do not create Git commits; the repository owner commits with GitHub Desktop.
