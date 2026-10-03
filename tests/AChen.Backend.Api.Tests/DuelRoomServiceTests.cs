using AChen.Backend.Api.Features.Duels;
using AChen.Backend.Api.Infrastructure;
using AChen.Duel.Core;

namespace AChen.Backend.Api.Tests;

public sealed class DuelRoomServiceTests
{
    [Fact]
    public void Returning_and_starting_a_new_match_preserves_the_previous_private_replay_in_the_room_history()
    {
        var (rooms, room, players) = StartedRoom(TimeProvider.System);
        var view = rooms.Get(players[0], room.Id).Duel!;
        rooms.Submit(players[0], room.Id, new DuelInputRequest(Guid.NewGuid(), new SeatInput { Revision = view.Revision,
            ActionToken = view.Actions.Single(a => a.Kind == DuelCommandKind.Surrender).ActionToken }));
        rooms.ReturnToRoom(players[0], room.Id);
        foreach (var player in players) rooms.SetReady(player, room.Id, true);
        var history = rooms.CaptureReplayHistory(room.Id);
        Assert.Equal(DuelCommandKind.Surrender, Assert.Single(Assert.Single(history).Entries).Command.Kind);
        Assert.True(DuelReplayRunner.Run(DuelCardCatalog.CreateDefault(), history[0]).Verified);
        Assert.Empty(rooms.CaptureReplay(room.Id).Entries);
    }

    [Fact]
    public void Declaration_name_pages_are_bound_to_own_action_and_never_search_the_opponents_deck()
    {
        var cards = DuelCardCatalog.CreateDefault();
        var rules = CardRuleCatalog.CreateDefault(cards);
        var opening = new[] { "65681983", "89631139", "89631139", "89631139", "48800175" };
        var filler = cards.Cards.Where(c => !c.IsExtra && rules.Get(c.CardId).Support.IsComplete && c.CardId != "49299410" && c.CardId != "48800175")
            .SelectMany(c => Enumerable.Repeat(c.CardId, Math.Max(0, c.MaxCopies - opening.Count(id => cards.Get(id).OriginalNameId == c.OriginalNameId))))
            .Take(33).Prepend("48800175").Prepend("48800175").ToArray();
        var ownDeck = opening.Concat(filler).ToArray();
        Assert.Equal(40, ownDeck.Length);
        var opponent = ownDeck.Where(id => id != "65681983").Prepend("49299410").ToArray();
        var service = new DuelRoomService(TimeProvider.System, new DuelRoomCardSupport(rules), new FixedRoomStart());
        var players = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var room = service.Create(players[0]); service.Join(players[1], room.Code);
        service.SubmitDeck(players[0], room.Id, new DuelDeckRequest(ownDeck, Array.Empty<string>()));
        service.SubmitDeck(players[1], room.Id, new DuelDeckRequest(opponent, Array.Empty<string>()));
        foreach (var player in players) { service.Connect(player, room.Id); service.SetReady(player, room.Id, true); }
        for (int i = 0; i < 4; i++) Pass(service, room.Id, players);
        var view = service.Get(players[0], room.Id).Duel!;
        var declaration = view.Actions.Single(a => a.AbilityId == "65681983.1");
        var names = service.QueryNames(players[0], room.Id, declaration.ActionToken, "", 0);
        Assert.Contains(names.Items, name => name.NameId == "48800175");
        Assert.DoesNotContain(names.Items, name => name.NameId == "49299410");
        Assert.DoesNotContain(names.Items, name => name.NameId == "89631139");
        Assert.Equal("STALE_OR_INVALID_ACTION", Assert.Throws<ApiException>(() =>
            service.QueryNames(players[1], room.Id, declaration.ActionToken, "", 0)).Code);
    }

    sealed class FixedRoomStart : IDuelRoomStartSource
    {
        public DuelStartRecord Create(string[][] mainDecks, string[][] extraDecks) => new()
        { MainDecks = mainDecks, ExtraDecks = extraDecks, Shuffle = false, FirstPlayer = 0, Seed = 73 };
    }

