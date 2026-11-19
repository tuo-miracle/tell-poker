# Meta Model API 調查（2026-11-03）：角色背後的 Agent 能否從 Gemini 換過去

> 2026-11-03 決定不採用：延遲約 3.5 秒（Gemini Live 0.7 秒），且 key 之後被拒。程式改動已全部撤回，本文只留作紀錄。

## 結論
- 「Meta Model API」是 `https://api.meta.ai/v1`（不是舊的 Llama API `api.llama.com`，那邊這把 key 列不到模型、Llama 模型名都 400）。文件：<https://dev.meta.ai/docs>（索引 <https://llama.developer.meta.com/llms.txt>，各頁加 `.md` 可直接讀）。
- 這把 key（主 checkout `.secrets/meta-llama.key`，格式 `LLM|…`；文件寫的是 `LLM_…`，但照原樣當 Bearer 可用）實測：
  - `GET /v1/models` 200：`muse-wonder-1.0`、`muse-spark-1.3`、`muse-spark-1.3-contributor`、`muse-spark-1.2`、`muse-spark-1.2-contributor`、`muse-spark-1.1`、`muse-voice-transcribe-1.0`、`muse-image-1.0`、`sam-3.1`。
  - `POST /v1/chat/completions`（`muse-spark-1.2`、`reasoning_effort: low`、`max_completion_tokens: 5`）200，2.41 s（單次、含連線）；5 個輸出 token 有 3 個是推理，`content` 為 null。**輸出上限要留給推理 token。**
- 沒有像 Gemini Live 那樣「音訊進、function call 出」的即時對話 API；有的是**即時語音轉文字**（Muse Voice Transcribe WebSocket）＋**文字模型**（Muse Spark，OpenAI 相容、支援 tool calling、structured output、SSE 串流）。

## API 能力（官方文件）
| 項目 | 內容 | 來源 |
|---|---|---|
| 端點／認證 | `https://api.meta.ai/v1`，`Authorization: Bearer`；Responses、Chat Completions（OpenAI 相容）、Messages（Anthropic 相容） | <https://dev.meta.ai/docs/api-reference.md> |
| 文字模型 | `muse-spark-1.3`（建議新用途）、`1.2`、`1.1`；`muse-wonder-1.0`（preview，長任務）；`-contributor` 版較便宜但內容可被拿去訓練 | <https://dev.meta.ai/docs/models.md> |
| 推理 | `reasoning_effort` minimal/low/…；`none` 對 Muse Spark 回 400（**一定有推理延遲**） | <https://dev.meta.ai/docs/reasoning.md> |
| Tool calling | 支援（function tools、`tool_choice`、`parallel_tool_calls`，Chat Completions 與 Responses） | <https://dev.meta.ai/docs/tool-calling.md> |
| Structured output | `response_format` JSON schema | <https://dev.meta.ai/docs/structured-output.md> |
| 串流 | SSE（`stream: true`）；推理時第一個可見 chunk 會晚到 | <https://dev.meta.ai/docs/protocols/chat-completions.md> |
| 音訊理解 | Muse Spark 吃 `input_audio`（wav/mp3，base64 或 file_id），**1.3 音訊品質「未完全支援」，文件建議用 1.2** | <https://dev.meta.ai/docs/video-understanding.md> |
| 語音轉文字 | `muse-voice-transcribe-1.0`：`wss://api.meta.ai/v1/asr/realtime`（握手帶 token、PCM16 mono 16/24 kHz、partial、ENDPOINTING 斷句、keywords 偏向）與 `POST /v1/asr/transcribe` | <https://dev.meta.ai/docs/speech-to-text.md>、<https://dev.meta.ai/docs/api-reference/voice/realtime.md> |
| 文字轉語音／即時對話 | 文件索引裡沒有 | llms.txt |
| 計價 | Muse Spark Standard：input $1.25、cached $0.15、output $4.25／1M token（Contributor $0.10／$0.002／$0.20）；Wonder $4／$0.20／$20；Voice Transcribe $0.18／小時 | <https://dev.meta.ai/docs/pricing-rate-limits.md> |
| 速率 | Spark/Wonder 3,000 RPM、4M TPM（per team）；contributor 150 RPM；Transcribe 256 併發串流、16,000 串流／小時 | 同上 |

