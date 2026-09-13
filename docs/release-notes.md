# Rovilo 发布说明（Play Console「此版本中的新功能」）

> 用途：每次出包产出的 **英文发布说明**，直接粘贴到
> Play Console → 发布 → 版本 →「此版本中的新功能 / What's new」。
> 每种语言上限 **500 字符**；**英文是上架文案，中文仅供理解、不上架**
> （与 `docs/store-listing-descriptions.md` 同规矩）。
>
> 取材与改写原则见 `.claude/skills/build-android-aab/SKILL.md` §5。
> 规矩：面向玩家（写"你能做什么"）、纯内部改动不写、不堆砌关键词。
>
> **最新在上。**

---

## versionCode 17 · 2026-09-13

**English（上架用，直接粘贴）**

```
Bug fixes and performance improvements.
```

**中文对照（不上架）**

```
问题修复与性能优化。
```

> 相对 v16 的用户可见改动：**无**。
> 本期提交（`55b57d8` 埋点、`2ccb35b` 广告位 ID、`f74102c`/`f0ae0dc` 文档与安全清理、
> `1901bc8`/`1102c2b` 构建与文档）全部属内部改动，按规矩不写入发布说明，
> 故采用官方认可的兜底句。
> 说明：广告位由官方测试位切为真实广告位，AdMob 就绪度审核通过前表现为「无填充」
> （即玩家看不到广告），这不是新功能，同样不写入。

---

## versionCode 16 · 2026-09-11

**English（上架用，直接粘贴）**

```
We've added two new buttons to the Settings screen:

• Rate Us — rate Rovilo and share your feedback without leaving the game
• Support — get in touch with us directly if you need help or want to report a problem

Thanks for playing! Your feedback helps us make Rovilo better.
```

**中文对照（不上架）**

```
我们在设置页面新增了两个按钮：

• 给个好评 —— 无需离开游戏，即可为 Rovilo 评分并留下反馈
• 联系支持 —— 遇到问题或需要帮助时，可直接与我们联系

感谢游玩！你的反馈会帮助我们把 Rovilo 做得更好。
```

> 相对 v15 的用户可见改动：仅 `c593662 feat(设置)` 一条。
> 同期另外两条提交（`701eed2` 隐私政策文档、`fa46b41` 构建宏）属内部改动，按规矩不写入发布说明。

---
