# PiCommandStrip vision

## Product vision

PiCommandStrip is a dedicated, context-aware touchscreen command strip that sits beneath a PC monitor. Its eventual display device is a Raspberry Pi 4B with a 7-inch, 1024x600 landscape touchscreen. The strip observes which application is active on a Windows PC, presents relevant information and controls, and sends deliberately constrained actions back to the PC over the local network.

The product should feel like a small appliance rather than a second general-purpose desktop: glanceable, responsive, easy to touch, and safe to leave connected.

## First milestone

The first milestone proves the complete communication loop on one Windows PC. The dashboard runs in a local browser; the Raspberry Pi is intentionally not involved yet.

The milestone must demonstrate that the system can:

1. Detect the foreground Windows application.
2. Broadcast foreground state to a connected dashboard through a native WebSocket.
3. Display the current process name and window title.
4. Receive a dashboard request containing a fixed command identifier.
5. Execute the request only when that identifier is present in an explicit server-side allowlist.
6. Return and display a structured command result.

This vertical slice reduces the main technical risks early: Windows foreground-window integration, bidirectional WebSocket communication, browser state updates, safe command dispatch, and graceful lifecycle management.

## Intended first-milestone experience

The user starts one ASP.NET Core application and opens its local dashboard URL in a browser. The dashboard shows connection status plus the foreground process and window title. As the user switches Windows applications, the display updates without a page refresh. Pressing a large touch-friendly command button sends a known command identifier; the dashboard then shows whether the command was accepted and its result.

The initial safe command should have a harmless, deterministic effect suitable for demonstrating the round trip. Its exact behavior will be chosen during implementation and documented alongside the allowlist.

## Product principles

- **Safe by construction:** the dashboard selects from server-defined command identifiers; it never supplies code, a shell command, an executable path, or a script.
- **Small first:** use one host process and built-in platform features until real requirements justify more infrastructure.
- **Local and self-contained:** ship frontend assets with the application and avoid runtime dependencies on CDNs or hosted assets.
- **Touch first:** design for 1024x600 landscape, large hit areas, readable status, and no hover-only interactions.
- **Observable:** useful logs and visible connection/command status should make failures understandable.
- **Evolvable:** isolate Windows-specific behavior and define explicit messages so a Raspberry Pi browser can be introduced later without rewriting the core.
- **Teachable:** document not just what is built, but why key C#, networking, and security decisions were made.

## Out of scope for the first milestone

- Running or provisioning software on the Raspberry Pi
- Remote access outside the trusted local development setup
- Authentication, authorization accounts, or user management
- TLS certificate setup
- Docker or other containerization
- A database or persistent command history
- MQTT, SignalR, or another messaging layer
- Plugins, user-defined commands, macros, or scripts
- A frontend framework, Node.js, npm, or an asset build pipeline
- Automatic per-application command layouts beyond proving current-context delivery
- Production deployment, service installation, or automatic startup

These are deferred, not permanently rejected. They should be introduced only when a later milestone supplies a concrete need and an appropriate security design.

## First-milestone success criteria

The milestone is complete when, on the Windows development PC:

- the pinned stable .NET SDK can restore, build, test, and run the solution;
- opening the dashboard establishes a WebSocket connection and visibly reports its state;
- switching foreground applications updates the displayed process and title promptly;
- a documented allowlisted button produces a visible success result;
- an unknown or malformed command request is rejected and performs no PC action;
- disconnects, malformed messages, monitoring errors, and shutdown are handled without crashing or hanging the host;
- no arbitrary execution path exists from browser input; and
- the dashboard remains usable at a 1024x600 viewport.

## Later direction

After the Windows-only loop is reliable, the same browser dashboard can be opened on the Raspberry Pi and pointed at the Windows host over the LAN. That step will require explicit decisions about network binding, device trust, authentication, and TLS. Later milestones can then add application-specific command sets and a refined touchscreen experience without weakening the fixed-command safety boundary.

