# 差異測試牌局紀錄格式（JSON Lines，一行一手）

```json
{"id":17,
 "seats":[1000,0,850,2000],          // 開局各座位籌碼（座位順序＝順時針；0 = 空位/出局，不參與）
 "button":2, "sbSeat":3, "bbSeat":0,  // 單挑時 button == sbSeat
 "sb":50, "bb":100,
 "hole":{"0":"AsKd","2":"7c7h","3":"Td9d"},   // 參與者的底牌；牌面 rank∈23456789TJQKA, suit∈cdhs
 "board":"2c3d4h5s6c",                 // 永遠給滿 5 張（未用到的街照樣給）
 "actions":[{"seat":3,"type":"call"},
            {"seat":0,"type":"raise","to":300},
            {"seat":2,"type":"allin","to":850},   // allin：to 為本街下注到多少
            {"seat":3,"type":"fold"}],
 "final":[1450,0,1700,700]             // 牌局結束後各座位籌碼（產生端有給；重播端比對）
}
```

- type ∈ fold | check | call | bet | raise | allin。bet/raise/allin 帶 `to`（本街「下注到」的總額，不是加注多少）。
  allin 若金額不超過跟注額，就是 all-in 跟注。
- 行動順序：翻牌前從大盲左邊第一位開始（單挑時為按鈕/小盲），翻牌後從按鈕左邊第一位開始，順時針。
- 盲注：小盲 min(stack, sb)、大盲 min(stack, bb)。無前注。

## 已知差異的分類（`run-diff.sh` 會自動歸類；只有 UNEXPLAINED 才算失敗）

| 類別 | 說明 | 依據／固定測試 |
|---|---|---|
| `oddchip_policy` | 本引擎每層邊池各自分配、多枚零頭從按鈕左邊逐枚分；PokerKit 會先殺掉輸家、合併剩餘贏家相同的池，零頭全給第一位。C# 產生端額外輸出 `final_pk_oddchip`（以 PokerKit 政策重算的結果）供比對。 | TDA 21、23；`DiffRegressionTests.Diff17`、`Diff3` |
| `pk_pointless_check` | 其他人都 all-in，某人無須跟注也無法加注時，PokerKit 仍要求他 check；本引擎直接亮牌發完。籌碼結果相同。 | TDA 17；`Diff94` |
| `pk_reopens_after_short_allin` | PokerKit 0.7.7 在盲注玩家短 all-in 加注後，讓已行動且只面對不足完整加注的玩家再加注；本引擎拒絕。 | TDA 49-A；`Diff452` |

| `pk_rejects_cumulative_reopen` | 多個短 all-in 累計達完整加注後，本引擎讓先前已行動者可再加注；PokerKit 0.7.7 拒絕。 | TDA 49-A（Addendum Ex.1）；`Diff2878` |

生成器不產生「大小盲相等」的盲注：PokerKit 在此情況讓大盲先行動，與 TDA 36-C 不同（`Diff416`）。
