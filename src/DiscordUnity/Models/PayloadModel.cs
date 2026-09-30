using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiscordUnity.Models
{
    internal class PayloadModel : PayloadModel<JToken>
    {
        public PayloadModel<T> As<T>() => new PayloadModel<T> { Op = Op, Data = Data == null ? default(T) : Data.ToObject<T>(DiscordAPI.JsonSerializer) };
    }

    internal class PayloadModel<T>
    {
        public int Op { get; set; }
        [JsonProperty("d", NullValueHandling = NullValueHandling.Include)]
        public T Data { get; set; }
        [JsonProperty("s")]
        public int? Sequence { get; set; }
        [JsonProperty("t")]
        public string Event { get; set; }
    }
}
