using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace STS2Mobile.Patches;

// Android can reach NDevConsole._Ready before the desktop-only debug console
// dependencies are fully available. If its NullReferenceException escapes
// Godot's C# bridge, Godot tears down the process and aborts. Keep the console
// behavior when it initializes successfully, but suppress the known Android
// initialization failure so the main menu can continue loading.
internal static class DevConsolePatches
{
    private const BindingFlags AllFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string ReadyMethod = "_Ready";
    private const string InstanceGetterMethod = "get_Instance";
    private const string TypeName = "MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole";

    private static FieldInfo _instanceField;
    private static MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole _dummyInstance;
    private static bool _loggedFirstHide;
    private static bool _loggedFirstSuppression;
    private static bool _loggedFirstDummyInstance;

    internal static void Apply(Harmony harmony)
    {
        var targetReady = FindTarget(ReadyMethod);
        if (targetReady != null)
        {
            harmony.Patch(
                targetReady,
                finalizer: new HarmonyMethod(
                    typeof(DevConsolePatches).GetMethod(nameof(ReadyFinalizer), AllFlags)
                )
            );
            PatchHelper.Log("Patched NDevConsole._Ready finalizer");
        }

        var targetInstance = FindTarget(InstanceGetterMethod);
        if (targetInstance != null)
        {
            harmony.Patch(
                targetInstance,
                prefix: new HarmonyMethod(
                    typeof(DevConsolePatches).GetMethod(nameof(GetInstancePrefix), AllFlags)
                )
            );
            PatchHelper.Log("Patched NDevConsole.get_Instance prefix");
        }
    }

    private static MethodInfo FindTarget(string methodName)
    {
        var type = typeof(NGame).Assembly.GetType(TypeName);
        if (type == null)
        {
            PatchHelper.Log("DevConsolePatches: NDevConsole type not found; skipping");
            return null;
        }

        var method = type.GetMethod(methodName, AllFlags);
        if (method == null)
            PatchHelper.Log($"DevConsolePatches: NDevConsole.{methodName} method not found; skipping");

        return method;
    }

    private static bool GetInstancePrefix(ref MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole __result)
    {
        var type = typeof(MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole);
        if (_instanceField == null)
        {
            _instanceField = type.GetField("_instance", AllFlags);
        }

        var inst = _instanceField?.GetValue(null) as MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole;
        if (inst != null)
        {
            __result = inst;
            return false;
        }

        // On mobile platforms where NDevConsole was suppressed or never added to the tree,
        // callers like NHotkeyManager._UnhandledInput check NDevConsole.Instance.Visible.
        // Return a hidden dummy instance to avoid InvalidOperationException.
        if (_dummyInstance == null)
        {
            try
            {
                _dummyInstance = new MegaCrit.Sts2.Core.Nodes.Debug.NDevConsole { Visible = false };
                if (!_loggedFirstDummyInstance)
                {
                    _loggedFirstDummyInstance = true;
                    PatchHelper.Log("DevConsolePatches: created dummy hidden NDevConsole for safe Instance access");
                }
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"DevConsolePatches: failed to create dummy NDevConsole ({ex.Message})");
            }
        }

        __result = _dummyInstance;
        return false;
    }

    private static Exception ReadyFinalizer(object __instance, Exception __exception)
    {
        if (MobilePlatform.IsMobile)
            HideAndroidConsole(__instance);

        if (__exception == null)
            return null;

        if (!_loggedFirstSuppression)
        {
            _loggedFirstSuppression = true;
            PatchHelper.Log(
                $"DevConsolePatches: suppressed {__exception.GetType().Name} from NDevConsole._Ready "
                    + $"({__exception.Message}); further occurrences silenced"
            );
        }

        return null;
    }

    private static void HideAndroidConsole(object instance)
    {
        if (instance is not Node node || !GodotObject.IsInstanceValid(node))
            return;

        try
        {
            DisableConsoleNode(node);
            node.GetViewport()?.GuiReleaseFocus();

            if (!_loggedFirstHide)
            {
                _loggedFirstHide = true;
                PatchHelper.Log("DevConsolePatches: hid Android dev console and disabled console focus");
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log(
                $"DevConsolePatches: failed to hide Android dev console ({ex.GetType().Name}: {ex.Message})"
            );
        }
    }

    private static void DisableConsoleNode(Node node)
    {
        if (node is CanvasLayer canvasLayer)
            canvasLayer.Visible = false;

        if (node is CanvasItem canvasItem)
            canvasItem.Visible = false;

        if (node is Control control)
        {
            control.FocusMode = Control.FocusModeEnum.None;
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
            if (control.HasFocus())
                control.ReleaseFocus();
        }

        if (node is LineEdit lineEdit)
            lineEdit.Editable = false;

        foreach (var child in node.GetChildren())
        {
            if (child is Node childNode)
                DisableConsoleNode(childNode);
        }
    }
}
