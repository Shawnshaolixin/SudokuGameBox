using System.Runtime.CompilerServices;

// 允许测试程序集 Sudoku.Core.Tests 访问本程序集的 internal 成员,
// 便于对内部算法工具(如 CandidateMath)做白盒测试。
[assembly: InternalsVisibleTo("Sudoku.Core.Tests")]
