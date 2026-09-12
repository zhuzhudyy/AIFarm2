using System;
using System.Reflection;
using AIFarm.Npc;
using AIFarm.Presentation;
using UnityEditor;
using UnityEngine;

namespace AIFarm.Editor
{
    /// <summary>Explicit Game View QA fixtures, never run by the player.</summary>
    public static class TownVisualVerification
    {
        public static void SetResolution(int width, int height)
        {
            Assembly assembly = typeof(UnityEditor.Editor).Assembly;
            Type sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            object sizes = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            object group = sizesType.GetProperty("currentGroup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sizes);
            Type groupType = group.GetType();
            Type sizeType = assembly.GetType("UnityEditor.GameViewSize");
            object fixedResolution = Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType"), "FixedResolution");
            object size = Activator.CreateInstance(sizeType, new object[] { fixedResolution, width, height, "AIFarm QA " + width + "x" + height });
            groupType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int index = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null) - 1;
            Type gameViewType = assembly.GetType("UnityEditor.GameView");
            EditorWindow window = EditorWindow.GetWindow(gameViewType);
            gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, index);
            window.Repaint();
        }

        public static void ShowReadabilityFixture()
        {
            foreach (ResidentId resident in ResidentIds.TownResidents)
                TownDialogueOverlay.Publish(resident,
                    "【界面验收文本】今天我沿着小路检查了农田，又去池塘边观察鱼群。作物还在生长，缺水时需要回来浇水；果树摘完后要等明天再结果。这段较长的中文用来检查自动换行、边缘裁切和多人气泡重叠。",
                    "ui-verification-" + resident.Value, source: "UI fixture");
        }
    }
}
