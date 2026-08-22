namespace Box.Gameplay
{
    /// <summary>对局计时时钟抽象:测试注入 FakeClock 走纯逻辑,运行时用 UnityClock。</summary>
    public interface IClock
    {
        float Now { get; }
    }

    /// <summary>Unity 时钟:自会话创建起秒数(Time.time)。</summary>
    public sealed class UnityClock : IClock
    {
        public float Now => UnityEngine.Time.time;
    }
}
