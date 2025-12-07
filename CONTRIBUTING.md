# Contributing to DuckovTogether Server

# 贡献指南

Thank you for your interest in contributing to DuckovTogether Server. This document provides guidelines and standards for contributing to this project.

感谢您有兴趣为 DuckovTogether Server 做出贡献。本文档提供了参与本项目的指南和规范。

---

## Table of Contents | 目录

- [Code of Conduct | 行为准则](#code-of-conduct--行为准则)
- [Getting Started | 开始之前](#getting-started--开始之前)
- [Development Environment | 开发环境](#development-environment--开发环境)
- [Coding Standards | 编码规范](#coding-standards--编码规范)
- [Commit Guidelines | 提交规范](#commit-guidelines--提交规范)
- [Pull Request Process | 拉取请求流程](#pull-request-process--拉取请求流程)
- [Issue Guidelines | Issue指南](#issue-guidelines--issue指南)
- [License | 许可证](#license--许可证)

---

## Code of Conduct | 行为准则

### English

- Be respectful and inclusive to all contributors
- Provide constructive feedback
- Focus on the code, not the person
- Accept criticism gracefully
- Help others learn and grow

### 中文

- 尊重并包容所有贡献者
- 提供建设性的反馈
- 关注代码本身，而非个人
- 优雅地接受批评
- 帮助他人学习和成长

---

## Getting Started | 开始之前

### Prerequisites | 前置要求

| Requirement | Version |
|-------------|---------|
| .NET SDK | 8.0+ |
| Git | 2.30+ |
| IDE | Visual Studio 2022 / JetBrains Rider / VS Code |

### Fork and Clone | 复刻与克隆

```bash
# Fork the repository on GitHub first
# 首先在GitHub上复刻仓库

git clone https://github.com/YOUR_USERNAME/DuckovTogether-Server.git
cd DuckovTogether-Server
git remote add upstream https://github.com/DuckovMultiplayer/DuckovTogether-Server.git
```

### Build | 构建

```bash
dotnet restore
dotnet build
```

---

## Development Environment | 开发环境

### Recommended IDE Settings | 推荐IDE设置

- Enable nullable reference types | 启用可空引用类型
- Use spaces (4) for indentation | 使用4个空格缩进
- UTF-8 encoding without BOM | UTF-8编码（无BOM）
- LF line endings preferred | 优先使用LF换行符

### Project Structure | 项目结构

```
HeadlessServer/
├── Core/                 # Core business logic | 核心业务逻辑
│   ├── Assets/          # Game asset parsing | 游戏资产解析
│   ├── GameLogic/       # Game logic | 游戏逻辑
│   ├── Save/            # Save management | 存档管理
│   ├── Security/        # Security features | 安全功能
│   ├── Sync/            # State synchronization | 状态同步
│   └── World/           # World state | 世界状态
├── Net/                  # Networking | 网络层
├── Plugins/              # Plugin system | 插件系统
└── Program.cs            # Entry point | 入口点
```

---

## Coding Standards | 编码规范

### Naming Conventions | 命名规范

| Element | Style | Example |
|---------|-------|---------|
| Namespace | PascalCase | `DuckovTogether.Core` |
| Class | PascalCase | `PlayerSyncManager` |
| Interface | IPascalCase | `IPlugin` |
| Method | PascalCase | `SendMessage()` |
| Property | PascalCase | `PlayerName` |
| Private Field | _camelCase | `_playerCount` |
| Parameter | camelCase | `playerId` |
| Constant | UPPER_SNAKE | `MAX_PLAYERS` |
| Event | PascalCase | `OnPlayerJoin` |

### Code Style | 代码风格

```csharp
// Good | 正确
namespace DuckovTogether.Core;

public class PlayerManager
{
    private readonly Dictionary<int, PlayerState> _players = new();
    
    public int PlayerCount => _players.Count;
    
    public void AddPlayer(int peerId, PlayerState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
            
        _players[peerId] = state;
    }
}

// Avoid | 避免
namespace DuckovTogether.Core
{
    public class playerManager  // Wrong casing
    {
        Dictionary<int, PlayerState> players;  // Missing access modifier
        
        public void addPlayer(int PeerId, PlayerState State)  // Wrong casing
        {
            players[PeerId] = State;  // No null check
        }
    }
}
```

### Documentation | 文档注释

```csharp
/// <summary>
/// Sends a message to the specified player.
/// 向指定玩家发送消息。
/// </summary>
/// <param name="peerId">Target player's peer ID. | 目标玩家的对等ID。</param>
/// <param name="data">Message data to send. | 要发送的消息数据。</param>
/// <returns>True if sent successfully. | 发送成功返回true。</returns>
/// <exception cref="ArgumentException">Thrown when peerId is invalid. | 当peerId无效时抛出。</exception>
public bool SendToPlayer(int peerId, byte[] data)
{
    // Implementation
}
```

### Error Handling | 错误处理

```csharp
// Good | 正确
try
{
    ProcessMessage(data);
}
catch (JsonException ex)
{
    Console.WriteLine($"[Error] JSON parse failed: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"[Error] Unexpected error: {ex.Message}");
}

// Avoid | 避免
try
{
    ProcessMessage(data);
}
catch { }  // Never use empty catch blocks | 永远不要使用空catch块
```

---

## Commit Guidelines | 提交规范

### Commit Message Format | 提交消息格式

```
<type>(<scope>): <subject>

<body>

<footer>
```

### Types | 类型

| Type | Description | 描述 |
|------|-------------|------|
| `feat` | New feature | 新功能 |
| `fix` | Bug fix | 修复Bug |
| `docs` | Documentation | 文档更新 |
| `style` | Code style | 代码风格 |
| `refactor` | Code refactoring | 代码重构 |
| `perf` | Performance | 性能优化 |
| `test` | Tests | 测试相关 |
| `chore` | Build/tools | 构建/工具 |

### Examples | 示例

```
feat(plugins): add hot reload support

- Implement AssemblyLoadContext for plugin unloading
- Add reload command to console
- Update plugin documentation

Closes #42
```

```
fix(sync): resolve player desync on scene change

Players were not properly synchronized when changing scenes
due to race condition in state update.

Fixes #78
```

---

## Pull Request Process | 拉取请求流程

### Before Submitting | 提交前

1. **Sync with upstream | 与上游同步**
   ```bash
   git fetch upstream
   git rebase upstream/main
   ```

2. **Run tests | 运行测试**
   ```bash
   dotnet test
   ```

3. **Build successfully | 确保构建成功**
   ```bash
   dotnet build -c Release
   ```

### PR Template | PR模板

```markdown
## Description | 描述
Brief description of changes.
简要描述更改内容。

## Type of Change | 更改类型
- [ ] Bug fix | 修复Bug
- [ ] New feature | 新功能
- [ ] Breaking change | 破坏性更改
- [ ] Documentation | 文档更新

## Testing | 测试
Describe testing performed.
描述已执行的测试。

## Checklist | 检查清单
- [ ] Code follows style guidelines | 代码遵循风格指南
- [ ] Self-reviewed code | 已自我审查代码
- [ ] Added comments for complex logic | 为复杂逻辑添加了注释
- [ ] Updated documentation | 更新了文档
- [ ] No new warnings | 无新警告
```

### Review Process | 审查流程

1. Maintainer reviews code | 维护者审查代码
2. Address feedback | 处理反馈
3. Approval required | 需要批准
4. Squash and merge | 压缩合并

---

## Issue Guidelines | Issue指南

### Bug Report | 错误报告

```markdown
**Environment | 环境**
- OS: Windows 11
- .NET Version: 8.0.1
- Server Version: 1.0.0

**Description | 描述**
Clear description of the bug.
清晰描述错误。

**Steps to Reproduce | 复现步骤**
1. Start server
2. Connect client
3. Perform action
4. Observe error

**Expected Behavior | 预期行为**
What should happen.
应该发生什么。

**Actual Behavior | 实际行为**
What actually happens.
实际发生什么。

**Logs | 日志**
Relevant log output.
相关日志输出。
```

### Feature Request | 功能请求

```markdown
**Description | 描述**
Clear description of the feature.
清晰描述功能。

**Use Case | 使用场景**
Why this feature is needed.
为什么需要此功能。

**Proposed Solution | 建议方案**
How it could be implemented.
如何实现。
```

---

## License | 许可证

By contributing, you agree that your contributions will be licensed under the same license as the project.

通过贡献，您同意您的贡献将根据与项目相同的许可证进行许可。

---

## Contact | 联系方式

- **GitHub**: [DuckovMultiplayer](https://github.com/DuckovMultiplayer)
- **Issues**: Use GitHub Issues for bug reports and feature requests

---

Thank you for contributing to DuckovTogether Server!

感谢您为 DuckovTogether Server 做出贡献！
