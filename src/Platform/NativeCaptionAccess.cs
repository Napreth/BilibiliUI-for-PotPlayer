using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BilibiliPotPlayer
{
    // Initialize a private copy of the player's menu resource without displaying a popup.
    internal static class NativeCaptionAccess
    {
        internal static List<NativeMenuSnapshot.Item> Read(IntPtr player) { return Read(player, false); }
        internal static List<NativeMenuSnapshot.Item> ReadSources(IntPtr player) { return Read(player, true); }
        static List<NativeMenuSnapshot.Item> Read(IntPtr player, bool children)
        {
            List<NativeMenuSnapshot.Item> items = new List<NativeMenuSnapshot.Item>();
            uint pid; Native.GetWindowThreadProcessId(player, out pid);
            IntPtr module = IntPtr.Zero, root = IntPtr.Zero;
            try
            {
                string library;
                using (Process process = Process.GetProcessById((int)pid)) library = Path.Combine(Path.GetDirectoryName(process.MainModule.FileName), "PotPlayer64.dll");
                module = LoadLibraryEx(library, IntPtr.Zero, 0x22);
                if (module == IntPtr.Zero) return items;
                root = LoadMenu(module, (IntPtr)101);
                if (root == IntPtr.Zero) return items;
                IntPtr caption = Find(root, 10126, 0);
                if (caption == IntPtr.Zero) return items;
                Initialize(player, caption, 0);
                if (children) InitializeChildren(player, caption, 0);
                ReadMenu(caption, items, 0);
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            finally { if (root != IntPtr.Zero) DestroyMenu(root); if (module != IntPtr.Zero) FreeLibrary(module); }
            return items;
        }
        static IntPtr Find(IntPtr menu, int command, int depth)
        {
            if (depth > 6) return IntPtr.Zero;
            int count = Math.Min(512, GetMenuItemCount(menu));
            for (int i = 0; i < count; i++) if (GetMenuItemID(menu, i) == command) return menu;
            for (int i = 0; i < count; i++)
            { IntPtr sub = GetSubMenu(menu, i), found = sub == IntPtr.Zero ? IntPtr.Zero : Find(sub, command, depth + 1); if (found != IntPtr.Zero) return found; }
            return IntPtr.Zero;
        }
        static void Initialize(IntPtr player, IntPtr menu, int depth)
        {
            if (depth > 6) return;
            long unused; Native.TrySend(player, 0x117, unchecked((int)menu.ToInt64()), 0, out unused);
        }
        static void InitializeChildren(IntPtr player, IntPtr menu, int depth)
        {
            if (depth > 4) return;
            for (int i = 0, count = Math.Min(128, GetMenuItemCount(menu)); i < count; i++)
            {
                IntPtr sub = GetSubMenu(menu, i); if (sub == IntPtr.Zero) continue;
                long unused; Native.TrySend(player, 0x117, unchecked((int)sub.ToInt64()), i, out unused);
                InitializeChildren(player, sub, depth + 1);
            }
        }
        static void ReadMenu(IntPtr menu, List<NativeMenuSnapshot.Item> items, int depth)
        {
            if (depth > 6) return;
            for (int i = 0, count = Math.Min(512, GetMenuItemCount(menu)); i < count && items.Count < 2048; i++)
            {
                uint command = GetMenuItemID(menu, i), state = GetMenuState(menu, (uint)i, 0x400);
                StringBuilder label = new StringBuilder(1024); GetMenuString(menu, (uint)i, label, label.Capacity, 0x400);
                if (command != 0 && command != UInt32.MaxValue) items.Add(new NativeMenuSnapshot.Item { Command = (int)command, Text = label.ToString().Split('\t')[0].Replace("&", "").Trim(), Checked = (state & 8) != 0, Enabled = (state & 3) == 0 });
                IntPtr sub = GetSubMenu(menu, i); if (sub != IntPtr.Zero) ReadMenu(sub, items, depth + 1);
            }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadMenu(IntPtr module, IntPtr resource);
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr menu);
        [DllImport("user32.dll")] static extern uint GetMenuItemID(IntPtr menu, int index);
        [DllImport("user32.dll")] static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetSubMenu(IntPtr menu, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int length, uint flags);
    }
}