    [Fact]
    public void A_connected_player_can_surrender_while_the_opponent_disconnect_has_paused_choices()
    {
        var (rooms, room, players) = StartedRoom(TimeProvider.System);
        var guestConnection = rooms.Connect(players[1], room.Id);
        rooms.Disconnect(players[1], room.Id, guestConnection.Id);
        var own = rooms.Get(players[0], room.Id).Duel!;
        var ended = rooms.Submit(players[0], room.Id, new DuelInputRequest(Guid.NewGuid(), new SeatInput
        { Revision = own.Revision, ActionToken = own.Actions.Single(x => x.Kind == DuelCommandKind.Surrender).ActionToken }));
        Assert.True(ended.Result!.Accepted);
        Assert.Equal("SURRENDER", ended.Room.Duel!.EndReason);
    }

    [Fact]
    public void A_departed_seat_cannot_leave_an_old_result_again_or_release_its_new_membership()
    {
        var (rooms, oldRoom, players) = StartedRoom(TimeProvider.System);
        rooms.Leave(players[0], oldRoom.Id);
        var nextRoom = rooms.Create(players[0]);
        Assert.Equal("ROOM_FORBIDDEN", Assert.Throws<ApiException>(() => rooms.Leave(players[0], oldRoom.Id)).Code);
        Assert.Equal(nextRoom.Id, rooms.Create(players[0]).Id);
    }

    [Fact]
    public async Task A_slow_receiver_is_disconnected_and_reconnects_with_only_latest_snapshot()
    {
        var clock = new RoomClock();
        var (rooms, room, players) = StartedRoom(clock);
        var connection = rooms.Connect(players[0], room.Id);
        for (var i = 0; i < 64; i++) rooms.Ping(players[0], room.Id, connection.Id);
        Assert.True(rooms.Get(players[1], room.Id).Paused);
        var replacement = rooms.Connect(players[0], room.Id);
        var latest = await replacement.Updates.ReadAsync();
        Assert.False(latest.Room.Paused);
        Assert.Equal(rooms.Get(players[0], room.Id).Sequence, latest.Room.Sequence);
        Assert.False(replacement.Updates.TryRead(out _));
    }

    [Fact]
    public void Idle_waiting_room_expires_after_thirty_minutes_and_releases_account_membership()
    {
        var clock = new RoomClock();
        var rooms = DuelRoomFixtures.Create(clock);
        var player = Guid.NewGuid();
        var room = rooms.Create(player);
        clock.Advance(TimeSpan.FromMinutes(30));
        rooms.Tick();
        Assert.Equal("ROOM_NOT_FOUND", Assert.Throws<ApiException>(() => rooms.Get(player, room.Id)).Code);
        Assert.NotEqual(room.Id, rooms.Create(player).Id);
    }

    [Fact]
    public void Preparation_rejects_an_unimplemented_card_rule_with_specific_card_and_rule_details()
    {
        var rooms = new DuelRoomService(TimeProvider.System, new DuelRoomFixtures.MissingFixtureSupport());
        var player = Guid.NewGuid();
        var room = rooms.Create(player);
        rooms.SubmitDeck(player, room.Id, ValidDeck());
        var error = Assert.Throws<UnsupportedDuelCardsException>(() => rooms.SetReady(player, room.Id, true));
        Assert.Equal("UNSUPPORTED_DUEL_CARDS", error.Code);
        Assert.Equal(new[] { "fixture.missing" }, error.Errors[ValidDeck().MainDeck[0]]);
        Assert.False(rooms.Get(player, room.Id).Players[0].Ready);
    }

