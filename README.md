# DuckovTogether Server

A headless dedicated server solution for multiplayer gaming experiences.

无头专用服务器解决方案，提供多人游戏体验。

## Overview | 概述

DuckovTogether Server is an **independently developed** dedicated server application. This project is created from scratch and is **not affiliated with, derived from, or based on any other project or codebase**.

DuckovTogether Server 是一个**独立开发**的专用服务器应用程序。本项目从零开始创建，**与任何其他项目或代码库无关联、无衍生关系**。

This software is developed independently by tlkppm and contributors.

本软件由 tlkppm 及贡献者独立开发。

## Features | 功能特性

- **Headless Operation | 无头运行** - Deploy on cloud servers without display requirements | 可部署在无显示器的云服务器
- **Auto Resource Detection | 自动资源检测** - Automatic game path detection via Windows Registry and Steam libraries | 通过注册表和Steam库自动检测游戏路径
- **Data Validation | 数据验证** - Built-in validation layer for data integrity | 内置验证层确保数据完整性
- **State Synchronization | 状态同步** - Efficient synchronization for players, AI entities, and items | 高效同步玩家、AI和物品状态
- **Configurable | 可配置** - Flexible server configuration options | 灵活的服务器配置选项

## System Requirements | 系统要求

| Requirement | Specification |
|-------------|---------------|
| Operating System | Windows 10/11, Windows Server 2019+ |
| Runtime | .NET 8.0 |
| Network | Open port (default: 9050) |

## Installation | 安装

1. Download the latest release from the Releases page | 从 Releases 页面下载最新版本
2. Extract to your preferred directory | 解压到任意目录
3. Run `DuckovTogetherServer.exe` | 运行 `DuckovTogetherServer.exe`

The server will automatically detect the game installation path.

服务器将自动检测游戏安装路径。

## Configuration | 配置

Edit `server_config.json`:

```json
{
  "Port": 9050,
  "MaxPlayers": 4,
  "ServerName": "DuckovTogether Server",
  "GamePath": "",
  "TickRate": 60
}
```

| Parameter | Description | Default |
|-----------|-------------|---------|
| Port | Server listening port | 9050 |
| MaxPlayers | Maximum concurrent players | 4 |
| ServerName | Server display name | DuckovTogether Server |
| GamePath | Game installation path (auto-detect if empty) | "" |
| TickRate | Server tick rate (Hz) | 60 |

## Command Line Arguments

```bash
DuckovTogetherServer.exe --port=9050 --max-players=4 --game-path="C:\Games\Duckov"
```

## Building from Source

```bash
git clone https://github.com/DuckovMultiplayer/DuckovTogether-Server.git
cd DuckovTogether-Server
dotnet build -c Release
```

## Legal Notice

### Independence Statement

This project is an **independent work** created from scratch. It is:

- NOT a fork, derivative, or modification of any existing project
- NOT affiliated with or endorsed by any game developer or publisher
- NOT based on any other codebase or project

### License

This project is licensed under a custom non-commercial license. See [LICENSE](LICENSE) for details.

Key terms:
- Personal, non-commercial use permitted
- Modification permitted with attribution
- Free distribution permitted
- Commercial use prohibited
- Monetization prohibited

### Disclaimer

This software is provided "AS IS" without warranty of any kind. Users assume all risks associated with its use. See [DISCLAIMER.md](DISCLAIMER.md) for complete terms.

## Support

For issues and feature requests, please use GitHub Issues.

---

Copyright (c) 2025 tlkppm. All rights reserved.
