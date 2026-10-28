# TR_SERVER_20261028 (Windows / R186632)

TalesRunner server emulator source for Windows, targeting the Thai R186632 client. This folder is a Windows-only source snapshot with changes through **28 October 2026**. The date in its name identifies this snapshot; it is not a claim of a published binary release or that every feature has been verified on Windows.

## Credit and origin

This project is based on **[arstacth/TR_SERVER_20260922](https://github.com/arstacth/TR_SERVER_20260922)**. Credit for the original repository and its foundation belongs to its author and contributors. This snapshot adapts that source for the R186632 client and includes subsequent server fixes. See the original repository for its own history and release files.

## Features

- Separate load balance, agent, room, relay, and community services
- MariaDB game and log databases
- Configurable network addresses, ports, limits, and client hash validation
- R186632 protocol and game-flow fixes incorporated through 28 October 2026

## Requirements

| Component | Requirement |
| --- | --- |
| OS | Windows 10/11 or Windows Server, 64-bit recommended |
| Database | MariaDB 10.4.28 with `utf8mb4` |
| Runtime | .NET Framework 4.8 |
| Build | Visual Studio or Build Tools with MSBuild and the .NET Framework 4.8 targeting pack |
| Client | Compatible TalesRunner R186632 client |

## Build from source

From this repository root, open `TR_SERVER.sln` in Visual Studio and select **Release / Any CPU**, or run this in a Developer Command Prompt with MSBuild on `PATH`:

```bat
MSBuild.exe TR_SERVER.sln /t:Build /p:Configuration=Release /p:Platform="Any CPU" /p:TargetFrameworkVersion=v4.8 /m:1
```

The executables are written to `bin\`; dependencies may be in `bin\bin\`. The `packages\` folder supplies DLL references for compilation. `BUILD.bat` performs a clean rebuild but **stops all running Windows server processes**, so use the direct MSBuild command above when you only want to build.

The solution contains `AgentServer`, `CommunityAgentServer`, `LoadBalanceServer`, `LocalCommons`, `RelayServer`, `RoomServer`, and `TRServerSetup`.

## Database and address setup: choose one method

The static starter data is compressed as `database\tr_game_db_starter_20261012.zip` to keep the repository file below GitHub's 100 MiB limit. Extract it before importing:

```powershell
Expand-Archive -LiteralPath .\database\tr_game_db_starter_20261012.zip -DestinationPath .\database
```

### Option 1: TRServerSetup (graphical setup)

Use this for a **new installation** after preparing the runtime folder described below. Copy the built `TRServerSetup.exe` and its `.exe.config` into that folder. The runtime folder must already contain `settings.ini`; the tool edits it and saves a backup. Open `TRServerSetup.exe`, then:

1. Choose the runtime folder that contains `AgentServer.exe` and `settings.ini`.
2. Choose the extracted `database\tr_game_db_starter_20261012.sql` as **Game database**, `database\tr_game_db_log_utf8mb4.sql` as **Log database**, and your MariaDB installation's `mysql.exe`.
3. Enter the MariaDB host, port, username and password, plus the IP address clients will connect to. For a local test, use `127.0.0.1`.
4. Test the connection, then click **Install**. The tool creates the databases if needed, imports the SQL, updates stored server addresses, and writes `settings.ini` with a backup.

Leave **Drop existing databases** unchecked unless you deliberately want to delete all current player data. The setup imports the starter data even when that box is unchecked, so do not rerun it against an existing populated game database. The tool does not start the server or apply the Rank migration; apply the migration command below, then follow **Configure and run**.

### Option 2: manual SQL import and settings

Create the databases and import the starter data and log schema yourself:

```bat
mysql -u root -p < database\make_db.sql
mysql -u root -p tr_game_db < database\tr_game_db_starter_20261012.sql
mysql -u root -p tr_game_db_log < database\tr_game_db_log_utf8mb4.sql
```

Then set your MariaDB connection and server IPs in the runtime folder's `settings.ini`. For a remote client, advertise the reachable public IP using `AgentServerIP` and `RelayAdvertiseIP`. For a local test, use `127.0.0.1`.

### Required after either method

Apply the Rank migration once:

```bat
mysql -u root -p tr_game_db < database\migrations\20261014_rank_daily_only.sql
```

The Rank migration moves leaderboard rebuilding from login to the daily `EssenMakeRankDaily` event. Enable `event_scheduler=ON` in MariaDB's server configuration so scheduled updates continue after a restart.

The included starter SQL contains game structure and seed data, without player accounts. Do not commit credentials or a live database export.

### Create a player account (MD5 password)

For this emulator, store the password as a **32-character MD5 hex string** in `userinfofrompublisher.fdPassword`. The login handler accepts the player's entered password and checks its MD5 value against the database. Connect with `mysql -u root -p tr_game_db`, then replace the example ID and password below with your own values:

```sql
-- The starter database enables TestDB, which can create an account on first
-- login using the password sent by the client. Turn it off for manual MD5 accounts.
UPDATE tblServerSettingInfo SET fdValue = 'false' WHERE fdKey = 'TestDB';

INSERT INTO userinfofrompublisher (fdUserID, fdGameID, fdPassword)
VALUES ('newplayer', 'newplayer', MD5('change-this-password'));

-- Verify the account exists without displaying its password hash.
SELECT fdUserID, fdGameID, CHAR_LENGTH(fdPassword) AS hash_length
FROM userinfofrompublisher WHERE fdUserID = 'newplayer';
```

`hash_length` should be `32`. `fdCash` uses the table default when omitted. The player enters the **original password** in the game, not the MD5 string. The game creates the remaining player profile data during login. Do not put a real password in this README or commit a SQL file containing one.

## Configure and run

Use a **separate runtime folder** outside this repository. Start with a complete runtime bundle from the [original release](https://github.com/arstacth/TR_SERVER_20260922/releases) or your existing Windows installation, then copy the newly built EXEs, EXE config files, and DLLs from this project's `bin\` over the corresponding runtime files. Preserve the complete nested `bin\` dependency directory from the runtime bundle: compilation succeeds with the `packages\` references, but startup also needs DLLs such as `System.Runtime.InteropServices.RuntimeInformation.dll` from that directory. Add your local settings and assets:

```text
runtime/
  AgentServer.exe
  CommunityAgentServer.exe
  LoadBalanceServer.exe
  RelayServer.exe
  RoomServer.exe
  TRServerSetup.exe       optional setup tool
  *.exe.config
  *.dll
  settings.ini
  hash.ini
  bin/TRServer.dll
  bin/                  other runtime DLLs from the bundle
  iteminfo/
```

The server programs must start with `runtime\` as their working directory. `AgentServer.exe` checks for `settings.ini`, `hash.ini`, and `bin\TRServer.dll` at startup. Preserve any additional assets from the runtime bundle you use. This repository contains source, not a ready-to-run bundle. Do not commit your runtime folder or credentials.

After completing either setup method above, open **Command Prompt**, change to the runtime folder, and start the programs in this order:

```bat
cd /d C:\TR_SERVER_RUNTIME
start "LoadBalanceServer" LoadBalanceServer.exe
start "RoomServer" RoomServer.exe
start "CommunityAgentServer" CommunityAgentServer.exe
start "RelayServer" RelayServer.exe
timeout /t 1 /nobreak >nul
start "AgentServer" AgentServer.exe
```

Keep the LoadBalanceServer console open; it waits on standard input and may exit if started as a detached background process without a console. If your runtime bundle has `START_SERVER.bat`, you can use that instead. Check `runtime\Logs\` and confirm the Agent log reports `Set Server Ready : true`. To stop the server, close the five program windows or use the runtime bundle's `STOP_SERVER.bat`. Test login and gameplay with a compatible R186632 client before using the build publicly.

### Create your own game-launch batch file

After the server is ready, create a new text file such as `StartGame.bat` **on your own computer**. Set `CLIENT_DIR` to the folder containing your compatible R186632 `trgame.exe`, and set `SERVER_IP` to the server address. Use `127.0.0.1` when the client and server run on the same machine, or your reachable public IP for a remote server. Save the file as `.bat` and double-click it:

```bat
@echo off
set "CLIENT_DIR=C:\TalesRunner"
set "SERVER_IP=127.0.0.1"
cd /d "%CLIENT_DIR%" || exit /b 1
trgame.exe -p:2 -zone:%SERVER_IP% -zoneID: -zoneName:TH -- TH
exit /b 0
```

This follows the launch arguments in the existing `!เริ่มเกมชั่วคราว.bat` example. Match `SERVER_IP` to the address configured for clients in `settings.ini`; this launch file starts the **client**, not the five server processes. Do not put account credentials in it.

| Default port | Protocol | Service |
| --- | --- | --- |
| 9500 | TCP | Load balance, client-facing |
| 9153 | TCP | Agent |
| 9499 | TCP | Agent secondary |
| 9000 | TCP | Community agent |
| 9155 | UDP | Relay |

Other local service ports, including 9152 and 9600, should remain private unless your network layout requires them. Check actual values in `settings.ini` before configuring a firewall.

## Repository layout

```text
TR_SERVER.sln          Windows solution
BUILD.bat              Clean rebuild script (stops running services)
AgentServer/           Main game logic and sessions
CommunityAgentServer/  Community service
LoadBalanceServer/     Client entry
LocalCommons/          Shared code
RelayServer/           UDP relay
RoomServer/            Rooms and match flow
TRServerSetup/         Setup project
packages/              Referenced DLLs
database/              MariaDB schema, starter ZIP, migration
tools/                 Build helper
```

## Status

This is a source snapshot. The Windows build was started locally on 28 October 2026 and AgentServer reported `Set Server Ready : true`. A new account's first nickname request could remain locked when an invalid short name received ACK 958; the server now replies with the matching ACK 1370. After a fresh login, the user confirmed nickname creation, the packet log showed request 1383 followed by success ACK 1370, and MariaDB stored the new nickname. Other Windows gameplay still needs testing before treating this as a production release.

## Troubleshooting

- **Missing targeting pack:** install the .NET Framework 4.8 Developer Pack.
- **Build cannot replace an EXE:** stop the relevant server process before deploying to a live runtime folder.
- **Cannot connect to MariaDB:** check service status, database import, connection string, and credentials.
- **Rank does not refresh daily:** verify MariaDB's `event_scheduler` is enabled.