    [Fact]
    public void Finished_seats_return_to_same_room_and_prepare_another_frozen_duel()
    {
        var (rooms, room, players) = StartedRoom(TimeProvider.System);
        var waiting = room.Duel!.WaitingSeat;
        var view = rooms.Get(players[waiting], room.Id).Duel!;
        var result = rooms.Submit(players[waiting], room.Id, new DuelInputRequest(Guid.NewGuid(), new SeatInput
        { Revision = view.Revision, ActionToken = view.Actions.Single(x => x.Kind == DuelCommandKind.Surrender).ActionToken }));
        Assert.Equal("Finished", result.Room.Status);
        Assert.Equal(room.Id, rooms.Create(players[1]).Id);

        var returned = rooms.ReturnToRoom(players[0], room.Id);
        Assert.Equal("Waiting", returned.Status);
        Assert.Null(returned.Duel);
        Assert.All(returned.Players, player => Assert.False(player.Ready));
        foreach (var player in players) rooms.SetReady(player, room.Id, true);
        Assert.Equal("Running", rooms.Get(players[1], room.Id).Status);
        Assert.Equal(5, rooms.Get(players[1], room.Id).Duel!.Players[1].HandCount);
    }

    [Fact]
    public void Retrying_an_old_receipt_advances_expired_clock_before_returning_latest_state()
    {
        var clock = new RoomClock();
        var (rooms, room, players) = StartedRoom(clock);
        var seat = room.Duel!.WaitingSeat;
        var view = rooms.Get(players[seat], room.Id).Duel!;
        var input = new DuelInputRequest(Guid.NewGuid(), new SeatInput { Revision = view.Revision,
            ActionToken = view.Actions.Single(x => x.Kind == DuelCommandKind.Pass).ActionToken });
        var original = rooms.Submit(players[seat], room.Id, input);
        clock.Advance(TimeSpan.FromSeconds(181));

        var duplicate = rooms.Submit(players[seat], room.Id, input);

        Assert.Equal(original.Result, duplicate.Result);
        Assert.True(duplicate.Room.Duel!.Finished);
        Assert.Equal("TIMEOUT", duplicate.Room.Duel.EndReason);
    }

    [Fact]
    public void Ready_accounts_start_only_after_both_seats_have_active_connections()
    {
        var rooms = DuelRoomFixtures.Create(TimeProvider.System);
        var players = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var room = rooms.Create(players[0]);
        rooms.Join(players[1], room.Code);
        foreach (var player in players) rooms.SubmitDeck(player, room.Id, ValidDeck());
        foreach (var player in players) rooms.SetReady(player, room.Id, true);
        Assert.Equal("Waiting", rooms.Get(players[0], room.Id).Status);
        rooms.Connect(players[0], room.Id);
        Assert.Equal("Waiting", rooms.Get(players[0], room.Id).Status);
        rooms.Connect(players[1], room.Id);
        Assert.Equal("Running", rooms.Get(players[0], room.Id).Status);
    }

    [Fact]
    public void Authority_replay_contains_player_command_and_clock_outcome_and_verifies_each_step()
    {
        var clock = new RoomClock();
        var (rooms, room, players) = StartedRoom(clock);
        Pass(rooms, room.Id, players);
        clock.Advance(TimeSpan.FromSeconds(181));
        rooms.Tick();

        var replay = rooms.CaptureReplay(room.Id);
        Assert.Equal(new[] { DuelCommandKind.Pass, DuelCommandKind.Timeout }, replay.Entries.Select(x => x.Command.Kind));
        Assert.True(DuelReplayRunner.Run(DuelCardCatalog.CreateDefault(), replay).Verified);
    }

    [Fact]
    public void Room_code_joins_the_second_account_and_preserves_the_creator_seat()
    {
        var rooms = DuelRoomFixtures.Create(TimeProvider.System);
        var host = Guid.NewGuid();
        var guest = Guid.NewGuid();

        var created = rooms.Create(host);
        var joined = rooms.Join(guest, created.Code);

        Assert.Equal(created.Id, joined.Id);
        Assert.Equal(0, rooms.Get(host, created.Id).LocalSeat);
        Assert.Equal(1, joined.LocalSeat);
        Assert.Equal(new[] { host, guest }, joined.Players.Select(player => player.UserId));
        Assert.Equal("Waiting", joined.Status);
    }

