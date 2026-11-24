using GamesByMalcs.Melvor2.Components.CombatFood;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Conditions;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Items;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Applies changes to a prepared copy through the game's own calls, in canonical order (levels before all; quests
/// before the purchases that need one; purchases before the toolbelt slots they unlock; the gear before the stance
/// and spell it brings; prayers turned off before prayers turned on, so a swap frees its slot). The game refuses
/// silently, so each change's requirements are read first for their text, then the call is made and its effect read
/// back. Copies only: never the live game.
/// </summary>
internal static partial class Changes
{
    public const string HitpointsId = "MelvorBase:Hitpoints";
    public const string CombatPotionSlot = "MelvorBase:CombatPotion";

    // Ammo the bank doesn't hold is added at this count; the top-up keeps it there.
    private const int AmmoAdded = 1000;
    private const int FoodAdded = 1000;

    /// <summary>The skills a change may set: the combat skills.</summary>
    public static readonly IReadOnlyList<string> CombatSkills =
    [
        "MelvorBase:Attack",
        "MelvorBase:Strength",
        "MelvorBase:Defence",
        HitpointsId,
        "MelvorBase:Ranged",
        "MelvorBase:Magic",
        "MelvorBase:Prayer",
        "MelvorBase:Slayer",
    ];

    /// <summary>The lowest level a skill can be set to: 10 for Hitpoints, as the game raises it on load.</summary>
    public static int LowestLevel(string skillId) => skillId == HitpointsId ? 10 : 1;

