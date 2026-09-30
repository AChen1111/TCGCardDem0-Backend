using System.IO.Compression;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publishing_loading_visiting_and_gold_claims_are_independent_of_ordinary_content(bool legacyPlatform)
    {
        using var s = await Scenario.Create(publishOrdinary: false);
        string legacyManifest = "";
        if (legacyPlatform)
        {
            // 模拟旧平台记录：缺少头像框及其他普通配置，活动流程不得读取它。
            legacyManifest = JsonSerializer.Serialize(new DevelopmentManifest { platform = "StandaloneWindows64", configs = [] }, Json);
            await using var scope = s.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(new CurrentContent { Target = "StandaloneWindows64", ContentId = Guid.NewGuid().ToString("D"), ManifestJson = legacyManifest });
            await db.SaveChangesAsync();
            s.Client.DefaultRequestHeaders.Add("X-Content-Target", "StandaloneWindows64");
            s.Client.DefaultRequestHeaders.Add("X-Config-Hash", "stale-ordinary-config");
        }
        await s.Publish();
        var index = await s.List(); Assert.Equal(6, index.Activities.Count);
        var response = await s.Post("visit", new {}); response.EnsureSuccessStatusCode();
        response = await s.Post("notice_national_day_2026/popup-shown", new ActivityPopupShownRequest
            { PolicyVersion = index.Activities.Single(x => x.Master.Type == 0).Master.PopupPolicyVersion, PeriodKey = "all" });
        response.EnsureSuccessStatusCode();
        var reward = await s.SendClaim("daily_gold", s.Claim("daily_gold", "daily", 0, ActivityService.Day(s.Clock.Now)));
        Assert.Equal(200, reward.Player.Gold);
        var file = index.Files.First(); response = await s.Client.GetAsync(file.Url); response.EnsureSuccessStatusCode();
        await using var check = s.Factory.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var ordinary = await database.Set<CurrentContent>().ToListAsync();
        if (legacyPlatform) Assert.Equal(legacyManifest, Assert.Single(ordinary).ManifestJson);
        else Assert.Empty(ordinary);
        var manifest = (await database.Set<ActivityReleaseRecord>().SingleAsync()).ManifestJson;
        Assert.DoesNotContain("referenceTarget", manifest); Assert.DoesNotContain("referenceConfigHash", manifest);
    }
    [Fact] public async Task Ordinary_resource_references_do_not_gate_activity_publication_or_index()
    {
        using var s = await Scenario.Create();
        var rows = s.Masters();
        rows[0].NameKey = "text_not_in_ordinary_config";
        rows[0].BannerResourceKey = "image_not_in_ordinary_config";
        s.SetMaster(rows);
        var exchange = GameConfigTables.Map<ActivityExchangeRow>(BinaryTable.Decode(s.Files["daily_card_exchange"]));
        exchange[0].RewardIds = ["card_not_in_ordinary_config"];
        s.Files["daily_card_exchange"] = GameConfigTables.FromRows(exchange).Encode();
        await s.Publish();
        var index = await s.List(); Assert.Equal(rows[0].NameKey, index.Activities[0].Master.NameKey);
    }
    [Fact] public async Task Index_contains_master_and_hashes_without_detail_body_and_all_samples_download()
    {
        using var s = await Scenario.Create(); await s.Publish();
        var index = await s.List(); Assert.Equal(2, index.SchemaVersion); Assert.Equal(6, index.Activities.Count);
        var body = await s.Client.GetStringAsync("/api/activities/");
        Assert.DoesNotContain("rewardTypes", body); Assert.DoesNotContain("costGold", body);
        foreach (var file in index.Files)
        {
            var response = await s.Client.GetAsync(file.Url); response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(); Assert.Equal(file.Sha256, ActivityCsvConfiguration.Hash(bytes)); Assert.Equal(file.Size, bytes.LongLength);
        }
        await using var scope = s.Factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(ActivityDefinitionRecord))!.GetProperties(), x => x.Name.Contains("Draft") || x.Name.Contains("DefinitionJson"));
        Assert.DoesNotContain("Reward", (await db.Set<ActivityReleaseRecord>().SingleAsync()).MasterJson);
    }
    [Fact] public async Task Hash_failure_and_stale_revision_leave_current_release_unchanged()
    {
        using var s = await Scenario.Create(); await s.Publish(); var before = await s.List();
        var manifest = s.Manifest(); manifest.Files[1].Sha256 = new string('0', 64);
        await Error(await s.Upload(manifest), "INVALID_ACTIVITY_PACKAGE");
        manifest = s.Manifest(); manifest.ExpectedRevision = 0;
        await Error(await s.Upload(manifest), "ACTIVITY_VERSION_CHANGED");
        var after = await s.List(); Assert.Equal(before.ReleaseId, after.ReleaseId); Assert.Equal(before.DefinitionsRevision, after.DefinitionsRevision);
    }
    [Fact] public async Task Invalid_reference_and_reward_arrays_report_table_and_field()
    {
        using var s = await Scenario.Create(); var rows = s.Masters(); rows[0].DetailTable = "missing"; s.SetMaster(rows);
        var response = await s.Upload(s.Manifest()); await Error(response, "INVALID_ACTIVITY_PACKAGE"); Assert.Contains("DetailTable", await response.Content.ReadAsStringAsync());
        s.ResetFiles(); var detail = GameConfigTables.Map<ActivityGiftRow>(BinaryTable.Decode(s.Files["daily_gold"])); detail[0].Amounts = [100,200];
        s.Files["daily_gold"] = GameConfigTables.FromRows(detail).Encode(); response = await s.Upload(s.Manifest()); await Error(response, "INVALID_ACTIVITY_PACKAGE");
        var text = await response.Content.ReadAsStringAsync(); Assert.Contains("daily_gold.csv", text); Assert.Contains("Amounts", text);
    }
    [Fact] public async Task Rules_are_fixed_on_first_publish_even_without_player_records()
    {
        using var s = await Scenario.Create(); await s.Publish();
        var rows = GameConfigTables.Map<ActivityGiftRow>(BinaryTable.Decode(s.Files["daily_gold"])); rows[0].LimitPerPeriod = 2;
        s.Files["daily_gold"] = GameConfigTables.FromRows(rows).Encode(); var response = await s.Upload(s.Manifest()); await Error(response, "INVALID_ACTIVITY_PACKAGE");
        Assert.Contains("LimitPerPeriod", await response.Content.ReadAsStringAsync()); Assert.Equal(1, (await s.List()).DefinitionsRevision);
    }
    [Fact] public async Task Reward_update_preserves_counters_and_old_request_result_before_version_check()
    {
        using var s = await Scenario.Create(); await s.Publish();
        var request = s.Claim("daily_gold", "daily", 0, ActivityService.Day(s.Clock.Now)); var first = await s.SendClaim("daily_gold", request); Assert.Equal(200, first.Player.Gold);
        var rows = GameConfigTables.Map<ActivityGiftRow>(BinaryTable.Decode(s.Files["daily_gold"])); rows[0].Amounts = [500];
        s.Files["daily_gold"] = GameConfigTables.FromRows(rows).Encode(); await s.Publish();
        var retry = await s.SendClaim("daily_gold", request); Assert.Equal(first.Player.Revision, retry.Player.Revision); Assert.Equal(200, retry.GoldGained);
        var index = await s.List(); var daily = index.Activities.Single(x => x.Master.ActivityId == "daily_gold");
        Assert.Equal(2, daily.DefinitionVersion); Assert.Equal(1, daily.PlayerState.EntryStates.Single().ClaimedCount);
        Assert.Equal(1, index.Activities.Single(x => x.Master.ActivityId == "daily_card_exchange").DefinitionVersion);
        s.Clock.Now = s.Clock.Now.AddDays(1); request = s.Claim("daily_gold", "daily", 1, ActivityService.Day(s.Clock.Now)); request.DefinitionVersion = 2;
        Assert.Equal(700, (await s.SendClaim("daily_gold", request)).Player.Gold);
    }
    [Fact] public async Task Exchange_cost_update_keeps_period_count_and_uses_new_price_next_day()
    {
        using var s = await Scenario.Create(); await s.Publish(); await s.GrantGold(2000);
        var request = s.Claim("daily_card_exchange", "exchange_offer_001", 1, ActivityService.Day(s.Clock.Now));
        var first = await s.SendClaim("daily_card_exchange", request, true); Assert.Equal(1200, first.Player.Gold);
        var rows = GameConfigTables.Map<ActivityExchangeRow>(BinaryTable.Decode(s.Files["daily_card_exchange"])); rows[0].CostGold = 100;
        s.Files["daily_card_exchange"] = GameConfigTables.FromRows(rows).Encode(); await s.Publish();
        var state = (await s.List()).Activities.Single(x => x.Master.ActivityId == "daily_card_exchange"); Assert.False(state.PlayerState.EntryStates.Single().CanClaim);
        s.Clock.Now = s.Clock.Now.AddDays(1); request = s.Claim("daily_card_exchange", "exchange_offer_001", first.Player.Revision, ActivityService.Day(s.Clock.Now)); request.DefinitionVersion = 2;
        Assert.Equal(1100, (await s.SendClaim("daily_card_exchange", request, true)).Player.Gold);
    }
    [Fact] public async Task Retry_after_end_uses_original_settlement_but_new_request_is_rejected()
    {
        using var s = await Scenario.Create(); await s.Publish(); var request = s.Claim("national_day_gift_2026", "welcome", 0);
        var first = await s.SendClaim("national_day_gift_2026", request); s.Clock.Now = s.Clock.Now.AddDays(8);
        Assert.Equal(first.Player.Revision, (await s.SendClaim("national_day_gift_2026", request)).Player.Revision);
        request.RequestId = Guid.NewGuid().ToString(); request.ExpectedRevision = first.Player.Revision;
        await Error(await s.Post("national_day_gift_2026/claim", request), "ACTIVITY_ENDED");
    }
    [Fact] public async Task Concurrent_devices_settle_once_and_payload_conflicts_are_rejected()
    {
        using var s = await Scenario.Create(); await s.Publish();
        var a = s.Claim("national_day_gift_2026", "welcome", 0); var b = s.Claim("national_day_gift_2026", "welcome", 0);
        var results = await Task.WhenAll(s.Post("national_day_gift_2026/claim", a), s.Post("national_day_gift_2026/claim", b)); Assert.Single(results.Where(x => x.IsSuccessStatusCode));
        Assert.Equal(1000, (await s.Player()).Gold);
        var winner = results[0].IsSuccessStatusCode ? a : b; winner.ExpectedRevision = 99;
        await Error(await s.Post("national_day_gift_2026/claim", winner), "REQUEST_ID_CONFLICT");
    }
    [Fact] public async Task Sign_in_only_visit_counts_days_and_catch_up_rewards_remain_available()
    {
        using var s = await Scenario.Create(); await s.Publish();
        Assert.Equal(0, (await s.List()).Activities.Single(x => x.Master.Type == 2).PlayerState.Progress);
        await s.Post("visit", new {}); await s.Post("visit", new {});
        Assert.Equal(1, (await s.List()).Activities.Single(x => x.Master.Type == 2).PlayerState.Progress);
        s.Clock.Now = s.Clock.Now.AddDays(2); await s.Post("visit", new {});
        var sign = (await s.List()).Activities.Single(x => x.Master.Type == 2); Assert.Equal(2, sign.PlayerState.Progress);
        Assert.False(sign.PlayerState.EntryStates.Single(x => x.EntryId == "day_3").CanClaim);
        await s.SendClaim(sign.Master.ActivityId, s.Claim(sign.Master.ActivityId, "day_1", 0));
        Assert.Equal(300, (await s.SendClaim(sign.Master.ActivityId, s.Claim(sign.Master.ActivityId, "day_2", 1))).Player.Gold);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_popups_ignore_receipts_and_claims_without_resetting_reward_limits(bool stopWhenCompleted)
    {
        using var s = await Scenario.Create(publishOrdinary: false);
        var rows = s.Masters();
        var gift = rows.Single(x => x.ActivityId == "national_day_gift_2026");
        gift.PopupFrequency = "oncePerLogin";
        gift.PopupTrigger = "rewardClaimable";
        gift.StopWhenCompleted = stopWhenCompleted;
        s.SetMaster(rows); await s.Publish();
        var receipt = new ActivityPopupShownRequest { PolicyVersion = gift.PopupPolicyVersion, PeriodKey = "all" };
        Assert.True((await s.List()).Activities.Single(x => x.Master.ActivityId == gift.ActivityId).ShouldShow);
        var displayed = await s.Read<ActivityIndexResponse>(await s.Post(gift.ActivityId + "/popup-shown", receipt));
        Assert.True(displayed.Activities.Single(x => x.Master.ActivityId == gift.ActivityId).ShouldShow);
        var claimed = await s.SendClaim(gift.ActivityId, s.Claim(gift.ActivityId, "welcome", 0));
        var state = claimed.Activities.Activities.Single(x => x.Master.ActivityId == gift.ActivityId);
        Assert.True(state.PlayerState.Completed);
        Assert.False(state.PlayerState.EntryStates.Single().CanClaim);
        Assert.Equal(1, state.PlayerState.EntryStates.Single().TotalClaimedCount);
        Assert.True(state.ShouldShow);
        var displayedAgain = await s.Read<ActivityIndexResponse>(await s.Post(gift.ActivityId + "/popup-shown", receipt));
        Assert.True(displayedAgain.Activities.Single(x => x.Master.ActivityId == gift.ActivityId).ShouldShow);
        await Error(await s.Post(gift.ActivityId + "/claim", s.Claim(gift.ActivityId, "welcome", claimed.Player.Revision)), "LIMIT_REACHED");
        Assert.Equal(1000, (await s.Player()).Gold);
    }
    [Fact] public async Task Switching_completed_activity_to_login_popups_keeps_claims_and_old_idempotent_results()
    {
        using var s = await Scenario.Create(publishOrdinary: false);
        var rows = s.Masters(); var gift = rows.Single(x => x.ActivityId == "national_day_gift_2026");
        gift.PopupFrequency = "oncePerActivity"; gift.StopWhenCompleted = true;
        s.SetMaster(rows); await s.Publish();
        var receipt = await s.Post(gift.ActivityId + "/popup-shown", new ActivityPopupShownRequest { PolicyVersion = gift.PopupPolicyVersion, PeriodKey = "all" });
        receipt.EnsureSuccessStatusCode();
        var original = s.Claim(gift.ActivityId, "welcome", 0);
        var claimed = await s.SendClaim(gift.ActivityId, original);
        Assert.False(claimed.Activities.Activities.Single(x => x.Master.ActivityId == gift.ActivityId).ShouldShow);
        gift.PopupFrequency = "oncePerLogin"; gift.StopWhenCompleted = false; gift.PopupPolicyVersion++;
        s.SetMaster(rows); await s.Publish();
        var state = (await s.List()).Activities.Single(x => x.Master.ActivityId == gift.ActivityId);
        Assert.True(state.ShouldShow); Assert.True(state.PlayerState.Completed);
        Assert.Equal(1, state.PlayerState.EntryStates.Single().TotalClaimedCount);
        Assert.Equal(2, state.DefinitionVersion);
        var repeated = await s.SendClaim(gift.ActivityId, original);
        Assert.Equal(JsonSerializer.Serialize(claimed, Json), JsonSerializer.Serialize(repeated, Json));
        var newClaim = s.Claim(gift.ActivityId, "welcome", claimed.Player.Revision);
        newClaim.DefinitionVersion = state.DefinitionVersion;
        await Error(await s.Post(gift.ActivityId + "/claim", newClaim), "LIMIT_REACHED");
        Assert.Equal(1000, (await s.Player()).Gold);
    }
    [Theory]
    [InlineData("oncePerActivity")]
    [InlineData("oncePerDay")]
    public async Task Explicit_activity_and_daily_popup_policies_keep_their_receipt_limits(string frequency)
    {
        using var s = await Scenario.Create(publishOrdinary: false);
        var rows = s.Masters(); var notice = rows.Single(x => x.Type == 0);
        notice.PopupFrequency = frequency; s.SetMaster(rows); await s.Publish();
        Assert.True((await s.List()).Activities.Single(x => x.Master.Type == 0).ShouldShow);
        var period = frequency == "oncePerDay" ? ActivityService.Day(s.Clock.Now) : "all";
        var response = await s.Post(notice.ActivityId + "/popup-shown", new ActivityPopupShownRequest { PolicyVersion = notice.PopupPolicyVersion, PeriodKey = period });
        response.EnsureSuccessStatusCode();
        Assert.False((await s.List()).Activities.Single(x => x.Master.Type == 0).ShouldShow);
        s.Clock.Now = s.Clock.Now.AddDays(1);
        Assert.Equal(frequency == "oncePerDay", (await s.List()).Activities.Single(x => x.Master.Type == 0).ShouldShow);
    }
    [Fact] public async Task Successful_draws_keep_accumulating_milestone_progress()
    {
        using var s = await Scenario.Create(); await s.Publish(); await s.GrantGold(20000);
        int pack = GameConfigTables.Map<ActivityMilestoneRow>(BinaryTable.Decode(s.Files["draw_ten_reward_2026"]))[0].PackIds[0];
        var draw = await s.Client.PostAsJsonAsync("/api/player/card-draws", new {packId=pack,poolKey=GameConfigTables.Assemble(PublishedConfigFixture.SourceFiles()).Catalog.CardPacks.Single(x=>x.Id==pack).PoolKey,count=10,expectedRevision=1});
        draw.EnsureSuccessStatusCode(); Assert.Equal(10, (await s.List()).Activities.Single(x => x.Master.Type == 4).PlayerState.Progress);
    }
    [Fact] public async Task Disabled_future_and_zero_activity_packages_have_valid_index()
    {
        using var s = await Scenario.Create(); var rows = s.Masters(); rows[0].StartsAt = s.Clock.Now.AddDays(2); rows[0].EndsAt = s.Clock.Now.AddDays(3); rows[1].IsEnabled = false; s.SetMaster(rows); await s.Publish();
        var index = await s.List(); Assert.Equal("upcoming", index.Activities[0].Status); Assert.Equal("disabled", index.Activities[1].Status);
        Assert.False(index.Activities[0].Master.IsOpen(s.Clock.Now)); Assert.False(index.Activities[1].Master.IsOpen(s.Clock.Now));
        Assert.False(index.Activities[0].ShouldShow); Assert.False(index.Activities[1].ShouldShow);
        s.Files.Clear(); s.SetMaster([]); await s.Publish(); Assert.Empty((await s.List()).Activities);
    }
    [Fact] public async Task Old_schema_and_manual_edit_routes_are_unavailable()
    {
        using var s = await Scenario.Create(); s.Client.DefaultRequestHeaders.Remove("X-Activity-Schema"); s.Client.DefaultRequestHeaders.Add("X-Activity-Schema", "1");
        await Error(await s.Client.GetAsync("/api/activities/"), "ACTIVITY_SCHEMA_CHANGED");
        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, (await s.Admin.PostAsJsonAsync("/api/admin/activities/",new{})).StatusCode);
    }
    static async Task Error(HttpResponseMessage response,string code)
    { Assert.False(response.IsSuccessStatusCode); var body=await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(code,body.GetProperty("code").GetString()); }
    sealed class MutableClock : TimeProvider { public DateTimeOffset Now=DateTimeOffset.Parse("2026-10-01T12:00:00+08:00"); public override DateTimeOffset GetUtcNow()=>Now; }
    sealed class Scenario : IDisposable
    {
        public ApiFactory Factory=null!; public HttpClient Client=null!,Admin=null!; public MutableClock Clock=new();
        public Dictionary<string,byte[]> Files = new(); public string Hash=""; public long Revision;
        static string SourceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName,"Assets/ActivityConfiguration"))) directory = directory.Parent;
            return Path.Combine(directory!.FullName,"Assets/ActivityConfiguration");
        }
        public void ResetFiles() => Files = Directory.GetFiles(SourceRoot(),"*.bytes").ToDictionary(Path.GetFileNameWithoutExtension,File.ReadAllBytes)!;
        public ActivityMasterRow[] Masters() => ActivityCsvConfiguration.Master(Files["activities"]);
        public void SetMaster(ActivityMasterRow[] rows) => Files["activities"] = GameConfigTables.FromRows(rows).Encode();
        public static async Task<Scenario> Create(bool publishOrdinary = true)
        {
            var s=new Scenario(); s.ResetFiles(); s.Factory=new ApiFactory {Clock=s.Clock}; s.Client=s.Factory.CreateClient(); s.Admin=s.Factory.CreateClient();
            s.Admin.DefaultRequestHeaders.Add("X-Content-Publish-Key",ApiFactory.PublishKey);
            if (publishOrdinary)
            {
                var uploaded=await PublishedConfigFixture.Upload(s.Admin,"Editor",PublishedConfigFixture.Package("Editor",PublishedConfigFixture.SourceFiles()));
                var manifest=await s.Read<DevelopmentManifest>(uploaded); s.Hash=manifest.configHash;
                s.Client.DefaultRequestHeaders.Add("X-Content-Target","Editor"); s.Client.DefaultRequestHeaders.Add("X-Config-Hash",s.Hash);
            }
            s.Client.DefaultRequestHeaders.Add("X-Activity-Schema","2");
            var registered=await s.Client.PostAsJsonAsync("/api/auth/register",new {username="Activity"+Guid.NewGuid().ToString("N")[..12],password="correct-horse-42"});
            registered.EnsureSuccessStatusCode(); var auth=await registered.Content.ReadFromJsonAsync<JsonElement>();
            s.Client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",auth.GetProperty("accessToken").GetString()); return s;
        }
        public ActivityPackageManifest Manifest() => new() {ReleaseId=Guid.NewGuid().ToString("D"),ExpectedRevision=Revision,
            Files=Files.Select(x=>new ActivityFileInfo {Table=x.Key,Size=x.Value.Length,Sha256=ActivityCsvConfiguration.Hash(x.Value)}).ToList()};
        public async Task<HttpResponseMessage> Upload(ActivityPackageManifest manifest)
        {
            using var memory=new MemoryStream();
            using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
            {
                foreach(var file in Files) {using var stream=zip.CreateEntry(file.Key+".bytes").Open(); stream.Write(file.Value);}
                using var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open()); writer.Write(JsonSerializer.Serialize(manifest,Json));
            }
            using var content=new ByteArrayContent(memory.ToArray()); content.Headers.ContentType=new MediaTypeHeaderValue("application/zip");
            return await Admin.PutAsync("/api/admin/activities/config",content);
        }
        public async Task Publish() { var result=await Read<ActivityReleaseRecord>(await Upload(Manifest())); Revision=result.Revision; }
        public ActivityClaimRequest Claim(string id,string entry,long revision,string period="all")=>new() {EntryId=entry,ExpectedRevision=revision,DefinitionVersion=1,RequestId=Guid.NewGuid().ToString(),PeriodKey=period};
        public Task<HttpResponseMessage> Post(string path,object body)=>Client.PostAsJsonAsync("/api/activities/"+path,body,Json);
        public async Task<ActivityClaimResponse> SendClaim(string id,ActivityClaimRequest request,bool exchange=false)=>await Read<ActivityClaimResponse>(await Post(id+(exchange?"/exchange":"/claim"),request));
        public async Task<ActivityIndexResponse> List()=>await Client.GetFromJsonAsync<ActivityIndexResponse>("/api/activities/",Json) ?? throw new Exception("Empty index");
        public async Task<PlayerResponse> Player()=>await Client.GetFromJsonAsync<PlayerResponse>("/api/player/bootstrap",Json) ?? throw new Exception("Empty player");
        public async Task<T> Read<T>(HttpResponseMessage response) { if(!response.IsSuccessStatusCode) throw new Exception(await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
        public async Task GrantGold(long amount) {var player=await Player();await using var scope=Factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var profile=await db.PlayerProfiles.SingleAsync(x=>x.UserId==player.Id);profile.Gold=amount;profile.Revision++;await db.SaveChangesAsync();}
        public void Dispose() {Client.Dispose();Admin.Dispose();Factory.Dispose();}
    }
}
