using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace RPlus.PartnerSdk.Utils;

internal static class JsonUtil
{
  internal static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
  {
    ContractResolver = new CamelCasePropertyNamesContractResolver(),
    DateFormatHandling = DateFormatHandling.IsoDateFormat,
    DateTimeZoneHandling = DateTimeZoneHandling.Utc,
    // Keep nulls included for backward compatibility with existing servers.
    NullValueHandling = NullValueHandling.Include,
  };

  internal static string Serialize(object obj)
  {
    return JsonConvert.SerializeObject(obj, Formatting.None, Settings);
  }
}

