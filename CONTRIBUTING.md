# Contributing to AgentR

First off, thank you for considering contributing to AgentR. It's people like you that make AgentR such a great tool for the distributed AI ecosystem.

## 🤝 Code of Conduct

By participating in this project, you agree to abide by our Code of Conduct. We expect all contributors to treat others with respect and professionalism.

## 🛠️ The Development Process

AgentR follows strict enterprise-grade engineering standards.

1.  **Strict Typing**: We use `Nullable` reference types everywhere. No warnings allowed (`TreatWarningsAsErrors` is on).
2.  **Async/Await**: Use `ValueTask` where possible to reduce allocations.
3.  **Tests**: All new features must be accompanied by Integration Tests (`src/AgentR.Tests`).

## 🚀 Pull Request Guidelines

1.  **One Feature, One PR**: Keep pull requests focused on a single change.
2.  **Naming Conventions**:
    *   Branches: `feature/my-feature`, `fix/issue-123`
    *   Commits: Conventional Commits (e.g., `feat: add new mcp client`, `fix: concurrency bug in lock`)
3.  **Documentation**: Update `README.md` and `TECHNICAL_SUMMARY.md` if you change architecture.

## 🐛 Reporting Bugs

Please use the GitHub Issue Tracker. Include:
*   AgentR Version
*   Platform/OS
*   Exception Stack Trace
*   Reproduction Steps

## ⚖️ License

By contributing, you agree that your contributions will be licensed under its MIT License.
Copyright (c) 2026 Digvijay Chauhan.