    [Fact]
    public void Room_membership_is_required_and_a_third_account_cannot_take_a_seat()
    {
        var rooms = DuelRoomFixtures.Create(TimeProvider.System);
        var host = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var room = rooms.Create(host);
        rooms.Join(guest, room.Code);

        Assert.Equal("ROOM_FORBIDDEN", Assert.Throws<ApiException>(() => rooms.Get(stranger, room.Id)).Code);
        Assert.Equal("ROOM_FULL", Assert.Throws<ApiException>(() => rooms.Join(stranger, room.Code)).Code);
        Assert.Equal(2, rooms.Get(host, room.Id).Players.Count);
    }

    [Fact]
    public void Retried_create_and_join_return_the_existing_membership_without_consuming_a_seat()
    {
        var rooms = DuelRoomFixtures.Create(TimeProvider.System);
        var host = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var room = rooms.Create(host);
        Assert.Equal(room.Id, rooms.Create(host).Id);
        Assert.Equal(0, rooms.Join(host, room.Code).LocalSeat);
        var joined = rooms.Join(guest, room.Code);
        Assert.Equal(joined.Sequence, rooms.Join(guest, room.Code).Sequence);
        Assert.Equal(2, rooms.Get(host, room.Id).Players.Count);
    }

    [Fact]
    public void Guest_can_leave_and_be_replaced_but_host_leaving_closes_the_room()
    {
        var rooms = DuelRoomFixtures.Create(TimeProvider.System);
        var host = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var room = rooms.Create(host);
        rooms.Join(guest, room.Code);
        rooms.Leave(guest, room.Id);
        var replacement = Guid.NewGuid();
        Assert.Equal(1, rooms.Join(replacement, room.Code).LocalSeat);
        rooms.Leave(host, room.Id);
        Assert.Equal("ROOM_NOT_FOUND", Assert.Throws<ApiException>(() => rooms.Join(guest, room.Code)).Code);
        Assert.NotEqual(room.Id, rooms.Create(replacement).Id);
    }

    [Fact]
    public void Submitted_free_deck_is_copied_and_only_its_owner_can_read_the_cards()
    {
        var rooms = DuelRoomFixtures.Create(TimeProvider.System);
        var host = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var room = rooms.Create(host);
        rooms.Join(guest, room.Code);
        var deck = ValidDeck();
        var firstCard = deck.MainDeck[0];

        rooms.SubmitDeck(host, room.Id, deck);
        deck.MainDeck[0] = "changed-after-submit";

        Assert.Equal(firstCard, rooms.Get(host, room.Id).Deck!.MainDeck[0]);
        Assert.Null(rooms.Get(guest, room.Id).Deck);
        Assert.True(rooms.Get(guest, room.Id).Players[0].HasDeck);
    }

    static DuelDeckRequest ValidDeck() => new(DuelCardCatalog.CreateDefault().Cards
        .Where(card => !card.IsExtra).SelectMany(card => Enumerable.Repeat(card.CardId, card.MaxCopies)).Take(40).ToArray(),
        Array.Empty<string>());

    [Theory]
    [InlineData("short")]
    [InlineData("unknown")]
    [InlineData("extra-in-main")]
    [InlineData("banned")]
    [InlineData("copies")]
    public void Invalid_decks_do_not_replace_the_last_valid_submitted_deck(string invalidKind)
    {
        var rooms = DuelRoomFixtures.Create(TimeProvider.System);
        var host = Guid.NewGuid();
        var room = rooms.Create(host);
        var valid = ValidDeck();
        rooms.SubmitDeck(host, room.Id, valid);
        var main = (string[])valid.MainDeck.Clone();
        switch (invalidKind)
        {
            case "short": main = main[..39]; break;
            case "unknown": main[0] = "missing-card"; break;
            case "extra-in-main": main[0] = DuelCardCatalog.CreateDefault().Cards.First(card => card.IsExtra).CardId; break;
            case "banned": main[0] = "23434538"; break;
            case "copies": main[0] = main[1] = main[2] = main[3] = "14558127"; break;
        }

        Assert.Equal("INVALID_DUEL_DECK", Assert.Throws<ApiException>(() =>
            rooms.SubmitDeck(host, room.Id, new DuelDeckRequest(main, Array.Empty<string>()))).Code);
        Assert.Equal(valid.MainDeck, rooms.Get(host, room.Id).Deck!.MainDeck);
    }

