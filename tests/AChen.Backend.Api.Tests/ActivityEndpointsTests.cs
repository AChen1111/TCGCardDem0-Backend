using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AChen.Configuration;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.Activities;
using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Features.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AChen.Backend.Api.Tests;

public sealed class ActivityEndpointsTests
{
    static readonly JsonSerializerOptions Json = LatestContentService.Json;
    [Fact]
    public async Task Retry_after_end_returns_original_settlement_and_new_request_cannot_claim()
    {
        using var s = await Scenario.Create();
        var d = s.Gift("once"); d.ScheduleMode = ActivityScheduleMode.Timed; d.StartsAt = s.Clock.Now.AddDays(-1); d.EndsAt = s.Clock.Now.AddMinutes(1);
        await s.Publish(d);
        var request = s.Claim(d.Id, "reward", 0);
        var first = await s.SendClaim(d.Id, request);
        Assert.Equal(200, first.Player.Gold);
        s.Clock.Now = s.Clock.Now.AddHours(1);
        var retry = await s.SendClaim(d.Id, request);
        Assert.Equal(first.Player.Revision, retry.Player.Revision);
        Assert.Equal(200, (await s.Player()).Gold);
        request.RequestId = Guid.NewGuid().ToString(); request.ExpectedRevision = first.Player.Revision;
        await Error(await s.Post(d.Id + "/claim", request), "ACTIVITY_ENDED");
    }

    [Fact]
    public async Task Business_limit_and_request_payload_identity_are_independent_of_request_id()
    {
        using var s = await Scenario.Create(); await s.Publish(s.Gift("limit"));
        var request = s.Claim("limit", "reward", 0); var result = await s.SendClaim("limit", request);
        request.ExpectedRevision = result.Player.Revision;
        await Error(await s.Post("limit/claim", request), "REQUEST_ID_CONFLICT");
        request.RequestId = Guid.NewGuid().ToString();
        await Error(await s.Post("limit/claim", request), "LIMIT_REACHED");
        Assert.Equal(200, (await s.Player()).Gold);
    }

