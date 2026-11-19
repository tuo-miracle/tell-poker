# Poker.Core 實作計畫（core 分支）

- [x] Card / Pcg32 / Deck（PCG32 對官方參考輸出鎖定；洗牌結果鎖定）
- [x] HandEvaluator（bitmask + 8192 查表；手工比較案例；窮舉 Slow 測試；速度基準）
- [x] 規則引擎：HandState（不可變）、Command、GameEvent、Engine.Apply、LegalActions
  - [x] StartHand：按鈕（明確或自動輪轉、跳過 0 籌碼）、單挑按鈕=SB、盲注（不足則 all-in）、發底牌
  - [x] 行動：Fold/Check/Call/Bet/Raise/AllIn、最小加注（raise-to）、lastFullRaise、短 all-in 不重開（累計）
  - [x] 回合結束：退還未跟注、PotsUpdated、發下一街；all-in 直接發完；只剩一人立即結束
  - [x] 攤牌：分層邊池、平分、零頭按鈕左起、攤牌順序（TDA 18A）
  - [x] InterpretChipCommit（TDA 50% 規則，條文寫進註解）
  - [x] PlayerView 私密事件過濾
- [x] 差異測試：tools/difftest（C# 產生/重播）+ PokerKit（Python）雙向 ≥10,000 手
- [x] XML 註解（臺灣中文）、最終驗證、commit

## 設計決定
- 按鈕：移動式按鈕（下一位有籌碼者），非 TDA dead button；單挑時避免同一人連續兩次大盲。
- 大盲不足額 all-in 時，跟注水位＝實際貼出的最高盲注，最小加注單位仍為大盲（TDA 無「須跟完整大盲」條文；53-B 視貼出的大盲為開局下注；與 PokerKit 0.7.7 一致）。

## Review（2026-10-12）
- 快速測試 144/144；窮舉 133,784,560 手各牌型數量全對（1.89 s）；EvaluateMask 約 59 M/s、Card 版約 27 M/s（Mono、M1 Max）。
- 差異測試（PokerKit 0.7.7，雙向）：最終 10,000×2 手 0 個未解釋差異；20,000×2 手（seed 7）另有 1 例由 TDA 49-A 裁定（Diff2878）。已知差異分類見 tools/difftest/FORMAT.md。
- Fresh-context 審查（opus）：無規則 bug；修正事件／指令可被轉型竄改、總籌碼溢位兩點。