    [Fact]
    public void Both_ready_accounts_start_one_duel_from_frozen_free_decks()
    {
        var rooms = DuelRoomFixtures.Create(TimeProvider.System);
        var host = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var room = rooms.Create(host);
        rooms.Join(guest, room.Code);
        Assert.Equal("DECK_REQUIRED", Assert.Throws<ApiException>(() => rooms.SetReady(host, room.Id, true)).Code);
        rooms.SubmitDeck(host, room.Id, ValidDeck());
        rooms.SubmitDeck(guest, room.Id, ValidDeck());
        rooms.Connect(host, room.Id);
        rooms.Connect(guest, room.Id);
        Assert.Equal("Waiting", rooms.SetReady(host, room.Id, true).Status);
        var started = rooms.SetReady(guest, room.Id, true);

        Assert.Equal("Running", started.Status);
        Assert.Equal(5, started.Duel!.Players[0].HandCount);
        Assert.Equal(5, started.Duel.Players[1].HandCount);
        Assert.Equal(5, started.Duel.Cards.Count(card => card.Zone == DuelZone.Hand));
        Assert.All(started.Duel.Cards.Where(card => card.Zone == DuelZone.Hand), card => Assert.Equal(1, card.Owner));
        Assert.Equal("DUEL_ALREADY_STARTED", Assert.Throws<ApiException>(() => rooms.SubmitDeck(host, room.Id, ValidDeck())).Code);
    }

    [Fact]
    public async Task A_replacement_connection_receives_latest_snapshot_and_closes_the_old_connection()
    {
        var rooms = DuelRoomFixtures.Create(TimeProvider.System);
        var host = Guid.NewGuid();
        var room = rooms.Create(host);
        var original = rooms.Connect(host, room.Id);
        Assert.Equal(room.Id, (await original.Updates.ReadAsync()).Room.Id);
        rooms.Join(Guid.NewGuid(), room.Code);
        Assert.Equal(2, (await original.Updates.ReadAsync()).Room.Players.Count);

        var replacement = rooms.Connect(host, room.Id);
        var resumed = await replacement.Updates.ReadAsync();

        Assert.Equal(rooms.Get(host, room.Id).Sequence, resumed.Room.Sequence);
        Assert.Equal(2, resumed.Room.Players.Count);
        Assert.True(original.Updates.Completion.IsCompleted);
        Assert.NotEqual(original.Id, replacement.Id);
    }

    [Fact]
    public void Command_retry_returns_original_receipt_before_checking_stale_action_revision()
    {
        var (rooms, room, players) = StartedRoom(TimeProvider.System);
        var current = rooms.Get(players[0], room.Id).Duel!;
        var user = players[current.WaitingSeat];
        var ownView = rooms.Get(user, room.Id).Duel!;
        var request = new DuelInputRequest(Guid.NewGuid(), new SeatInput
        { Revision = ownView.Revision, ActionToken = ownView.Actions.Single(action => action.Kind == DuelCommandKind.Pass).ActionToken });

        var first = rooms.Submit(user, room.Id, request);
        var duplicate = rooms.Submit(user, room.Id, request);

        Assert.True(first.Result!.Accepted);
        Assert.Equal(first.Result, duplicate.Result);
        Assert.Equal(first.Room.Sequence, duplicate.Room.Sequence);
        Assert.Equal(first.Room.Duel!.Revision, duplicate.Room.Duel!.Revision);
        Assert.Empty(duplicate.Events);
        request.Input.ActionToken = "different-payload";
        Assert.Equal("REQUEST_ID_REUSED", Assert.Throws<ApiException>(() => rooms.Submit(user, room.Id, request)).Code);
    }

    static (DuelRoomService Rooms, DuelRoomView Room, Guid[] Players) StartedRoom(TimeProvider clock)
    {
        var rooms = DuelRoomFixtures.Create(clock);
        var players = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var room = rooms.Create(players[0]);
        rooms.Join(players[1], room.Code);
        foreach (var player in players) rooms.SubmitDeck(player, room.Id, ValidDeck());
        foreach (var player in players) rooms.Connect(player, room.Id);
        foreach (var player in players) rooms.SetReady(player, room.Id, true);
        return (rooms, rooms.Get(players[0], room.Id), players);
    }

