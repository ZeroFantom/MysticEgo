using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

sealed partial class CloudDesk {
    // Official GET /api/v2/locations: locations[].location / availability_zones.
    // Do not guess region aliases or create a zone from the catalog's region name.
    static Dictionary<string,HashSet<string>> ReadLocationMap(JsonNode response) {
        if(response["locations"] is not JsonArray rows) throw new Exception("Неизвестная структура Timeweb /api/v2/locations. Заказ остановлен до получения корректного списка локаций.");
        var result=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in rows) {
            if(row is not JsonObject entry || entry["availability_zones"] is not JsonArray zones)
                throw new Exception("Некорректная запись в Timeweb /api/v2/locations.");
            var loc=S(entry,"location");
            if(!Regex.IsMatch(loc,@"^[a-zA-Z0-9_-]+$")) throw new Exception("Некорректный код локации Timeweb.");
            if(!result.TryGetValue(loc,out var known)) result[loc]=known=new(StringComparer.OrdinalIgnoreCase);
            foreach(var zone in zones) {
                if(zone is not JsonValue v || !v.TryGetValue<string>(out var id) || !Regex.IsMatch(id,@"^[a-zA-Z0-9_-]+$")) throw new Exception("Некорректный код зоны Timeweb.");
                known.Add(id);
            }
        }
        return result;
    }
    static bool LocationListed(Dictionary<string,HashSet<string>> map,string location,string explicitZone) =>
        map.TryGetValue(location,out var zones) && (explicitZone.Length==0 || zones.Contains(explicitZone));
    static int LocationSelfTest() {
        var map=ReadLocationMap(JsonNode.Parse("""{"locations":[{"location":"ru-1","location_code":"RU","availability_zones":["spb-1","spb-2"]},{"location":"de-1","location_code":"DE","availability_zones":["fra-1"]}]}""")!);
        int passed=0;
        void Check(bool ok) { if(!ok) throw new Exception("Location catalog regression"); passed++; }
        Check(LocationListed(map,"ru-1",""));
        Check(LocationListed(map,"ru-1","spb-1"));
        Check(LocationListed(map,"ru-1","SPB-2"));
        Check(!LocationListed(map,"ru-1","fra-1"));
        Check(!LocationListed(map,"ru-4",""));
        Check(!LocationListed(map,"","spb-1"));
        Check(!LocationListed(ReadLocationMap(JsonNode.Parse("{\"locations\":[]}")!),"ru-1",""));
        bool malformed=false;
        try { ReadLocationMap(JsonNode.Parse("{\"locations\":[{\"location\":\"ru-1\"}]}")!); } catch(Exception) {malformed=true;}
        Check(malformed);
        return passed;
    }
}
