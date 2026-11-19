# 交接整理（2026-11-10）

使用者決定：開私人 GitHub repo 推上去；10/28 隨機種子改動併入；文件用繁體中文；已併分支刪除、look1 留 archive tag。

## Git
- [x] 10/28 主資料夾的隨機種子改動（SeedSource）套進 feat/realistic-hank-preview，跑測試
- [x] 10/28 的 3 份研究與概念文件搬進來
- [x] 秘密掃描（gitleaks：工作區與歷史）
- [x] 分批 commit（作者 Max Lai）
- [x] 全套 EditMode＋PlayMode 測試
- [x] main fast-forward 到整理後的分支；主資料夾還原乾淨
- [x] 刪 35 個已併分支；look1 打 tag `archive/look1-toon-spike` 後刪分支與 worktree
- [x] 開 private GitHub repo，推 main 與 tags

## 文件
- [x] README.md：專案是什麼、怎麼建置、測試、上機
- [x] docs/HANDOVER.md：現況、比賽時程與規則、秘密清單（只列名稱）、已知問題、下一步、踩過的坑
- [x] 比賽影片流程搬進 repo 並可重建（scratchpad 已被系統清掉，只剩 repo 副本）

## 結果
- 私人 repo：https://github.com/elebymax/tell-poker（main、tag archive/look1-toon-spike）。
- 測試（2026-11-10，預設場景）：EditMode 876＝828 過／0 失敗／48 略過；PlayMode 885＝879 過／0 失敗／6 略過（略過的是截圖工具、錄影重播、需要真網路的測試）。
- repo 提交的場景維持預設（卡通）版，寫實版只在建置時用旗標產生；交接時一度誤提交寫實場景，已改回。
- gitleaks：歷史只有一筆，是 Meta XR Immersive Debugger 自動產生的本機 DevAgent 配對碼（`Assets/Resources/DevAgentSettings.asset`，功能關閉、只連區網），未改寫歷史。
- 文件經獨立查證後修正（.gitignore 誤排除 Remotion 原始碼、送件版本說法矛盾、README 補上機前置步驟等）。
- 影片完整重建未驗證：素材在頭盔 `/sdcard/Oculus/VideoShots/`，交接當下頭盔沒接上。