    [Fact]
    public void Leaving_a_running_duel_surrenders_without_removing_the_opponent_result()
    {
        var (rooms, room, players) = StartedRoom(TimeProvider.System);
        rooms.Leave(players[0], room.Id);
        var result = rooms.Get(players[1], room.Id);
        Assert.Equal("Finished", result.Status);
        Assert.Equal(1, result.Duel!.Winner);
        Assert.Equal("SURRENDER", result.Duel.EndReason);
        Assert.NotEqual(room.Id, rooms.Create(players[0]).Id);
    }

    [Fact]
    public void Only_the_waiting_seat_spends_the_180_second_turn_budget_and_times_out()
    {
        var clock = new RoomClock();
        var (rooms, room, players) = StartedRoom(clock);
        var waiting = rooms.Get(players[0], room.Id).Duel!.WaitingSeat;
        clock.Advance(TimeSpan.FromSeconds(179));
        rooms.Tick();
        var almost = rooms.Get(players[0], room.Id);
        Assert.Equal(1, almost.RemainingSeconds[waiting]);
        Assert.Equal(180, almost.RemainingSeconds[1 - waiting]);
        Assert.False(almost.Duel!.Finished);

        clock.Advance(TimeSpan.FromSeconds(1));
        rooms.Tick();

        var finished = rooms.Get(players[0], room.Id);
        Assert.Equal("Finished", finished.Status);
        Assert.Equal(1 - waiting, finished.Duel!.Winner);
        Assert.Equal("TIMEOUT", finished.Duel.EndReason);
    }

    [Fact]
    public void A_new_complete_turn_resets_both_budgets_without_resetting_on_each_response_pass()
    {
        var clock = new RoomClock();
        var (rooms, room, players) = StartedRoom(clock);
        clock.Advance(TimeSpan.FromSeconds(30));
        Pass(rooms, room.Id, players);
        Assert.Contains(150, rooms.Get(players[0], room.Id).RemainingSeconds);
        for (var i = 0; i < 20 && rooms.Get(players[0], room.Id).Duel!.Turn == 1; i++)
            Pass(rooms, room.Id, players);
        var nextTurn = rooms.Get(players[0], room.Id);
        Assert.Equal(2, nextTurn.Duel!.Turn);
        Assert.Equal(new double[] { 180, 180 }, nextTurn.RemainingSeconds);
    }

    static void Pass(DuelRoomService rooms, Guid roomId, Guid[] players)
    {
        var waiting = rooms.Get(players[0], roomId).Duel!.WaitingSeat;
        var view = rooms.Get(players[waiting], roomId).Duel!;
        var result = rooms.Submit(players[waiting], roomId, new DuelInputRequest(Guid.NewGuid(), new SeatInput
        { Revision = view.Revision, ActionToken = view.Actions.Single(action => action.Kind == DuelCommandKind.Pass).ActionToken }));
        Assert.True(result.Result!.Accepted);
    }

    [Fact]
    public void Disconnect_pauses_budget_and_reconnect_preserves_it_while_old_disconnect_is_ignored()
    {
        var clock = new RoomClock();
        var (rooms, room, players) = StartedRoom(clock);
        var waiting = room.Duel!.WaitingSeat;
        var user = players[waiting];
        var firstConnection = rooms.Connect(user, room.Id);
        clock.Advance(TimeSpan.FromSeconds(30));
        rooms.Disconnect(user, room.Id, firstConnection.Id);
        Assert.True(rooms.Get(user, room.Id).Paused);

        clock.Advance(TimeSpan.FromSeconds(59));
        var resumed = rooms.Connect(user, room.Id);
        rooms.Disconnect(user, room.Id, firstConnection.Id);
        clock.Advance(TimeSpan.FromSeconds(1));
        var current = rooms.Get(user, room.Id);

        Assert.False(current.Paused);
        Assert.Equal(149, current.RemainingSeconds[waiting]);
        Assert.False(current.Duel!.Finished);
        Assert.NotEqual(firstConnection.Id, resumed.Id);
    }

