namespace Unreal99.Platform;

/// <summary>
/// Pure device-topology reconciliation shared by normal hot-plug handling and the headless test.
/// Raw Input handles are ephemeral, so an assignment is retained only while its handle still
/// exists; a reconnected device is matched by its stable display identity, then newly active
/// unclaimed mice fill any remaining automatic slots.
/// </summary>
public static class DeviceAssignment
{
    public static bool ReconcileMice(PlayerDevice[] players, IReadOnlyList<RawDevice> mice)
    {
        nint[] before = players.Select(p => p.MouseHandle).ToArray();
        string[] beforeNames = players.Select(p => p.MouseName).ToArray();
        var available = mice.Select(m => m.Handle).Where(h => h != 0).ToHashSet();
        var claimed = new HashSet<nint>();

        // A removed handle—or a duplicate left behind after Windows reused a handle—must stop
        // driving a player immediately. Keep the name so the same device can reclaim its slot.
        foreach (PlayerDevice player in players)
        {
            if (player.MouseHandle == 0) continue;
            if (!available.Contains(player.MouseHandle) || !claimed.Add(player.MouseHandle))
                player.MouseHandle = 0;
        }

        // First restore both manual and automatic assignments by identity. This makes unplugging
        // and reconnecting the same mouse preserve the player slot even when Windows changes its
        // raw handle.
        foreach (PlayerDevice player in players)
        {
            if (player.MouseHandle != 0 || string.IsNullOrWhiteSpace(player.MouseName)) continue;
            RawDevice match = mice.FirstOrDefault(m => !claimed.Contains(m.Handle)
                && string.Equals(m.Name, player.MouseName, StringComparison.Ordinal));
            if (match == null) continue;
            player.MouseHandle = match.Handle;
            claimed.Add(match.Handle);
        }

        // A replacement mouse has no saved identity. Once it actually moves, give it to the first
        // empty slot. A manually selected mouse gets first refusal by identity above, but must not
        // leave a player permanently on the shared cursor path when that old device is gone. That
        // path is especially unsafe in captured mode because cursor coordinates are not raw motion.
        foreach (PlayerDevice player in players)
        {
            if (player.MouseHandle != 0) continue;
            RawDevice match = mice.FirstOrDefault(m => m.SeenInput && !claimed.Contains(m.Handle));
            if (match == null) continue;
            player.MouseHandle = match.Handle;
            player.MouseName = match.Name;
            player.MouseAssignedManually = false;
            claimed.Add(match.Handle);
        }

        for (int i = 0; i < players.Length; i++)
            if (players[i].MouseHandle != before[i]
                || !string.Equals(players[i].MouseName, beforeNames[i], StringComparison.Ordinal))
                return true;
        return false;
    }

    public static int RunSelfTest()
    {
        PlayerDevice[] players = Enumerable.Range(0, 3).Select(PlayerDevice.Keyboard).ToArray();
        players[0].MouseHandle = 11;
        players[0].MouseName = "滑鼠 A";
        players[1].MouseHandle = 22;
        players[1].MouseName = "滑鼠 B";
        players[1].MouseAssignedManually = true;

        RawDevice[] afterReconnect =
        [
            new() { Handle = 33, Name = "滑鼠 B", IsMouse = true },
            new() { Handle = 44, Name = "滑鼠 C", IsMouse = true, SeenInput = true },
        ];
        bool changed = ReconcileMice(players, afterReconnect);
        bool pass = changed
            && players[0].MouseHandle == 44 && players[0].MouseName == "滑鼠 C"
            && players[1].MouseHandle == 33 && players[1].MouseName == "滑鼠 B"
            && players[2].MouseHandle == 0;

        // If the explicitly selected B later disappears, an active replacement must take over
        // rather than making that player consume Silk's captured cursor coordinates as look input.
        changed = ReconcileMice(players,
        [
            new() { Handle = 44, Name = "滑鼠 C", IsMouse = true },
            new() { Handle = 55, Name = "滑鼠 D", IsMouse = true, SeenInput = true },
        ]);
        pass &= changed && players[0].MouseHandle == 44
            && players[1].MouseHandle == 55 && players[1].MouseName == "滑鼠 D"
            && !players[1].MouseAssignedManually;

        // Removing both devices clears their ephemeral handles without losing the identities
        // needed to reclaim the same slots after a later arrival notification.
        changed = ReconcileMice(players, []);
        pass &= changed && players[0].MouseHandle == 0 && players[0].MouseName == "滑鼠 C"
            && players[1].MouseHandle == 0 && players[1].MouseName == "滑鼠 D";

        Console.WriteLine($"滑鼠熱插拔與自動重新指派: {(pass ? "通過" : "失敗")}");
        return pass ? 0 : 1;
    }
}
