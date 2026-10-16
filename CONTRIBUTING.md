# 贡献指南

感谢关注本项目！欢迎提交 Issue 与 Pull Request。

## 开始之前

- 开发环境：Windows 10/11 + [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（推荐 Visual Studio 2022 或 VS Code + C# Dev Kit）。
- 构建与运行：

  ```powershell
  dotnet build src -c Release
  dotnet run --project src
  ```

- 提交前请确保 `dotnet build src -c Release` 无警告无错误；涉及导入/计分/考试的改动，建议附带一条 `--selftest` 断言。

## 版权红线（重要）

**请勿向本仓库提交任何真实考试真题、受版权保护的题目内容或题库文件**（包括 Issue、PR、截图中的文字）。此类提交将被拒绝或清除。测试请使用 `samples/` 中的格式示例或自编内容。

## 提交规范

- Commit 信息建议使用 `类型: 摘要` 格式（如 `fix: 修复导入器判断题归一化`、`feat: 支持图片压缩开关`）。
- 一个 PR 聚焦一件事；涉及 UI 变更请附运行截图。

## 代码约定

- UI 文案与颜色遵循 `src/App.xaml` / `Ui.cs` 中的既有色板；
- 计分规则（全对才得分、答案归一化）为需求约束，不做可配置化改动，除非先在 Issue 中达成一致；
- 新增数据表/字段请同步更新 `Services/Db.cs` 中的建表语句与 `CHANGELOG.md`。
