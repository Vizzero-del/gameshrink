# GameShrink (WPF, .NET 8)

GameShrink is a Windows 10/11 desktop utility that **reduces disk space used by an installed game** by toggling **built-in NTFS file compression**, primarily via Windows **`compact.exe`**.

Key points:

- It **does not repack** game archives (`.pak`, `.bundle`, …)
- It **does not patch** binaries/content
- It **does not interfere** with DRM/anti-cheat
- All actions are **reversible** (`compact /U ...`)

## Build / Run

### Prerequisites

- Windows 10/11
- **.NET 8 SDK** installed (because this repository is built with the `dotnet` CLI)
  - Download: https://dotnet.microsoft.com/download/dotnet/8.0

### Commands

From the repository root:

```bat
cd GameShrink

dotnet build GameShrink.sln -c Release

dotnet run --project .\GameShrink.App\GameShrink.App.csproj
```

## How it works (reversible)

- **Safe (NTFS/standard)** uses:
  - `compact /C /S:"{dir}" /I /F /Q`
- **Stronger (LZX)** uses:
  - `compact /C /S:"{dir}" /I /F /Q /EXE:LZX`
- **Rollback (return as it was)** uses:
  - `compact /U /S:"{dir}" /I /F /Q`

These commands change only the **on-disk compression attribute/format**.
The logical bytes that the application reads are transparently decompressed by Windows, so game files remain logically identical.

## Trade-offs

- More compression → less disk space, but **higher CPU overhead** during reads.
- `LZX` usually gives better savings but can be heavier on CPU.
- Some game updates overwrite files and can reset compression for replaced files.

## UI structure

Main window tabs:

1. **Select Folder**: browse + auto-detect Steam/Epic library roots.
2. **Analysis**: total size, file count, estimated savings, top-10 files/folders.
3. **Run**: choose profile (Safe / Stronger LZX), Start/Pause/Resume/Stop, Rollback.
4. **Settings**: exclusions (extensions / folder fragments).

## Safety rules implemented

- Never follows reparse points (symlinks/junctions/mount points) outside the folder.
- Checks volume info and warns if not NTFS / compression unsupported.
- All work is off the UI thread; operations support cancellation.
- Logs are written to `%LocalAppData%\GameShrink\gameshrink.log`.

## Packaging

Portable zip:

- Build Release
- Copy folder:
  - `GameShrink\GameShrink.App\bin\Release\net8.0-windows\`
- Zip it and distribute.

MSIX is possible, but not included in this minimal sample.
