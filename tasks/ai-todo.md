# AI 工作線計畫（worktree ai）

## 架構
- BotView：從 HandState 只複製公開資訊＋自己的底牌（隱藏資訊防線）
- TableObservations：只吃 PlayerView 過濾後的事件，收到別人私密事件就丟例外
- PreflopTable（169 類，離線 MC 產生的常數表＋產生器）→ Equity（MC，依對手動作加權）
- Personality（資料＋三預設）→ Policy → AiDecision（Command、Intent、勝率、思考時間、Cue）
- PlayerSignals／PlayerRead（±0.08 上限、時間衰減、全域開關）
- TellDirector（真實／反向／掩飾演出）＋ LineBook（每角色 15–25 句、冷卻）
- HandReveal（每手結束亮牌與內心狀態）
- 自我對弈（Slow）：VPIP／PFR／AF／破綻一致率

## 步驟
- [x] PreflopTable＋產生器＋測試
- [x] Equity MC＋測試（含不讀隱藏資訊）
- [x] Personality＋PlayerRead＋測試
- [x] TellDirector＋LineBook＋測試
- [x] AiPlayer／Policy／Observations／HandReveal＋測試（合法性、堅果不棄、隱藏資訊、思考時間、效能）
- [x] 自我對弈校準與 Slow 測試
- [x] 全部測試、commit、回報

## Review（2026-10-12）
- 快速測試 290 個：287 通過、3 略過（AI 新增 51 個：49 通過、2 個 Explicit 工具）；突變測試（決策時偷看下一座位底牌）會讓「隱藏資訊」測試變紅。
- 決策耗時（Mono、M1 Max）：翻牌、3 對手、MC 1000 次，平均約 0.46 ms。
- 自我對弈 100,000 手（MC 1000、4 執行緒、39 秒）：Pip 16.3/6.2/AF 0.76、Rocco 33.5/24.6/2.88、Vex 21.7/16.0/2.11；破綻一致率 92.0%／65.1%／35.0%（設定 92／65／35）。
- 校準過程：OpenScale 1.12→1.0；半虛張加注移到「夠跟」分支（原本幾乎不會觸發）；Rocco 攻擊度 0.85、Vex 0.8。
