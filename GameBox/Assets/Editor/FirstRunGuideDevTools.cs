using UnityEditor;
using UnityEngine;

/// <summary>
/// 首局新手引导(HasShownTutorial)开发工具(2026-09-09):
/// 引导为一次性标记——完整走完(Skip/Got it)后 PlayerPrefs.HasShownTutorial=1,
/// 此后进对局不再重播(产品语义,防骚扰)。手动复测/视觉回归需先清标记再进对局。
/// 编辑器 Play 与编辑器菜单共享同一 PlayerPrefs 存储(注册表):点菜单即清,无需改代码。
/// 真机/热更包:PlayerPrefs 在设备上,菜单够不到,请用系统清应用数据或重装。
/// 键名字面量与 FirstRunGuide.PrefsKey 同步(热更程序集不被 Editor 引用,改键名时两处同改)。
/// </summary>
public static class FirstRunGuideDevTools
{
    const string PrefsKey = "HasShownTutorial"; // 权威源:Assets/HotUpdate/Sudoku/FirstRunGuide.cs PrefsKey

    /// <summary>清除已引导标记 → 下次进对局重新弹出三步引导。</summary>
    [MenuItem("Box/Dev/Reset First-Run Guide Flag")]
    public static void ResetTutorialFlag()
    {
        bool wasShown = PlayerPrefs.GetInt(PrefsKey, 0) == 1;
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save(); // 立即落盘,防编辑器非正常退出时标记残留
        Debug.Log($"[FirstRunGuide] 标记清除完成(原值:{(wasShown ? "已引导" : "未引导/无标记")})。"
            + "重新 Play 并进一局即可复测三步引导;无需清除不影响其他偏好。");
    }
}