## 目前 voice proxy 裡 Gemini 做的事與可換性
| 功能 | 現在 | 換 Meta |
|---|---|---|
| /live：邊講邊送音訊、聽懂＋回話＋reply() function call（`session.ts`、`live.ts`） | Gemini Live，講完→第一塊音訊 708／969 ms | **要拆兩段**：Voice Transcribe realtime（邊講邊送）＋ Muse Spark chat（tool call 或 JSON schema） |
| /live 的備案、/talk 整段（`gemini.ts` `callGemini`、`handler.ts`） | generateContent 吃 wav、responseSchema | 可直接換成 Muse Spark 1.2 `input_audio`＋`response_format`（單次呼叫），或 transcribe＋chat |
| 誰回話（addressee） | 本地正規式＋模型的 speaker 欄位 | 不受影響（模型欄位照樣用 schema） |
| 事實數字、PG 過濾、罐頭句、TTS | 本地／ElevenLabs | 不受影響 |
| bake 聽檢（`bake/qa.ts`） | Gemini 逐字轉錄＋對照檢查 | 可換：`/v1/asr/transcribe` 做盲聽轉錄；對照檢查用 Muse Spark。非即時、不急，建議最後再換 |
| smoke／bench（`smoke/*.ts`） | Gemini | 跟著改 |

## 推薦架構
1. Unity 不變（還是 /live WebSocket、PCM16、VAD 結尾送 end）。
2. 中繼一開口就開 `wss://api.meta.ai/v1/asr/realtime`（24 kHz 或 16 kHz，`keywords`: Pip/Rocco/Vex/Silas/Dot/pot/blinds…），邊收邊轉送；Unity 的 end → `endStream`，拿最終 transcript。
3. transcript ＋ 公開牌局狀態 ＋ 角色設定 → `muse-spark-1.2`（或 1.3，文字用途沒有音訊限制）Chat Completions，`reasoning_effort: "minimal"`、`stream: true`、`response_format` 用現有 reply 欄位的 JSON schema（或 tool_choice 強制 `reply`）。
4. 之後照舊：facts.ts 填數字 → ElevenLabs /stream。
5. 開關：`AGENT=gemini|meta`（預設 gemini），失敗（逾時、4xx/5xx）自動退回 Gemini 路徑，再不行就是現有的離線罐頭句。
- 延遲估計（未實測）：轉錄收尾 0.2–0.5 s ＋ Muse Spark minimal 推理＋短回覆 0.8–2.5 s（單次實測 5 token 就 2.4 s，含連線）＋ TTS 第一塊 0.16 s ≈ **1.2–3 s**，可能比現在的 0.7 s 慢 2–4 倍。要先寫 bench（同 `smoke/bench-live.ts` 的 6 句）量中位數／p90 再決定。
- 成本估計：每句約 1.5k input（system prompt 可快取）＋ 100–200 output（含推理）≈ US$0.001–0.003；轉錄 5 s ≈ US$0.00025。
- 要改的檔：新增 `src/meta.ts`（chat＋schema）、`src/metaasr.ts`（realtime WS client，可沿用 `openUpstream` 的介面）；改 `session.ts`、`handler.ts`、`keys.ts`／`node-server.ts`／`worker.ts`（`MODEL_API_KEY`）、`docs/privacy.md`→`privacy-content.ts`（收音訊的對象從 Google 改成 Meta）、`smoke/bench-*.ts`。
- 不建議 Contributor 模式：內容會被拿去訓練，和隱私頁「不拿你的聲音訓練 AI」衝突。

## 若不用 Meta 的語音轉文字
- 用 Muse Spark 1.2 直接吃 `input_audio`（整段 wav，一次呼叫出 JSON）：最少改動，但不能邊講邊送，延遲最高。
- 或 ElevenLabs Scribe（已有 ElevenLabs 帳號）做轉錄＋Muse Spark 寫回話。

## 沒做到／不確定
- 只做了模型清單＋1 次 5 token 對話；沒量轉錄延遲、沒試 tool calling／JSON schema／音訊輸入、沒試 1.3 vs 1.2 的回話品質。
- 2.41 s 是單一樣本（含 TLS 連線、推理），不能代表穩定延遲。
- key 格式和文件不同（`|` vs `_`）但可用；額度與付款狀態沒查。

## 第 2 階段（2026-11-03）
曾實作 AGENT=meta（已撤回）：Meta 講完→回話文字中位數 2.8 s、第一塊音訊約 3.9 s，比 Gemini Live（0.7 s）慢約 4–5 倍；品質 17/17。
