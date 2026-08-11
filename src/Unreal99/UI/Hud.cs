using System.Numerics;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Platform;
using Unreal99.Rendering;

namespace Unreal99.UI;

/// <summary>
/// The in-game HUD. Every element scales with viewport height so a quarter-screen view stays
/// readable, and all text comes from <see cref="Loc"/>.
/// </summary>
public sealed class Hud
{
    public int FaceRegular;
    public int FaceBold;
    public Texture2D WeaponThumbnailAtlas;

    private static readonly uint White = UiRenderer.Rgba(1f, 1f, 1f);
    private static readonly uint Shadow = UiRenderer.Rgba(0f, 0f, 0f, 0.75f);
    private static readonly uint PanelBg = UiRenderer.Rgba(0.02f, 0.03f, 0.05f, 0.52f);
    private static readonly uint PanelEdge = UiRenderer.Rgba(0.55f, 0.72f, 0.95f, 0.28f);
    private static readonly Team[] FlagTeams = [Team.Red, Team.Blue];

    // A native 1920x1080 four-player game yields 960x540 quadrants. Treat those (and either
    // orientation of two-player split screen) as compact so objective and inventory bands never
    // fall back to the single-view coordinates and overlap.
    private static bool CompactLayout(int width, int height) => height <= 600 || width <= 1000;
    private static bool CompactBottomRow(int width, int height)
        => CompactLayout(width, height) && width >= 880;
    private static float CompactSidePanelWidth(int width, int height)
        => CompactBottomRow(width, height) ? MathF.Min(170f, width * 0.18f) : MathF.Min(242f, width * 0.43f);
    private static float LayoutFont(float requested) => MathF.Max(UiRenderer.MinimumTextSize, requested);

    private static string FitText(UiRenderer ui, int face, float size, string text, float maximumWidth)
    {
        if (string.IsNullOrEmpty(text) || maximumWidth <= 0f) return "";
        if (ui.MeasureText(face, size, text) <= maximumWidth) return text;
        const string ellipsis = "…";
        if (ui.MeasureText(face, size, ellipsis) > maximumWidth) return "";
        for (int length = text.Length - 1; length > 0; length--)
        {
            if (char.IsHighSurrogate(text[length - 1])) length--;
            string candidate = text[..Math.Max(0, length)] + ellipsis;
            if (ui.MeasureText(face, size, candidate) <= maximumWidth) return candidate;
        }
        return ellipsis;
    }

    public void Draw(UiRenderer ui, GameWorld world, Pawn pawn, PlayerController controller,
        in Camera camera, int width, int height, float dt, bool showDebug, string debugText)
    {
        float s = MathF.Max(height / 900f, 0.42f);      // uniform scale factor
        var mode = world.Mode;
        var feedback = world.FeedbackFor(pawn);
        Vector3 accent = mode.TeamBased ? GameTypes.TeamColor(pawn.Team) : pawn.AccentColor;

        DrawPlayerLabels(ui, world, pawn, camera, width, height, s);
        DrawCrosshair(ui, world, pawn, width, height, s);
        DrawDamageIndicators(ui, feedback, width, height, s);
        if (pawn.Alive) DrawCombatNumbers(ui, feedback, width, height, s);
        DrawHealthArmor(ui, pawn, width, height, s, accent);
        DrawAmmoWeapon(ui, pawn, width, height, s, accent);
        DrawWeaponInventory(ui, pawn, controller.Device.Bindings, width, height, s, accent);
        DrawPowerups(ui, pawn, width, height, s);
        DrawMatchStatus(ui, world, pawn, width, height, s, accent);
        DrawKillFeed(ui, world, width, height, s);
        DrawAnnouncements(ui, feedback, width, height, s);
        DrawObjective(ui, world, pawn, width, height, s);
        if (pawn.InVehicle) DrawVehicleStatus(ui, world, pawn, width, height, s);

        if (world.Frozen) DrawResumeCountdown(ui, world, width, height, s);
        if (!pawn.Alive) DrawDeathOverlay(ui, world, pawn, width, height, s);
        if (pawn.ZoomFov > 0f && pawn.Alive) DrawZoomOverlay(ui, width, height, s, accent);
        if (controller is { WantsScoreboard: true } || mode.IsOver) DrawScoreboard(ui, world, pawn, width, height, s);
        if (showDebug) DrawDebug(ui, debugText, width, height, s);
        _ = dt;
    }

    // ---------------------------------------------------------------- player identification

