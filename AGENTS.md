# 仓库协作规则

## Worktree 完成后自动创建正式 PR

当你在 **独立 git worktree 且非默认分支（非 `main`）** 中完成了用户的修改任务后，**无需等待用户再次催促，自动完成提交、推送并创建正式 PR**：

1. **触发条件**
   - 当前处于独立 git worktree 且分支不是 `main`。
   - 本次任务产生了需要合入的代码、文档或配置改动。
   - 纯问答、只读排查或用户明确表示「先别提 PR / 只在本地改 / 建草稿 PR」时，遵守用户当次要求，不自动创建正式 PR。

2. **提交前验证**
   - 涉及 C# / .NET 代码改动时，提交前必须通过零警告构建与全部单元测试：
     ```sh
     dotnet build Jige.slnx -p:EnableWindowsTargeting=true -warnaserror
     dotnet test Jige.slnx -warnaserror
     ```
   - 同时遵守 [CONTRIBUTING.md](CONTRIBUTING.md) 中的只读协议、出处标注与验证分级红线。

3. **提交、推送与创建正式 PR**
   - 仅暂存本次任务相关的改动，按仓库习惯用简洁清晰的中文提交信息 commit。
   - 将分支推送到远端（`git push -u origin HEAD`）。
   - 如果该分支尚无开放的 PR，**直接创建正式 PR**（`draft: false`，目标分支 `main`）；在 T3 Code 环境下优先调用 `pull_request_create` 工具（以便自动关联当前线程）。
   - 如果该分支已有开放的 PR，直接推送新提交更新该 PR，不重复创建。

4. **汇报结果**
   - 在最终回复中附上 PR 链接、分支名、改动摘要和构建/测试验证结果。