    /// <summary>
    /// Applies <paramref name="changes"/> to <paramref name="copy"/> and returns each with what became of it, in the
    /// list's order. With <paramref name="throwOnRefusal"/>, a refused change throws instead: run copies must have
    /// every change the check accepted. Changes whose keys are in <paramref name="ignoring"/> have their requirements
    /// ignored whatever <paramref name="ignoreRequirements"/> says.
    /// </summary>
    public static List<ChangeOutcome> Apply(
        Copy copy,
        ChangeList changes,
        bool ignoreRequirements,
        bool throwOnRefusal,
        IReadOnlySet<ChangeKey>? ignoring = null
    )
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(changes);
        using var scope = Guards.Enter();
        var byChange = new Dictionary<Change, ChangeOutcome>();
        var order = changes
            .Where(c => c.Kind != ChangeKind.Prayer)
            .Concat(changes.Where(c => c.Kind == ChangeKind.Prayer && !c.TurnsOn))
            .Concat(changes.Where(c => c.TurnsOn));
        var everything = ignoreRequirements;
        // Without the patch that makes the game read a requirement as met, a change is tried with its requirements,
        // and one that doesn't meet them is refused with its reasons rather than applied some other way.
        var canIgnore = Dependencies.IsAvailable(Dependencies.IgnoreRequirements);
        foreach (var change in order)
        {
            ChangeOutcome outcome;
            ignoreRequirements = canIgnore && (everything || ignoring?.Contains(change.Key) == true);
            try
            {
                outcome =
                    Unsupported(change)
                    ?? change.Kind switch
                    {
                        ChangeKind.Level => Level(copy, change),
                        ChangeKind.Quest => QuestChange.Apply(copy, change, ignoreRequirements),
                        ChangeKind.Shop => ShopChange.Apply(copy, change, ignoreRequirements),
                        ChangeKind.Guild => GuildChange.Apply(copy, change),
                        ChangeKind.Star => StarChange.Apply(copy, change, ignoreRequirements),
                        ChangeKind.Equip => Equip(copy, change, ignoreRequirements),
                        ChangeKind.Toolbelt => ToolbeltChange.Apply(copy, change, ignoreRequirements),
                        ChangeKind.Stance => Stance(copy, change),
                        ChangeKind.Spell => Spell(copy, change, ignoreRequirements),
                        ChangeKind.Imbuement => Imbuement(copy, change, ignoreRequirements),
                        ChangeKind.Food => Food(copy, change),
                        ChangeKind.Prayer => Prayer(copy, change, ignoreRequirements),
                        ChangeKind.Blocks => Slayer.BlockChange.Apply(copy, change),
                        ChangeKind.Loadouts => Slayer.LoadoutChange.Apply(copy, change),
                        ChangeKind.SlayerTask or ChangeKind.SlayerTaskFor => Slayer.LoadoutChange.ApplyTask(
                            copy,
                            change
                        ),
                        ChangeKind.Potion => PotionChange.Apply(copy, change, ignoreRequirements),
                        ChangeKind.Pet => PetChange.Apply(copy, change, ignoreRequirements),
                        ChangeKind.Bonfire => BonfireChange.Apply(copy, change, ignoreRequirements),
                        ChangeKind.Technique => TechniqueChange.Apply(copy, change, ignoreRequirements),
                        _ => Refused(change, null, Note.Of("unsupported on this game build")),
                    };
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                Log.Warn($"change {change.ShortForm} refused", e);
                outcome = Refused(change, null, Note.Of("the game failed: {0}", e.Message));
            }
            if (throwOnRefusal && outcome.LeftOut)
                throw new InvalidOperationException(
                    $"the game refused {change.ShortForm} in the copy: {outcome.Reasons.English("; ")}"
                );
            byChange[change] = outcome;
        }
        return [.. changes.Select(c => byChange[c])];
    }

    /// <summary>Whether the build has combat potions, so the potion can be changed.</summary>
    public static bool PotionsInBuild(MelvorGame game) =>
        game
            .Data.GetDataRegistryOutput<Item>()
            .GetAllObjects()
            .Any(item =>
                item.Capabilities.TryGet<ToolbeltCapability>(out var toolbelt)
                && toolbelt.FitsInSlot.Id == CombatPotionSlot
            );

    public static ChangeOutcome Refused(Change change, string? from, params Note[] reasons) =>
        new(change, from, ChangeState.Refused, [.. reasons]);

    public static ChangeOutcome Outcome(Change change, string? from, bool applied, List<Note> reasons, bool ignored)
    {
        if (!applied)
            return new(change, from, ChangeState.Refused, reasons);
        return ignored && reasons.Count > 0
            ? new(change, from, ChangeState.RequirementIgnored, reasons)
            : new(change, from, ChangeState.Accepted, []);
    }

    // The feature a kind's game calls are listed under, beyond those every change needs.
    private static string? FeatureOf(ChangeKind kind) =>
        kind switch
        {
            ChangeKind.Toolbelt => Dependencies.Toolbelt,
            ChangeKind.Star => Dependencies.Astrology,
            ChangeKind.Guild => Dependencies.Guild,
            ChangeKind.Shop => Dependencies.Shop,
            ChangeKind.Quest => Dependencies.Quests,
            ChangeKind.Blocks or ChangeKind.Loadouts or ChangeKind.SlayerTask or ChangeKind.SlayerTaskFor =>
                Dependencies.SlayerMasters,
            ChangeKind.Potion => Dependencies.Herblore,
            ChangeKind.Pet => Dependencies.Pets,
            ChangeKind.Bonfire => Dependencies.Firemaking,
            ChangeKind.Technique => Dependencies.Agility,
            _ => null,
        };

    // A kind whose game calls this build lacks is refused, rather than tried and failing somewhere inside.
    private static ChangeOutcome? Unsupported(Change change) =>
        FeatureOf(change.Kind) is { } feature && !Dependencies.IsAvailable(feature)
            ? Refused(change, null, Note.Of("unsupported on this game build"))
            : null;

    // What the change names isn't in the running build's data.
    public static ChangeOutcome Missing(Change change, string? from) =>
        new(change, from, ChangeState.NotInBuild, [Note.Of("not in this build")]);

    private static ChangeOutcome Level(Copy copy, Change change)
    {
        var skillId = change.Subject!;
        var skill = copy.Game.LeveledEntities.FirstOrDefault(e => e.Id == skillId);
        if (skill == null)
            return Missing(change, null);
        var experience = skill.Experience;
        var from = experience.Level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!CombatSkills.Contains(skillId))
            return Refused(change, from, Note.Of("can't change this skill"));
        var level = change.Level!.Value;
        if (level == experience.Level)
            return new(change, from, ChangeState.NoChange, []);
        var lowest = LowestLevel(skillId);
        if (level < lowest || level > experience.LevelCap)
            return Refused(change, from, Note.Of("level must be {0}–{1}", lowest, experience.LevelCap));
        experience.SetXp(experience.Table.GetXpForLevel(level));
        return experience.Level == level
            ? new(change, from, ChangeState.Accepted, [])
            : Refused(change, from, Note.Of("the game kept level {0}", experience.Level));
    }

    private static ChangeOutcome Equip(Copy copy, Change change, bool ignoreRequirements)
    {
        var game = copy.Game;
        var equipment = copy.Combat.Player.Equipment;
        if (!game.Data.TryGetObject<EquipmentSlot>(change.Subject!, out var slot))
            return Missing(change, null);
        // A slot the character hasn't unlocked has no active slot.
        if (!equipment.TryGetActiveSlot(slot, out var active))
        {
            var locked = Targets.Unmet(slot.UnlockRequirements);
            return Refused(change, null, locked.Count > 0 ? [.. locked] : [Note.Of("this slot is locked")]);
        }
        var from = active.Equipped?.Item.Id ?? Change.None;
        if (from == change.Value)
            return new(change, from, ChangeState.NoChange, []);
        if (change.Item is not { } itemId)
        {
            copy.Combat.ItemEquipping.UnequipSlot(slot);
            return active.IsEmpty
                ? new(change, from, ChangeState.Accepted, [])
                : Refused(change, from, Note.Of("the game didn't take it off"));
        }
        if (!game.Data.TryGetObject<Item>(itemId, out var item))
            return Missing(change, from);
        if (!item.Capabilities.TryGet<EquipmentCapability>(out var capability) || capability.Type != slot.FitsType)
            return Refused(change, from, Note.Of("doesn't fit this slot"));
        var reasons = Targets.Unmet(capability.Requirements);
        var slotReasons = Targets.Unmet(slot.UnlockRequirements);
        if (slotReasons.Count > 0)
            return Refused(change, from, [.. reasons, .. slotReasons]);
        if (equipment.IsItemEquipped(item) && !capability.CanEquipQuantity)
            return Refused(change, from, Note.Of("already worn in another slot"));
        var bank = copy.Bank.Inventory;
        var quantity = bank.GetQuantity(item);
        if (quantity == 0)
        {
            var wanted = capability.CanEquipQuantity ? AmmoAdded : 1;
            quantity = bank.AddQuantity(item, wanted, AddItemFlags.None);
            if (quantity == 0)
                return Refused(change, from, Note.Of("bank full"));
        }
        var ignored = ignoreRequirements && reasons.Count > 0;
        using (ignored ? RequirementsIgnored.Enter(capability.Requirements) : default(RequirementsIgnored.Scope?))
            copy.Combat.ItemEquipping.EquipItem(item, capability.CanEquipQuantity ? quantity : 1, slot);
        var applied = active.Equipped?.Item == item;
        if (!applied && reasons.Count == 0)
            reasons.Add(Note.Of("the game didn't equip it"));
        return Outcome(change, from, applied, reasons, ignored);
    }

    private static ChangeOutcome Stance(Copy copy, Change change)
    {
        var player = copy.Combat.Player;
        var from = player.Stats.AttackStance.Value.Id;
        if (from == change.Value)
            return new(change, from, ChangeState.NoChange, []);
        if (
            !copy.Game.Data.TryGetObject<GamesByMalcs.Melvor2.Entities.Combat.AttackStance>(
                change.Value,
                out var stance
            )
        )
            return Missing(change, from);
        var weapon = player.Equipment.CurrentWeapon.Weapon.Type;
        if (!weapon.Stances.Contains(stance))
            return Refused(change, from, Note.Of("this weapon has no such stance"));
        player.SetSelectedStance(weapon, stance);
        return player.Stats.AttackStance.Value == stance
            ? new(change, from, ChangeState.Accepted, [])
            : Refused(change, from, Note.Of("the game kept its stance"));
    }

    // Runes the bank doesn't hold are added, as food is: every tick tops them back up, so the count never matters.
    private const int RunesAdded = 10_000;

    private static ChangeOutcome Spell(Copy copy, Change change, bool ignoreRequirements)
    {
        var magic = copy.Combat.MagicSpells;
        var from = magic.SelectedSpell?.Data.Id ?? Change.None;
        if (from == change.Value)
            return new(change, from, ChangeState.NoChange, []);
        var spell = magic.ActiveSpells.Keys.FirstOrDefault(s => s.Id == change.Value);
        if (spell == null)
            return Missing(change, from);
        var reasons = Targets.Unmet(spell.UnlockRequirements);
        if (reasons.Count > 0 && !ignoreRequirements)
            return Refused(change, from, [.. reasons]);
        if (!magic.AvailableElements.Contains(spell.Element))
            return Refused(change, from, Note.Of("weapon can't cast {0}", spell.Element.DisplayName.Text));
        if (AddRunes(copy, spell.RuneCosts) is { } full)
            return Refused(change, from, full);
        var ignored = ignoreRequirements && reasons.Count > 0;
        using (ignored ? RequirementsIgnored.Enter(spell.UnlockRequirements) : default(RequirementsIgnored.Scope?))
            magic.SelectSpell(spell);
        var applied = magic.SelectedSpell?.Data == spell;
        if (!applied && reasons.Count == 0)
            reasons.Add(Note.Of("the game didn't select it"));
        return Outcome(change, from, applied, reasons, ignored);
    }

    private static ChangeOutcome Imbuement(Copy copy, Change change, bool ignoreRequirements)
    {
        var magic = copy.Combat.MagicSpells;
        var from = magic.SelectedImbuement?.Data.Id ?? Change.None;
        if (from == change.Value)
            return new(change, from, ChangeState.NoChange, []);
        if (change.Value == Change.None)
        {
            magic.DeselectImbuement();
            return magic.SelectedImbuement == null
                ? new(change, from, ChangeState.Accepted, [])
                : Refused(change, from, Note.Of("the game kept it"));
        }
        var imbuement = magic.ActiveImbuements.Keys.FirstOrDefault(s => s.Id == change.Value);
        if (imbuement == null)
            return Missing(change, from);
        var reasons = Targets.Unmet(imbuement.UnlockRequirements);
        if (reasons.Count > 0 && !ignoreRequirements)
            return Refused(change, from, [.. reasons]);
        if (!magic.AvailableElements.Contains(imbuement.Element))
            return Refused(change, from, Note.Of("weapon can't cast {0}", imbuement.Element.DisplayName.Text));
        if (AddRunes(copy, imbuement.RuneCosts) is { } full)
            return Refused(change, from, full);
        var ignored = ignoreRequirements && reasons.Count > 0;
        using (ignored ? RequirementsIgnored.Enter(imbuement.UnlockRequirements) : default(RequirementsIgnored.Scope?))
            magic.SelectImbuement(imbuement);
        var applied = magic.SelectedImbuement?.Data == imbuement;
        if (!applied && reasons.Count == 0)
            reasons.Add(Note.Of("the game didn't select it"));
        return Outcome(change, from, applied, reasons, ignored);
    }

    /// <summary>
    /// Gives the bank the runes the selected spell and imbuement cost when it holds none, as choosing a spell in a
    /// setup does. Fights top runes back up after each tick, but only runes the bank holds: with none, the copy
    /// can't cast and never attacks.
    /// </summary>
    public static void StockRunes(Copy copy)
    {
        if (!Dependencies.IsAvailable(Dependencies.Changes))
            return;
        using var scope = Guards.Enter();
        var magic = copy.Combat.MagicSpells;
        var costs = (magic.SelectedSpell?.Data.RuneCosts ?? []).Concat(magic.SelectedImbuement?.Data.RuneCosts ?? []);
        if (AddRunes(copy, costs) is { } full)
            Log.Warn($"runes not added for the selected spell: {full.English()}");
    }

    // The runes a spell costs, added to the bank when it holds none; a reason when the bank is full.
    private static Note? AddRunes(Copy copy, IEnumerable<GamesByMalcs.Melvor2.Data.Items.ItemQuantity> costs)
    {
        var bank = copy.Bank.Inventory;
        foreach (var cost in costs)
        {
            if (bank.GetQuantity(cost.Item) == 0 && bank.AddQuantity(cost.Item, RunesAdded, AddItemFlags.None) == 0)
                return Note.Of("bank full");
        }
        return null;
    }

    private static ChangeOutcome Food(Copy copy, Change change)
    {
        var food = copy.Combat.Food;
        var from = food.SelectedFood?.Item.Id ?? Change.None;
        if (from == change.Value)
            return new(change, from, ChangeState.NoChange, []);
        if (change.Item is not { } itemId)
        {
            food.UnselectFood();
            return food.SelectedFood == null
                ? new(change, from, ChangeState.Accepted, [])
                : Refused(change, from, Note.Of("the game kept it"));
        }
        if (!copy.Game.Data.TryGetObject<Item>(itemId, out var item))
            return Missing(change, from);
        if (!item.Capabilities.TryGet<FoodCapability>(out _))
            return Refused(change, from, Note.Of("isn't food"));
        var bank = copy.Bank.Inventory;
        if (bank.GetQuantity(item) == 0 && bank.AddQuantity(item, FoodAdded, AddItemFlags.None) == 0)
            return Refused(change, from, Note.Of("bank full"));
        food.SelectFood(item);
        return food.SelectedFood?.Item == item
            ? new(change, from, ChangeState.Accepted, [])
            : Refused(change, from, Note.Of("the game didn't select it"));
    }

    private static ChangeOutcome Prayer(Copy copy, Change change, bool ignoreRequirements)
    {
        var prayers = copy.Combat.Prayer;
        var prayer = prayers.ActivePrayers.Keys.FirstOrDefault(p => p.Id == change.Subject);
        if (prayer == null)
            return Missing(change, null);
        var selected = prayers.IsPrayerSelected(prayer);
        var from = selected ? Change.On : Change.Off;
        if (from == change.Value)
            return new(change, from, ChangeState.NoChange, []);
        if (!change.TurnsOn)
        {
            prayers.TogglePrayer(prayer);
            return prayers.IsPrayerSelected(prayer)
                ? Refused(change, from, Note.Of("the game kept it on"))
                : new(change, from, ChangeState.Accepted, []);
        }
        var reasons = Targets.Unmet(prayer.UnlockRequirements);
        if (reasons.Count > 0 && !ignoreRequirements)
            return Refused(change, from, [.. reasons]);
        GivePrayerPoints(copy, prayer.Cost.Currency);
        if (
            !prayers.ActiveSlots.Values.Any(s =>
                s.FitsType == prayer.FitsInSlot && s.UnlockRequirements.AreMet && s.Selected == null
            )
        )
            return Refused(change, from, Note.Of("no free prayer slot"));
        var ignored = ignoreRequirements && reasons.Count > 0;
        using (ignored ? RequirementsIgnored.Enter(prayer.UnlockRequirements) : default(RequirementsIgnored.Scope?))
            prayers.TogglePrayer(prayer);
        var applied = prayers.IsPrayerSelected(prayer) && prayers.IsPrayerActive(prayer);
        if (!applied && reasons.Count == 0)
            reasons.Add(Note.Of("the game didn't turn it on"));
        return Outcome(change, from, applied, reasons, ignored);
    }

    /// <summary>The unmet conditions of a group, for callers outside a copy's apply (the item picker).</summary>
    public static IReadOnlyList<Note> Unmet(ConditionGroup group) => Targets.Unmet(group);

    /// <summary>
    /// Returns <paramref name="change"/> with its short ids resolved against <paramref name="game"/>'s data
    /// (<paramref name="raw"/> is the text it was read from, so ids given in full are kept): a short id matching ids
    /// in several namespaces is a <see cref="CommandException"/> listing them; one matching none is kept, and the copy
    /// refuses it.
    /// </summary>
    public static Change Resolve(MelvorGame game, Change change, string raw)
    {
        string Id<T>(string id)
            where T : INamespacedObject =>
            Resolve(id, raw, game.Data.GetDataRegistryOutput<T>().GetAllObjects().Select(o => o.Id));
        var subject = change.Kind switch
        {
            ChangeKind.Level => Resolve(change.Subject!, raw, game.LeveledEntities.Select(e => e.Id)),
            ChangeKind.Equip => Id<EquipmentSlot>(change.Subject!),
            ChangeKind.Toolbelt => Id<ToolbeltSlot>(change.Subject!),
            ChangeKind.Star => Id<GamesByMalcs.Melvor2.Entities.Astrology.AstrologyNode>(change.Subject!),
            ChangeKind.Quest => Id<GamesByMalcs.Melvor2.Entities.Quests.Quest>(change.Subject!),
            ChangeKind.Shop => Id<GamesByMalcs.Melvor2.Entities.Shop.ShopPurchase>(change.Subject!),
            ChangeKind.Guild => Resolve(change.Subject!, raw, game.LeveledEntities.Select(e => e.Id)),
            ChangeKind.Prayer => Id<GamesByMalcs.Melvor2.Components.Prayers.Prayer>(change.Subject!),
            ChangeKind.Blocks or ChangeKind.Loadouts => Id<GamesByMalcs.Melvor2.Components.SlayerTasks.SlayerMaster>(
                change.Subject!
            ),
            ChangeKind.Pet => Id<GamesByMalcs.Melvor2.Components.Pets.Pet>(change.Subject!),
            ChangeKind.Technique => Id<GamesByMalcs.Melvor2.Entities.Agility.AgilityTechnique>(change.Subject!),
            _ => change.Subject,
        };
        var value =
            change.Item is { } item ? Id<Item>(item)
            : change.Value == Change.None ? change.Value
            : change.Kind switch
            {
                ChangeKind.Bonfire when change.Tier is { } tier =>
                    Id<GamesByMalcs.Melvor2.Entities.Firemaking.FiremakingBonfire>(tier),
                ChangeKind.Stance => Id<GamesByMalcs.Melvor2.Entities.Combat.AttackStance>(change.Value),
                ChangeKind.Spell => Id<GamesByMalcs.Melvor2.Components.MagicSpells.MagicSpell>(change.Value),
                ChangeKind.Imbuement => Id<GamesByMalcs.Melvor2.Components.MagicSpells.Imbuement>(change.Value),
                ChangeKind.Blocks => string.Join(
                    ',',
                    change.Blocked.Select(Id<GamesByMalcs.Melvor2.Entities.Combat.Enemy>)
                ),
                ChangeKind.Loadouts => string.Join(
                    ',',
                    change.Loadouts.Select(p =>
                        Id<GamesByMalcs.Melvor2.Entities.Combat.Enemy>(p.Enemy)
                        + "@"
                        + Id<GamesByMalcs.Melvor2.Components.Loadouts.LoadoutSlot>(p.Slot)
                    )
                ),
                _ => change.Value,
            };
        return new Change(change.Kind, subject, value);
    }

    // A short id (taken to be in the base namespace when parsed) matched against every id of its kind by its local
    // part.
    private static string Resolve(string id, string raw, IEnumerable<string> ids)
    {
        if (raw.Contains(id, StringComparison.Ordinal))
            return id; // given in full
        var local = ":" + Ids.Brief(id);
        var matches = ids.Where(i => i.EndsWith(local, StringComparison.Ordinal)).Distinct().ToList();
        return matches.Count switch
        {
            0 => id, // the copy refuses it as not in this build
            1 => matches[0],
            _ => throw new CommandException($"'{Ids.Brief(id)}' is ambiguous: {string.Join(", ", matches)}"),
        };
    }
}
