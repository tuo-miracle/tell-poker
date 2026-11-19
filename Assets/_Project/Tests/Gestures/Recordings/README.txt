頭盔錄製的手部資料（.hands）放這裡，ReplayTests.Recorded 會逐一回放。
檔名慣例：[no_]<手勢>[_<說明>]_<編號>.hands
  手勢：peek、chips、bet、retract、knock、foldpush、allin、flip、idle
  slam（舊的摔牌標籤）：摔牌手勢已移除，當反例，和 idle 一樣預期沒有任何行動事件
  例：knock_01.hands（預期 KnockCheck）
      no_knock_single_01.hands（只敲一下，預期沒有 KnockCheck）
      idle_desk_01.hands（日常雜動，預期沒有任何行動事件）
錄製時的 Note 可覆寫（每行 key: value）：expect、expect_not、turn、can_check、phase、chips、denom、min_peek
沒有檔案時測試會 Ignore，不算失敗。
