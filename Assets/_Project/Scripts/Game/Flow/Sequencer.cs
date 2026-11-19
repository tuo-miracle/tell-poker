using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 演出佇列：一步一步播放（發牌、籌碼滑行、亮牌……），播完牌局流程才往下走。
    /// 由 GameController.Step 驅動，測試可以用固定 dt 重現。
    /// </summary>
    public sealed class Sequencer
    {
        sealed class Item
        {
            public float Duration, T, Timeout;
            public Action Start, End;
            public Action<float> Update;
            public Func<bool> Until;
            public bool Started;
        }

        readonly LinkedList<Item> _items = new LinkedList<Item>();

        public bool IsIdle => _items.Count == 0;
        public int Count => _items.Count;

        /// <summary>一段 duration 秒的動畫：update 收到 0–1 的進度。</summary>
        public void Then(float duration, Action start = null, Action<float> update = null, Action end = null) =>
            _items.AddLast(new Item { Duration = Mathf.Max(0f, duration), Start = start, Update = update, End = end });

        public void Do(Action action) => Then(0f, action);
        public void Wait(float seconds) => Then(seconds);

        /// <summary>等到條件成立（或逾時）才繼續；逾時也會呼叫 end。</summary>
        public void WaitUntil(Func<bool> condition, float timeout, Action end = null) =>
            _items.AddLast(new Item { Until = condition, Timeout = timeout, End = end });

        /// <summary>插到目前這步之後（在步驟的 start 裡追加子步驟用）。</summary>
        public void Next(float duration, Action start = null, Action<float> update = null, Action end = null)
        {
            var item = new Item { Duration = Mathf.Max(0f, duration), Start = start, Update = update, End = end };
            if (_items.First == null) _items.AddFirst(item); else _items.AddAfter(_items.First, item);
        }

        public void Clear() => _items.Clear();

        public void Tick(float dt)
        {
            int guard = 0;
            while (_items.First != null && guard++ < 256)
            {
                var it = _items.First.Value;
                if (!it.Started) { it.Started = true; it.Start?.Invoke(); }
                if (it.Until != null)
                {
                    it.T += dt;
                    dt = 0f;
                    if (it.Until() || it.T >= it.Timeout) { Remove(it); it.End?.Invoke(); continue; }
                    return;
                }
                it.T += dt;
                dt = 0f;
                float p = it.Duration <= 0f ? 1f : Mathf.Clamp01(it.T / it.Duration);
                it.Update?.Invoke(p);
                if (p < 1f) return;
                Remove(it);
                it.End?.Invoke();
            }
        }

        void Remove(Item it)
        {
            if (_items.First != null && _items.First.Value == it) _items.RemoveFirst(); else _items.Remove(it);
        }
    }
}
