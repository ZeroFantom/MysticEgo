using System.Text.Json.Nodes;

sealed partial class CloudDesk {
    readonly System.Collections.Concurrent.ConcurrentDictionary<string,long> capacityBlocked=new(StringComparer.OrdinalIgnoreCase);
    static bool IsServerCreateError(TimewebApiException e) => e.Method=="POST" && e.Version==1 && e.Path=="servers";
    static bool IsCapacityRejection(TimewebApiException e) => IsServerCreateError(e) &&
        ((e.Status==400 && e.ErrorCode=="servers_is_run_out_error") || (e.Status==409 && e.ErrorCode=="no_free_node"));
    static bool IsLocationRejection(TimewebApiException e) => IsServerCreateError(e) &&
        e.Status==409 && e.ErrorCode=="location_not_available";
    // Only exact, understood error codes from the VPS-create endpoint imply a rejected order.
    static bool IsPresetRejection(TimewebApiException e) => e.Method=="POST" && e.Version==1 && e.Path=="servers" &&
        e.Status==404 && e.ErrorCode=="preset_not_found";
    static bool IsKnownOrderRejection(TimewebApiException e) => IsCapacityRejection(e) || IsPresetRejection(e) || IsLocationRejection(e);
    static string CapacityKey(string zone,string location,string preset,TimewebApiException e) =>
        e.ErrorCode=="no_free_node" ? zone+":"+preset : (location.Length>0?location:zone);
    static long CapacityNow()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    bool IsCapacityBlocked(string zone,string location,string preset="") => new[]{zone,location,zone+":"+preset,"preset:"+preset}.Any(key=>capacityBlocked.TryGetValue(key,out var until)&&until>CapacityNow());
    object[] CapacityBlocks()=>capacityBlocked.Where(kv=>kv.Value>CapacityNow()).OrderBy(kv=>kv.Key).Select(kv=>(object)new {key=kv.Key,until=kv.Value}).ToArray();
    object RejectCapacity(string key,string detail) {
        var until=CapacityNow()+15*60*1000;
        capacityBlocked[key]=until;
        return new {orderState="capacity_rejected",blockKey=key,until,error=Redact(detail)};
    }
    object RejectOrder(string zone,string location,string preset,TimewebApiException e) {
        if(!IsKnownOrderRejection(e)) throw e;
        if(IsLocationRejection(e)) {
            // If an explicit availability zone was requested, exclude that zone, otherwise the preset's location.
            var blockedZone=zone.Length>0?zone:location;
            var expires=CapacityNow()+15*60*1000;
            capacityBlocked[blockedZone]=expires;
            return new {orderState="location_rejected",blockKey=blockedZone,until=expires,error=Redact(e.Message)};
        }
        if(!IsPresetRejection(e)) return RejectCapacity(CapacityKey(zone,location,preset,e),e.Message);
        // The provider rejected the preset ID itself. Hide all catalog copies until cooldown expires.
        var key="preset:"+preset;
        var until=CapacityNow()+15*60*1000;
        capacityBlocked[key]=until;
        return new {orderState="preset_rejected",blockKey=key,until,error=Redact(e.Message)};
    }
    static object SshKeyPayload(string pub) => new {name="MysticEgo CloudDesk",body=pub+" MysticEgo CloudDesk",is_default=false};