    [Theory]
    [InlineData(false, 1, "DISCONNECT_TIMEOUT")]
    [InlineData(true, -1, "BOTH_DISCONNECTED")]
    public void Sixty_second_disconnect_deadline_finishes_or_interrupts_the_duel(bool both, int winner, string reason)
    {
        var clock = new RoomClock();
        var (rooms, room, players) = StartedRoom(clock);
        var connections = players.Select(player => rooms.Connect(player, room.Id)).ToArray();
        rooms.Disconnect(players[0], room.Id, connections[0].Id);
        if (both) rooms.Disconnect(players[1], room.Id, connections[1].Id);
        clock.Advance(TimeSpan.FromSeconds(59));
        rooms.Tick();
        Assert.False(rooms.Get(players[0], room.Id).Duel!.Finished);

        clock.Advance(TimeSpan.FromSeconds(1));
        rooms.Tick();

        var finished = rooms.Get(players[0], room.Id);
        Assert.True(finished.Duel!.Finished);
        Assert.Equal(winner, finished.Duel.Winner);
        Assert.Equal(reason, finished.Duel.EndReason);
    }

    [Fact]
    public async Task Hosted_room_clock_publishes_timeout_without_a_client_poll_or_command()
    {
        var clock = new RoomClock();
        var (rooms, room, players) = StartedRoom(clock);
        var connection = rooms.Connect(players[0], room.Id);
        await connection.Updates.ReadAsync();
        using var worker = new DuelRoomTicker(rooms);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            clock.Advance(TimeSpan.FromSeconds(181));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var update = await connection.Updates.ReadAsync(timeout.Token);
            Assert.True(update.Room.Duel!.Finished);
            Assert.Equal("TIMEOUT", update.Room.Duel.EndReason);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public void Reconnect_recovers_an_applied_request_and_does_not_accept_the_replaced_socket()
    {
        var (rooms, room, players) = StartedRoom(TimeProvider.System);
        var seat = room.Duel!.WaitingSeat;
        var user = players[seat];
        var oldConnection = rooms.Connect(user, room.Id);
        var view = rooms.Get(user, room.Id).Duel!;
        var input = new DuelInputRequest(Guid.NewGuid(), new SeatInput
        { Revision = view.Revision, ActionToken = view.Actions.Single(action => action.Kind == DuelCommandKind.Pass).ActionToken });
        var applied = rooms.Submit(user, room.Id, input, oldConnection.Id);
        rooms.Disconnect(user, room.Id, oldConnection.Id);
        var replacement = rooms.Connect(user, room.Id);

        var recovered = rooms.Submit(user, room.Id, input, replacement.Id);

        Assert.Equal(applied.Result, recovered.Result);
        Assert.Equal(applied.Room.Duel!.Revision, recovered.Room.Duel!.Revision);
        Assert.Empty(recovered.Events);
        Assert.Equal("CONNECTION_REPLACED", Assert.Throws<ApiException>(() =>
            rooms.Submit(user, room.Id, input, oldConnection.Id)).Code);
    }

    [Fact]
    public void Another_seat_cannot_use_a_private_action_token_or_advance_the_duel()
    {
        var (rooms, room, players) = StartedRoom(TimeProvider.System);
        var seat = room.Duel!.WaitingSeat;
        var view = rooms.Get(players[seat], room.Id).Duel!;
        var forged = rooms.Submit(players[1 - seat], room.Id, new DuelInputRequest(Guid.NewGuid(), new SeatInput
        { Revision = view.Revision, ActionToken = view.Actions.Single(action => action.Kind == DuelCommandKind.Pass).ActionToken }));
        Assert.False(forged.Result!.Accepted);
        Assert.Equal(view.Revision, forged.Room.Duel!.Revision);
        Assert.Equal(room.Sequence, forged.Room.Sequence);
    }

    sealed class RoomClock : TimeProvider
    {
        DateTimeOffset now = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }
}
