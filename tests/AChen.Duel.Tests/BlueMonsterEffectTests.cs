using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class BlueMonsterEffectTests
{
    [Fact]
    public void Photon_lord_opponent_turn_effect_can_attach_a_matching_deck_card_in_a_directed_rules_fixture()
    {
        // No Main Deck Photon/Galaxy card exists in this closed pool. This fixture only exercises the complete effect program.
        var duel = Create(Array.Empty<string>(), new[] { "89631139", "89631139", "89631139", "89631139", "89631139", "39030163" }, "08165596");
        var lord = duel.State.Cards.Single(c => c.DefinitionId == "08165596");
        var photon = duel.State.Cards.Single(c => c.DefinitionId == "39030163");
        lord.Zone = DuelZone.Monster; lord.Position = CardPosition.FaceUpAttack; lord.Controller = 1;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 }).Accepted);
        Activate(duel, lord, "08165596.3"); PassTwice(duel);
        Answer(duel, photon.InstanceId.ToString()); Answer(duel, "material");
        Assert.Equal(DuelZone.Material, photon.Zone);
        Assert.Equal(lord.InstanceId, photon.HostInstanceId);
        Assert.Contains(photon.InstanceId, lord.Materials);
    }

    [Fact]
    public void Photon_material_makes_photon_lord_indestructible_to_effects()
    {
        var duel = Create(new[] { "38517737" }, extra: new[] { "08165596", "39030163" });
        var alternative = duel.State.Cards.Single(c => c.DefinitionId == "38517737");
        var lord = duel.State.Cards.Single(c => c.DefinitionId == "08165596");
        var photon = duel.State.Cards.Single(c => c.DefinitionId == "39030163");
        alternative.Zone = lord.Zone = DuelZone.Monster; alternative.Position = lord.Position = CardPosition.FaceUpAttack;
        lord.Controller = 1; photon.Zone = DuelZone.Material; photon.HostInstanceId = lord.InstanceId; lord.Materials.Add(photon.InstanceId);
        Activate(duel, alternative, "38517737.2", lord.InstanceId); PassTwice(duel);
        Assert.Equal(DuelZone.Monster, lord.Zone);
        Assert.Equal(DuelZone.Material, photon.Zone);
    }

    [Fact]
    public void Photon_lord_detaches_a_galaxy_material_to_negate_and_destroy_an_enemy_monster_effect()
    {
        var duel = Create(new[] { "38517737" }, extra: new[] { "08165596", "39030163" });
        var alternative = duel.State.Cards.Single(c => c.DefinitionId == "38517737");
        var lord = duel.State.Cards.Single(c => c.DefinitionId == "08165596");
        var material = duel.State.Cards.Single(c => c.DefinitionId == "39030163");
        alternative.Zone = lord.Zone = DuelZone.Monster; alternative.Position = lord.Position = CardPosition.FaceUpAttack;
        lord.Controller = 1;
        material.Zone = DuelZone.Material; material.HostInstanceId = lord.InstanceId; lord.Materials.Add(material.InstanceId);
        Activate(duel, alternative, "38517737.2", lord.InstanceId);
        Activate(duel, lord, "08165596.2", 0, material.InstanceId); PassTwice(duel);
        Assert.Equal(DuelZone.Graveyard, material.Zone);
        Assert.Equal(DuelZone.Graveyard, alternative.Zone);
        Assert.Equal(DuelZone.Monster, lord.Zone);
    }

    [Fact]
    public void Xyz_cipher_blade_destroyed_by_an_enemy_effect_can_revive_its_graveyard_cipher_dragon()
    {
        var duel = Create(new[] { "38517737" }, extra: new[] { "02530830", "18963306" });
        var alternative = duel.State.Cards.Single(c => c.DefinitionId == "38517737");
        var blade = duel.State.Cards.Single(c => c.DefinitionId == "02530830");
        var cipher = duel.State.Cards.Single(c => c.DefinitionId == "18963306");
        alternative.Zone = blade.Zone = DuelZone.Monster; alternative.Position = blade.Position = CardPosition.FaceUpAttack;
        alternative.Controller = 1;
        blade.SummonMethod = cipher.SummonMethod = SummonMethod.Xyz; blade.ProperlySummoned = cipher.ProperlySummoned = true;
        cipher.Zone = DuelZone.Graveyard; cipher.Position = CardPosition.FaceUp;
        duel.State.TurnPlayer = 1; duel.State.WaitingSeat = 1;
        Activate(duel, alternative, "38517737.2", blade.InstanceId); PassTwice(duel);
        Assert.Equal(DuelZone.Graveyard, blade.Zone);
        Assert.Equal(DecisionKind.YesNo, duel.State.PendingDecision?.Kind);
        Answer(duel, "yes"); Answer(duel, cipher.InstanceId.ToString()); PassTwice(duel);
        Answer(duel, "0"); Answer(duel, "attack");
        Assert.Equal(DuelZone.Monster, cipher.Zone);
    }

    [Fact]
    public void Full_armor_targets_two_equips_without_paying_them_as_cost_and_attaches_both_on_resolution()
    {
        var duel = Create(new[] { "70368879", "06853254" }, extra: new[] { "39030163" });
        var armor = duel.State.Cards.Single(c => c.DefinitionId == "39030163");
        armor.Zone = DuelZone.Monster; armor.Position = CardPosition.FaceUpAttack;
        var equips = duel.State.Cards.Where(c => c.Owner == 0 && new[] { "70368879", "06853254" }.Contains(c.DefinitionId)).ToArray();
        for (int i = 0; i < equips.Length; i++) { equips[i].Zone = DuelZone.SpellTrap; equips[i].Position = CardPosition.FaceUp; equips[i].Slot = i; equips[i].EquipTarget = armor.Ref; }
        Activate(duel, armor, "39030163.1", 0, equips.Select(c => c.InstanceId).ToArray());
        Assert.Empty(duel.State.Chain.Single().Costs);
        Assert.Equal(2, duel.State.Chain.Single().Targets.Count);
        Assert.All(equips, c => Assert.Equal(DuelZone.SpellTrap, c.Zone));
        PassTwice(duel);
        Assert.All(equips, c => { Assert.Equal(DuelZone.Material, c.Zone); Assert.Equal(armor.InstanceId, c.HostInstanceId); });
        Assert.Equal(2, armor.Materials.Count);
    }

    [Theory]
    [InlineData("39030163", "39030163.2")]
    [InlineData("02530830", "02530830.1")]
    public void Galaxy_xyz_detaches_one_material_as_cost_to_destroy_its_face_up_enemy_target(string cardId, string abilityId)
    {
        var duel = Create(Array.Empty<string>(), extra: new[] { cardId });
        var xyz = duel.State.Cards.Single(c => c.DefinitionId == cardId);
        var material = duel.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand);
        var enemy = duel.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
        xyz.Zone = enemy.Zone = DuelZone.Monster; xyz.Position = enemy.Position = CardPosition.FaceUpAttack;
        material.Zone = DuelZone.Material; material.HostInstanceId = xyz.InstanceId; xyz.Materials.Add(material.InstanceId);
        Activate(duel, xyz, abilityId, enemy.InstanceId, material.InstanceId);
        Assert.Equal(DuelZone.Graveyard, material.Zone); PassTwice(duel);
        Assert.Equal(DuelZone.Graveyard, enemy.Zone);
        Assert.Empty(xyz.Materials);
    }

    [Fact]
    public void Veiler_cannot_pay_a_graveyard_send_cost_that_dark_law_would_replace_with_banish()
    {
        var duel = Create(new[] { "97268402" }, extra: new[] { "58481572" });
        var veiler = duel.State.Cards.Single(c => c.DefinitionId == "97268402");
        var darkLaw = duel.State.Cards.Single(c => c.DefinitionId == "58481572");
        darkLaw.Zone = DuelZone.Monster; darkLaw.Position = CardPosition.FaceUpAttack; darkLaw.Controller = 1;
        duel.State.TurnPlayer = 1; duel.State.WaitingSeat = 0; duel.State.Window = TimingWindow.FastResponse;
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.AbilityId == "97268402.1");
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = veiler.InstanceId, AbilityId = "97268402.1", TargetId = darkLaw.InstanceId }).Accepted);
        Assert.Equal(DuelZone.Hand, veiler.Zone);
    }

    [Fact]
    public void Heliopolis_detaches_a_cost_then_tributes_in_resolution_and_destroys_the_same_number()
    {
        var duel = Create(Array.Empty<string>(), extra: new[] { "64332231" });
        var heliopolis = duel.State.Cards.Single(c => c.DefinitionId == "64332231");
        heliopolis.Zone = DuelZone.Monster; heliopolis.Position = CardPosition.FaceUpAttack;
        var hands = duel.State.Cards.Where(c => c.Owner == 0 && c.Zone == DuelZone.Hand).Take(2).ToArray();
        hands[0].Zone = DuelZone.Material; hands[0].HostInstanceId = heliopolis.InstanceId; heliopolis.Materials.Add(hands[0].InstanceId);
        var enemy = duel.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
        enemy.Zone = DuelZone.Monster; enemy.Position = CardPosition.FaceUpAttack;
        Activate(duel, heliopolis, "64332231.1", 0, hands[0].InstanceId);
        Assert.Equal(DuelZone.Graveyard, hands[0].Zone);
        Assert.Equal(DuelZone.Hand, hands[1].Zone);
        PassTwice(duel); Answer(duel, hands[1].InstanceId.ToString());
        if (duel.State.PendingDecision!.Kind == DecisionKind.YesNo) Answer(duel, "finish");
        Answer(duel, enemy.InstanceId.ToString());
        Assert.Equal(DuelZone.Graveyard, hands[1].Zone);
        Assert.Equal(DuelZone.Graveyard, enemy.Zone);
        Assert.Equal(DuelZone.Monster, heliopolis.Zone);
    }

    [Fact]
    public void Hope_harbinger_targets_an_own_xyz_after_another_face_up_xyz_was_destroyed_and_adds_printed_attack()
    {
        var duel = Create(Array.Empty<string>(), extra: new[] { "63767246", "18963306", "60461804" });
        var harbinger = duel.State.Cards.Single(c => c.DefinitionId == "63767246");
        var cipher = duel.State.Cards.Single(c => c.DefinitionId == "18963306");
        var phoenix = duel.State.Cards.Single(c => c.DefinitionId == "60461804");
        var enemy = duel.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
        harbinger.Zone = cipher.Zone = phoenix.Zone = enemy.Zone = DuelZone.Monster;
        harbinger.Position = cipher.Position = phoenix.Position = enemy.Position = CardPosition.FaceUpAttack;
        harbinger.Slot = 0; cipher.Slot = 1; phoenix.Slot = 2;
        Activate(duel, phoenix, "60461804.2"); PassTwice(duel);
        Answer(duel, cipher.InstanceId.ToString()); Answer(duel, enemy.InstanceId.ToString());
        Assert.Equal(DecisionKind.YesNo, duel.State.PendingDecision?.Kind);
        Answer(duel, "yes"); Answer(duel, harbinger.InstanceId.ToString()); PassTwice(duel);
        Assert.Equal(6000, harbinger.CurrentAtk);
    }

    [Fact]
    public void Hope_harbinger_detaches_one_and_redirects_the_same_attack_directly_to_calculation()
    {
        var duel = Create(Array.Empty<string>(), extra: new[] { "63767246" });
        var harbinger = duel.State.Cards.Single(c => c.DefinitionId == "63767246");
        var attacker = duel.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand);
        var material = duel.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand && c.InstanceId != attacker.InstanceId);
        var original = duel.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
        harbinger.Zone = attacker.Zone = original.Zone = DuelZone.Monster;
        harbinger.Position = attacker.Position = original.Position = CardPosition.FaceUpAttack;
        harbinger.Controller = 1; harbinger.Slot = 1;
        material.Zone = DuelZone.Material; material.HostInstanceId = harbinger.InstanceId; harbinger.Materials.Add(material.InstanceId);
        duel.State.Turn = 2; duel.State.Phase = DuelPhase.Battle; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0, CardId = attacker.InstanceId, TargetId = original.InstanceId }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 }).Accepted);
        Activate(duel, harbinger, "63767246.2", 0, material.InstanceId); PassTwice(duel);
        Assert.Equal(harbinger.Ref, duel.State.AttackTarget);
        Assert.Equal(BattleStep.AfterCalculation, duel.State.BattleStep);
        Assert.Equal(1, attacker.AttacksThisTurn);
        Assert.Equal(DuelZone.Graveyard, material.Zone);
    }

    [Fact]
    public void Hope_harbinger_negates_a_field_spell_effect_and_attaches_the_source_as_material()
    {
        var duel = Create(new[] { "70368879" }, extra: new[] { "63767246" });
        var source = duel.State.Cards.Single(c => c.DefinitionId == "70368879");
        var harbinger = duel.State.Cards.Single(c => c.DefinitionId == "63767246");
        harbinger.Zone = DuelZone.Monster; harbinger.Position = CardPosition.FaceUpAttack; harbinger.Controller = 1;
        Activate(duel, source, "70368879.1"); Activate(duel, harbinger, "63767246.1"); PassTwice(duel);
        Assert.Equal(DuelZone.Material, source.Zone);
        Assert.Equal(harbinger.InstanceId, source.HostInstanceId);
        Assert.Contains(source.InstanceId, harbinger.Materials);
        Assert.Equal(4, duel.State.Cards.Count(c => c.Controller == 0 && c.Zone == DuelZone.Hand));
        Assert.Equal(8000, duel.State.Players[1].LifePoints);
    }

    [Fact]
    public void Cipher_dragon_takes_control_changes_name_and_attack_and_forbids_its_other_monsters_direct_attacks()
    {
        var duel = Create(Array.Empty<string>(), extra: new[] { "18963306" });
        var cipher = duel.State.Cards.Single(c => c.DefinitionId == "18963306");
        cipher.Zone = DuelZone.Monster; cipher.Position = CardPosition.FaceUpAttack;
        var material = duel.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand);
        material.Zone = DuelZone.Material; material.HostInstanceId = cipher.InstanceId; cipher.Materials.Add(material.InstanceId);
        var enemy = duel.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
        enemy.Zone = DuelZone.Monster; enemy.Position = CardPosition.FaceUpAttack;
        duel.State.Turn = 2;
        Activate(duel, cipher, "18963306.1", enemy.InstanceId, material.InstanceId); PassTwice(duel);
        Assert.Equal(DecisionKind.ChooseZone, duel.State.PendingDecision?.Kind);
        Answer(duel, "1");
        Assert.Equal(0, enemy.Controller);
        Assert.Equal("18963306", enemy.CurrentNameId);
        Assert.Equal(3000, enemy.CurrentAtk);
        Assert.True(enemy.Negated);
        PassTwice(duel);
        duel.State.Phase = DuelPhase.Battle;
        var attacks = duel.QueryLegalActions(0).Where(a => a.Kind == DuelCommandKind.Attack).ToArray();
        Assert.Contains(attacks, a => a.Card.Equals(cipher.Ref) && a.CanAttackDirectly);
        Assert.DoesNotContain(attacks, a => a.Card.Equals(enemy.Ref) && a.CanAttackDirectly);
    }

    [Fact]
    public void Synchro_spirit_tributes_itself_and_summons_azure_in_defense_whose_protection_stops_the_delayed_destruction()
    {
        var duel = Create(Array.Empty<string>(), extra: new[] { "59822133", "40908371" });
        var spirit = duel.State.Cards.Single(c => c.DefinitionId == "59822133");
        var azure = duel.State.Cards.Single(c => c.DefinitionId == "40908371");
        spirit.Zone = DuelZone.Monster; spirit.Position = CardPosition.FaceUpAttack; spirit.SummonMethod = SummonMethod.Synchro; spirit.ProperlySummoned = true;
        Activate(duel, spirit, "59822133.3");
        Assert.Equal(DuelZone.Graveyard, spirit.Zone);
        PassTwice(duel); Answer(duel, duel.State.PendingDecision!.Options.Single(o => o.Card.InstanceId == azure.InstanceId).Id);
        Answer(duel, "0");
        Assert.Equal(CardPosition.FaceUpDefense, azure.Position);
        Assert.Equal("40908371.1", Assert.Single(duel.State.Chain).AbilityId);
        PassTwice(duel); PassTwice(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
        Assert.Equal(DuelZone.Monster, azure.Zone);
        Assert.DoesNotContain(duel.State.Effects, e => e.Kind == EffectRecordKind.DelayedDestroy);
    }

    [Fact]
    public void Spirit_dragon_negates_a_graveyard_activation_after_its_source_was_banished_as_cost()
    {
        var duel = Create(new[] { "71039903" }, extra: new[] { "59822133" });
        var spirit = duel.State.Cards.Single(c => c.DefinitionId == "59822133");
        var stone = duel.State.Cards.Single(c => c.DefinitionId == "71039903");
        var white = duel.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand && c.DefinitionId == "89631139");
        stone.Zone = white.Zone = DuelZone.Graveyard; stone.Position = white.Position = CardPosition.FaceUp;
        spirit.Zone = DuelZone.Monster; spirit.Position = CardPosition.FaceUpAttack; spirit.Controller = 1;
        Activate(duel, stone, "71039903.2", white.InstanceId);
        Assert.Equal(DuelZone.Banished, stone.Zone);
        Activate(duel, spirit, "59822133.2"); PassTwice(duel);
        Assert.Equal(DuelZone.Graveyard, white.Zone);
        Assert.Empty(duel.State.Chain);
    }

    [Fact]
    public void Spirit_dragon_blocks_simultaneous_two_monster_groups_for_both_players()
    {
        var duel = Create(Array.Empty<string>(), extra: new[] { "59822133" });
        var spirit = duel.State.Cards.Single(c => c.DefinitionId == "59822133");
        spirit.Zone = DuelZone.Monster; spirit.Position = CardPosition.FaceUpAttack;
        foreach (int player in new[] { 0, 1 })
        {
            var group = duel.State.Cards.Where(c => c.Owner == player && c.Zone == DuelZone.Hand).Take(2).ToArray();
            Assert.True(duel.CanSpecialSummonGroup(group.Take(1).ToArray(), player));
            Assert.False(duel.CanSpecialSummonGroup(group, player));
        }
    }

    [Fact]
    public void Stardust_spark_shields_one_actual_destruction_then_a_second_destruction_succeeds()
    {
        var fixture = new ProgramAbility("70368879", 999, 1, new[] { DuelZone.Hand }, c => true, (c, link) =>
        {
            if (link.Step != 0) return;
            if (c.DestroyMany(link, c.State.Cards.Where(card => card.Controller == 0 && card.DefinitionId == "89631139"
                && card.Zone == DuelZone.Monster)).Completed) link.Step = 1;
        });
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "70368879", "70368879" }.Concat(Enumerable.Repeat("89631139", 38)).ToArray(),
                Enumerable.Repeat("89631139", 40).ToArray() }, ExtraDecks = new[] { new[] { "83994433" }, Array.Empty<string>() } },
            new DuelAbilityRegistry(DuelAbilityRegistry.CreateDefault().Handlers.Concat(new[] { fixture })));
        while (duel.State.Phase != DuelPhase.Main1) PassTwice(duel);
        var spark = duel.State.Cards.Single(c => c.DefinitionId == "83994433");
        var blue = duel.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand && c.DefinitionId == "89631139");
        spark.Zone = blue.Zone = DuelZone.Monster; spark.Position = blue.Position = CardPosition.FaceUpAttack; blue.Slot = 1;
        Activate(duel, spark, "83994433.1", blue.InstanceId); PassTwice(duel); PassTwice(duel);
        var spells = duel.State.Cards.Where(c => c.DefinitionId == "70368879").ToArray();
        Activate(duel, spells[0], "70368879.999"); PassTwice(duel);
        Assert.Equal(DuelZone.Monster, blue.Zone);
        PassTwice(duel);
        Activate(duel, spells[1], "70368879.999"); PassTwice(duel);
        Assert.Equal(DuelZone.Graveyard, blue.Zone);
        Assert.Equal(DuelZone.Monster, spark.Zone);
    }

    [Fact]
    public void Twin_burst_contact_summon_has_battle_protection_and_banishes_an_enemy_it_failed_to_destroy()
    {
        var duel = Create(Array.Empty<string>(), extra: new[] { "02129638" });
        var whites = duel.State.Cards.Where(c => c.Owner == 0 && c.Zone == DuelZone.Hand).Take(2).ToArray();
        for (int i = 0; i < 2; i++) { whites[i].Zone = DuelZone.Monster; whites[i].Position = CardPosition.FaceUpAttack; whites[i].Slot = i; }
        var twin = duel.State.Cards.Single(c => c.DefinitionId == "02129638");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0, CardId = twin.InstanceId,
            Cards = whites.Select(c => c.InstanceId).ToArray(), Slot = 0 }).Accepted);
        PassTwice(duel);
        var enemy = duel.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
        enemy.Zone = DuelZone.Monster; enemy.Position = CardPosition.FaceUpAttack;
        enemy.CurrentAtk = 4000;
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.SetAttack, Target = enemy.Ref, Value = 4000 });
        duel.State.Turn = 2; duel.State.Phase = DuelPhase.Battle; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0, CardId = twin.InstanceId, TargetId = enemy.InstanceId }).Accepted);
        for (int i = 0; i < 8 && duel.State.BattleStep != BattleStep.DamageEnd; i++) PassTwice(duel);
        Assert.Equal(DuelZone.Monster, twin.Zone);
        Assert.Equal(DecisionKind.YesNo, duel.State.PendingDecision?.Kind);
        Answer(duel, "yes"); PassTwice(duel);
        Assert.Equal(DuelZone.Banished, enemy.Zone);
    }

    [Fact]
    public void Kaiju_removes_two_field_counters_before_negating_and_banishing_the_opponents_spell()
    {
        var duel = Create(new[] { "70368879", "55063751" });
        var spell = duel.State.Cards.Single(c => c.DefinitionId == "70368879");
        var kaiju = duel.State.Cards.Single(c => c.DefinitionId == "55063751");
        kaiju.Zone = DuelZone.Monster; kaiju.Position = CardPosition.FaceUpAttack; kaiju.Controller = 1;
        kaiju.Counters.Add("kaiju", 2);
        Activate(duel, spell, "70368879.1");
        Activate(duel, kaiju, "55063751.4", 0, kaiju.InstanceId);
        Assert.Equal(0, kaiju.Counters["kaiju"]);
        PassTwice(duel);
        Assert.Equal(DuelZone.Banished, spell.Zone);
        Assert.Equal(3, duel.State.Cards.Count(c => c.Owner == 0 && c.Zone == DuelZone.Hand));
        Assert.Equal(8000, duel.State.Players[1].LifePoints);
    }

    [Fact]
    public void Kaiju_tributes_an_immune_enemy_without_chain_then_summons_a_second_kaiju_to_its_owners_field()
    {
        var duel = Create(new[] { "55063751", "55063751" });
        var kaijus = duel.State.Cards.Where(c => c.DefinitionId == "55063751").ToArray();
        var enemy = duel.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
        enemy.Zone = DuelZone.Monster; enemy.Position = CardPosition.FaceUpAttack;
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.Unaffected, Target = enemy.Ref });
        var summon = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0, CardId = kaijus[0].InstanceId,
            TargetId = enemy.InstanceId, AbilityId = "55063751.1", Slot = 0 });
        Assert.True(summon.Accepted, summon.Error);
        Assert.Equal(DuelZone.Graveyard, enemy.Zone);
        Assert.Equal(1, kaijus[0].Controller);
        Assert.Equal(0, Assert.Single(summon.Events.Where(e => e.Kind == DuelEventKind.Summoned)).Player);
        Assert.Empty(duel.State.Chain);
        PassTwice(duel);
        var own = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0, CardId = kaijus[1].InstanceId,
            AbilityId = "55063751.2", Slot = 0 });
        Assert.True(own.Accepted, own.Error);
        Assert.Equal(0, kaijus[1].Controller);
        Assert.Empty(duel.State.Chain);
    }

    [Fact]
    public void Crystal_wing_negates_a_hand_monster_effect_without_destroying_its_already_paid_graveyard_cost()
    {
        var duel = Create(new[] { "08240199", "97268402" }, extra: new[] { "50954680" });
        var sage = duel.State.Cards.Single(c => c.DefinitionId == "08240199");
        var target = duel.State.Cards.Single(c => c.DefinitionId == "97268402");
        var crystal = duel.State.Cards.Single(c => c.DefinitionId == "50954680");
        target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack; target.Slot = 0;
        crystal.Zone = DuelZone.Monster; crystal.Position = CardPosition.FaceUpAttack; crystal.Controller = 1;
        Activate(duel, sage, "08240199.2", target.InstanceId);
        Activate(duel, crystal, "50954680.1");
        PassTwice(duel);
        Assert.Null(duel.State.PendingDecision);
        Assert.Equal(DuelZone.Graveyard, sage.Zone); // Sage is already in the graveyard after paying its cost; it cannot be destroyed again.
        Assert.Equal(DuelZone.Monster, target.Zone);
        Assert.Equal(3000, crystal.CurrentAtk);
    }

    [Fact]
    public void Crystal_wing_counter_destroys_a_field_effect_source_and_gains_its_printed_attack()
    {
        var duel = Create(new[] { "38517737" }, extra: new[] { "50954680" });
        var alternative = duel.State.Cards.Single(c => c.DefinitionId == "38517737");
        var crystal = duel.State.Cards.Single(c => c.DefinitionId == "50954680");
        alternative.Zone = crystal.Zone = DuelZone.Monster;
        alternative.Position = crystal.Position = CardPosition.FaceUpAttack;
        crystal.Controller = 1;
        Activate(duel, alternative, "38517737.2", crystal.InstanceId);
        Activate(duel, crystal, "50954680.1"); PassTwice(duel);
        Assert.Equal(DuelZone.Graveyard, alternative.Zone);
        Assert.Equal(6000, crystal.CurrentAtk);
    }

    [Fact]
    public void Crystal_wing_calculation_bonus_applies_only_to_the_current_high_level_battle()
    {
        var duel = Create(Array.Empty<string>(), extra: new[] { "50954680" });
        var crystal = duel.State.Cards.Single(c => c.DefinitionId == "50954680");
        var enemy = duel.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
        crystal.Zone = enemy.Zone = DuelZone.Monster; crystal.Position = enemy.Position = CardPosition.FaceUpAttack;
        duel.State.Turn = 2; duel.State.Phase = DuelPhase.Battle; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0,
            CardId = crystal.InstanceId, TargetId = enemy.InstanceId }).Accepted);
        while (duel.State.BattleStep != BattleStep.Calculation) PassTwice(duel);
        Assert.Equal("50954680.2", Assert.Single(duel.State.Chain).AbilityId);
        PassTwice(duel);
        Assert.Equal(6000, crystal.CurrentAtk);
        PassTwice(duel);
        Assert.Equal(5000, duel.State.Players[1].LifePoints);
        Assert.Equal(3000, crystal.CurrentAtk);
    }

    [Fact]
    public void Moonlight_dragon_mandatory_special_summon_trigger_returns_an_enemy_special_summoned_monster()
    {
        var duel = Create(new[] { "97268402", "23434538", "27780618" }, extra: new[] { "33698022" });
        var materials = duel.State.Cards.Where(c => c.Owner == 0 && new[] { "97268402", "23434538", "27780618" }.Contains(c.DefinitionId)).ToArray();
        for (int i = 0; i < materials.Length; i++) { materials[i].Zone = DuelZone.Monster; materials[i].Position = CardPosition.FaceUpAttack; materials[i].Slot = i; }
        var enemy = duel.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
        enemy.Zone = DuelZone.Monster; enemy.Position = CardPosition.FaceUpAttack; enemy.SummonMethod = SummonMethod.Effect; enemy.SummonedTurn = 1;
        var moonlight = duel.State.Cards.Single(c => c.DefinitionId == "33698022");
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = moonlight.InstanceId, Cards = materials.Select(c => c.InstanceId).ToArray(), Slot = 0 });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(DecisionKind.ChooseCards, duel.State.PendingDecision?.Kind);
        Answer(duel, enemy.InstanceId.ToString()); PassTwice(duel);
        Assert.Equal(DuelZone.Hand, enemy.Zone);
        Assert.Equal(0, moonlight.Controller);
    }

    [Fact]
    public void Azure_eyes_own_standby_targets_and_revives_a_normal_graveyard_monster()
    {
        var duel = Create(Array.Empty<string>(), extra: new[] { "40908371" });
        var azure = duel.State.Cards.Single(c => c.DefinitionId == "40908371");
        var white = duel.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand);
        azure.Zone = DuelZone.Monster; azure.Position = CardPosition.FaceUpAttack; azure.Slot = 0;
        white.Zone = DuelZone.Graveyard; white.Position = CardPosition.FaceUp;
        for (int i = 0; i < 40 && !(duel.State.Turn == 3 && duel.State.Phase == DuelPhase.Standby); i++)
            Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
        Assert.Equal(DecisionKind.YesNo, duel.State.PendingDecision?.Kind);
        Answer(duel, "yes"); Answer(duel, white.InstanceId.ToString()); PassTwice(duel);
        Answer(duel, "1"); Answer(duel, "attack");
        Assert.Equal(DuelZone.Monster, white.Zone);
        Assert.Equal(1, white.Slot);
    }

    [Fact]
    public void Azure_eyes_synchro_summon_protects_the_dragons_present_until_the_next_turn_ends()
    {
        var duel = Create(new[] { "97268402" }, extra: new[] { "40908371" });
        var tuner = duel.State.Cards.Single(c => c.DefinitionId == "97268402");
        var whites = duel.State.Cards.Where(c => c.Owner == 0 && c.Zone == DuelZone.Hand && c.DefinitionId == "89631139").Take(2).ToArray();
        tuner.Zone = whites[0].Zone = whites[1].Zone = DuelZone.Monster;
        tuner.Position = whites[0].Position = whites[1].Position = CardPosition.FaceUpAttack;
        tuner.Slot = 0; whites[0].Slot = 1; whites[1].Slot = 2;
        var azure = duel.State.Cards.Single(c => c.DefinitionId == "40908371");
        var summon = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = azure.InstanceId, Cards = new[] { tuner.InstanceId, whites[0].InstanceId }, Slot = 0 });
        Assert.True(summon.Accepted, summon.Error);
        Assert.Equal("40908371.1", Assert.Single(duel.State.Chain).AbilityId);
        PassTwice(duel);
        foreach (var dragon in new[] { azure, whites[1] })
        {
            Assert.Contains(duel.State.Effects, e => e.Kind == EffectRecordKind.EffectIndestructible && e.Target.Equals(dragon.Ref) && e.ExpiresTurn == 2);
            Assert.Contains(duel.State.Effects, e => e.Kind == EffectRecordKind.Untargetable && e.Target.Equals(dragon.Ref) && e.ExpiresTurn == 2);
        }
    }

    [Fact]
    public void Alternative_white_dragon_destroys_an_enemy_and_cannot_use_that_effect_after_attacking()
    {
        var duel = Create(new[] { "38517737" });
        var alternative = duel.State.Cards.Single(c => c.DefinitionId == "38517737");
        var enemy = duel.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
        alternative.Zone = DuelZone.Monster; alternative.Position = CardPosition.FaceUpAttack;
        enemy.Zone = DuelZone.Monster; enemy.Position = CardPosition.FaceUpAttack;
        alternative.AttacksThisTurn = 1;
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = alternative.InstanceId,
            AbilityId = "38517737.2", TargetId = enemy.InstanceId }).Accepted);
        alternative.AttacksThisTurn = 0;
        Activate(duel, alternative, "38517737.2", enemy.InstanceId); PassTwice(duel);
        Assert.Equal(DuelZone.Graveyard, enemy.Zone);
        Assert.Contains(duel.State.Effects, e => e.Kind == EffectRecordKind.CannotAttack && e.Target.Equals(alternative.Ref));
    }

    [Fact]
    public void Alternative_white_dragon_reveals_a_hand_white_dragon_without_chain_and_shares_procedure_limit()
    {
        var duel = Create(new[] { "38517737", "38517737" });
        var alternatives = duel.State.Cards.Where(c => c.DefinitionId == "38517737").ToArray();
        var white = duel.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand && c.DefinitionId == "89631139");
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = alternatives[0].InstanceId, AbilityId = "38517737.summon", Slot = 0, Cards = new[] { white.InstanceId } });
        Assert.True(result.Accepted, result.Error);
        Assert.Empty(duel.State.Chain);
        Assert.Equal(DuelZone.Hand, white.Zone);
        Assert.Contains(result.Events, e => e.Kind == DuelEventKind.Revealed && e.DefinitionId == "89631139" && e.VisibleToMask == 3);
        Assert.True(alternatives[0].ProperlySummoned);
        Assert.Equal("89631139", alternatives[0].CurrentNameId);
        PassTwice(duel);
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.Kind == DuelCommandKind.SpecialSummon && a.Card.Equals(alternatives[1].Ref));
    }

    [Fact]
    public void Ancient_stone_sent_as_cost_waits_until_end_phase_to_summon_a_blue_eyes_from_deck()
    {
        var duel = Create(new[] { "39701395", "71039903" });
        var spell = duel.State.Cards.Single(c => c.DefinitionId == "39701395");
        var stone = duel.State.Cards.Single(c => c.DefinitionId == "71039903");
        Activate(duel, spell, "39701395.1", 0, stone.InstanceId); PassTwice(duel);
        Assert.Null(duel.State.PendingDecision);
        PassTwice(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
        Assert.Equal(DecisionKind.YesNo, duel.State.PendingDecision?.Kind);
        Answer(duel, "yes"); PassTwice(duel);
        Answer(duel, duel.State.PendingDecision!.Options.First().Id);
        Answer(duel, "0"); Answer(duel, "attack");
        Assert.Contains(duel.State.Cards, c => c.Controller == 0 && c.Zone == DuelZone.Monster && c.DefinitionId == "89631139");
    }

    [Fact]
    public void Ancient_stone_banishes_itself_as_cost_and_recovers_a_targeted_graveyard_blue_eyes()
    {
        var duel = Create(new[] { "71039903" });
        var stone = duel.State.Cards.Single(c => c.DefinitionId == "71039903");
        var blue = duel.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand && c.DefinitionId == "89631139");
        stone.Zone = DuelZone.Graveyard; stone.Position = CardPosition.FaceUp;
        blue.Zone = DuelZone.Graveyard; blue.Position = CardPosition.FaceUp;
        Activate(duel, stone, "71039903.2", blue.InstanceId);
        Assert.Equal(DuelZone.Banished, stone.Zone);
        PassTwice(duel);
        Assert.Equal(DuelZone.Hand, blue.Zone);
        Assert.Null(duel.State.PendingDecision);
    }

    [Fact]
    public void Spirit_of_white_tributes_itself_as_cost_and_summons_a_hand_blue_eyes_white_dragon()
    {
        var duel = Create(new[] { "45467446" });
        var spirit = duel.State.Cards.Single(c => c.DefinitionId == "45467446");
        var opponent = duel.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
        spirit.Zone = DuelZone.Monster; spirit.Position = CardPosition.FaceUpAttack;
        opponent.Zone = DuelZone.Monster; opponent.Position = CardPosition.FaceUpAttack;
        Activate(duel, spirit, "45467446.3");
        Assert.Equal(DuelZone.Graveyard, spirit.Zone);
        Assert.True(spirit.CurrentNormal);
        PassTwice(duel);
        Answer(duel, duel.State.PendingDecision!.Options.First().Id);
        Answer(duel, "0"); Answer(duel, "defense");
        Assert.Contains(duel.State.Cards, c => c.Controller == 0 && c.Zone == DuelZone.Monster
            && c.CurrentNameId == "89631139" && c.Position == CardPosition.FaceUpDefense);
    }

    [Fact]
    public void Spirit_of_white_optional_summon_trigger_banishes_an_opponents_set_spell_or_trap()
    {
        var duel = Create(new[] { "06853254", "45467446" }, new[] { "71587526" });
        var source = duel.State.Cards.Single(c => c.DefinitionId == "06853254");
        var spirit = duel.State.Cards.Single(c => c.DefinitionId == "45467446");
        var trap = duel.State.Cards.Single(c => c.DefinitionId == "71587526");
        spirit.Zone = DuelZone.Graveyard; spirit.Position = CardPosition.FaceUp;
        trap.Zone = DuelZone.SpellTrap; trap.Position = CardPosition.FaceDown;
        Activate(duel, source, "06853254.1", spirit.InstanceId); PassTwice(duel);
        Answer(duel, "0"); Answer(duel, "attack");
        Assert.Equal(DecisionKind.YesNo, duel.State.PendingDecision?.Kind);
        Answer(duel, "yes");
        Answer(duel, trap.InstanceId.ToString());
        PassTwice(duel);
        Assert.Equal(DuelZone.Banished, trap.Zone);
    }

    [Fact]
    public void Spirit_of_white_is_a_normal_monster_in_graveyard_for_dragon_shrines_second_send()
    {
        var duel = Create(new[] { "41620959", "89631139", "89631139", "89631139", "89631139", "45467446" });
        var shrine = duel.State.Cards.Single(c => c.DefinitionId == "41620959");
        var spirit = duel.State.Cards.Single(c => c.DefinitionId == "45467446");
        Activate(duel, shrine, "41620959.1"); PassTwice(duel);
        Answer(duel, duel.State.PendingDecision!.Options.Single(o => o.Card.InstanceId == spirit.InstanceId).Id);
        Assert.Equal(DuelZone.Graveyard, spirit.Zone);
        Assert.Equal(DecisionKind.ChooseCards, duel.State.PendingDecision?.Kind);
        Assert.Equal(0, duel.State.PendingDecision!.Min);
    }

    [Fact]
    public void Sages_hand_effect_pays_its_discard_then_sends_the_effect_target_and_summons_blue_eyes()
    {
        var duel = Create(new[] { "08240199", "97268402" });
        var sage = duel.State.Cards.Single(c => c.DefinitionId == "08240199");
        var target = duel.State.Cards.Single(c => c.DefinitionId == "97268402");
        target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack;
        Activate(duel, sage, "08240199.2", target.InstanceId);
        Assert.Equal(DuelZone.Graveyard, sage.Zone);
        Assert.Equal(DuelZone.Monster, target.Zone);
        PassTwice(duel);
        Assert.Equal(DuelZone.Graveyard, target.Zone);
        Assert.Equal(DecisionKind.ChooseCards, duel.State.PendingDecision?.Kind);
        Answer(duel, duel.State.PendingDecision!.Options.First().Id);
        Answer(duel, "0"); Answer(duel, "attack");
        Assert.Contains(duel.State.Cards, c => c.Controller == 0 && c.Zone == DuelZone.Monster && c.DefinitionId == "89631139");
    }

    static void Activate(DuelEngine duel, DuelCardState source, string ability, int target = 0, params int[] costs)
    {
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = source.Controller,
            CardId = source.InstanceId, AbilityId = ability, TargetId = target, Cards = costs });
        Assert.True(result.Accepted, result.Error);
    }
    static void PassTwice(DuelEngine duel)
    {
        for (int i = 0; i < 2; i++) Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass,
            Player = duel.State.WaitingSeat }).Accepted);
    }
    static void Answer(DuelEngine duel, params string[] choices) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Answer, Player = duel.State.PendingDecision!.Player,
        DecisionId = duel.State.PendingDecision.Id, Options = choices }).Accepted);
    static DuelEngine Create(string[] opening, string[]? opponentOpening = null, params string[] extra)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { opening.Concat(Enumerable.Repeat("89631139", 40 - opening.Length)).ToArray(),
                (opponentOpening ?? Array.Empty<string>()).Concat(Enumerable.Repeat("89631139", 40 - (opponentOpening?.Length ?? 0))).ToArray() },
            ExtraDecks = new[] { extra, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) PassTwice(duel);
        return duel;
    }
}
