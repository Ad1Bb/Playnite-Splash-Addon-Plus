using System;
using System.Runtime.InteropServices;

namespace SplashAddonPlus
{
    /// <summary>
    /// Reads Xbox-compatible (XInput) controllers and reports a fresh press of the B button
    /// (the right face button; Circle on PlayStation pads exposed as XInput by tools like DS4Windows).
    /// Read-only: XInput is shared, nothing is captured from Playnite or the game.
    /// </summary>
    internal sealed class GamepadWatcher
    {
        public const ushort ButtonB = 0x2000;
        private const int MaxControllers = 4;

        // Querying an empty slot is comparatively slow in XInput, so empty slots are re-checked only once per second.
        private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);

        private readonly Func<int, ushort?> _readButtons;
        private readonly Func<DateTime> _clock;
        private readonly bool[] _connected = new bool[MaxControllers];
        private readonly bool[] _bWasDown = new bool[MaxControllers];
        private DateTime _nextProbe = DateTime.MinValue;

        public GamepadWatcher()
            : this(XInput.ReadButtons, () => DateTime.Now)
        {
        }

        /// <param name="readButtons">Button mask of a controller slot, or null when nothing is connected there.</param>
        internal GamepadWatcher(Func<int, ushort?> readButtons, Func<DateTime> clock)
        {
            _readButtons = readButtons;
            _clock = clock;

            // Remember what is already held, so a button pressed before the splash appeared does not count.
            Poll(probeAll: true, acceptPress: false);
        }

        /// <summary>Index of the controller whose B button was just pressed, or -1.</summary>
        public int PollForBPress(bool acceptPress)
        {
            DateTime now = _clock();
            bool probeAll = now >= _nextProbe;
            if (probeAll)
            {
                _nextProbe = now + ProbeInterval;
            }

            return Poll(probeAll, acceptPress);
        }

        private int Poll(bool probeAll, bool acceptPress)
        {
            int pressedOn = -1;
            for (int i = 0; i < MaxControllers; i++)
            {
                if (!_connected[i] && !probeAll)
                {
                    continue;
                }

                ushort? buttons = _readButtons(i);
                if (!buttons.HasValue)
                {
                    _connected[i] = false;
                    _bWasDown[i] = false;
                    continue;
                }

                bool justConnected = !_connected[i];
                _connected[i] = true;

                bool bDown = (buttons.Value & ButtonB) != 0;
                if (bDown && !_bWasDown[i] && !justConnected && acceptPress && pressedOn < 0)
                {
                    pressedOn = i;
                }

                _bWasDown[i] = bDown;
            }

            return pressedOn;
        }

        private static class XInput
        {
            private const uint ErrorSuccess = 0;

            // 0 = xinput1_4 (Windows 8 and newer), 1 = xinput9_1_0 (older systems), 2 = XInput unavailable.
            private static int _library;

            [StructLayout(LayoutKind.Sequential)]
            private struct XINPUT_GAMEPAD
            {
                public ushort wButtons;
                public byte bLeftTrigger;
                public byte bRightTrigger;
                public short sThumbLX;
                public short sThumbLY;
                public short sThumbRX;
                public short sThumbRY;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct XINPUT_STATE
            {
                public uint dwPacketNumber;
                public XINPUT_GAMEPAD Gamepad;
            }

            [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
            private static extern uint GetState14(uint userIndex, out XINPUT_STATE state);

            [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
            private static extern uint GetState910(uint userIndex, out XINPUT_STATE state);

            public static ushort? ReadButtons(int slot)
            {
                while (_library < 2)
                {
                    try
                    {
                        XINPUT_STATE state;
                        uint result = _library == 0 ? GetState14((uint)slot, out state) : GetState910((uint)slot, out state);
                        return result == ErrorSuccess ? state.Gamepad.wButtons : (ushort?)null;
                    }
                    catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
                    {
                        _library++;
                    }
                }

                return null;
            }
        }
    }
}
