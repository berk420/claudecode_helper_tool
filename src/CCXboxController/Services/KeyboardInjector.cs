using System;
using System.Collections.Generic;
using CCXboxController.NativeMethods;

namespace CCXboxController.Services;

public static class KeyboardInjector
{
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_C = 0x43;

    public static void SendCopyShortcut()
    {
        var inputs = new List<Input>(4);
        AppendVkDown(inputs, VK_CONTROL);
        AppendVkDown(inputs, VK_C);
        AppendVkUp(inputs, VK_C);
        AppendVkUp(inputs, VK_CONTROL);
        var arr = inputs.ToArray();
        SendInputInterop.SendInput((uint)arr.Length, arr, System.Runtime.InteropServices.Marshal.SizeOf<Input>());
    }

    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var inputs = new List<Input>(text.Length * 2);
        foreach (char c in text)
        {
            if (c == '\n')
            {
                AppendVk(inputs, VK_RETURN);
                continue;
            }
            if (c == '\r') continue;
            AppendUnicodeChar(inputs, c);
        }

        if (inputs.Count == 0) return;
        var arr = inputs.ToArray();
        SendInputInterop.SendInput((uint)arr.Length, arr, System.Runtime.InteropServices.Marshal.SizeOf<Input>());
    }

    private static void AppendUnicodeChar(List<Input> list, char c)
    {
        // Surrogate pairs already handled correctly because each ushort is sent as-is.
        list.Add(new Input
        {
            type = SendInputInterop.INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KeyboardInput
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = SendInputInterop.KEYEVENTF_UNICODE,
                    time = 0,
                    dwExtraInfo = 0
                }
            }
        });
        list.Add(new Input
        {
            type = SendInputInterop.INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KeyboardInput
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = SendInputInterop.KEYEVENTF_UNICODE | SendInputInterop.KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = 0
                }
            }
        });
    }

    private static void AppendVk(List<Input> list, ushort vk)
    {
        AppendVkDown(list, vk);
        AppendVkUp(list, vk);
    }

    private static void AppendVkDown(List<Input> list, ushort vk)
    {
        list.Add(new Input
        {
            type = SendInputInterop.INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KeyboardInput { wVk = vk, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = 0 }
            }
        });
    }

    private static void AppendVkUp(List<Input> list, ushort vk)
    {
        list.Add(new Input
        {
            type = SendInputInterop.INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KeyboardInput { wVk = vk, wScan = 0, dwFlags = SendInputInterop.KEYEVENTF_KEYUP, time = 0, dwExtraInfo = 0 }
            }
        });
    }
}