    // Preparation cannot submit a VPS order. Only errors in this phase are known safe to retry.
    async Task<object> SubmitPreparedOrder(Func<Task<JsonObject>> prepare, Func<JsonObject,Task<object>> submit) {
        JsonObject body;
        try { body=await prepare(); }
        catch(Exception ex) { return new {orderState="not_sent",error=Redact(ex.Message)}; }
        // Only the submit callback can classify an explicit provider rejection. All other failures propagate.
        return await submit(body);
    }
    async Task<object> OrderSelftest() {
        int passed=0;
        void Test(bool ok) { if(!ok) throw new Exception("Order safety selftest failed"); passed++; }
        var payload=System.Text.Json.JsonSerializer.SerializeToNode(SshKeyPayload("ssh-ed25519 test"))!.AsObject();
        Test(payload["is_default"] is JsonValue value && value.TryGetValue<bool>(out var flag) && !flag);
        Test(S(payload,"name")=="MysticEgo CloudDesk" && S(payload,"body")=="ssh-ed25519 test MysticEgo CloudDesk");
        int calls=0;
        Task<object> Submit(JsonObject body) { calls++; return Task.FromResult<object>(body); }
        var before=await SubmitPreparedOrder(()=>Task.FromException<JsonObject>(new Exception("SSH key rejected")),Submit);
        var result=System.Text.Json.JsonSerializer.SerializeToNode(before)!.AsObject();
        Test(calls==0 && S(result,"orderState")=="not_sent" && S(result,"error")=="SSH key rejected");
        var good=new JsonObject{["name"]="test"};
        Test(ReferenceEquals(await SubmitPreparedOrder(()=>Task.FromResult(good),Submit),good) && calls==1);
        bool unknown=false;
        try { await SubmitPreparedOrder(()=>Task.FromResult(good),_=>{calls++;return Task.FromException<object>(new Exception("Network timeout"));}); }
        catch(Exception e) when(e.Message=="Network timeout") { unknown=true; }
        Test(unknown && calls==2);
        var capacity=new TimewebApiException("POST",1,"servers",400,"servers_is_run_out_error","capacity rejected");
        Test(IsCapacityRejection(capacity));
        Test(!IsCapacityRejection(new TimewebApiException("POST",1,"ssh-keys",400,capacity.ErrorCode,"test")));
        Test(!IsCapacityRejection(new TimewebApiException("POST",1,"servers",500,capacity.ErrorCode,"test")));
        Test(!IsCapacityRejection(new TimewebApiException("POST",1,"servers",400,"bad_request","test")));
        Test(TimewebErrorCode("{\"error_code\":\"servers_is_run_out_error\"}")==capacity.ErrorCode);
        Test(TimewebErrorCode("invalid")=="");
        var rejected=System.Text.Json.JsonSerializer.SerializeToNode(RejectCapacity("selftest-location",capacity.Message))!.AsObject();
        Test(S(rejected,"orderState")=="capacity_rejected" && IsCapacityBlocked("selftest-location",""));
        Test(IsCapacityBlocked("selftest-zone","selftest-location") && !IsCapacityBlocked("other","other"));
        capacityBlocked["selftest-location"]=CapacityNow()-1;
        Test(!IsCapacityBlocked("selftest-location",""));
        capacityBlocked.TryRemove("selftest-location",out _);
        var noNode=new TimewebApiException("POST",1,"servers",409,"no_free_node","No free node");
        Test(IsCapacityRejection(noNode));
        Test(!IsCapacityRejection(new TimewebApiException("POST",1,"servers",409,"conflict","test")));
        Test(!IsCapacityRejection(new TimewebApiException("POST",1,"servers/42/ips",409,"no_free_node","test")));
        Test(!IsCapacityRejection(new TimewebApiException("POST",1,"servers",500,"no_free_node","test")));
        Test(CapacityKey("ru-test","ru-test","123",noNode)=="ru-test:123" && CapacityKey("zone-test","ru-test","123",capacity)=="ru-test");
        var denied=System.Text.Json.JsonSerializer.SerializeToNode(RejectCapacity(CapacityKey("ru-test","ru-test","123",noNode),noNode.Message))!.AsObject();
        Test(S(denied,"blockKey")=="ru-test:123" && S(denied,"orderState")=="capacity_rejected" && IsCapacityBlocked("ru-test","ru-test","123"));
        Test(!IsCapacityBlocked("ru-test","ru-test","124") && !IsCapacityBlocked("other","other","123"));
        capacityBlocked.TryRemove("ru-test:123",out _);
        var missing=new TimewebApiException("POST",1,"servers",404,"preset_not_found","Preset not found");
        Test(IsKnownOrderRejection(missing) && IsPresetRejection(missing));
        Test(IsKnownOrderRejection(capacity) && IsKnownOrderRejection(noNode));
        Test(!IsKnownOrderRejection(new TimewebApiException("POST",1,"servers",404,"not_found","test")));
        Test(!IsKnownOrderRejection(new TimewebApiException("POST",1,"servers",500,"preset_not_found","test")));
        Test(!IsKnownOrderRejection(new TimewebApiException("GET",1,"servers",404,"preset_not_found","test")));
        Test(!IsKnownOrderRejection(new TimewebApiException("POST",1,"servers/42/ips",404,"preset_not_found","test")));
        var unavailable=System.Text.Json.JsonSerializer.SerializeToNode(RejectOrder("ru-test","ru-test","2453",missing))!.AsObject();
        Test(S(unavailable,"orderState")=="preset_rejected" && S(unavailable,"blockKey")=="preset:2453");
        Test(IsCapacityBlocked("ru-test","ru-test","2453") && IsCapacityBlocked("different-zone","different-region","2453"));
        Test(!IsCapacityBlocked("ru-test","ru-test","2454"));
        capacityBlocked["preset:2453"]=CapacityNow()-1;
        Test(!IsCapacityBlocked("ru-test","ru-test","2453"));
        capacityBlocked.TryRemove("preset:2453",out _);
        var closed=new TimewebApiException("POST",1,"servers",409,"location_not_available","Location is not available");
        Test(IsLocationRejection(closed) && IsKnownOrderRejection(closed));
        Test(!IsKnownOrderRejection(new TimewebApiException("POST",1,"servers",500,closed.ErrorCode,"test")));
        Test(!IsKnownOrderRejection(new TimewebApiException("GET",1,"servers",409,closed.ErrorCode,"test")));
        Test(!IsKnownOrderRejection(new TimewebApiException("POST",1,"servers/42/ips",409,closed.ErrorCode,"test")));
        var closedResult=System.Text.Json.JsonSerializer.SerializeToNode(RejectOrder("zone-test","region-test","123",closed))!.AsObject();
        Test(S(closedResult,"orderState")=="location_rejected" && S(closedResult,"blockKey")=="zone-test");
        Test(IsCapacityBlocked("zone-test","region-test","123") && IsCapacityBlocked("zone-test","region-test","124"));
        Test(!IsCapacityBlocked("other-zone","region-test","123"));
        capacityBlocked.TryRemove("zone-test",out _);
        var closedRegion=System.Text.Json.JsonSerializer.SerializeToNode(RejectOrder("region-test","region-test","123",closed))!.AsObject();
        Test(S(closedRegion,"blockKey")=="region-test" && IsCapacityBlocked("other-zone","region-test","124"));
        capacityBlocked.TryRemove("region-test",out _);
        return new {passed};
    }
}
