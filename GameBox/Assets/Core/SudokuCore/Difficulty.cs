namespace Sudoku.Core
{
    /// <summary>
    /// 数独难度档位(数值越大越难)。
    /// 阶段 A 仅暴露 Easy / Medium / Hard 三档给玩法,其余为后续扩展预留。
    /// </summary>
    public enum Difficulty
    {
        Beginner = 0,
        Easy = 1,
        Medium = 2,
        Hard = 3,
        Expert = 4,
        Master = 5
    }
}
