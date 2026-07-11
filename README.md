# Navislamia
---
[![Build Status](https://github.com/iSmokeDrow/Navislamia/actions/workflows/build.yml/badge.svg?branch=Development)](https://github.com/iSmokeDrow/Navislamia/actions/workflows/build.yml)
[![OpenRZ Discord](https://badgen.net/discord/members/UQz9uydsFY)](https://discord.gg/tETH6zHrQ5)

[![Recent Commits](https://img.shields.io/github/commit-activity/m/iSmokeDrow/Navislamia?label=Commits&style=flat-square)]()
[![Open Pull Requests](https://img.shields.io/github/issues-pr-raw/ismokedrow/navislamia?label=Open%20Pull%20Requests&style=flat-square)]()
[![Open Issues](https://img.shields.io/github/issues-raw/ismokedrow/navislamia?color=red&label=Open%20Issues&style=flat-square)]()
[![Closed Issues](https://img.shields.io/github/issues-closed-raw/ismokedrow/navislamia?color=Green&label=Closed%20Issues&style=flat-square)]()

## Foreword

Navislamia is written and maintained by volunteers for fun. This project does not now, nor will it ever seek monetary reimbursement or gains. It is not our intent to defame or otherwise denigrate the hard work done by developers of the `Arcadia Framework`. More so it is our intent to pay homage to the game all the volunteers and players involved have sunk countless hours and years of their lives into.

## **What is Navislamia?** 

Navislamia is an free, open source, cross platform reimplementation of the Rappelz Game Server.

## Key Features

- Written in `.NET`
    - Entity Framework for Database Interactions
    - Moonsharp (lua 5.2) for Scripting
    - Serilog for Logging

## Goals

- Accept and process connections from Unmodified Epic 7.3 Game Clients
- Complete emulation of all actions/processes provided by the original Rappelz Game Server

### Current State

- Game Client login up to `Character List`
- Character Creation

## Architecture

Navislamia implements only the **Game Server**. In the Rappelz / Arcadia architecture the
login flow is split across separate servers:

| Server               | Port | Provided by Navislamia?              |
|----------------------|------|--------------------------------------|
| Auth Server (login)  | 4502 | Stub included (`AuthServer/`)        |
| Upload Server (data) | 4616 | Stub included (`AuthServer/`)        |
| Game Server          | 4515 | Yes — `DevConsole/` hosts `Game/`    |

At startup the Game Server connects to the Auth and Upload servers **as a client** and
registers itself; it will not start if they are unreachable. The `AuthServer` project is a
minimal stub that answers this registration handshake so the Game Server can boot. Full
Epic 7.3 client login (RSA key exchange, XRC4 cipher, account validation, server selection,
one-time key) is **not yet implemented** — design notes live under `docs/superpowers/`.

## Building & Running

### Prerequisites

- .NET SDK 8.0 (x64)
- PostgreSQL with the `arcadia` database — connection settings in `DevConsole/appsettings.json`

### Run the whole server (Windows)

From the repository root:

```powershell
.\start-server.ps1
```

This launches the stub `AuthServer` (ports 4502 / 4616), waits until it is actually
listening, then starts the Game Server (`DevConsole`, port 4515). When `DevConsole` exits,
the stub is stopped automatically. Pass `-Configuration Release` for a release build.

Success looks like this in the DevConsole output:

```
[... DBG AuthActions] Successfully registered to the Auth Server!
[... DBG UploadActions] Successfully registered to the Upload Server!
[... INF GameModule] Listening for clients on 127.0.0.1:4515
```

### Run manually

```powershell
# Terminal 1 — stub auth/upload servers (start this first)
dotnet run --project AuthServer

# Terminal 2 — game server (only once AuthServer is listening)
cd DevConsole
dotnet run
```

## Community

### How can I help?

A project of this scope requires varying talent to progress to completion. That being said the project is currently seeking:

- Git maintainer (help w/ readme and wiki documentation)
- Developers proficient in:
    - .NET, ASP.NET Web Development, MSSQL, PostgreSQL
    - LUA 5.2 Scripting
    - Community Management

If you feel like you or someone you know would like to contribute to this project, please click the discord link above and introduce yourself in the lobby.


