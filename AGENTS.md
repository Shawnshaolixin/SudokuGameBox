# 项目智能体约定 (Agent Rules)

本仓库的所有 AI 编码智能体（GitHub Copilot / Claude Code / Codex / Cursor 等）
在生成代码与提交变更时必须遵守以下规则。

## 规则 1：代码必须添加必要注释（要求，不是可选项）

以下场景**必须**写中文注释：

- 公开 API / 类 / 方法：说明用途、关键参数、返回值。
- 非显然的业务逻辑或算法：例如数独求解的回溯、唯一候选、隐藏单等策略。
- 跨模块调用约定与生命周期顺序依赖：例如 Unity `Awake` / `OnEnable` / `Start` 的执行顺序依赖。
- 性能关键路径：例如每帧更新的热点代码。
- 临时方案 / 已知缺陷：用 `TODO` / `FIXME` / `HACK` 标记并说明原因。

禁止事项：

- 禁止写"逐行复述代码"的废话注释（如 `i++ // 自增`）。
- 注释使用中文，与仓库其余代码保持一致。

## 规则 2：提交信息一律使用中文

- 每次 `git commit` 必须使用中文 commit message，**不要使用英文**。
- 格式遵循仓库现有风格（Conventional Commits 中文版）：

  ```
  type(模块): 中文描述
  ```

  常用 type：`feat` / `fix` / `docs` / `chore` / `refactor` / `test` / `perf`。

- 示例（与历史一致）：
  - `feat(资源): Addressables 资源管线与首包瘦身(Phase 6)`
  - `docs(架构): 新增玩法组件指南(13号文档)`
- 提交前先 `git status` 确认改动范围，只提交本次任务相关的文件。

## 适用范围

- 本文件是唯一权威规则来源；`CLAUDE.md` 与 `.github/copilot-instructions.md` 仅为入口，引用本文件，不重复维护内容。
- 若规则需要修改，直接改本文件即可对所有 agent 生效。