    private void DrawPlayerLabels(UiRenderer ui, GameWorld world, Pawn viewer, in Camera camera,
        int width, int height, float s)
    {
        // The death overlay owns the centre of compact split-screen views; suppress world labels
        // until respawn so names and health bars do not obscure the countdown and killer details.
        if (!viewer.Alive) return;
        const float labelHeight = 26f;
        float font = LayoutFont(22f * s);
        var occupied = new List<(float Left, float Top, float Right, float Bottom)>(world.Pawns.Count);
        foreach (Pawn player in world.Pawns)
        {
            if (!player.Alive || player.Id == viewer.Id) continue;

            Vector3 anchor = player.Position + new Vector3(0f, 2.35f, 0f);
            if (!camera.WorldToScreen(anchor, out Vector2 uv)
                || uv.X < 0.015f || uv.X > 0.985f || uv.Y < 0.015f || uv.Y > 0.985f)
                continue;

            // Keep normal labels honest: identify a player once their head is actually visible,
            // but do not reveal enemies through arena geometry.
            if (!world.Level.Collision.LineOfSight(camera.Position, anchor)) continue;

            float distance = Vector3.Distance(camera.Position, anchor);
            float alpha = 1f - MathX.Saturate((distance - 70f) / 45f) * 0.36f;
            if (player.IsInvisible) alpha *= 0.42f;
            Vector3 identity = world.Mode.TeamBased
                ? GameTypes.TeamColor(player.Team)
                : player.AccentColor;
            float centerX = uv.X * width;
            float headY = (1f - uv.Y) * height;
            bool flagCarrier = world.Mode.Kind == GameModeKind.CaptureTheFlag && player.HasFlag;
            float maximumNameWidth = MathF.Min(220f, width - 20f);
            string name = FitText(ui, FaceBold, font, player.Name, maximumNameWidth - 20f);
            float nameWidth = MathX.Clamp(ui.MeasureText(FaceBold, font, name) + 22f,
                92f, maximumNameWidth);
            float tagWidth = MathF.Max(nameWidth, 170f);
            float tagHeight = flagCarrier ? 87f : 56f;
            bool compact = CompactLayout(width, height);
            float safeTop = compact ? 72f : MathF.Max(82f, 96f * s);
            float safeBottom = compact ? height - 220f : height - MathF.Max(174f, 194f * s);
            float maximumTop = MathF.Max(safeTop, safeBottom - tagHeight);
            centerX = MathX.Clamp(centerX, tagWidth * 0.5f + 4f,
                width - tagWidth * 0.5f - 4f);
            float tagTop = MathX.Clamp(headY - tagHeight - 8f, safeTop, maximumTop);

            // Players can line up almost perfectly in a corridor. Stack their labels vertically
            // instead of drawing names and HP values on top of one another. Labels also stay out
            // of the match header above and the objective/inventory panels below.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float left = centerX - tagWidth * 0.5f;
                float right = centerX + tagWidth * 0.5f;
                bool intersects = occupied.Any(r => left < r.Right && right > r.Left
                    && tagTop < r.Bottom && tagTop + tagHeight > r.Top);
                if (!intersects) break;
                float upward = tagTop - tagHeight - 5f;
                tagTop = upward >= safeTop
                    ? upward
                    : MathF.Min(maximumTop, tagTop + tagHeight + 5f);
            }
            occupied.Add((centerX - tagWidth * 0.5f, tagTop,
                centerX + tagWidth * 0.5f, tagTop + tagHeight));
            float nameY = tagTop + (flagCarrier ? labelHeight + 5f : 0f);

            if (flagCarrier)
            {
                string flagText = Loc.FlagCarrierMarker(GameTypes.TeamName(player.CarriedFlag));
                float flagWidth = MathF.Min(width - 16f,
                    ui.MeasureText(FaceBold, font, flagText) + 20f);
                float flagY = tagTop;
                Vector3 flagColor = GameTypes.TeamColor(player.CarriedFlag);
                ui.ChamferRect(centerX - flagWidth * 0.5f, flagY, flagWidth, labelHeight, 5f,
                    UiRenderer.Rgba(flagColor * 0.20f, 0.88f * alpha));
                ui.TextOutline(FaceBold, font, centerX, flagY, flagText,
                    UiRenderer.Rgba(flagColor * 1.35f, alpha), Shadow, 2f,
                    TextAlign.Center);
            }

            ui.ChamferRect(centerX - nameWidth * 0.5f, nameY, nameWidth, labelHeight, 5f,
                UiRenderer.Rgba(0.015f, 0.025f, 0.05f, 0.78f * alpha));
            ui.Line(new Vector2(centerX - nameWidth * 0.5f + 5f, nameY),
                new Vector2(centerX + nameWidth * 0.5f - 5f, nameY), 2f,
                UiRenderer.Rgba(identity * 1.25f, alpha));
            ui.TextOutline(FaceBold, font, centerX, nameY, name,
                UiRenderer.Rgba(identity * 1.25f + new Vector3(0.18f), alpha),
                Shadow, 2f, TextAlign.Center);

            float health = MathF.Max(0f, player.Health);
            float healthFraction = MathX.Saturate(health / MathF.Max(1f, player.MaxHealth));
            Vector3 healthColor = healthFraction > 0.5f ? new Vector3(0.25f, 1f, 0.46f)
                : healthFraction > 0.25f ? new Vector3(1f, 0.82f, 0.20f)
                : new Vector3(1f, 0.20f, 0.16f);
            string hp = MathF.Ceiling(health).ToString("0");
            float hpWidth = ui.MeasureText(FaceBold, font, hp);
            const float barWidth = 104f;
            const float barHeight = 9f;
            float rowWidth = barWidth + 8f + hpWidth;
            float barX = centerX - rowWidth * 0.5f;
            float barY = nameY + labelHeight + 5f;
            ui.Rect(barX - 2f, barY - 2f, barWidth + 4f, barHeight + 4f,
                UiRenderer.Rgba(0f, 0f, 0f, 0.82f * alpha));
            ui.Rect(barX, barY, barWidth, barHeight,
                UiRenderer.Rgba(0.09f, 0.10f, 0.13f, 0.94f * alpha));
            ui.Rect(barX, barY, barWidth * healthFraction, barHeight,
                UiRenderer.Rgba(healthColor, 0.98f * alpha));
            ui.TextOutline(FaceBold, font, barX + barWidth + 8f, barY - 8f, hp,
                UiRenderer.Rgba(healthColor, alpha), Shadow, 2f);
        }
    }

    // ---------------------------------------------------------------- crosshair

    private void DrawCrosshair(UiRenderer ui, GameWorld world, Pawn pawn, int width, int height, float s)
    {
        if (!pawn.Alive) return;
        Vector2 c = new(width * 0.5f, height * 0.5f);
        var feedback = world.FeedbackFor(pawn);
        uint col = UiRenderer.Rgba(0.55f, 0.95f, 1f, 0.92f);

        float gap = 6f * s;
        float len = 10f * s;
        float thick = MathF.Max(1.5f, 2f * s);

        switch (pawn.Weapon)
        {
            case WeaponKind.SniperRifle:
                if (pawn.ZoomFov <= 0f)
                {
                    ui.Ring(c, 13f * s, thick, col, 28);
                    ui.Line(new Vector2(c.X - 22f * s, c.Y), new Vector2(c.X - 6f * s, c.Y), thick, col);
                    ui.Line(new Vector2(c.X + 6f * s, c.Y), new Vector2(c.X + 22f * s, c.Y), thick, col);
                    ui.Line(new Vector2(c.X, c.Y - 22f * s), new Vector2(c.X, c.Y - 6f * s), thick, col);
                    ui.Line(new Vector2(c.X, c.Y + 6f * s), new Vector2(c.X, c.Y + 22f * s), thick, col);
                }
                break;

            case WeaponKind.FlakCannon:
            case WeaponKind.Minigun:
                {
                    // Spread-aware: the reticle opens as the minigun spins up.
                    float spread = pawn.Weapon == WeaponKind.Minigun ? 4f + pawn.SpinUp * 9f : 9f;
                    float r = (gap + spread) * s;
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i / 4f * MathX.TwoPi + MathX.Pi / 4f;
                        Vector2 d = new(MathF.Cos(a), MathF.Sin(a));
                        ui.Line(c + d * r, c + d * (r + len), thick, col);
                    }
                    break;
                }

            case WeaponKind.RocketLauncher:
            case WeaponKind.Redeemer:
                ui.Ring(c, 11f * s, thick, col, 24);
                ui.Line(new Vector2(c.X - 18f * s, c.Y), new Vector2(c.X - 12f * s, c.Y), thick, col);
                ui.Line(new Vector2(c.X + 12f * s, c.Y), new Vector2(c.X + 18f * s, c.Y), thick, col);
                break;

            case WeaponKind.ShockRifle:
                ui.Ring(c, 9f * s, thick, col, 20);
                ui.Circle(c, 1.6f * s, col, 8);
                break;

            default:
                ui.Line(new Vector2(c.X - gap - len, c.Y), new Vector2(c.X - gap, c.Y), thick, col);
                ui.Line(new Vector2(c.X + gap, c.Y), new Vector2(c.X + gap + len, c.Y), thick, col);
                ui.Line(new Vector2(c.X, c.Y - gap - len), new Vector2(c.X, c.Y - gap), thick, col);
                ui.Line(new Vector2(c.X, c.Y + gap), new Vector2(c.X, c.Y + gap + len), thick, col);
                break;
        }

        // Hit marker.
        if (feedback.HitMarkerTimer > 0f)
        {
            float t = feedback.HitMarkerTimer / 0.22f;
            uint hitCol = feedback.HitMarkerLethal
                ? UiRenderer.Rgba(1f, 0.25f, 0.2f, t)
                : UiRenderer.Rgba(1f, 1f, 1f, t);
            float r0 = 8f * s, r1 = (16f + (1f - t) * 8f) * s;
            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * MathX.TwoPi + MathX.Pi / 4f;
                Vector2 d = new(MathF.Cos(a), MathF.Sin(a));
                ui.Line(c + d * r0, c + d * r1, 2.5f * s, hitCol);
            }
        }

        // Charge meter for chargeable weapons.
        if (pawn.ChargeTime > 0.02f)
        {
            var def = pawn.WeaponDef;
            float max = MathF.Max(def.Primary.Chargeable ? def.Primary.MaxCharge : def.Alt.MaxCharge, 0.01f);
            float f = MathX.Saturate(pawn.ChargeTime / max);
            ui.Ring(c, 26f * s, 3.5f * s, UiRenderer.Rgba(0.1f, 0.1f, 0.12f, 0.55f), 32);
            ui.Ring(c, 26f * s, 3.5f * s, UiRenderer.Rgba(1f, 0.55f + f * 0.4f, 0.15f, 0.95f), 32,
                -MathX.HalfPi, f * MathX.TwoPi);
        }
    }

    private void DrawDamageIndicators(UiRenderer ui, Feedback feedback, int width, int height, float s)
    {
        if (feedback.DamageDirectionTimer <= 0f) return;
        float alpha = MathX.Saturate(feedback.DamageDirectionTimer / 1.3f);
        Vector2 c = new(width * 0.5f, height * 0.5f);
        float angle = feedback.DamageDirection;
        // Screen space: forward is up, so rotate into the HUD's frame.
        Vector2 dir = new(MathF.Sin(angle), -MathF.Cos(angle));
        Vector2 side = new(-dir.Y, dir.X);
        float r = 105f * s;
        Vector2 tip = c + dir * r;
        uint col = UiRenderer.Rgba(1f, 0.15f, 0.1f, alpha * 0.85f);
        ui.Triangle(tip + dir * 22f * s, tip - side * 20f * s, tip + side * 20f * s, col);
    }

    private void DrawCombatNumbers(UiRenderer ui, Feedback feedback, int width, int height, float s)
    {
        int dealtLane = 0;
        int takenLane = 0;
        float font = LayoutFont(27f * s);
        foreach (DamageNumberEvent number in feedback.DamageNumbers)
        {
            if (number.Timer <= 0f || number.Duration <= 0f) continue;
            float progress = 1f - number.Timer / number.Duration;
            float alpha = MathX.Saturate(number.Timer / 0.32f);
            int lane = number.Dealt ? dealtLane++ : takenLane++;
            float x = width * 0.5f + (number.Dealt ? 92f : -92f) * MathF.Max(s, 0.72f);
            float y = height * 0.5f + (number.Dealt ? -62f : 34f) * MathF.Max(s, 0.72f)
                - progress * 54f - lane * 25f;
            int amount = Math.Max(1, (int)MathF.Round(number.Amount));
            string text = number.Dealt
                ? Loc.DamageDealtNumber(amount)
                : Loc.DamageTakenNumber(amount);
            Vector3 color = number.Dealt
                ? new Vector3(1f, 0.76f, 0.16f)
                : new Vector3(1f, 0.20f, 0.16f);
            ui.TextOutline(FaceBold, font, x, y, text, UiRenderer.Rgba(color, alpha),
                UiRenderer.Rgba(0f, 0f, 0f, 0.88f * alpha), 2.5f, TextAlign.Center);
        }
    }

    // ---------------------------------------------------------------- status panels

    private void DrawHealthArmor(UiRenderer ui, Pawn pawn, int width, int height, float s, Vector3 accent)
    {
        if (CompactLayout(width, height))
        {
            DrawCompactHealthArmor(ui, pawn, width, height, accent);
            return;
        }

        float pad = 22f * s;
        float panelW = 240f * s;
        float panelH = 92f * s;
        float x = pad;
        float y = height - pad - panelH;

        ui.ChamferRect(x, y, panelW, panelH, 12f * s, PanelBg);
        ui.Line(new Vector2(x + 12f * s, y), new Vector2(x + panelW - 12f * s, y), 1.6f * s, PanelEdge);
        string ownName = FitText(ui, FaceBold, LayoutFont(22f * s), pawn.Name, panelW - 12f * s);
        ui.TextOutline(FaceBold, LayoutFont(22f * s), x + 8f * s, y - 31f * s, ownName,
            UiRenderer.Rgba(accent * 1.2f + new Vector3(0.15f)), Shadow, 2f * s);

        // Health
        float health = MathF.Max(0f, pawn.Health);
        Vector3 healthColor = health > 100f ? new Vector3(0.35f, 1f, 0.85f)
                            : health > 50f ? new Vector3(0.35f, 1f, 0.5f)
                            : health > 25f ? new Vector3(1f, 0.85f, 0.25f)
                            : new Vector3(1f, 0.25f, 0.2f);

        ui.Text(FaceRegular, 17f * s, x + 16f * s, y + 8f * s, Loc.HudHealth,
            UiRenderer.Rgba(0.75f, 0.82f, 0.92f, 0.9f));
        ui.TextOutline(FaceBold, 46f * s, x + 16f * s, y + 27f * s, ((int)health).ToString(),
            UiRenderer.Rgba(healthColor), Shadow, 2f * s);

        // Armour
        ui.Text(FaceRegular, 17f * s, x + 132f * s, y + 8f * s, Loc.HudArmor,
            UiRenderer.Rgba(0.75f, 0.82f, 0.92f, 0.9f));
        Vector3 armorColor = pawn.HasShieldBelt ? new Vector3(1f, 0.45f, 1f) : new Vector3(1f, 0.72f, 0.25f);
        ui.TextOutline(FaceBold, 46f * s, x + 132f * s, y + 27f * s, ((int)pawn.Armor).ToString(),
            UiRenderer.Rgba(armorColor), Shadow, 2f * s);

        // Bars under the numbers.
        float barY = y + panelH - 9f * s;
        float barH = 4.5f * s;
        ui.Rect(x + 16f * s, barY, 100f * s, barH, UiRenderer.Rgba(0.08f, 0.09f, 0.12f, 0.8f));
        ui.Rect(x + 16f * s, barY, 100f * s * MathX.Saturate(health / MathF.Max(1f, pawn.MaxHealth)), barH,
            UiRenderer.Rgba(healthColor, 0.95f));
        ui.Rect(x + 132f * s, barY, 100f * s, barH, UiRenderer.Rgba(0.08f, 0.09f, 0.12f, 0.8f));
        ui.Rect(x + 132f * s, barY, 100f * s * MathX.Saturate(pawn.Armor / 150f), barH,
            UiRenderer.Rgba(armorColor, 0.95f));

    }

    private void DrawCompactHealthArmor(UiRenderer ui, Pawn pawn, int width, int height, Vector3 accent)
    {
        const float pad = 10f;
        bool bottomRow = CompactBottomRow(width, height);
        float panelW = CompactSidePanelWidth(width, height);
        float panelH = bottomRow ? 74f : 66f;
        float x = pad;
        float y = height - pad - panelH;
        float font = LayoutFont(22f);
        ui.ChamferRect(x, y, panelW, panelH, 7f, PanelBg);
        ui.Line(new Vector2(x + 7f, y), new Vector2(x + panelW - 7f, y), 1.5f, PanelEdge);
        string ownName = FitText(ui, FaceBold, font, pawn.Name, panelW - 18f);
        ui.Text(FaceBold, font, x + 9f, y + 3f, ownName,
            UiRenderer.Rgba(accent * 1.2f + new Vector3(0.15f), 0.98f));

        float health = MathF.Max(0f, pawn.Health);
        Vector3 healthColor = health > 100f ? new Vector3(0.35f, 1f, 0.85f)
            : health > 50f ? new Vector3(0.35f, 1f, 0.5f)
            : health > 25f ? new Vector3(1f, 0.85f, 0.25f)
            : new Vector3(1f, 0.25f, 0.2f);
        Vector3 armorColor = pawn.HasShieldBelt ? new Vector3(1f, 0.45f, 1f)
            : new Vector3(1f, 0.72f, 0.25f);

        if (bottomRow)
        {
            // Three short rows preserve full-sized text and three-digit values in the narrowed
            // side card. The thin bars remain visible beneath their corresponding value rows.
            const float valueLeft = 70f;
            ui.Text(FaceRegular, font, x + 9f, y + 25f, Loc.HudHealth,
                UiRenderer.Rgba(0.75f, 0.82f, 0.92f, 0.95f));
            ui.Text(FaceBold, font, x + panelW - 9f, y + 25f, ((int)health).ToString(),
                UiRenderer.Rgba(healthColor), TextAlign.Right);
            ui.Rect(x + valueLeft, y + 45f, panelW - valueLeft - 9f, 3f,
                UiRenderer.Rgba(0.08f, 0.09f, 0.12f, 0.85f));
            ui.Rect(x + valueLeft, y + 45f,
                (panelW - valueLeft - 9f) * MathX.Saturate(health / MathF.Max(1f, pawn.MaxHealth)), 3f,
                UiRenderer.Rgba(healthColor, 0.95f));
            ui.Text(FaceRegular, font, x + 9f, y + 48f, Loc.HudArmor,
                UiRenderer.Rgba(0.75f, 0.82f, 0.92f, 0.95f));
            ui.Text(FaceBold, font, x + panelW - 9f, y + 48f, ((int)pawn.Armor).ToString(),
                UiRenderer.Rgba(armorColor), TextAlign.Right);
            ui.Rect(x + valueLeft, y + 69f, panelW - valueLeft - 9f, 3f,
                UiRenderer.Rgba(0.08f, 0.09f, 0.12f, 0.85f));
            ui.Rect(x + valueLeft, y + 69f,
                (panelW - valueLeft - 9f) * MathX.Saturate(pawn.Armor / 150f), 3f,
                UiRenderer.Rgba(armorColor, 0.95f));
            return;
        }

        float split = x + panelW * 0.52f;
        ui.Text(FaceRegular, font, x + 9f, y + 30f, Loc.HudHealth,
            UiRenderer.Rgba(0.75f, 0.82f, 0.92f, 0.95f));
        ui.Text(FaceBold, font, split - 9f, y + 30f, ((int)health).ToString(),
            UiRenderer.Rgba(healthColor), TextAlign.Right);
        ui.Text(FaceRegular, font, split + 6f, y + 30f, Loc.HudArmor,
            UiRenderer.Rgba(0.75f, 0.82f, 0.92f, 0.95f));
        ui.Text(FaceBold, font, x + panelW - 9f, y + 30f, ((int)pawn.Armor).ToString(),
            UiRenderer.Rgba(armorColor), TextAlign.Right);
        float healthBarWidth = panelW * 0.43f;
        float armorBarWidth = panelW * 0.40f;
        ui.Rect(x + 9f, y + panelH - 5f, healthBarWidth, 3f,
            UiRenderer.Rgba(0.08f, 0.09f, 0.12f, 0.85f));
        ui.Rect(x + 9f, y + panelH - 5f,
            healthBarWidth * MathX.Saturate(health / MathF.Max(1f, pawn.MaxHealth)), 3f,
            UiRenderer.Rgba(healthColor, 0.95f));
        ui.Rect(split + 6f, y + panelH - 5f, armorBarWidth, 3f,
            UiRenderer.Rgba(0.08f, 0.09f, 0.12f, 0.85f));
        ui.Rect(split + 6f, y + panelH - 5f,
            armorBarWidth * MathX.Saturate(pawn.Armor / 150f), 3f,
            UiRenderer.Rgba(armorColor, 0.95f));
    }

    private void DrawAmmoWeapon(UiRenderer ui, Pawn pawn, int width, int height, float s, Vector3 accent)
    {
        if (CompactLayout(width, height))
        {
            DrawCompactAmmoWeapon(ui, pawn, width, height, accent);
            return;
        }

        float pad = 22f * s;
        float panelW = 250f * s;
        float panelH = 92f * s;
        float x = width - pad - panelW;
        float y = height - pad - panelH;

        ui.ChamferRect(x, y, panelW, panelH, 12f * s, PanelBg);
        ui.Line(new Vector2(x + 12f * s, y), new Vector2(x + panelW - 12f * s, y), 1.6f * s, PanelEdge);

        var def = pawn.WeaponDef;
        ui.Text(FaceRegular, 19f * s, x + panelW - 16f * s, y + 6f * s, def.Name,
            UiRenderer.Rgba(accent * 1.1f, 0.95f), TextAlign.Right);

        if (def.Ammo == AmmoKind.None)
        {
            ui.TextOutline(FaceBold, 44f * s, x + panelW - 16f * s, y + 30f * s, "∞",
                White, Shadow, 2f * s, TextAlign.Right);
        }
        else
        {
            int ammo = pawn.AmmoFor(pawn.Weapon);
            float frac = ammo / (float)MathF.Max(1, def.MaxAmmo);
            Vector3 ammoColor = frac < 0.12f ? new Vector3(1f, 0.25f, 0.2f)
                              : frac < 0.30f ? new Vector3(1f, 0.8f, 0.25f)
                              : new Vector3(0.92f, 0.95f, 1f);
            ui.TextOutline(FaceBold, 44f * s, x + panelW - 16f * s, y + 28f * s, ammo.ToString(),
                UiRenderer.Rgba(ammoColor), Shadow, 2f * s, TextAlign.Right);

            float barY = y + panelH - 9f * s;
            ui.Rect(x + 16f * s, barY, panelW - 32f * s, 4.5f * s, UiRenderer.Rgba(0.08f, 0.09f, 0.12f, 0.8f));
            ui.Rect(x + 16f * s, barY, (panelW - 32f * s) * MathX.Saturate(frac), 4.5f * s,
                UiRenderer.Rgba(ammoColor, 0.95f));
        }

    }

    private void DrawCompactAmmoWeapon(UiRenderer ui, Pawn pawn, int width, int height, Vector3 accent)
    {
        const float pad = 10f;
        float panelW = CompactSidePanelWidth(width, height);
        const float panelH = 40f;
        float x = width - pad - panelW;
        float y = height - pad - panelH;
        float font = LayoutFont(22f);
        var def = pawn.WeaponDef;
        string ammo = def.Ammo == AmmoKind.None ? "∞" : pawn.AmmoFor(pawn.Weapon).ToString();
        float ammoWidth = ui.MeasureText(FaceBold, font, ammo);
        string weapon = FitText(ui, FaceRegular, font, def.Name,
            panelW - ammoWidth - 32f);

        ui.ChamferRect(x, y, panelW, panelH, 7f, PanelBg);
        ui.Line(new Vector2(x + 7f, y), new Vector2(x + panelW - 7f, y), 1.5f, PanelEdge);
        ui.Text(FaceRegular, font, x + 9f, y + 5f, weapon,
            UiRenderer.Rgba(accent * 1.12f, 0.98f));
        ui.Text(FaceBold, font, x + panelW - 9f, y + 5f, ammo, White, TextAlign.Right);
        if (def.Ammo != AmmoKind.None)
        {
            float fraction = pawn.AmmoFor(pawn.Weapon) / (float)Math.Max(1, def.MaxAmmo);
            ui.Rect(x + 9f, y + panelH - 4f, (panelW - 18f) * MathX.Saturate(fraction), 2.5f,
                UiRenderer.Rgba(accent, 0.96f));
        }
    }

    /// <summary>
    /// Original-style persistent weapon inventory. Every slot uses the actual in-engine pickup
    /// model and communicates ownership, remaining ammo and that player's direct-select binding;
    /// the selected weapon gets a strong outline.
    /// </summary>
    private void DrawWeaponInventory(UiRenderer ui, Pawn pawn, BindingProfile bindings,
        int width, int height, float s, Vector3 accent)
    {
        bool compact = CompactLayout(width, height);
        bool bottomRow = CompactBottomRow(width, height);
        WeaponKind[] order = Weapons.CycleOrder;
        float gap = compact ? 3f : 4f * s;
        float available = bottomRow
            ? width - (CompactSidePanelWidth(width, height) + 18f) * 2f
            : compact ? width - 16f
            : width - MathF.Min(width * 0.38f, 560f * s);
        float cardWidth = MathX.Clamp(
            (available - gap * (order.Length - 1)) / order.Length,
            bottomRow ? 36f : compact ? 48f : 58f, compact ? 70f : 84f * s);
        float cardHeight = bottomRow ? 74f : compact ? 64f : MathF.Max(70f, 70f * s);
        float totalWidth = order.Length * cardWidth + (order.Length - 1) * gap;
        float startX = width * 0.5f - totalWidth * 0.5f;
        float y = bottomRow ? height - cardHeight - 10f
            : compact ? height - 151f : height - cardHeight - 10f * s;
        float font = LayoutFont(compact ? 22f : 18f * s);

        for (int i = 0; i < order.Length; i++)
        {
            WeaponKind weapon = order[i];
            WeaponDef def = Weapons.Get(weapon);
            bool owned = pawn.HasWeapon[(int)weapon];
            bool selected = weapon == pawn.Weapon;
            int ammo = def.Ammo == AmmoKind.None ? 999 : pawn.AmmoFor(weapon);
            bool usable = owned && (def.Ammo == AmmoKind.None || ammo > 0);
            float x = startX + i * (cardWidth + gap);
            Vector3 weaponColor = def.Tint * 0.72f + accent * 0.28f;
            float panelAlpha = selected ? 0.96f : owned ? 0.88f : 0.64f;
            ui.ChamferRect(x, y, cardWidth, cardHeight, 5f,
                UiRenderer.Rgba(weaponColor * (selected ? 0.34f : 0.14f), panelAlpha));

            if (WeaponThumbnailAtlas != null)
            {
                const float inset = 0.002f;
                int column = (int)weapon % Renderer.WeaponHudAtlasColumns;
                int row = (int)weapon / Renderer.WeaponHudAtlasColumns;
                // Framebuffer textures are bottom-up relative to the HUD's top-left coordinates.
                Vector2 uv0 = new(
                    column / (float)Renderer.WeaponHudAtlasColumns + inset,
                    (row + 1f) / Renderer.WeaponHudAtlasRows - inset);
                Vector2 uv1 = new(
                    (column + 1f) / Renderer.WeaponHudAtlasColumns - inset,
                    row / (float)Renderer.WeaponHudAtlasRows + inset);
                float imageAlpha = owned ? 1f : 0.42f;
                ui.Texture(WeaponThumbnailAtlas, x + 2f, y + 2f, cardWidth - 4f, cardHeight - 4f,
                    UiRenderer.Rgba(1f, 1f, 1f, imageAlpha), uv0, uv1);
                if (!owned)
                    ui.Rect(x + 2f, y + 2f, cardWidth - 4f, cardHeight - 4f,
                        UiRenderer.Rgba(0.02f, 0.025f, 0.04f, 0.30f));
            }

            ui.RectOutline(x, y, cardWidth, cardHeight, selected ? 3f : 1.3f,
                selected ? UiRenderer.Rgba(weaponColor * 1.35f, 1f)
                : UiRenderer.Rgba(owned ? weaponColor : new Vector3(0.3f), 0.66f));

            GameAction? action = DirectWeaponAction(weapon);
            string key = action.HasValue ? BindingNames.CompactControl(bindings[action.Value]) : "";
            if (!string.IsNullOrEmpty(key))
            {
                key = FitText(ui, FaceBold, font, key, cardWidth - 8f);
                float keyWidth = MathF.Min(cardWidth - 4f,
                    ui.MeasureText(FaceBold, font, key) + 8f);
                ui.ChamferRect(x + 2f, y + 2f, keyWidth, font + 4f, 3f,
                    UiRenderer.Rgba(0.01f, 0.015f, 0.025f, 0.78f));
                ui.Text(FaceBold, font, x + 6f, y + 2f, key,
                    UiRenderer.Rgba(owned ? new Vector3(0.92f, 0.97f, 1f) : new Vector3(0.46f),
                        owned ? 1f : 0.62f));
            }

            string ammoText = !owned ? "—" : def.Ammo == AmmoKind.None ? "∞" : ammo.ToString();
            Vector3 ammoColor = !usable ? new Vector3(1f, 0.22f, 0.18f)
                : def.Ammo != AmmoKind.None && ammo <= Math.Max(2, def.MaxAmmo / 8)
                    ? new Vector3(1f, 0.78f, 0.18f)
                    : new Vector3(0.90f, 0.96f, 1f);
            float ammoWidth = MathF.Min(cardWidth - 4f,
                ui.MeasureText(FaceBold, font, ammoText) + 8f);
            ui.ChamferRect(x + cardWidth - ammoWidth - 2f, y + cardHeight - font - 6f,
                ammoWidth, font + 4f, 3f, UiRenderer.Rgba(0.01f, 0.015f, 0.025f, 0.80f));
            ui.Text(FaceBold, font, x + cardWidth - 6f, y + cardHeight - font - 6f, ammoText,
                UiRenderer.Rgba(ammoColor, owned ? 1f : 0.50f), TextAlign.Right);
        }
    }

    private static GameAction? DirectWeaponAction(WeaponKind weapon)
    {
        int slot = (int)weapon;
        if (slot <= (int)WeaponKind.RocketLauncher) return GameAction.Weapon1 + slot;
        if (weapon == WeaponKind.Redeemer) return GameAction.Weapon10;
        return null; // Sniper rifle has no fixed slot in the original 1–9/0 scheme.
    }

    private void DrawPowerups(UiRenderer ui, Pawn pawn, int width, int height, float s)
    {
        bool compact = CompactLayout(width, height);
        float x = compact ? 10f : 24f * s;
        float y = height * 0.5f - 60f * s;
        float font = LayoutFont(15f * s);
        float rowH = MathF.Max(26f * s, font + 8f);
        float panelW = compact ? MathF.Min(220f, width * 0.38f) : 158f * s;

        void Row(string label, float remaining, Vector3 color)
        {
            float cardH = rowH - MathF.Max(3f, 4f * s);
            ui.ChamferRect(x, y, panelW, cardH, 6f * s, UiRenderer.Rgba(0f, 0f, 0f, 0.42f));
            ui.Rect(x, y, 4f * s, rowH - 4f * s, UiRenderer.Rgba(color, 0.95f));
            string duration = $"{(int)MathF.Ceiling(remaining)}";
            float durationWidth = ui.MeasureText(FaceBold, font, duration);
            string fitted = FitText(ui, FaceRegular, font, label,
                panelW - MathF.Max(24f, 12f * s) - durationWidth);
            ui.Text(FaceRegular, font, x + MathF.Max(9f, 12f * s), y + 3f * s, fitted,
                UiRenderer.Rgba(color * 1.2f, 0.95f));
            ui.Text(FaceBold, font, x + panelW - MathF.Max(8f, 10f * s), y + 3f * s, duration,
                White, TextAlign.Right);
            y += rowH;
        }

        if (pawn.HasDamageAmp) Row(Loc.PickupDamageAmp, pawn.DamageAmpTime, new Vector3(1f, 0.3f, 0.2f));
        if (pawn.IsInvisible) Row(Loc.PickupInvisibility, pawn.InvisibilityTime, new Vector3(0.6f, 0.8f, 1f));
        if (pawn.JumpBootCharges > 0)
            Row(Loc.PickupJumpBoots, pawn.JumpBootCharges, new Vector3(0.45f, 1f, 0.4f));
        if (pawn.HasShieldBelt)
            Row(Loc.PickupShieldBelt, pawn.Armor, new Vector3(1f, 0.4f, 1f));
        if (pawn.Breath < Physics.BreathSeconds - 0.5f)
            Row("氧氣", pawn.Breath, new Vector3(0.35f, 0.75f, 1f));
        _ = width;
    }

    private void DrawMatchStatus(UiRenderer ui, GameWorld world, Pawn pawn, int width, int height, float s,
        Vector3 accent)
    {
        if (CompactLayout(width, height))
        {
            DrawCompactMatchStatus(ui, world, pawn, width, accent);
            return;
        }

        var mode = world.Mode;
        float cx = width * 0.5f;
        float y = 12f * s;

        // Timer pill.
        string time = mode.TimeLimit > 0f ? Loc.TimeRemaining(mode.TimeRemaining) : "∞";
        float pillW = 116f * s, pillH = 34f * s;
        ui.ChamferRect(cx - pillW * 0.5f, y, pillW, pillH, 9f * s, PanelBg);
        ui.Text(FaceBold, 22f * s, cx, y + 5f * s, time, White, TextAlign.Center);

        if (mode.TeamBased)
        {
            float boxW = 92f * s;
            Vector3 red = GameTypes.TeamColor(Team.Red);
            Vector3 blue = GameTypes.TeamColor(Team.Blue);
            ui.ChamferRect(cx - pillW * 0.5f - boxW - 6f * s, y, boxW, pillH, 9f * s,
                UiRenderer.Rgba(red * 0.35f, 0.62f));
            ui.Text(FaceBold, 22f * s, cx - pillW * 0.5f - boxW * 0.5f - 6f * s, y + 5f * s,
                mode.TeamScore(Team.Red).ToString(), UiRenderer.Rgba(1f, 0.7f, 0.65f), TextAlign.Center);
            ui.ChamferRect(cx + pillW * 0.5f + 6f * s, y, boxW, pillH, 9f * s,
                UiRenderer.Rgba(blue * 0.35f, 0.62f));
            ui.Text(FaceBold, 22f * s, cx + pillW * 0.5f + boxW * 0.5f + 6f * s, y + 5f * s,
                mode.TeamScore(Team.Blue).ToString(), UiRenderer.Rgba(0.7f, 0.8f, 1f), TextAlign.Center);
        }
        else
        {
            var ranking = mode.Ranking(world);
            int myScore = mode.ScoreOf(pawn);
            int leadScore = ranking.Count > 0 ? mode.ScoreOf(ranking[0]) : 0;
            int rank = ranking.IndexOf(pawn) + 1;

            float boxW = 116f * s;
            ui.ChamferRect(cx - pillW * 0.5f - boxW - 6f * s, y, boxW, pillH, 9f * s, PanelBg);
            ui.Text(FaceRegular, 13f * s, cx - pillW * 0.5f - boxW + 10f * s - 6f * s, y + 3f * s,
                Loc.HudFrags, UiRenderer.Rgba(0.7f, 0.78f, 0.9f, 0.9f));
            ui.Text(FaceBold, 21f * s, cx - pillW * 0.5f - 16f * s, y + 4f * s,
                myScore.ToString(), UiRenderer.Rgba(accent * 1.15f), TextAlign.Right);

            ui.ChamferRect(cx + pillW * 0.5f + 6f * s, y, boxW, pillH, 9f * s, PanelBg);
            ui.Text(FaceRegular, 13f * s, cx + pillW * 0.5f + 16f * s, y + 3f * s,
                Loc.HudLeader, UiRenderer.Rgba(0.7f, 0.78f, 0.9f, 0.9f));
            ui.Text(FaceBold, 21f * s, cx + pillW * 0.5f + boxW - 10f * s, y + 4f * s,
                leadScore.ToString(), rank == 1 ? UiRenderer.Rgba(0.4f, 1f, 0.55f) : White, TextAlign.Right);
        }

        // Persistent match context. This must remain visible even for unlimited matches, where
        // the previous limit-only line disappeared and left players unable to identify the map
        // or mode without opening the scoreboard.
        string matchContext = $"{Loc.ModeName(mode.Kind)} · {world.Level.Name}";
        if (mode.LimitValue > 0) matchContext += $" · {mode.LimitValue}";
        ui.TextShadow(FaceRegular, 14f * s, cx, y + pillH + 3f * s, matchContext,
            UiRenderer.Rgba(0.78f, 0.84f, 0.94f, 0.94f), TextAlign.Center,
            shadowOffset: MathF.Max(1f, 1.5f * s), shadowAlpha: 0.9f);

        if (mode.TeamBased && pawn.Team != Team.None)
        {
            // Scores alone do not say which side this viewport belongs to—particularly in demo
            // mode and split screen. Keep a localized, team-coloured identity pill permanently
            // in the upper-right safe area, independent of transient objective notices.
            string teamLabel = $"{Loc.HudYourTeam}：{GameTypes.TeamName(pawn.Team)}";
            float fontSize = MathF.Max(18f * s, 12f);
            float teamH = MathF.Max(27f * s, 20f);
            float teamW = ui.MeasureText(FaceBold, fontSize, teamLabel) + MathF.Max(34f * s, 24f);
            float teamX = width - 22f * s - teamW;
            float teamY = y;
            uint teamColor = UiRenderer.Rgba(accent * 1.18f, 1f);
            ui.ChamferRect(teamX, teamY, teamW, teamH, 7f * s,
                UiRenderer.Rgba(accent * 0.27f, 0.76f));
            ui.Rect(teamX, teamY + 4f * s, MathF.Max(4f * s, 3f),
                teamH - 8f * s, teamColor);
            ui.TextShadow(FaceBold, fontSize, teamX + teamW * 0.5f,
                teamY + MathF.Max(2f * s, 1f), teamLabel,
                teamColor, TextAlign.Center, shadowOffset: MathF.Max(1f, 1.5f * s),
                shadowAlpha: 0.9f);
        }
    }

    private void DrawCompactMatchStatus(UiRenderer ui, GameWorld world, Pawn pawn, int width,
        Vector3 accent)
    {
        var mode = world.Mode;
        float cx = width * 0.5f;
        const float font = UiRenderer.MinimumTextSize;
        const float pillH = 30f;
        const float top = 5f;
        string time = mode.TimeLimit > 0f ? Loc.TimeRemaining(mode.TimeRemaining) : "∞";
        string score = mode.TeamBased
            ? $"{Loc.HudTeamRed} {mode.TeamScore(Team.Red)}  ·  {time}  ·  " +
              $"{mode.TeamScore(Team.Blue)} {Loc.HudTeamBlue}"
            : $"{Loc.HudFrags} {mode.ScoreOf(pawn)}  ·  {time}";
        score = FitText(ui, FaceBold, font, score, width - 28f);
        float scoreW = MathF.Min(width - 18f, ui.MeasureText(FaceBold, font, score) + 28f);
        ui.ChamferRect(cx - scoreW * 0.5f, top, scoreW, pillH, 7f, PanelBg);
        ui.Text(FaceBold, font, cx, top + 3f, score, White, TextAlign.Center);

        string context = $"{Loc.ModeName(mode.Kind)} · {world.Level.Name}";
        if (mode.LimitValue > 0) context += $" · {mode.LimitValue}";
        context = FitText(ui, FaceRegular, font, context, width - 24f);
        ui.TextShadow(FaceRegular, font, cx, top + 33f, context,
            UiRenderer.Rgba(0.80f, 0.86f, 0.96f, 0.98f), TextAlign.Center,
            shadowOffset: 1.5f, shadowAlpha: 0.9f);

        if (!mode.TeamBased || pawn.Team == Team.None) return;
        string label = $"{Loc.HudYourTeam}：{GameTypes.TeamName(pawn.Team)}";
        float teamW = MathF.Min(width - 24f, ui.MeasureText(FaceBold, font, label) + 34f);
        float teamX = width - 10f - teamW;
        const float teamY = top;
        ui.ChamferRect(teamX, teamY, teamW, pillH, 7f,
            UiRenderer.Rgba(accent * 0.27f, 0.78f));
        ui.Rect(teamX, teamY + 4f, 4f, pillH - 8f,
            UiRenderer.Rgba(accent * 1.18f, 1f));
        ui.TextShadow(FaceBold, font, teamX + teamW * 0.5f, teamY + 3f, label,
            UiRenderer.Rgba(accent * 1.18f, 1f), TextAlign.Center, 1.5f, 0.9f);
    }

    private void DrawKillFeed(UiRenderer ui, GameWorld world, int width, int height, float s)
    {
        bool compact = CompactLayout(width, height);
        float font = LayoutFont(16f * s);
        float x = width - (compact ? 10f : 22f * s);
        float y = compact ? 102f : MathF.Max(92f * s, font + 58f);
        float rowH = font + 8f;
        float maximumWidth = compact ? MathF.Min(width * 0.50f, 360f) : width - 44f * s;
        int rows = 0;
        int maximumRows = compact ? 3 : Math.Max(3, (int)((height * 0.42f - y) / rowH));
        for (int i = world.KillFeed.Count - 1; i >= 0; i--)
        {
            if (rows++ >= maximumRows) break;
            var entry = world.KillFeed[i];
            float alpha = MathX.Saturate(entry.Timer / 1.1f);
            string text = FitText(ui, FaceRegular, font, entry.Text,
                maximumWidth - MathF.Max(16f, 18f * s));
            float w = MathF.Min(maximumWidth,
                ui.MeasureText(FaceRegular, font, text) + MathF.Max(16f, 18f * s));
            ui.ChamferRect(x - w, y, w, rowH - 3f, 5f * s,
                UiRenderer.Rgba(0f, 0f, 0f, 0.50f * alpha));
            ui.Text(FaceRegular, font, x - MathF.Max(8f, 9f * s), y + 2f, text,
                UiRenderer.Rgba(entry.Color * 1.2f, alpha), TextAlign.Right);
            y += rowH;
        }
    }

    /// <summary>
    /// The hold after loading a save. Drawn straight from the world's timer rather than through
    /// the announcement feed, so a number is on screen for every frame of the countdown instead
    /// of only for the moment each second is called out.
    /// </summary>
    private void DrawResumeCountdown(UiRenderer ui, GameWorld world, int width, int height, float s)
    {
        float cx = width * 0.5f;
        ui.Rect(0f, 0f, width, height, UiRenderer.Rgba(0.01f, 0.02f, 0.05f, 0.34f));

        int second = Math.Max(1, (int)MathF.Ceiling(world.ResumeCountdown));
        // Each digit swells as its second begins, so the beat is readable without a clock.
        float within = world.ResumeCountdown - MathF.Floor(world.ResumeCountdown);
        float pop = MathX.Saturate((1f - within) * 3.2f);
        float size = MathX.Lerp(96f, 66f, MathX.SmoothStep(0f, 1f, pop)) * s;

        ui.Text(FaceBold, 24f * s, cx, height * 0.30f, Loc.SaveResuming,
            UiRenderer.Rgba(0.72f, 0.84f, 1f, 0.95f), TextAlign.Center);
        ui.TextOutline(FaceBold, size, cx, height * 0.36f, second.ToString(),
            UiRenderer.Rgba(1f, 0.80f, 0.28f), UiRenderer.Rgba(0f, 0f, 0f, 0.9f),
            4f * s, TextAlign.Center);
    }

    private void DrawAnnouncements(UiRenderer ui, Feedback feedback, int width, int height, float s)
    {
        float cx = width * 0.5f;

        if (feedback.BigTextTimer > 0f)
        {
            float t = feedback.BigTextTimer;
            float pop = MathX.Saturate((2.6f - t) * 5f);
            float size = MathX.Lerp(30f, 44f, MathX.SmoothStep(0f, 1f, pop)) * s;
            float alpha = MathX.Saturate(t * 2.2f);
            ui.TextOutline(FaceBold, size, cx, height * 0.24f, feedback.BigText,
                UiRenderer.Rgba(feedback.BigTextColor * 1.25f, alpha),
                UiRenderer.Rgba(0f, 0f, 0f, alpha * 0.85f), 3f * s, TextAlign.Center);
        }

        if (feedback.SubTextTimer > 0f)
        {
            float alpha = MathX.Saturate(feedback.SubTextTimer * 2.2f);
            ui.TextShadow(FaceBold, 22f * s, cx, height * 0.32f, feedback.SubText,
                UiRenderer.Rgba(1f, 0.85f, 0.35f, alpha), TextAlign.Center, 2f * s);
        }

        if (feedback.PickupTimer > 0f)
        {
            float alpha = MathX.Saturate(feedback.PickupTimer * 2.4f);
            ui.TextShadow(FaceRegular, 18f * s, cx, height * 0.70f, feedback.PickupText,
                UiRenderer.Rgba(0.75f, 0.95f, 1f, alpha), TextAlign.Center, 2f * s);
        }
    }

    /// <summary>
    /// One card per control point, named and coloured by who holds it. Domination is unreadable
    /// without this: the score moves on its own and a player needs to know which points are
    /// earning it and which one to go and take.
    /// </summary>
    private void DrawDominationPoints(UiRenderer ui, GameWorld world, Pawn pawn, int width, int height, float s)
    {
        var points = world.Level.ControlPoints;
        if (points.Count == 0) return;

        bool compact = CompactLayout(width, height);
        float font = LayoutFont(15f * s);
        float statusFont = LayoutFont(13f * s);
        float gap = compact ? 8f : 10f * s;
        float available = width - (compact ? 24f : 48f * s);
        float cardWidth = compact
            ? MathF.Min(196f, (available - (points.Count - 1) * gap) / points.Count)
            : 132f * s;
        float total = points.Count * cardWidth + (points.Count - 1) * gap;
        float x = width * 0.5f - total * 0.5f;
        float cardHeight = MathF.Max(52f, 48f * s);
        float y = CompactBottomRow(width, height) ? height - 151f
            : compact ? height - 216f : height - 152f * s;

        for (int i = 0; i < points.Count; i++)
        {
            Team owner = i < world.ControlPointOwners.Count ? world.ControlPointOwners[i] : Team.None;
            Vector3 col = owner == Team.None ? new Vector3(0.62f, 0.64f, 0.70f) : GameTypes.TeamColor(owner);
            // A freshly taken point pulses so the change registers without watching the feed.
            float since = i < world.ControlPointSince.Count ? world.ControlPointSince[i] : 99f;
            float pulse = since < 1.2f ? 0.62f + 0.38f * MathF.Cos(since * 18f) : 0.62f;

            ui.ChamferRect(x, y, cardWidth, cardHeight, 6f * s, UiRenderer.Rgba(col * 0.34f, pulse));
            if (owner == pawn.Team && owner != Team.None)
                ui.RectOutline(x, y, cardWidth, cardHeight, 1.6f * s, UiRenderer.Rgba(col, 0.85f));

            string pointName = FitText(ui, FaceBold, font, points[i].Name,
                cardWidth - MathF.Max(16f, 20f * s));
            string ownerName = owner == Team.None ? Loc.DomNeutral : GameTypes.TeamName(owner);
            ownerName = FitText(ui, FaceRegular, statusFont, ownerName,
                cardWidth - MathF.Max(16f, 20f * s));
            ui.Text(FaceBold, font, x + MathF.Max(8f, 10f * s), y + 3f, pointName,
                UiRenderer.Rgba(0.94f, 0.96f, 1f));
            ui.Text(FaceRegular, statusFont, x + MathF.Max(8f, 10f * s), y + 27f, ownerName,
                UiRenderer.Rgba(col * 1.25f, 0.98f));
            x += cardWidth + gap;
        }

        int mine = world.ControlPointsHeldBy(pawn.Team);
        string held = FitText(ui, FaceRegular, LayoutFont(14f * s), Loc.DomTeamHolds(mine), width - 24f);
        ui.Text(FaceRegular, LayoutFont(14f * s), width * 0.5f, y - 28f, held,
            UiRenderer.Rgba(0.72f, 0.80f, 0.92f), TextAlign.Center);
    }

    /// <summary>
    /// The Onslaught node chain. One card per node in map order, coloured by owner, with the
    /// reachable ones outlined — that outline is the whole strategic read of the mode, because a
    /// node you cannot reach is not a target no matter how close it is. Build progress and damage
    /// show as a bar across the bottom of the card.
    /// </summary>
    private void DrawOnslaughtNodes(UiRenderer ui, GameWorld world, Pawn pawn, int width, int height, float s)
    {
        var nodes = world.Onslaught.Nodes;
        if (nodes.Count == 0) return;

        bool compact = CompactLayout(width, height);
        float font = LayoutFont(14f * s);
        float statusFont = LayoutFont(12f * s);
        float gap = compact ? 6f : 8f * s;
        float available = width - (compact ? 24f : 48f * s);
        float cardWidth = MathF.Min(compact ? 150f : 118f * s,
            (available - (nodes.Count - 1) * gap) / nodes.Count);
        float total = nodes.Count * cardWidth + (nodes.Count - 1) * gap;
        float x = width * 0.5f - total * 0.5f;
        float cardHeight = MathF.Max(50f, 46f * s);
        float y = CompactBottomRow(width, height) ? height - 151f
            : compact ? height - 216f : height - 152f * s;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            Team visibleTeam = node.Team != Team.None ? node.Team : node.BuildingFor;
            Vector3 col = visibleTeam == Team.None
                ? new Vector3(0.62f, 0.64f, 0.70f) : GameTypes.TeamColor(visibleTeam);
            bool reachable = node.Team != pawn.Team && world.Onslaught.IsReachable(i, pawn.Team);
            if (node.IsCore && node.Team != pawn.Team) reachable = world.Onslaught.CoreVulnerable(node.Team);

            ui.ChamferRect(x, y, cardWidth, cardHeight, 6f * s, UiRenderer.Rgba(col * 0.34f, 0.62f));
            if (reachable)
                ui.RectOutline(x, y, cardWidth, cardHeight, 1.8f * s,
                    UiRenderer.Rgba(new Vector3(1f, 0.82f, 0.35f), 0.95f));
            else if (node.Team == pawn.Team)
                ui.RectOutline(x, y, cardWidth, cardHeight, 1.4f * s, UiRenderer.Rgba(col, 0.7f));

            string name = FitText(ui, FaceBold, font, node.Name, cardWidth - MathF.Max(14f, 18f * s));
            ui.Text(FaceBold, font, x + MathF.Max(7f, 9f * s), y + 2f, name,
                UiRenderer.Rgba(0.94f, 0.96f, 1f));

            string status = node.IsCore || node.IsActive
                ? (node.Team == Team.None ? Loc.DomNeutral : GameTypes.TeamName(node.Team))
                : $"{(int)(node.Built * 100f)}%";
            // A running auxiliary clock replaces the owner line: the seconds left are the only
            // thing anyone cares about while a Leviathan or a prime-node strike is counting down.
            if (node.HasCountdown && node.CountdownRemaining >= 0f)
                status = $"{status} · {(int)MathF.Ceiling(node.CountdownRemaining)}s";
            else if (node.IsAuxiliary && !node.IsActive)
                status = $"{status} · {Loc.WarSupportNode}";
            ui.Text(FaceRegular, statusFont, x + MathF.Max(7f, 9f * s), y + 24f, status,
                UiRenderer.Rgba(col * 1.25f, 0.98f));
            // An orb standing guard is why a node cannot be shot; say so rather than letting the
            // player fire into it and conclude the game is broken.
            if (node.OrbShield != Team.None)
                ui.RectOutline(x - 2f, y - 2f, cardWidth + 4f, cardHeight + 4f, 2.2f * s,
                    UiRenderer.Rgba(GameTypes.TeamColor(node.OrbShield) * 1.6f, 0.95f));

            // Health while it is standing, build progress while it is going up.
            float fill = node.IsActive
                ? (node.MaxHealth > 0f ? node.Health / node.MaxHealth : 1f)
                : node.Built;
            ui.ChamferRect(x + 6f, y + cardHeight - 8f, (cardWidth - 12f) * MathX.Saturate(fill), 3f * s, 1f,
                UiRenderer.Rgba(col * 1.4f, 0.9f));
            x += cardWidth + gap;
        }

        // The one line that decides what a player should be doing right now.
        bool ourCoreExposed = world.Onslaught.CoreVulnerable(pawn.Team);
        Team enemy = pawn.Team == Team.Red ? Team.Blue : Team.Red;
        bool theirCoreExposed = world.Onslaught.CoreVulnerable(enemy);
        // In Warfare the orb outranks the core state: who is holding it decides the next minute.
        WarfareOrb ourOrb = world.Mode.Kind == GameModeKind.Warfare
            ? world.Warfare.OrbOf(pawn.Team) : null;
        WarfareOrb theirOrb = world.Mode.Kind == GameModeKind.Warfare
            ? world.Warfare.OrbOf(enemy) : null;
        Pawn ourCarrier = ourOrb != null ? world.FindPawn(ourOrb.CarrierId) : null;

        string headline = world.Mode.State == MatchState.Overtime ? Loc.OnsCoreDrain
            : theirOrb is { Held: true } && world.FindPawn(theirOrb.CarrierId) is { } them
                ? Loc.WarOrbCarrier(them.Name)
            : ourCoreExposed ? Loc.OnsOurCoreExposed
            : ourCarrier != null ? Loc.WarOrbCarrier(ourCarrier.Name)
            : ourOrb is { Dropped: true } ? Loc.WarOrbDropped
            : theirCoreExposed ? Loc.OnsEnemyCoreExposed
            : Loc.OnsCoreShielded;
        Vector3 headlineCol = world.Mode.State == MatchState.Overtime ? new Vector3(1f, 0.55f, 0.25f)
            : theirOrb is { Held: true } ? GameTypes.TeamColor(enemy) * 1.3f
            : ourCoreExposed ? new Vector3(1f, 0.45f, 0.3f)
            : ourCarrier != null ? GameTypes.TeamColor(pawn.Team) * 1.3f
            : theirCoreExposed ? new Vector3(0.5f, 1f, 0.55f)
            : new Vector3(0.72f, 0.80f, 0.92f);
        ui.Text(FaceRegular, LayoutFont(14f * s), width * 0.5f, y - 28f,
            FitText(ui, FaceRegular, LayoutFont(14f * s), headline, width - 24f),
            UiRenderer.Rgba(headlineCol, 0.95f), TextAlign.Center);
    }

    /// <summary>
    /// The Assault objective card: what is live now, how far along it is, whether the viewer is
    /// attacking or defending, and the time to beat in round two. Nothing else on screen tells a
    /// player which of the two very different jobs they currently have.
    /// </summary>
    private void DrawAssaultObjective(UiRenderer ui, GameWorld world, Pawn pawn, int width, int height, float s)
    {
        var state = world.Assault;
        if (state.Objectives.Count == 0) return;

        bool compact = CompactLayout(width, height);
        bool sideLayout = CompactBottomRow(width, height);
        bool attacking = pawn.Team == state.Attackers;
        float titleFont = LayoutFont(16f * s);
        float statusFont = LayoutFont(13f * s);
        float cardWidth = sideLayout ? CompactSidePanelWidth(width, height)
            : compact ? MathF.Min(300f, width - 24f) : 340f * s;
        // In a quarter-screen viewport the target-to-beat gets its own line. Keeping it inside
        // the same status line is what made the two strings overlap at 1920x1080 four-player.
        float cardHeight = sideLayout && state.Round == 2
            ? 92f : MathF.Max(72f, 66f * s);
        float x = sideLayout
            ? attacking ? 10f : width - 10f - cardWidth
            : width * 0.5f - cardWidth * 0.5f;
        // Anchor the objective immediately above the matching bottom-side status card: attack
        // on the left, defence on the right. This clears the central sightline and weapon strip
        // in both four-player quadrants and the two split-screen orientations.
        float y = sideLayout ? height - 101f - cardHeight
            : compact ? height - 238f : height - 174f * s;

        Vector3 col = attacking ? new Vector3(1f, 0.72f, 0.28f) : new Vector3(0.42f, 0.72f, 1f);

        ui.ChamferRect(x, y, cardWidth, cardHeight, 7f * s, UiRenderer.Rgba(col * 0.30f, 0.66f));
        ui.RectOutline(x, y, cardWidth, cardHeight, 1.6f * s, UiRenderer.Rgba(col, 0.85f));

        var objective = state.CurrentObjective;
        string title = objective?.Name ?? Loc.AsObjectivesCleared;
        ui.Text(FaceBold, titleFont, x + MathF.Max(10f, 12f * s), y + 4f,
            FitText(ui, FaceBold, titleFont, title, cardWidth - 24f), UiRenderer.Rgba(0.95f, 0.97f, 1f));

        // Round, role, and progress through the sequence.
        string role = attacking ? Loc.AsAttacking : Loc.AsDefending;
        string round = state.Round == 1 ? Loc.AsRoundOne : Loc.AsRoundTwo;
        string counter = $"{state.CompletedCount}/{state.Objectives.Count}";
        string status = $"{round}　{role}　{counter}";
        float textInset = MathF.Max(10f, 12f * s);
        if (sideLayout)
        {
            // Keep the numeric progress visible even when a localized round/role phrase is long.
            // Ellipsizing the combined line previously hid the most useful part (for example 1/7).
            float counterWidth = ui.MeasureText(FaceBold, statusFont, counter);
            string compactStatus = FitText(ui, FaceRegular, statusFont, $"{round}·{role}",
                cardWidth - textInset * 2f - counterWidth - 8f);
            ui.Text(FaceRegular, statusFont, x + textInset, y + 28f, compactStatus,
                UiRenderer.Rgba(col * 1.2f, 0.98f));
            ui.Text(FaceBold, statusFont, x + cardWidth - textInset, y + 28f, counter,
                UiRenderer.Rgba(col * 1.2f, 0.98f), TextAlign.Right);
        }
        else
            ui.Text(FaceRegular, statusFont, x + textInset, y + 28f,
                FitText(ui, FaceRegular, statusFont, status, cardWidth - 20f),
                UiRenderer.Rgba(col * 1.2f, 0.98f));

        // Round two runs against a clock, so the target is the most important number on screen.
        if (state.Round == 2)
        {
            string target = state.TargetTime < float.MaxValue
                ? $"{Loc.AsTargetTime} {(int)(state.TargetTime / 60f):0}:{(int)(state.TargetTime % 60f):00}"
                : $"{Loc.AsTargetTime} {Loc.AsNoTarget}";
            if (sideLayout)
                ui.Text(FaceRegular, statusFont, x + MathF.Max(10f, 12f * s), y + 48f,
                    FitText(ui, FaceRegular, statusFont, target, cardWidth - 20f),
                    UiRenderer.Rgba(0.92f, 0.86f, 0.62f, 0.95f));
            else
                ui.Text(FaceRegular, statusFont, x + cardWidth - MathF.Max(10f, 12f * s), y + 28f,
                    target, UiRenderer.Rgba(0.92f, 0.86f, 0.62f, 0.95f), TextAlign.Right);
        }

        if (objective != null)
            ui.ChamferRect(x + 8f, y + cardHeight - 10f, (cardWidth - 16f) * MathX.Saturate(objective.Progress),
                4f * s, 1f, UiRenderer.Rgba(col * 1.5f, 0.95f));
    }

    /// <summary>
    /// What the crew needs while mounted: which vehicle, which seat, and the hull integrity —
    /// none of which the ordinary health/armour readout describes, because that is still the
    /// occupant's own body and is not what is about to explode.
    /// </summary>
    private void DrawVehicleStatus(UiRenderer ui, GameWorld world, Pawn pawn, int width, int height, float s)
    {
        var v = world.FindVehicle(pawn.VehicleId);
        if (v == null || !v.Alive) return;
        var def = v.Def;
        int seat = MathX.Clamp(pawn.VehicleSeat, 0, def.Seats.Length - 1);

        bool compact = CompactLayout(width, height);
        float cardWidth = compact ? 168f : 210f * s;
        float cardHeight = MathF.Max(58f, 54f * s);
        float x = width * 0.5f - cardWidth * 0.5f;
        // Clear of the objective cards below it: the Onslaught chain headline sits at -180s.
        float y = compact ? height - 300f : height - 252f * s;

        float integrity = def.Health > 0f ? MathX.Saturate(v.Health / def.Health) : 1f;
        // Red below a third: at that point the right decision is usually to get out.
        Vector3 col = integrity > 0.6f ? new Vector3(0.55f, 0.85f, 1f)
            : integrity > 0.33f ? new Vector3(1f, 0.82f, 0.35f)
            : new Vector3(1f, 0.38f, 0.28f);

        ui.ChamferRect(x, y, cardWidth, cardHeight, 6f * s, UiRenderer.Rgba(col * 0.28f, 0.62f));
        ui.RectOutline(x, y, cardWidth, cardHeight, 1.4f * s, UiRenderer.Rgba(col, 0.75f));

        float font = LayoutFont(15f * s);
        float statusFont = LayoutFont(12f * s);
        ui.Text(FaceBold, font, x + MathF.Max(9f, 11f * s), y + 3f,
            FitText(ui, FaceBold, font, def.Name, cardWidth - 22f),
            UiRenderer.Rgba(0.95f, 0.97f, 1f));
        ui.Text(FaceRegular, statusFont, x + MathF.Max(9f, 11f * s), y + 25f,
            $"{def.Seats[seat].Role}　{(int)MathF.Ceiling(v.Health)}",
            UiRenderer.Rgba(col * 1.2f, 0.98f));

        // The Paladin's shield and the Leviathan's deploy are states you cannot see from inside.
        if (def.HasShield && v.ShieldUp && v.ShieldHealth > 0f)
            ui.Text(FaceRegular, statusFont, x + cardWidth - MathF.Max(9f, 11f * s), y + 25f,
                Loc.VehShieldUp, UiRenderer.Rgba(0.5f, 0.85f, 1f, 0.95f), TextAlign.Right);
        else if (def.CanDeploy && v.Deploy > 0f)
            ui.Text(FaceRegular, statusFont, x + cardWidth - MathF.Max(9f, 11f * s), y + 25f,
                v.Deploy >= 1f ? Loc.VehDeployed : Loc.VehDeploying,
                UiRenderer.Rgba(1f, 0.8f, 0.4f, 0.95f), TextAlign.Right);

        ui.ChamferRect(x + 7f, y + cardHeight - 9f, (cardWidth - 14f) * integrity, 3.5f * s, 1f,
            UiRenderer.Rgba(col * 1.4f, 0.95f));
    }

    private void DrawObjective(UiRenderer ui, GameWorld world, Pawn pawn, int width, int height, float s)
    {
        if (world.Mode.Kind == GameModeKind.Domination)
        {
            DrawDominationPoints(ui, world, pawn, width, height, s);
            return;
        }
        if (world.NodeNetworkMode)
        {
            DrawOnslaughtNodes(ui, world, pawn, width, height, s);
            return;
        }
        if (world.Mode.Kind == GameModeKind.Assault)
        {
            DrawAssaultObjective(ui, world, pawn, width, height, s);
            return;
        }
        if (world.Mode.Kind != GameModeKind.CaptureTheFlag) return;

        bool compact = CompactLayout(width, height);
        float titleFont = LayoutFont(15f * s);
        float statusFont = LayoutFont(13f * s);
        // Align each flag card exactly with the status card below it: health/armour on the
        // left, current weapon/ammo on the right. Compact bottom rows use the shared 170 px
        // width; full-size HUDs retain their intentionally different 240/250 scale widths.
        float leftCardWidth = compact ? CompactSidePanelWidth(width, height) : 240f * s;
        float rightCardWidth = compact ? CompactSidePanelWidth(width, height) : 250f * s;
        float cardHeight = MathF.Max(54f, 48f * s);
        float sideInset = compact ? 10f : 22f * s;
        float y = CompactBottomRow(width, height) ? height - 151f
            : compact ? height - 216f : height - 152f * s;
        foreach (Team team in FlagTeams)
        {
            if (!world.FlagHome.TryGetValue(team, out Vector3 flagHome)) continue;
            // Put each team's state on its own side of the viewport. Besides matching the
            // red-left/blue-right score language, the open middle leaves the combat view and
            // flag-carrier message unobstructed in every split-screen arrangement.
            float cardWidth = team == Team.Red ? leftCardWidth : rightCardWidth;
            float x = team == Team.Red
                ? sideInset
                : width - sideInset - cardWidth;
            Vector3 col = GameTypes.TeamColor(team);
            int carrier = world.FlagCarrier.TryGetValue(team, out int c) ? c : -1;
            bool home = carrier < 0 && Vector3.Distance(world.FlagPosition[team], flagHome) < 0.4f;
            Pawn carrierPawn = carrier >= 0 ? world.FindPawn(carrier) : null;
            string status = carrierPawn != null
                ? Loc.FlagHeldBy(carrierPawn.Name)
                : carrier >= 0 ? Loc.HudFlagTaken
                : home ? Loc.HudFlagAtBase : Loc.HudFlagDropped;
            Vector3 statusColor = carrier >= 0 ? new Vector3(1f, 0.76f, 0.25f)
                : home ? new Vector3(0.48f, 1f, 0.58f) : new Vector3(1f, 0.48f, 0.25f);

            ui.ChamferRect(x, y, cardWidth, cardHeight, 6f * s, UiRenderer.Rgba(col * 0.3f, 0.62f));
            string title = FitText(ui, FaceBold, titleFont,
                $"{GameTypes.TeamName(team)}旗幟", cardWidth - MathF.Max(16f, 20f * s));
            status = FitText(ui, FaceRegular, statusFont, status,
                cardWidth - MathF.Max(16f, 20f * s));
            ui.Text(FaceBold, titleFont, x + MathF.Max(8f, 10f * s), y + 3f, title,
                UiRenderer.Rgba(col * 1.3f, 0.95f));
            ui.Text(FaceRegular, statusFont, x + MathF.Max(8f, 10f * s), y + 28f, status,
                UiRenderer.Rgba(statusColor, 0.98f));
        }

        if (pawn.HasFlag)
        {
            string held = FitText(ui, FaceBold, LayoutFont(22f * s),
                Loc.YouHoldFlag(GameTypes.TeamName(pawn.CarriedFlag)), width - 24f);
            ui.TextShadow(FaceBold, LayoutFont(22f * s), width * 0.5f, y - 30f, held,
                UiRenderer.Rgba(GameTypes.TeamColor(pawn.CarriedFlag) * 1.3f, 1f), TextAlign.Center, 2f * s);
        }
    }

    private void DrawDeathOverlay(UiRenderer ui, GameWorld world, Pawn pawn, int width, int height, float s)
    {
        ui.GradientRect(0, 0, width, height,
            UiRenderer.Rgba(0.22f, 0.01f, 0.01f, 0.10f), UiRenderer.Rgba(0.22f, 0.01f, 0.01f, 0.26f));
        ui.TextOutline(FaceBold, 42f * s, width * 0.5f, height * 0.40f, Loc.HudYouAreDead,
            UiRenderer.Rgba(1f, 0.3f, 0.25f), Shadow, 3f * s, TextAlign.Center);

        int lives = world.Mode.LivesFor(pawn);
        if (lives == 0)
        {
            ui.TextShadow(FaceRegular, 22f * s, width * 0.5f, height * 0.49f, Loc.HudSpectating,
                UiRenderer.Rgba(0.85f, 0.85f, 0.9f, 0.95f), TextAlign.Center, 2f * s);
        }
        else if (pawn.RespawnTimer > 0f)
        {
            ui.TextShadow(FaceRegular, 24f * s, width * 0.5f, height * 0.49f,
                $"{Loc.HudRespawnIn} {MathF.Ceiling(pawn.RespawnTimer):0}",
                UiRenderer.Rgba(0.9f, 0.92f, 1f, 0.95f), TextAlign.Center, 2f * s);
        }

        var killer = world.FindPawn(pawn.LastAttackerId);
        if (killer != null && killer != pawn)
            ui.TextShadow(FaceRegular, 19f * s, width * 0.5f, height * 0.55f,
                $"{killer.Name}  {(int)MathF.Max(0, killer.Health)} / {(int)killer.Armor}",
                UiRenderer.Rgba(1f, 0.7f, 0.6f, 0.9f), TextAlign.Center, 2f * s);
    }

    private void DrawZoomOverlay(UiRenderer ui, int width, int height, float s, Vector3 accent)
    {
        // Vignette the corners with solid bars so the scope reads as an optic.
        float r = MathF.Min(width, height) * 0.42f;
        Vector2 c = new(width * 0.5f, height * 0.5f);
        uint dark = UiRenderer.Rgba(0f, 0f, 0f, 0.92f);
        ui.Rect(0, 0, c.X - r, height, dark);
        ui.Rect(c.X + r, 0, width - (c.X + r), height, dark);
        ui.Rect(c.X - r, 0, r * 2f, c.Y - r, dark);
        ui.Rect(c.X - r, c.Y + r, r * 2f, height - (c.Y + r), dark);

        ui.Ring(c, r, 3f * s, UiRenderer.Rgba(0.05f, 0.06f, 0.08f, 0.95f), 64);
        ui.Ring(c, r - 4f * s, 1.4f * s, UiRenderer.Rgba(accent, 0.55f), 64);

        uint line = UiRenderer.Rgba(0.6f, 0.95f, 1f, 0.75f);
        ui.Line(new Vector2(c.X - r, c.Y), new Vector2(c.X - 14f * s, c.Y), 1.4f * s, line);
        ui.Line(new Vector2(c.X + 14f * s, c.Y), new Vector2(c.X + r, c.Y), 1.4f * s, line);
        ui.Line(new Vector2(c.X, c.Y - r), new Vector2(c.X, c.Y - 14f * s), 1.4f * s, line);
        ui.Line(new Vector2(c.X, c.Y + 14f * s), new Vector2(c.X, c.Y + r), 1.4f * s, line);
        for (int i = 1; i <= 6; i++)
        {
            float t = i / 7f * r;
            float w = (i % 2 == 0 ? 10f : 5f) * s;
            ui.Line(new Vector2(c.X - w, c.Y + t), new Vector2(c.X + w, c.Y + t), 1.2f * s, line);
        }
        ui.Circle(c, 2f * s, UiRenderer.Rgba(1f, 0.3f, 0.25f, 0.95f), 10);
    }

    // ---------------------------------------------------------------- scoreboard

    private void DrawScoreboard(UiRenderer ui, GameWorld world, Pawn viewer, int width, int height, float s)
    {
        var mode = world.Mode;
        var ranking = mode.Ranking(world);

        float w = MathF.Min(width * 0.88f, 720f * s);
        float rowH = 27f * s;
        float headerH = 76f * s;
        float h = headerH + rowH * (ranking.Count + 1) + 16f * s;
        float x = (width - w) * 0.5f;
        float y = MathF.Max(16f * s, (height - h) * 0.5f);

        ui.Rect(0, 0, width, height, UiRenderer.Rgba(0f, 0f, 0f, 0.55f));
        ui.ChamferRect(x, y, w, h, 16f * s, UiRenderer.Rgba(0.03f, 0.04f, 0.07f, 0.92f));
        ui.Line(new Vector2(x + 16f * s, y + headerH - 10f * s),
                new Vector2(x + w - 16f * s, y + headerH - 10f * s), 1.6f * s, PanelEdge);

        string title = mode.IsOver ? mode.ResultTextFor(world, viewer) : Loc.ScoreboardTitle;
        Vector3 titleColor = mode.IsOver
            ? (title == Loc.ResultVictory ? new Vector3(0.4f, 1f, 0.5f) : new Vector3(1f, 0.45f, 0.3f))
            : new Vector3(0.85f, 0.92f, 1f);
        ui.TextOutline(FaceBold, 32f * s, x + w * 0.5f, y + 12f * s, title,
            UiRenderer.Rgba(titleColor), Shadow, 2.5f * s, TextAlign.Center);
        ui.Text(FaceRegular, 15f * s, x + w * 0.5f, y + 50f * s,
            $"{Loc.ModeName(mode.Kind)} · {world.Level.Name}",
            UiRenderer.Rgba(0.65f, 0.72f, 0.85f, 0.9f), TextAlign.Center);

        // Column layout.
        float colName = x + 26f * s;
        float colFrags = x + w * 0.56f;
        float colDeaths = x + w * 0.70f;
        float colAcc = x + w * 0.84f;
        float colExtra = x + w - 26f * s;

        float ry = y + headerH;
        uint headerCol = UiRenderer.Rgba(0.55f, 0.65f, 0.8f, 0.85f);
        ui.Text(FaceRegular, 14f * s, colName, ry, Loc.ScoreName, headerCol);
        ui.Text(FaceRegular, 14f * s, colFrags, ry,
            mode.Kind == GameModeKind.Domination ? Loc.ScorePoints : Loc.ScoreFrags,
            headerCol, TextAlign.Right);
        ui.Text(FaceRegular, 14f * s, colDeaths, ry, Loc.ScoreDeaths, headerCol, TextAlign.Right);
        ui.Text(FaceRegular, 14f * s, colAcc, ry, Loc.ScoreAccuracy, headerCol, TextAlign.Right);
        ui.Text(FaceRegular, 14f * s, colExtra, ry,
            mode.Kind switch
            {
                GameModeKind.CaptureTheFlag => Loc.ScoreCaptures,
                GameModeKind.Domination => Loc.ScoreDomCaptures,
                GameModeKind.Onslaught or GameModeKind.Warfare => Loc.ScoreNodes,
                GameModeKind.Assault => Loc.ScoreObjectives,
                _ => Loc.ScoreRatio,
            },
            headerCol, TextAlign.Right);
        ry += rowH;

        foreach (var p in ranking)
        {
            bool isViewer = p == viewer;
            Vector3 rowColor = mode.TeamBased ? GameTypes.TeamColor(p.Team) : p.AccentColor;
            if (isViewer)
                ui.Rect(x + 14f * s, ry - 2f * s, w - 28f * s, rowH - 2f * s,
                    UiRenderer.Rgba(rowColor * 0.4f, 0.30f));
            else if ((ranking.IndexOf(p) & 1) == 1)
                ui.Rect(x + 14f * s, ry - 2f * s, w - 28f * s, rowH - 2f * s,
                    UiRenderer.Rgba(1f, 1f, 1f, 0.035f));

            ui.Rect(colName - 12f * s, ry + 2f * s, 5f * s, rowH - 10f * s, UiRenderer.Rgba(rowColor, 0.95f));

            string name = p.Name + (p.IsBot ? "" : $" [{Loc.HudPlayer}{p.PlayerIndex + 1}]");
            if (!p.Alive && mode.LivesFor(p) == 0) name += " ✕";
            ui.Text(FaceRegular, 17f * s, colName, ry + 1f * s, name,
                isViewer ? UiRenderer.Rgba(1f, 1f, 1f) : UiRenderer.Rgba(0.85f, 0.89f, 0.95f));

            ui.Text(FaceBold, 17f * s, colFrags, ry + 1f * s, mode.ScoreOf(p).ToString(),
                UiRenderer.Rgba(rowColor * 1.2f), TextAlign.Right);
            ui.Text(FaceRegular, 17f * s, colDeaths, ry + 1f * s, p.Deaths.ToString(),
                UiRenderer.Rgba(0.8f, 0.8f, 0.85f), TextAlign.Right);
            ui.Text(FaceRegular, 17f * s, colAcc, ry + 1f * s, $"{p.Accuracy * 100f:0}%",
                UiRenderer.Rgba(0.75f, 0.85f, 0.95f), TextAlign.Right);

            string extra = mode.Kind is GameModeKind.CaptureTheFlag or GameModeKind.Domination
                    or GameModeKind.Onslaught or GameModeKind.Assault or GameModeKind.Warfare
                ? p.Captures.ToString()
                : $"{(p.Deaths > 0 ? p.Frags / (float)p.Deaths : p.Frags):0.0}";
            ui.Text(FaceRegular, 17f * s, colExtra, ry + 1f * s, extra,
                UiRenderer.Rgba(0.8f, 0.85f, 0.95f), TextAlign.Right);
            ry += rowH;
        }

        if (mode.IsOver)
            ui.TextShadow(FaceRegular, 18f * s, x + w * 0.5f, y + h + 12f * s, Loc.ResultPressToContinue,
                UiRenderer.Rgba(0.9f, 0.92f, 1f, 0.8f + 0.2f * MathF.Sin(world.Time * 4f)),
                TextAlign.Center, 2f * s);
    }

    private void DrawDebug(UiRenderer ui, string text, int width, int height, float s)
    {
        if (string.IsNullOrEmpty(text)) return;
        string[] lines = text.Split('\n');
        float font = LayoutFont(14f * s);
        float lh = font + 5f;
        float w = 0f;
        foreach (string line in lines) w = MathF.Max(w, ui.MeasureText(FaceRegular, font, line));
        w = MathF.Min(w, width - 28f);
        ui.Rect(10f * s, 10f * s, w + 18f * s, lines.Length * lh + 12f * s,
            UiRenderer.Rgba(0f, 0f, 0f, 0.55f));
        for (int i = 0; i < lines.Length; i++)
            ui.Text(FaceRegular, font, 19f * s, 16f * s + i * lh,
                FitText(ui, FaceRegular, font, lines[i], w),
                UiRenderer.Rgba(0.6f, 1f, 0.7f, 0.95f));
        _ = (width, height);
    }
}