    [Fact]
    public async Task Concurrent_devices_settle_one_claim()
    {
        using var s = await Scenario.Create(); await s.Publish(s.Gift("concurrent"));
        var responses = await Task.WhenAll(s.Post("concurrent/claim", s.Claim("concurrent", "reward", 0)), s.Post("concurrent/claim", s.Claim("concurrent", "reward", 0)));
        Assert.Single(responses.Where(x => x.IsSuccessStatusCode));
        Assert.Equal(200, (await s.Player()).Gold);
        await using var scope = s.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Set<PlayerActivityClaim>().CountAsync());
    }

    [Fact]
    public async Task Daily_reset_uses_beijing_midnight_and_rejects_previous_period()
    {
        using var s = await Scenario.Create(); s.Clock.Now = DateTimeOffset.Parse("2026-10-01T23:59:59+08:00");
        var d = s.Gift("daily"); d.Entries[0].PeriodKind = ActivityPeriodKind.Daily; d.Entries[0].TotalLimit = null; await s.Publish(d);
        var request = s.Claim("daily", "reward", 0, "2026-10-01"); await s.SendClaim("daily", request);
        s.Clock.Now = s.Clock.Now.AddSeconds(1);
        var states = await s.List(); Assert.Equal("2026-10-02", states.ServerDay); Assert.True(states.Activities.Single().PlayerState.EntryStates.Single().CanClaim);
        request.RequestId = Guid.NewGuid().ToString(); request.ExpectedRevision = 1;
        await Error(await s.Post("daily/claim", request), "PERIOD_CHANGED");
        request.PeriodKey = "2026-10-02"; request.RequestId = Guid.NewGuid().ToString();
        Assert.Equal(400, (await s.SendClaim("daily", request)).Player.Gold);
    }

    [Fact]
    public async Task Sign_in_counts_distinct_lobby_days_and_unlocked_rewards_can_be_caught_up()
    {
        using var s = await Scenario.Create(); var d = s.Gift("login"); d.Type = ActivityType.SignIn; d.RequiredDays = 3;
        d.Entries = Enumerable.Range(1,3).Select(i => new ActivityEntryDefinition { Id="day_"+i, GiftId="gold", DayIndex=i, TotalLimit=1 }).ToList();
        await s.Publish(d);
        await s.Post("visit", new { }); await s.Post("visit", new { });
        Assert.Equal(1, (await s.List()).Activities.Single().PlayerState.Progress);
        s.Clock.Now = s.Clock.Now.AddDays(2); await s.Post("visit", new { });
        var states = await s.List(); Assert.Equal(2, states.Activities.Single().PlayerState.Progress);
        Assert.False(states.Activities.Single().PlayerState.EntryStates.Single(x => x.EntryId == "day_3").CanClaim);
        await s.SendClaim("login", s.Claim("login", "day_1", 0));
        Assert.Equal(400, (await s.SendClaim("login", s.Claim("login", "day_2", 1))).Player.Gold);
    }

    [Fact]
    public async Task Exchange_deducts_cost_grants_published_card_and_freezes_reward_identity()
    {
        using var s = await Scenario.Create(); var card = s.Config.AllCards.First().CardId;
        await s.SaveGift(new ActivityGiftDefinition { Id="card", NameKey="gift.card.name", Rewards=[new ActivityReward { RewardType=ActivityRewardType.Card,RewardId=card,Amount=1,CardVariant=0 }] });
        var d = s.Gift("exchange"); d.Type = ActivityType.Exchange; d.Entries[0].GiftId="card"; d.Entries[0].NameKey="exchange.daily_card.name"; d.Entries[0].CostGold=800;
        await s.Publish(d); await s.GrantGold(1000);
        var result = await s.SendClaim("exchange", s.Claim("exchange", "reward", 1), true);
        Assert.Equal(200, result.Player.Gold); Assert.Contains(result.Player.OwnedCards, x => x.CardId == card && x.Count == 1);
        d.Entries[0].CostGold=1; var draft = await s.SaveDraft(d, 2);
        await Error(await s.Admin.PostAsJsonAsync("/api/admin/activities/exchange/publish", new ActivityVersionRequest(draft.Revision), Json), "ACTIVITY_FROZEN");
        Assert.Equal(200, (await s.Player()).Gold);
    }

    [Fact]
    public async Task Draft_updates_do_not_leak_and_disable_reenable_keeps_claim_history()
    {
        using var s = await Scenario.Create(); var d = s.Gift("availability"); await s.Publish(d);
        await s.SendClaim(d.Id, s.Claim(d.Id,"reward",0));
        d.SortOrder=999; var draft=await s.SaveDraft(d,2);
        Assert.Equal(0,(await s.List()).Activities.Single().Definition.SortOrder);
        var disabled=await s.Read<ActivityDefinitionRecord>(await s.Admin.PostAsJsonAsync("/api/admin/activities/availability/disable",new ActivityVersionRequest(draft.Revision),Json));
        Assert.Empty((await s.List()).Activities);
        await s.Read<ActivityDefinitionRecord>(await s.Admin.PostAsJsonAsync("/api/admin/activities/availability/publish",new ActivityVersionRequest(disabled.Revision),Json));
        var state=(await s.List()).Activities.Single(); Assert.True(state.PlayerState.Completed); Assert.Equal(999,state.Definition.SortOrder);
    }

    [Fact]
    public async Task Publish_requires_bilingual_keys_and_reward_popup_has_page_entry()
    {
        using var s=await Scenario.Create(); var d=s.Gift("invalid"); d.NameKey="activity.unpublished.name";
        var draft=await s.SaveDraft(d,0);
        await Error(await s.Admin.PostAsJsonAsync("/api/admin/activities/invalid/publish",new ActivityVersionRequest(draft.Revision),Json),"INVALID_NAME_KEY");
        d.NameKey="activity.daily_gold.name"; d.DisplayMode=ActivityDisplayMode.Popup; draft=await s.SaveDraft(d,draft.Revision);
        await Error(await s.Admin.PostAsJsonAsync("/api/admin/activities/invalid/publish",new ActivityVersionRequest(draft.Revision),Json),"INVALID_ACTIVITY_DISPLAY");
    }

    [Fact]
    public async Task Popup_receipt_suppresses_only_that_policy_and_server_day()
    {
        using var s=await Scenario.Create(); var d=s.Gift("popup"); d.DisplayMode=ActivityDisplayMode.PageAndPopup;
        d.Popup.Frequency="oncePerDay"; await s.Publish(d);
        Assert.True((await s.List()).Activities.Single().Definition.Popup.ShouldShow);
        var request=new ActivityPopupShownRequest {PolicyVersion=1,PeriodKey=ActivityService.Day(s.Clock.Now)};
        (await s.Post("popup/popup-shown",request)).EnsureSuccessStatusCode();
        (await s.Post("popup/popup-shown",request)).EnsureSuccessStatusCode();
        Assert.False((await s.List()).Activities.Single().Definition.Popup.ShouldShow);
        s.Clock.Now=s.Clock.Now.AddDays(1); Assert.True((await s.List()).Activities.Single().Definition.Popup.ShouldShow);
    }

    [Fact]
    public async Task Successful_draw_updates_milestone_inside_inventory_settlement()
    {
        using var s=await Scenario.Create(); var pack=s.Config.Catalog.CardPacks.First(x => x.IsEnabled);
        var d=s.Gift("draws"); d.Type=ActivityType.Milestone; d.PackIds=[pack.Id]; d.Entries[0].Threshold=1; await s.Publish(d); await s.GrantGold(100000);
        var response=await s.Client.PostAsJsonAsync("/api/player/card-draws",new {packId=pack.Id,poolKey=pack.PoolKey,count=1,expectedRevision=1}); await s.Read<JsonElement>(response);
        var state=(await s.List()).Activities.Single(); Assert.Equal(1,state.PlayerState.Progress); Assert.True(state.PlayerState.EntryStates.Single().CanClaim);
        var failed=await s.Client.PostAsJsonAsync("/api/player/card-draws",new {packId=pack.Id,poolKey=pack.PoolKey,count=1,expectedRevision=1}); Assert.False(failed.IsSuccessStatusCode);
        Assert.Equal(1,(await s.List()).Activities.Single().PlayerState.Progress);
    }

    [Fact]
    public async Task Insufficient_gold_leaves_inventory_and_activity_counters_unchanged()
    {
        using var s=await Scenario.Create(); var d=s.Gift("poor_exchange"); d.Type=ActivityType.Exchange; d.Entries[0].NameKey="exchange.daily_card.name"; d.Entries[0].CostGold=800;
        await s.Publish(d); await Error(await s.Post("poor_exchange/exchange",s.Claim(d.Id,"reward",0)),"INSUFFICIENT_GOLD");
        var player=await s.Player(); Assert.Equal(0,player.Gold); Assert.Equal(0,player.Revision);
        await using var scope=s.Factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0,await db.Set<PlayerActivityClaim>().CountAsync()); Assert.Equal(0,await db.Set<PlayerActivityCounter>().CountAsync());
    }
    [Fact]
    public async Task Activity_dependencies_cannot_form_cycle()
    {
        using var s=await Scenario.Create(); var first=s.Gift("first"); await s.Publish(first);
        var second=s.Gift("second"); second.OpenConditions.AllOf.Add(new ActivityCondition {Type="activityCompleted",ActivityId="first"}); await s.Publish(second);
        first.OpenConditions.AllOf.Add(new ActivityCondition {Type="activityCompleted",ActivityId="second"}); var draft=await s.SaveDraft(first,2);
        await Error(await s.Admin.PostAsJsonAsync("/api/admin/activities/first/publish",new ActivityVersionRequest(draft.Revision),Json),"INVALID_ACTIVITY_CONDITION");
    }
    [Fact]
    public async Task New_content_hash_and_removed_name_keys_block_incompatible_activity()
    {
        using var s=await Scenario.Create(); await s.Publish(s.Gift("compatible"));
        var files=PublishedConfigFixture.SourceFiles(); var table=BinaryTable.Decode(files["Translations"]);
        int key=table.Column("Key","string"); table.Rows=table.Rows.Where(x=>(string)x[key]!="gift.gold_200.name").ToArray(); files["Translations"]=table.Encode();
        var manifest=await s.Read<DevelopmentManifest>(await PublishedConfigFixture.Upload(s.Admin,"Editor",PublishedConfigFixture.Package("Editor",files)));
        await Error(await s.Client.GetAsync("/api/activities/"),"CONTENT_CHANGED");
        s.Client.DefaultRequestHeaders.Remove("X-Config-Hash"); s.Client.DefaultRequestHeaders.Add("X-Config-Hash",manifest.configHash);
        Assert.Empty((await s.List()).Activities);
    }
    [Fact]
    public async Task Plan_example_drafts_publish_as_complete_supported_snapshots()
    {
        using var s=await Scenario.Create(); var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null && !File.Exists(Path.Combine(directory.FullName,"AdminWeb/public/activity-examples.json"))) directory=directory.Parent;
        var examples=JsonSerializer.Deserialize<Examples>(File.ReadAllText(Path.Combine(directory!.FullName,"AdminWeb/public/activity-examples.json")),Json)!;
        foreach(var gift in examples.Gifts) await s.SaveGift(gift);
        foreach(var definition in examples.Activities) await s.Publish(definition);
        var snapshot=await s.List(); Assert.Equal(6,snapshot.Activities.Count);
        Assert.Equal(3,snapshot.Activities.Single(x=>x.Definition.Type==ActivityType.SignIn).Definition.Entries.Count);
    }
    sealed class Examples {public ActivityGiftDefinition[] Gifts=[];public ActivityDefinition[] Activities=[];}
    static async Task Error(HttpResponseMessage response,string code)
    { Assert.False(response.IsSuccessStatusCode); var body=await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(code,body.GetProperty("code").GetString()); }
    sealed class MutableClock : TimeProvider { public DateTimeOffset Now=DateTimeOffset.Parse("2026-10-01T12:00:00+08:00"); public override DateTimeOffset GetUtcNow()=>Now; }
    sealed class Scenario : IDisposable
    {
        public ApiFactory Factory=null!; public HttpClient Client=null!,Admin=null!; public MutableClock Clock=new();
        public PublishedGameConfig Config=null!; public string Hash="";
        public static async Task<Scenario> Create()
        {
            var s=new Scenario(); s.Factory=new ApiFactory {Clock=s.Clock}; s.Client=s.Factory.CreateClient(); s.Admin=s.Factory.CreateClient();
            s.Admin.DefaultRequestHeaders.Add("X-Content-Publish-Key",ApiFactory.PublishKey);
            var files=PublishedConfigFixture.SourceFiles(); s.Config=GameConfigTables.Assemble(files);
            var uploaded=await PublishedConfigFixture.Upload(s.Admin,"Editor",PublishedConfigFixture.Package("Editor",files));
            var manifest=await s.Read<DevelopmentManifest>(uploaded); s.Hash=manifest.configHash;
            s.Client.DefaultRequestHeaders.Add("X-Content-Target","Editor"); s.Client.DefaultRequestHeaders.Add("X-Config-Hash",s.Hash);
            s.Client.DefaultRequestHeaders.Add("X-Activity-Schema","1"); s.Client.DefaultRequestHeaders.Add("X-Activity-Types","0,1,2,3,4");
            var registered=await s.Client.PostAsJsonAsync("/api/auth/register",new {username="Activity"+Guid.NewGuid().ToString("N")[..12],password="correct-horse-42"});
            registered.EnsureSuccessStatusCode(); var auth=await registered.Content.ReadFromJsonAsync<JsonElement>();
            s.Client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",auth.GetProperty("accessToken").GetString());
            await s.SaveGift(new ActivityGiftDefinition {Id="gold",NameKey="gift.gold_200.name",Rewards=[new ActivityReward {RewardType=ActivityRewardType.Gold,Amount=200}]}); return s;
        }
        public ActivityDefinition Gift(string id)=>new() {Id=id,NameKey="activity.daily_gold.name",Type=ActivityType.Gift,Entries=[new ActivityEntryDefinition {Id="reward",GiftId="gold",TotalLimit=1}]};
        public async Task SaveGift(ActivityGiftDefinition gift)=>(await Admin.PutAsJsonAsync("/api/admin/activities/gifts/"+gift.Id,new ActivitySaveGiftRequest(gift,0),Json)).EnsureSuccessStatusCode();
        public async Task<ActivityDefinitionRecord> SaveDraft(ActivityDefinition d,long revision)=>await Read<ActivityDefinitionRecord>(await Admin.PutAsJsonAsync("/api/admin/activities/"+d.Id,new ActivityDraftRequest {Target="Editor",ConfigHash=Hash,ExpectedVersion=revision,Definition=d},Json));
        public async Task Publish(ActivityDefinition d) { var row=await SaveDraft(d,0); await Read<ActivityDefinitionRecord>(await Admin.PostAsJsonAsync("/api/admin/activities/"+d.Id+"/publish",new ActivityVersionRequest(row.Revision),Json)); }
        public ActivityClaimRequest Claim(string id,string e,long rev,string period="all")=>new() {EntryId=e,ExpectedRevision=rev,DefinitionVersion=1,RequestId=Guid.NewGuid().ToString(),PeriodKey=period};
        public Task<HttpResponseMessage> Post(string path,object body)=>Client.PostAsJsonAsync("/api/activities/"+path,body,Json);
        public async Task<ActivityClaimResponse> SendClaim(string id,ActivityClaimRequest request,bool exchange=false)=>await Read<ActivityClaimResponse>(await Post(id+(exchange?"/exchange":"/claim"),request));
        public async Task<ActivityListResponse> List()=>await Client.GetFromJsonAsync<ActivityListResponse>("/api/activities/",Json) ?? throw new Exception("Empty snapshot");
        public async Task<PlayerResponse> Player()=>await Client.GetFromJsonAsync<PlayerResponse>("/api/player/bootstrap",Json) ?? throw new Exception("Empty player");
        public async Task<T> Read<T>(HttpResponseMessage response) { if(!response.IsSuccessStatusCode) throw new Exception(await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
        public async Task GrantGold(long amount) { var player=await Player(); await using var scope=Factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<AppDbContext>(); var profile=await db.PlayerProfiles.SingleAsync(x=>x.UserId==player.Id); profile.Gold=amount; profile.Revision++; await db.SaveChangesAsync(); }
        public void Dispose() {Client.Dispose();Admin.Dispose();Factory.Dispose();}
    }
}
