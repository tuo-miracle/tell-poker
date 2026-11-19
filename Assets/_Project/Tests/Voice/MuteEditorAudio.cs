using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 跑測試時不要從電腦喇叭播台詞（2026-11-03 使用者在 Mac 上聽到了）：整個測試組件開始前把 AudioListener.volume 設 0，結束還原。
/// 只在測試裡；播放、時長、字幕的邏輯照常（音量不影響 AudioSource 的播放狀態），實機不受影響。
/// </summary>
[SetUpFixture]
public class MuteEditorAudio
{
    float _volume = 1f;

    [OneTimeSetUp] public void Mute() { _volume = AudioListener.volume; AudioListener.volume = 0f; }
    [OneTimeTearDown] public void Restore() => AudioListener.volume = _volume;
}
