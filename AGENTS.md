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

## 规则 3：每次修改代码后必须自行编译验证（要求，不是可选项）

任何涉及代码 / asmdef / 脚本宏定义的改动，**在向用户汇报完成之前**，必须自行完成编译验证，确认**零编译错误**，否则任务不算完成、不允许提交。

验证方式（二选一，以实际可行者为准）：

1. **Unity CLI 批处理编译**（首选，可自动化执行）：
   ```powershell
   "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" `
     -batchmode -quit -projectPath "d:\Projects\AI\SudokuGameBox\GameBox" `
     -logFile "d:\Projects\AI\SudokuGameBox\Build\Logs\compile-check.log"
   ```
2. **回到 Unity 编辑器查看 Console**（用户正开着编辑器时使用）。

判断标准：

- 编译日志不得含有 `error CS` / `Compilation failed` / `Scripts have compiler errors`。
- 以日志尾部出现 `Exiting batchmode successfully now!` 为准；**注意 Unity 退出码 2 不代表编译失败**（licensing 噪音），`-runTests` 模式禁止加 `-quit`。
- Console 中 Errors 数量必须为 0。

禁止行为：

- 禁止在未编译验证（或 Console 仍有报错）时就汇报"完成"。
- 禁止提交带编译错误的代码。

## 规则 4：Android 发布构建必须走 AAB 构建指南（要求）

任何 agent 执行 Android AAB 发布构建（上架 Google Play 的 release 包）时，必须先读
`docs/17_AAB发布构建指南.md` 并按步骤执行：环境变量注入签名密码、构建后验证产物签名
（jarsigner 须为 `CN=SudokuGameBox`）、**禁止产出或交付 debug 签名包**。

## 适用范围

- 本文件是唯一权威规则来源；`CLAUDE.md` 与 `.github/copilot-instructions.md` 仅为入口，引用本文件，不重复维护内容。
- 若规则需要修改，直接改本文件即可对所有 agent 生效。